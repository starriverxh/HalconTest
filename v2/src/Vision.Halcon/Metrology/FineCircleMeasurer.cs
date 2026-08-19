using HalconDotNet;
using VisionSystem.Domain;

namespace VisionSystem.Halcon.Metrology;

public sealed record FineCircleOptions
{
    public double SearchRadiusPx { get; init; } = 30;
    public double MinimumRadiusPx { get; init; } = 12;
    public double MaximumRadiusPx { get; init; } = 24;
    public double EdgeSigma { get; init; } = 1.0;
    public double EdgeLow { get; init; } = 10;
    public double EdgeHigh { get; init; } = 30;
    public double MaximumCenterShiftPx { get; init; } = 5;
    public double MaximumFitResidualPx { get; init; } = 1.0;
    public bool AllowAreaCenterFallback { get; init; } = true;
}

internal sealed record FineCircleMeasurement(
    PixelPoint Point,
    double RadiusPx,
    double ResidualPx,
    bool UsedFallback,
    int CandidateContours);

internal sealed class FineCircleMeasurer(FineCircleOptions options)
{
    public IReadOnlyList<FineCircleMeasurement> Measure(
        HObject grayImage,
        IReadOnlyList<PixelPoint> coarsePoints)
    {
        var measurements = new List<FineCircleMeasurement>(coarsePoints.Count);
        foreach (var coarse in coarsePoints)
        {
            measurements.Add(MeasureOne(grayImage, coarse));
        }

        return measurements;
    }

    private FineCircleMeasurement MeasureOne(HObject grayImage, PixelPoint coarse)
    {
        HOperatorSet.GenCircle(out HObject searchRegion, coarse.Row, coarse.Column, options.SearchRadiusPx);
        using (searchRegion)
        {
            HOperatorSet.ReduceDomain(grayImage, searchRegion, out HObject reduced);
            using (reduced)
            {
                HOperatorSet.EdgesSubPix(
                    reduced,
                    out HObject contours,
                    "canny",
                    options.EdgeSigma,
                    options.EdgeLow,
                    options.EdgeHigh);
                using (contours)
                {
                    HOperatorSet.CountObj(contours, out HTuple countTuple);
                    using (countTuple)
                    {
                        var contourCount = countTuple.I;
                        CircleCandidate? best = null;
                        for (var objectIndex = 1; objectIndex <= contourCount; objectIndex++)
                        {
                            HOperatorSet.SelectObj(contours, out HObject contour, objectIndex);
                            using (contour)
                            {
                                var candidate = TryFit(contour, coarse, objectIndex);
                                if (candidate is not null && (best is null || candidate.Cost < best.Cost))
                                {
                                    best = candidate;
                                }
                            }
                        }

                        if (best is not null && best.ResidualPx <= options.MaximumFitResidualPx)
                        {
                            var score = Math.Clamp(1.0 / (1.0 + best.ResidualPx), 0, 1);
                            return new FineCircleMeasurement(
                                new PixelPoint(best.Row, best.Column, score),
                                best.Radius,
                                best.ResidualPx,
                                false,
                                contourCount);
                        }

                        if (!options.AllowAreaCenterFallback)
                        {
                            throw new InvalidDataException(
                                $"No acceptable subpixel circle was found near ({coarse.Row:F2},{coarse.Column:F2}).");
                        }

                        return new FineCircleMeasurement(
                            coarse with { Score = Math.Min(coarse.Score, 0.5) },
                            double.NaN,
                            double.NaN,
                            true,
                            contourCount);
                    }
                }
            }
        }
    }

    private CircleCandidate? TryFit(HObject contour, PixelPoint coarse, int objectIndex)
    {
        try
        {
            HOperatorSet.FitCircleContourXld(
                contour,
                "atukey",
                -1,
                2.0,
                0,
                5,
                2.0,
                out HTuple rows,
                out HTuple columns,
                out HTuple radii,
                out HTuple startPhi,
                out HTuple endPhi,
                out HTuple pointOrder);
            using (rows)
            using (columns)
            using (radii)
            using (startPhi)
            using (endPhi)
            using (pointOrder)
            {
                if (radii.Length == 0)
                {
                    return null;
                }

                var row = rows[0].D;
                var column = columns[0].D;
                var radius = radii[0].D;
                var shift = Math.Sqrt(Math.Pow(row - coarse.Row, 2) + Math.Pow(column - coarse.Column, 2));
                if (radius < options.MinimumRadiusPx || radius > options.MaximumRadiusPx ||
                    shift > options.MaximumCenterShiftPx)
                {
                    return null;
                }

                HOperatorSet.GetContourXld(contour, out HTuple contourRows, out HTuple contourColumns);
                using (contourRows)
                using (contourColumns)
                {
                    if (contourRows.Length < 8)
                    {
                        return null;
                    }

                    var rowValues = contourRows.ToDArr();
                    var columnValues = contourColumns.ToDArr();
                    var residual = Math.Sqrt(Enumerable.Range(0, rowValues.Length)
                        .Select(index =>
                        {
                            var measuredRadius = Math.Sqrt(
                                Math.Pow(rowValues[index] - row, 2) + Math.Pow(columnValues[index] - column, 2));
                            return Math.Pow(measuredRadius - radius, 2);
                        })
                        .Average());
                    var cost = residual + 0.1 * shift + 0.01 * Math.Abs(radius -
                        (options.MinimumRadiusPx + options.MaximumRadiusPx) / 2.0);
                    return new CircleCandidate(objectIndex, row, column, radius, residual, cost);
                }
            }
        }
        catch (HOperatorException)
        {
            return null;
        }
    }

    private sealed record CircleCandidate(
        int ObjectIndex,
        double Row,
        double Column,
        double Radius,
        double ResidualPx,
        double Cost);
}
