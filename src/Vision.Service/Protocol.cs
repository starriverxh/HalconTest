using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vision.Service;

public static class Protocol
{
    public const int Version = 1;
    public const int MaxMessageBytes = 64 * 1024;

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };
}

public sealed record VisionRequest(
    string? RequestId,
    string? Command,
    int? TimeoutMs = null,
    JsonElement? Parameters = null);

public sealed record VisionResponse(
    string RequestId,
    string Command,
    bool Ok,
    string Code,
    string Message,
    DateTimeOffset TimestampUtc,
    long DurationMs,
    object? Data = null,
    int ProtocolVersion = Protocol.Version);

public static class ErrorCodes
{
    public const string Ok = "OK";
    public const string InvalidJson = "INVALID_JSON";
    public const string InvalidRequest = "INVALID_REQUEST";
    public const string UnknownCommand = "UNKNOWN_COMMAND";
    public const string Busy = "BUSY";
    public const string Timeout = "TIMEOUT";
    public const string Cancelled = "CANCELLED";
    public const string Forbidden = "FORBIDDEN";
    public const string BackendError = "BACKEND_ERROR";
    public const string InternalError = "INTERNAL_ERROR";
    public const string MessageTooLarge = "MESSAGE_TOO_LARGE";
}
