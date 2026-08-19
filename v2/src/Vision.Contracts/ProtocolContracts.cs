using System.Text.Json;

namespace VisionSystem.Contracts;

/// <summary>
/// Newline-delimited JSON protocol shared by the runtime and lightweight
/// machine/PLC-facing clients. Contracts deliberately contain no HALCON types.
/// </summary>
public static class VisionProtocol
{
    public const string CurrentVersion = "2.0";
    public const int MaximumLineBytes = 64 * 1024;
}

public static class VisionCommands
{
    public const string Hello = "hello";
    public const string Status = "status";
    public const string Inspect = "inspect";
    public const string Acknowledge = "ack";
    public const string ResetFault = "reset_fault";
    public const string EngineeringInspectOffline = "engineering.inspect_offline";
}

public sealed record VisionCommandEnvelope
{
    public string ProtocolVersion { get; init; } = VisionProtocol.CurrentVersion;
    public string RequestId { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public JsonElement Payload { get; init; }
}

public sealed record ProductionInspectPayload
{
    public string ProductId { get; init; } = string.Empty;
    public string RecipeId { get; init; } = string.Empty;
    public string? SerialNumber { get; init; }
}

public sealed record EngineeringInspectPayload
{
    public string ProductId { get; init; } = string.Empty;
    public string RecipeId { get; init; } = string.Empty;
    public string? BoardImagePath { get; init; }
    public string? PinImagePath { get; init; }
}

public sealed record AcknowledgePayload
{
    public string CompletedRequestId { get; init; } = string.Empty;
}

public sealed record VisionResponseEnvelope
{
    public string ProtocolVersion { get; init; } = VisionProtocol.CurrentVersion;
    public required string RequestId { get; init; }
    public required bool Success { get; init; }
    public required string Status { get; init; }
    public object? Payload { get; init; }
    public ProtocolError? Error { get; init; }
    public bool IsReplay { get; init; }
}

public sealed record ProtocolError(
    string Code,
    string Message,
    bool Retryable);

public sealed record StationStatusDto
{
    public required string Mode { get; init; }
    public required string State { get; init; }
    public required bool ProductionCommissioned { get; init; }
    public required bool MotionPermittedByStation { get; init; }
    public required string? ActiveRequestId { get; init; }
    public required string? ActiveRecipeId { get; init; }
    public required string? LastCompletedRequestId { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
}

public sealed record InspectionResultDto
{
    public required string RequestId { get; init; }
    public required string ProductId { get; init; }
    public required string RecipeId { get; init; }
    public required int RecipeVersion { get; init; }
    public required string Disposition { get; init; }
    public required bool MeasurementValid { get; init; }
    public required bool MotionAllowed { get; init; }
    public required double CommandXmm { get; init; }
    public required double CommandYmm { get; init; }
    public required double CommandThetaDeg { get; init; }
    public double? BoardScore { get; init; }
    public double? PinScore { get; init; }
    public double? AlignmentRmseMm { get; init; }
    public double? MaximumPinResidualMm { get; init; }
    public required IReadOnlyList<PointResidualDto> PointResiduals { get; init; }
    public required IReadOnlyList<FailureDto> Failures { get; init; }
    public required IReadOnlyList<string> StateHistory { get; init; }
    public FrameEvidenceDto? BoardFrame { get; init; }
    public FrameEvidenceDto? PinFrame { get; init; }
    public required IReadOnlyDictionary<string, double> StageMetrics { get; init; }
    public required string BoardAlgorithmName { get; init; }
    public required string PinAlgorithmName { get; init; }
    public required string RecipeStatus { get; init; }
    public required string ProductCode { get; init; }
    public required long DurationMs { get; init; }
    public string? ResultPath { get; init; }
}

public sealed record FrameEvidenceDto(
    string FrameId,
    string CameraRole,
    string Provenance,
    DateTimeOffset CapturedUtc,
    int Width,
    int Height,
    string SourceName,
    string? ArchivedImagePath,
    string? Sha256);

public sealed record PointResidualDto(
    string Id,
    double TargetXmm,
    double TargetYmm,
    double PredictedXmm,
    double PredictedYmm,
    double ErrorMm);

public sealed record FailureDto(
    string Code,
    string Stage,
    string Message,
    bool Recoverable);
