using HalconDotNet;
using Vision.Service.Acquisition;
using Vision.Service.Configuration;
using Vision.Service.Storage;

namespace Vision.Service.Vision;

public sealed class PinInspectionService(
    ICamera camera,
    PcbHoleLocator holeLocator,
    PinTipLocator pinTipLocator,
    CoordinateTransformer coordinateTransformer,
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

                var holes = holeLocator.Locate(analysisImage, recipe.HoleDetection);
                var machinePoints = coordinateTransformer.Transform(holes);
                var boardPose = CalculateBoardPose(machinePoints, holes);
                var toolPose = pinTipLocator.Locate(recipe.ToolReferencePose);
                var correction = correctionCalculator.Calculate(boardPose, toolPose);
                var isOk = Evaluate(holes.Count, boardPose.Score, correction);
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
                        ["camera"] = camera.Name,
                        ["holeCount"] = holes.Count,
                        ["expectedHoleCount"] = recipe.HoleDetection.ExpectedCount,
                        ["imagePoints"] = holes,
                        ["machinePoints"] = machinePoints,
                        ["boardPose"] = boardPose,
                        ["toolPose"] = toolPose,
                        ["calibrationRmseMm"] = calibration.RmseMm,
                        ["calibrationMaxFitErrorMm"] = calibration.MaxFitErrorMm,
                        ["calibrationMaxValidationErrorMm"] = calibration.MaxValidationErrorMm
                    });
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

    private bool Evaluate(int holeCount, double score, XYThetaCorrection correction) =>
        holeCount == recipe.HoleDetection.ExpectedCount &&
        score >= recipe.Tolerances.MinimumFeatureScore &&
        Math.Abs(correction.Xmm) <= recipe.Tolerances.MaxCorrectionMm &&
        Math.Abs(correction.Ymm) <= recipe.Tolerances.MaxCorrectionMm &&
        Math.Abs(correction.ThetaDeg) <= recipe.Tolerances.MaxRotationDeg &&
        calibration.RmseMm <= recipe.Tolerances.MaxCalibrationRmseMm &&
        calibration.MaxValidationErrorMm <= recipe.Tolerances.MaxCalibrationErrorMm;
}
