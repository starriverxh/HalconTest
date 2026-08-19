using VisionSystem.Domain;
using VisionSystem.Infrastructure;

namespace VisionSystem.Infrastructure.Tests;

public static class Program
{
    private static int _passed;

    public static async Task<int> Main()
    {
        await RunAsync("Package verifier accepts correct hashes", TestCorrectPackageAsync);
        await RunAsync("Package verifier detects a tampered file", TestTamperedPackageAsync);
        await RunAsync("Package verifier rejects path traversal", TestPackagePathTraversalAsync);
        await RunAsync("Verified repository loads an active hash-matched recipe", TestActiveRepositoryAsync);
        await RunAsync("Verified repository gates missing entry and wrong activation hash", TestActiveIndexAndHashGatesAsync);
        await RunAsync("Verified repository rejects active path traversal and file tampering", TestRepositoryTraversalAndTamperingAsync);
        await RunAsync("Verified repository gates index subject and unhashed recipe", TestRepositorySubjectAndManifestGatesAsync);

        Console.WriteLine($"Infrastructure tests passed: {_passed}/7");
        return 0;
    }

    private static async Task TestCorrectPackageAsync()
    {
        using var temporary = new TemporaryDirectory();
        var package = await CreateRecipePackageAsync(temporary.Path, "recipe-a");
        var verifier = new PackageIntegrityVerifier();
        var report = await verifier.VerifyAsync(package.Directory);

        True(report.IsValid);
        Equal(0, report.Errors.Count);
        Equal(package.ExpectedPackageHash, report.PackageSha256);
        Equal("recipe-a", report.Manifest!.SubjectId);
        Equal(PackageLifecycleState.Published, report.Manifest.State);
    }

    private static async Task TestTamperedPackageAsync()
    {
        using var temporary = new TemporaryDirectory();
        var package = await CreateRecipePackageAsync(temporary.Path, "recipe-a");
        await File.AppendAllTextAsync(package.RecipePath, Environment.NewLine + " ");
        var report = await new PackageIntegrityVerifier().VerifyAsync(package.Directory);

        False(report.IsValid);
        Contains(report.Errors, "SHA-256 mismatch: recipe.json");
    }

    private static async Task TestPackagePathTraversalAsync()
    {
        using var temporary = new TemporaryDirectory();
        var packageDirectory = Path.Combine(temporary.Path, "malicious");
        Directory.CreateDirectory(packageDirectory);
        var manifest = new PackageManifest
        {
            PackageId = "malicious",
            PackageType = "VisionRecipe",
            SubjectId = "recipe-a",
            Version = "1.0.0",
            State = PackageLifecycleState.Published,
            CreatedUtc = DateTimeOffset.UnixEpoch,
            CreatedBy = "test",
            Files = new Dictionary<string, string>
            {
                ["../outside.txt"] = new string('0', 64)
            }
        };
        await AtomicJsonStore.WriteAsync(Path.Combine(packageDirectory, "manifest.json"), manifest);
        var report = await new PackageIntegrityVerifier().VerifyAsync(packageDirectory);

        False(report.IsValid);
        True(report.Errors.Any(error => error.Contains("escapes its directory", StringComparison.Ordinal)));
        Throws<InvalidDataException>(() =>
            PackageIntegrityVerifier.ResolvePackageFile(packageDirectory, "../outside.txt"));
        Throws<InvalidDataException>(() =>
            PackageIntegrityVerifier.ResolvePackageFile(packageDirectory, Path.GetFullPath("outside.txt")));
    }

