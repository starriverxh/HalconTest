using System.Net;

namespace Vision.Service.Configuration;

public sealed record ServiceOptions
{
    public IPAddress ListenAddress { get; init; } = IPAddress.Loopback;
    public int Port { get; init; } = 9100;
    public int DefaultTimeoutMs { get; init; } = 5_000;
    public int MaximumTimeoutMs { get; init; } = 60_000;
    public bool AllowRemoteShutdown { get; init; }
    public string RuntimeDirectory { get; init; } = Path.Combine(Environment.CurrentDirectory, "runtime");
    public string RecipePath { get; init; } = Path.Combine(Environment.CurrentDirectory, "config", "vision.recipe.json");
    public string Backend { get; init; } = "halcon";
    public int MockDelayMs { get; init; } = 120;

    public static ServiceOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            var key = args[index];
            if (!key.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected argument '{key}'.");
            }

            if (key.Equals("--allow-remote-shutdown", StringComparison.OrdinalIgnoreCase))
            {
                values[key] = "true";
                continue;
            }

            if (++index >= args.Length)
            {
                throw new ArgumentException($"Missing value for '{key}'.");
            }

            values[key] = args[index];
        }

        var host = Get(values, "--host", "127.0.0.1");
        if (!IPAddress.TryParse(host, out var address))
        {
            throw new ArgumentException($"Invalid IP address '{host}'. Use an explicit IPv4/IPv6 address.");
        }

        return new ServiceOptions
        {
            ListenAddress = address,
            Port = GetInt(values, "--port", 9100, 0, 65_535),
            DefaultTimeoutMs = GetInt(values, "--timeout-ms", 5_000, 1, 60_000),
            MaximumTimeoutMs = GetInt(values, "--max-timeout-ms", 60_000, 1, 600_000),
            RuntimeDirectory = Path.GetFullPath(Get(values, "--runtime-dir", Path.Combine(Environment.CurrentDirectory, "runtime"))),
            RecipePath = Path.GetFullPath(Get(values, "--recipe", Path.Combine(Environment.CurrentDirectory, "config", "vision.recipe.json"))),
            Backend = Get(values, "--backend", "halcon"),
            MockDelayMs = GetInt(values, "--mock-delay-ms", 120, 0, 60_000),
            AllowRemoteShutdown = values.ContainsKey("--allow-remote-shutdown")
        };
    }

    private static string Get(Dictionary<string, string?> values, string key, string fallback) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static int GetInt(Dictionary<string, string?> values, string key, int fallback, int minimum, int maximum)
    {
        var text = Get(values, key, fallback.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (!int.TryParse(text, out var value) || value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(key, $"'{key}' must be between {minimum} and {maximum}.");
        }

        return value;
    }
}
