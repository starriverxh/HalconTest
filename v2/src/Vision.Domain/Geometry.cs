namespace VisionSystem.Domain;

/// <summary>HALCON image coordinate. Row is Y-down; Column is X-right.</summary>
public readonly record struct PixelPoint(double Row, double Column, double Score = 1.0)
{
    public bool IsFinite =>
        double.IsFinite(Row) && double.IsFinite(Column) && double.IsFinite(Score);
}

/// <summary>Machine coordinate in millimetres.</summary>
public readonly record struct MachinePoint(double Xmm, double Ymm)
{
    public bool IsFinite => double.IsFinite(Xmm) && double.IsFinite(Ymm);
}

public sealed record IdentifiedMachinePoint(
    string Id,
    MachinePoint Point,
    double Score = 1.0);

public readonly record struct Pose2D(double Xmm, double Ymm, double ThetaDeg)
{
    public bool IsFinite =>
        double.IsFinite(Xmm) && double.IsFinite(Ymm) && double.IsFinite(ThetaDeg);

    public static double NormalizeAngle(double angleDeg)
    {
        while (angleDeg > 180)
        {
            angleDeg -= 360;
        }

        while (angleDeg <= -180)
        {
            angleDeg += 360;
        }

        return angleDeg;
    }
}

public readonly record struct XYThetaCorrection(double Xmm, double Ymm, double ThetaDeg)
{
    public bool IsFinite =>
        double.IsFinite(Xmm) && double.IsFinite(Ymm) && double.IsFinite(ThetaDeg);
}

public sealed record AlignmentResidual(
    string Id,
    MachinePoint TargetHole,
    MachinePoint TransformedPin,
    double ErrorMm);

public sealed record AlignmentPlan(
    XYThetaCorrection AxisCommand,
    double RigidRotationDeg,
    MachinePoint RigidTranslation,
    IReadOnlyList<AlignmentResidual> Residuals,
    double RmseMm,
    double MaximumResidualMm);

public sealed record RectangleRoi
{
    public double Row1 { get; init; }
    public double Column1 { get; init; }
    public double Row2 { get; init; }
    public double Column2 { get; init; }

    public bool IsValid =>
        double.IsFinite(Row1) && double.IsFinite(Column1) &&
        double.IsFinite(Row2) && double.IsFinite(Column2) &&
        Row1 >= 0 && Column1 >= 0 && Row2 > Row1 && Column2 > Column1;
}
