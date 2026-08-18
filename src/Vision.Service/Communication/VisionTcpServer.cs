using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Vision.Service.Configuration;
using Vision.Service.Storage;
using Vision.Service.Vision;
using Vision.Service.Workbench;

namespace Vision.Service.Communication;

public sealed class VisionTcpServer : IAsyncDisposable
{
    private readonly ServiceOptions _options;
    private readonly IVisionBackend _backend;
    private readonly IVisionWorkbench? _workbench;
    private readonly ResultLogger _store;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stopSource = new();
    private readonly SemaphoreSlim _triggerGate = new(1, 1);
    private readonly SemaphoreSlim _workbenchGate = new(1, 1);
    private readonly object _clientTasksLock = new();
    private readonly HashSet<Task> _clientTasks = [];
    private long _acceptedConnections;
    private long _completedTriggers;
    private long _failedTriggers;
    private DateTimeOffset? _startedUtc;

    public VisionTcpServer(ServiceOptions options, IVisionBackend backend, IVisionWorkbench? workbench = null)
    {
        if (options.DefaultTimeoutMs > options.MaximumTimeoutMs)
        {
            throw new ArgumentException("DefaultTimeoutMs cannot exceed MaximumTimeoutMs.", nameof(options));
        }

        _options = options;
        _backend = backend;
        _workbench = workbench;
        _store = new ResultLogger(options.RuntimeDirectory);
        _listener = new TcpListener(options.ListenAddress, options.Port);
    }

