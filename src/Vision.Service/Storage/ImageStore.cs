using HalconDotNet;

namespace Vision.Service.Storage;

public sealed class ImageStore
{
    private readonly string _imagesDirectory;

    public ImageStore(string runtimeDirectory)
    {
        _imagesDirectory = Path.Combine(runtimeDirectory, "images");
        Directory.CreateDirectory(_imagesDirectory);
    }

    public Task<string> SaveAsync(
        HObject image,
        string requestId,
        string classification,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var safeRequestId = MakeSafe(requestId);
        var safeClassification = MakeSafe(classification);
        var path = Path.Combine(
            _imagesDirectory,
            $"{DateTime.UtcNow:yyyyMMdd_HHmmssfff}_{safeRequestId}_{safeClassification}.png");
        HOperatorSet.WriteImage(image, "png", 0, path);
        return Task.FromResult(path);
    }

    private static string MakeSafe(string value)
    {
        var safe = string.Concat(value.Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_'));
        return string.IsNullOrWhiteSpace(safe) ? "unnamed" : safe[..Math.Min(safe.Length, 80)];
    }
}
