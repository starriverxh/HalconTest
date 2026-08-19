using Vision.Service.Acquisition;
using Vision.Service.Communication;
using Vision.Service.Configuration;
using Vision.Service.Storage;
using Vision.Service.Vision;
using Vision.Service.Workbench;

namespace Vision.Service;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = ServiceOptions.Parse(args);
            await using var backend = await CreateBackendAsync(options).ConfigureAwait(false);
            var workbench = new VisionWorkbenchService(options.RuntimeDirectory, options.RecipePath);
            await using var server = new VisionTcpServer(options, backend, workbench);
            using var shutdownSource = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                shutdownSource.Cancel();
                server.RequestStop();
            };

            Console.WriteLine($"Vision service listening on {options.ListenAddress}:{options.Port} using {backend.Name} backend.");
            await server.RunAsync(shutdownSource.Token).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Vision service failed: {exception.Message}");
            return 1;
        }
    }

    private static async Task<IVisionBackend> CreateBackendAsync(ServiceOptions options) => options.Backend.ToLowerInvariant() switch
    {
        "mock" => new MockVisionBackend(TimeSpan.FromMilliseconds(options.MockDelayMs)),
        "halcon" or "halcondotnet" => await CreateHalconBackendAsync(options).ConfigureAwait(false),
        _ => throw new ArgumentException($"Unsupported backend '{options.Backend}'.")
    };

    private static async Task<IVisionBackend> CreateHalconBackendAsync(ServiceOptions options)
    {
        var recipe = await VisionRecipe.LoadAsync(options.RecipePath).ConfigureAwait(false);
        var calibration = await new CalibrationService()
            .LoadOrCreateAsync(recipe.Calibration)
            .ConfigureAwait(false);
        var camera = new HalconCamera(recipe.Camera);
        await camera.OpenAsync().ConfigureAwait(false);
        var shapeModelLocator = new ShapeModelLocator(recipe.Localization);
        var inspectionService = new PinInspectionService(
            camera,
            new ImageQualityInspector(),
            new PcbHoleLocator(),
            new PinTipLocator(),
            new CoordinateTransformer(calibration),
            shapeModelLocator,
            new XYThetaCalculator(),
            new ImageStore(options.RuntimeDirectory),
            recipe,
            calibration);

        Console.WriteLine(
            $"Recipe '{recipe.RecipeName}', calibration RMSE={calibration.RmseMm:F6} mm, " +
            $"validation max={calibration.MaxValidationErrorMm:F6} mm.");
        return new HalconVisionBackend(inspectionService, camera, shapeModelLocator);
    }
}
