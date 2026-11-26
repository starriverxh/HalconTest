using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Trigger.Client;

public static class Program
{
    static int Test = 0;
    private static readonly JsonSerializerOptions RequestJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private static readonly JsonSerializerOptions DisplayJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = ClientOptions.Parse(args);
            using var timeoutSource = new CancellationTokenSource(options.ClientTimeoutMs);
            using var client = new TcpClient { NoDelay = true };
            await client.ConnectAsync(options.Host, options.Port, timeoutSource.Token).ConfigureAwait(false);

            var request = new
            {
                requestId = options.RequestId,
                command = options.Command,
                timeoutMs = options.RequestTimeoutMs,
                parameters = options.Parameters
            };
            var requestBytes = JsonSerializer.SerializeToUtf8Bytes(request, RequestJsonOptions);
            var stream = client.GetStream();
            await stream.WriteAsync(requestBytes, timeoutSource.Token).ConfigureAwait(false);
            await stream.WriteAsync("\n"u8.ToArray(), timeoutSource.Token).ConfigureAwait(false);
            await stream.FlushAsync(timeoutSource.Token).ConfigureAwait(false);

            var response = await ReadLineAsync(stream, timeoutSource.Token).ConfigureAwait(false)
                ?? throw new IOException("Server closed the connection without a response.");
            using var document = JsonDocument.Parse(response);
            Console.WriteLine(JsonSerializer.Serialize(document.RootElement, DisplayJsonOptions));

            return document.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean() ? 0 : 2;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Client operation timed out.");
            return 3;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Client failed: {exception.Message}");
            return 1;
        }
    }

    private static async Task<string?> ReadLineAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        using var line = new MemoryStream();
        while (line.Length <= 64 * 1024)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return line.Length == 0 ? null : Encoding.UTF8.GetString(line.ToArray());
            }

            var newline = Array.IndexOf(buffer, (byte)'\n', 0, read);
            if (newline >= 0)
            {
                line.Write(buffer, 0, newline);
                return Encoding.UTF8.GetString(line.ToArray()).TrimEnd('\r');
            }

            line.Write(buffer, 0, read);
        }

        throw new IOException("Response exceeds 64 KiB.");
    }
}

internal sealed record ClientOptions(
    string Host,
    int Port,
    string Command,
    string RequestId,
    int? RequestTimeoutMs,
    int ClientTimeoutMs,
    JsonElement? Parameters)
{
    public static ClientOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            var key = args[index];
            if (!key.StartsWith("--", StringComparison.Ordinal) || ++index >= args.Length)
            {
                throw new ArgumentException($"Arguments use '--name value'; invalid token '{key}'.");
            }

            values[key] = args[index];
        }

        var command = Get(values, "--command", "trigger").ToLowerInvariant();
        JsonElement? parameters = values.TryGetValue("--params", out var json)
            ? ParseParameters(json)
            : null;
        return new ClientOptions(
            Get(values, "--host", "127.0.0.1"),
            GetInt(values, "--port", 5001, 1, 65_535),
            command,
            Get(values, "--request-id", Guid.NewGuid().ToString("N")),
            GetNullableInt(values, "--request-timeout-ms", 1, 600_000),
            GetInt(values, "--client-timeout-ms", 10_000, 1, 600_000),
            parameters);
    }

    private static JsonElement ParseParameters(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("--params must be a JSON object.");
        }

        return document.RootElement.Clone();
    }

    private static string Get(Dictionary<string, string> values, string key, string fallback) =>
        values.TryGetValue(key, out var value) ? value : fallback;

    private static int GetInt(Dictionary<string, string> values, string key, int fallback, int minimum, int maximum)
    {
        var value = values.TryGetValue(key, out var text) && int.TryParse(text, out var parsed) ? parsed : fallback;
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(key, $"'{key}' must be between {minimum} and {maximum}.");
        }

        return value;
    }

    private static int? GetNullableInt(Dictionary<string, string> values, string key, int minimum, int maximum)
    {
        if (!values.TryGetValue(key, out var text))
        {
            return null;
        }

        if (!int.TryParse(text, out var value) || value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(key, $"'{key}' must be between {minimum} and {maximum}.");
        }

        return value;
    }
}
