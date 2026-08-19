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
using VisionSystem.Halcon.Models;
using VisionSystem.Halcon.Safety;

namespace VisionSystem.Halcon.Board;

public sealed record BoardAlgorithmOptions
{
    public BoardCoarseOptions Coarse { get; init; } = new();
    public FineCircleOptions FineCircle { get; init; } = new();
    public bool RejectFineFallbackInProduction { get; init; } = true;
}

public sealed class HalconBoardVisionAlgorithm : IBoardVisionAlgorithm, IHalconDiagnosticsSource
{
    private readonly object _diagnosticSync = new();
    private readonly ShapeModelCache _shapeModels = new();
    private readonly PlanarPixelMapper _mapper = new();
    private readonly ImageQualityNode _quality = new();
    private readonly BoardCoarseLocator _coarse;
    private readonly FineCircleMeasurer _fine;
    private readonly BoardAlgorithmOptions _options;
    private IReadOnlyList<HalconStageDiagnostic> _lastDiagnostics = [];
    private bool _disposed;

    public HalconBoardVisionAlgorithm(BoardAlgorithmOptions? options = null)
    {
        _options = options ?? new BoardAlgorithmOptions();
        _coarse = new BoardCoarseLocator(_shapeModels, _options.Coarse);
        _fine = new FineCircleMeasurer(_options.FineCircle);
    }

    public string Name => "HALCON PCB coarse-to-fine locator";

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

