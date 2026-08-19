using System.Security.Cryptography;
using HalconDotNet;
using VisionSystem.Application;
using VisionSystem.Domain;
using VisionSystem.Halcon.Frames;

namespace VisionSystem.Halcon.Acquisition;

/// <summary>
/// Writes the acquired HALCON image before the frame is disposed. A SHA-256 is
/// recorded so a result can later prove which pixels were measured.
/// </summary>
public sealed class HalconFrameArchive(string runtimeDirectory) : IFrameArchive
{
    private readonly string _imageRoot = CreateImageRoot(runtimeDirectory);

    public async Task<FrameEvidence> ArchiveAsync(
        string requestId,
        IVisionFrame frame,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        cancellationToken.ThrowIfCancellationRequested();
        if (frame is not HalconFrame halconFrame)
        {
            throw new InvalidOperationException(
                $"{nameof(HalconFrameArchive)} can archive only {nameof(HalconFrame)} instances.");
        }

        var dayDirectory = Path.Combine(_imageRoot, frame.CapturedUtc.UtcDateTime.ToString("yyyyMMdd"));
        Directory.CreateDirectory(dayDirectory);
        var role = frame.Role.ToString().ToLowerInvariant();
        var stem = $"{frame.CapturedUtc:HHmmssfff}_{Sanitize(requestId)}_{role}_{Sanitize(frame.FrameId)}";
        var finalPath = Path.Combine(dayDirectory, stem + ".png");
        // HALCON appends an extension when the filename does not end in the
        // selected image format. Keep .png last so the temporary name is exact.
        var temporaryPath = Path.Combine(
            dayDirectory,
            $".{stem}.{Guid.NewGuid():N}.tmp.png");
        var movedByThisCall = false;
        var archiveCompleted = false;

        try
        {
            HOperatorSet.WriteImage(halconFrame.BorrowImage(), "png", 0, temporaryPath);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, finalPath, overwrite: false);
            movedByThisCall = true;
            var sha256 = await ComputeSha256Async(finalPath, cancellationToken).ConfigureAwait(false);
            var evidence = new FrameEvidence
            {
                FrameId = frame.FrameId,
                CameraRole = frame.Role.ToString(),
                Provenance = frame.Provenance,
                CapturedUtc = frame.CapturedUtc,
                Width = frame.Width,
                Height = frame.Height,
                SourceName = frame.SourceName,
                ArchivedImagePath = finalPath,
                Sha256 = sha256
            };
            archiveCompleted = true;
            return evidence;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            if (movedByThisCall && !archiveCompleted && File.Exists(finalPath))
            {
                File.Delete(finalPath);
            }
        }
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false))
            .ToLowerInvariant();
    }

    private static string CreateImageRoot(string runtimeDirectory)
    {
        var path = Path.Combine(Path.GetFullPath(runtimeDirectory), "images");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Sanitize(string value)
    {
        var safe = string.Concat(value.Take(96).Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_'));
        return string.IsNullOrWhiteSpace(safe) ? "frame" : safe;
    }
}
