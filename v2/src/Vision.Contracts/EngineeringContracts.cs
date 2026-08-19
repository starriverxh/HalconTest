namespace VisionSystem.Contracts;

public enum EngineeringArtifactState
{
    Draft,
    Validated,
    Approved,
    Published,
    Active,
    Retired
}

public sealed record PipelineStageDescriptor
{
    public required string Id { get; init; }
    public required int Order { get; init; }
    public required string Name { get; init; }
    public required string Purpose { get; init; }
    public required IReadOnlyList<string> Inputs { get; init; }
    public required IReadOnlyList<string> Outputs { get; init; }
    public required IReadOnlyList<string> HalconOperators { get; init; }
    public required IReadOnlyList<ParameterDescriptor> Parameters { get; init; }
    public required IReadOnlyList<string> PossibleFailureCodes { get; init; }
    public required IReadOnlyList<string> AcceptanceCriteria { get; init; }
}

public sealed record ParameterDescriptor(
    string Name,
    string DisplayName,
    string Unit,
    string PhysicalMeaning,
    double? Minimum,
    double? Maximum,
    string DefaultValue);

public sealed record RuntimeSnapshotDto
{
    public required string SnapshotId { get; init; }
    public required string ProductRecipeVersion { get; init; }
    public required string RecipePackageSha256 { get; init; }
    public required string StationCalibrationId { get; init; }
    public required string CalibrationPackageSha256 { get; init; }
    public required string BoardCameraSerial { get; init; }
    public required string PinCameraSerial { get; init; }
    public required string AlgorithmAssemblyVersion { get; init; }
    public required DateTimeOffset ActivatedUtc { get; init; }
}
