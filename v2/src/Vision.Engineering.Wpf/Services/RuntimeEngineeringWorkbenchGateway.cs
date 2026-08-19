using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.IO;
using VisionSystem.Contracts;
using VisionSystem.Engineering.Wpf.Models;

namespace VisionSystem.Engineering.Wpf.Services;

/// <summary>
/// Connects the production-style WPF page to the loopback engineering endpoint.
/// Teaching-only template/calibration/regression pages remain delegated to the
/// deterministic demo gateway until their Runtime engineering use cases exist.
/// </summary>
public sealed class RuntimeEngineeringWorkbenchGateway(
    string host = "127.0.0.1",
    int port = 9201) : IEngineeringWorkbenchGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly DemoEngineeringWorkbenchGateway _teaching = new();

    public bool IsOffline => true;

    public async Task<ProductionResult> RunOfflineCycleAsync(CancellationToken cancellationToken)
    {
        var requestId = $"wpf-{DateTimeOffset.Now:yyyyMMdd-HHmmssfff}";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            using var client = new TcpClient { NoDelay = true };
            await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, false, leaveOpen: true);
            await WriteAsync(stream, new
            {
                protocolVersion = VisionProtocol.CurrentVersion,
                requestId,
                command = VisionCommands.EngineeringInspectOffline,
                payload = new EngineeringInspectPayload
                {
                    ProductId = "TRAINING-PCB",
                    RecipeId = "pcb-pin-row-offline-v2"
                }
            }, timeout.Token).ConfigureAwait(false);

            var responseLine = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false)
                ?? throw new IOException("Runtime closed without returning an inspection result.");
            using var document = JsonDocument.Parse(responseLine);
            var root = document.RootElement;
            if (!root.GetProperty("success").GetBoolean())
            {
                var error = root.TryGetProperty("error", out var errorElement) &&
                            errorElement.TryGetProperty("code", out var codeElement)
                    ? codeElement.GetString() ?? "RUNTIME_ERROR"
                    : "RUNTIME_ERROR";
                return Failed(requestId, error, host, port);
            }

            var payload = root.GetProperty("payload");
            var result = new ProductionResult(
                payload.GetProperty("requestId").GetString() ?? requestId,
                payload.GetProperty("disposition").GetString() ?? "ENGINEERING_ONLY",
                payload.GetProperty("commandXmm").GetDouble(),
                payload.GetProperty("commandYmm").GetDouble(),
                payload.GetProperty("commandThetaDeg").GetDouble(),
                ReadNullableDouble(payload, "boardScore") ?? 0,
                payload.GetProperty("durationMs").GetInt64(),
                ReadFirstFailureCode(payload) ?? "MOTION_DISABLED_OFFLINE",
                RuntimeConnected: true,
                MotionAllowed: payload.GetProperty("motionAllowed").GetBoolean(),
                SourceDescription: $"Runtime {host}:{port} · engineering.inspect_offline · HALCON离线双图 · Runtime写盘",
                StateHistory: ReadStateHistory(payload));

            await WriteAsync(stream, new
            {
                protocolVersion = VisionProtocol.CurrentVersion,
                requestId = requestId + "-ack",
                command = VisionCommands.Acknowledge,
                payload = new { completedRequestId = requestId }
            }, timeout.Token).ConfigureAwait(false);
            _ = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed(requestId, "RUNTIME_TIMEOUT", host, port);
        }
        catch (Exception exception) when (exception is SocketException or IOException or JsonException or KeyNotFoundException)
        {
            return Failed(requestId, $"RUNTIME_UNAVAILABLE:{exception.GetType().Name}", host, port);
        }
    }

    public Task<TemplateValidationResult> ValidateTemplateAsync(CancellationToken cancellationToken) =>
        _teaching.ValidateTemplateAsync(cancellationToken);

    public Task<CalibrationFitResult> FitCalibrationAsync(CancellationToken cancellationToken) =>
        _teaching.FitCalibrationAsync(cancellationToken);

    public Task<RegressionResult> RunRegressionAsync(CancellationToken cancellationToken) =>
        _teaching.RunRegressionAsync(cancellationToken);

    public IReadOnlyList<TraceRecord> GetTraceRecords() => _teaching.GetTraceRecords();

    public IReadOnlyList<DeviceHealth> GetDeviceHealth() => _teaching.GetDeviceHealth();

    private static async Task WriteAsync(Stream stream, object value, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, JsonOptions) + "\n");
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static double? ReadNullableDouble(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static string? ReadFirstFailureCode(JsonElement payload)
    {
        if (!payload.TryGetProperty("failures", out var failures) ||
            failures.ValueKind != JsonValueKind.Array ||
            failures.GetArrayLength() == 0)
        {
            return null;
        }

        var first = failures[0];
        return first.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static IReadOnlyList<string> ReadStateHistory(JsonElement payload) =>
        payload.TryGetProperty("stateHistory", out var states) && states.ValueKind == JsonValueKind.Array
            ? states.EnumerateArray().Select(state => state.GetString() ?? "UNKNOWN").ToArray()
            : [];

    private static ProductionResult Failed(string requestId, string failure, string host, int port) =>
        new(
            requestId,
            "FAULT",
            0,
            0,
            0,
            0,
            0,
            failure,
            RuntimeConnected: false,
            MotionAllowed: false,
            SourceDescription: $"Runtime {host}:{port} 不可用 · 未执行HALCON · 未写结果");
}
