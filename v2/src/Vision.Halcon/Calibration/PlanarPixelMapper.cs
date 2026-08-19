using System.Text.Json;
using HalconDotNet;
using VisionSystem.Domain;

namespace VisionSystem.Halcon.Calibration;

public sealed record PlanarCalibrationModel
{
    public string SchemaVersion { get; init; } = "2.0";
    public string SourceFrame { get; init; } = string.Empty;
    public string TargetFrame { get; init; } = "machine";
    public double[] HomMat2D { get; init; } = [];
    public double RmseMm { get; init; }
    public double MaximumValidationErrorMm { get; init; }
    public int ImageWidth { get; init; }
    public int ImageHeight { get; init; }
    public double WorkPlaneZmm { get; init; }
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UnixEpoch;
    public string CameraSerial { get; init; } = "OFFLINE";
    public string Notes { get; init; } = string.Empty;
}

internal sealed class PlanarPixelMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly object _sync = new();
    private string? _loadedPath;
    private DateTime _loadedWriteUtc;
    private PlanarCalibrationModel? _model;

    public PlanarCalibrationModel Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new FileNotFoundException("Planar pixel-to-machine calibration was not found.", path);
        }

        var fullPath = Path.GetFullPath(path);
        var writeUtc = File.GetLastWriteTimeUtc(fullPath);
        lock (_sync)
        {
            if (_model is not null && string.Equals(_loadedPath, fullPath, StringComparison.OrdinalIgnoreCase) &&
                _loadedWriteUtc == writeUtc)
            {
                return _model;
            }

            var parsed = JsonSerializer.Deserialize<PlanarCalibrationModel>(
                File.ReadAllText(fullPath), JsonOptions)
                ?? throw new InvalidDataException($"Calibration '{fullPath}' is empty.");
            Validate(parsed, fullPath);
            _loadedPath = fullPath;
            _loadedWriteUtc = writeUtc;
            _model = parsed;
            return parsed;
        }
    }

    public IReadOnlyList<IdentifiedMachinePoint> Transform(
        PlanarCalibrationModel model,
        IReadOnlyList<PixelPoint> pixels,
        IReadOnlyList<string> ids)
    {
        if (pixels.Count != ids.Count)
        {
            throw new InvalidDataException(
                $"Feature correspondence has {pixels.Count} points but {ids.Count} identifiers.");
        }

        if (pixels.Count == 0)
        {
            return [];
        }

        using var homMat2D = new HTuple(model.HomMat2D);
        using var columns = new HTuple(pixels.Select(point => point.Column).ToArray());
        using var rows = new HTuple(pixels.Select(point => point.Row).ToArray());
        HOperatorSet.AffineTransPoint2d(homMat2D, columns, rows, out HTuple x, out HTuple y);
        using (x)
        using (y)
        {
            var xValues = x.ToDArr();
            var yValues = y.ToDArr();
            return Enumerable.Range(0, pixels.Count)
                .Select(index => new IdentifiedMachinePoint(
                    ids[index],
                    new MachinePoint(xValues[index], yValues[index]),
                    pixels[index].Score))
                .ToArray();
        }
    }

    public static IReadOnlyList<VisionFailure> ValidateForRecipe(
        PlanarCalibrationModel model,
        CalibrationRecipe recipe,
        int imageWidth,
        int imageHeight,
        DateTimeOffset now,
        string stage)
    {
        var failures = new List<VisionFailure>();
        if (model.ImageWidth > 0 && model.ImageHeight > 0 &&
            (model.ImageWidth != imageWidth || model.ImageHeight != imageHeight))
        {
            failures.Add(new VisionFailure(
                VisionFailureCode.CalibrationResidualTooHigh,
                $"Calibration image size {model.ImageWidth}x{model.ImageHeight} does not match frame {imageWidth}x{imageHeight}.",
                false,
                stage));
        }

        if (model.RmseMm > recipe.MaximumRmseMm ||
            model.MaximumValidationErrorMm > recipe.MaximumValidationErrorMm)
        {
            failures.Add(new VisionFailure(
                VisionFailureCode.CalibrationResidualTooHigh,
                $"Calibration RMSE/max validation error is {model.RmseMm:F4}/{model.MaximumValidationErrorMm:F4} mm.",
                false,
                stage));
        }

        if (recipe.MaximumAgeDays > 0 && model.CreatedUtc != DateTimeOffset.UnixEpoch &&
            now - model.CreatedUtc > TimeSpan.FromDays(recipe.MaximumAgeDays))
        {
            failures.Add(new VisionFailure(
                VisionFailureCode.CalibrationExpired,
                $"Calibration is older than {recipe.MaximumAgeDays} days.",
                false,
                stage));
        }

        return failures;
    }

    private static void Validate(PlanarCalibrationModel model, string path)
    {
        if (model.HomMat2D.Length != 6 || model.HomMat2D.Any(value => !double.IsFinite(value)))
        {
            throw new InvalidDataException($"Calibration '{path}' must contain six finite HomMat2D values.");
        }

        if (model.RmseMm < 0 || model.MaximumValidationErrorMm < 0)
        {
            throw new InvalidDataException($"Calibration '{path}' contains negative residuals.");
        }
    }
}
