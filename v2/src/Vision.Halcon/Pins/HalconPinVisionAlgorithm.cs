using System.Text.Json;
using HalconDotNet;
using VisionSystem.Application;
using VisionSystem.Domain;
using VisionSystem.Halcon.Calibration;
using VisionSystem.Halcon.Diagnostics;
using VisionSystem.Halcon.Frames;
using VisionSystem.Halcon.Geometry;
using VisionSystem.Halcon.Imaging;
using VisionSystem.Halcon.Metrology;
using VisionSystem.Halcon.Safety;
using VisionSystem.Halcon.Segmentation;

namespace VisionSystem.Halcon.Pins;

public sealed record PinAlgorithmOptions
{
    public RectangleRoi? SearchRoi { get; init; }
    public ImageQualityRecipe ImageQuality { get; init; } = new()
    {
        MinimumMeanGray = 20,
        MaximumMeanGray = 250,
        MinimumGrayDeviation = 5,
        MinimumMeanEdgeAmplitude = 1,
        SaturationGray = 250,
        MaximumSaturatedFraction = 0.4
    };
    public double ThresholdMin { get; init; }
    public double ThresholdMax { get; init; } = 65;
    public double MinimumAreaPx { get; init; } = 400;
    public double MaximumAreaPx { get; init; } = 1200;
    public double MinimumCircularity { get; init; } = 0.80;
    public FineCircleOptions FineCircle { get; init; } = new()
    {
        SearchRadiusPx = 26,
        MinimumRadiusPx = 10,
        MaximumRadiusPx = 20,
        MaximumCenterShiftPx = 5,
        MaximumFitResidualPx = 1.0
    };
    public bool RejectFineFallbackInProduction { get; init; } = true;
}

public sealed class HalconPinVisionAlgorithm : IPinVisionAlgorithm, IHalconDiagnosticsSource
{
    private readonly object _diagnosticSync = new();
    private readonly PlanarPixelMapper _mapper = new();
    private readonly ImageQualityNode _quality = new();
    private readonly FineCircleMeasurer _fine;
    private readonly PinAlgorithmOptions _options;
    private IReadOnlyList<HalconStageDiagnostic> _lastDiagnostics = [];
    private bool _disposed;

    public HalconPinVisionAlgorithm(PinAlgorithmOptions? options = null)
    {
        _options = options ?? new PinAlgorithmOptions();
        _fine = new FineCircleMeasurer(_options.FineCircle);
    }

    public string Name => "HALCON individual pin-tip metrology";

    public IReadOnlyList<HalconStageDiagnostic> LastDiagnostics
    {
        get
        {
            lock (_diagnosticSync)
            {
                return _lastDiagnostics.ToArray();
            }
        }
    }

