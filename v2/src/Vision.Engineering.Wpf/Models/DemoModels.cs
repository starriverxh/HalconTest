namespace VisionSystem.Engineering.Wpf.Models;

public sealed record ProductionResult(
    string CycleId,
    string Disposition,
    double Xmm,
    double Ymm,
    double ThetaDeg,
    double Score,
    long DurationMs,
    string FailureCode,
    bool RuntimeConnected = false,
    bool MotionAllowed = false,
    string SourceDescription = "教学内存Gateway（未调用Runtime）",
    IReadOnlyList<string>? StateHistory = null);

public sealed record TemplateValidationResult(
    int Total,
    int Located,
    int FalseAccept,
    int FalseReject,
    double MinimumOkScore,
    double MaximumNgScore,
    long P99Milliseconds);

public sealed record CalibrationPointResult(
    string Id,
    double Row,
    double Column,
    double MachineX,
    double MachineY,
    double ErrorMm,
    bool IsValidation);

public sealed record CalibrationFitResult(
    double RmseMm,
    double MaximumFitErrorMm,
    double MaximumValidationErrorMm,
    IReadOnlyList<CalibrationPointResult> Points);

public sealed record RegressionResult(
    int Total,
    int Passed,
    int FalseAccept,
    int FalseReject,
    double MaximumPositionErrorMm,
    long P99Milliseconds,
    string ReportId);

public sealed record TraceRecord(
    string CycleId,
    DateTimeOffset Timestamp,
    string ProductId,
    string Disposition,
    string RecipeVersion,
    string FailureCode,
    long DurationMs);

public sealed record DeviceHealth(
    string Name,
    string State,
    string Detail,
    bool IsBlocking);
