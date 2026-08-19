using HalconDotNet;
using Vision.Service.Configuration;

namespace Vision.Service.Vision;

/// <summary>
/// Production shape-model locator. The model is loaded once during service startup,
/// reused for every trigger, and explicitly cleared on shutdown.
/// </summary>
public sealed class ShapeModelLocator : IDisposable
{
    private readonly LocalizationOptions _options;
    private readonly HTuple? _modelId;
    private bool _disposed;

    public ShapeModelLocator(LocalizationOptions options)
    {
        _options = options;
        if (options.Mode.Equals("ShapeModel", StringComparison.OrdinalIgnoreCase))
        {
            HOperatorSet.ReadShapeModel(options.ModelFile, out HTuple modelId);
            _modelId = modelId;
        }
    }

    public BoardPose Locate(HObject grayImage, CoordinateTransformer transformer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_modelId is null)
        {
            throw new InvalidOperationException("Shape-model localization is not configured.");
        }

        HOperatorSet.FindShapeModel(
            grayImage,
            _modelId,
            DegreesToRadians(_options.AngleStartDeg),
            DegreesToRadians(_options.AngleExtentDeg),
            _options.MinScore,
            1,
            0.5,
            "least_squares",
            _options.NumLevels,
            _options.Greediness,
            out HTuple rows,
            out HTuple columns,
            out HTuple angles,
            out HTuple scores);
        using (rows)
        using (columns)
        using (angles)
        using (scores)
        {
            if (scores.Length == 0)
            {
                return new BoardPose(0, 0, 0, 0);
            }

            var row = rows[0].D;
            var column = columns[0].D;
            var angle = angles[0].D;
            var origin = transformer.Transform(row, column);

            // Transform a second point on the model X axis. This correctly includes
            // camera-to-machine rotation, scale, axis direction and affine shear.
            const double directionLengthPx = 100;
            var direction = transformer.Transform(
                row + Math.Sin(angle) * directionLengthPx,
                column + Math.Cos(angle) * directionLengthPx);
            var machineAngle = Math.Atan2(
                direction.Ymm - origin.Ymm,
                direction.Xmm - origin.Xmm) * 180.0 / Math.PI;
            return new BoardPose(origin.Xmm, origin.Ymm, machineAngle, scores[0].D);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_modelId is not null)
        {
            HOperatorSet.ClearShapeModel(_modelId);
            _modelId.Dispose();
        }
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;
}
