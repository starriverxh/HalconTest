using VisionSystem.Application;
using VisionSystem.Domain;

namespace VisionSystem.Application.Tests;

public static class Program
{
    private static int _passed;

    public static async Task<int> Main()
    {
        Run("State machine accepts a complete legal cycle", TestLegalStateSequence);
        Run("State machine rejects an illegal transition", TestIllegalStateSequence);
        Run("Rigid solver recovers rotation and translation", TestRigidFit);
        Run("Rigid solver converts transform through real T-axis pivot", TestPivotCommand);
        Run("Rigid solver rejects missing and duplicate IDs", TestIdFailures);
        await RunAsync("Released real cycle returns Alignment and permits safe motion", TestPassedAlignmentAsync);
        await RunAsync("Engineering simulator never authorizes motion", TestEngineeringOnlyAsync);
        await RunAsync("Engineering product defect remains Rejected", TestEngineeringDefectAsync);
        await RunAsync("Safe command and predicted residual are independent gates", TestIndependentGatesAsync);
        await RunAsync("Point-ID mismatch rejects the part, not the service", TestCycleIdFailureAsync);
        await RunAsync("Product defect is Rejected while equipment exception is Faulted", TestRejectedVersusFaultAsync);
        await RunAsync("Non-live frame provenance can never authorize motion", TestNonLiveProvenanceAsync);
        await RunAsync("Image archive failure faults before measurement", TestArchiveFailureAsync);
        Run("Released recipes require full image retention", TestReleasedRecipeRequiresArchive);

        Console.WriteLine($"Application tests passed: {_passed}/14");
        return 0;
    }

    private static void TestLegalStateSequence()
    {
        var machine = new VisionCycleStateMachine();
        var sequence = new[]
        {
            VisionCycleState.ValidatingRequest,
            VisionCycleState.LoadingRecipe,
            VisionCycleState.CheckingInterlocks,
            VisionCycleState.AcquiringBoardImage,
            VisionCycleState.CheckingBoardImage,
            VisionCycleState.LocatingBoard,
            VisionCycleState.InspectingPins,
            VisionCycleState.TransformingCoordinates,
            VisionCycleState.CalculatingCorrection,
            VisionCycleState.PersistingResult,
            VisionCycleState.Completed
        };

        foreach (var state in sequence)
        {
            True(machine.CanTransitionTo(state));
            machine.TransitionTo(state);
        }

        True(machine.IsTerminal);
        Equal(VisionCycleState.Completed, machine.Current);
        Equal(sequence.Length + 1, machine.History.Count);
    }

    private static void TestIllegalStateSequence()
    {
        var machine = new VisionCycleStateMachine();
        Throws<InvalidOperationException>(() => machine.TransitionTo(VisionCycleState.Completed));
        Equal(VisionCycleState.Idle, machine.Current);
        Equal(1, machine.History.Count);
    }

    private static void TestRigidFit()
    {
        var pins = Points(("A", 0, 0), ("B", 10, 0), ("C", 0, 8));
        const double angleDeg = 17;
        var holes = pins.Select(pin => new IdentifiedMachinePoint(
            pin.Id,
            RotateThenTranslate(pin.Point, angleDeg, new MachinePoint(1.25, -0.75)))).ToArray();
        var result = new RigidAlignmentSolver().Solve(
            pins,
            holes,
            new MachineKinematicsRecipe { RotationCenterMachine = new MachinePoint(0, 0) });

        True(result.IsValid);
        var plan = result.Value ?? throw new InvalidOperationException("Alignment plan is missing.");
        Near(angleDeg, plan.RigidRotationDeg, 1e-10);
        Near(1.25, plan.RigidTranslation.Xmm, 1e-10);
        Near(-0.75, plan.RigidTranslation.Ymm, 1e-10);
        Near(0, plan.MaximumResidualMm, 1e-10);
    }

    private static void TestPivotCommand()
    {
        var pins = Points(("A", 1, 2), ("B", 14, 3), ("C", 4, 11));
        var pivot = new MachinePoint(100, -50);
        var expectedCommand = new MachinePoint(0.35, -0.22);
        const double angleDeg = 12;
        var holes = pins.Select(pin => new IdentifiedMachinePoint(
            pin.Id,
            ApplyMachineCommand(pin.Point, pivot, angleDeg, expectedCommand))).ToArray();
        var result = new RigidAlignmentSolver().Solve(
            pins,
            holes,
            new MachineKinematicsRecipe { RotationCenterMachine = pivot });

        True(result.IsValid);
        var plan = result.Value ?? throw new InvalidOperationException("Alignment plan is missing.");
        Near(expectedCommand.Xmm, plan.AxisCommand.Xmm, 1e-10);
        Near(expectedCommand.Ymm, plan.AxisCommand.Ymm, 1e-10);
        Near(angleDeg, plan.AxisCommand.ThetaDeg, 1e-10);
        Near(0, plan.MaximumResidualMm, 1e-10);
    }

