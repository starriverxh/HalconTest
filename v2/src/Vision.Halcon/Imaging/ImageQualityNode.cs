using HalconDotNet;
using VisionSystem.Domain;

namespace VisionSystem.Halcon.Imaging;

internal sealed record ImageQualityNodeResult(
    ImageQualityMeasurement Measurement,
    IReadOnlyList<VisionFailure> Failures,
    IReadOnlyDictionary<string, double> Metrics);

internal sealed class ImageQualityNode
{
    public ImageQualityNodeResult Evaluate(
        HObject grayImage,
        ImageQualityRecipe recipe,
        RectangleRoi? roi,
        string stage)
    {
        HObject analysisImage = grayImage;
        HObject? roiRegion = null;
        HObject? reduced = null;
        try
        {
            if (roi is { IsValid: true })
            {
                HOperatorSet.GenRectangle1(out HObject region, roi.Row1, roi.Column1, roi.Row2, roi.Column2);
                roiRegion = region;
                HOperatorSet.ReduceDomain(grayImage, region, out HObject reducedImage);
                reduced = reducedImage;
                analysisImage = reducedImage;
            }

            HOperatorSet.GetDomain(analysisImage, out HObject domain);
            using (domain)
            {
                HOperatorSet.Intensity(domain, analysisImage, out HTuple mean, out HTuple deviation);
                using (mean)
                using (deviation)
                {
                    HOperatorSet.SobelAmp(analysisImage, out HObject edgeAmplitude, "sum_abs", 3);
                    using (edgeAmplitude)
                    {
                        HOperatorSet.Intensity(domain, edgeAmplitude, out HTuple edgeMean, out HTuple edgeDeviation);
                        using (edgeMean)
                        using (edgeDeviation)
                        {
                            HOperatorSet.Threshold(
                                analysisImage,
                                out HObject saturated,
                                recipe.SaturationGray,
                                255);
                            using (saturated)
                            {
                                var domainArea = ReadArea(domain);
                                var saturatedArea = ReadArea(saturated);
                                var saturatedFraction = domainArea <= 0 ? 1.0 : saturatedArea / domainArea;
                                var measurement = new ImageQualityMeasurement(
                                    mean.D,
                                    deviation.D,
                                    edgeMean.D,
                                    saturatedFraction);
                                var failures = EvaluateFailures(measurement, recipe, stage);
                                return new ImageQualityNodeResult(
                                    measurement,
                                    failures,
                                    new Dictionary<string, double>
                                    {
                                        ["meanGray"] = measurement.MeanGray,
                                        ["grayDeviation"] = measurement.GrayDeviation,
                                        ["meanEdgeAmplitude"] = measurement.MeanEdgeAmplitude,
                                        ["saturatedFraction"] = measurement.SaturatedFraction,
                                        ["domainAreaPx"] = domainArea
                                    });
                            }
                        }
                    }
                }
            }
        }
        finally
        {
            reduced?.Dispose();
            roiRegion?.Dispose();
        }
    }

    private static double ReadArea(HObject region)
    {
        HOperatorSet.AreaCenter(region, out HTuple area, out HTuple row, out HTuple column);
        using (area)
        using (row)
        using (column)
        {
            return area.Length == 0 ? 0 : area.ToDArr().Sum();
        }
    }

    private static IReadOnlyList<VisionFailure> EvaluateFailures(
        ImageQualityMeasurement value,
        ImageQualityRecipe recipe,
        string stage)
    {
        var failures = new List<VisionFailure>();
        if (value.MeanGray < recipe.MinimumMeanGray)
        {
            failures.Add(new VisionFailure(VisionFailureCode.ImageTooDark,
                $"Mean gray {value.MeanGray:F2} is below {recipe.MinimumMeanGray:F2}.", true, stage));
        }

        if (value.MeanGray > recipe.MaximumMeanGray)
        {
            failures.Add(new VisionFailure(VisionFailureCode.ImageTooBright,
                $"Mean gray {value.MeanGray:F2} is above {recipe.MaximumMeanGray:F2}.", true, stage));
        }

        if (value.GrayDeviation < recipe.MinimumGrayDeviation)
        {
            failures.Add(new VisionFailure(VisionFailureCode.ImageLowContrast,
                $"Gray deviation {value.GrayDeviation:F2} is below {recipe.MinimumGrayDeviation:F2}.", true, stage));
        }

        if (value.MeanEdgeAmplitude < recipe.MinimumMeanEdgeAmplitude)
        {
            failures.Add(new VisionFailure(VisionFailureCode.ImageBlurred,
                $"Mean edge amplitude {value.MeanEdgeAmplitude:F2} is below {recipe.MinimumMeanEdgeAmplitude:F2}.", true, stage));
        }

        if (value.SaturatedFraction > recipe.MaximumSaturatedFraction)
        {
            failures.Add(new VisionFailure(VisionFailureCode.ImageSaturated,
                $"Saturated fraction {value.SaturatedFraction:P2} exceeds {recipe.MaximumSaturatedFraction:P2}.", true, stage));
        }

        return failures;
    }
}
