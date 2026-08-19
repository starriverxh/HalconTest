namespace Vision.Service.Vision;

public sealed record ImagePoint(double Row, double Column, double RadiusPx, double Score);

public sealed record MachinePoint(double Xmm, double Ymm);

public sealed record BoardPose(double Xmm, double Ymm, double ThetaDeg, double Score);

public sealed record ToolPose(double Xmm, double Ymm, double ThetaDeg, double Score, string Source);

public sealed record XYThetaCorrection(double Xmm, double Ymm, double ThetaDeg);

public sealed record ImageQualityResult(
    bool IsAcceptable,
    double MeanGray,
    double GrayDeviation,
    double MeanEdgeAmplitude,
    double SaturatedFraction,
    IReadOnlyList<string> FailureReasons);

public sealed record CalibrationModel(
    double[] HomMat2D,
    double RmseMm,
    double MaxFitErrorMm,
    double MaxValidationErrorMm,
    double[] FitPointErrorsMm,
    string SourceFingerprint,
    DateTimeOffset CreatedUtc);

public sealed record VisionResult(
    bool IsOk,
    double X,
    double Y,
    double Theta,
    double Score,
    string? ImagePath = null,
    IReadOnlyDictionary<string, object?>? Details = null,
    IReadOnlyList<string>? FailureReasons = null);