    private static async Task TestActiveRepositoryAsync()
    {
        using var temporary = new TemporaryDirectory();
        var packagesRoot = Path.Combine(temporary.Path, "packages");
        var package = await CreateRecipePackageAsync(packagesRoot, "recipe-a");
        var indexPath = Path.Combine(temporary.Path, "active", "index.json");
        await WriteActiveIndexAsync(
            indexPath,
            "recipe-a",
            Path.GetRelativePath(packagesRoot, package.Directory),
            package.ExpectedPackageHash);
        var repository = new VerifiedRecipePackageRepository(
            packagesRoot,
            indexPath,
            new PackageIntegrityVerifier());

        var recipe = await repository.GetAsync("recipe-a", CancellationToken.None);
        Equal("recipe-a", recipe.RecipeId);
        Equal("PCB-TRAINING", recipe.ProductCode);
        var caseInsensitiveRecipe = await repository.GetAsync("RECIPE-A", CancellationToken.None);
        Equal("recipe-a", caseInsensitiveRecipe.RecipeId);
        var listed = await repository.ListAsync(CancellationToken.None);
        Equal(1, listed.Count);
        Equal("recipe-a", listed[0]);
    }

    private static async Task TestActiveIndexAndHashGatesAsync()
    {
        using var temporary = new TemporaryDirectory();
        var packagesRoot = Path.Combine(temporary.Path, "packages");
        var package = await CreateRecipePackageAsync(packagesRoot, "recipe-a");
        var indexPath = Path.Combine(temporary.Path, "active", "index.json");
        await WriteActiveIndexAsync(
            indexPath,
            "recipe-a",
            Path.GetRelativePath(packagesRoot, package.Directory),
            new string('f', 64));
        var repository = new VerifiedRecipePackageRepository(
            packagesRoot,
            indexPath,
            new PackageIntegrityVerifier());

        await ThrowsAsync<InvalidDataException>(() =>
            repository.GetAsync("recipe-a", CancellationToken.None));
        await ThrowsAsync<FileNotFoundException>(() =>
            repository.GetAsync("not-active", CancellationToken.None));
    }

    private static async Task TestRepositoryTraversalAndTamperingAsync()
    {
        using var temporary = new TemporaryDirectory();
        var packagesRoot = Path.Combine(temporary.Path, "packages");
        var package = await CreateRecipePackageAsync(packagesRoot, "recipe-a");
        var indexPath = Path.Combine(temporary.Path, "active", "index.json");
        await WriteActiveIndexAsync(
            indexPath,
            "recipe-a",
            "../escape",
            package.ExpectedPackageHash);
        var repository = new VerifiedRecipePackageRepository(
            packagesRoot,
            indexPath,
            new PackageIntegrityVerifier());
        await ThrowsAsync<InvalidDataException>(() =>
            repository.GetAsync("recipe-a", CancellationToken.None));

        await WriteActiveIndexAsync(
            indexPath,
            "recipe-a",
            Path.GetRelativePath(packagesRoot, package.Directory),
            package.ExpectedPackageHash);
        await File.AppendAllTextAsync(package.RecipePath, " ");
        await ThrowsAsync<InvalidDataException>(() =>
            repository.GetAsync("recipe-a", CancellationToken.None));
    }

    private static async Task TestRepositorySubjectAndManifestGatesAsync()
    {
        using var temporary = new TemporaryDirectory();
        var packagesRoot = Path.Combine(temporary.Path, "packages");
        var package = await CreateRecipePackageAsync(packagesRoot, "recipe-a");
        var indexPath = Path.Combine(temporary.Path, "active", "index.json");
        await WriteActiveIndexAsync(
            indexPath,
            "recipe-a",
            Path.GetRelativePath(packagesRoot, package.Directory),
            package.ExpectedPackageHash,
            entrySubjectId: "different-subject");
        var repository = new VerifiedRecipePackageRepository(
            packagesRoot,
            indexPath,
            new PackageIntegrityVerifier());
        await ThrowsAsync<InvalidDataException>(() =>
            repository.GetAsync("recipe-a", CancellationToken.None));

        var unprotected = await CreateRecipePackageAsync(
            packagesRoot,
            "recipe-unprotected",
            protectRecipe: false);
        await WriteActiveIndexAsync(
            indexPath,
            "recipe-unprotected",
            Path.GetRelativePath(packagesRoot, unprotected.Directory),
            unprotected.ExpectedPackageHash);
        repository = new VerifiedRecipePackageRepository(
            packagesRoot,
            indexPath,
            new PackageIntegrityVerifier());
        await ThrowsAsync<InvalidDataException>(() =>
            repository.GetAsync("recipe-unprotected", CancellationToken.None));
    }

