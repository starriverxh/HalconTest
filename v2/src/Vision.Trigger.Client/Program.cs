using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using VisionSystem.Contracts;

var options = ClientOptions.Parse(args);
var requestId = options.RequestId ?? $"learn-{DateTimeOffset.Now:yyyyMMdd-HHmmssfff}";
var payload = options.Engineering
    ? (object)new EngineeringInspectPayload
    {
        ProductId = options.ProductId,
        RecipeId = options.RecipeId,
        BoardImagePath = options.BoardImagePath,
        PinImagePath = options.PinImagePath
    }
    : new ProductionInspectPayload
    {
        ProductId = options.ProductId,
        RecipeId = options.RecipeId,
        SerialNumber = options.SerialNumber
    };

var command = new
{
    protocolVersion = VisionProtocol.CurrentVersion,
    requestId,
    command = options.Engineering ? VisionCommands.EngineeringInspectOffline : VisionCommands.Inspect,
    payload
};

using var client = new TcpClient { NoDelay = true };
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds));
await client.ConnectAsync(options.Host, options.Port, timeout.Token);
await using var stream = client.GetStream();
using var reader = new StreamReader(stream, Encoding.UTF8, false, leaveOpen: true);
await SendAsync(stream, command, timeout.Token);
var resultLine = await reader.ReadLineAsync(timeout.Token)
    ?? throw new IOException("Vision service closed without a response.");
if (!PrintJson(resultLine))
{
    return 2;
}

if (options.Acknowledge)
{
    var acknowledgement = new
    {
        protocolVersion = VisionProtocol.CurrentVersion,
        requestId = requestId + "-ack",
        command = VisionCommands.Acknowledge,
        payload = new { completedRequestId = requestId }
    };
    await SendAsync(stream, acknowledgement, timeout.Token);
    var acknowledgementLine = await reader.ReadLineAsync(timeout.Token)
        ?? throw new IOException("Vision service closed before acknowledgement response.");
    if (!PrintJson(acknowledgementLine))
    {
        return 3;
    }
}

return 0;

static async Task SendAsync(Stream stream, object value, CancellationToken cancellationToken)
{
    var json = JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    var bytes = Encoding.UTF8.GetBytes(json + "\n");
    await stream.WriteAsync(bytes, cancellationToken);
    await stream.FlushAsync(cancellationToken);
}

static bool PrintJson(string json)
{
    using var document = JsonDocument.Parse(json);
    Console.WriteLine(JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
    {
        WriteIndented = true
    }));
    return document.RootElement.TryGetProperty("success", out var success) && success.GetBoolean();
}

internal sealed record ClientOptions
{
    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 9201;
    public bool Engineering { get; init; } = true;
    public string ProductId { get; init; } = "TRAINING-PCB";
    public string RecipeId { get; init; } = "pcb-pin-row-offline-v2";
    public string? SerialNumber { get; init; }
    public string? RequestId { get; init; }
    public string? BoardImagePath { get; init; }
    public string? PinImagePath { get; init; }
    public int TimeoutSeconds { get; init; } = 15;
    public bool Acknowledge { get; init; } = true;

    public static ClientOptions Parse(string[] args)
    {
        string? Value(string name)
        {
            var index = Array.FindIndex(args, item => item.Equals(name, StringComparison.OrdinalIgnoreCase));
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        var production = args.Contains("--production", StringComparer.OrdinalIgnoreCase);
        var portText = Value("--port");
        var timeoutText = Value("--timeout-seconds");
        return new ClientOptions
        {
            Host = Value("--host") ?? "127.0.0.1",
            Port = portText is null ? production ? 9200 : 9201 : ParsePositive(portText, "port"),
            Engineering = !production,
            ProductId = Value("--product") ?? "TRAINING-PCB",
            RecipeId = Value("--recipe") ?? "pcb-pin-row-offline-v2",
            SerialNumber = Value("--sn"),
            RequestId = Value("--request-id"),
            BoardImagePath = Value("--board"),
            PinImagePath = Value("--pins"),
            TimeoutSeconds = timeoutText is null ? 15 : ParsePositive(timeoutText, "timeout-seconds"),
            Acknowledge = !args.Contains("--no-ack", StringComparer.OrdinalIgnoreCase)
        };
    }

    private static int ParsePositive(string value, string name) =>
        int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : throw new ArgumentException($"Invalid --{name} value '{value}'.");
}
