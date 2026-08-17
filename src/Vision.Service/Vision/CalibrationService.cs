using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using HalconDotNet;
using Vision.Service.Configuration;

namespace Vision.Service.Vision;

public sealed class CalibrationService
{
    public async Task<CalibrationModel> LoadOrCreateAsync(
        CalibrationOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(options.PointsFile))
        {
            throw new FileNotFoundException("Nine-point calibration CSV was not found.", options.PointsFile);
        }

        var bytes = await File.ReadAllBytesAsync(options.PointsFile, cancellationToken).ConfigureAwait(false);
        var fingerprint = Convert.ToHexString(SHA256.HashData(bytes));
        if (File.Exists(options.ModelFile))
        {
            await using var modelStream = File.OpenRead(options.ModelFile);
            var cached = await JsonSerializer.DeserializeAsync<CalibrationModel>(modelStream, RecipeJson.Options, cancellationToken)
                .ConfigureAwait(false);
            if (cached is { HomMat2D.Length: >= 6 } && cached.SourceFingerprint == fingerprint)
            {
                return cached;
            }
        }

        var points = ParsePoints(bytes);
        var fitPoints = points.Where(point => point.UseForFit).ToArray();
        var validationPoints = points.Where(point => !point.UseForFit).ToArray();
        if (fitPoints.Length < 3)
        {
            throw new InvalidDataException("Calibration requires at least three fit points.");
        }

        using var imageColumns = new HTuple(fitPoints.Select(point => point.Column).ToArray());
        using var imageRows = new HTuple(fitPoints.Select(point => point.Row).ToArray());
        using var machineX = new HTuple(fitPoints.Select(point => point.Xmm).ToArray());
        using var machineY = new HTuple(fitPoints.Select(point => point.Ymm).ToArray());
        HOperatorSet.VectorToHomMat2d(imageColumns, imageRows, machineX, machineY, out HTuple homMat2D);
        using (homMat2D)
        {
            var fitErrors = TransformAndCalculateErrors(homMat2D, fitPoints);
            var validationErrors = validationPoints.Length == 0
                ? []
                : TransformAndCalculateErrors(homMat2D, validationPoints);
            var model = new CalibrationModel(
                homMat2D.ToDArr(),
                Math.Sqrt(fitErrors.Sum(error => error * error) / fitErrors.Length),
                fitErrors.Max(),
                validationErrors.Length == 0 ? 0 : validationErrors.Max(),
                fitErrors,
                fingerprint,
                DateTimeOffset.UtcNow);

            var directory = Path.GetDirectoryName(options.ModelFile)
                ?? throw new InvalidOperationException("Calibration model directory is unavailable.");
            Directory.CreateDirectory(directory);
            await using var output = new FileStream(options.ModelFile, FileMode.Create, FileAccess.Write, FileShare.Read);
            await JsonSerializer.SerializeAsync(output, model, RecipeJson.Options, cancellationToken).ConfigureAwait(false);
            return model;
        }
    }

    private static double[] TransformAndCalculateErrors(HTuple homMat2D, IReadOnlyList<CalibrationPoint> points)
    {
        using var columns = new HTuple(points.Select(point => point.Column).ToArray());
        using var rows = new HTuple(points.Select(point => point.Row).ToArray());
        HOperatorSet.AffineTransPoint2d(homMat2D, columns, rows, out HTuple transformedX, out HTuple transformedY);
        using (transformedX)
        using (transformedY)
        {
            var xValues = transformedX.ToDArr();
            var yValues = transformedY.ToDArr();
            return Enumerable.Range(0, points.Count)
                .Select(index => Math.Sqrt(
                    Math.Pow(xValues[index] - points[index].Xmm, 2) +
                    Math.Pow(yValues[index] - points[index].Ymm, 2)))
                .ToArray();
        }
    }

    private static CalibrationPoint[] ParsePoints(byte[] bytes)
    {
        var lines = System.Text.Encoding.UTF8.GetString(bytes)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Skip(1).Select((line, index) =>
        {
            var values = line.Split(',', StringSplitOptions.TrimEntries);
            if (values.Length != 5)
            {
                throw new InvalidDataException($"Calibration CSV line {index + 2} must contain five columns.");
            }

            return new CalibrationPoint(
                ParseDouble(values[0]),
                ParseDouble(values[1]),
                ParseDouble(values[2]),
                ParseDouble(values[3]),
                bool.Parse(values[4]));
        }).ToArray();
    }

    private static double ParseDouble(string text) =>
        double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    private sealed record CalibrationPoint(double Row, double Column, double Xmm, double Ymm, bool UseForFit);
}
