using HalconDotNet;
using VisionSystem.Domain;

namespace VisionSystem.Halcon.Segmentation;

internal sealed record BlobExtractionResult(
    IReadOnlyList<PixelPoint> Points,
    double MinimumCircularity,
    double MeanAreaPx,
    int ConnectedCount);

internal static class BlobCandidateExtractor
{
    public static BlobExtractionResult Extract(
        HObject grayImage,
        HObject searchRegion,
        double thresholdMin,
        double thresholdMax,
        double minimumArea,
        double maximumArea,
        double minimumCircularity)
    {
        HOperatorSet.ReduceDomain(grayImage, searchRegion, out HObject reduced);
        using (reduced)
        {
            HOperatorSet.Threshold(reduced, out HObject thresholdRegion, thresholdMin, thresholdMax);
            using (thresholdRegion)
            {
                HOperatorSet.Connection(thresholdRegion, out HObject connected);
                using (connected)
                {
                    HOperatorSet.CountObj(connected, out HTuple connectedCount);
                    using (connectedCount)
                    {
                        using var features = new HTuple(new[] { "area", "circularity" });
                        using var minimums = new HTuple(new[] { minimumArea, minimumCircularity });
                        using var maximums = new HTuple(new[] { maximumArea, 1.0 });
                        HOperatorSet.SelectShape(
                            connected,
                            out HObject selected,
                            features,
                            "and",
                            minimums,
                            maximums);
                        using (selected)
                        {
                            HOperatorSet.AreaCenter(selected, out HTuple areas, out HTuple rows, out HTuple columns);
                            HOperatorSet.Circularity(selected, out HTuple circularities);
                            using (areas)
                            using (rows)
                            using (columns)
                            using (circularities)
                            {
                                var areaValues = areas.ToDArr();
                                var rowValues = rows.ToDArr();
                                var columnValues = columns.ToDArr();
                                var circularityValues = circularities.ToDArr();
                                var points = Enumerable.Range(0, rowValues.Length)
                                    .Select(index => new PixelPoint(
                                        rowValues[index],
                                        columnValues[index],
                                        circularityValues[index]))
                                    .ToArray();
                                return new BlobExtractionResult(
                                    points,
                                    circularityValues.Length == 0 ? 0 : circularityValues.Min(),
                                    areaValues.Length == 0 ? 0 : areaValues.Average(),
                                    connectedCount.I);
                            }
                        }
                    }
                }
            }
        }
    }
}
