using CommunityToolkit.Mvvm.ComponentModel;

namespace Vision.Desktop.Models;

public sealed record RectangleRoiData(double Row1, double Column1, double Row2, double Column2)
{
    public RectangleRoiData Normalize() => new(
        Math.Min(Row1, Row2),
        Math.Min(Column1, Column2),
        Math.Max(Row1, Row2),
        Math.Max(Column1, Column2));
}

public sealed record ImageCoordinate(double Row, double Column);

public sealed record VisionOverlay(
    double Row,
    double Column,
    double Radius,
    string Label,
    string Color = "#22C55E",
    string Kind = "circle");

public sealed record ProcessingViewData(
    string ImagePath,
    string BinaryPreviewPath,
    int ImageWidth,
    int ImageHeight,
    RectangleRoiData? Roi,
    int CandidateCount,
    int SelectedCount,
    double CandidateAreaPx,
    IReadOnlyList<WorkbenchPointViewData> Points,
    long ProcessingTimeMs);

public sealed record WorkbenchPointViewData(
    double Row,
    double Column,
    double RadiusPx,
    double Score,
    double AreaPx);

public sealed record ShapeModelCreateViewData(
    string ModelPath,
    int ImageWidth,
    int ImageHeight,
    RectangleRoiData Roi,
    double OriginRow,
    double OriginColumn,
    long ProcessingTimeMs);

public sealed record ShapeModelMatchViewData(
    string ImagePath,
    string ModelPath,
    int ImageWidth,
    int ImageHeight,
    IReadOnlyList<ShapeModelInstanceViewData> Matches,
    long ProcessingTimeMs);

public sealed record ShapeModelInstanceViewData(double Row, double Column, double AngleDeg, double Score);

public sealed record CalibrationFitViewData(
    CalibrationModelViewData Model,
    IReadOnlyList<CalibrationPointEvaluationViewData> Points,
    string ModelPath,
    string? CsvPath);

public sealed record CalibrationModelViewData(
    double[] HomMat2D,
    double RmseMm,
    double MaxFitErrorMm,
    double MaxValidationErrorMm,
    double[] FitPointErrorsMm,
    string SourceFingerprint,
    DateTimeOffset CreatedUtc);

public sealed record CalibrationPointEvaluationViewData(
    double Row,
    double Column,
    double MachineX,
    double MachineY,
    bool UseForFit,
    double PredictedX,
    double PredictedY,
    double ErrorMm);

public sealed record RecipeSaveViewData(
    string RecipePath,
    string RecipeName,
    int RecipeVersion,
    DateTimeOffset UpdatedUtc,
    string BackupPath,
    bool RequiresServiceRestart,
    string ActivationMessage);

public sealed record RecipeSnapshotViewData(
    string RecipePath,
    string RecipeName,
    int RecipeVersion,
    DateTimeOffset UpdatedUtc,
    RectangleRoiData? Roi,
    double ThresholdMin,
    double ThresholdMax,
    double MinRadiusPx,
    double MaxRadiusPx,
    double MinCircularity,
    int ExpectedCount,
    bool UseShapeModel,
    string ShapeModelPath,
    double AngleStartDeg,
    double AngleExtentDeg,
    double MatchMinScore);

public partial class CalibrationPointRow : ObservableObject
{
    [ObservableProperty]
    private double _row;

    [ObservableProperty]
    private double _column;

    [ObservableProperty]
    private double _machineX;

    [ObservableProperty]
    private double _machineY;

    [ObservableProperty]
    private bool _useForFit = true;

    [ObservableProperty]
    private double? _predictedX;

    [ObservableProperty]
    private double? _predictedY;

    [ObservableProperty]
    private double? _errorMm;
}
