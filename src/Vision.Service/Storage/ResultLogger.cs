using System.Text.Json;
using Vision.Service.Communication;

namespace Vision.Service.Storage;

public sealed class ResultLogger
{
    private readonly string _logsDirectory;
    private readonly string _resultsDirectory;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public ResultLogger(string runtimeDirectory)
    {
        _logsDirectory = Path.Combine(runtimeDirectory, "logs");
        _resultsDirectory = Path.Combine(runtimeDirectory, "results");
        Directory.CreateDirectory(_logsDirectory);
        Directory.CreateDirectory(_resultsDirectory);
    }

    public async Task LogAsync(string level, string eventName, object data, CancellationToken cancellationToken = default)
    {
        var entry = JsonSerializer.Serialize(new
        {
            timestampUtc = DateTimeOffset.UtcNow,
            level,
            eventName,
            data
        }, Protocol.JsonOptions);
        var path = Path.Combine(_logsDirectory, $"vision-{DateTime.UtcNow:yyyyMMdd}.jsonl");

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(path, entry + Environment.NewLine, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<string> SaveResultAsync(string requestId, object result, CancellationToken cancellationToken = default)
    {
        var safeId = string.Concat(requestId.Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_'));
        safeId = safeId[..Math.Min(safeId.Length, 80)];
        var path = Path.Combine(_resultsDirectory, $"{DateTime.UtcNow:yyyyMMdd_HHmmssfff}_{safeId}.json");
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, useAsync: true);
        await JsonSerializer.SerializeAsync(stream, result, Protocol.JsonOptions, cancellationToken).ConfigureAwait(false);
        return path;
    }
}