    private static void TestIdFailures()
    {
        var solver = new RigidAlignmentSolver();
        var pins = Points(("A", 0, 0), ("B", 10, 0));
        var missing = solver.Solve(
            pins,
            Points(("A", 0, 0), ("C", 10, 0)),
            new MachineKinematicsRecipe());
        False(missing.IsValid);
        Equal(VisionFailureCode.PointCorrespondenceFailed, missing.Failures.Single().Code);

        var duplicate = solver.Solve(
            Points(("A", 0, 0), ("A", 10, 0)),
            Points(("A", 0, 0), ("B", 10, 0)),
            new MachineKinematicsRecipe());
        False(duplicate.IsValid);
        Equal(VisionFailureCode.PointCorrespondenceFailed, duplicate.Failures.Single().Code);
    }

    private static async Task TestPassedAlignmentAsync()
    {
        var pins = Points(("A", 0, 0), ("B", 10, 0), ("C", 0, 10));
        var holes = pins.Select(point => point with
        {
            Point = new MachinePoint(point.Point.Xmm + 0.4, point.Point.Ymm - 0.2)
        }).ToArray();
        var result = await ExecuteAsync(
            CreateRecipe(),
            BoardSuccess(holes),
            PinSuccess(pins),
            new FakeCamera(CameraRole.Pins));

        Equal(InspectionDisposition.Passed, result.Disposition);
        True(result.MotionAllowed);
        NotNull(result.Alignment);
        Near(0.4, result.Alignment!.AxisCommand.Xmm, 1e-10);
        Near(-0.2, result.Alignment.AxisCommand.Ymm, 1e-10);
        Near(0, result.Alignment.MaximumResidualMm, 1e-10);
        Equal(VisionCycleState.Completed, result.StateHistory[^1]);
        Equal(AcquisitionProvenance.LiveCamera, result.BoardFrame!.Provenance);
        Equal(AcquisitionProvenance.LiveCamera, result.PinFrame!.Provenance);
        Equal(64, result.BoardFrame.Sha256!.Length);
        True(result.StageMetrics.ContainsKey("board.featureCount"));
        True(result.StageMetrics.ContainsKey("pins.featureCount"));
        False(result.StageMetrics.ContainsKey("board.board.featureCount"));
        Equal("fake-board", result.BoardAlgorithmName);
        Equal("fake-pins", result.PinAlgorithmName);
    }

    private static async Task TestEngineeringOnlyAsync()
    {
        var pins = Points(("A", 0, 0), ("B", 10, 0), ("C", 0, 10));
        var recipe = CreateRecipe() with
        {
            Pins = CreateRecipe().Pins with
            {
                Enabled = false,
                AllowEngineeringSimulation = true,
                EngineeringReferencePins = pins,
                EngineeringReferencePose = new Pose2D(0, 0, 0)
            }
        };
        var result = await ExecuteAsync(
            recipe,
            BoardSuccess(pins),
            new EngineeringPinSimulator(),
            null);

        Equal(InspectionDisposition.EngineeringOnly, result.Disposition);
        False(result.MotionAllowed);
        NotNull(result.Alignment);
        Contains(result.Failures, VisionFailureCode.PinCameraNotCommissioned);
        Equal(VisionCycleState.Inhibited, result.StateHistory[^1]);
    }

    private static async Task TestEngineeringDefectAsync()
    {
        var points = Points(("A", 0, 0), ("B", 10, 0), ("C", 0, 10));
        var recipe = CreateRecipe() with { Status = RecipeLifecycleStatuses.Engineering };
        var pinFailure = new VisionFailure(
            VisionFailureCode.PinBent,
            "A simulated pin violates straightness tolerance.",
            true,
            "Pins");
        var result = await ExecuteAsync(
            recipe,
            BoardSuccess(points),
            new FakePinAlgorithm(new VisionStageResult<PinMeasurement>(
                CreatePinMeasurement(points, true),
                [pinFailure],
                new Dictionary<string, double>())),
            new FakeCamera(CameraRole.Pins));

        Equal(InspectionDisposition.Rejected, result.Disposition);
        False(result.MotionAllowed);
        Contains(result.Failures, VisionFailureCode.PinBent);
        Contains(result.Failures, VisionFailureCode.PinCameraNotCommissioned);
        Contains(result.Failures, VisionFailureCode.RecipeNotReleasedForMotion);
    }

