using VisionSystem.Domain;
using VisionSystem.Runtime;

namespace VisionSystem.Runtime.Tests;

public static class Program
{
    private static int _passed;

    public static async Task<int> Main()
    {
        Run("Uncommissioned station accepts engineering but rejects production", TestEngineeringStation);
        Run("Commissioned station completes production and requires ACK", TestProductionStation);
        Run("Fault requires reset and rejects ACK", TestFaultResetRules);
        await RunAsync("Identical request ID and content executes exactly once", TestIdempotentReplayAsync);
        await RunAsync("Same request ID with different content conflicts", TestRequestIdConflictAsync);
        await RunAsync("New cycle is rejected until previous result is ACKed", TestUnacknowledgedResultBlocksAsync);
        Run("InspectionResultMapper preserves every point residual", TestResultMapping);

        Console.WriteLine($"Runtime tests passed: {_passed}/7");
        return 0;
    }

    private static void TestEngineeringStation()
    {
        var station = new RuntimeStation(false);
        Equal(RuntimeStationState.Starting.ToString(), station.Snapshot().State);
        station.CompleteStartup();
        var ready = station.Snapshot();
        Equal(RuntimeStationState.Uncommissioned.ToString(), ready.State);
        Equal("OfflineCommissioning", ready.Mode);
        False(ready.MotionPermittedByStation);
        False(station.TryBegin("prod-1", "recipe-a", engineering: false));
        True(station.TryBegin("eng-1", "recipe-a", engineering: true));
        Equal(RuntimeStationState.Processing.ToString(), station.Snapshot().State);
        station.Complete("eng-1", systemFault: false);
        Equal(RuntimeStationState.ResultAvailable.ToString(), station.Snapshot().State);
        Equal(AcknowledgeStatus.Acknowledged, station.Acknowledge("eng-1"));
        Equal(RuntimeStationState.Uncommissioned.ToString(), station.Snapshot().State);
        Equal(AcknowledgeStatus.NotAvailable, station.Acknowledge("eng-1"));
    }

    private static void TestProductionStation()
    {
        var station = ReadyStation(productionCommissioned: true);
        var ready = station.Snapshot();
        Equal(RuntimeStationState.Ready.ToString(), ready.State);
        True(ready.MotionPermittedByStation);
        True(station.TryBegin("prod-1", "recipe-a", engineering: false));
        station.Complete("prod-1", systemFault: false);
        var completed = station.Snapshot();
        Equal(RuntimeStationState.ResultAvailable.ToString(), completed.State);
        Equal("prod-1", completed.LastCompletedRequestId!);
        False(completed.MotionPermittedByStation);
        False(station.TryBegin("prod-2", "recipe-a", engineering: false));
        Equal(AcknowledgeStatus.RequestIdMismatch, station.Acknowledge("wrong-request"));
        Equal(RuntimeStationState.ResultAvailable.ToString(), station.Snapshot().State);
        Equal(AcknowledgeStatus.Acknowledged, station.Acknowledge("prod-1"));
        Equal(RuntimeStationState.Ready.ToString(), station.Snapshot().State);
    }

    private static void TestFaultResetRules()
    {
        var station = ReadyStation(productionCommissioned: true);
        True(station.TryBegin("fault-1", "recipe-a", engineering: false));
        Throws<InvalidOperationException>(() => station.Complete("different-owner", systemFault: false));
        station.Complete("fault-1", systemFault: true);
        Equal(RuntimeStationState.Faulted.ToString(), station.Snapshot().State);
        Equal(AcknowledgeStatus.NotAvailable, station.Acknowledge("fault-1"));
        False(station.TryBegin("blocked", "recipe-a", engineering: false));
        True(station.ResetFault());
        Equal(RuntimeStationState.Ready.ToString(), station.Snapshot().State);
        False(station.ResetFault());
    }

