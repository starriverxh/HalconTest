using System.Text.Json;
using System.Text.Json.Serialization;
using VisionSystem.Application;
using VisionSystem.Domain;

namespace VisionSystem.Infrastructure;

public sealed class JsonRecipeRepository(string recipesDirectory) : IRecipeRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _recipesDirectory = Path.GetFullPath(recipesDirectory);

    public async Task<VisionRecipe> GetAsync(string recipeId, CancellationToken cancellationToken)
    {
        var path = GetRecipePath(recipeId);
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<VisionRecipe>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException($"Recipe '{path}' is empty.");
    }

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(_recipesDirectory))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        IReadOnlyList<string> result = Directory
            .EnumerateFiles(_recipesDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null)
            .Cast<string>()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Task.FromResult(result);
    }

    private string GetRecipePath(string recipeId)
    {
        if (string.IsNullOrWhiteSpace(recipeId) || recipeId.Any(character =>
                !(char.IsLetterOrDigit(character) || character is '-' or '_')))
        {
            throw new InvalidDataException("RecipeId may contain only letters, digits, '-' and '_'.");
        }

        var path = Path.GetFullPath(Path.Combine(_recipesDirectory, recipeId + ".json"));
        if (!path.StartsWith(_recipesDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Recipe path escapes the configured repository.");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Vision recipe was not found.", path);
        }

        return path;
    }
}
