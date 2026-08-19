using System.Net;
using VisionSystem.Application;
using VisionSystem.Halcon.Acquisition;
using VisionSystem.Halcon.Board;
using VisionSystem.Halcon.Pins;
using VisionSystem.Infrastructure;
using VisionSystem.Runtime;

var repositoryRoot = WorkspaceLayout.FindRepositoryRoot();
Directory.SetCurrentDirectory(repositoryRoot);
var options = RuntimeOptions.Parse(args, Path.Combine(repositoryRoot, "v2"));
if (options.ProductionCommissioned)
{
    throw new InvalidOperationException(
        "This executable is composed with FileFrameSource and cannot be marked production-commissioned. " +
        "Replace both camera adapters, equipment readiness and calibration activation before enabling motion.");
}

Directory.CreateDirectory(options.RuntimeDirectory);
var recipeRepository = new JsonRecipeRepository(options.RecipesDirectory);
var resultRepository = new JsonInspectionRepository(options.RuntimeDirectory);
var equipment = new CommissioningEquipmentReadiness();
var eventSink = new JsonLineCycleEventSink(options.RuntimeDirectory);
var frameArchive = new HalconFrameArchive(options.RuntimeDirectory);
var clock = new SystemClock();
await using var boardCamera = new FileFrameSource(CameraRole.Board);
await using var pinCamera = new FileFrameSource(CameraRole.Pins);
using var boardAlgorithm = new HalconBoardVisionAlgorithm();
using var pinAlgorithm = new HalconPinVisionAlgorithm();
var orchestrator = new InspectionOrchestrator(
    recipeRepository,
    new RecipeValidator(),
    equipment,
    boardCamera,
    pinCamera,
    boardAlgorithm,
    pinAlgorithm,
    frameArchive,
    resultRepository,
    eventSink,
    clock,
    new RigidAlignmentSolver());

var station = new RuntimeStation(productionCommissioned: false);
station.CompleteStartup();
await using var queue = new IdempotentInspectionQueue(
    options.QueueCapacity,
    options.IdempotencyCapacity,
    station,
    orchestrator.ExecuteAsync);
var productionAddress = IPAddress.TryParse(options.BindAddress, out var parsedAddress)
    ? parsedAddress
    : throw new ArgumentException($"--bind must be an IP address, got '{options.BindAddress}'.");
await using var productionServer = new NdjsonVisionServer(
    productionAddress,
    options.ProductionPort,
    VisionEndpointKind.Production,
    station,
    queue);
await using var engineeringServer = new NdjsonVisionServer(
    IPAddress.Loopback,
    options.EngineeringPort,
    VisionEndpointKind.Engineering,
    station,
    queue);

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine("Vision.Runtime V2");
Console.WriteLine("Mode: OfflineCommissioning");
Console.WriteLine($"Recipe directory: {options.RecipesDirectory}");
Console.WriteLine($"Runtime directory: {options.RuntimeDirectory}");
Console.WriteLine($"Production: {productionAddress}:{options.ProductionPort} (inspect disabled)");
Console.WriteLine($"Engineering: 127.0.0.1:{options.EngineeringPort}");
Console.WriteLine("Motion authority: DISABLED");
Console.WriteLine("Press Ctrl+C to stop.");

try
{
    await Task.WhenAll(
        productionServer.RunAsync(shutdown.Token),
        engineeringServer.RunAsync(shutdown.Token));
}
finally
{
    station.BeginStopping();
    station.CompleteStopping();
}

internal static class WorkspaceLayout
{
    public static string FindRepositoryRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(Path.GetFullPath(start));
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "v2", "HalconPinInsertion.V2.sln")) &&
                    File.Exists(Path.Combine(
                        directory.FullName,
                        "v2",
                        "datasets",
                        "images",
                        "synthetic_pcb_good.png")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not locate the HalconTest repository root. Run from the repository or use the provided scripts.");
    }
}