    private static async Task TestIndependentGatesAsync()
    {
        var pins = Points(("A", 0, 0), ("B", 10, 0), ("C", 0, 10));
        var translatedHoles = pins.Select(point => point with
        {
            Point = new MachinePoint(point.Point.Xmm + 3, point.Point.Ymm)
        }).ToArray();
        var safeRangeRecipe = CreateRecipe() with
        {
            CorrectionTolerance = new CorrectionToleranceRecipe
            {
                MaximumSafeCommandXmm = 2,
                MaximumSafeCommandYmm = 2,
                MaximumSafeCommandThetaDeg = 2,
                MaximumPredictedResidualMm = 0.05
            }
        };
        var commandRejected = await ExecuteAsync(
            safeRangeRecipe,
            BoardSuccess(translatedHoles),
            PinSuccess(pins),
            new FakeCamera(CameraRole.Pins));
        Equal(InspectionDisposition.Rejected, commandRejected.Disposition);
        Contains(commandRejected.Failures, VisionFailureCode.CorrectionOutOfRange);
        DoesNotContain(commandRejected.Failures, VisionFailureCode.AlignmentResidualTooHigh);
        Near(0, commandRejected.Alignment!.MaximumResidualMm, 1e-10);

        var distortedHoles = Points(("A", 0, 0), ("B", 10, 0), ("C", 0, 10.3));
        var residualRejected = await ExecuteAsync(
            safeRangeRecipe,
            BoardSuccess(distortedHoles),
            PinSuccess(pins),
            new FakeCamera(CameraRole.Pins));
        Equal(InspectionDisposition.Rejected, residualRejected.Disposition);
        Contains(residualRejected.Failures, VisionFailureCode.AlignmentResidualTooHigh);
        DoesNotContain(residualRejected.Failures, VisionFailureCode.CorrectionOutOfRange);
        True(residualRejected.Alignment!.MaximumResidualMm > 0.05);
    }

    private static async Task TestCycleIdFailureAsync()
    {
        var result = await ExecuteAsync(
            CreateRecipe(),
            BoardSuccess(Points(("A", 0, 0), ("B", 10, 0), ("D", 0, 10))),
            PinSuccess(Points(("A", 0, 0), ("B", 10, 0), ("C", 0, 10))),
            new FakeCamera(CameraRole.Pins));

        Equal(InspectionDisposition.Rejected, result.Disposition);
        False(result.MotionAllowed);
        Null(result.Alignment);
        Contains(result.Failures, VisionFailureCode.PointCorrespondenceFailed);
        Equal(VisionCycleState.Rejected, result.StateHistory[^1]);
    }

    private static async Task TestRejectedVersusFaultAsync()
    {
        var points = Points(("A", 0, 0), ("B", 10, 0), ("C", 0, 10));
        var productFailure = new VisionFailure(
            VisionFailureCode.PinMissing,
            "One pin was not found.",
            true,
            "Pins");
        var rejected = await ExecuteAsync(
            CreateRecipe(),
            BoardSuccess(points),
            new FakePinAlgorithm(new VisionStageResult<PinMeasurement>(
                CreatePinMeasurement(points, false),
                [productFailure],
                new Dictionary<string, double>())),
            new FakeCamera(CameraRole.Pins));
        Equal(InspectionDisposition.Rejected, rejected.Disposition);
        False(rejected.MotionAllowed);
        Contains(rejected.Failures, VisionFailureCode.PinMissing);
        Equal(VisionCycleState.Rejected, rejected.StateHistory[^1]);

        var faulted = await ExecuteAsync(
            CreateRecipe(),
            BoardSuccess(points),
            PinSuccess(points),
            new FakeCamera(CameraRole.Pins),
            new FakeCamera(CameraRole.Board, throwOnAcquire: true));
        Equal(InspectionDisposition.Faulted, faulted.Disposition);
        False(faulted.MotionAllowed);
        Contains(faulted.Failures, VisionFailureCode.InternalError);
        Equal(VisionCycleState.Faulted, faulted.StateHistory[^1]);
    }

