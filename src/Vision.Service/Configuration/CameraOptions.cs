namespace Vision.Service.Configuration;

public sealed record CameraOptions
{
    public string Mode { get; init; } = "File";
    public string ImagePath { get; init; } = string.Empty;
    public string InterfaceName { get; init; } = "GigEVision2";
    public string Device { get; init; } = "CHANGE_ME";
    public double ExposureMicroseconds { get; init; } = 1_500;
    public double GainDb { get; init; }
    public int AcquisitionTimeoutMs { get; init; } = 3_000;
}
