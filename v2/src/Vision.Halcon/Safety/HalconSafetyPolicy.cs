using VisionSystem.Domain;

namespace VisionSystem.Halcon.Safety;

public static class HalconSafetyPolicy
{
    /// <summary>
    /// A published/active recipe can authorize real motion, therefore a coarse
    /// area-center fallback is never accepted as a fine metrology result.
    /// </summary>
    public static bool RejectsFineMeasurementFallback(string? recipeStatus) =>
        RecipeLifecycleStatuses.AllowsAutomaticMotion(recipeStatus);
}
