# VisionSystem.Halcon

This assembly is the HALCON boundary of V2. Domain and Application never reference `HalconDotNet`; `HalconFrame`
owns the iconic image and exposes it only inside this assembly. Shape-model handles are cached per algorithm instance
and cleared by `Dispose`.

Minimal offline composition:

```csharp
await using var boardCamera = new FileFrameSource(CameraRole.Board);
await using var pinCamera = new FileFrameSource(CameraRole.Pins);
using var boardAlgorithm = new HalconBoardVisionAlgorithm();
using var pinAlgorithm = new HalconPinVisionAlgorithm();

await boardCamera.OpenAsync(recipe.BoardCamera, token);
using var boardFrame = await boardCamera.AcquireAsync(boardPath, token);
var board = await boardAlgorithm.LocateAsync(boardFrame, recipe, token);

await pinCamera.OpenAsync(recipe.PinCamera, token);
using var pinFrame = await pinCamera.AcquireAsync(pinPath, token);
var pins = await pinAlgorithm.InspectAsync(pinFrame, recipe, token);
```

Public composition types:

- `FileFrameSource(CameraRole)` — deterministic file acquisition; refuses hardware camera modes.
- `HalconBoardVisionAlgorithm(BoardAlgorithmOptions?)` — quality gate, optional shape-model coarse pose, followed
  blob ROI, XLD circle refinement, and planar machine mapping.
- `HalconPinVisionAlgorithm(PinAlgorithmOptions?)` — every pin tip, fine circles, pitch/straightness, and independent
  up-camera mapping. A file-camera result is always marked simulated/engineering-only.
- `IHalconDiagnosticsSource.LastDiagnostics` — ordered stage outcomes; `VisionStageResult.Metrics` contains the same
  numeric timing and measurement data in flattened form. A residual metric of `-1` means no XLD fit was available;
  finite sentinels are used so traceability JSON never contains unsupported NaN/Infinity values.
- `HalconSafetyPolicy.RejectsFineMeasurementFallback(status)` — rejects `area_center` fallback for any lifecycle
  state that can authorize automatic motion.

Fine localization uses `edges_sub_pix` and robust `fit_circle_contour_xld('atukey')`. The configured fallback keeps
poor engineering images debuggable, but every fallback is counted and active/published recipes reject it.

The included planar JSON loader applies a six-value `HomMat2D`. It is appropriate for deterministic offline data and
for an already-undistorted, fixed-height plane only. Production commissioning must add and validate the actual camera
intrinsic model, lens distortion, work-plane height, camera/lens serial binding, and independent validation points.

HALCON 23.11 Progress x64 is expected at `%HALCONROOT%`; the local development fallback is
`D:\Program Files\MVTec\HALCON-23.11-Progress`. The project references
`bin\dotnet35\halcondotnet.dll` and therefore builds/runs x64.
