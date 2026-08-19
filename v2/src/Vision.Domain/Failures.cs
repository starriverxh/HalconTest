namespace VisionSystem.Domain;

public enum VisionFailureCode
{
    None = 0,
    RequestInvalid,
    RecipeInvalid,
    RecipeNotReleasedForMotion,
    RecipeVersionConflict,
    CameraNotReady,
    AcquisitionNotLive,
    AcquisitionTimeout,
    FrameSequenceInvalid,
    ImageTooDark,
    ImageTooBright,
    ImageLowContrast,
    ImageBlurred,
    ImageSaturated,
    BoardNotFound,
    BoardFeatureCountMismatch,
    BoardScoreTooLow,
    PinCameraNotCommissioned,
    PinRowNotFound,
    PinMissing,
    PinBent,
    PinPitchOutOfTolerance,
    PointCorrespondenceFailed,
    AlignmentResidualTooHigh,
    CalibrationMissing,
    CalibrationExpired,
    CalibrationResidualTooHigh,
    CorrectionOutOfRange,
    MotionNotStationary,
    SafetyInterlockOpen,
    CycleTimeout,
    ImageArchiveFailed,
    PersistenceFailed,
    InternalError
}

/// <summary>
/// Describes what a failure means to the production cycle. A rejected measurement
/// is not a service fault: the station remains healthy and can inspect the next part.
/// </summary>
public enum VisionFailureImpact
{
    RejectCycle,
    InhibitMotion,
    FaultSystem
}

public sealed record VisionFailure(
    VisionFailureCode Code,
    string Message,
    bool IsRecoverable,
    string Stage,
    VisionFailureImpact? Impact = null)
{
    public VisionFailureImpact EffectiveImpact => Impact ?? VisionFailurePolicy.Classify(Code);
}

public static class VisionFailurePolicy
{
    public static VisionFailureImpact Classify(VisionFailureCode code) => code switch
    {
        VisionFailureCode.None => VisionFailureImpact.InhibitMotion,

        // These describe the current part or the validity of its measurement. They
        // reject this cycle but do not make the vision service unhealthy.
        VisionFailureCode.ImageTooDark or
        VisionFailureCode.ImageTooBright or
        VisionFailureCode.ImageLowContrast or
        VisionFailureCode.ImageBlurred or
        VisionFailureCode.ImageSaturated or
        VisionFailureCode.BoardNotFound or
        VisionFailureCode.BoardFeatureCountMismatch or
        VisionFailureCode.BoardScoreTooLow or
        VisionFailureCode.PinRowNotFound or
        VisionFailureCode.PinMissing or
        VisionFailureCode.PinBent or
        VisionFailureCode.PinPitchOutOfTolerance or
        VisionFailureCode.PointCorrespondenceFailed or
        VisionFailureCode.AlignmentResidualTooHigh or
        VisionFailureCode.CorrectionOutOfRange => VisionFailureImpact.RejectCycle,

        // A simulated or uncommissioned tool pose may be useful for teaching, but
        // it is never evidence from which automatic machine motion can be allowed.
        VisionFailureCode.PinCameraNotCommissioned or
        VisionFailureCode.AcquisitionNotLive or
        VisionFailureCode.RecipeNotReleasedForMotion => VisionFailureImpact.InhibitMotion,

        // Configuration, equipment, calibration, persistence and unhandled errors
        // invalidate the station itself until the cause is corrected or reset.
        _ => VisionFailureImpact.FaultSystem
    };
}
