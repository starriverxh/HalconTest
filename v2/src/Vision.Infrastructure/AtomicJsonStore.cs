using System.Text.Json;
using System.Text.Json.Serialization;

namespace VisionSystem.Infrastructure;

public static class AtomicJsonStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task WriteAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken = default)
    {
        var finalPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)
            ?? throw new InvalidDataException("Output path has no parent directory."));
        var temporaryPath = finalPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(temporaryPath, finalPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
