using System.Text.Json;
using VisionSystem.DatasetCli;

var command = args.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal)) ?? "verify";
var manifestPath = Path.GetFullPath(Value("--manifest") ?? "v2/datasets/manifest.json");
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    PropertyNameCaseInsensitive = true
};
await using var stream = File.OpenRead(manifestPath);
var manifest = await JsonSerializer.DeserializeAsync<DatasetManifest>(stream, jsonOptions)
    ?? throw new InvalidDataException("Dataset manifest is empty.");
var verified = await new DatasetVerifier().VerifyAsync(manifest, manifestPath);
Console.WriteLine($"PASS dataset integrity: {manifest.DatasetId} {manifest.Version}, {verified.Count} cases");

if (command.Equals("verify", StringComparison.OrdinalIgnoreCase))
{
    return 0;
}

if (!command.Equals("run", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Usage: Vision.Dataset.Cli [verify|run] [--manifest path] [--host 127.0.0.1] [--port 9201]");
    return 2;
}

var host = Value("--host") ?? "127.0.0.1";
var portText = Value("--port");
var port = portText is null
    ? 9201
    : int.TryParse(portText, out var parsed) && parsed is > 0 and <= 65535
        ? parsed
        : throw new ArgumentException($"Invalid --port '{portText}'.");
var client = new EngineeringRegressionClient(host, port);
var failures = 0;
foreach (var testCase in verified)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var result = await client.RunAsync(testCase, timeout.Token);
    Console.WriteLine($"{(result.Passed ? "PASS" : "FAIL")} {result.Id}: {result.Detail}");
    if (!result.Passed)
    {
        failures++;
    }
}

Console.WriteLine($"Dataset regression: {verified.Count - failures}/{verified.Count} passed");
return failures == 0 ? 0 : 1;

string? Value(string name)
{
    var index = Array.FindIndex(args, item => item.Equals(name, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}
