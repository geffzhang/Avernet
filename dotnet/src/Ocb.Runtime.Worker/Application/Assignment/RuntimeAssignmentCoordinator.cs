using Ocb.GrainContracts.Runtime;

namespace Ocb.Runtime.Worker.Application.Assignment;

/// <summary>
/// Coordinates bot-worker assignment and lease lifecycle.
///
/// Maintains in-memory lease state per bot. In production this would
/// be backed by a Grain or distributed lease store; the current
/// implementation is a single-node coordinator suitable for singlebox
/// and test profiles.
/// </summary>
public sealed class RuntimeAssignmentCoordinator : IRuntimeAssignmentCoordinator
{
    private readonly Dictionary<string, WorkerAssignmentLease> _leases = new();
    private readonly object _lock = new();

    /// <summary>
    /// Attempts to assign a worker to a bot by issuing a new lease.
    /// If the bot already has a valid (non-expired) lease, the assignment
    /// is rejected with "LEASE_CONFLICT".
    /// </summary>
    public Task<AssignmentResult> AssignWorkerAsync(
        AssignWorkerRequest request,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        lock (_lock)
        {
            if (_leases.TryGetValue(request.BotId, out var existing) && existing.ExpiresAt > now)
            {
                return Task.FromResult(new AssignmentResult(
                    Accepted: false, Lease: null,
                    Reason: "LEASE_CONFLICT"));
            }

            var lease = new WorkerAssignmentLease(
                LeaseToken: Guid.NewGuid().ToString("N"),
                BotId: request.BotId,
                WorkerId: request.WorkerId,
                IssuedAt: now,
                ExpiresAt: now.AddMinutes(request.LeaseDurationMinutes));

            _leases[request.BotId] = lease;

            return Task.FromResult(new AssignmentResult(
                Accepted: true, Lease: lease, Reason: null));
        }
    }

    /// <summary>
    /// Renews an existing lease. Rejects if the lease is expired
    /// or if the token does not match the current lease.
    /// </summary>
    public Task<LeaseRenewResult> RenewLeaseAsync(
        RenewLeaseRequest request,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // Client's declared expiry must be in the future
        if (request.LeaseExpiresAtUtc <= now)
        {
            return Task.FromResult(new LeaseRenewResult(
                Accepted: false, Reason: "LEASE_EXPIRED", NewExpiresAt: null));
        }

        lock (_lock)
        {
            if (!_leases.TryGetValue(request.BotId, out var current))
            {
                return Task.FromResult(new LeaseRenewResult(
                    Accepted: false, Reason: "LEASE_NOT_FOUND", NewExpiresAt: null));
            }

            if (current.LeaseToken != request.LeaseToken)
            {
                return Task.FromResult(new LeaseRenewResult(
                    Accepted: false, Reason: "LEASE_TOKEN_MISMATCH", NewExpiresAt: null));
            }

            var renewed = current with
            {
                ExpiresAt = now.AddMinutes(5),
            };

            _leases[request.BotId] = renewed;

            return Task.FromResult(new LeaseRenewResult(
                Accepted: true, Reason: null, NewExpiresAt: renewed.ExpiresAt));
        }
    }

    /// <summary>
    /// Releases a lease, freeing the bot for reassignment.
    /// Only the current lease holder can release.
    /// </summary>
    public Task<LeaseReleaseResult> ReleaseLeaseAsync(
        ReleaseLeaseRequest request,
        CancellationToken ct)
    {
        lock (_lock)
        {
            if (!_leases.TryGetValue(request.BotId, out var current))
            {
                return Task.FromResult(new LeaseReleaseResult(
                    Released: false, Reason: "LEASE_NOT_FOUND"));
            }

            if (current.LeaseToken != request.LeaseToken)
            {
                return Task.FromResult(new LeaseReleaseResult(
                    Released: false, Reason: "LEASE_TOKEN_MISMATCH"));
            }

            _leases.Remove(request.BotId);

            return Task.FromResult(new LeaseReleaseResult(
                Released: true, Reason: null));
        }
    }

    /// <summary>
    /// Requests internal reassignment of a bot.
    /// Frees the current lease and signals the new worker.
    /// </summary>
    public Task<ReassignResult> RequestInternalReassignAsync(
        RuntimeReassignRequest request,
        CancellationToken ct)
    {
        lock (_lock)
        {
            _leases.Remove(request.BotId);

            return Task.FromResult(new ReassignResult(
                Accepted: true,
                LeaseToken: Guid.NewGuid().ToString("N"),
                Message: null));
        }
    }
}
