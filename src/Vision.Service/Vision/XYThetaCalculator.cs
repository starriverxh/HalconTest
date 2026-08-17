namespace Vision.Service.Vision;

public sealed class XYThetaCalculator
{
    public XYThetaCorrection Calculate(BoardPose board, ToolPose tool) => new(
        board.Xmm - tool.Xmm,
        board.Ymm - tool.Ymm,
        NormalizeAngle(board.ThetaDeg - tool.ThetaDeg));

    private static double NormalizeAngle(double angle)
    {
        while (angle > 180)
        {
            angle -= 360;
        }

        while (angle <= -180)
        {
            angle += 360;
        }

        return angle;
    }
}
