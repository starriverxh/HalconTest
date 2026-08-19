using HalconDotNet;
using VisionSystem.Application;
using VisionSystem.Domain;

namespace VisionSystem.Halcon.Frames;

/// <summary>
/// Owns exactly one HALCON image. The image accessor is internal so HALCON iconic
/// objects cannot escape through the Domain or Application contracts.
/// </summary>
public sealed class HalconFrame : IVisionFrame
{
    private HObject? _image;

    internal HalconFrame(
        HObject image,
        string frameId,
        CameraRole role,
        DateTimeOffset capturedUtc,
        int width,
        int height,
        string sourceName,
        AcquisitionProvenance provenance)
    {
        _image = image;
        FrameId = frameId;
        Role = role;
        CapturedUtc = capturedUtc;
        Width = width;
        Height = height;
        SourceName = sourceName;
        Provenance = provenance;
    }

    public string FrameId { get; }
    public CameraRole Role { get; }
    public DateTimeOffset CapturedUtc { get; }
    public int Width { get; }
    public int Height { get; }
    public string SourceName { get; }
    public AcquisitionProvenance Provenance { get; }

    internal HObject BorrowImage() =>
        _image ?? throw new ObjectDisposedException(nameof(HalconFrame));

    public void Dispose()
    {
        Interlocked.Exchange(ref _image, null)?.Dispose();
    }
}
