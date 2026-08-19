using VisionSystem.Contracts;

namespace VisionSystem.Runtime;

public enum RuntimeStationState
{
    Starting,
    Uncommissioned,
    Ready,
    Processing,
    ResultAvailable,
    Faulted,
    Stopping,
    Stopped
}

public enum AcknowledgeStatus
{
    Acknowledged,
    NotAvailable,
    RequestIdMismatch
}

public sealed class RuntimeStation(bool productionCommissioned)
{
    private readonly object _sync = new();
    private RuntimeStationState _state = RuntimeStationState.Starting;
    private string? _activeRequestId;
    private string? _activeRecipeId;
    private string? _lastCompletedRequestId;

    public bool ProductionCommissioned { get; } = productionCommissioned;

    public void CompleteStartup()
    {
        lock (_sync)
        {
            Require(RuntimeStationState.Starting);
            _state = ProductionCommissioned ? RuntimeStationState.Ready : RuntimeStationState.Uncommissioned;
        }
    }

    public bool TryBegin(string requestId, string recipeId, bool engineering)
    {
        lock (_sync)
        {
            var allowed = _state == RuntimeStationState.Ready ||
                          (engineering && _state == RuntimeStationState.Uncommissioned);
            if (!allowed)
            {
                return false;
            }

            _state = RuntimeStationState.Processing;
            _activeRequestId = requestId;
            _activeRecipeId = recipeId;
            return true;
        }
    }

    public void Complete(string requestId, bool systemFault)
    {
        lock (_sync)
        {
            if (_state != RuntimeStationState.Processing ||
                !string.Equals(_activeRequestId, requestId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Completed request does not own the active station cycle.");
            }

            _lastCompletedRequestId = requestId;
            _activeRequestId = null;
            _activeRecipeId = null;
            _state = systemFault ? RuntimeStationState.Faulted : RuntimeStationState.ResultAvailable;
        }
    }

    public AcknowledgeStatus Acknowledge(string completedRequestId)
    {
        lock (_sync)
        {
            if (_state != RuntimeStationState.ResultAvailable)
            {
                return AcknowledgeStatus.NotAvailable;
            }

            if (!string.Equals(_lastCompletedRequestId, completedRequestId, StringComparison.Ordinal))
            {
                return AcknowledgeStatus.RequestIdMismatch;
            }

            _state = ProductionCommissioned ? RuntimeStationState.Ready : RuntimeStationState.Uncommissioned;
            return AcknowledgeStatus.Acknowledged;
        }
    }

    public bool ResetFault()
    {
        lock (_sync)
        {
            if (_state != RuntimeStationState.Faulted)
            {
                return false;
            }

            // A real commissioned station must run its hardware self-test here.
            // The offline runtime has no motion authority and can safely return to
            // Uncommissioned after clearing in-memory state.
            _activeRequestId = null;
            _activeRecipeId = null;
            _state = ProductionCommissioned ? RuntimeStationState.Ready : RuntimeStationState.Uncommissioned;
            return true;
        }
    }

    public void BeginStopping()
    {
        lock (_sync)
        {
            _state = RuntimeStationState.Stopping;
        }
    }

    public void CompleteStopping()
    {
        lock (_sync)
        {
            _state = RuntimeStationState.Stopped;
        }
    }

    public StationStatusDto Snapshot()
    {
        lock (_sync)
        {
            return new StationStatusDto
            {
                Mode = ProductionCommissioned ? "AutoCapable" : "OfflineCommissioning",
                State = _state.ToString(),
                ProductionCommissioned = ProductionCommissioned,
                MotionPermittedByStation = ProductionCommissioned && _state == RuntimeStationState.Ready,
                ActiveRequestId = _activeRequestId,
                ActiveRecipeId = _activeRecipeId,
                LastCompletedRequestId = _lastCompletedRequestId,
                TimestampUtc = DateTimeOffset.UtcNow
            };
        }
    }

    private void Require(RuntimeStationState expected)
    {
        if (_state != expected)
        {
            throw new InvalidOperationException($"Station is {_state}, expected {expected}.");
        }
    }
}
