using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using VisionSystem.Domain;

namespace VisionSystem.Runtime;

public enum QueueSubmissionStatus
{
    Accepted,
    Replay,
    Busy,
    RequestIdConflict,
    StationUnavailable
}

public sealed record QueueSubmission(
    QueueSubmissionStatus Status,
    Task<InspectionResult>? ResultTask,
    bool IsReplay,
    string? Message);

public sealed class IdempotentInspectionQueue : IAsyncDisposable
{
    private sealed record WorkItem(
        InspectionRequest Request,
        string Fingerprint,
        bool Engineering,
        TaskCompletionSource<InspectionResult> Completion);

    private sealed record CacheEntry(
        string Fingerprint,
        Task<InspectionResult> ResultTask,
        DateTimeOffset CreatedUtc);

    private readonly Channel<WorkItem> _channel;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _completionOrder = new();
    private readonly Func<InspectionRequest, CancellationToken, Task<InspectionResult>> _execute;
    private readonly RuntimeStation _station;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _worker;
    private readonly int _cacheCapacity;
    private readonly object _submissionSync = new();

    public IdempotentInspectionQueue(
        int queueCapacity,
        int cacheCapacity,
        RuntimeStation station,
        Func<InspectionRequest, CancellationToken, Task<InspectionResult>> execute)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(queueCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(cacheCapacity, 1);
        _station = station;
        _execute = execute;
        _cacheCapacity = cacheCapacity;
        _channel = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(queueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite,
            AllowSynchronousContinuations = false
        });
        _worker = Task.Run(ProcessAsync);
    }

    public QueueSubmission Submit(InspectionRequest request, bool engineering)
    {
        var fingerprint = Fingerprint(request, engineering);
        lock (_submissionSync)
        {
            if (_cache.TryGetValue(request.RequestId, out var existing))
            {
                return existing.Fingerprint == fingerprint
                    ? new QueueSubmission(QueueSubmissionStatus.Replay, existing.ResultTask, true, null)
                    : new QueueSubmission(
                        QueueSubmissionStatus.RequestIdConflict,
                        null,
                        false,
                        "The same requestId was previously used with different content.");
            }

            if (!_station.TryBegin(request.RequestId, request.RecipeId, engineering))
            {
                return new QueueSubmission(
                    QueueSubmissionStatus.StationUnavailable,
                    null,
                    false,
                    $"Station is {_station.Snapshot().State}; acknowledge the previous result or reset the fault first.");
            }

            var completion = new TaskCompletionSource<InspectionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entry = new CacheEntry(fingerprint, completion.Task, DateTimeOffset.UtcNow);
            if (!_cache.TryAdd(request.RequestId, entry))
            {
                throw new InvalidOperationException("Idempotency cache changed while holding the submission lock.");
            }

            var item = new WorkItem(request, fingerprint, engineering, completion);
            if (!_channel.Writer.TryWrite(item))
            {
                _cache.TryRemove(request.RequestId, out _);
                _station.Complete(request.RequestId, false);
                _station.Acknowledge(request.RequestId);
                return new QueueSubmission(
                    QueueSubmissionStatus.Busy,
                    null,
                    false,
                    "Inspection queue is full.");
            }

            return new QueueSubmission(QueueSubmissionStatus.Accepted, completion.Task, false, null);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        _shutdown.Cancel();
        try
        {
            await _worker.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _shutdown.Dispose();
    }

    private async Task ProcessAsync()
    {
        await foreach (var item in _channel.Reader.ReadAllAsync(_shutdown.Token).ConfigureAwait(false))
        {
            try
            {
                var result = await _execute(item.Request, _shutdown.Token).ConfigureAwait(false);
                _station.Complete(item.Request.RequestId, result.Disposition == InspectionDisposition.Faulted);
                // Publish the station state before waking protocol waiters. This
                // guarantees an immediate ACK observes ResultAvailable/Faulted,
                // never the preceding Processing state.
                item.Completion.TrySetResult(result);
            }
            catch (OperationCanceledException exception) when (_shutdown.IsCancellationRequested)
            {
                item.Completion.TrySetCanceled(exception.CancellationToken);
                break;
            }
            catch (Exception exception)
            {
                _station.Complete(item.Request.RequestId, true);
                item.Completion.TrySetException(exception);
            }
            finally
            {
                _completionOrder.Enqueue(item.Request.RequestId);
                TrimCache();
            }
        }
    }

    private void TrimCache()
    {
        while (_cache.Count > _cacheCapacity && _completionOrder.TryDequeue(out var requestId))
        {
            _cache.TryRemove(requestId, out _);
        }
    }

    private static string Fingerprint(InspectionRequest request, bool engineering)
    {
        var json = JsonSerializer.Serialize(new
        {
            request.ProductId,
            request.RecipeId,
            request.BoardImageOverride,
            request.PinImageOverride,
            engineering
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
}
