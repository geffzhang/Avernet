using System.Collections.Concurrent;
using Ocb.Contracts.Fusion;

namespace Ocb.Fusion.Application;

/// <summary>
/// Worker profile service — manages worker lifecycle (CRUD).
/// In-memory stub for route parity testing.
/// </summary>
public sealed class WorkerProfileService
{
    private readonly ConcurrentDictionary<string, WorkerProfileDto> _workers = new();

    public Task<WorkerProfileDto> CreateWorkerAsync(CreateWorkerRequest request, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var worker = new WorkerProfileDto(
            WorkerId: request.WorkerId,
            Name: request.Name,
            Type: request.Type,
            Status: "active",
            Config: request.Config,
            CreatedAt: now,
            UpdatedAt: null
        );

        _workers[request.WorkerId] = worker;
        return Task.FromResult(worker);
    }

    public Task<WorkerListResponse> ListWorkersAsync(CancellationToken ct)
    {
        var workers = _workers.Values.ToList();
        return Task.FromResult(new WorkerListResponse(workers, workers.Count));
    }

    public Task<WorkerProfileDto> GetWorkerAsync(string workerId, CancellationToken ct)
    {
        if (!_workers.TryGetValue(workerId, out var worker))
            throw new KeyNotFoundException($"Worker '{workerId}' not found.");

        return Task.FromResult(worker);
    }

    public Task<WorkerProfileDto> UpdateWorkerAsync(string workerId, UpdateWorkerRequest request, CancellationToken ct)
    {
        if (!_workers.TryGetValue(workerId, out var existing))
            throw new KeyNotFoundException($"Worker '{workerId}' not found.");

        var updated = existing with
        {
            Name = request.Name ?? existing.Name,
            Status = request.Status ?? existing.Status,
            Config = request.Config ?? existing.Config,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _workers[workerId] = updated;
        return Task.FromResult(updated);
    }
}
