using System.Diagnostics;
using VisionSystem.Domain;

namespace VisionSystem.Application;

/// <summary>
/// Application-level production cycle. It contains no HALCON or WPF types and is
/// the single place that authorizes (or forbids) machine motion.
/// </summary>
public sealed class InspectionOrchestrator(
    IRecipeRepository recipes,
    RecipeValidator recipeValidator,
    IEquipmentReadiness equipment,
    ICameraSource boardCamera,
    ICameraSource? pinCamera,
    IBoardVisionAlgorithm boardAlgorithm,
    IPinVisionAlgorithm pinAlgorithm,
    IFrameArchive frameArchive,
    IInspectionRepository results,
    ICycleEventSink events,
    ISystemClock clock,
    RigidAlignmentSolver? alignmentSolver = null)
{
    private readonly RigidAlignmentSolver _alignmentSolver = alignmentSolver ?? new RigidAlignmentSolver();

    public async Task<InspectionResult> ExecuteAsync(
        InspectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var started = clock.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var stateMachine = new VisionCycleStateMachine();
        var failures = new List<VisionFailure>();
        BoardMeasurement? board = null;
        PinMeasurement? pins = null;
        AlignmentPlan? alignment = null;
        VisionRecipe? recipe = null;
        FrameEvidence? boardFrameEvidence = null;
        FrameEvidence? pinFrameEvidence = null;
        var stageMetrics = new Dictionary<string, double>(StringComparer.Ordinal);
        var correction = new XYThetaCorrection();
        var engineeringOnly = false;

        async Task MoveAsync(VisionCycleState state, CancellationToken token = default)
        {
            stateMachine.TransitionTo(state);
            await events.StateChangedAsync(request.RequestId, state, clock.UtcNow, token)
                .ConfigureAwait(false);
        }

        try
        {
            await MoveAsync(VisionCycleState.ValidatingRequest, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(request.RequestId) ||
                string.IsNullOrWhiteSpace(request.ProductId) ||
                string.IsNullOrWhiteSpace(request.RecipeId))
            {
                failures.Add(new VisionFailure(
                    VisionFailureCode.RequestInvalid,
                    "RequestId, ProductId and RecipeId are required.",
                    false,
                    "Request",
                    VisionFailureImpact.FaultSystem));
                return await CompleteFromFailuresAsync().ConfigureAwait(false);
            }

            await MoveAsync(VisionCycleState.LoadingRecipe, cancellationToken).ConfigureAwait(false);
            recipe = await recipes.GetAsync(request.RecipeId, cancellationToken).ConfigureAwait(false);
            var validation = recipeValidator.Validate(recipe);
            failures.AddRange(validation.Failures);
            if (!validation.IsValid)
            {
                return await CompleteFromFailuresAsync().ConfigureAwait(false);
            }

            using var timeoutSource = new CancellationTokenSource(recipe.ProductionPolicy.CycleTimeoutMs);
            using var cycleSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
            var cycleToken = cycleSource.Token;

            await MoveAsync(VisionCycleState.CheckingInterlocks, cycleToken).ConfigureAwait(false);
            var readiness = await equipment.CheckAsync(cycleToken).ConfigureAwait(false);
            failures.AddRange(readiness.Failures);
            if (recipe.ProductionPolicy.RequireMotionStationary && !readiness.MotionStationary)
            {
                AddFailureOnce(new VisionFailure(
                    VisionFailureCode.MotionNotStationary,
                    "Motion axes are not stationary.",
                    false,
                    "Interlock",
                    VisionFailureImpact.FaultSystem));
            }

            if (recipe.ProductionPolicy.RequireSafetyInterlock && !readiness.SafetyInterlockClosed)
            {
                AddFailureOnce(new VisionFailure(
                    VisionFailureCode.SafetyInterlockOpen,
                    "Safety interlock is open.",
                    false,
                    "Interlock",
                    VisionFailureImpact.FaultSystem));
            }

            if (failures.Any(failure => failure.EffectiveImpact == VisionFailureImpact.FaultSystem))
            {
                return await CompleteFromFailuresAsync().ConfigureAwait(false);
            }

            await boardCamera.OpenAsync(recipe.BoardCamera, cycleToken).ConfigureAwait(false);
            await MoveAsync(VisionCycleState.AcquiringBoardImage, cycleToken).ConfigureAwait(false);
            using (var boardFrame = await boardCamera
                       .AcquireAsync(request.BoardImageOverride, cycleToken)
                       .ConfigureAwait(false))
            {
                boardFrameEvidence = await CaptureFrameEvidenceAsync(boardFrame, cycleToken)
                    .ConfigureAwait(false);
                if (boardFrameEvidence is null)
                {
                    return await CompleteFromFailuresAsync().ConfigureAwait(false);
                }

                GateFrameProvenance(boardFrame, "Board.Acquisition");
                await MoveAsync(VisionCycleState.CheckingBoardImage, cycleToken).ConfigureAwait(false);
                await MoveAsync(VisionCycleState.LocatingBoard, cycleToken).ConfigureAwait(false);
                var boardStage = await boardAlgorithm
                    .LocateAsync(boardFrame, recipe, cycleToken)
                    .ConfigureAwait(false);
                failures.AddRange(boardStage.Failures);
                AddStageMetrics("board", boardStage.Metrics);
                board = boardStage.Value;
            }

            if (board is null)
            {
                if (!failures.Any(failure =>
                        failure.Stage.StartsWith("Board", StringComparison.OrdinalIgnoreCase)))
                {
                    AddFailureOnce(new VisionFailure(
                        VisionFailureCode.BoardNotFound,
                        "The board algorithm did not return a valid measurement or a diagnostic failure.",
                        true,
                        "Board",
                        VisionFailureImpact.RejectCycle));
                }

                return await CompleteFromFailuresAsync().ConfigureAwait(false);
            }

            if (recipe.Board.LocatorMode == BoardLocatorMode.ShapeModelThenBlob &&
                board.Score < recipe.Board.ShapeModel.MinimumScore)
            {
                failures.Add(new VisionFailure(
                    VisionFailureCode.BoardScoreTooLow,
                    $"Board score {board.Score:F3} is below the configured minimum " +
                    $"{recipe.Board.ShapeModel.MinimumScore:F3}.",
                    true,
                    "Board",
                    VisionFailureImpact.RejectCycle));
            }

            if (recipe.Pins.Enabled && pinCamera is not null)
            {
                await pinCamera.OpenAsync(recipe.PinCamera, cycleToken).ConfigureAwait(false);
                await MoveAsync(VisionCycleState.AcquiringPinImage, cycleToken).ConfigureAwait(false);
                using var pinFrame = await pinCamera
                    .AcquireAsync(request.PinImageOverride, cycleToken)
                    .ConfigureAwait(false);
                pinFrameEvidence = await CaptureFrameEvidenceAsync(pinFrame, cycleToken)
                    .ConfigureAwait(false);
                if (pinFrameEvidence is null)
                {
                    return await CompleteFromFailuresAsync().ConfigureAwait(false);
                }

                GateFrameProvenance(pinFrame, "Pins.Acquisition");
                await MoveAsync(VisionCycleState.CheckingPinImage, cycleToken).ConfigureAwait(false);
                await MoveAsync(VisionCycleState.InspectingPins, cycleToken).ConfigureAwait(false);
                var pinStage = await pinAlgorithm
                    .InspectAsync(pinFrame, recipe, cycleToken)
                    .ConfigureAwait(false);
                failures.AddRange(pinStage.Failures);
                AddStageMetrics("pins", pinStage.Metrics);
                pins = pinStage.Value;
            }
            else
            {
                await MoveAsync(VisionCycleState.InspectingPins, cycleToken).ConfigureAwait(false);
                var pinStage = await pinAlgorithm
                    .InspectAsync(null, recipe, cycleToken)
                    .ConfigureAwait(false);
                failures.AddRange(pinStage.Failures);
                AddStageMetrics("pins", pinStage.Metrics);
                pins = pinStage.Value;
            }

            if (pins is null)
            {
                if (!failures.Any(failure =>
                        failure.Stage.StartsWith("Pins", StringComparison.OrdinalIgnoreCase)))
                {
                    AddFailureOnce(new VisionFailure(
                        VisionFailureCode.PinRowNotFound,
                        "The pin algorithm did not return a valid measurement or a diagnostic failure.",
                        true,
                        "Pins",
                        VisionFailureImpact.RejectCycle));
                }

                return await CompleteFromFailuresAsync().ConfigureAwait(false);
            }

            engineeringOnly = pins.IsSimulated ||
                !RecipeLifecycleStatuses.AllowsAutomaticMotion(recipe.Status);
            if (pins.IsSimulated)
            {
                AddFailureOnce(new VisionFailure(
                    VisionFailureCode.PinCameraNotCommissioned,
                    "Pin measurement is simulated; automatic motion is forbidden.",
                    false,
                    "Pins",
                    VisionFailureImpact.InhibitMotion));
            }

            if (!RecipeLifecycleStatuses.AllowsAutomaticMotion(recipe.Status))
            {
                AddFailureOnce(new VisionFailure(
                    VisionFailureCode.RecipeNotReleasedForMotion,
                    $"Recipe status '{recipe.Status}' is not released for automatic motion.",
                    false,
                    "Recipe",
                    VisionFailureImpact.InhibitMotion));
            }

            await MoveAsync(VisionCycleState.TransformingCoordinates, cycleToken).ConfigureAwait(false);
            await MoveAsync(VisionCycleState.CalculatingCorrection, cycleToken).ConfigureAwait(false);
            var alignmentStage = _alignmentSolver.Solve(
                pins.PinsInMachine,
                board.HolesInMachine,
                recipe.MachineKinematics);
            failures.AddRange(alignmentStage.Failures);
            alignment = alignmentStage.Value;
            if (alignment is null)
            {
                return await CompleteFromFailuresAsync().ConfigureAwait(false);
            }

            correction = alignment.AxisCommand;

            // A large command is a machine safety/range decision. It is not an
            // accuracy measurement and is deliberately evaluated separately from
            // the post-fit point residuals below.
            if (!correction.IsFinite ||
                Math.Abs(correction.Xmm) > recipe.CorrectionTolerance.MaximumSafeCommandXmm ||
                Math.Abs(correction.Ymm) > recipe.CorrectionTolerance.MaximumSafeCommandYmm ||
                Math.Abs(correction.ThetaDeg) > recipe.CorrectionTolerance.MaximumSafeCommandThetaDeg)
            {
                failures.Add(new VisionFailure(
                    VisionFailureCode.CorrectionOutOfRange,
                    $"Axis command X={correction.Xmm:F4} mm, Y={correction.Ymm:F4} mm, " +
                    $"T={correction.ThetaDeg:F4} deg exceeds the safe command envelope.",
                    true,
                    "Correction",
                    VisionFailureImpact.RejectCycle));
            }

            if (!double.IsFinite(alignment.MaximumResidualMm) ||
                alignment.MaximumResidualMm > recipe.CorrectionTolerance.MaximumPredictedResidualMm)
            {
                failures.Add(new VisionFailure(
                    VisionFailureCode.AlignmentResidualTooHigh,
                    $"Predicted point residual {alignment.MaximumResidualMm:F4} mm exceeds " +
                    $"{recipe.CorrectionTolerance.MaximumPredictedResidualMm:F4} mm.",
                    true,
                    "Alignment",
                    VisionFailureImpact.RejectCycle));
            }

            return await CompleteFromFailuresAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            failures.Add(new VisionFailure(
                VisionFailureCode.CycleTimeout,
                "Vision cycle exceeded its configured timeout.",
                true,
                "Cycle",
                VisionFailureImpact.FaultSystem));
            return await CompleteFromFailuresAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            failures.Add(new VisionFailure(
                VisionFailureCode.CycleTimeout,
                "Vision cycle was cancelled by its caller.",
                true,
                "Cycle",
                VisionFailureImpact.FaultSystem));
            return await CompleteFromFailuresAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(new VisionFailure(
                VisionFailureCode.InternalError,
                exception.Message,
                false,
                "Unhandled",
                VisionFailureImpact.FaultSystem));
            return await CompleteFromFailuresAsync().ConfigureAwait(false);
        }

        Task<InspectionResult> CompleteFromFailuresAsync()
        {
            var disposition = ResolveDisposition(engineeringOnly, failures);
            return CompleteAsync(disposition);
        }

        async Task<InspectionResult> CompleteAsync(InspectionDisposition disposition)
        {
            var terminal = disposition switch
            {
                InspectionDisposition.Passed => VisionCycleState.Completed,
                InspectionDisposition.EngineeringOnly => VisionCycleState.Inhibited,
                InspectionDisposition.Rejected => VisionCycleState.Rejected,
                _ => VisionCycleState.Faulted
            };

            if (stateMachine.Current != VisionCycleState.PersistingResult)
            {
                await MoveAsync(VisionCycleState.PersistingResult).ConfigureAwait(false);
            }

            stopwatch.Stop();

            // Motion permission is derived here, not supplied by a caller. This
            // invariant makes EngineeringOnly impossible to accidentally authorize.
            var motionAllowed =
                disposition == InspectionDisposition.Passed &&
                alignment is not null &&
                pins is { IsSimulated: false } &&
                boardFrameEvidence is { Provenance: AcquisitionProvenance.LiveCamera } &&
                pinFrameEvidence is { Provenance: AcquisitionProvenance.LiveCamera } &&
                failures.Count == 0;
            var result = new InspectionResult
            {
                RequestId = request.RequestId,
                ProductId = request.ProductId,
                RecipeId = recipe?.RecipeId ?? request.RecipeId,
                RecipeVersion = recipe?.Version ?? 0,
                Disposition = disposition,
                MotionAllowed = motionAllowed,
                Correction = correction,
                Alignment = alignment,
                Board = board,
                Pins = pins,
                BoardFrame = boardFrameEvidence,
                PinFrame = pinFrameEvidence,
                BoardImagePath = boardFrameEvidence?.ArchivedImagePath,
                PinImagePath = pinFrameEvidence?.ArchivedImagePath,
                StageMetrics = new Dictionary<string, double>(stageMetrics, StringComparer.Ordinal),
                BoardAlgorithmName = boardAlgorithm.Name,
                PinAlgorithmName = pinAlgorithm.Name,
                RecipeStatus = recipe?.Status ?? string.Empty,
                ProductCode = recipe?.ProductCode ?? string.Empty,
                Failures = failures.ToArray(),
                StateHistory = stateMachine.History.Append(terminal).ToArray(),
                StartedUtc = started,
                CompletedUtc = clock.UtcNow,
                DurationMs = stopwatch.ElapsedMilliseconds
            };

            try
            {
                var path = await results.SaveAsync(result, CancellationToken.None).ConfigureAwait(false);
                await MoveAsync(terminal).ConfigureAwait(false);
                return result with
                {
                    ResultPath = path,
                    StateHistory = stateMachine.History.ToArray()
                };
            }
            catch (Exception exception)
            {
                failures.Add(new VisionFailure(
                    VisionFailureCode.PersistenceFailed,
                    exception.Message,
                    true,
                    "Persistence",
                    VisionFailureImpact.FaultSystem));
                if (stateMachine.Current == VisionCycleState.PersistingResult)
                {
                    await MoveAsync(VisionCycleState.Faulted).ConfigureAwait(false);
                }

                return result with
                {
                    Disposition = InspectionDisposition.Faulted,
                    MotionAllowed = false,
                    Failures = failures.ToArray(),
                    StateHistory = stateMachine.History.ToArray()
                };
            }
        }

        void AddFailureOnce(VisionFailure failure)
        {
            if (failures.All(existing => existing.Code != failure.Code))
            {
                failures.Add(failure);
            }
        }

        async Task<FrameEvidence?> CaptureFrameEvidenceAsync(
            IVisionFrame frame,
            CancellationToken token)
        {
            if (recipe is not null && !recipe.ProductionPolicy.SaveAllImages)
            {
                return CreateFrameEvidence(frame);
            }

            try
            {
                return await frameArchive.ArchiveAsync(request.RequestId, frame, token)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures.Add(new VisionFailure(
                    VisionFailureCode.ImageArchiveFailed,
                    exception.Message,
                    true,
                    $"{frame.Role}.Archive",
                    VisionFailureImpact.FaultSystem));
                return null;
            }
        }

        void GateFrameProvenance(IVisionFrame frame, string stage)
        {
            if (frame.Provenance == AcquisitionProvenance.LiveCamera)
            {
                return;
            }

            engineeringOnly = true;
            AddFailureOnce(new VisionFailure(
                VisionFailureCode.AcquisitionNotLive,
                $"{frame.Role} frame provenance is {frame.Provenance}; automatic motion requires a live camera frame.",
                false,
                stage,
                VisionFailureImpact.InhibitMotion));
        }

        void AddStageMetrics(string prefix, IReadOnlyDictionary<string, double> metrics)
        {
            foreach (var item in metrics)
            {
                var key = item.Key.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase)
                    ? item.Key
                    : $"{prefix}.{item.Key}";
                stageMetrics[key] = item.Value;
            }
        }
    }

    private static FrameEvidence CreateFrameEvidence(IVisionFrame frame) => new()
    {
        FrameId = frame.FrameId,
        CameraRole = frame.Role.ToString(),
        Provenance = frame.Provenance,
        CapturedUtc = frame.CapturedUtc,
        Width = frame.Width,
        Height = frame.Height,
        SourceName = frame.SourceName
    };

    private static InspectionDisposition ResolveDisposition(
        bool engineeringOnly,
        IReadOnlyCollection<VisionFailure> failures)
    {
        if (failures.Any(failure => failure.EffectiveImpact == VisionFailureImpact.FaultSystem))
        {
            return InspectionDisposition.Faulted;
        }

        // A real product/measurement defect remains Rejected even on an offline
        // engineering station. EngineeringOnly describes an otherwise-valid
        // result that is inhibited solely by simulation or recipe lifecycle.
        if (failures.Any(failure => failure.EffectiveImpact == VisionFailureImpact.RejectCycle))
        {
            return InspectionDisposition.Rejected;
        }

        if (engineeringOnly ||
            failures.Any(failure => failure.EffectiveImpact == VisionFailureImpact.InhibitMotion))
        {
            return InspectionDisposition.EngineeringOnly;
        }

        return InspectionDisposition.Passed;
    }
}
