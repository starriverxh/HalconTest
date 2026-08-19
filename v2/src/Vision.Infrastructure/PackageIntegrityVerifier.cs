using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VisionSystem.Infrastructure;

public sealed class PackageIntegrityVerifier
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<PackageVerificationReport> VerifyAsync(
        string packageDirectory,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetFullPath(packageDirectory);
        var errors = new List<string>();
        var manifestPath = Path.Combine(directory, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return new PackageVerificationReport(false, directory, string.Empty, null,
                ["manifest.json is missing."]);
        }

        PackageManifest? manifest;
        try
        {
            await using var stream = File.OpenRead(manifestPath);
            manifest = await JsonSerializer.DeserializeAsync<PackageManifest>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            return new PackageVerificationReport(false, directory, string.Empty, null,
                [$"manifest.json cannot be read: {exception.Message}"]);
        }

        if (manifest is null)
        {
            return new PackageVerificationReport(false, directory, string.Empty, null,
                ["manifest.json is empty."]);
        }

        if (manifest.State is not (PackageLifecycleState.Published or PackageLifecycleState.Active))
        {
            errors.Add($"Package state '{manifest.State}' is not activatable.");
        }

        if (manifest.Files.Count == 0)
        {
            errors.Add("Manifest contains no files.");
        }

        var verifiedHashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path;
            try
            {
                path = ResolvePackageFile(directory, item.Key);
            }
            catch (InvalidDataException exception)
            {
                errors.Add(exception.Message);
                continue;
            }

            if (!File.Exists(path))
            {
                errors.Add($"Package file is missing: {item.Key}");
                continue;
            }

            var actual = await ComputeFileSha256Async(path, cancellationToken).ConfigureAwait(false);
            if (!actual.Equals(item.Value, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"SHA-256 mismatch: {item.Key}");
            }

            verifiedHashes[item.Key.Replace('\\', '/')] = actual;
        }

        var packageHash = ComputePackageHash(manifest, verifiedHashes);
        return new PackageVerificationReport(errors.Count == 0, directory, packageHash, manifest, errors);
    }

    public static string ResolvePackageFile(string packageDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException($"Package path is not relative: '{relativePath}'.");
        }

        var root = Path.GetFullPath(packageDirectory);
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Package path escapes its directory: '{relativePath}'.");
        }

        return path;
    }

    public static async Task<string> ComputeFileSha256Async(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string ComputePackageHash(IEnumerable<KeyValuePair<string, string>> fileHashes)
    {
        var canonical = string.Join(
            "\n",
            fileHashes
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => $"{item.Key.Replace('\\', '/')}:{item.Value.ToLowerInvariant()}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    public static string ComputePackageHash(
        PackageManifest manifest,
        IEnumerable<KeyValuePair<string, string>> fileHashes)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var hardware = manifest.Hardware is null
            ? string.Empty
            : JsonSerializer.Serialize(manifest.Hardware, JsonOptions);
        var header = string.Join("\n", new[]
        {
            $"schemaVersion:{manifest.SchemaVersion}",
            $"packageId:{manifest.PackageId}",
            $"packageType:{manifest.PackageType}",
            $"subjectId:{manifest.SubjectId}",
            $"version:{manifest.Version}",
            $"state:{manifest.State}",
            $"createdUtc:{manifest.CreatedUtc.ToUniversalTime():O}",
            $"createdBy:{manifest.CreatedBy}",
            $"validationReport:{manifest.ValidationReport ?? string.Empty}",
            $"hardware:{hardware}"
        });
        var fileBody = string.Join(
            "\n",
            fileHashes
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => $"{item.Key.Replace('\\', '/')}:{item.Value.ToLowerInvariant()}"));
        var canonical = header + "\nfiles\n" + fileBody;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
