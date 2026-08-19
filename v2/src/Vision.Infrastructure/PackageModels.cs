using System.Text.Json.Serialization;

namespace VisionSystem.Infrastructure;

public enum PackageLifecycleState
{
    Draft,
    Validated,
    Approved,
    Published,
    Active,
    Retired
}

public sealed record PackageManifest
{
    public string SchemaVersion { get; init; } = "2.0";
    public required string PackageId { get; init; }
    public required string PackageType { get; init; }
    public required string SubjectId { get; init; }
    public required string Version { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required PackageLifecycleState State { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
    public required string CreatedBy { get; init; }
    public required IReadOnlyDictionary<string, string> Files { get; init; }
    public string? ValidationReport { get; init; }
    public HardwareFingerprint? Hardware { get; init; }
}

public sealed record HardwareFingerprint
{
    public string? BoardCameraSerial { get; init; }
    public string? PinCameraSerial { get; init; }
    public string? BoardLensId { get; init; }
    public string? PinLensId { get; init; }
    public string? ImageSize { get; init; }
    public double? WorkingDistanceMm { get; init; }
}

public sealed record ActivePackageEntry
{
    public required string SubjectId { get; init; }
    public required string RelativePackageDirectory { get; init; }
    public required string PackageSha256 { get; init; }
    public required DateTimeOffset ActivatedUtc { get; init; }
    public required string ActivatedBy { get; init; }
}

public sealed record ActivePackageIndex
{
    public string SchemaVersion { get; init; } = "2.0";
    public IReadOnlyDictionary<string, ActivePackageEntry> Recipes { get; init; } =
        new Dictionary<string, ActivePackageEntry>(StringComparer.OrdinalIgnoreCase);
    public ActivePackageEntry? StationCalibration { get; init; }
}

public sealed record PackageVerificationReport(
    bool IsValid,
    string PackageDirectory,
    string PackageSha256,
    PackageManifest? Manifest,
    IReadOnlyList<string> Errors);