    private static async Task<CreatedPackage> CreateRecipePackageAsync(
        string packagesRoot,
        string recipeId,
        bool protectRecipe = true)
    {
        var directory = Path.Combine(packagesRoot, recipeId, "1.0.0");
        Directory.CreateDirectory(Path.Combine(directory, "models"));
        var recipePath = Path.Combine(directory, "recipe.json");
        var modelPath = Path.Combine(directory, "models", "board.shm");
        var recipe = new VisionRecipe
        {
            RecipeId = recipeId,
            ProductCode = "PCB-TRAINING",
            Version = 1,
            Status = RecipeLifecycleStatuses.Published,
            UpdatedUtc = DateTimeOffset.UnixEpoch
        };
        await AtomicJsonStore.WriteAsync(recipePath, recipe);
        await File.WriteAllBytesAsync(modelPath, [1, 3, 3, 7, 9, 11]);

        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["models/board.shm"] = await PackageIntegrityVerifier.ComputeFileSha256Async(modelPath)
        };
        if (protectRecipe)
        {
            hashes["recipe.json"] = await PackageIntegrityVerifier.ComputeFileSha256Async(recipePath);
        }
        var manifest = new PackageManifest
        {
            PackageId = $"{recipeId}-1.0.0",
            PackageType = "VisionRecipe",
            SubjectId = recipeId,
            Version = "1.0.0",
            State = PackageLifecycleState.Published,
            CreatedUtc = DateTimeOffset.UnixEpoch,
            CreatedBy = "test-suite",
            Files = hashes
        };
        var expectedPackageHash = PackageIntegrityVerifier.ComputePackageHash(manifest, hashes);
        await AtomicJsonStore.WriteAsync(Path.Combine(directory, "manifest.json"), manifest);
        return new CreatedPackage(directory, recipePath, expectedPackageHash);
    }

    private static Task WriteActiveIndexAsync(
        string indexPath,
        string recipeId,
        string relativeDirectory,
        string packageHash,
        string? entrySubjectId = null)
    {
        var index = new ActivePackageIndex
        {
            Recipes = new Dictionary<string, ActivePackageEntry>(StringComparer.OrdinalIgnoreCase)
            {
                [recipeId] = new ActivePackageEntry
                {
                    SubjectId = entrySubjectId ?? recipeId,
                    RelativePackageDirectory = relativeDirectory.Replace('\\', '/'),
                    PackageSha256 = packageHash,
                    ActivatedUtc = DateTimeOffset.UnixEpoch,
                    ActivatedBy = "test-suite"
                }
            }
        };
        return AtomicJsonStore.WriteAsync(indexPath, index);
    }

    private static async Task RunAsync(string name, Func<Task> test)
    {
        await test();
        _passed++;
        Console.WriteLine($"PASS {name}");
    }

    private static void True(bool value)
    {
        if (!value)
        {
            throw new InvalidOperationException("Expected true.");
        }
    }

    private static void False(bool value) => True(!value);

    private static void Equal<T>(T expected, T actual) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
        }
    }

    private static void Contains(IEnumerable<string> values, string expected)
    {
        if (values.All(value => !value.Contains(expected, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Expected a value containing '{expected}'.");
        }
    }

    private static void Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static async Task ThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private sealed record CreatedPackage(
        string Directory,
        string RecipePath,
        string ExpectedPackageHash);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"vision-v2-infrastructure-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