    public Task<VisionStageResult<PinMeasurement>> InspectAsync(
        IVisionFrame? frame,
        VisionRecipe recipe,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = new DiagnosticCollector();
        var failures = new List<VisionFailure>();

        if (frame is null)
        {
            if (!recipe.Pins.AllowEngineeringSimulation)
            {
                failures.Add(new VisionFailure(
                    VisionFailureCode.PinCameraNotCommissioned,
                    "No pin frame was supplied and engineering simulation is disabled.",
                    false,
                    "Pins.Frame"));
                return Complete(null, failures, diagnostics);
            }

            var simulated = new PinMeasurement(
                recipe.Pins.EngineeringReferencePose,
                1,
                [],
                recipe.Pins.EngineeringReferencePins,
                true,
                "Explicit engineering reference; motion must remain forbidden");
            diagnostics.SetMetric("pins.simulated", 1);
            return Complete(simulated, failures, diagnostics);
        }

        if (frame is not HalconFrame halconFrame || frame.Role != CameraRole.Pins)
        {
            failures.Add(new VisionFailure(
                VisionFailureCode.FrameSequenceInvalid,
                "Pin algorithm requires a pins-role HalconFrame.",
                false,
                "Pins.Frame"));
            return Complete(null, failures, diagnostics);
        }

        using var gray = HalconImageLease.CreateGray(halconFrame.BorrowImage());
        var searchRoi = _options.SearchRoi is { IsValid: true }
            ? _options.SearchRoi
            : new RectangleRoi { Row1 = 0, Column1 = 0, Row2 = frame.Height - 1, Column2 = frame.Width - 1 };
        var quality = _quality.Evaluate(gray.Image, _options.ImageQuality, searchRoi, "Pins.Quality");
        using (var qualityStage = diagnostics.Start("pins.quality"))
        {
            if (quality.Failures.Count > 0)
            {
                failures.AddRange(quality.Failures);
                qualityStage.Failure("Pin image failed quality gate.", quality.Metrics);
                return Complete(null, failures, diagnostics);
            }

            qualityStage.Success(metrics: quality.Metrics);
        }

        IReadOnlyList<PixelPoint> coarsePoints;
        using (var coarseStage = diagnostics.Start("pins.coarse"))
        {
            HOperatorSet.GenRectangle1(
                out HObject searchRegion,
                searchRoi.Row1,
                searchRoi.Column1,
                searchRoi.Row2,
                searchRoi.Column2);
            using (searchRegion)
            {
                var extraction = BlobCandidateExtractor.Extract(
                    gray.Image,
                    searchRegion,
                    _options.ThresholdMin,
                    _options.ThresholdMax,
                    _options.MinimumAreaPx,
                    _options.MaximumAreaPx,
                    _options.MinimumCircularity);
                coarsePoints = PointSetGeometry.SortAlongPrincipalAxis(extraction.Points);
                var metrics = new Dictionary<string, double>
                {
                    ["pinCount"] = coarsePoints.Count,
                    ["connectedCount"] = extraction.ConnectedCount,
                    ["minimumCircularity"] = extraction.MinimumCircularity,
                    ["meanAreaPx"] = extraction.MeanAreaPx
                };
                if (coarsePoints.Count != recipe.Pins.ExpectedPinCount)
                {
                    failures.Add(new VisionFailure(
                        coarsePoints.Count < recipe.Pins.ExpectedPinCount
                            ? VisionFailureCode.PinMissing
                            : VisionFailureCode.PinRowNotFound,
                        $"Detected {coarsePoints.Count} pin tips; expected {recipe.Pins.ExpectedPinCount}.",
                        true,
                        "Pins.Coarse"));
                    coarseStage.Failure("Pin count mismatch.", metrics);
                    return Complete(null, failures, diagnostics);
                }

                coarseStage.Success(metrics: metrics);
            }
        }

        var fine = _fine.Measure(gray.Image, coarsePoints);
        using (var fineStage = diagnostics.Start("pins.fine"))
        {
            var fallbackCount = fine.Count(item => item.UsedFallback);
            var residuals = fine.Where(item => double.IsFinite(item.ResidualPx))
                .Select(item => item.ResidualPx).ToArray();
            var metrics = new Dictionary<string, double>
            {
                ["fallbackCount"] = fallbackCount,
                ["meanResidualPx"] = residuals.Length == 0 ? -1 : residuals.Average(),
                ["maximumResidualPx"] = residuals.Length == 0 ? -1 : residuals.Max()
            };
            if (fallbackCount > 0 && _options.RejectFineFallbackInProduction &&
                HalconSafetyPolicy.RejectsFineMeasurementFallback(recipe.Status))
            {
                failures.Add(new VisionFailure(
                    VisionFailureCode.PinRowNotFound,
                    $"{fallbackCount} pin tips used area-center fallback instead of subpixel circle fitting.",
                    true,
                    "Pins.Fine"));
                fineStage.Failure("Production recipe rejects pin-tip fallback.", metrics);
                return Complete(null, failures, diagnostics);
            }

            fineStage.Success(
                fallbackCount == 0 ? "All pin tips used subpixel circle fits." : "Engineering fallback was used.",
                metrics);
        }

        var pixels = PointSetGeometry.SortAlongPrincipalAxis(fine.Select(item => item.Point).ToArray());
        var ids = recipe.Pins.EngineeringReferencePins.Count == pixels.Count
            ? recipe.Pins.EngineeringReferencePins.Select(point => point.Id).ToArray()
            : Enumerable.Range(1, pixels.Count).Select(index => $"P{index}").ToArray();
        IReadOnlyList<IdentifiedMachinePoint> machinePoints;
        using (var calibrationStage = diagnostics.Start("pins.calibration"))
        {
            try
            {
                var calibration = _mapper.Load(recipe.Calibration.PinToMachineTransformPath);
                var calibrationFailures = PlanarPixelMapper.ValidateForRecipe(
                    calibration,
                    recipe.Calibration,
                    frame.Width,
                    frame.Height,
                    DateTimeOffset.UtcNow,
                    "Pins.Calibration");
                failures.AddRange(calibrationFailures);
                var metrics = new Dictionary<string, double>
                {
                    ["rmseMm"] = calibration.RmseMm,
                    ["maximumValidationErrorMm"] = calibration.MaximumValidationErrorMm,
                    ["workPlaneZmm"] = calibration.WorkPlaneZmm
                };
                if (calibrationFailures.Count > 0)
                {
                    calibrationStage.Failure("Pin calibration failed validation.", metrics);
                    return Complete(null, failures, diagnostics);
                }

                machinePoints = _mapper.Transform(calibration, pixels, ids);
                calibrationStage.Success(metrics: metrics);
            }
            catch (Exception exception) when (exception is FileNotFoundException or InvalidDataException or JsonException)
            {
                failures.Add(new VisionFailure(
                    VisionFailureCode.CalibrationMissing,
                    exception.Message,
                    false,
                    "Pins.Calibration"));
                calibrationStage.Failure(exception.Message);
                return Complete(null, failures, diagnostics);
            }
        }

        var pose = PointSetGeometry.EstimatePose(machinePoints);
        var nominal = recipe.Pins.EngineeringReferencePins.Count == machinePoints.Count
            ? recipe.Pins.EngineeringReferencePins
            : null;
        var rowQuality = PointSetGeometry.EvaluateRow(machinePoints, nominal);
        diagnostics.SetMetric("pins.geometry.maximumPitchDeviationMm", rowQuality.MaximumPitchDeviation);
        diagnostics.SetMetric("pins.geometry.maximumStraightnessMm", rowQuality.MaximumStraightness);
        if (rowQuality.MaximumPitchDeviation > recipe.Pins.MaximumPitchErrorMm)
        {
            failures.Add(new VisionFailure(
                VisionFailureCode.PinPitchOutOfTolerance,
                $"Maximum pin pitch deviation {rowQuality.MaximumPitchDeviation:F4} mm exceeds {recipe.Pins.MaximumPitchErrorMm:F4} mm.",
                true,
                "Pins.Geometry"));
        }

        if (rowQuality.MaximumStraightness > recipe.Pins.MaximumStraightnessErrorMm)
        {
            failures.Add(new VisionFailure(
                VisionFailureCode.PinBent,
                $"Maximum pin straightness error {rowQuality.MaximumStraightness:F4} mm exceeds {recipe.Pins.MaximumStraightnessErrorMm:F4} mm.",
                true,
                "Pins.Geometry"));
        }

        var score = pixels.Min(point => point.Score);
        var isOffline = frame.Provenance != AcquisitionProvenance.LiveCamera;
        diagnostics.SetMetric("pins.simulated", isOffline ? 1 : 0);
        diagnostics.SetMetric("pins.pose.xMm", pose.Xmm);
        diagnostics.SetMetric("pins.pose.yMm", pose.Ymm);
        diagnostics.SetMetric("pins.pose.thetaDeg", pose.ThetaDeg);
        var measurement = new PinMeasurement(
            pose,
            score,
            pixels,
            machinePoints,
            isOffline,
            isOffline
                ? "HALCON offline pin image; engineering only"
                : "HALCON up-camera individual pin metrology");
        return Complete(measurement, failures, diagnostics);
    }

    public void Dispose() => _disposed = true;

    private Task<VisionStageResult<PinMeasurement>> Complete(
        PinMeasurement? value,
        IReadOnlyList<VisionFailure> failures,
        DiagnosticCollector diagnostics)
    {
        lock (_diagnosticSync)
        {
            _lastDiagnostics = diagnostics.Entries;
        }

        return Task.FromResult(new VisionStageResult<PinMeasurement>(
            value,
            failures.ToArray(),
            diagnostics.Metrics));
    }
}