    public int BoundPort => (_listener.LocalEndpoint as IPEndPoint)?.Port ?? _options.Port;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        using var linkedStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopSource.Token);
        _startedUtc = DateTimeOffset.UtcNow;
        _listener.Start();
        await SafeLogAsync("Information", "service_started", new
        {
            address = _options.ListenAddress.ToString(),
            port = BoundPort,
            backend = _backend.Name,
            processId = Environment.ProcessId
        }).ConfigureAwait(false);

        try
        {
            while (!linkedStop.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(linkedStop.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (linkedStop.IsCancellationRequested)
                {
                    break;
                }
                catch (SocketException) when (linkedStop.IsCancellationRequested)
                {
                    break;
                }

                Interlocked.Increment(ref _acceptedConnections);
                var task = HandleClientAsync(client, linkedStop.Token);
                lock (_clientTasksLock)
                {
                    _clientTasks.Add(task);
                }

                _ = task.ContinueWith(
                    completed =>
                    {
                        lock (_clientTasksLock)
                        {
                            _clientTasks.Remove(completed);
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
        finally
        {
            _listener.Stop();
            Task[] clients;
            lock (_clientTasksLock)
            {
                clients = [.. _clientTasks];
            }

            await Task.WhenAll(clients).ConfigureAwait(false);
            await SafeLogAsync("Information", "service_stopped", new
            {
                completedTriggers = Interlocked.Read(ref _completedTriggers),
                failedTriggers = Interlocked.Read(ref _failedTriggers)
            }).ConfigureAwait(false);
        }
    }

    public void RequestStop()
    {
        if (!_stopSource.IsCancellationRequested)
        {
            _stopSource.Cancel();
            _listener.Stop();
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken serverToken)
    {
        using (client)
        {
            client.NoDelay = true;
            var remoteEndpoint = client.Client.RemoteEndPoint as IPEndPoint;
            await SafeLogAsync("Information", "client_connected", new { remote = remoteEndpoint?.ToString() }).ConfigureAwait(false);
            try
            {
                var stream = client.GetStream();
                var reader = new JsonLineReader(stream);
                while (!serverToken.IsCancellationRequested)
                {
                    string? line;
                    try
                    {
                        line = await reader.ReadLineAsync(serverToken).ConfigureAwait(false);
                    }
                    catch (MessageTooLargeException)
                    {
                        var response = Error(string.Empty, string.Empty, ErrorCodes.MessageTooLarge,
                            $"A request must not exceed {Protocol.MaxMessageBytes} UTF-8 bytes.");
                        await WriteResponseAsync(stream, response, CancellationToken.None).ConfigureAwait(false);
                        break;
                    }
                    catch (DecoderFallbackException)
                    {
                        var response = Error(string.Empty, string.Empty, ErrorCodes.InvalidJson, "Request is not valid UTF-8.");
                        await WriteResponseAsync(stream, response, CancellationToken.None).ConfigureAwait(false);
                        break;
                    }

                    if (line is null)
                    {
                        break;
                    }

                    var outcome = await ProcessLineAsync(line, remoteEndpoint, serverToken).ConfigureAwait(false);
                    await WriteResponseAsync(stream, outcome.Response, serverToken).ConfigureAwait(false);
                    if (outcome.ShutdownAfterReply)
                    {
                        RequestStop();
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (serverToken.IsCancellationRequested)
            {
            }
            catch (IOException exception)
            {
                await SafeLogAsync("Warning", "client_io_error", new
                {
                    remote = remoteEndpoint?.ToString(),
                    exception.Message
                }).ConfigureAwait(false);
            }
            catch (SocketException exception)
            {
                await SafeLogAsync("Warning", "client_socket_error", new
                {
                    remote = remoteEndpoint?.ToString(),
                    exception.SocketErrorCode,
                    exception.Message
                }).ConfigureAwait(false);
            }
            finally
            {
                await SafeLogAsync("Information", "client_disconnected", new { remote = remoteEndpoint?.ToString() }).ConfigureAwait(false);
            }
        }
    }

    private async Task<CommandOutcome> ProcessLineAsync(string line, IPEndPoint? remoteEndpoint, CancellationToken serverToken)
    {
        var stopwatch = Stopwatch.StartNew();
        VisionRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<VisionRequest>(line, Protocol.JsonOptions);
        }
        catch (JsonException exception)
        {
            return new CommandOutcome(Error(string.Empty, string.Empty, ErrorCodes.InvalidJson,
                $"Malformed JSON: {exception.Message}", stopwatch.ElapsedMilliseconds), false);
        }

        var requestId = request?.RequestId?.Trim() ?? string.Empty;
        var command = request?.Command?.Trim().ToLowerInvariant() ?? string.Empty;
        if (request is null || string.IsNullOrWhiteSpace(requestId) || requestId.Length > 128 ||
            string.IsNullOrWhiteSpace(command))
        {
            return new CommandOutcome(Error(requestId, command, ErrorCodes.InvalidRequest,
                "requestId (1-128 characters) and command are required.", stopwatch.ElapsedMilliseconds), false);
        }

        var outcome = command switch
        {
            "ping" => new CommandOutcome(Success(requestId, command, "Service is reachable.",
                new { protocolVersion = Protocol.Version }, stopwatch.ElapsedMilliseconds), false),
            "status" => new CommandOutcome(Success(requestId, command, "Status returned.",
                BuildStatus(), stopwatch.ElapsedMilliseconds), false),
            "trigger" => new CommandOutcome(await TriggerAsync(request, requestId, command, stopwatch, serverToken).ConfigureAwait(false), false),
            "preview_processing" => new CommandOutcome(await WorkbenchAsync<ProcessingRequest, ProcessingResult>(
                request, requestId, command, remoteEndpoint, stopwatch, serverToken,
                (parameters, token) => _workbench!.PreviewProcessingAsync(parameters, token)).ConfigureAwait(false), false),
            "create_template" => new CommandOutcome(await WorkbenchAsync<ShapeModelCreateRequest, ShapeModelCreateResult>(
                request, requestId, command, remoteEndpoint, stopwatch, serverToken,
                (parameters, token) => _workbench!.CreateShapeModelAsync(parameters, token)).ConfigureAwait(false), false),
            "match_template" => new CommandOutcome(await WorkbenchAsync<ShapeModelMatchRequest, ShapeModelMatchResult>(
                request, requestId, command, remoteEndpoint, stopwatch, serverToken,
                (parameters, token) => _workbench!.MatchShapeModelAsync(parameters, token)).ConfigureAwait(false), false),
            "calibrate_points" => new CommandOutcome(await WorkbenchAsync<CalibrationFitRequest, CalibrationFitResult>(
                request, requestId, command, remoteEndpoint, stopwatch, serverToken,
                (parameters, token) => _workbench!.FitCalibrationAsync(parameters, token)).ConfigureAwait(false), false),
            "shutdown" => Shutdown(requestId, command, remoteEndpoint, stopwatch),
            _ => new CommandOutcome(Error(requestId, command, ErrorCodes.UnknownCommand,
                $"Unknown command '{command}'.", stopwatch.ElapsedMilliseconds), false)
        };

        await SafeLogAsync(outcome.Response.Ok ? "Information" : "Warning", "request_completed", new
        {
            requestId,
            command,
            outcome.Response.Code,
            outcome.Response.DurationMs,
            remote = remoteEndpoint?.ToString()
        }).ConfigureAwait(false);
        return outcome;
    }

    private async Task<VisionResponse> WorkbenchAsync<TRequest, TResult>(
        VisionRequest request,
        string requestId,
        string command,
        IPEndPoint? remoteEndpoint,
        Stopwatch stopwatch,
        CancellationToken serverToken,
        Func<TRequest, CancellationToken, Task<TResult>> operation)
        where TRequest : class
    {
        if (remoteEndpoint is null || !IPAddress.IsLoopback(remoteEndpoint.Address))
        {
            return Error(requestId, command, ErrorCodes.Forbidden,
                "HALCON engineering commands are restricted to the local workstation.", stopwatch.ElapsedMilliseconds);
        }

        if (_workbench is null)
        {
            return Error(requestId, command, ErrorCodes.BackendError,
                "The HALCON engineering workbench is not enabled.", stopwatch.ElapsedMilliseconds);
        }

        if (request.Parameters is not { ValueKind: JsonValueKind.Object } parametersElement)
        {
            return Error(requestId, command, ErrorCodes.InvalidRequest,
                "A JSON parameters object is required.", stopwatch.ElapsedMilliseconds);
        }

        TRequest? parameters;
        try
        {
            parameters = parametersElement.Deserialize<TRequest>(Protocol.JsonOptions);
        }
        catch (JsonException exception)
        {
            return Error(requestId, command, ErrorCodes.InvalidRequest,
                $"Invalid workbench parameters: {exception.Message}", stopwatch.ElapsedMilliseconds);
        }

        if (parameters is null)
        {
            return Error(requestId, command, ErrorCodes.InvalidRequest,
                "Workbench parameters are empty.", stopwatch.ElapsedMilliseconds);
        }

        if (!await _triggerGate.WaitAsync(0, serverToken).ConfigureAwait(false))
        {
            return Error(requestId, command, ErrorCodes.Busy,
                "Production inspection or another engineering operation is running.", stopwatch.ElapsedMilliseconds);
        }

        var workbenchGateAcquired = false;
        if (!await _workbenchGate.WaitAsync(0).ConfigureAwait(false))
        {
            _triggerGate.Release();
            return Error(requestId, command, ErrorCodes.Busy,
                "Another engineering operation is running.", stopwatch.ElapsedMilliseconds);
        }

        workbenchGateAcquired = true;

        try
        {
            var timeoutMs = Math.Clamp(request.TimeoutMs ?? _options.DefaultTimeoutMs, 1, _options.MaximumTimeoutMs);
            using var timeoutSource = new CancellationTokenSource(timeoutMs);
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(serverToken, timeoutSource.Token);
            try
            {
                var result = await operation(parameters, linkedSource.Token).ConfigureAwait(false);
                return Success(requestId, command, "HALCON engineering operation completed.", result,
                    stopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested && !serverToken.IsCancellationRequested)
            {
                return Error(requestId, command, ErrorCodes.Timeout,
                    $"Engineering operation exceeded the {timeoutMs} ms timeout.", stopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                return Error(requestId, command, ErrorCodes.Cancelled,
                    "Engineering operation was cancelled.", stopwatch.ElapsedMilliseconds);
            }
            catch (Exception exception)
            {
                await SafeLogAsync("Error", "workbench_error", new { requestId, command, exception = exception.ToString() })
                    .ConfigureAwait(false);
                return Error(requestId, command, ErrorCodes.BackendError, exception.Message, stopwatch.ElapsedMilliseconds);
            }
        }
        finally
        {
            if (workbenchGateAcquired)
            {
                _workbenchGate.Release();
            }

            _triggerGate.Release();
        }
    }

    private async Task<VisionResponse> TriggerAsync(
        VisionRequest request,
        string requestId,
        string command,
        Stopwatch stopwatch,
        CancellationToken serverToken)
    {
        if (!_backend.IsReady)
        {
            return Error(requestId, command, ErrorCodes.BackendError, "Vision backend is not ready.", stopwatch.ElapsedMilliseconds);
        }

        if (!await _triggerGate.WaitAsync(0, serverToken).ConfigureAwait(false))
        {
            return Error(requestId, command, ErrorCodes.Busy, "Another trigger is being processed.", stopwatch.ElapsedMilliseconds);
        }

        try
        {
            var timeoutMs = Math.Clamp(request.TimeoutMs ?? _options.DefaultTimeoutMs, 1, _options.MaximumTimeoutMs);
            using var timeoutSource = new CancellationTokenSource(timeoutMs);
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(serverToken, timeoutSource.Token);
            try
            {
                var measurement = await _backend.TriggerAsync(requestId, request.Parameters, linkedSource.Token).ConfigureAwait(false);
                var completedUtc = DateTimeOffset.UtcNow;
                var persisted = new
                {
                    protocolVersion = Protocol.Version,
                    requestId,
                    command,
                    completedUtc,
                    durationMs = stopwatch.ElapsedMilliseconds,
                    backend = _backend.Name,
                    measurement
                };
                var resultPath = await _store.SaveResultAsync(requestId, persisted, serverToken).ConfigureAwait(false);
                Interlocked.Increment(ref _completedTriggers);
                return Success(requestId, command, "Inspection completed.", new
                {
                    measurement.IsOk,
                    measurement.X,
                    measurement.Y,
                    measurement.Theta,
                    measurement.Score,
                    measurement.ImagePath,
                    measurement.Details,
                    resultPath
                }, stopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested && !serverToken.IsCancellationRequested)
            {
                Interlocked.Increment(ref _failedTriggers);
                return Error(requestId, command, ErrorCodes.Timeout,
                    $"Inspection exceeded the {timeoutMs} ms timeout.", stopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _failedTriggers);
                return Error(requestId, command, ErrorCodes.Cancelled, "Inspection was cancelled.", stopwatch.ElapsedMilliseconds);
            }
            catch (Exception exception)
            {
                Interlocked.Increment(ref _failedTriggers);
                await SafeLogAsync("Error", "backend_error", new { requestId, exception = exception.ToString() }).ConfigureAwait(false);
                return Error(requestId, command, ErrorCodes.BackendError, exception.Message, stopwatch.ElapsedMilliseconds);
            }
        }
        finally
        {
            _triggerGate.Release();
        }
    }

    private CommandOutcome Shutdown(string requestId, string command, IPEndPoint? remoteEndpoint, Stopwatch stopwatch)
    {
        var isLoopback = remoteEndpoint is not null && IPAddress.IsLoopback(remoteEndpoint.Address);
        if (!isLoopback && !_options.AllowRemoteShutdown)
        {
            return new CommandOutcome(Error(requestId, command, ErrorCodes.Forbidden,
                "Remote shutdown is disabled.", stopwatch.ElapsedMilliseconds), false);
        }

        return new CommandOutcome(Success(requestId, command, "Shutdown accepted.", null,
            stopwatch.ElapsedMilliseconds), true);
    }

    private object BuildStatus() => new
    {
        state = _triggerGate.CurrentCount == 0 ? "busy" : "ready",
        busy = _triggerGate.CurrentCount == 0,
        workbenchBusy = _workbenchGate.CurrentCount == 0,
        workbenchEnabled = _workbench is not null,
        backend = _backend.Name,
        backendReady = _backend.IsReady,
        startedUtc = _startedUtc,
        uptimeSeconds = _startedUtc is null ? 0 : (DateTimeOffset.UtcNow - _startedUtc.Value).TotalSeconds,
        acceptedConnections = Interlocked.Read(ref _acceptedConnections),
        completedTriggers = Interlocked.Read(ref _completedTriggers),
        failedTriggers = Interlocked.Read(ref _failedTriggers),
        processId = Environment.ProcessId
    };

    private static VisionResponse Success(string requestId, string command, string message, object? data, long durationMs) =>
        new(requestId, command, true, ErrorCodes.Ok, message, DateTimeOffset.UtcNow, durationMs, data);

    private static VisionResponse Error(string requestId, string command, string code, string message, long durationMs = 0) =>
        new(requestId, command, false, code, message, DateTimeOffset.UtcNow, durationMs);

    private static async Task WriteResponseAsync(NetworkStream stream, VisionResponse response, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(response, Protocol.JsonOptions);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SafeLogAsync(string level, string eventName, object data)
    {
        try
        {
            await _store.LogAsync(level, eventName, data).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[{DateTimeOffset.Now:O}] Logging failed: {exception.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        RequestStop();
        _stopSource.Dispose();
        _triggerGate.Dispose();
        _workbenchGate.Dispose();
        await _backend.DisposeAsync().ConfigureAwait(false);
    }

    private sealed record CommandOutcome(VisionResponse Response, bool ShutdownAfterReply);
}

internal sealed class MessageTooLargeException : Exception;

internal sealed class JsonLineReader(NetworkStream stream)
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly byte[] _readBuffer = new byte[4096];
    private readonly MemoryStream _lineBuffer = new();
    private int _readOffset;
    private int _readCount;

    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            if (_readOffset >= _readCount)
            {
                _readCount = await stream.ReadAsync(_readBuffer, cancellationToken).ConfigureAwait(false);
                _readOffset = 0;
                if (_readCount == 0)
                {
                    if (_lineBuffer.Length == 0)
                    {
                        return null;
                    }

                    return DecodeAndReset();
                }
            }

            while (_readOffset < _readCount)
            {
                var value = _readBuffer[_readOffset++];
                if (value == (byte)'\n')
                {
                    return DecodeAndReset();
                }

                if (_lineBuffer.Length >= Protocol.MaxMessageBytes)
                {
                    throw new MessageTooLargeException();
                }

                _lineBuffer.WriteByte(value);
            }
        }
    }

    private string DecodeAndReset()
    {
        var length = checked((int)_lineBuffer.Length);
        var buffer = _lineBuffer.GetBuffer();
        if (length > 0 && buffer[length - 1] == (byte)'\r')
        {
            length--;
        }

        var result = StrictUtf8.GetString(buffer, 0, length);
        _lineBuffer.SetLength(0);
        return result;
    }
}
