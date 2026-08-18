using System.Diagnostics;
using System.Globalization;
using HalconDotNet;
using Vision.Service.Vision;

namespace Vision.Service.Workbench;

/// <summary>
/// HALCON operator workbench used by the engineering UI. These methods execute the
/// same operators that are later frozen into a production recipe; the UI itself
/// never performs vision calculations.
/// </summary>
public sealed class VisionWorkbenchService(string runtimeDirectory) : IVisionWorkbench
{
    private readonly string _previewDirectory = CreateDirectory(runtimeDirectory, "previews");
    private readonly string _modelDirectory = CreateDirectory(runtimeDirectory, "models");
    private readonly string _calibrationDirectory = CreateDirectory(runtimeDirectory, "calibration");

    public Task<ProcessingResult> PreviewProcessingAsync(
        ProcessingRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateImage(request.ImagePath);
        ValidateProcessingParameters(request);
        var stopwatch = Stopwatch.StartNew();

        HOperatorSet.ReadImage(out HObject image, request.ImagePath);
        using (image)
        using (var grayImage = ToGray(image))
        {
            HOperatorSet.GetImageSize(grayImage, out HTuple widthTuple, out HTuple heightTuple);
            using (widthTuple)
            using (heightTuple)
            {
                var width = widthTuple.I;
                var height = heightTuple.I;
                var roi = NormalizeAndClamp(request.Roi, width, height);
                HObject analysisImage = grayImage;
                HObject? roiRegion = null;
                HObject? reducedImage = null;
                try
                {
                    if (roi is not null)
                    {
                        HOperatorSet.GenRectangle1(
                            out HObject generatedRoi,
                            roi.Row1,
                            roi.Column1,
                            roi.Row2,
                            roi.Column2);
                        roiRegion = generatedRoi;
                        HOperatorSet.ReduceDomain(grayImage, generatedRoi, out HObject reduced);
                        reducedImage = reduced;
                        analysisImage = reduced;
                    }

                    HOperatorSet.Threshold(
                        analysisImage,
                        out HObject thresholdRegion,
                        request.ThresholdMin,
                        request.ThresholdMax);
                    using (thresholdRegion)
                    {
                        HOperatorSet.AreaCenter(thresholdRegion, out HTuple candidateAreas, out HTuple candidateRows, out HTuple candidateColumns);
                        using (candidateAreas)
                        using (candidateRows)
                        using (candidateColumns)
                        {
                            var candidateArea = candidateAreas.Length == 0 ? 0 : candidateAreas.ToDArr().Sum();
                            HOperatorSet.Connection(thresholdRegion, out HObject connectedRegions);
                            using (connectedRegions)
                            {
                                HOperatorSet.CountObj(connectedRegions, out HTuple candidateCountTuple);
                                using (candidateCountTuple)
                                {
                                    var minArea = Math.PI * request.MinRadiusPx * request.MinRadiusPx;
                                    var maxArea = Math.PI * request.MaxRadiusPx * request.MaxRadiusPx;
                                    using var features = new HTuple(new[] { "area", "circularity" });
                                    using var minimums = new HTuple(new[] { minArea, request.MinCircularity });
                                    using var maximums = new HTuple(new[] { maxArea, 1.0 });
                                    HOperatorSet.SelectShape(
                                        connectedRegions,
                                        out HObject selectedRegions,
                                        features,
                                        "and",
                                        minimums,
                                        maximums);
                                    using (selectedRegions)
                                    {
                                        var points = ReadRegionPoints(selectedRegions);
                                        HOperatorSet.RegionToBin(selectedRegions, out HObject binaryImage, 255, 0, width, height);
                                        using (binaryImage)
                                        {
                                            var previewPath = Path.Combine(
                                                _previewDirectory,
                                                $"processing_{DateTime.UtcNow:yyyyMMdd_HHmmssfff}_{Guid.NewGuid():N}.png");
                                            HOperatorSet.WriteImage(binaryImage, "png", 0, previewPath);
                                            stopwatch.Stop();
                                            return Task.FromResult(new ProcessingResult(
                                                Path.GetFullPath(request.ImagePath),
                                                previewPath,
                                                width,
                                                height,
                                                roi,
                                                candidateCountTuple.I,
                                                points.Count,
                                                candidateArea,
                                                points,
                                                stopwatch.ElapsedMilliseconds));
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                finally
                {
                    reducedImage?.Dispose();
                    roiRegion?.Dispose();
                }
            }
        }
    }

    public Task<ShapeModelCreateResult> CreateShapeModelAsync(
        ShapeModelCreateRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateImage(request.ImagePath);
        if (request.Roi is null)
        {
            throw new InvalidDataException("Draw a rectangular ROI before creating a shape model.");
        }

        if (request.Contrast is < 1 or > 255 || request.AngleExtentDeg <= 0)
        {
            throw new InvalidDataException("Shape-model contrast or angle range is invalid.");
        }

        var stopwatch = Stopwatch.StartNew();
        HOperatorSet.ReadImage(out HObject image, request.ImagePath);
        using (image)
        using (var grayImage = ToGray(image))
        {
            HOperatorSet.GetImageSize(grayImage, out HTuple widthTuple, out HTuple heightTuple);
            using (widthTuple)
            using (heightTuple)
            {
                var width = widthTuple.I;
                var height = heightTuple.I;
                var roi = NormalizeAndClamp(request.Roi, width, height)
                    ?? throw new InvalidDataException("ROI is empty.");
                if (roi.Row2 - roi.Row1 < 5 || roi.Column2 - roi.Column1 < 5)
                {
                    throw new InvalidDataException("ROI must be at least 5 x 5 pixels.");
                }

                HOperatorSet.GenRectangle1(out HObject roiRegion, roi.Row1, roi.Column1, roi.Row2, roi.Column2);
                using (roiRegion)
                {
                    HOperatorSet.ReduceDomain(grayImage, roiRegion, out HObject reducedImage);
                    using (reducedImage)
                    {
                        HOperatorSet.CreateShapeModel(
                            reducedImage,
                            "auto",
                            DegreesToRadians(request.AngleStartDeg),
                            DegreesToRadians(request.AngleExtentDeg),
                            "auto",
                            "auto",
                            "use_polarity",
                            request.Contrast,
                            "auto",
                            out HTuple modelId);
                        using (modelId)
                        {
                            try
                            {
                                var modelPath = Path.Combine(_modelDirectory, MakeSafeModelName(request.ModelName) + ".shm");
                                HOperatorSet.WriteShapeModel(modelId, modelPath);
                                HOperatorSet.GetShapeModelOrigin(modelId, out HTuple originRowTuple, out HTuple originColumnTuple);
                                using (originRowTuple)
                                using (originColumnTuple)
                                {
                                    stopwatch.Stop();
                                    return Task.FromResult(new ShapeModelCreateResult(
                                        modelPath,
                                        width,
                                        height,
                                        roi,
                                        originRowTuple.D,
                                        originColumnTuple.D,
                                        stopwatch.ElapsedMilliseconds));
                                }
                            }
                            finally
                            {
                                HOperatorSet.ClearShapeModel(modelId);
                            }
                        }
                    }
                }
            }
        }
    }

    public Task<ShapeModelMatchResult> MatchShapeModelAsync(
        ShapeModelMatchRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateImage(request.ImagePath);
        var modelPath = Path.GetFullPath(request.ModelPath);
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("HALCON shape-model file was not found.", modelPath);
        }

        if (request.MinScore is < 0 or > 1 || request.NumMatches < 0 || request.MaxOverlap is < 0 or > 1)
        {
            throw new InvalidDataException("Shape-model matching parameters are invalid.");
        }

        var stopwatch = Stopwatch.StartNew();
        HOperatorSet.ReadImage(out HObject image, request.ImagePath);
        using (image)
        using (var grayImage = ToGray(image))
        {
            HOperatorSet.GetImageSize(grayImage, out HTuple widthTuple, out HTuple heightTuple);
            using (widthTuple)
            using (heightTuple)
            {
                HOperatorSet.ReadShapeModel(modelPath, out HTuple modelId);
                using (modelId)
                {
                    try
                    {
                        HOperatorSet.FindShapeModel(
                            grayImage,
                            modelId,
                            DegreesToRadians(request.AngleStartDeg),
                            DegreesToRadians(request.AngleExtentDeg),
                            request.MinScore,
                            request.NumMatches,
                            request.MaxOverlap,
                            "least_squares",
                            0,
                            0.9,
                            out HTuple rows,
                            out HTuple columns,
                            out HTuple angles,
                            out HTuple scores);
                        using (rows)
                        using (columns)
                        using (angles)
                        using (scores)
                        {
                            var rowValues = rows.ToDArr();
                            var columnValues = columns.ToDArr();
                            var angleValues = angles.ToDArr();
                            var scoreValues = scores.ToDArr();
                            var matches = Enumerable.Range(0, scoreValues.Length)
                                .Select(index => new ShapeModelMatch(
                                    rowValues[index],
                                    columnValues[index],
                                    angleValues[index] * 180.0 / Math.PI,
                                    scoreValues[index]))
                                .ToArray();
                            stopwatch.Stop();
                            return Task.FromResult(new ShapeModelMatchResult(
                                Path.GetFullPath(request.ImagePath),
                                modelPath,
                                widthTuple.I,
                                heightTuple.I,
                                matches,
                                stopwatch.ElapsedMilliseconds));
                        }
                    }
                    finally
                    {
                        HOperatorSet.ClearShapeModel(modelId);
                    }
                }
            }
        }
    }

    public async Task<CalibrationFitResult> FitCalibrationAsync(
        CalibrationFitRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Points.Count < 3)
        {
            throw new InvalidDataException("Calibration requires at least three points.");
        }

        var modelPath = string.IsNullOrWhiteSpace(request.ModelPath)
            ? Path.Combine(_calibrationDirectory, "camera-to-machine.json")
            : Path.GetFullPath(request.ModelPath);
        var result = await new CalibrationService()
            .CalibrateAsync(request.Points, modelPath, cancellationToken)
            .ConfigureAwait(false);

        string? csvPath = null;
        if (!string.IsNullOrWhiteSpace(request.CsvPath))
        {
            csvPath = Path.GetFullPath(request.CsvPath);
            var directory = Path.GetDirectoryName(csvPath)
                ?? throw new InvalidOperationException("Calibration CSV directory is unavailable.");
            Directory.CreateDirectory(directory);
            var lines = new[] { "row,column,machine_x_mm,machine_y_mm,use_for_fit" }
                .Concat(request.Points.Select(point => string.Join(",",
                    point.Row.ToString("R", CultureInfo.InvariantCulture),
                    point.Column.ToString("R", CultureInfo.InvariantCulture),
                    point.MachineX.ToString("R", CultureInfo.InvariantCulture),
                    point.MachineY.ToString("R", CultureInfo.InvariantCulture),
                    point.UseForFit.ToString(CultureInfo.InvariantCulture))));
            await File.WriteAllLinesAsync(csvPath, lines, cancellationToken).ConfigureAwait(false);
        }

        return result with { CsvPath = csvPath };
    }

    private static HObject ToGray(HObject image)
    {
        HOperatorSet.CountChannels(image, out HTuple channelCount);
        using (channelCount)
        {
            if (channelCount.I == 1)
            {
                HOperatorSet.CopyImage(image, out HObject copy);
                return copy;
            }

            HOperatorSet.Rgb1ToGray(image, out HObject grayImage);
            return grayImage;
        }
    }

    private static IReadOnlyList<WorkbenchImagePoint> ReadRegionPoints(HObject selectedRegions)
    {
        HOperatorSet.AreaCenter(selectedRegions, out HTuple areas, out HTuple rows, out HTuple columns);
        using (areas)
        using (rows)
        using (columns)
        {
            HOperatorSet.Circularity(selectedRegions, out HTuple circularities);
            using (circularities)
            {
                var areaValues = areas.ToDArr();
                var rowValues = rows.ToDArr();
                var columnValues = columns.ToDArr();
                var scoreValues = circularities.ToDArr();
                return Enumerable.Range(0, areaValues.Length)
                    .Select(index => new WorkbenchImagePoint(
                        rowValues[index],
                        columnValues[index],
                        Math.Sqrt(areaValues[index] / Math.PI),
                        scoreValues[index],
                        areaValues[index]))
                    .OrderBy(point => point.Column)
                    .ToArray();
            }
        }
    }

    private static void ValidateImage(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            throw new FileNotFoundException("Workbench image was not found.", imagePath);
        }
    }

    private static void ValidateProcessingParameters(ProcessingRequest request)
    {
        if (request.ThresholdMin > request.ThresholdMax || request.MinRadiusPx <= 0 ||
            request.MinRadiusPx >= request.MaxRadiusPx || request.MinCircularity is < 0 or > 1)
        {
            throw new InvalidDataException("Image-processing parameters are invalid.");
        }
    }

    private static RectangleRoi? NormalizeAndClamp(RectangleRoi? value, int width, int height)
    {
        if (value is null)
        {
            return null;
        }

        var roi = value.Normalize();
        return new RectangleRoi(
            Math.Clamp(roi.Row1, 0, height - 1),
            Math.Clamp(roi.Column1, 0, width - 1),
            Math.Clamp(roi.Row2, 0, height - 1),
            Math.Clamp(roi.Column2, 0, width - 1));
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    private static string MakeSafeModelName(string value)
    {
        var name = string.Concat(value.Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_'));
        return string.IsNullOrWhiteSpace(name) ? "shape-model" : name[..Math.Min(name.Length, 80)];
    }

    private static string CreateDirectory(string root, string name)
    {
        var path = Path.Combine(Path.GetFullPath(root), name);
        Directory.CreateDirectory(path);
        return path;
    }
}
