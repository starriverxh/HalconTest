using System.Net;

namespace Vision.Service;

public sealed record ServiceOptions
{
    public IPAddress ListenAddress { get; init; } = IPAddress.Loopback;
    public int Port { get; init; } = 5001;
    public int DefaultTimeoutMs { get; init; } = 5_000;
    public int MaximumTimeoutMs { get; init; } = 60_000;
    public bool AllowRemoteShutdown { get; init; }
    public string RuntimeDirectory { get; init; } = Path.Combine(Environment.CurrentDirectory, "runtime");
    public string Backend { get; init; } = "mock";
    public int MockDelayMs { get; init; } = 120;
    public string? HalconAssemblyPath { get; init; }
    public string? ProcedurePath { get; init; }
    public string ProcedureName { get; init; } = "inspect_pin_row";

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
            Port = GetInt(values, "--port", 5001, 0, 65_535),
            DefaultTimeoutMs = GetInt(values, "--timeout-ms", 5_000, 1, 60_000),
            MaximumTimeoutMs = GetInt(values, "--max-timeout-ms", 60_000, 1, 600_000),
            RuntimeDirectory = Path.GetFullPath(Get(values, "--runtime-dir", Path.Combine(Environment.CurrentDirectory, "runtime"))),
            Backend = Get(values, "--backend", "mock"),
            MockDelayMs = GetInt(values, "--mock-delay-ms", 120, 0, 60_000),
            AllowRemoteShutdown = values.ContainsKey("--allow-remote-shutdown"),
            HalconAssemblyPath = GetOptional(values, "--halcon-assembly"),
            ProcedurePath = GetOptional(values, "--procedure-path"),
            ProcedureName = Get(values, "--procedure-name", "inspect_pin_row")
        };
    }

    private static string Get(Dictionary<string, string?> values, string key, string fallback) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static string? GetOptional(Dictionary<string, string?> values, string key) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

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