    private static async Task TestNonLiveProvenanceAsync()
    {
        var points = Points(("A", 0, 0), ("B", 10, 0), ("C", 0, 10));
        foreach (var provenance in new[]
                 {
                     AcquisitionProvenance.OfflineFile,
                     AcquisitionProvenance.Synthetic,
                     AcquisitionProvenance.Unknown
                 })
        {
            foreach (var offlineRole in new[] { CameraRole.Board, CameraRole.Pins })
            {
                var result = await ExecuteAsync(
                    CreateRecipe(),
                    BoardSuccess(points),
                    PinSuccess(points),
                    new FakeCamera(
                        CameraRole.Pins,
                        provenance: offlineRole == CameraRole.Pins
                            ? provenance
                            : AcquisitionProvenance.LiveCamera),
                    new FakeCamera(
                        CameraRole.Board,
                        provenance: offlineRole == CameraRole.Board
                            ? provenance
                            : AcquisitionProvenance.LiveCamera));

                Equal(InspectionDisposition.EngineeringOnly, result.Disposition);
                False(result.MotionAllowed);
                Contains(result.Failures, VisionFailureCode.AcquisitionNotLive);
                Equal(VisionCycleState.Inhibited, result.StateHistory[^1]);
            }
        }
    }

    private static async Task TestArchiveFailureAsync()
    {
        var points = Points(("A", 0, 0), ("B", 10, 0), ("C", 0, 10));
        var result = await ExecuteAsync(
            CreateRecipe(),
            BoardSuccess(points),
            PinSuccess(points),
            new FakeCamera(CameraRole.Pins),
            frameArchive: new ThrowingFrameArchive());

        Equal(InspectionDisposition.Faulted, result.Disposition);
        False(result.MotionAllowed);
        Contains(result.Failures, VisionFailureCode.ImageArchiveFailed);
        Null(result.Board);
        Null(result.BoardFrame);
    }

    private static void TestReleasedRecipeRequiresArchive()
    {
        var baseRecipe = CreateRecipe();
        var result = new RecipeValidator().Validate(baseRecipe with
        {
            ProductionPolicy = baseRecipe.ProductionPolicy with { SaveAllImages = false }
        });

        False(result.IsValid);
        Contains(result.Failures, VisionFailureCode.RecipeInvalid);
    }

    private static VisionRecipe CreateRecipe() => new()
    {
        RecipeId = "recipe-v2",
        ProductCode = "PCB-A",
        Version = 1,
        Status = RecipeLifecycleStatuses.Published,
        BoardCamera = new CameraRecipe { ExposureMicroseconds = 1000, AcquisitionTimeoutMs = 1000 },
        PinCamera = new CameraRecipe { ExposureMicroseconds = 1000, AcquisitionTimeoutMs = 1000 },
        Board = new BoardVisionRecipe
        {
            LocatorMode = BoardLocatorMode.BlobRow,
            SearchRoi = new RectangleRoi { Row1 = 1, Column1 = 1, Row2 = 100, Column2 = 100 },
            HoleFeature = new BlobFeatureRecipe
            {
                ThresholdMin = 0,
                ThresholdMax = 100,
                MinimumAreaPx = 10,
                MaximumAreaPx = 100,
                MinimumCircularity = 0.5,
                ExpectedCount = 3
            },
            FeatureIds = ["A", "B", "C"]
        },
        Pins = new PinVisionRecipe
        {
            Enabled = true,
            ExpectedPinCount = 3,
            AllowEngineeringSimulation = true
        },
        CorrectionTolerance = new CorrectionToleranceRecipe
        {
            MaximumSafeCommandXmm = 2,
            MaximumSafeCommandYmm = 2,
            MaximumSafeCommandThetaDeg = 2,
            MaximumPredictedResidualMm = 0.05
        },
        MachineKinematics = new MachineKinematicsRecipe
        {
            RotationCenterMachine = new MachinePoint(0, 0),
            CommandConvention = MachineCommandConventions.RotateAboutPivotThenTranslate
        },
        ProductionPolicy = new ProductionPolicyRecipe
        {
            RequireMotionStationary = true,
            RequireSafetyInterlock = true,
            RequirePinCamera = true,
            CycleTimeoutMs = 2000
        }
    };

