using VisionSystem.Domain;

namespace VisionSystem.Application;

public sealed record RecipeValidationResult(
    bool IsValid,
    IReadOnlyList<VisionFailure> Failures);

public sealed class RecipeValidator
{
    public RecipeValidationResult Validate(VisionRecipe recipe)
    {
        var failures = new List<VisionFailure>();
        AddIf(string.IsNullOrWhiteSpace(recipe.RecipeId), VisionFailureCode.RecipeInvalid,
            "RecipeId is required.", "Recipe", failures);
        AddIf(string.IsNullOrWhiteSpace(recipe.ProductCode), VisionFailureCode.RecipeInvalid,
            "ProductCode is required.", "Recipe", failures);
        AddIf(recipe.Version < 1, VisionFailureCode.RecipeInvalid,
            "Recipe version must be at least 1.", "Recipe", failures);
        AddIf(!RecipeLifecycleStatuses.IsKnown(recipe.Status), VisionFailureCode.RecipeInvalid,
            $"Recipe status '{recipe.Status}' is unknown.", "Recipe", failures);
        AddIf(!recipe.Board.SearchRoi.IsValid, VisionFailureCode.RecipeInvalid,
            "Board search ROI is invalid.", "Recipe.Board", failures);
        AddIf(!double.IsFinite(recipe.Board.HoleFeature.ThresholdMin) ||
              !double.IsFinite(recipe.Board.HoleFeature.ThresholdMax) ||
              recipe.Board.HoleFeature.ThresholdMin > recipe.Board.HoleFeature.ThresholdMax,
            VisionFailureCode.RecipeInvalid, "Blob threshold range is invalid.", "Recipe.Board", failures);
        AddIf(recipe.Board.HoleFeature.MinimumAreaPx <= 0 ||
              recipe.Board.HoleFeature.MaximumAreaPx <= recipe.Board.HoleFeature.MinimumAreaPx,
            VisionFailureCode.RecipeInvalid, "Blob area range is invalid.", "Recipe.Board", failures);
        AddIf(recipe.Board.HoleFeature.MinimumCircularity is < 0 or > 1,
            VisionFailureCode.RecipeInvalid, "Minimum circularity must be in [0,1].", "Recipe.Board", failures);
        AddIf(recipe.Board.HoleFeature.ExpectedCount < 2,
            VisionFailureCode.RecipeInvalid, "At least two board features are required for pose.", "Recipe.Board", failures);
        AddIf(recipe.Board.LocatorMode == BoardLocatorMode.ShapeModelThenBlob &&
              string.IsNullOrWhiteSpace(recipe.Board.ShapeModel.ModelPath),
            VisionFailureCode.RecipeInvalid, "ShapeModel mode requires a model path.", "Recipe.Board", failures);
        AddIf(!AreFinite(
                  recipe.Board.ShapeModel.AngleStartDeg,
                  recipe.Board.ShapeModel.AngleExtentDeg,
                  recipe.Board.ShapeModel.MinimumScore,
                  recipe.Board.ShapeModel.MaximumOverlap,
                  recipe.Board.ShapeModel.Greediness) ||
              recipe.Board.ShapeModel.AngleExtentDeg <= 0 ||
              recipe.Board.ShapeModel.MinimumScore is < 0 or > 1 ||
              recipe.Board.ShapeModel.MaximumOverlap is < 0 or > 1 ||
              recipe.Board.ShapeModel.Greediness is < 0 or > 1,
            VisionFailureCode.RecipeInvalid,
            "Shape-model search parameters are invalid.",
            "Recipe.Board",
            failures);
        AddIf(recipe.Board.FeatureIds.Any(string.IsNullOrWhiteSpace) ||
              recipe.Board.FeatureIds.Count != recipe.Board.HoleFeature.ExpectedCount ||
              recipe.Board.FeatureIds.Distinct(StringComparer.Ordinal).Count() != recipe.Board.FeatureIds.Count,
            VisionFailureCode.RecipeInvalid,
            "Board feature IDs must be unique and match ExpectedCount.", "Recipe.Board", failures);
        AddIf(!AreFiniteAndPositive(
                  recipe.CorrectionTolerance.MaximumSafeCommandXmm,
                  recipe.CorrectionTolerance.MaximumSafeCommandYmm,
                  recipe.CorrectionTolerance.MaximumSafeCommandThetaDeg,
                  recipe.CorrectionTolerance.MaximumPredictedResidualMm) ||
              recipe.CorrectionTolerance.MaximumSafeCommandXmm <= 0 ||
              recipe.CorrectionTolerance.MaximumSafeCommandYmm <= 0 ||
              recipe.CorrectionTolerance.MaximumSafeCommandThetaDeg <= 0 ||
              recipe.CorrectionTolerance.MaximumPredictedResidualMm <= 0,
            VisionFailureCode.RecipeInvalid, "Correction tolerances must be positive.", "Recipe", failures);
        AddIf(!double.IsFinite(recipe.MachineKinematics.RotationCenterMachine.Xmm) ||
              !double.IsFinite(recipe.MachineKinematics.RotationCenterMachine.Ymm),
            VisionFailureCode.RecipeInvalid, "T-axis rotation center must be finite.", "Recipe.Machine", failures);
        AddIf(!string.Equals(
                recipe.MachineKinematics.CommandConvention,
                MachineCommandConventions.RotateAboutPivotThenTranslate,
                StringComparison.Ordinal),
            VisionFailureCode.RecipeInvalid,
            $"Unsupported command convention '{recipe.MachineKinematics.CommandConvention}'.",
            "Recipe.Machine",
            failures);
        AddIf(recipe.Pins.ExpectedPinCount < 2,
            VisionFailureCode.RecipeInvalid, "At least two pins are required for rigid alignment.", "Recipe.Pins", failures);
        AddIf(!AreFiniteAndPositive(
                recipe.Pins.MaximumPitchErrorMm,
                recipe.Pins.MaximumStraightnessErrorMm),
            VisionFailureCode.RecipeInvalid,
            "Pin geometry tolerances must be finite and positive.",
            "Recipe.Pins",
            failures);
        AddIf(recipe.Pins.EngineeringReferencePins.Any(point => string.IsNullOrWhiteSpace(point.Id)) ||
              recipe.Pins.EngineeringReferencePins
                  .Select(point => point.Id)
                  .Distinct(StringComparer.Ordinal)
                  .Count() != recipe.Pins.EngineeringReferencePins.Count,
            VisionFailureCode.RecipeInvalid,
            "Engineering reference pin IDs must be non-empty and unique.",
            "Recipe.Pins",
            failures);
        ValidateCamera(recipe.BoardCamera, "Recipe.BoardCamera", failures);
        ValidateCamera(recipe.PinCamera, "Recipe.PinCamera", failures);
        ValidateImageQuality(recipe.Board.ImageQuality, failures);
        AddIf(!AreFiniteAndPositive(
                recipe.Calibration.MaximumRmseMm,
                recipe.Calibration.MaximumValidationErrorMm) ||
              recipe.Calibration.MaximumAgeDays <= 0,
            VisionFailureCode.RecipeInvalid,
            "Calibration acceptance limits and maximum age must be positive.",
            "Recipe.Calibration",
            failures);
        AddIf(recipe.ProductionPolicy.CycleTimeoutMs <= 0,
            VisionFailureCode.RecipeInvalid, "Cycle timeout must be positive.", "Recipe", failures);
        AddIf(RecipeLifecycleStatuses.AllowsAutomaticMotion(recipe.Status) &&
              !recipe.ProductionPolicy.SaveAllImages,
            VisionFailureCode.RecipeInvalid,
            "Published/Active recipes must archive every acquired frame for traceability.",
            "Recipe.ProductionPolicy",
            failures);

        return new RecipeValidationResult(failures.Count == 0, failures);
    }

