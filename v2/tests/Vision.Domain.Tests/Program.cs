using VisionSystem.Domain;

namespace VisionSystem.Domain.Tests;

public static class Program
{
    private static int _passed;

    public static int Main()
    {
        Run("Pose angle normalization", () =>
        {
            Near(-170, Pose2D.NormalizeAngle(190));
            Near(170, Pose2D.NormalizeAngle(-190));
            Near(180, Pose2D.NormalizeAngle(540));
            Near(180, Pose2D.NormalizeAngle(-180));
        });

        Run("Failure semantics separate rejection, inhibition and fault", () =>
        {
            Equal(VisionFailureImpact.RejectCycle,
                VisionFailurePolicy.Classify(VisionFailureCode.PinMissing));
            Equal(VisionFailureImpact.InhibitMotion,
                VisionFailurePolicy.Classify(VisionFailureCode.PinCameraNotCommissioned));
            Equal(VisionFailureImpact.InhibitMotion,
                VisionFailurePolicy.Classify(VisionFailureCode.AcquisitionNotLive));
            Equal(VisionFailureImpact.FaultSystem,
                VisionFailurePolicy.Classify(VisionFailureCode.CameraNotReady));
            Equal(VisionFailureImpact.FaultSystem,
                VisionFailurePolicy.Classify(VisionFailureCode.ImageArchiveFailed));
            Equal(VisionFailureImpact.FaultSystem,
                VisionFailurePolicy.Classify(VisionFailureCode.PersistenceFailed));
        });

        Run("Only released recipe states allow automatic motion", () =>
        {
            True(RecipeLifecycleStatuses.AllowsAutomaticMotion(RecipeLifecycleStatuses.Published));
            True(RecipeLifecycleStatuses.AllowsAutomaticMotion(RecipeLifecycleStatuses.Active));
            False(RecipeLifecycleStatuses.AllowsAutomaticMotion(RecipeLifecycleStatuses.Engineering));
            False(RecipeLifecycleStatuses.AllowsAutomaticMotion(RecipeLifecycleStatuses.Approved));
            False(RecipeLifecycleStatuses.AllowsAutomaticMotion(RecipeLifecycleStatuses.Retired));
        });

        Run("Finite motion values are explicit", () =>
        {
            True(new Pose2D(1, 2, 3).IsFinite);
            False(new Pose2D(double.NaN, 2, 3).IsFinite);
            True(new XYThetaCorrection(1, 2, 3).IsFinite);
            False(new XYThetaCorrection(1, double.PositiveInfinity, 3).IsFinite);
        });

        Console.WriteLine($"Domain tests passed: {_passed}/4");
        return 0;
    }

    private static void Run(string name, Action test)
    {
        test();
        _passed++;
        Console.WriteLine($"PASS {name}");
    }

    private static void True(bool value)
    {
        if (!value)
        {
            throw new InvalidOperationException("Expected true.");
        }
    }

    private static void False(bool value) => True(!value);

    private static void Equal<T>(T expected, T actual) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
        }
    }

    private static void Near(double expected, double actual, double tolerance = 1e-9)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
        }
    }
}
