using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VisionSystem.Contracts;
using VisionSystem.Domain;

namespace VisionSystem.Runtime;

public enum VisionEndpointKind
{
    Production,
    Engineering
}

public sealed class NdjsonVisionServer(
    IPAddress address,
    int port,
    VisionEndpointKind endpoint,
    RuntimeStation station,
    IdempotentInspectionQueue queue) : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly TcpListener _listener = new(address, port);
    private readonly ConcurrentDictionary<int, Task> _clients = new();
    private int _clientSequence;

    public int Port { get; } = port;
    public VisionEndpointKind Endpoint { get; } = endpoint;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _listener.Start();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                client.NoDelay = true;
                var id = Interlocked.Increment(ref _clientSequence);
                var task = HandleClientAsync(client, cancellationToken);
                _clients[id] = task;
                _ = task.ContinueWith(
                    completedTask => _clients.TryRemove(id, out _),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            _listener.Stop();
            await Task.WhenAll(_clients.Values).ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync()
    {
        _listener.Stop();
        return ValueTask.CompletedTask;
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        await using (var stream = client.GetStream())
        {
            var reader = new NdjsonLineReader(stream, VisionProtocol.MaximumLineBytes);
            while (!cancellationToken.IsCancellationRequested)
            {
                string? line;
                try
                {
                    line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (InvalidDataException exception)
                {
                    await WriteAsync(stream, Error(string.Empty, "LINE_TOO_LONG", exception.Message, false), cancellationToken)
                        .ConfigureAwait(false);
                    return;
                }

                if (line is null)
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var response = await DispatchAsync(line, cancellationToken).ConfigureAwait(false);
                await WriteAsync(stream, response, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<VisionResponseEnvelope> DispatchAsync(string line, CancellationToken cancellationToken)
    {
        VisionCommandEnvelope? command;
        try
        {
            command = JsonSerializer.Deserialize<VisionCommandEnvelope>(line, JsonOptions);
        }
        catch (JsonException exception)
        {
            return Error(string.Empty, "INVALID_JSON", exception.Message, false);
        }

        if (command is null)
        {
            return Error(string.Empty, "EMPTY_COMMAND", "Command envelope is empty.", false);
        }

        if (!string.Equals(command.ProtocolVersion, VisionProtocol.CurrentVersion, StringComparison.Ordinal))
        {
            return Error(command.RequestId, "PROTOCOL_VERSION_UNSUPPORTED",
                $"Expected protocolVersion '{VisionProtocol.CurrentVersion}'.", false);
        }

        if (!IsValidRequestId(command.RequestId))
        {
            return Error(command.RequestId, "INVALID_REQUEST_ID",
                "requestId must contain 1-128 letters, digits, '.', '-', '_' or ':'.", false);
        }

        return command.Command.Trim().ToLowerInvariant() switch
        {
            VisionCommands.Hello => Success(command.RequestId, "HELLO", new
            {
                service = "Vision.Runtime",
                protocolVersion = VisionProtocol.CurrentVersion,
                endpoint = Endpoint.ToString(),
                capabilities = Endpoint == VisionEndpointKind.Production
                    ? new[] { VisionCommands.Status, VisionCommands.Inspect, VisionCommands.Acknowledge, VisionCommands.ResetFault }
                    : new[] { VisionCommands.Status, VisionCommands.EngineeringInspectOffline, VisionCommands.Acknowledge, VisionCommands.ResetFault }
            }),
            VisionCommands.Status => Success(command.RequestId, "STATUS", station.Snapshot()),
            VisionCommands.Acknowledge => HandleAcknowledge(command),
            VisionCommands.ResetFault => station.ResetFault()
                ? Success(command.RequestId, "FAULT_RESET", station.Snapshot())
                : Error(command.RequestId, "RESET_NOT_ALLOWED", "Station is not faulted.", false),
            VisionCommands.Inspect => await HandleProductionInspectAsync(command, cancellationToken).ConfigureAwait(false),
            VisionCommands.EngineeringInspectOffline => await HandleEngineeringInspectAsync(command, cancellationToken)
                .ConfigureAwait(false),
            _ => Error(command.RequestId, "UNKNOWN_COMMAND", $"Unknown command '{command.Command}'.", false)
        };
    }

    private VisionResponseEnvelope HandleAcknowledge(VisionCommandEnvelope command)
    {
        AcknowledgePayload? payload;
        try
        {
            payload = command.Payload.Deserialize<AcknowledgePayload>(JsonOptions);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return Error(command.RequestId, "INVALID_PAYLOAD", exception.Message, false);
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.CompletedRequestId))
        {
            return Error(command.RequestId, "INVALID_PAYLOAD", "completedRequestId is required.", false);
        }

        return station.Acknowledge(payload.CompletedRequestId) switch
        {
            AcknowledgeStatus.Acknowledged =>
                Success(command.RequestId, "ACKNOWLEDGED", station.Snapshot()),
            AcknowledgeStatus.RequestIdMismatch =>
                Error(command.RequestId, "ACK_REQUEST_ID_MISMATCH",
                    "completedRequestId does not match the result awaiting acknowledgement.", false),
            _ => Error(command.RequestId, "ACK_NOT_ALLOWED",
                "No completed result is awaiting acknowledgement.", false)
        };
    }

    private async Task<VisionResponseEnvelope> HandleProductionInspectAsync(
        VisionCommandEnvelope command,
        CancellationToken cancellationToken)
    {
        if (Endpoint != VisionEndpointKind.Production)
        {
            return Error(command.RequestId, "COMMAND_NOT_ALLOWED",
                "Production inspect is available only on the production endpoint.", false);
        }

        if (!station.ProductionCommissioned)
        {
            return Error(command.RequestId, "STATION_UNCOMMISSIONED",
                "Real board/pin cameras, calibration and safety handshake are not commissioned. Use the local engineering endpoint for offline learning.", false);
        }

        ProductionInspectPayload? payload;
        try
        {
            payload = command.Payload.Deserialize<ProductionInspectPayload>(JsonOptions);
        }
        catch (JsonException exception)
        {
            return Error(command.RequestId, "INVALID_PAYLOAD", exception.Message, false);
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.ProductId) || string.IsNullOrWhiteSpace(payload.RecipeId))
        {
            return Error(command.RequestId, "INVALID_PAYLOAD", "productId and recipeId are required.", false);
        }

        var request = new InspectionRequest(command.RequestId, payload.ProductId, payload.RecipeId);
        return await ExecuteQueuedAsync(request, false, cancellationToken).ConfigureAwait(false);
    }

    private async Task<VisionResponseEnvelope> HandleEngineeringInspectAsync(
        VisionCommandEnvelope command,
        CancellationToken cancellationToken)
    {
        if (Endpoint != VisionEndpointKind.Engineering)
        {
            return Error(command.RequestId, "COMMAND_NOT_ALLOWED",
                "Offline image overrides are accepted only on the loopback engineering endpoint.", false);
        }

        EngineeringInspectPayload? payload;
        try
        {
            payload = command.Payload.Deserialize<EngineeringInspectPayload>(JsonOptions);
        }
        catch (JsonException exception)
        {
            return Error(command.RequestId, "INVALID_PAYLOAD", exception.Message, false);
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.ProductId) || string.IsNullOrWhiteSpace(payload.RecipeId))
        {
            return Error(command.RequestId, "INVALID_PAYLOAD", "productId and recipeId are required.", false);
        }

        var request = new InspectionRequest(
            command.RequestId,
            payload.ProductId,
            payload.RecipeId,
            payload.BoardImagePath,
            payload.PinImagePath);
        return await ExecuteQueuedAsync(request, true, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<VisionResponseEnvelope> AwaitResultAsync(
        string requestId,
        QueueSubmission submission,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await (submission.ResultTask
                ?? throw new InvalidOperationException("Accepted queue item has no result task."))
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            return new VisionResponseEnvelope
            {
                RequestId = requestId,
                Success = result.Disposition != InspectionDisposition.Faulted,
                Status = result.Disposition.ToString().ToUpperInvariant(),
                Payload = InspectionResultMapper.ToContract(result),
                IsReplay = submission.IsReplay
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Error(requestId, "EXECUTION_FAILED", exception.Message, true);
        }
    }

    private async Task<VisionResponseEnvelope> ExecuteQueuedAsync(
        InspectionRequest request,
        bool engineering,
        CancellationToken cancellationToken)
    {
        var submission = queue.Submit(request, engineering);
        return submission.Status switch
        {
            QueueSubmissionStatus.Accepted or QueueSubmissionStatus.Replay =>
                await AwaitResultAsync(request.RequestId, submission, cancellationToken).ConfigureAwait(false),
            QueueSubmissionStatus.Busy => Error(request.RequestId, "BUSY", submission.Message ?? "Busy.", true),
            QueueSubmissionStatus.RequestIdConflict => Error(request.RequestId, "REQUEST_ID_CONFLICT",
                submission.Message ?? "requestId conflict.", false),
            _ => Error(request.RequestId, "STATION_UNAVAILABLE",
                submission.Message ?? "Station unavailable.", true)
        };
    }

    private static bool IsValidRequestId(string requestId) =>
        requestId.Length is > 0 and <= 128 && requestId.All(character =>
            char.IsLetterOrDigit(character) || character is '.' or '-' or '_' or ':');

    private static VisionResponseEnvelope Success(string requestId, string status, object payload) => new()
    {
        RequestId = requestId,
        Success = true,
        Status = status,
        Payload = payload
    };

    private static VisionResponseEnvelope Error(
        string requestId,
        string code,
        string message,
        bool retryable) => new()
    {
        RequestId = requestId,
        Success = false,
        Status = "ERROR",
        Error = new ProtocolError(code, message, retryable)
    };

    private static async Task WriteAsync(
        Stream stream,
        VisionResponseEnvelope response,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response, JsonOptions) + "\n");
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
