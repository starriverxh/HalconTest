using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using VisionSystem.Contracts;

namespace VisionSystem.DatasetCli;

public sealed record RegressionCaseResult(
    string Id,
    bool Passed,
    string ActualDisposition,
    IReadOnlyList<string> ActualFailureCodes,
    string Detail);

public sealed class EngineeringRegressionClient(string host, int port)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<RegressionCaseResult> RunAsync(
        VerifiedDatasetCase testCase,
        CancellationToken cancellationToken)
    {
        var requestId = $"dataset-{Sanitize(testCase.Definition.Id)}-{DateTimeOffset.UtcNow:HHmmssfff}";
        using var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, false, leaveOpen: true);
        await WriteAsync(stream, new
        {
            protocolVersion = VisionProtocol.CurrentVersion,
            requestId,
            command = VisionCommands.EngineeringInspectOffline,
            payload = new EngineeringInspectPayload
            {
                ProductId = "TRAINING-PCB",
                RecipeId = "pcb-pin-row-offline-v2",
                BoardImagePath = testCase.BoardImagePath,
                PinImagePath = testCase.PinImagePath
            }
        }, cancellationToken).ConfigureAwait(false);

        var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new IOException("Runtime disconnected before sending a dataset result.");
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        if (!root.GetProperty("success").GetBoolean())
        {
            var error = root.TryGetProperty("error", out var errorElement) &&
                        errorElement.TryGetProperty("code", out var code)
                ? code.GetString() ?? "UNKNOWN"
                : "UNKNOWN";
            return new RegressionCaseResult(testCase.Definition.Id, false, "ERROR", [error],
                "Runtime rejected the engineering command.");
        }

        var payload = root.GetProperty("payload");
        var disposition = payload.GetProperty("disposition").GetString() ?? string.Empty;
        var actualCodes = payload.GetProperty("failures")
            .EnumerateArray()
            .Select(failure => failure.GetProperty("code").GetString() ?? string.Empty)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var expectedCodes = testCase.Definition.ExpectedFailureCodes
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var passed = string.Equals(
                         disposition,
                         testCase.Definition.ExpectedDisposition,
                         StringComparison.OrdinalIgnoreCase) &&
                     actualCodes.SequenceEqual(expectedCodes, StringComparer.Ordinal);

        await WriteAsync(stream, new
        {
            protocolVersion = VisionProtocol.CurrentVersion,
            requestId = requestId + "-ack",
            command = VisionCommands.Acknowledge,
            payload = new { completedRequestId = requestId }
        }, cancellationToken).ConfigureAwait(false);
        _ = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

        var detail = passed
            ? "Disposition and exact failure-code set match."
            : $"Expected {testCase.Definition.ExpectedDisposition} [{string.Join(",", expectedCodes)}], " +
              $"actual {disposition} [{string.Join(",", actualCodes)}].";
        return new RegressionCaseResult(
            testCase.Definition.Id,
            passed,
            disposition,
            actualCodes,
            detail);
    }

    private static async Task WriteAsync(Stream stream, object value, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, JsonOptions) + "\n");
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string Sanitize(string value) => string.Concat(value.Select(character =>
        char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-'));
}
