using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using HalconDotNet;
using Vision.Service.Acquisition;
using Vision.Service.Communication;
using Vision.Service.Configuration;
using Vision.Service.Storage;
using Vision.Service.Vision;
using Vision.Service.Workbench;

namespace Vision.Service.Tests;

public static class Program
{
    private static int _passed;

    public static async Task<int> Main()
    {
        var runtimeDirectory = Path.Combine(Path.GetTempPath(), $"vision-service-tests-{Guid.NewGuid():N}");
        var options = new ServiceOptions
        {
            ListenAddress = IPAddress.Loopback,
            Port = 0,
            DefaultTimeoutMs = 1_000,
            MaximumTimeoutMs = 2_000,
            RuntimeDirectory = runtimeDirectory
        };

        var testRecipePath = Path.Combine(runtimeDirectory, "recipe", "vision.recipe.json");
        Directory.CreateDirectory(Path.GetDirectoryName(testRecipePath)!);
        File.Copy(
            Path.Combine(Environment.CurrentDirectory, "config", "vision.recipe.json"),
            testRecipePath);
        var workbench = new VisionWorkbenchService(runtimeDirectory, testRecipePath);
        await using var server = new VisionTcpServer(
            options,
            new MockVisionBackend(TimeSpan.FromMilliseconds(150)),
            workbench);
        var serverTask = server.RunAsync();
        var readyDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (server.BoundPort == 0 && DateTime.UtcNow < readyDeadline)
        {
            await Task.Delay(10);
        }

        var port = server.BoundPort;
        Check(port > 0, "Server did not bind a TCP port.");
        try
        {
            await TestAsync("ping", async () =>
            {
                using var response = await SendAsync(port, new { requestId = "ping-1", command = "ping" });
                Equal(true, response.RootElement.GetProperty("ok").GetBoolean());
                Equal("OK", response.RootElement.GetProperty("code").GetString());
            });

            await TestAsync("invalid request", async () =>
            {
                using var response = await SendRawAsync(port, "{\"command\":\"ping\"}");
                Equal("INVALID_REQUEST", response.RootElement.GetProperty("code").GetString());
            });

            await TestAsync("trigger and result persistence", async () =>
            {
                using var response = await SendAsync(port, new
                {
                    requestId = "trigger-1",
                    command = "trigger",
                    parameters = new { mockX = 0.01, mockY = -0.01, toleranceMm = 0.05 }
                });
                Equal(true, response.RootElement.GetProperty("ok").GetBoolean());
                var resultPath = response.RootElement.GetProperty("data").GetProperty("resultPath").GetString();
                Check(resultPath is not null && File.Exists(resultPath), "Result file was not persisted.");
            });

            await TestAsync("busy response", async () =>
            {
                var slow = SendAsync(port, new
                {
                    requestId = "slow-1",
                    command = "trigger",
                    parameters = new { delayMs = 500 }
                });
                await Task.Delay(50);
                using var response = await SendAsync(port, new { requestId = "busy-1", command = "trigger" });
                Equal("BUSY", response.RootElement.GetProperty("code").GetString());
                using var completed = await slow;
                Equal(true, completed.RootElement.GetProperty("ok").GetBoolean());
            });

            await TestAsync("timeout", async () =>
            {
                using var response = await SendAsync(port, new
                {
                    requestId = "timeout-1",
                    command = "trigger",
                    timeoutMs = 30,
                    parameters = new { delayMs = 300 }
                });
                Equal("TIMEOUT", response.RootElement.GetProperty("code").GetString());
            });

            await TestAsync("multiple JSON lines per connection", async () =>
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port);
                var stream = client.GetStream();
                var payload = "{\"requestId\":\"multi-1\",\"command\":\"ping\"}\n{\"requestId\":\"multi-2\",\"command\":\"status\"}\n";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(payload));
                using var first = JsonDocument.Parse(await ReadLineAsync(stream));
                using var second = JsonDocument.Parse(await ReadLineAsync(stream));
                Equal("multi-1", first.RootElement.GetProperty("requestId").GetString());
                Equal("multi-2", second.RootElement.GetProperty("requestId").GetString());
            });

            var offlineImage = Path.Combine(Environment.CurrentDirectory, "data", "offline", "synthetic_pcb_good.png");
            await TestAsync("HALCON ROI and Blob processing workbench", async () =>
            {
                using var response = await SendAsync(port, new
                {
                    requestId = "processing-1",
                    command = "preview_processing",
                    timeoutMs = 10_000,
                    parameters = new
                    {
                        imagePath = offlineImage,
                        roi = new { row1 = 230, column1 = 120, row2 = 370, column2 = 880 },
                        thresholdMin = 0,
                        thresholdMax = 65,
                        minRadiusPx = 15,
                        maxRadiusPx = 22,
                        minCircularity = 0.85
                    }
                });
                Equal(true, response.RootElement.GetProperty("ok").GetBoolean());
                var data = response.RootElement.GetProperty("data");
                Equal(5, data.GetProperty("selectedCount").GetInt32());
                var previewPath = data.GetProperty("binaryPreviewPath").GetString();
                Check(previewPath is not null && File.Exists(previewPath), "Binary processing preview was not saved.");
            });

            string? shapeModelPath = null;
            await TestAsync("HALCON shape model creation", async () =>
            {
                using var response = await SendAsync(port, new
                {
                    requestId = "template-create-1",
                    command = "create_template",
                    timeoutMs = 15_000,
                    parameters = new
                    {
                        imagePath = offlineImage,
                        roi = new { row1 = 245, column1 = 135, row2 = 355, column2 = 865 },
                        modelName = "self-test-five-hole-row",
                        angleStartDeg = -5,
                        angleExtentDeg = 10,
                        contrast = 20
                    }
                });
                Equal(true, response.RootElement.GetProperty("ok").GetBoolean());
                shapeModelPath = response.RootElement.GetProperty("data").GetProperty("modelPath").GetString();
                Check(shapeModelPath is not null && File.Exists(shapeModelPath), "Shape model was not saved.");
            });

            await TestAsync("HALCON shape model matching", async () =>
            {
                using var response = await SendAsync(port, new
                {
                    requestId = "template-match-1",
                    command = "match_template",
                    timeoutMs = 15_000,
                    parameters = new
                    {
                        imagePath = offlineImage,
                        modelPath = shapeModelPath,
                        angleStartDeg = -5,
                        angleExtentDeg = 10,
                        minScore = 0.5,
                        numMatches = 1,
                        maxOverlap = 0.5
                    }
                });
                Equal(true, response.RootElement.GetProperty("ok").GetBoolean());
                var matches = response.RootElement.GetProperty("data").GetProperty("matches");
                Check(matches.GetArrayLength() == 1, "Expected one shape-model match.");
                Check(matches[0].GetProperty("score").GetDouble() >= 0.9, "Shape-model score is unexpectedly low.");
            });

            await TestAsync("interactive calibration fitting and validation", async () =>
            {
                var modelPath = Path.Combine(runtimeDirectory, "calibration", "interactive-test.json");
                var csvPath = Path.Combine(runtimeDirectory, "calibration", "interactive-test.csv");
                using var response = await SendAsync(port, new
                {
                    requestId = "calibration-1",
                    command = "calibrate_points",
                    parameters = new
                    {
                        modelPath,
                        csvPath,
                        points = new object[]
                        {
                            new { row = 0, column = 0, machineX = 0, machineY = 0, useForFit = true },
                            new { row = 0, column = 100, machineX = 10, machineY = 0, useForFit = true },
                            new { row = 100, column = 0, machineX = 0, machineY = 10, useForFit = true },
                            new { row = 100, column = 100, machineX = 10, machineY = 10, useForFit = false }
                        }
                    }
                });
                Equal(true, response.RootElement.GetProperty("ok").GetBoolean());
                var model = response.RootElement.GetProperty("data").GetProperty("model");
                Near(0, model.GetProperty("rmseMm").GetDouble(), 1e-9);
                Near(0, model.GetProperty("maxValidationErrorMm").GetDouble(), 1e-9);
                Check(File.Exists(modelPath) && File.Exists(csvPath), "Calibration artifacts were not saved.");
            });

            await TestAsync("versioned atomic production recipe save", async () =>
            {
                using var response = await SendAsync(port, new
                {
                    requestId = "recipe-save-1",
                    command = "save_recipe",
                    parameters = new
                    {
                        expectedVersion = 1,
                        roi = new { row1 = 230, column1 = 120, row2 = 370, column2 = 880 },
                        thresholdMin = 0,
                        thresholdMax = 65,
                        minRadiusPx = 15,
                        maxRadiusPx = 22,
                        minCircularity = 0.85,
                        expectedCount = 5,
                        useShapeModel = false,
                        shapeModelPath = "",
                        angleStartDeg = -10,
                        angleExtentDeg = 20,
                        matchMinScore = 0.6
                    }
                });
                Equal(true, response.RootElement.GetProperty("ok").GetBoolean());
                var data = response.RootElement.GetProperty("data");
                Equal(2, data.GetProperty("recipeVersion").GetInt32());
                Equal(true, data.GetProperty("requiresServiceRestart").GetBoolean());
                var backupPath = data.GetProperty("backupPath").GetString();
                Check(backupPath is not null && File.Exists(backupPath), "Recipe history backup was not created.");
                using var savedRecipe = JsonDocument.Parse(await File.ReadAllTextAsync(testRecipePath));
                Equal(2, savedRecipe.RootElement.GetProperty("recipeVersion").GetInt32());
                Equal(230, savedRecipe.RootElement.GetProperty("holeDetection").GetProperty("searchRoi").GetProperty("row1").GetInt32());
            });

            await TestAsync("production recipe reload snapshot", async () =>
            {
                using var response = await SendAsync(port, new
                {
                    requestId = "recipe-get-1",
                    command = "get_recipe",
                    parameters = new { }
                });
                Equal(true, response.RootElement.GetProperty("ok").GetBoolean());
                var data = response.RootElement.GetProperty("data");
                Equal(2, data.GetProperty("recipeVersion").GetInt32());
                Equal(5, data.GetProperty("expectedCount").GetInt32());
                Equal(false, data.GetProperty("useShapeModel").GetBoolean());
            });

            await TestAsync("stale production recipe version is rejected", async () =>
            {
                using var response = await SendAsync(port, new
                {
                    requestId = "recipe-stale-1",
                    command = "save_recipe",
                    parameters = new
                    {
                        expectedVersion = 1,
                        roi = new { row1 = 230, column1 = 120, row2 = 370, column2 = 880 },
                        thresholdMin = 0,
                        thresholdMax = 65,
                        minRadiusPx = 15,
                        maxRadiusPx = 22,
                        minCircularity = 0.85,
                        expectedCount = 5,
                        useShapeModel = false,
                        shapeModelPath = "",
                        angleStartDeg = -10,
                        angleExtentDeg = 20,
                        matchMinScore = 0.6
                    }
                });
                Equal(false, response.RootElement.GetProperty("ok").GetBoolean());
                Equal("BACKEND_ERROR", response.RootElement.GetProperty("code").GetString());
                Check(response.RootElement.GetProperty("message").GetString()!.Contains("version conflict", StringComparison.OrdinalIgnoreCase),
                    "Version conflict message was not returned.");
            });

            await TestAsync("local shutdown", async () =>
            {
                using var response = await SendAsync(port, new { requestId = "shutdown-1", command = "shutdown" });
                Equal(true, response.RootElement.GetProperty("ok").GetBoolean());
                await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
            });

            await TestAsync("C# HalconDotNet offline pipeline", async () =>
            {
                var recipePath = Path.Combine(Environment.CurrentDirectory, "config", "vision.recipe.json");
                var recipe = await VisionRecipe.LoadAsync(recipePath);
                recipe = recipe with
                {
                    Calibration = recipe.Calibration with
                    {
                        ModelFile = Path.Combine(runtimeDirectory, "calibration", "camera-to-machine.json")
                    }
                };
                var calibration = await new CalibrationService().LoadOrCreateAsync(recipe.Calibration);
                await using var camera = new HalconCamera(recipe.Camera);
                await camera.OpenAsync();
                using var shapeModelLocator = new ShapeModelLocator(recipe.Localization);
                var inspection = new PinInspectionService(
                    camera,
                    new ImageQualityInspector(),
                    new PcbHoleLocator(),
                    new PinTipLocator(),
                    new CoordinateTransformer(calibration),
                    shapeModelLocator,
                    new XYThetaCalculator(),
                    new ImageStore(runtimeDirectory),
                    recipe,
                    calibration);
                var result = await inspection.InspectAsync("halcon-offline-1", null);
                Console.WriteLine(
                    $"HALCON result: OK={result.IsOk}, X={result.X:F6}, Y={result.Y:F6}, " +
                    $"T={result.Theta:F6}, Score={result.Score:F3}");
                Equal(true, result.IsOk);
                Near(0.018, result.X, 0.001);
                Near(-0.012, result.Y, 0.001);
                Near(0.006, result.Theta, 0.001);
                Check(result.ImagePath is not null && File.Exists(result.ImagePath), "HALCON source image was not persisted.");
            });

            await TestAsync("production cached ShapeModel locator", async () =>
            {
                Check(shapeModelPath is not null, "Shape model path is unavailable.");
                var recipe = await VisionRecipe.LoadAsync(
                    Path.Combine(Environment.CurrentDirectory, "config", "vision.recipe.json"));
                recipe = recipe with
                {
                    Calibration = recipe.Calibration with
                    {
                        ModelFile = Path.Combine(runtimeDirectory, "calibration", "shape-locator-calibration.json")
                    }
                };
                var calibration = await new CalibrationService().LoadOrCreateAsync(recipe.Calibration);
                var transformer = new CoordinateTransformer(calibration);
                using var locator = new ShapeModelLocator(new LocalizationOptions
                {
                    Mode = "ShapeModel",
                    ModelFile = shapeModelPath!,
                    AngleStartDeg = -5,
                    AngleExtentDeg = 10,
                    MinScore = 0.5,
                    NumLevels = 0,
                    Greediness = 0.9
                });
                HOperatorSet.ReadImage(out HObject image, offlineImage);
                using (image)
                {
                    HOperatorSet.Rgb1ToGray(image, out HObject grayImage);
                    using (grayImage)
                    {
                        var pose = locator.Locate(grayImage, transformer);
                        Check(pose.Score >= 0.9, "Production ShapeModel locator score is unexpectedly low.");
                        Check(double.IsFinite(pose.Xmm) && double.IsFinite(pose.Ymm) && double.IsFinite(pose.ThetaDeg),
                            "Production ShapeModel pose contains a non-finite value.");
                    }
                }
            });

            await TestAsync("image-quality gate rejects blank frames", () =>
            {
                HOperatorSet.GenImageConst(out HObject blankImage, "byte", 320, 240);
                using (blankImage)
                {
                    var quality = new ImageQualityInspector().Inspect(blankImage, new ImageQualityOptions());
                    Equal(false, quality.IsAcceptable);
                    Check(quality.FailureReasons.Contains("IMAGE_MEAN_GRAY_OUT_OF_RANGE"),
                        "Blank frame did not fail the mean-gray gate.");
                    Check(quality.FailureReasons.Contains("IMAGE_CONTRAST_TOO_LOW"),
                        "Blank frame did not fail the contrast gate.");
                }

                return Task.CompletedTask;
            });

            Console.WriteLine($"All {_passed} self-tests passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            server.RequestStop();
            try
            {
                await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (Exception)
            {
            }

            return 1;
        }
    }

    private static async Task TestAsync(string name, Func<Task> test)
    {
        await test();
        _passed++;
        Console.WriteLine($"PASS {name}");
    }

    private static async Task<JsonDocument> SendAsync(int port, object request) =>
        await SendRawAsync(port, JsonSerializer.Serialize(request));

    private static async Task<JsonDocument> SendRawAsync(int port, string request)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.UTF8.GetBytes(request + "\n"));
        return JsonDocument.Parse(await ReadLineAsync(stream));
    }

    private static async Task<string> ReadLineAsync(NetworkStream stream)
    {
        var buffer = new byte[1];
        using var result = new MemoryStream();
        while (await stream.ReadAsync(buffer) == 1)
        {
            if (buffer[0] == (byte)'\n')
            {
                return Encoding.UTF8.GetString(result.ToArray()).TrimEnd('\r');
            }

            result.WriteByte(buffer[0]);
        }

        throw new IOException("Connection closed before a complete response.");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Near(double expected, double actual, double tolerance)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            throw new InvalidOperationException(
                $"Expected '{expected}' ± {tolerance}, got '{actual}'.");
        }
    }
}
