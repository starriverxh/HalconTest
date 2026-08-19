namespace VisionSystem.Domain;

public enum CameraMode
{
    File,
    GigEVision2,
    Usb3Vision
}

public enum BoardLocatorMode
{
    BlobRow,
    ShapeModelThenBlob
}

public sealed record CameraRecipe
{
    public CameraMode Mode { get; init; } = CameraMode.File;
    public string Device { get; init; } = string.Empty;
    public string OfflineImagePath { get; init; } = string.Empty;
    public double ExposureMicroseconds { get; init; } = 1500;
    public double GainDb { get; init; }
    public int AcquisitionTimeoutMs { get; init; } = 3000;
    public string TriggerMode { get; init; } = "Off";
}

public sealed record ImageQualityRecipe
{
    public double MinimumMeanGray { get; init; } = 20;
    public double MaximumMeanGray { get; init; } = 240;
    public double MinimumGrayDeviation { get; init; } = 5;
    public double MinimumMeanEdgeAmplitude { get; init; } = 1;
    public double SaturationGray { get; init; } = 250;
    public double MaximumSaturatedFraction { get; init; } = 0.25;
}

public sealed record BlobFeatureRecipe
{
    public double ThresholdMin { get; init; }
    public double ThresholdMax { get; init; } = 65;
    public double MinimumAreaPx { get; init; } = 700;
    public double MaximumAreaPx { get; init; } = 1600;
    public double MinimumCircularity { get; init; } = 0.85;
    public int ExpectedCount { get; init; } = 5;
}

public sealed record ShapeModelRecipe
{
    public string ModelPath { get; init; } = string.Empty;
    public string MetadataPath { get; init; } = string.Empty;
    public double AngleStartDeg { get; init; } = -10;
    public double AngleExtentDeg { get; init; } = 20;
    public double MinimumScore { get; init; } = 0.65;
    public double MaximumOverlap { get; init; } = 0.5;
    public int PyramidLevels { get; init; }
    public double Greediness { get; init; } = 0.9;
}

public sealed record BoardVisionRecipe
{
    public BoardLocatorMode LocatorMode { get; init; } = BoardLocatorMode.BlobRow;
    public RectangleRoi SearchRoi { get; init; } = new();
    public ImageQualityRecipe ImageQuality { get; init; } = new();
    public BlobFeatureRecipe HoleFeature { get; init; } = new();
    public ShapeModelRecipe ShapeModel { get; init; } = new();
    public IReadOnlyList<string> FeatureIds { get; init; } = ["P1", "P2", "P3", "P4", "P5"];
}

public sealed record PinVisionRecipe
{
    public bool Enabled { get; init; }
    public bool AllowEngineeringSimulation { get; init; } = true;
    public int ExpectedPinCount { get; init; } = 5;
    public double MaximumPitchErrorMm { get; init; } = 0.02;
    public double MaximumStraightnessErrorMm { get; init; } = 0.02;
    public Pose2D EngineeringReferencePose { get; init; } = new();
    public IReadOnlyList<IdentifiedMachinePoint> EngineeringReferencePins { get; init; } = [];
}

public sealed record CalibrationRecipe
{
    public string BoardCameraModelPath { get; init; } = string.Empty;
    public string BoardToMachineTransformPath { get; init; } = string.Empty;
    public string PinCameraModelPath { get; init; } = string.Empty;
    public string PinToMachineTransformPath { get; init; } = string.Empty;
    public string RotationCenterModelPath { get; init; } = string.Empty;
    public double MaximumRmseMm { get; init; } = 0.01;
    public double MaximumValidationErrorMm { get; init; } = 0.02;
    public int MaximumAgeDays { get; init; } = 30;
}

public sealed record CorrectionToleranceRecipe
{
    public double MaximumSafeCommandXmm { get; init; } = 2.0;
    public double MaximumSafeCommandYmm { get; init; } = 2.0;
    public double MaximumSafeCommandThetaDeg { get; init; } = 2.0;
    public double MaximumPredictedResidualMm { get; init; } = 0.05;
}

public sealed record MachineKinematicsRecipe
{
    public MachinePoint RotationCenterMachine { get; init; } = new();
    public string CommandConvention { get; init; } = "RotateAboutPivotThenTranslate";
}

public static class MachineCommandConventions
{
    public const string RotateAboutPivotThenTranslate = "RotateAboutPivotThenTranslate";
}

public sealed record ProductionPolicyRecipe
{
    public bool RequireBoardCamera { get; init; } = true;
    public bool RequirePinCamera { get; init; } = true;
    public bool RequireMotionStationary { get; init; } = true;
    public bool RequireSafetyInterlock { get; init; } = true;
    public bool SaveAllImages { get; init; } = true;
    public int CycleTimeoutMs { get; init; } = 5000;
}

public sealed record VisionRecipe
{
    public string SchemaVersion { get; init; } = "2.0";
    public string RecipeId { get; init; } = "pcb-pin-row-demo-v2";
    public string ProductCode { get; init; } = "TRAINING-PCB";
    public int Version { get; init; } = 1;
    public string Status { get; init; } = "Engineering";
    public DateTimeOffset UpdatedUtc { get; init; } = DateTimeOffset.UnixEpoch;
    public CameraRecipe BoardCamera { get; init; } = new();
    public CameraRecipe PinCamera { get; init; } = new();
    public BoardVisionRecipe Board { get; init; } = new();
    public PinVisionRecipe Pins { get; init; } = new();
    public CalibrationRecipe Calibration { get; init; } = new();
    public CorrectionToleranceRecipe CorrectionTolerance { get; init; } = new();
    public MachineKinematicsRecipe MachineKinematics { get; init; } = new();
    public ProductionPolicyRecipe ProductionPolicy { get; init; } = new();
}

public static class RecipeLifecycleStatuses
{
    public const string Engineering = "Engineering";
    public const string Draft = "Draft";
    public const string Validated = "Validated";
    public const string Approved = "Approved";
    public const string Published = "Published";
    public const string Active = "Active";
    public const string Retired = "Retired";

    public static bool IsKnown(string? status) => status is not null &&
        Known.Contains(status);

    public static bool AllowsAutomaticMotion(string? status) =>
        string.Equals(status, Published, StringComparison.Ordinal) ||
        string.Equals(status, Active, StringComparison.Ordinal);

    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        Engineering,
        Draft,
        Validated,
        Approved,
        Published,
        Active,
        Retired
    };
}
