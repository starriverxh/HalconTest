using VisionSystem.Domain;

namespace VisionSystem.Application;

public enum CameraRole
{
    Board,
    Pins
}

public interface IVisionFrame : IDisposable
{
    string FrameId { get; }
    CameraRole Role { get; }
    DateTimeOffset CapturedUtc { get; }
    int Width { get; }
    int Height { get; }
    string SourceName { get; }
    AcquisitionProvenance Provenance { get; }
}

public interface ICameraSource : IAsyncDisposable
{
    CameraRole Role { get; }
    string Name { get; }
    bool IsReady { get; }
    Task OpenAsync(CameraRecipe recipe, CancellationToken cancellationToken);
    Task<IVisionFrame> AcquireAsync(string? imageOverride, CancellationToken cancellationToken);
}

/// <summary>
/// Persists the raw frame while its native image is still alive and returns the
/// immutable evidence that will be written into the cycle result.
/// </summary>
public interface IFrameArchive
{
    Task<FrameEvidence> ArchiveAsync(
        string requestId,
        IVisionFrame frame,
        CancellationToken cancellationToken);
}

public interface IBoardVisionAlgorithm : IDisposable
{
    string Name { get; }
    Task<VisionStageResult<BoardMeasurement>> LocateAsync(
        IVisionFrame frame,
        VisionRecipe recipe,
        CancellationToken cancellationToken);
}

public interface IPinVisionAlgorithm : IDisposable
{
    string Name { get; }
    Task<VisionStageResult<PinMeasurement>> InspectAsync(
        IVisionFrame? frame,
        VisionRecipe recipe,
        CancellationToken cancellationToken);
}

public sealed record VisionStageResult<T>(
    T? Value,
    IReadOnlyList<VisionFailure> Failures,
    IReadOnlyDictionary<string, double> Metrics)
    where T : class
{
    public bool IsValid => Value is not null && Failures.Count == 0;

    public static VisionStageResult<T> Success(
        T value,
        IReadOnlyDictionary<string, double>? metrics = null) =>
        new(value, [], metrics ?? new Dictionary<string, double>());

    public static VisionStageResult<T> Failure(params VisionFailure[] failures) =>
        new(null, failures, new Dictionary<string, double>());
}

public interface IRecipeRepository
{
    Task<VisionRecipe> GetAsync(string recipeId, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken);
}

public interface IInspectionRepository
{
    Task<string> SaveAsync(InspectionResult result, CancellationToken cancellationToken);
}

public sealed record EquipmentReadiness(
    bool MotionStationary,
    bool SafetyInterlockClosed,
    IReadOnlyList<VisionFailure> Failures);

public interface IEquipmentReadiness
{
    Task<EquipmentReadiness> CheckAsync(CancellationToken cancellationToken);
}

public interface ICycleEventSink
{
    Task StateChangedAsync(
        string requestId,
        VisionCycleState state,
        DateTimeOffset timestampUtc,
        CancellationToken cancellationToken);
}

public interface ISystemClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : ISystemClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
