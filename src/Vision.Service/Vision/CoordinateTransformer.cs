using HalconDotNet;

namespace Vision.Service.Vision;

public sealed class CoordinateTransformer(CalibrationModel calibration)
{
    public IReadOnlyList<MachinePoint> Transform(IReadOnlyList<ImagePoint> imagePoints)
    {
        if (imagePoints.Count == 0)
        {
            return [];
        }

        using var homMat2D = new HTuple(calibration.HomMat2D);
        using var columns = new HTuple(imagePoints.Select(point => point.Column).ToArray());
        using var rows = new HTuple(imagePoints.Select(point => point.Row).ToArray());
        HOperatorSet.AffineTransPoint2d(homMat2D, columns, rows, out HTuple machineX, out HTuple machineY);
        using (machineX)
        using (machineY)
        {
            var xValues = machineX.ToDArr();
            var yValues = machineY.ToDArr();
            return Enumerable.Range(0, xValues.Length)
                .Select(index => new MachinePoint(xValues[index], yValues[index]))
                .ToArray();
        }
    }
}
