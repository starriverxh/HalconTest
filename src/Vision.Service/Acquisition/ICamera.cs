using HalconDotNet;

namespace Vision.Service.Acquisition;

public interface ICamera : IAsyncDisposable
{
    string Name { get; }
    bool IsReady { get; }
    Task OpenAsync(CancellationToken cancellationToken = default);
    Task<HObject> CaptureAsync(string? imagePathOverride = null, CancellationToken cancellationToken = default);
}
