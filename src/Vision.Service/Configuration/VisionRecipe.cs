using System.Text.Json;

namespace Vision.Service.Configuration;

public sealed record VisionRecipe
{
    public string RecipeName { get; init; } = "pcb-pin-row-demo";
    public CameraOptions Camera { get; init; } = new();
    public HoleDetectionOptions HoleDetection { get; init; } = new();
    public CalibrationOptions Calibration { get; init; } = new();
    public Pose2D ToolReferencePose { get; init; } = new();
    public ToleranceOptions Tolerances { get; init; } = new();

    public static async Task<VisionRecipe> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        await using var stream = File.OpenRead(fullPath);
        var recipe = await JsonSerializer.DeserializeAsync<VisionRecipe>(stream, RecipeJson.Options, cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidDataException($"Recipe '{fullPath}' is empty.");
        recipe.Validate();
        return recipe.ResolvePaths(Path.GetDirectoryName(fullPath)!);
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RecipeName))
        {
            throw new InvalidDataException("RecipeName is required.");
        }

        if (!Camera.Mode.Equals("File", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("This learning version supports Camera.Mode='File'. A real frame-grabber implementation is the next hardware step.");
        }

        if (HoleDetection.ExpectedCount < 2 || HoleDetection.ThresholdMin > HoleDetection.ThresholdMax ||
            HoleDetection.MinRadiusPx <= 0 || HoleDetection.MinRadiusPx >= HoleDetection.MaxRadiusPx)
        {
            throw new InvalidDataException("Hole detection parameters are invalid.");
        }
    }

    private VisionRecipe ResolvePaths(string recipeDirectory) => this with
    {
        Camera = Camera with { ImagePath = Resolve(recipeDirectory, Camera.ImagePath) },
        Calibration = Calibration with
        {
            PointsFile = Resolve(recipeDirectory, Calibration.PointsFile),
            ModelFile = Resolve(recipeDirectory, Calibration.ModelFile)
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
