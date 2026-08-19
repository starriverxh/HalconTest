using HalconDotNet;
using VisionSystem.Application;
using VisionSystem.Domain;
using VisionSystem.Halcon.Frames;

namespace VisionSystem.Halcon.Acquisition;

/// <summary>
/// Deterministic offline source. It deliberately refuses non-file recipes so a
/// commissioning dataset can never masquerade as a hardware camera.
/// </summary>
public sealed class FileFrameSource(CameraRole role) : ICameraSource
{
    private CameraRecipe? _recipe;
    private long _sequence;

    public CameraRole Role { get; } = role;
    public string Name => $"HALCON file source ({Role})";
    public bool IsReady => _recipe is not null;

    public Task OpenAsync(CameraRecipe recipe, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (recipe.Mode != CameraMode.File)
        {
            throw new NotSupportedException(
                $"{nameof(FileFrameSource)} accepts CameraMode.File only; configured mode is {recipe.Mode}.");
        }

        EnsureImageExists(recipe.OfflineImagePath);
        _recipe = recipe;
        return Task.CompletedTask;
    }

    public Task<IVisionFrame> AcquireAsync(string? imageOverride, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var recipe = _recipe ?? throw new InvalidOperationException("File source is not open.");
        var imagePath = string.IsNullOrWhiteSpace(imageOverride)
            ? recipe.OfflineImagePath
            : Path.GetFullPath(imageOverride);
        EnsureImageExists(imagePath);

        HOperatorSet.ReadImage(out HObject image, imagePath);
        try
        {
            HOperatorSet.GetImageSize(image, out HTuple width, out HTuple height);
            using (width)
            using (height)
            {
                IVisionFrame frame = new HalconFrame(
                    image,
                    $"{Role.ToString().ToLowerInvariant()}-{Interlocked.Increment(ref _sequence):D8}",
                    Role,
                    DateTimeOffset.UtcNow,
                    width.I,
                    height.I,
                    Path.GetFullPath(imagePath),
                    AcquisitionProvenance.OfflineFile);
                image = null!;
                return Task.FromResult(frame);
            }
        }
        finally
        {
            image?.Dispose();
        }
    }

    public ValueTask DisposeAsync()
    {
        _recipe = null;
        return ValueTask.CompletedTask;
    }

    private static void EnsureImageExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new FileNotFoundException("Offline HALCON image was not found.", path);
        }
    }
}
