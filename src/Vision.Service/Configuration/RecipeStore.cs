using System.Text.Json;
using Vision.Service.Workbench;

namespace Vision.Service.Configuration;

/// <summary>
/// Persists an engineering recipe with optimistic version checking, a timestamped
/// history copy, and same-directory atomic replacement of the active JSON file.
/// </summary>
public sealed class RecipeStore(string recipePath, string runtimeDirectory)
{
    private readonly string _recipePath = Path.GetFullPath(recipePath);
    private readonly string _historyDirectory = CreateHistoryDirectory(runtimeDirectory);
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    public async Task<RecipeSnapshotResult> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var rawRecipe = await LoadRawAsync(cancellationToken).ConfigureAwait(false);
        var recipeDirectory = Path.GetDirectoryName(_recipePath)
            ?? throw new InvalidOperationException("Recipe directory is unavailable.");
        var resolved = rawRecipe.ResolvePaths(recipeDirectory);
        var roi = rawRecipe.HoleDetection.SearchRoi;
        return new RecipeSnapshotResult(
            _recipePath,
            rawRecipe.RecipeName,
            rawRecipe.RecipeVersion,
            rawRecipe.UpdatedUtc,
            roi is null ? null : new RectangleRoi(roi.Row1, roi.Column1, roi.Row2, roi.Column2),
            rawRecipe.HoleDetection.ThresholdMin,
            rawRecipe.HoleDetection.ThresholdMax,
            rawRecipe.HoleDetection.MinRadiusPx,
            rawRecipe.HoleDetection.MaxRadiusPx,
            rawRecipe.HoleDetection.MinCircularity,
            rawRecipe.HoleDetection.ExpectedCount,
            rawRecipe.Localization.Mode.Equals("ShapeModel", StringComparison.OrdinalIgnoreCase),
            resolved.Localization.ModelFile,
            rawRecipe.Localization.AngleStartDeg,
            rawRecipe.Localization.AngleExtentDeg,
            rawRecipe.Localization.MinScore);
    }

    public async Task<RecipeSaveResult> SaveEngineeringAsync(
        RecipeSaveRequest request,
        CancellationToken cancellationToken)
    {
        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var rawRecipe = await LoadRawAsync(cancellationToken).ConfigureAwait(false);
            if (request.ExpectedVersion is > 0 && request.ExpectedVersion != rawRecipe.RecipeVersion)
            {
                throw new InvalidOperationException(
                    $"Recipe version conflict: UI expected v{request.ExpectedVersion}, " +
                    $"but disk contains v{rawRecipe.RecipeVersion}. Reload before saving.");
            }

            var recipeDirectory = Path.GetDirectoryName(_recipePath)
                ?? throw new InvalidOperationException("Recipe directory is unavailable.");
            var localizationMode = request.UseShapeModel ? "ShapeModel" : "BlobRow";
            var modelFile = request.UseShapeModel
                ? MakeRecipeRelativePath(recipeDirectory, request.ShapeModelPath)
                : rawRecipe.Localization.ModelFile;
            var updated = rawRecipe with
            {
                RecipeVersion = checked(rawRecipe.RecipeVersion + 1),
                UpdatedUtc = DateTimeOffset.UtcNow,
                HoleDetection = rawRecipe.HoleDetection with
                {
                    ThresholdMin = request.ThresholdMin,
                    ThresholdMax = request.ThresholdMax,
                    MinRadiusPx = request.MinRadiusPx,
                    MaxRadiusPx = request.MaxRadiusPx,
                    MinCircularity = request.MinCircularity,
                    ExpectedCount = request.ExpectedCount,
                    SearchRoi = request.Roi is null
                        ? null
                        : new RectangleRoiOptions
                        {
                            Row1 = request.Roi.Normalize().Row1,
                            Column1 = request.Roi.Normalize().Column1,
                            Row2 = request.Roi.Normalize().Row2,
                            Column2 = request.Roi.Normalize().Column2
                        }
                },
                Localization = rawRecipe.Localization with
                {
                    Mode = localizationMode,
                    ModelFile = modelFile,
                    AngleStartDeg = request.AngleStartDeg,
                    AngleExtentDeg = request.AngleExtentDeg,
                    MinScore = request.MatchMinScore
                }
            };

            updated.ResolvePaths(recipeDirectory).Validate();
            Directory.CreateDirectory(recipeDirectory);
            var backupPath = Path.Combine(
                _historyDirectory,
                $"{MakeSafeName(rawRecipe.RecipeName)}_v{rawRecipe.RecipeVersion}_{DateTime.UtcNow:yyyyMMdd_HHmmssfff}.json");
            File.Copy(_recipePath, backupPath, false);

            var temporaryPath = Path.Combine(recipeDirectory, $".{Path.GetFileName(_recipePath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var stream = new FileStream(
                    temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(stream, updated, RecipeJson.Options, cancellationToken)
                        .ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                File.Move(temporaryPath, _recipePath, true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            return new RecipeSaveResult(
                _recipePath,
                updated.RecipeName,
                updated.RecipeVersion,
                updated.UpdatedUtc,
                backupPath,
                true,
                "Recipe saved atomically. Restart Vision.Service to activate the new version.");
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private async Task<VisionRecipe> LoadRawAsync(CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(_recipePath);
        return await JsonSerializer.DeserializeAsync<VisionRecipe>(stream, RecipeJson.Options, cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidDataException($"Recipe '{_recipePath}' is empty.");
    }

    private static string MakeRecipeRelativePath(string recipeDirectory, string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
        {
            throw new FileNotFoundException("A saved .shm model is required for ShapeModel production mode.", modelPath);
        }

        return Path.GetRelativePath(recipeDirectory, Path.GetFullPath(modelPath)).Replace('\\', '/');
    }

    private static string CreateHistoryDirectory(string runtimeDirectory)
    {
        var path = Path.Combine(Path.GetFullPath(runtimeDirectory), "recipe-history");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string MakeSafeName(string value) => string.Concat(value.Select(character =>
        char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_'));
}
