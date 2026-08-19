using VisionSystem.Domain;

namespace VisionSystem.Halcon.Geometry;

internal static class PointSetGeometry
{
    public static IReadOnlyList<PixelPoint> SortAlongPrincipalAxis(IReadOnlyList<PixelPoint> points)
    {
        if (points.Count < 2)
        {
            return points.ToArray();
        }

        var (dColumn, dRow) = PrincipalDirection(
            points.Select(point => (X: point.Column, Y: point.Row)).ToArray());
        return points.OrderBy(point => point.Column * dColumn + point.Row * dRow).ToArray();
    }

    public static Pose2D EstimatePose(IReadOnlyList<IdentifiedMachinePoint> points)
    {
        if (points.Count == 0)
        {
            throw new InvalidOperationException("At least one point is required to estimate a pose.");
        }

        var x = points.Average(point => point.Point.Xmm);
        var y = points.Average(point => point.Point.Ymm);
        if (points.Count == 1)
        {
            return new Pose2D(x, y, 0);
        }

        var (dx, dy) = PrincipalDirection(points.Select(point =>
            (X: point.Point.Xmm, Y: point.Point.Ymm)).ToArray());
        return new Pose2D(x, y, Math.Atan2(dy, dx) * 180.0 / Math.PI);
    }

    public static (double MaximumPitchDeviation, double MaximumStraightness) EvaluateRow(
        IReadOnlyList<IdentifiedMachinePoint> points,
        IReadOnlyList<IdentifiedMachinePoint>? nominal = null)
    {
        if (points.Count < 2)
        {
            return (double.PositiveInfinity, double.PositiveInfinity);
        }

        var ordered = points.ToArray();
        var pitches = Enumerable.Range(0, ordered.Length - 1)
            .Select(index => Distance(ordered[index].Point, ordered[index + 1].Point))
            .ToArray();
        double pitchDeviation;
        if (nominal is not null && nominal.Count == points.Count)
        {
            var nominalById = nominal.ToDictionary(point => point.Id, StringComparer.Ordinal);
            pitchDeviation = Enumerable.Range(0, ordered.Length - 1)
                .Where(index => nominalById.ContainsKey(ordered[index].Id) && nominalById.ContainsKey(ordered[index + 1].Id))
                .Select(index => Math.Abs(
                    pitches[index] - Distance(nominalById[ordered[index].Id].Point, nominalById[ordered[index + 1].Id].Point)))
                .DefaultIfEmpty(double.PositiveInfinity)
                .Max();
        }
        else
        {
            var meanPitch = pitches.Average();
            pitchDeviation = pitches.Max(pitch => Math.Abs(pitch - meanPitch));
        }

        var centerX = ordered.Average(point => point.Point.Xmm);
        var centerY = ordered.Average(point => point.Point.Ymm);
        var (dx, dy) = PrincipalDirection(ordered.Select(point =>
            (X: point.Point.Xmm, Y: point.Point.Ymm)).ToArray());
        var maximumStraightness = ordered.Max(point => Math.Abs(
            -dy * (point.Point.Xmm - centerX) + dx * (point.Point.Ymm - centerY)));
        return (pitchDeviation, maximumStraightness);
    }

    private static (double Dx, double Dy) PrincipalDirection(IReadOnlyList<(double X, double Y)> points)
    {
        var meanX = points.Average(point => point.X);
        var meanY = points.Average(point => point.Y);
        var xx = points.Sum(point => (point.X - meanX) * (point.X - meanX));
        var yy = points.Sum(point => (point.Y - meanY) * (point.Y - meanY));
        var xy = points.Sum(point => (point.X - meanX) * (point.Y - meanY));
        var angle = 0.5 * Math.Atan2(2 * xy, xx - yy);
        var dx = Math.Cos(angle);
        var dy = Math.Sin(angle);
        if (dx < 0)
        {
            dx = -dx;
            dy = -dy;
        }

        return (dx, dy);
    }

    private static double Distance(MachinePoint first, MachinePoint second) => Math.Sqrt(
        Math.Pow(second.Xmm - first.Xmm, 2) + Math.Pow(second.Ymm - first.Ymm, 2));
}
