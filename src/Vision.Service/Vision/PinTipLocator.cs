using Vision.Service.Configuration;

namespace Vision.Service.Vision;

/// <summary>
/// Uses the commissioned fixed tool pose while the needle camera is not installed.
/// Replace this implementation with image-based tip localization without changing callers.
/// </summary>
public sealed class PinTipLocator
{
    public ToolPose Locate(Pose2D referencePose) => new(
        referencePose.Xmm,
        referencePose.Ymm,
        referencePose.ThetaDeg,
        1.0,
        "commissioned-fixed-reference");
}
