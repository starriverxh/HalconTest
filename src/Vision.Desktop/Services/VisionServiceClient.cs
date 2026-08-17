using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Vision.Desktop.Models;

namespace Vision.Desktop.Services;

public sealed class VisionServiceClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<string> PingAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        using var document = await SendAsync(
            host,
            port,
            new { requestId = $"ui-ping-{Guid.NewGuid():N}", command = "ping" },
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(document.RootElement);
        return document.RootElement.GetProperty("message").GetString() ?? "Service is reachable.";
    }

    public async Task<InspectionViewData> TriggerAsync(
        string host,
        int port,
        string? imagePath,
        CancellationToken cancellationToken = default)
    {
        var requestId = $"ui-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
        using var document = await SendAsync(
            host,
            port,
            new
            {
                requestId,
                command = "trigger",
                timeoutMs = 15_000,
                parameters = string.IsNullOrWhiteSpace(imagePath) ? null : new { imagePath }
            },
            cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        EnsureSuccess(root);
        var data = root.GetProperty("data");
        return new InspectionViewData(
            data.GetProperty("isOk").GetBoolean(),
            data.GetProperty("x").GetDouble(),
            data.GetProperty("y").GetDouble(),
            data.GetProperty("theta").GetDouble(),
            data.GetProperty("score").GetDouble(),
            data.TryGetProperty("imagePath", out var savedImage) ? savedImage.GetString() : null,
            data.TryGetProperty("resultPath", out var resultPath) ? resultPath.GetString() : null,
            root.GetProperty("durationMs").GetInt64());
    }

    private static async Task<JsonDocument> SendAsync(
        string host,
        int port,
        object request,
        CancellationToken cancellationToken)
    {
        using var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
        await using var stream = client.GetStream();
        var payload = JsonSerializer.Serialize(request, JsonOptions) + "\n";
        await stream.WriteAsync(Encoding.UTF8.GetBytes(payload), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), leaveOpen: true);
        var response = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new IOException("Vision service closed the connection without a response.");
        return JsonDocument.Parse(response);
    }

    private static void EnsureSuccess(JsonElement root)
    {
        if (root.GetProperty("ok").GetBoolean())
        {
            return;
        }

        var code = root.TryGetProperty("code", out var codeElement) ? codeElement.GetString() : "UNKNOWN";
        var message = root.TryGetProperty("message", out var messageElement) ? messageElement.GetString() : "Vision request failed.";
        throw new InvalidOperationException($"{code}: {message}");
    }
}
