namespace Vision.Desktop.Models;

public sealed record InspectionViewData(
    bool IsOk,
    double X,
    double Y,
    double Theta,
    double Score,
    string? ImagePath,
    string? ResultPath,
    long DurationMs,
    IReadOnlyList<string> FailureReasons);
