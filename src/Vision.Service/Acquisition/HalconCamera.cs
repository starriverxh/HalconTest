using HalconDotNet;
using Vision.Service.Configuration;

namespace Vision.Service.Acquisition;

/// <summary>
/// Offline HALCON image source. It deliberately implements ICamera so that a future
/// GigE/USB implementation can replace it without changing the inspection pipeline.
/// </summary>
public sealed class HalconCamera(CameraOptions options) : ICamera
{
    private bool _isOpen;

    public string Name => $"HALCON file source ({Path.GetFileName(options.ImagePath)})";
    public bool IsReady => _isOpen;

    public Task OpenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!options.Mode.Equals("File", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("Only the offline File camera mode is implemented before hardware selection.");
        }

        EnsureImageExists(options.ImagePath);
        _isOpen = true;
        return Task.CompletedTask;
    }

    public Task<HObject> CaptureAsync(string? imagePathOverride = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_isOpen)
        {
            throw new InvalidOperationException("Camera is not open.");
        }

        var path = string.IsNullOrWhiteSpace(imagePathOverride)
            ? options.ImagePath
            : Path.GetFullPath(imagePathOverride);
        EnsureImageExists(path);
        HOperatorSet.ReadImage(out HObject image, path);
        return Task.FromResult(image);
    }

    private static void EnsureImageExists(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Offline camera image was not found.", path);
        }
    }

    public ValueTask DisposeAsync()
    {
        _isOpen = false;
        return ValueTask.CompletedTask;
    }
}
