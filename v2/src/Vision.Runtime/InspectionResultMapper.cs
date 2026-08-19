using VisionSystem.Contracts;
using VisionSystem.Domain;

namespace VisionSystem.Runtime;

public static class InspectionResultMapper
{
    public static InspectionResultDto ToContract(InspectionResult result)
    {
        var alignment = result.Alignment;
        return new InspectionResultDto
        {
            RequestId = result.RequestId,
            ProductId = result.ProductId,
            RecipeId = result.RecipeId,
            RecipeVersion = result.RecipeVersion,
            Disposition = result.Disposition.ToString(),
            MeasurementValid = result.Board is not null && result.Pins is not null && alignment is not null,
            MotionAllowed = result.MotionAllowed,
            CommandXmm = result.Correction.Xmm,
            CommandYmm = result.Correction.Ymm,
            CommandThetaDeg = result.Correction.ThetaDeg,
            BoardScore = result.Board?.Score,
            PinScore = result.Pins?.Score,
            AlignmentRmseMm = alignment?.RmseMm,
            MaximumPinResidualMm = alignment?.MaximumResidualMm,
            PointResiduals = alignment?.Residuals.Select(item => new PointResidualDto(
                item.Id,
                item.TargetHole.Xmm,
                item.TargetHole.Ymm,
                item.TransformedPin.Xmm,
                item.TransformedPin.Ymm,
                item.ErrorMm)).ToArray() ?? [],
            Failures = result.Failures.Select(item => new FailureDto(
                item.Code.ToString(),
                item.Stage,
                item.Message,
                item.IsRecoverable)).ToArray(),
            StateHistory = result.StateHistory.Select(item => item.ToString()).ToArray(),
            BoardFrame = ToContract(result.BoardFrame),
            PinFrame = ToContract(result.PinFrame),
            StageMetrics = new Dictionary<string, double>(result.StageMetrics, StringComparer.Ordinal),
            BoardAlgorithmName = result.BoardAlgorithmName,
            PinAlgorithmName = result.PinAlgorithmName,
            RecipeStatus = result.RecipeStatus,
            ProductCode = result.ProductCode,
            DurationMs = result.DurationMs,
            ResultPath = result.ResultPath
        };
    }

    private static FrameEvidenceDto? ToContract(FrameEvidence? frame) => frame is null
        ? null
        : new FrameEvidenceDto(
            frame.FrameId,
            frame.CameraRole,
            frame.Provenance.ToString(),
            frame.CapturedUtc,
            frame.Width,
            frame.Height,
            frame.SourceName,
            frame.ArchivedImagePath,
            frame.Sha256);
}
