using HalconDotNet;
using Vision.Service.Configuration;

namespace Vision.Service.Vision;

public sealed class PcbHoleLocator
{
    public IReadOnlyList<ImagePoint> Locate(HObject grayImage, HoleDetectionOptions options)
    {
        HObject analysisImage = grayImage;
        HObject? roiRegion = null;
        HObject? reducedImage = null;
        try
        {
            if (options.SearchRoi is not null)
            {
                HOperatorSet.GenRectangle1(
                    out HObject generatedRoi,
                    options.SearchRoi.Row1,
                    options.SearchRoi.Column1,
                    options.SearchRoi.Row2,
                    options.SearchRoi.Column2);
                roiRegion = generatedRoi;
                HOperatorSet.ReduceDomain(grayImage, generatedRoi, out HObject reduced);
                reducedImage = reduced;
                analysisImage = reduced;
            }

            HOperatorSet.Threshold(analysisImage, out HObject candidateRegion, options.ThresholdMin, options.ThresholdMax);
            using (candidateRegion)
            {
                HOperatorSet.Connection(candidateRegion, out HObject connectedRegions);
                using (connectedRegions)
                {
                    var minimumArea = Math.PI * options.MinRadiusPx * options.MinRadiusPx;
                    var maximumArea = Math.PI * options.MaxRadiusPx * options.MaxRadiusPx;
                    using var features = new HTuple(new[] { "area", "circularity" });
                    using var minimums = new HTuple(new[] { minimumArea, options.MinCircularity });
                    using var maximums = new HTuple(new[] { maximumArea, 1.0 });
                    HOperatorSet.SelectShape(connectedRegions, out HObject selectedRegions, features, "and", minimums, maximums);
                    using (selectedRegions)
                    {
                        HOperatorSet.AreaCenter(selectedRegions, out HTuple areas, out HTuple rows, out HTuple columns);
                        using (areas)
                        using (rows)
                        using (columns)
                        {
                            HOperatorSet.Circularity(selectedRegions, out HTuple scores);
                            using (scores)
                            {
                                var areaValues = areas.ToDArr();
                                var rowValues = rows.ToDArr();
                                var columnValues = columns.ToDArr();
                                var scoreValues = scores.ToDArr();
                                return Enumerable.Range(0, columnValues.Length)
                                    .Select(index => new ImagePoint(
                                        rowValues[index],
                                        columnValues[index],
                                        Math.Sqrt(areaValues[index] / Math.PI),
                                        scoreValues[index]))
                                    .OrderBy(point => point.Column)
                                    .ToArray();
                            }
                        }
                    }
                }
            }
        }
        finally
        {
            reducedImage?.Dispose();
            roiRegion?.Dispose();
        }
    }
}