    private static async Task TestIdempotentReplayAsync()
    {
        var station = ReadyStation(productionCommissioned: true);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executionCount = 0;
        await using var queue = new IdempotentInspectionQueue(
            1,
            10,
            station,
            async (request, token) =>
            {
                Interlocked.Increment(ref executionCount);
                await release.Task.WaitAsync(token);
                return Result(request);
            });
        var request = new InspectionRequest("same-id", "part-1", "recipe-a");
        var first = queue.Submit(request, engineering: false);
        var replay = queue.Submit(request, engineering: false);

        Equal(QueueSubmissionStatus.Accepted, first.Status);
        Equal(QueueSubmissionStatus.Replay, replay.Status);
        True(replay.IsReplay);
        True(ReferenceEquals(first.ResultTask, replay.ResultTask));
        release.SetResult();
        await first.ResultTask!;
        Equal(RuntimeStationState.ResultAvailable.ToString(), station.Snapshot().State);
        Equal(1, executionCount);
        Equal(AcknowledgeStatus.Acknowledged, station.Acknowledge("same-id"));
    }

    private static async Task TestRequestIdConflictAsync()
    {
        var station = ReadyStation(productionCommissioned: true);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var queue = new IdempotentInspectionQueue(
            1,
            10,
            station,
            async (request, token) =>
            {
                await release.Task.WaitAsync(token);
                return Result(request);
            });
        var first = queue.Submit(
            new InspectionRequest("conflict-id", "part-1", "recipe-a"),
            engineering: false);
        var conflict = queue.Submit(
            new InspectionRequest("conflict-id", "part-2", "recipe-a"),
            engineering: false);

        Equal(QueueSubmissionStatus.Accepted, first.Status);
        Equal(QueueSubmissionStatus.RequestIdConflict, conflict.Status);
        Null(conflict.ResultTask);
        release.SetResult();
        await first.ResultTask!;
        Equal(RuntimeStationState.ResultAvailable.ToString(), station.Snapshot().State);
        Equal(AcknowledgeStatus.Acknowledged, station.Acknowledge("conflict-id"));
    }

    private static async Task TestUnacknowledgedResultBlocksAsync()
    {
        var station = ReadyStation(productionCommissioned: true);
        var executionCount = 0;
        await using var queue = new IdempotentInspectionQueue(
            1,
            10,
            station,
            (request, _) =>
            {
                Interlocked.Increment(ref executionCount);
                return Task.FromResult(Result(request));
            });
        var first = queue.Submit(
            new InspectionRequest("cycle-1", "part-1", "recipe-a"),
            engineering: false);
        await first.ResultTask!;
        Equal(RuntimeStationState.ResultAvailable.ToString(), station.Snapshot().State);

        var blocked = queue.Submit(
            new InspectionRequest("cycle-2", "part-2", "recipe-a"),
            engineering: false);
        Equal(QueueSubmissionStatus.StationUnavailable, blocked.Status);
        Equal(1, executionCount);

        Equal(AcknowledgeStatus.Acknowledged, station.Acknowledge("cycle-1"));
        var accepted = queue.Submit(
            new InspectionRequest("cycle-2", "part-2", "recipe-a"),
            engineering: false);
        Equal(QueueSubmissionStatus.Accepted, accepted.Status);
        await accepted.ResultTask!;
        Equal(RuntimeStationState.ResultAvailable.ToString(), station.Snapshot().State);
        Equal(2, executionCount);
        Equal(AcknowledgeStatus.Acknowledged, station.Acknowledge("cycle-2"));
    }

