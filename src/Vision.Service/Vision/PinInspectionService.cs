using HalconDotNet;
using Vision.Service.Acquisition;
using Vision.Service.Configuration;
using Vision.Service.Storage;

namespace Vision.Service.Vision;

public sealed class PinInspectionService(
    ICamera camera,
    ImageQualityInspector imageQualityInspector,
    PcbHoleLocator holeLocator,
    PinTipLocator pinTipLocator,
    CoordinateTransformer coordinateTransformer,
    ShapeModelLocator shapeModelLocator,
    XYThetaCalculator correctionCalculator,
    ImageStore imageStore,
    VisionRecipe recipe,
    CalibrationModel calibration)
{
    public bool IsReady => camera.IsReady;

    public async Task<VisionResult> InspectAsync(
        string requestId,
        string? imagePathOverride,
        CancellationToken cancellationToken = default)
    {
        using var image = await camera.CaptureAsync(imagePathOverride, cancellationToken).ConfigureAwait(false);
        HOperatorSet.CountChannels(image, out HTuple channelCount);
        using (channelCount)
        {
            HObject analysisImage = image;
            HObject? convertedGrayImage = null;
            try
            {
                if (channelCount.I > 1)
                {
                    HOperatorSet.Rgb1ToGray(image, out HObject grayImage);
                    convertedGrayImage = grayImage;
                    analysisImage = grayImage;
                }

                var imageQuality = imageQualityInspector.Inspect(analysisImage, recipe.ImageQuality);
                var holes = holeLocator.Locate(analysisImage, recipe.HoleDetection);
                var machinePoints = coordinateTransformer.Transform(holes);
                var blobBoardPose = CalculateBoardPose(machinePoints, holes);
                var shapeBoardPose = recipe.Localization.Mode.Equals("ShapeModel", StringComparison.OrdinalIgnoreCase)
                    ? shapeModelLocator.Locate(analysisImage, coordinateTransformer)
                    : null;
                var boardPose = shapeBoardPose ?? blobBoardPose;
                var toolPose = pinTipLocator.Locate(recipe.ToolReferencePose);
                var correction = correctionCalculator.Calculate(boardPose, toolPose);
                var failureReasons = Evaluate(imageQuality, holes.Count, boardPose.Score, correction);
                var isOk = failureReasons.Count == 0;
                var imagePath = await imageStore.SaveAsync(
                    image,
                    requestId,
                    isOk ? "OK" : "NG",
                    cancellationToken).ConfigureAwait(false);

                return new VisionResult(
                    isOk,
                    correction.Xmm,
                    correction.Ymm,
                    correction.ThetaDeg,
                    boardPose.Score,
                    imagePath,
                    new Dictionary<string, object?>
                    {
                        ["recipe"] = recipe.RecipeName,
                        ["recipeVersion"] = recipe.RecipeVersion,
                        ["camera"] = camera.Name,
                        ["imageQuality"] = imageQuality,
                        ["holeCount"] = holes.Count,
                        ["expectedHoleCount"] = recipe.HoleDetection.ExpectedCount,
                        ["imagePoints"] = holes,
                        ["machinePoints"] = machinePoints,
                        ["localizationMode"] = recipe.Localization.Mode,
                        ["blobBoardPose"] = blobBoardPose,
                        ["shapeBoardPose"] = shapeBoardPose,
                        ["boardPose"] = boardPose,
                        ["toolPose"] = toolPose,
                        ["calibrationRmseMm"] = calibration.RmseMm,
                        ["calibrationMaxFitErrorMm"] = calibration.MaxFitErrorMm,
                        ["calibrationMaxValidationErrorMm"] = calibration.MaxValidationErrorMm,
                        ["failureReasons"] = failureReasons
                    },
                    failureReasons);
            }
            finally
            {
                convertedGrayImage?.Dispose();
            }
        }
    }

    private static BoardPose CalculateBoardPose(
        IReadOnlyList<MachinePoint> machinePoints,
        IReadOnlyList<ImagePoint> imagePoints)
    {
        if (machinePoints.Count < 2)
        {
            return new BoardPose(0, 0, 0, 0);
        }

        var first = machinePoints[0];
        var last = machinePoints[^1];
        return new BoardPose(
            machinePoints.Average(point => point.Xmm),
            machinePoints.Average(point => point.Ymm),
            Math.Atan2(last.Ymm - first.Ymm, last.Xmm - first.Xmm) * 180.0 / Math.PI,
            imagePoints.Min(point => point.Score));
    }

    private IReadOnlyList<string> Evaluate(
        ImageQualityResult imageQuality,
        int holeCount,
        double score,
        XYThetaCorrection correction)
    {
        var failures = new List<string>(imageQuality.FailureReasons);
        if (holeCount != recipe.HoleDetection.ExpectedCount)
        {
            failures.Add("HOLE_COUNT_MISMATCH");
        }

        if (score < recipe.Tolerances.MinimumFeatureScore)
        {
            failures.Add("LOW_FEATURE_SCORE");
        }

        if (Math.Abs(correction.Xmm) > recipe.Tolerances.MaxCorrectionMm)
        {
            failures.Add("X_CORRECTION_OUT_OF_RANGE");
        }

        if (Math.Abs(correction.Ymm) > recipe.Tolerances.MaxCorrectionMm)
        {
            failures.Add("Y_CORRECTION_OUT_OF_RANGE");
        }

        if (Math.Abs(correction.ThetaDeg) > recipe.Tolerances.MaxRotationDeg)
        {
            failures.Add("THETA_CORRECTION_OUT_OF_RANGE");
        }

        if (calibration.RmseMm > recipe.Tolerances.MaxCalibrationRmseMm ||
            calibration.MaxValidationErrorMm > recipe.Tolerances.MaxCalibrationErrorMm)
        {
            failures.Add("CALIBRATION_INVALID");
        }

        return failures;
    }
}
