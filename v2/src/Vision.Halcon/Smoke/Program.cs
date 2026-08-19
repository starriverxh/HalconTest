using System.Text.Json;
using System.Text.Json.Serialization;
using VisionSystem.Application;
using VisionSystem.Domain;
using VisionSystem.Halcon.Acquisition;
using VisionSystem.Halcon.Board;
using VisionSystem.Halcon.Metrology;
using VisionSystem.Halcon.Pins;
using VisionSystem.Halcon.Safety;

var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    PropertyNameCaseInsensitive = true,
    Converters = { new JsonStringEnumConverter() }
};
var recipe = JsonSerializer.Deserialize<VisionRecipe>(
    await File.ReadAllTextAsync("v2/datasets/recipes/pcb-pin-row-offline-v2.json"), options)
    ?? throw new InvalidDataException("Recipe is empty.");

await using var boardSource = new FileFrameSource(CameraRole.Board);
await boardSource.OpenAsync(recipe.BoardCamera, CancellationToken.None);
using var boardFrame = await boardSource.AcquireAsync(null, CancellationToken.None);
using var boardAlgorithm = new HalconBoardVisionAlgorithm();
var board = await boardAlgorithm.LocateAsync(boardFrame, recipe, CancellationToken.None);
Console.WriteLine($"board valid={board.IsValid} failures={string.Join(',', board.Failures.Select(x => x.Code))}");
Console.WriteLine($"board features={board.Value?.HolesInMachine.Count ?? 0} fallback={board.Metrics.GetValueOrDefault("board.fine.fallbackCount")}");

var archiveRoot = Path.Combine(Path.GetTempPath(), $"vision-v2-halcon-smoke-{Guid.NewGuid():N}");
var archivePassed = false;
try
{
    var archive = new HalconFrameArchive(archiveRoot);
    var evidence = await archive.ArchiveAsync("smoke-board", boardFrame, CancellationToken.None);
    archivePassed =
        evidence.Provenance == AcquisitionProvenance.OfflineFile &&
        evidence.Sha256 is { Length: 64 } &&
        evidence.ArchivedImagePath is not null &&
        File.Exists(evidence.ArchivedImagePath) &&
        !Directory.EnumerateFiles(archiveRoot, "*", SearchOption.AllDirectories)
            .Any(path => Path.GetFileName(path).Contains(".tmp.", StringComparison.Ordinal));
    Console.WriteLine($"frame archive valid={archivePassed} sha256={evidence.Sha256}");
}
finally
{
    if (Directory.Exists(archiveRoot))
    {
        Directory.Delete(archiveRoot, recursive: true);
    }
}

await using var pinSource = new FileFrameSource(CameraRole.Pins);
await pinSource.OpenAsync(recipe.PinCamera, CancellationToken.None);
using var pinAlgorithm = new HalconPinVisionAlgorithm();
using var pinFrame = await pinSource.AcquireAsync(null, CancellationToken.None);
var pins = await pinAlgorithm.InspectAsync(pinFrame, recipe, CancellationToken.None);
Console.WriteLine($"pins valid={pins.IsValid} failures={string.Join(',', pins.Failures.Select(x => x.Code))}");
Console.WriteLine($"pins features={pins.Value?.PinsInMachine.Count ?? 0} fallback={pins.Metrics.GetValueOrDefault("pins.fine.fallbackCount")}");

using var missingFrame = await pinSource.AcquireAsync(
    "v2/datasets/images/synthetic_pin_missing.pgm", CancellationToken.None);
var missing = await pinAlgorithm.InspectAsync(missingFrame, recipe, CancellationToken.None);
Console.WriteLine($"missing-pin failures={string.Join(',', missing.Failures.Select(x => x.Code))}");

using var bentFrame = await pinSource.AcquireAsync(
    "v2/datasets/images/synthetic_pin_bent.pgm", CancellationToken.None);
var bent = await pinAlgorithm.InspectAsync(bentFrame, recipe, CancellationToken.None);
Console.WriteLine($"bent-pin failures={string.Join(',', bent.Failures.Select(x => x.Code))}");

var fallbackPolicyPassed =
    HalconSafetyPolicy.RejectsFineMeasurementFallback(RecipeLifecycleStatuses.Published) &&
    HalconSafetyPolicy.RejectsFineMeasurementFallback(RecipeLifecycleStatuses.Active) &&
    !HalconSafetyPolicy.RejectsFineMeasurementFallback(RecipeLifecycleStatuses.Engineering);

var activeRecipe = recipe with { Status = RecipeLifecycleStatuses.Active };
var forcedFallbackCircle = new FineCircleOptions
{
    SearchRadiusPx = 30,
    MinimumRadiusPx = 10,
    MaximumRadiusPx = 24,
    EdgeLow = 250,
    EdgeHigh = 255,
    AllowAreaCenterFallback = true
};
using var activeBoardFrame = await boardSource.AcquireAsync(null, CancellationToken.None);
using var activeBoardAlgorithm = new HalconBoardVisionAlgorithm(new BoardAlgorithmOptions
{
    FineCircle = forcedFallbackCircle,
    RejectFineFallbackInProduction = true
});
var activeBoardFallback = await activeBoardAlgorithm.LocateAsync(
    activeBoardFrame, activeRecipe, CancellationToken.None);

using var activePinFrame = await pinSource.AcquireAsync(null, CancellationToken.None);
using var activePinAlgorithm = new HalconPinVisionAlgorithm(new PinAlgorithmOptions
{
    FineCircle = forcedFallbackCircle,
    RejectFineFallbackInProduction = true
});
var activePinFallback = await activePinAlgorithm.InspectAsync(
    activePinFrame, activeRecipe, CancellationToken.None);
var activeFallbackRejected =
    activeBoardFallback.Value is null && activeBoardFallback.Failures.Count > 0 &&
    activePinFallback.Value is null && activePinFallback.Failures.Count > 0;
Console.WriteLine($"active fallback rejected={activeFallbackRejected}");

if (!board.IsValid || board.Value?.HolesInMachine.Count != 5 ||
    !pins.IsValid || pins.Value?.PinsInMachine.Count != 5 ||
    !missing.Failures.Any(failure => failure.Code == VisionFailureCode.PinMissing) ||
    !bent.Failures.Any(failure => failure.Code == VisionFailureCode.PinBent) ||
    !fallbackPolicyPassed || !activeFallbackRejected || !archivePassed)
{
    return 1;
}

return 0;