    private static async Task<InspectionResult> ExecuteAsync(
        VisionRecipe recipe,
        IBoardVisionAlgorithm boardAlgorithm,
        IPinVisionAlgorithm pinAlgorithm,
        ICameraSource? pinCamera,
        ICameraSource? boardCamera = null,
        IFrameArchive? frameArchive = null)
    {
        var orchestrator = new InspectionOrchestrator(
            new FakeRecipeRepository(recipe),
            new RecipeValidator(),
            new ReadyEquipment(),
            boardCamera ?? new FakeCamera(CameraRole.Board),
            pinCamera,
            boardAlgorithm,
            pinAlgorithm,
            frameArchive ?? new MemoryFrameArchive(),
            new MemoryInspectionRepository(),
            new MemoryCycleEventSink(),
            new IncrementingClock(),
            new RigidAlignmentSolver());
        return await orchestrator.ExecuteAsync(new InspectionRequest(
            $"request-{Guid.NewGuid():N}",
            "part-001",
            recipe.RecipeId));
    }

    private static IBoardVisionAlgorithm BoardSuccess(IReadOnlyList<IdentifiedMachinePoint> holes) =>
        new FakeBoardAlgorithm(VisionStageResult<BoardMeasurement>.Success(new BoardMeasurement(
            new Pose2D(0, 0, 0),
            1,
            [],
            holes,
            new ImageQualityMeasurement(100, 20, 10, 0),
            "fake-board"),
            new Dictionary<string, double> { ["board.featureCount"] = holes.Count }));

    private static IPinVisionAlgorithm PinSuccess(IReadOnlyList<IdentifiedMachinePoint> pins) =>
        new FakePinAlgorithm(VisionStageResult<PinMeasurement>.Success(
            CreatePinMeasurement(pins, false),
            new Dictionary<string, double> { ["pins.featureCount"] = pins.Count }));

    private static PinMeasurement CreatePinMeasurement(
        IReadOnlyList<IdentifiedMachinePoint> pins,
        bool simulated) => new(
        new Pose2D(0, 0, 0),
        1,
        [],
        pins,
        simulated,
        simulated ? "simulator" : "fake-pins");

    private static IdentifiedMachinePoint[] Points(
        params (string Id, double X, double Y)[] values) => values
        .Select(value => new IdentifiedMachinePoint(value.Id, new MachinePoint(value.X, value.Y)))
        .ToArray();

    private static MachinePoint RotateThenTranslate(
        MachinePoint point,
        double angleDeg,
        MachinePoint translation)
    {
        var radians = angleDeg * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return new MachinePoint(
            cos * point.Xmm - sin * point.Ymm + translation.Xmm,
            sin * point.Xmm + cos * point.Ymm + translation.Ymm);
    }

    private static MachinePoint ApplyMachineCommand(
        MachinePoint point,
        MachinePoint pivot,
        double angleDeg,
        MachinePoint translation)
    {
        var relative = new MachinePoint(point.Xmm - pivot.Xmm, point.Ymm - pivot.Ymm);
        var rotated = RotateThenTranslate(relative, angleDeg, new MachinePoint());
        return new MachinePoint(
            rotated.Xmm + pivot.Xmm + translation.Xmm,
            rotated.Ymm + pivot.Ymm + translation.Ymm);
    }

    private static void Run(string name, Action test)
    {
        test();
        _passed++;
        Console.WriteLine($"PASS {name}");
    }

    private static async Task RunAsync(string name, Func<Task> test)
    {
        await test();
        _passed++;
        Console.WriteLine($"PASS {name}");
    }

    private static void True(bool value)
    {
        if (!value)
        {
            throw new InvalidOperationException("Expected true.");
        }
    }

    private static void False(bool value) => True(!value);

    private static void Null(object? value)
    {
        if (value is not null)
        {
            throw new InvalidOperationException("Expected null.");
        }
    }

    private static void NotNull(object? value)
    {
        if (value is null)
        {
            throw new InvalidOperationException("Expected a value.");
        }
    }