    private static void TestResultMapping()
    {
        var residuals = new[]
        {
            new AlignmentResidual(
                "P1",
                new MachinePoint(10, 20),
                new MachinePoint(10.01, 19.99),
                Math.Sqrt(0.0002)),
            new AlignmentResidual(
                "P2",
                new MachinePoint(30, 40),
                new MachinePoint(29.98, 40.03),
                Math.Sqrt(0.0013))
        };
        var alignment = new AlignmentPlan(
            new XYThetaCorrection(0.1, -0.2, 0.3),
            0.3,
            new MachinePoint(0.11, -0.19),
            residuals,
            0.027,
            residuals.Max(item => item.ErrorMm));
        var source = Result(
            new InspectionRequest("map-1", "part-1", "recipe-a"),
            alignment,
            includeMeasurements: true);

        var mapped = InspectionResultMapper.ToContract(source);
        True(mapped.MeasurementValid);
        Equal(2, mapped.PointResiduals.Count);
        Equal("P1", mapped.PointResiduals[0].Id);
        Near(10, mapped.PointResiduals[0].TargetXmm);
        Near(20, mapped.PointResiduals[0].TargetYmm);
        Near(10.01, mapped.PointResiduals[0].PredictedXmm);
        Near(19.99, mapped.PointResiduals[0].PredictedYmm);
        Near(residuals[0].ErrorMm, mapped.PointResiduals[0].ErrorMm);
        Equal("P2", mapped.PointResiduals[1].Id);
        Near(residuals[1].ErrorMm, mapped.MaximumPinResidualMm!.Value);
        Equal("LiveCamera", mapped.BoardFrame!.Provenance);
        Equal("abc123", mapped.BoardFrame.Sha256!);
        Equal("fake-board", mapped.BoardAlgorithmName);
        Equal("Published", mapped.RecipeStatus);
        Near(5, mapped.StageMetrics["board.featureCount"]);
    }

    private static RuntimeStation ReadyStation(bool productionCommissioned)
    {
        var station = new RuntimeStation(productionCommissioned);
        station.CompleteStartup();
        return station;
    }

    private static InspectionResult Result(
        InspectionRequest request,
        AlignmentPlan? alignment = null,
        bool includeMeasurements = false) => new()
    {
        RequestId = request.RequestId,
        ProductId = request.ProductId,
        RecipeId = request.RecipeId,
        RecipeVersion = 1,
        Disposition = InspectionDisposition.Passed,
        MotionAllowed = alignment is not null,
        Correction = alignment?.AxisCommand ?? new XYThetaCorrection(),
        Alignment = alignment,
        Board = includeMeasurements ? MeasurementBoard() : null,
        Pins = includeMeasurements ? MeasurementPins() : null,
        BoardFrame = includeMeasurements ? Evidence("Board", "abc123") : null,
        PinFrame = includeMeasurements ? Evidence("Pins", "def456") : null,
        StageMetrics = new Dictionary<string, double> { ["board.featureCount"] = 5 },
        BoardAlgorithmName = "fake-board",
        PinAlgorithmName = "fake-pins",
        RecipeStatus = RecipeLifecycleStatuses.Published,
        ProductCode = "PCB-A",
        Failures = [],
        StateHistory = [VisionCycleState.Idle, VisionCycleState.Completed],
        StartedUtc = DateTimeOffset.UnixEpoch,
        CompletedUtc = DateTimeOffset.UnixEpoch.AddMilliseconds(10),
        DurationMs = 10
    };

    private static FrameEvidence Evidence(string role, string sha256) => new()
    {
        FrameId = $"{role.ToLowerInvariant()}-1",
        CameraRole = role,
        Provenance = AcquisitionProvenance.LiveCamera,
        CapturedUtc = DateTimeOffset.UnixEpoch,
        Width = 640,
        Height = 480,
        SourceName = "test-camera",
        ArchivedImagePath = $"memory://{role}",
        Sha256 = sha256
    };

    private static BoardMeasurement MeasurementBoard() => new(
        new Pose2D(),
        1,
        [],
        [],
        new ImageQualityMeasurement(100, 20, 5, 0),
        "test-board");

    private static PinMeasurement MeasurementPins() => new(
        new Pose2D(),
        1,
        [],
        [],
        false,
        "test-pins");

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

    private static void Equal<T>(T expected, T actual) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
        }
    }

    private static void Near(double expected, double actual, double tolerance = 1e-12)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
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
}
