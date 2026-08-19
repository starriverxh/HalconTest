using System.Text.Json;
using HalconDotNet;

namespace VisionSystem.Halcon.Models;

public sealed record ShapeModelMetadata
{
    public string SchemaVersion { get; init; } = "2.0";
    public double ReferenceRow { get; init; }
    public double ReferenceColumn { get; init; }
    public double ReferenceAngleDeg { get; init; }
    public string ProductDatum { get; init; } = "PCB_DATUM";
    public string TrainingImageFingerprint { get; init; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UnixEpoch;
}

internal sealed class ShapeModelCache : IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<string, CachedShapeModel> _models = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public CachedShapeModel Get(string modelPath, string metadataPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var fullModelPath = Path.GetFullPath(modelPath);
        if (!File.Exists(fullModelPath))
        {
            throw new FileNotFoundException("HALCON shape model was not found.", fullModelPath);
        }

        lock (_sync)
        {
            if (_models.TryGetValue(fullModelPath, out var cached))
            {
                return cached;
            }

            var metadata = LoadMetadata(metadataPath);
            HOperatorSet.ReadShapeModel(fullModelPath, out HTuple modelId);
            cached = new CachedShapeModel(fullModelPath, modelId, metadata);
            _models.Add(fullModelPath, cached);
            return cached;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var model in _models.Values)
            {
                model.Dispose();
            }

            _models.Clear();
        }
    }

    private static ShapeModelMetadata LoadMetadata(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new FileNotFoundException(
                "Shape-model metadata is required because the HALCON default origin is not a product datum.", path);
        }

        return JsonSerializer.Deserialize<ShapeModelMetadata>(
                   File.ReadAllText(path),
                   new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true })
               ?? throw new InvalidDataException($"Shape-model metadata '{path}' is empty.");
    }
}

internal sealed class CachedShapeModel : IDisposable
{
    private HTuple? _modelId;

    public CachedShapeModel(string path, HTuple modelId, ShapeModelMetadata metadata)
    {
        Path = path;
        _modelId = modelId;
        Metadata = metadata;
    }

    public string Path { get; }
    public ShapeModelMetadata Metadata { get; }
    public HTuple ModelId => _modelId ?? throw new ObjectDisposedException(nameof(CachedShapeModel));

    public void Dispose()
    {
        var id = Interlocked.Exchange(ref _modelId, null);
        if (id is null)
        {
            return;
        }

        HOperatorSet.ClearShapeModel(id);
        id.Dispose();
    }
}