    private static void Equal<T>(T expected, T actual) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
        }
    }

    private static void Near(double expected, double actual, double tolerance)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            throw new InvalidOperationException($"Expected {expected}, got {actual} (tolerance {tolerance}).");
        }
    }

    private static void Contains(IEnumerable<VisionFailure> failures, VisionFailureCode code)
    {
        if (failures.All(failure => failure.Code != code))
        {
            throw new InvalidOperationException($"Expected failure '{code}'.");
        }
    }

    private static void DoesNotContain(IEnumerable<VisionFailure> failures, VisionFailureCode code)
    {
        if (failures.Any(failure => failure.Code == code))
        {
            throw new InvalidOperationException($"Did not expect failure '{code}'.");
        }
    }

    private static void Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private sealed class FakeRecipeRepository(VisionRecipe recipe) : IRecipeRepository
    {
        public Task<VisionRecipe> GetAsync(string recipeId, CancellationToken cancellationToken) =>
            Task.FromResult(recipe);

        public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([recipe.RecipeId]);
    }

    private sealed class FakeFrame(
        CameraRole role,
        AcquisitionProvenance provenance) : IVisionFrame
    {
        public string FrameId { get; } = Guid.NewGuid().ToString("N");
        public CameraRole Role { get; } = role;
        public DateTimeOffset CapturedUtc { get; } = DateTimeOffset.UtcNow;
        public int Width => 640;
        public int Height => 480;
        public string SourceName => "fake";
        public AcquisitionProvenance Provenance { get; } = provenance;
        public void Dispose() { }
    }

    private sealed class FakeCamera(
        CameraRole role,
        bool throwOnAcquire = false,
        AcquisitionProvenance provenance = AcquisitionProvenance.LiveCamera) : ICameraSource
    {
        public CameraRole Role { get; } = role;
        public string Name => $"fake-{Role}";
        public bool IsReady { get; private set; }

        public Task OpenAsync(CameraRecipe recipe, CancellationToken cancellationToken)
        {
            IsReady = true;
            return Task.CompletedTask;
        }

        public Task<IVisionFrame> AcquireAsync(string? imageOverride, CancellationToken cancellationToken)
        {
            if (throwOnAcquire)
            {
                throw new InvalidOperationException("Injected camera failure.");
            }

            return Task.FromResult<IVisionFrame>(new FakeFrame(Role, provenance));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeBoardAlgorithm(VisionStageResult<BoardMeasurement> result) : IBoardVisionAlgorithm
    {
        public string Name => "fake-board";
        public Task<VisionStageResult<BoardMeasurement>> LocateAsync(
            IVisionFrame frame,
            VisionRecipe recipe,
            CancellationToken cancellationToken) => Task.FromResult(result);
        public void Dispose() { }
    }

    private sealed class FakePinAlgorithm(VisionStageResult<PinMeasurement> result) : IPinVisionAlgorithm
    {
        public string Name => "fake-pins";
        public Task<VisionStageResult<PinMeasurement>> InspectAsync(
            IVisionFrame? frame,
            VisionRecipe recipe,
            CancellationToken cancellationToken) => Task.FromResult(result);
        public void Dispose() { }
    }

    private sealed class ReadyEquipment : IEquipmentReadiness
    {
        public Task<EquipmentReadiness> CheckAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new EquipmentReadiness(true, true, []));
    }

    private sealed class MemoryInspectionRepository : IInspectionRepository
    {
        public Task<string> SaveAsync(InspectionResult result, CancellationToken cancellationToken) =>
            Task.FromResult("memory://inspection-result");
    }

    private sealed class MemoryFrameArchive : IFrameArchive
    {
        public Task<FrameEvidence> ArchiveAsync(
            string requestId,
            IVisionFrame frame,
            CancellationToken cancellationToken) => Task.FromResult(new FrameEvidence
        {
            FrameId = frame.FrameId,
            CameraRole = frame.Role.ToString(),
            Provenance = frame.Provenance,
            CapturedUtc = frame.CapturedUtc,
            Width = frame.Width,
            Height = frame.Height,
            SourceName = frame.SourceName,
            ArchivedImagePath = $"memory://{requestId}/{frame.Role}",
            Sha256 = new string('0', 64)
        });
    }

    private sealed class ThrowingFrameArchive : IFrameArchive
    {
        public Task<FrameEvidence> ArchiveAsync(
            string requestId,
            IVisionFrame frame,
            CancellationToken cancellationToken) =>
            throw new IOException("Injected frame archive failure.");
    }

    private sealed class MemoryCycleEventSink : ICycleEventSink
    {
        public Task StateChangedAsync(
            string requestId,
            VisionCycleState state,
            DateTimeOffset timestampUtc,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class IncrementingClock : ISystemClock
    {
        private long _milliseconds;
        public DateTimeOffset UtcNow =>
            DateTimeOffset.UnixEpoch.AddMilliseconds(Interlocked.Increment(ref _milliseconds));
    }
}
