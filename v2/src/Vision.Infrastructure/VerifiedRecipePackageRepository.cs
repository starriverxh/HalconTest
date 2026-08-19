using System.Text.Json;
using System.Text.Json.Serialization;
using VisionSystem.Application;
using VisionSystem.Domain;

namespace VisionSystem.Infrastructure;

/// <summary>
/// Loads only an explicitly activated, hash-verified immutable recipe package.
/// A failed candidate never replaces the previous active package.
/// </summary>
public sealed class VerifiedRecipePackageRepository(
    string packagesRoot,
    string activeIndexPath,
    PackageIntegrityVerifier verifier) : IRecipeRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _packagesRoot = Path.GetFullPath(packagesRoot);
    private readonly string _activeIndexPath = Path.GetFullPath(activeIndexPath);

    public async Task<VisionRecipe> GetAsync(string recipeId, CancellationToken cancellationToken)
    {
        var index = await ReadIndexAsync(cancellationToken).ConfigureAwait(false);
        var entry = index.Recipes
            .Where(item => string.Equals(item.Key, recipeId, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Value)
            .FirstOrDefault();
        if (entry is null)
        {
            throw new FileNotFoundException($"Recipe '{recipeId}' has no activated package.");
        }

        if (!string.Equals(entry.SubjectId, recipeId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Active package index subject does not match the requested recipe ID.");
        }

        var packageDirectory = ResolveBelowRoot(_packagesRoot, entry.RelativePackageDirectory);
        var report = await verifier.VerifyAsync(packageDirectory, cancellationToken).ConfigureAwait(false);
        if (!report.IsValid)
        {
            throw new InvalidDataException(
                $"Active recipe package failed integrity verification: {string.Join("; ", report.Errors)}");
        }

        if (!report.PackageSha256.Equals(entry.PackageSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Active recipe package fingerprint differs from activation record.");
        }

        var manifest = report.Manifest
            ?? throw new InvalidDataException("Verified recipe package did not provide a manifest.");
        if (!string.Equals(manifest.SubjectId, recipeId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Recipe package subject does not match the requested recipe ID.");
        }

        if (manifest.Files.Keys.All(path =>
                !string.Equals(path.Replace('\\', '/'), "recipe.json", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("recipe.json is not protected by the package manifest.");
        }

        var recipePath = PackageIntegrityVerifier.ResolvePackageFile(packageDirectory, "recipe.json");
        await using var stream = File.OpenRead(recipePath);
        var recipe = await JsonSerializer.DeserializeAsync<VisionRecipe>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("recipe.json is empty.");
        if (!string.Equals(recipe.RecipeId, recipeId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("recipe.json ID does not match its package subject.");
        }

        return recipe;
    }

    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken)
    {
        var index = await ReadIndexAsync(cancellationToken).ConfigureAwait(false);
        return index.Recipes.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<PackageVerificationReport> VerifyCandidateAsync(
        string relativePackageDirectory,
        CancellationToken cancellationToken = default)
    {
        var path = ResolveBelowRoot(_packagesRoot, relativePackageDirectory);
        return await verifier.VerifyAsync(path, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ActivePackageIndex> ReadIndexAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_activeIndexPath))
        {
            throw new FileNotFoundException("Active package index was not found.", _activeIndexPath);
        }

        await using var stream = File.OpenRead(_activeIndexPath);
        return await JsonSerializer.DeserializeAsync<ActivePackageIndex>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("Active package index is empty.");
    }

    private static string ResolveBelowRoot(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException("Package directory must be relative to the configured package root.");
        }

        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Package directory escapes the configured package root.");
        }

        return path;
    }
}
