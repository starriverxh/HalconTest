using VisionSystem.Domain;

namespace VisionSystem.Application;

/// <summary>
/// Explicit commissioning simulator. Results are always marked IsSimulated and can
/// never authorize motion when the production policy requires a real pin camera.
/// </summary>
public sealed class EngineeringPinSimulator : IPinVisionAlgorithm
{
    public string Name => "Engineering pin simulator";

    public Task<VisionStageResult<PinMeasurement>> InspectAsync(
        IVisionFrame? frame,
        VisionRecipe recipe,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!recipe.Pins.AllowEngineeringSimulation)
        {
            return Task.FromResult(VisionStageResult<PinMeasurement>.Failure(
                new VisionFailure(
                    VisionFailureCode.PinCameraNotCommissioned,
                    "Pin camera is not commissioned and engineering simulation is disabled.",
                    false,
                    "Pins")));
        }

        var measurement = new PinMeasurement(
            recipe.Pins.EngineeringReferencePose,
            1,
            [],
            recipe.Pins.EngineeringReferencePins,
            true,
            Name);
        return Task.FromResult(VisionStageResult<PinMeasurement>.Success(measurement));
    }

    public void Dispose()
    {
    }
}
