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
            root.GetProperty("durationMs").GetInt64(),
            data.TryGetProperty("failureReasons", out var failureReasons) && failureReasons.ValueKind == JsonValueKind.Array
                ? failureReasons.EnumerateArray().Select(item => item.GetString() ?? "UNKNOWN").ToArray()
                : []);
    }

    public Task<ProcessingViewData> PreviewProcessingAsync(
        string host,
        int port,
        string imagePath,
        RectangleRoiData? roi,
        double thresholdMin,
        double thresholdMax,
        double minRadiusPx,
        double maxRadiusPx,
        double minCircularity,
        CancellationToken cancellationToken = default) =>
        SendWorkbenchAsync<ProcessingViewData>(host, port, "preview_processing", new
        {
            imagePath,
            roi,
            thresholdMin,
            thresholdMax,
            minRadiusPx,
            maxRadiusPx,
            minCircularity
        }, cancellationToken);

    public Task<ShapeModelCreateViewData> CreateShapeModelAsync(
        string host,
        int port,
        string imagePath,
        RectangleRoiData? roi,
        string modelName,
        double angleStartDeg,
        double angleExtentDeg,
        int contrast,
        CancellationToken cancellationToken = default) =>
        SendWorkbenchAsync<ShapeModelCreateViewData>(host, port, "create_template", new
        {
            imagePath,
            roi,
            modelName,
            angleStartDeg,
            angleExtentDeg,
            contrast
        }, cancellationToken);

    public Task<ShapeModelMatchViewData> MatchShapeModelAsync(
        string host,
        int port,
        string imagePath,
        string modelPath,
        double angleStartDeg,
        double angleExtentDeg,
        double minScore,
        int numMatches,
        double maxOverlap,
        CancellationToken cancellationToken = default) =>
        SendWorkbenchAsync<ShapeModelMatchViewData>(host, port, "match_template", new
        {
            imagePath,
            modelPath,
            angleStartDeg,
            angleExtentDeg,
            minScore,
            numMatches,
            maxOverlap
        }, cancellationToken);

    public Task<CalibrationFitViewData> FitCalibrationAsync(
        string host,
        int port,
        IReadOnlyList<CalibrationPointRow> points,
        string modelPath,
        string csvPath,
        CancellationToken cancellationToken = default) =>
        SendWorkbenchAsync<CalibrationFitViewData>(host, port, "calibrate_points", new
        {
            points = points.Select(point => new
            {
                point.Row,
                point.Column,
                point.MachineX,
                point.MachineY,
                point.UseForFit
            }),
            modelPath,
            csvPath
        }, cancellationToken);

    public Task<RecipeSaveViewData> SaveRecipeAsync(
        string host,
        int port,
        int? expectedVersion,
        RectangleRoiData roi,
        double thresholdMin,
        double thresholdMax,
        double minRadiusPx,
        double maxRadiusPx,
        double minCircularity,
        int expectedCount,
        bool useShapeModel,
        string shapeModelPath,
        double angleStartDeg,
        double angleExtentDeg,
        double matchMinScore,
        CancellationToken cancellationToken = default) =>
        SendWorkbenchAsync<RecipeSaveViewData>(host, port, "save_recipe", new
        {
            expectedVersion,
            roi,
            thresholdMin,
            thresholdMax,
            minRadiusPx,
            maxRadiusPx,
            minCircularity,
            expectedCount,
            useShapeModel,
            shapeModelPath,
            angleStartDeg,
            angleExtentDeg,
            matchMinScore
        }, cancellationToken);

    public Task<RecipeSnapshotViewData> GetRecipeAsync(
        string host,
        int port,
        CancellationToken cancellationToken = default) =>
        SendWorkbenchAsync<RecipeSnapshotViewData>(
            host,
            port,
            "get_recipe",
            new { },
            cancellationToken);

    private static async Task<T> SendWorkbenchAsync<T>(
        string host,
        int port,
        string command,
        object parameters,
        CancellationToken cancellationToken)
    {
        using var document = await SendAsync(host, port, new
        {
            requestId = $"ui-{command}-{Guid.NewGuid():N}",
            command,
            timeoutMs = 30_000,
            parameters
        }, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(document.RootElement);
        return document.RootElement.GetProperty("data").Deserialize<T>(JsonOptions)
            ?? throw new InvalidDataException($"The service returned empty data for '{command}'.");
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