    private static void AddIf(
        bool condition,
        VisionFailureCode code,
        string message,
        string stage,
        ICollection<VisionFailure> failures)
    {
        if (condition)
        {
            failures.Add(new VisionFailure(code, message, false, stage));
        }
    }

    private static void ValidateCamera(
        CameraRecipe camera,
        string stage,
        ICollection<VisionFailure> failures)
    {
        AddIf(!double.IsFinite(camera.ExposureMicroseconds) || camera.ExposureMicroseconds <= 0,
            VisionFailureCode.RecipeInvalid, "Camera exposure must be finite and positive.", stage, failures);
        AddIf(!double.IsFinite(camera.GainDb),
            VisionFailureCode.RecipeInvalid, "Camera gain must be finite.", stage, failures);
        AddIf(camera.AcquisitionTimeoutMs <= 0,
            VisionFailureCode.RecipeInvalid, "Camera acquisition timeout must be positive.", stage, failures);
    }

    private static bool AreFiniteAndPositive(params double[] values) =>
        values.All(value => double.IsFinite(value) && value > 0);

    private static bool AreFinite(params double[] values) => values.All(double.IsFinite);

    private static void ValidateImageQuality(
        ImageQualityRecipe quality,
        ICollection<VisionFailure> failures)
    {
        AddIf(!AreFinite(
                  quality.MinimumMeanGray,
                  quality.MaximumMeanGray,
                  quality.MinimumGrayDeviation,
                  quality.MinimumMeanEdgeAmplitude,
                  quality.SaturationGray,
                  quality.MaximumSaturatedFraction) ||
              quality.MinimumMeanGray < 0 ||
              quality.MaximumMeanGray > 255 ||
              quality.MinimumMeanGray >= quality.MaximumMeanGray ||
              quality.MinimumGrayDeviation < 0 ||
              quality.MinimumMeanEdgeAmplitude < 0 ||
              quality.SaturationGray is < 0 or > 255 ||
              quality.MaximumSaturatedFraction is < 0 or > 1,
            VisionFailureCode.RecipeInvalid,
            "Board image-quality limits are invalid.",
            "Recipe.Board.ImageQuality",
            failures);
    }
}
