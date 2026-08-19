namespace Vision.Service.Workbench;

public sealed record RectangleRoi(double Row1, double Column1, double Row2, double Column2)
{
    public RectangleRoi Normalize() => new(
        Math.Min(Row1, Row2),
        Math.Min(Column1, Column2),
        Math.Max(Row1, Row2),
        Math.Max(Column1, Column2));
}

public sealed record ProcessingRequest
{
    public string ImagePath { get; init; } = string.Empty;
    public RectangleRoi? Roi { get; init; }
    public double ThresholdMin { get; init; } = 0;
    public double ThresholdMax { get; init; } = 65;
    public double MinRadiusPx { get; init; } = 15;
    public double MaxRadiusPx { get; init; } = 22;
    public double MinCircularity { get; init; } = 0.85;
}

public sealed record WorkbenchImagePoint(
    double Row,
    double Column,
    double RadiusPx,
    double Score,
    double AreaPx);

public sealed record ProcessingResult(
    string ImagePath,
    string BinaryPreviewPath,
    int ImageWidth,
    int ImageHeight,
    RectangleRoi? Roi,
    int CandidateCount,
    int SelectedCount,
    double CandidateAreaPx,
    IReadOnlyList<WorkbenchImagePoint> Points,
    long ProcessingTimeMs);

public sealed record ShapeModelCreateRequest
{
    public string ImagePath { get; init; } = string.Empty;
    public RectangleRoi? Roi { get; init; }
    public string ModelName { get; init; } = "pcb-template";
    public double AngleStartDeg { get; init; } = -10;
    public double AngleExtentDeg { get; init; } = 20;
    public int Contrast { get; init; } = 20;
}

public sealed record ShapeModelCreateResult(
    string ModelPath,
    int ImageWidth,
    int ImageHeight,
    RectangleRoi Roi,
    double OriginRow,
    double OriginColumn,
    long ProcessingTimeMs);

public sealed record ShapeModelMatchRequest
{
    public string ImagePath { get; init; } = string.Empty;
    public string ModelPath { get; init; } = string.Empty;
    public double AngleStartDeg { get; init; } = -10;
    public double AngleExtentDeg { get; init; } = 20;
    public double MinScore { get; init; } = 0.5;
    public int NumMatches { get; init; } = 1;
    public double MaxOverlap { get; init; } = 0.5;
}

public sealed record ShapeModelMatch(double Row, double Column, double AngleDeg, double Score);

public sealed record ShapeModelMatchResult(
    string ImagePath,
    string ModelPath,
    int ImageWidth,
    int ImageHeight,
    IReadOnlyList<ShapeModelMatch> Matches,
    long ProcessingTimeMs);

public sealed record CalibrationPointInput
{
    public double Row { get; init; }
    public double Column { get; init; }
    public double MachineX { get; init; }
    public double MachineY { get; init; }
    public bool UseForFit { get; init; } = true;
}

public sealed record CalibrationFitRequest
{
    public IReadOnlyList<CalibrationPointInput> Points { get; init; } = [];
    public string ModelPath { get; init; } = string.Empty;
    public string? CsvPath { get; init; }
}

public sealed record CalibrationPointEvaluation(
    double Row,
    double Column,
    double MachineX,
    double MachineY,
    bool UseForFit,
    double PredictedX,
    double PredictedY,
    double ErrorMm);

public sealed record CalibrationFitResult(
    Vision.CalibrationModel Model,
    IReadOnlyList<CalibrationPointEvaluation> Points,
    string ModelPath,
    string? CsvPath);

public sealed record RecipeSaveRequest
{
    public int? ExpectedVersion { get; init; }
    public RectangleRoi? Roi { get; init; }
    public double ThresholdMin { get; init; }
    public double ThresholdMax { get; init; } = 65;
    public double MinRadiusPx { get; init; } = 15;
    public double MaxRadiusPx { get; init; } = 22;
    public double MinCircularity { get; init; } = 0.85;
    public int ExpectedCount { get; init; } = 5;
    public bool UseShapeModel { get; init; }
    public string ShapeModelPath { get; init; } = string.Empty;
    public double AngleStartDeg { get; init; } = -10;
    public double AngleExtentDeg { get; init; } = 20;
    public double MatchMinScore { get; init; } = 0.6;
}

public sealed record RecipeSaveResult(
    string RecipePath,
    string RecipeName,
    int RecipeVersion,
    DateTimeOffset UpdatedUtc,
    string BackupPath,
    bool RequiresServiceRestart,
    string ActivationMessage);


public sealed record RecipeGetRequest;

public sealed record RecipeSnapshotResult(
    string RecipePath,
    string RecipeName,
    int RecipeVersion,
    DateTimeOffset UpdatedUtc,
    RectangleRoi? Roi,
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
