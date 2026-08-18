using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using HalconDotNet;
using Vision.Service.Configuration;
using Vision.Service.Workbench;

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
        return (await CalibrateCoreAsync(points, options.ModelFile, fingerprint, cancellationToken).ConfigureAwait(false)).Model;
    }

    public Task<CalibrationFitResult> CalibrateAsync(
        IReadOnlyList<CalibrationPointInput> points,
        string modelPath,
        CancellationToken cancellationToken = default)
    {
        var fingerprintBytes = JsonSerializer.SerializeToUtf8Bytes(points, RecipeJson.Options);
        var fingerprint = Convert.ToHexString(SHA256.HashData(fingerprintBytes));
        return CalibrateCoreAsync(points, modelPath, fingerprint, cancellationToken);
    }

    private static async Task<CalibrationFitResult> CalibrateCoreAsync(
        IReadOnlyList<CalibrationPointInput> points,
        string modelPath,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fitPoints = points.Where(point => point.UseForFit).ToArray();
        var validationPoints = points.Where(point => !point.UseForFit).ToArray();
        if (fitPoints.Length < 3)
        {
            throw new InvalidDataException("Calibration requires at least three fit points.");
        }

        if (!ContainsNonCollinearImagePoints(fitPoints))
        {
            throw new InvalidDataException("Calibration fit points are collinear; distribute points across the full image area.");
        }

        using var imageColumns = new HTuple(fitPoints.Select(point => point.Column).ToArray());
        using var imageRows = new HTuple(fitPoints.Select(point => point.Row).ToArray());
        using var machineX = new HTuple(fitPoints.Select(point => point.MachineX).ToArray());
        using var machineY = new HTuple(fitPoints.Select(point => point.MachineY).ToArray());
        HOperatorSet.VectorToHomMat2d(imageColumns, imageRows, machineX, machineY, out HTuple homMat2D);
        using (homMat2D)
        {
            var evaluations = TransformAndEvaluate(homMat2D, points);
            var fitErrors = evaluations.Where(point => point.UseForFit).Select(point => point.ErrorMm).ToArray();
            var validationErrors = evaluations.Where(point => !point.UseForFit).Select(point => point.ErrorMm).ToArray();
            var model = new CalibrationModel(
                homMat2D.ToDArr(),
                Math.Sqrt(fitErrors.Sum(error => error * error) / fitErrors.Length),
                fitErrors.Max(),
                validationErrors.Length == 0 ? 0 : validationErrors.Max(),
                fitErrors,
                fingerprint,
                DateTimeOffset.UtcNow);

            var fullModelPath = Path.GetFullPath(modelPath);
            var directory = Path.GetDirectoryName(fullModelPath)
                ?? throw new InvalidOperationException("Calibration model directory is unavailable.");
            Directory.CreateDirectory(directory);
            await using var output = new FileStream(fullModelPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            await JsonSerializer.SerializeAsync(output, model, RecipeJson.Options, cancellationToken).ConfigureAwait(false);
            return new CalibrationFitResult(model, evaluations, fullModelPath, null);
        }
    }

    private static IReadOnlyList<CalibrationPointEvaluation> TransformAndEvaluate(
        HTuple homMat2D,
        IReadOnlyList<CalibrationPointInput> points)
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
                .Select(index => new CalibrationPointEvaluation(
                    points[index].Row,
                    points[index].Column,
                    points[index].MachineX,
                    points[index].MachineY,
                    points[index].UseForFit,
                    xValues[index],
                    yValues[index],
                    Math.Sqrt(
                        Math.Pow(xValues[index] - points[index].MachineX, 2) +
                        Math.Pow(yValues[index] - points[index].MachineY, 2))))
                .ToArray();
        }
    }

    private static CalibrationPointInput[] ParsePoints(byte[] bytes)
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

            return new CalibrationPointInput
            {
                Row = ParseDouble(values[0]),
                Column = ParseDouble(values[1]),
                MachineX = ParseDouble(values[2]),
                MachineY = ParseDouble(values[3]),
                UseForFit = bool.Parse(values[4])
            };
        }).ToArray();
    }

    private static double ParseDouble(string text) =>
        double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static bool ContainsNonCollinearImagePoints(IReadOnlyList<CalibrationPointInput> points)
    {
        for (var first = 0; first < points.Count - 2; first++)
        {
            for (var second = first + 1; second < points.Count - 1; second++)
            {
                for (var third = second + 1; third < points.Count; third++)
                {
                    var twiceArea =
                        (points[second].Column - points[first].Column) * (points[third].Row - points[first].Row) -
                        (points[third].Column - points[first].Column) * (points[second].Row - points[first].Row);
                    if (Math.Abs(twiceArea) > 1e-6)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}
