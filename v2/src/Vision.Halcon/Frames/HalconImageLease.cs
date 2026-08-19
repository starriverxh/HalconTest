using HalconDotNet;

namespace VisionSystem.Halcon.Frames;

internal sealed class HalconImageLease : IDisposable
{
    private HObject? _image;

    private HalconImageLease(HObject image) => _image = image;

    public HObject Image => _image ?? throw new ObjectDisposedException(nameof(HalconImageLease));

    public static HalconImageLease CreateGray(HObject source)
    {
        HOperatorSet.CountChannels(source, out HTuple channels);
        using (channels)
        {
            if (channels.I == 1)
            {
                HOperatorSet.CopyImage(source, out HObject copy);
                return new HalconImageLease(copy);
            }

            HOperatorSet.Rgb1ToGray(source, out HObject gray);
            return new HalconImageLease(gray);
        }
    }

    public void Dispose() => Interlocked.Exchange(ref _image, null)?.Dispose();
}
