using System.Text.Json;

namespace Vision.Service.Vision;

public interface IVisionBackend : IAsyncDisposable
{
    string Name { get; }
    bool IsReady { get; }
    Task<VisionResult> TriggerAsync(string requestId, JsonElement? parameters, CancellationToken cancellationToken);
}

public sealed class HalconVisionBackend(
    PinInspectionService inspectionService,
    IAsyncDisposable cameraLifetime,
    IDisposable? algorithmLifetime = null) : IVisionBackend
{
    public string Name => "halcondotnet";
    public bool IsReady => inspectionService.IsReady;

    public Task<VisionResult> TriggerAsync(
        string requestId,
        JsonElement? parameters,
        CancellationToken cancellationToken)
    {
        var imagePath = ReadString(parameters, "imagePath");
        return inspectionService.InspectAsync(requestId, imagePath, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        algorithmLifetime?.Dispose();
        await cameraLifetime.DisposeAsync().ConfigureAwait(false);
    }

    private static string? ReadString(JsonElement? parameters, string propertyName) =>
        parameters is { ValueKind: JsonValueKind.Object } value &&
        value.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}

public sealed class MockVisionBackend(TimeSpan? processingTime = null) : IVisionBackend
{
    private readonly TimeSpan _processingTime = processingTime ?? TimeSpan.FromMilliseconds(120);

    public string Name => "mock";
    public bool IsReady => true;

    public async Task<VisionResult> TriggerAsync(
        string requestId,
        JsonElement? parameters,
        CancellationToken cancellationToken)
    {
        var delay = ReadDouble(parameters, "delayMs") is { } requested
            ? TimeSpan.FromMilliseconds(Math.Clamp(requested, 0, 60_000))
            : _processingTime;
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

        var x = ReadDouble(parameters, "mockX") ?? 0.012;
        var y = ReadDouble(parameters, "mockY") ?? -0.008;
        var theta = ReadDouble(parameters, "mockTheta") ?? 0.015;
        var score = ReadDouble(parameters, "mockScore") ?? 0.998;
        var tolerance = ReadDouble(parameters, "toleranceMm") ?? 0.05;
        return new VisionResult(
            Math.Abs(x) <= tolerance && Math.Abs(y) <= tolerance,
            x,
            y,
            theta,
            score,
            Details: new Dictionary<string, object?>
            {
                ["mode"] = "simulation",
                ["requestId"] = requestId,
                ["toleranceMm"] = tolerance
            });
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static double? ReadDouble(JsonElement? parameters, string propertyName) =>
        parameters is { ValueKind: JsonValueKind.Object } value &&
        value.TryGetProperty(propertyName, out var property) &&
        property.TryGetDouble(out var result)
            ? result
            : null;
}
