using HalconDotNet;
using Vision.Service.Configuration;

namespace Vision.Service.Vision;

public sealed class ImageQualityInspector
{
    public ImageQualityResult Inspect(HObject grayImage, ImageQualityOptions options)
    {
        HOperatorSet.GetDomain(grayImage, out HObject domain);
        using (domain)
        {
            HOperatorSet.Intensity(domain, grayImage, out HTuple meanTuple, out HTuple deviationTuple);
            using (meanTuple)
            using (deviationTuple)
            {
                HOperatorSet.SobelAmp(grayImage, out HObject edgeAmplitude, "sum_abs", 3);
                using (edgeAmplitude)
                {
                    HOperatorSet.Intensity(domain, edgeAmplitude, out HTuple edgeMeanTuple, out HTuple edgeDeviationTuple);
                    using (edgeMeanTuple)
                    using (edgeDeviationTuple)
                    {
                        HOperatorSet.Threshold(grayImage, out HObject saturatedRegion, options.SaturationGray, 255);
                        using (saturatedRegion)
                        {
                            HOperatorSet.AreaCenter(domain, out HTuple domainAreaTuple, out HTuple domainRowTuple, out HTuple domainColumnTuple);
                            HOperatorSet.AreaCenter(saturatedRegion, out HTuple saturatedAreaTuple, out HTuple saturatedRowTuple, out HTuple saturatedColumnTuple);
                            using (domainAreaTuple)
                            using (domainRowTuple)
                            using (domainColumnTuple)
                            using (saturatedAreaTuple)
                            using (saturatedRowTuple)
                            using (saturatedColumnTuple)
                            {
                                var domainArea = domainAreaTuple.Length == 0 ? 0 : domainAreaTuple.ToDArr().Sum();
                                var saturatedArea = saturatedAreaTuple.Length == 0 ? 0 : saturatedAreaTuple.ToDArr().Sum();
                                var saturationFraction = domainArea <= 0 ? 1 : saturatedArea / domainArea;
                                var mean = meanTuple.D;
                                var deviation = deviationTuple.D;
                                var edgeMean = edgeMeanTuple.D;
                                var failures = new List<string>();
                                if (mean < options.MinimumMeanGray || mean > options.MaximumMeanGray)
                                {
                                    failures.Add("IMAGE_MEAN_GRAY_OUT_OF_RANGE");
                                }

                                if (deviation < options.MinimumGrayDeviation)
                                {
                                    failures.Add("IMAGE_CONTRAST_TOO_LOW");
                                }

                                if (edgeMean < options.MinimumMeanEdgeAmplitude)
                                {
                                    failures.Add("IMAGE_BLUR_OR_NO_FEATURE");
                                }

                                if (saturationFraction > options.MaximumSaturatedFraction)
                                {
                                    failures.Add("IMAGE_SATURATED");
                                }

                                return new ImageQualityResult(
                                    failures.Count == 0,
                                    mean,
                                    deviation,
                                    edgeMean,
                                    saturationFraction,
                                    failures);
                            }
                        }
                    }
                }
            }
        }
    }
}
