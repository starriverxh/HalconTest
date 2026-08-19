using System.Text.Json;

namespace Vision.Service.Configuration;

public sealed record VisionRecipe
{
    public string RecipeName { get; init; } = "pcb-pin-row-demo";
    public int RecipeVersion { get; init; } = 1;
    public DateTimeOffset UpdatedUtc { get; init; } = DateTimeOffset.UnixEpoch;
    public CameraOptions Camera { get; init; } = new();
    public HoleDetectionOptions HoleDetection { get; init; } = new();
    public ImageQualityOptions ImageQuality { get; init; } = new();
    public LocalizationOptions Localization { get; init; } = new();
    public CalibrationOptions Calibration { get; init; } = new();
    public Pose2D ToolReferencePose { get; init; } = new();
    public ToleranceOptions Tolerances { get; init; } = new();

    public static async Task<VisionRecipe> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        await using var stream = File.OpenRead(fullPath);
        var recipe = await JsonSerializer.DeserializeAsync<VisionRecipe>(stream, RecipeJson.Options, cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidDataException($"Recipe '{fullPath}' is empty.");
        var resolved = recipe.ResolvePaths(Path.GetDirectoryName(fullPath)!);
        resolved.Validate();
        return resolved;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RecipeName))
        {
            throw new InvalidDataException("RecipeName is required.");
        }

        if (RecipeVersion < 1)
        {
            throw new InvalidDataException("RecipeVersion must be at least 1.");
        }

        if (!Camera.Mode.Equals("File", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("This learning version supports Camera.Mode='File'. A real frame-grabber implementation is the next hardware step.");
        }

        if (HoleDetection.ExpectedCount < 2 || HoleDetection.ThresholdMin > HoleDetection.ThresholdMax ||
            HoleDetection.ThresholdMin < 0 || HoleDetection.ThresholdMax > 255 ||
            HoleDetection.MinRadiusPx <= 0 || HoleDetection.MinRadiusPx >= HoleDetection.MaxRadiusPx ||
            HoleDetection.MinCircularity is < 0 or > 1)
        {
            throw new InvalidDataException("Hole detection parameters are invalid.");
        }

        HoleDetection.SearchRoi?.Validate("HoleDetection.SearchRoi");

        if (ImageQuality.MinimumMeanGray < 0 || ImageQuality.MaximumMeanGray > 255 ||
            ImageQuality.MinimumMeanGray >= ImageQuality.MaximumMeanGray ||
            ImageQuality.MinimumGrayDeviation < 0 || ImageQuality.MinimumMeanEdgeAmplitude < 0 ||
            ImageQuality.MaximumSaturatedFraction is < 0 or > 1)
        {
            throw new InvalidDataException("Image-quality limits are invalid.");
        }

        if (!Localization.Mode.Equals("BlobRow", StringComparison.OrdinalIgnoreCase) &&
            !Localization.Mode.Equals("ShapeModel", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Localization.Mode must be 'BlobRow' or 'ShapeModel'.");
        }

        if (Localization.Mode.Equals("ShapeModel", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(Localization.ModelFile) || !File.Exists(Localization.ModelFile))
            {
                throw new FileNotFoundException("The production shape model was not found.", Localization.ModelFile);
            }

            if (Localization.MinScore is < 0 or > 1 || Localization.AngleExtentDeg <= 0 ||
                Localization.NumLevels < 0 || Localization.Greediness is < 0 or > 1)
            {
                throw new InvalidDataException("Shape-model localization parameters are invalid.");
            }
        }
    }

    internal VisionRecipe ResolvePaths(string recipeDirectory) => this with
    {
        Camera = Camera with { ImagePath = Resolve(recipeDirectory, Camera.ImagePath) },
        Calibration = Calibration with
        {
            PointsFile = Resolve(recipeDirectory, Calibration.PointsFile),
            ModelFile = Resolve(recipeDirectory, Calibration.ModelFile)
        },
        Localization = Localization with
        {
            ModelFile = string.IsNullOrWhiteSpace(Localization.ModelFile)
                ? string.Empty
                : Resolve(recipeDirectory, Localization.ModelFile)
        }
    };

    private static string Resolve(string baseDirectory, string path) =>
        Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(baseDirectory, path));
}

public sealed record HoleDetectionOptions
{
    public double ThresholdMin { get; init; } = 180;
    public double ThresholdMax { get; init; } = 255;
    public double MinRadiusPx { get; init; } = 15;
    public double MaxRadiusPx { get; init; } = 22;
    public double MinCircularity { get; init; } = 0.85;
    public int ExpectedCount { get; init; } = 5;
    public RectangleRoiOptions? SearchRoi { get; init; }
}

public sealed record RectangleRoiOptions
{
    public double Row1 { get; init; }
    public double Column1 { get; init; }
    public double Row2 { get; init; }
    public double Column2 { get; init; }

    public void Validate(string name)
    {
        if (Row1 < 0 || Column1 < 0 || Row2 <= Row1 || Column2 <= Column1)
        {
            throw new InvalidDataException($"{name} must be a normalized, non-empty Rectangle1 ROI.");
        }
    }
}

public sealed record LocalizationOptions
{
    public string Mode { get; init; } = "BlobRow";
    public string ModelFile { get; init; } = string.Empty;
    public double AngleStartDeg { get; init; } = -10;
    public double AngleExtentDeg { get; init; } = 20;
    public double MinScore { get; init; } = 0.6;
    public int NumLevels { get; init; }
    public double Greediness { get; init; } = 0.9;
}

public sealed record ImageQualityOptions
{
    public double MinimumMeanGray { get; init; } = 20;
    public double MaximumMeanGray { get; init; } = 240;
    public double MinimumGrayDeviation { get; init; } = 5;
    public double MinimumMeanEdgeAmplitude { get; init; } = 1;
    public double SaturationGray { get; init; } = 250;
    public double MaximumSaturatedFraction { get; init; } = 0.25;
}

public sealed record CalibrationOptions
{
    public string PointsFile { get; init; } = string.Empty;
    public string ModelFile { get; init; } = string.Empty;
}

public sealed record Pose2D
{
    public double Xmm { get; init; }
    public double Ymm { get; init; }
    public double ThetaDeg { get; init; }
}

public sealed record ToleranceOptions
{
    public double MaxCorrectionMm { get; init; } = 2.0;
    public double MaxRotationDeg { get; init; } = 2.0;
    public double MaxCalibrationRmseMm { get; init; } = 0.010;
    public double MaxCalibrationErrorMm { get; init; } = 0.020;
    public double MinimumFeatureScore { get; init; } = 0.85;
}

internal static class RecipeJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}