    public Task<VisionStageResult<BoardMeasurement>> LocateAsync(
        IVisionFrame frame,
        VisionRecipe recipe,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = new DiagnosticCollector();
        var failures = new List<VisionFailure>();

        if (frame is not HalconFrame halconFrame || frame.Role != CameraRole.Board)
        {
            failures.Add(new VisionFailure(
                VisionFailureCode.FrameSequenceInvalid,
                "PCB algorithm requires a board-role HalconFrame.",
                false,
                "Board.Frame"));
            return Complete(null, failures, diagnostics);
        }

        using var gray = HalconImageLease.CreateGray(halconFrame.BorrowImage());

        using (var stage = diagnostics.Start("board.quality"))
        {
            var quality = _quality.Evaluate(gray.Image, recipe.Board.ImageQuality, recipe.Board.SearchRoi, "Board.Quality");
            foreach (var metric in quality.Metrics)
            {
                diagnostics.SetMetric($"board.quality.{metric.Key}", metric.Value);
            }

            if (quality.Failures.Count > 0)
            {
                failures.AddRange(quality.Failures);
                stage.Failure("PCB image failed quality gate.", quality.Metrics);
                return Complete(null, failures, diagnostics);
            }

            stage.Success(metrics: quality.Metrics);

            BoardCoarseResult coarse;
            using (var coarseStage = diagnostics.Start("board.coarse"))
            {
                try
                {
                    coarse = _coarse.Locate(gray.Image, recipe.Board);
                    var coarseMetrics = new Dictionary<string, double>
                    {
                        ["featureCount"] = coarse.Points.Count,
                        ["connectedCount"] = coarse.ConnectedCount,
                        ["meanAreaPx"] = coarse.MeanAreaPx,
                        ["score"] = coarse.Score,
                        ["matchRow"] = coarse.MatchRow,
                        ["matchColumn"] = coarse.MatchColumn,
                        ["matchAngleDeg"] = coarse.MatchAngleDeg,
                        ["usedShapeModel"] = coarse.UsedShapeModel ? 1 : 0
                    };
                    if (coarse.Points.Count != recipe.Board.HoleFeature.ExpectedCount)
                    {
                        failures.Add(new VisionFailure(
                            VisionFailureCode.BoardFeatureCountMismatch,
                            $"Detected {coarse.Points.Count} PCB holes; expected {recipe.Board.HoleFeature.ExpectedCount}.",
                            true,
                            "Board.Coarse"));
                        coarseStage.Failure("PCB coarse feature count mismatch.", coarseMetrics);
                        return Complete(null, failures, diagnostics);
                    }

                    coarseStage.Success(metrics: coarseMetrics);
                }
                catch (Exception exception) when (exception is HOperatorException or InvalidDataException or FileNotFoundException)
                {
                    failures.Add(new VisionFailure(
                        VisionFailureCode.BoardNotFound,
                        exception.Message,
                        true,
                        "Board.Coarse"));
                    coarseStage.Failure(exception.Message);
                    return Complete(null, failures, diagnostics);
                }
            }

            IReadOnlyList<FineCircleMeasurement> fine;
            using (var fineStage = diagnostics.Start("board.fine"))
            {
                try
                {
                    fine = _fine.Measure(gray.Image, coarse.Points);
                    var fallbackCount = fine.Count(item => item.UsedFallback);
                    var finiteResiduals = fine.Where(item => double.IsFinite(item.ResidualPx))
                        .Select(item => item.ResidualPx).ToArray();
                    var fineMetrics = new Dictionary<string, double>
                    {
                        ["circleCount"] = fine.Count,
                        ["fallbackCount"] = fallbackCount,
                        ["meanResidualPx"] = finiteResiduals.Length == 0 ? -1 : finiteResiduals.Average(),
                        ["maximumResidualPx"] = finiteResiduals.Length == 0 ? -1 : finiteResiduals.Max(),
                        ["meanCandidateContours"] = fine.Count == 0 ? 0 : fine.Average(item => item.CandidateContours)
                    };
                    if (fallbackCount > 0 && _options.RejectFineFallbackInProduction &&
                        HalconSafetyPolicy.RejectsFineMeasurementFallback(recipe.Status))
                    {
                        failures.Add(new VisionFailure(
                            VisionFailureCode.BoardNotFound,
                            $"{fallbackCount} PCB holes used area-center fallback instead of subpixel contour fitting.",
                            true,
                            "Board.Fine"));
                        fineStage.Failure("Production recipe rejects fine-measurement fallback.", fineMetrics);
                        return Complete(null, failures, diagnostics);
                    }

                    fineStage.Success(
                        fallbackCount == 0 ? "All holes used subpixel circle fits." : "Engineering fallback was used.",
                        fineMetrics);
                }
                catch (Exception exception) when (exception is HOperatorException or InvalidDataException)
                {
                    failures.Add(new VisionFailure(
                        VisionFailureCode.BoardNotFound,
                        exception.Message,
                        true,
                        "Board.Fine"));
                    fineStage.Failure(exception.Message);
                    return Complete(null, failures, diagnostics);
                }
            }

            var sortedPixels = PointSetGeometry.SortAlongPrincipalAxis(fine.Select(item => item.Point).ToArray());
            if (recipe.Board.FeatureIds.Count != sortedPixels.Count)
            {
                failures.Add(new VisionFailure(
                    VisionFailureCode.PointCorrespondenceFailed,
                    $"Recipe has {recipe.Board.FeatureIds.Count} feature IDs for {sortedPixels.Count} measured holes.",
                    false,
                    "Board.Correspondence"));
                return Complete(null, failures, diagnostics);
            }

            IReadOnlyList<IdentifiedMachinePoint> machinePoints;
            PlanarCalibrationModel calibration;
            using (var calibrationStage = diagnostics.Start("board.calibration"))
            {
                try
                {
                    calibration = _mapper.Load(recipe.Calibration.BoardToMachineTransformPath);
                    var calibrationFailures = PlanarPixelMapper.ValidateForRecipe(
                        calibration,
                        recipe.Calibration,
                        frame.Width,
                        frame.Height,
                        DateTimeOffset.UtcNow,
                        "Board.Calibration");
                    failures.AddRange(calibrationFailures);
                    var calibrationMetrics = new Dictionary<string, double>
                    {
                        ["rmseMm"] = calibration.RmseMm,
                        ["maximumValidationErrorMm"] = calibration.MaximumValidationErrorMm,
                        ["workPlaneZmm"] = calibration.WorkPlaneZmm
                    };
                    if (calibrationFailures.Count > 0)
                    {
                        calibrationStage.Failure("PCB calibration failed validation.", calibrationMetrics);
                        return Complete(null, failures, diagnostics);
                    }

                    machinePoints = _mapper.Transform(calibration, sortedPixels, recipe.Board.FeatureIds);
                    calibrationStage.Success(metrics: calibrationMetrics);
                }
                catch (Exception exception) when (exception is FileNotFoundException or InvalidDataException or JsonException)
                {
                    failures.Add(new VisionFailure(
                        VisionFailureCode.CalibrationMissing,
                        exception.Message,
                        false,
                        "Board.Calibration"));
                    calibrationStage.Failure(exception.Message);
                    return Complete(null, failures, diagnostics);
                }
            }

            var pose = PointSetGeometry.EstimatePose(machinePoints);
            var score = Math.Min(coarse.Score, sortedPixels.Min(point => point.Score));
            diagnostics.SetMetric("board.pose.xMm", pose.Xmm);
            diagnostics.SetMetric("board.pose.yMm", pose.Ymm);
            diagnostics.SetMetric("board.pose.thetaDeg", pose.ThetaDeg);
            diagnostics.SetMetric("board.score", score);
            var measurement = new BoardMeasurement(
                pose,
                score,
                sortedPixels,
                machinePoints,
                quality.Measurement,
                coarse.UsedShapeModel
                    ? "HALCON ShapeModel coarse + Blob ROI + XLD circle"
                    : "HALCON Blob coarse + XLD circle");
            return Complete(measurement, failures, diagnostics);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shapeModels.Dispose();
    }

    private Task<VisionStageResult<BoardMeasurement>> Complete(
        BoardMeasurement? value,
        IReadOnlyList<VisionFailure> failures,
        DiagnosticCollector diagnostics)
    {
        lock (_diagnosticSync)
        {
            _lastDiagnostics = diagnostics.Entries;
        }

        return Task.FromResult(new VisionStageResult<BoardMeasurement>(
            value,
            failures.ToArray(),
            diagnostics.Metrics));
    }
}
