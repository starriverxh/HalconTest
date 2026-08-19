using System.Security.Cryptography;

namespace VisionSystem.DatasetCli;

public sealed class DatasetVerifier
{
    public async Task<IReadOnlyList<VerifiedDatasetCase>> VerifyAsync(
        DatasetManifest manifest,
        string manifestPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(manifest.DatasetId) || string.IsNullOrWhiteSpace(manifest.Version))
        {
            throw new InvalidDataException("datasetId and version are required.");
        }

        if (manifest.Cases.Count == 0)
        {
            throw new InvalidDataException("Dataset contains no cases.");
        }

        var duplicateIds = manifest.Cases.GroupBy(item => item.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateIds.Length > 0)
        {
            throw new InvalidDataException($"Dataset case IDs are duplicated: {string.Join(", ", duplicateIds)}.");
        }

        var repositoryRoot = FindRepositoryRoot(manifestPath);
        var manifestDirectory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))
            ?? throw new InvalidDataException("Manifest has no parent directory.");
        var verified = new List<VerifiedDatasetCase>();
        foreach (var item in manifest.Cases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.ExpectedDisposition))
            {
                throw new InvalidDataException("Every dataset case needs id and expectedDisposition.");
            }

            var board = ResolveBelowRoot(repositoryRoot, manifestDirectory, item.BoardImage);
            var pins = ResolveBelowRoot(repositoryRoot, manifestDirectory, item.PinImage);
            await VerifyHashAsync(board, item.Sha256.BoardImage, cancellationToken).ConfigureAwait(false);
            await VerifyHashAsync(pins, item.Sha256.PinImage, cancellationToken).ConfigureAwait(false);
            verified.Add(new VerifiedDatasetCase(item, board, pins));
        }

        return verified;
    }

    private static string FindRepositoryRoot(string manifestPath)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(manifestPath))
            ?? throw new InvalidDataException("Manifest has no parent directory."));
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "v2")) &&
                Directory.Exists(Path.Combine(directory.FullName, "data")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidDataException("Dataset manifest is not below the HalconTest repository root.");
    }

    private static string ResolveBelowRoot(string root, string baseDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException($"Dataset image path must be relative: '{relativePath}'.");
        }

        var path = Path.GetFullPath(Path.Combine(baseDirectory, relativePath));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Dataset image escapes the repository: '{relativePath}'.");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Dataset image is missing.", path);
        }

        return path;
    }

    private static async Task VerifyHashAsync(
        string path,
        string expected,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expected) || expected.Length != 64)
        {
            throw new InvalidDataException($"Dataset SHA-256 is invalid for '{path}'.");
        }

        await using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Dataset image hash mismatch: {path}{Environment.NewLine}expected={expected}{Environment.NewLine}actual={actual}");
        }
    }
}
