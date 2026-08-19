using VisionSystem.Domain;

namespace VisionSystem.Application;

public sealed class VisionCycleStateMachine
{
    private static readonly IReadOnlyDictionary<VisionCycleState, IReadOnlySet<VisionCycleState>> Allowed =
        new Dictionary<VisionCycleState, IReadOnlySet<VisionCycleState>>
        {
            [VisionCycleState.Idle] = Set(VisionCycleState.ValidatingRequest),
            [VisionCycleState.ValidatingRequest] = Set(VisionCycleState.LoadingRecipe, VisionCycleState.PersistingResult),
            [VisionCycleState.LoadingRecipe] = Set(VisionCycleState.CheckingInterlocks, VisionCycleState.PersistingResult),
            [VisionCycleState.CheckingInterlocks] = Set(VisionCycleState.AcquiringBoardImage, VisionCycleState.PersistingResult),
            [VisionCycleState.AcquiringBoardImage] = Set(VisionCycleState.CheckingBoardImage, VisionCycleState.PersistingResult),
            [VisionCycleState.CheckingBoardImage] = Set(VisionCycleState.LocatingBoard, VisionCycleState.PersistingResult),
            [VisionCycleState.LocatingBoard] = Set(
                VisionCycleState.AcquiringPinImage,
                VisionCycleState.InspectingPins,
                VisionCycleState.PersistingResult),
            [VisionCycleState.AcquiringPinImage] = Set(VisionCycleState.CheckingPinImage, VisionCycleState.PersistingResult),
            [VisionCycleState.CheckingPinImage] = Set(VisionCycleState.InspectingPins, VisionCycleState.PersistingResult),
            [VisionCycleState.InspectingPins] = Set(VisionCycleState.TransformingCoordinates, VisionCycleState.PersistingResult),
            [VisionCycleState.TransformingCoordinates] = Set(VisionCycleState.CalculatingCorrection, VisionCycleState.PersistingResult),
            [VisionCycleState.CalculatingCorrection] = Set(VisionCycleState.PersistingResult),
            [VisionCycleState.PersistingResult] = Set(
                VisionCycleState.Completed,
                VisionCycleState.Inhibited,
                VisionCycleState.Rejected,
                VisionCycleState.Faulted),
            [VisionCycleState.Completed] = Set(),
            [VisionCycleState.Inhibited] = Set(),
            [VisionCycleState.Rejected] = Set(),
            [VisionCycleState.Faulted] = Set()
        };

    private readonly List<VisionCycleState> _history = [VisionCycleState.Idle];

    public VisionCycleState Current { get; private set; } = VisionCycleState.Idle;
    public IReadOnlyList<VisionCycleState> History => _history;
    public bool IsTerminal => Current is
        VisionCycleState.Completed or VisionCycleState.Inhibited or
        VisionCycleState.Rejected or VisionCycleState.Faulted;

    public bool CanTransitionTo(VisionCycleState next) => Allowed[Current].Contains(next);

    public void TransitionTo(VisionCycleState next)
    {
        if (!CanTransitionTo(next))
        {
            throw new InvalidOperationException($"Illegal vision-cycle transition: {Current} -> {next}.");
        }

        Current = next;
        _history.Add(next);
    }

    private static IReadOnlySet<VisionCycleState> Set(params VisionCycleState[] states) =>
        new HashSet<VisionCycleState>(states);
}
