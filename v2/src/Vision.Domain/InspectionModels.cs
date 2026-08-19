namespace VisionSystem.Domain;

public enum VisionCycleState
{
    Idle,
    ValidatingRequest,
    LoadingRecipe,
    CheckingInterlocks,
    AcquiringBoardImage,
    CheckingBoardImage,
    LocatingBoard,
    AcquiringPinImage,
    CheckingPinImage,
    InspectingPins,
    TransformingCoordinates,
    CalculatingCorrection,
    PersistingResult,
    Completed,
    Inhibited,
    Rejected,
    Faulted
}

public enum InspectionDisposition
{
    Passed,
    Rejected,
    Faulted,
    EngineeringOnly
}

/// <summary>
/// Describes where a frame actually came from. Motion authorization must be
/// based on this run-time provenance, not only on a recipe camera mode.
/// </summary>
public enum AcquisitionProvenance
{
    Unknown,
    LiveCamera,
    OfflineFile,
    Synthetic
}

/// <summary>
/// Immutable evidence captured with one image. ArchivedImagePath and Sha256 are
/// populated by the frame archive when image retention is enabled.
/// </summary>
public sealed record FrameEvidence
{
    public required string FrameId { get; init; }
    public required string CameraRole { get; init; }
    public required AcquisitionProvenance Provenance { get; init; }
    public required DateTimeOffset CapturedUtc { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required string SourceName { get; init; }
    public string? ArchivedImagePath { get; init; }
    public string? Sha256 { get; init; }
}

public sealed record ImageQualityMeasurement(
    double MeanGray,
    double GrayDeviation,
    double MeanEdgeAmplitude,
    double SaturatedFraction);

public sealed record BoardMeasurement(
    Pose2D Pose,
    double Score,
    IReadOnlyList<PixelPoint> ImageFeatures,
    IReadOnlyList<IdentifiedMachinePoint> HolesInMachine,
    ImageQualityMeasurement Quality,
    string LocatorName);

public sealed record PinMeasurement(
    Pose2D Pose,
    double Score,
    IReadOnlyList<PixelPoint> PinTips,
    IReadOnlyList<IdentifiedMachinePoint> PinsInMachine,
    bool IsSimulated,
    string LocatorName);

public sealed record InspectionRequest(
    string RequestId,
    string ProductId,
    string RecipeId,
    string? BoardImageOverride = null,
    string? PinImageOverride = null);

public sealed record InspectionResult
{
    public required string RequestId { get; init; }
    public required string ProductId { get; init; }
    public required string RecipeId { get; init; }
    public required int RecipeVersion { get; init; }
    public required InspectionDisposition Disposition { get; init; }
    public required bool MotionAllowed { get; init; }
    public required XYThetaCorrection Correction { get; init; }
    public required AlignmentPlan? Alignment { get; init; }
    public required BoardMeasurement? Board { get; init; }
    public required PinMeasurement? Pins { get; init; }
    public required IReadOnlyList<VisionFailure> Failures { get; init; }
    public required IReadOnlyList<VisionCycleState> StateHistory { get; init; }
    public required DateTimeOffset StartedUtc { get; init; }
    public required DateTimeOffset CompletedUtc { get; init; }
    public required long DurationMs { get; init; }
    public FrameEvidence? BoardFrame { get; init; }
    public FrameEvidence? PinFrame { get; init; }
    public IReadOnlyDictionary<string, double> StageMetrics { get; init; } =
        new Dictionary<string, double>(StringComparer.Ordinal);
    public string BoardAlgorithmName { get; init; } = string.Empty;
    public string PinAlgorithmName { get; init; } = string.Empty;
    public string RecipeStatus { get; init; } = string.Empty;
    public string ProductCode { get; init; } = string.Empty;
    public string? BoardImagePath { get; init; }
    public string? PinImagePath { get; init; }
    public string? ResultPath { get; init; }
}
