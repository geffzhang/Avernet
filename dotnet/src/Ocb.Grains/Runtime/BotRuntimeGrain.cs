using Ocb.GrainContracts.Runtime;

namespace Ocb.Grains.Runtime;

/// <summary>
/// Grain that owns the authoritative desired/observed state for a bot
/// runtime. State is serializable only — no process handles or absolute
/// paths leak into grain state.
/// </summary>
public sealed class BotRuntimeGrain : Grain, IBotRuntimeGrain
{
    private readonly IPersistentState<BotRuntimeState> _state;

    public BotRuntimeGrain(
        [PersistentState("bot-runtime", "orleans-storage")]
        IPersistentState<BotRuntimeState> state)
    {
        _state = state;
    }

    public async Task ReportObservedAsync(ObservedRuntimeState observed)
    {
        _state.State.Observed = observed;
        _state.State.ReassignPending = false;
        await _state.WriteStateAsync();
    }

    public async Task SetDesiredAsync(DesiredRuntimeState desired)
    {
        _state.State.Desired = desired;
        await _state.WriteStateAsync();
    }

    public Task<BotRuntimeSnapshot> GetSnapshotAsync()
    {
        return Task.FromResult(new BotRuntimeSnapshot(
            _state.State.Desired,
            _state.State.Observed));
    }

    /// <summary>
    /// Called when a worker's lease is lost (worker failure detected).
    /// Marks the bot for reassignment and clears the desired worker.
    /// </summary>
    public async Task OnWorkerLeaseLostAsync(string workerId, string leaseToken)
    {
        if (_state.State.Desired?.WorkerId == workerId)
        {
            _state.State.Desired = _state.State.Desired with
            {
                WorkerId = null,
                Status = "ReassignPending",
            };
        }

        _state.State.ReassignPending = true;
        _state.State.LastLostWorkerId = workerId;
        await _state.WriteStateAsync();
    }
}
