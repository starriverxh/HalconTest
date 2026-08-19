using System.Text.Json;
using System.Text.Json.Serialization;
using VisionSystem.Application;
using VisionSystem.Domain;

namespace VisionSystem.Infrastructure;

public sealed class JsonInspectionRepository(string runtimeDirectory) : IInspectionRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _resultsDirectory = CreateDirectory(runtimeDirectory);

    public async Task<string> SaveAsync(InspectionResult result, CancellationToken cancellationToken)
    {
        var safeId = string.Concat(result.RequestId.Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_'));
        var finalPath = Path.Combine(
            _resultsDirectory,
            $"{result.StartedUtc:yyyyMMdd_HHmmssfff}_{safeId}_{result.Disposition}.json");
        var temporaryPath = finalPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, result, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(temporaryPath, finalPath);
            return finalPath;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string CreateDirectory(string runtimeDirectory)
    {
        var path = Path.Combine(Path.GetFullPath(runtimeDirectory), "results");
        Directory.CreateDirectory(path);
        return path;
    }
}
