namespace Ocb.Backend.Skills.Activation;

/// <summary>
/// Contract for persisting the observed state after a Runtime Worker
/// materialization attempt. Separates recording from orchestration so
/// the backend never retries remote calls.
/// </summary>
public interface IObservedStateStore
{
    /// <summary>
    /// Record the observed state for a tenant/bot after materialization.
    /// </summary>
    Task RecordAsync(
        string tenantId,
        string botId,
        string observedState,
        string? activeViewId,
        string? errorCode,
        CancellationToken ct);
}
