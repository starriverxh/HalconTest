namespace VisionSystem.DatasetCli;

public sealed record DatasetManifest
{
    public required string DatasetId { get; init; }
    public required string Version { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
    public required IReadOnlyList<DatasetCase> Cases { get; init; }
}

public sealed record DatasetCase
{
    public required string Id { get; init; }
    public required string BoardImage { get; init; }
    public required string PinImage { get; init; }
    public required string ExpectedDisposition { get; init; }
    public required IReadOnlyList<string> ExpectedFailureCodes { get; init; }
    public required DatasetCaseHashes Sha256 { get; init; }
}

public sealed record DatasetCaseHashes
{
    public required string BoardImage { get; init; }
    public required string PinImage { get; init; }
}

public sealed record VerifiedDatasetCase(
    DatasetCase Definition,
    string BoardImagePath,
    string PinImagePath);
