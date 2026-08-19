using System.Diagnostics;

namespace VisionSystem.Halcon.Diagnostics;

public sealed record HalconStageDiagnostic(
    string Stage,
    bool Succeeded,
    long DurationMicroseconds,
    string Message,
    IReadOnlyDictionary<string, double> Metrics);

public interface IHalconDiagnosticsSource
{
    IReadOnlyList<HalconStageDiagnostic> LastDiagnostics { get; }
}

internal sealed class DiagnosticCollector
{
    private readonly List<HalconStageDiagnostic> _entries = [];
    private readonly Dictionary<string, double> _metrics = new(StringComparer.Ordinal);

    public IReadOnlyList<HalconStageDiagnostic> Entries => _entries.ToArray();
    public IReadOnlyDictionary<string, double> Metrics => new Dictionary<string, double>(_metrics);

    public StageTimer Start(string stage) => new(this, stage);

    public void SetMetric(string name, double value) => _metrics[name] = value;

    internal void Complete(
        string stage,
        long elapsedMicroseconds,
        bool succeeded,
        string message,
        IReadOnlyDictionary<string, double>? metrics)
    {
        var stageMetrics = metrics is null
            ? new Dictionary<string, double>()
            : new Dictionary<string, double>(metrics);
        foreach (var pair in stageMetrics)
        {
            _metrics[$"{stage}.{pair.Key}"] = pair.Value;
        }

        _metrics[$"{stage}.durationUs"] = elapsedMicroseconds;
        _entries.Add(new HalconStageDiagnostic(stage, succeeded, elapsedMicroseconds, message, stageMetrics));
    }
}

internal sealed class StageTimer : IDisposable
{
    private readonly DiagnosticCollector _owner;
    private readonly string _stage;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private bool _completed;

    public StageTimer(DiagnosticCollector owner, string stage)
    {
        _owner = owner;
        _stage = stage;
    }

    public void Success(string message = "OK", IReadOnlyDictionary<string, double>? metrics = null) =>
        Complete(true, message, metrics);

    public void Failure(string message, IReadOnlyDictionary<string, double>? metrics = null) =>
        Complete(false, message, metrics);

    public void Dispose()
    {
        if (!_completed)
        {
            Complete(false, "Stage exited without an explicit outcome.", null);
        }
    }

    private void Complete(bool succeeded, string message, IReadOnlyDictionary<string, double>? metrics)
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        _stopwatch.Stop();
        _owner.Complete(
            _stage,
            (long)(_stopwatch.Elapsed.TotalMilliseconds * 1000.0),
            succeeded,
            message,
            metrics);
    }
}
