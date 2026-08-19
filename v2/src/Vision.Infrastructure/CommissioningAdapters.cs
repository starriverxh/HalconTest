using System.Text.Json;
using VisionSystem.Application;
using VisionSystem.Domain;

namespace VisionSystem.Infrastructure;

public sealed class CommissioningEquipmentReadiness : IEquipmentReadiness
{
    public Task<EquipmentReadiness> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Explicitly represents the offline workstation. A real PLC adapter must
        // replace this class before automatic motion can be commissioned.
        return Task.FromResult(new EquipmentReadiness(true, true, []));
    }
}

public sealed class JsonLineCycleEventSink(string runtimeDirectory) : ICycleEventSink
{
    private readonly string _path = CreatePath(runtimeDirectory);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task StateChangedAsync(
        string requestId,
        VisionCycleState state,
        DateTimeOffset timestampUtc,
        CancellationToken cancellationToken)
    {
        var line = JsonSerializer.Serialize(new { requestId, state = state.ToString(), timestampUtc }) + Environment.NewLine;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(_path, line, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string CreatePath(string runtimeDirectory)
    {
        var directory = Path.Combine(Path.GetFullPath(runtimeDirectory), "events");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"cycle-events-{DateTime.UtcNow:yyyyMMdd}.jsonl");
    }
}
