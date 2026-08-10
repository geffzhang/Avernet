using Ocb.Contracts.Processes;

namespace Ocb.Runtime.Worker.Application.Processes;

/// <summary>
/// Abstraction over OS-level process lifecycle for worker processes.
/// Enables testability via fake process handles.
/// </summary>
public interface IWorkerProcessRuntime
{
    /// <summary>
    /// Start a new process with the given command parameters.
    /// </summary>
    Task<ProcessStartResult> StartAsync(
        StartProcessCommand command, CancellationToken ct);

    /// <summary>
    /// Stream log events (stdout + stderr) from the running process.
    /// </summary>
    IAsyncEnumerable<RuntimeLogEvent> StreamLogsAsync(
        ProcessRuntimeId runtimeId, CancellationToken ct);

    /// <summary>
    /// Forcefully cancel (kill) a running process.
    /// </summary>
    Task<CancelResult> CancelAsync(
        ProcessRuntimeId runtimeId, CancellationToken ct);

    /// <summary>
    /// Gracefully shutdown a running process.
    /// </summary>
    Task<ShutdownResult> ShutdownAsync(
        ProcessRuntimeId runtimeId, CancellationToken ct);

    /// <summary>
    /// Check the health status of a running process.
    /// </summary>
    Task<ProcessHealthResult> GetHealthAsync(
        ProcessRuntimeId runtimeId, CancellationToken ct);
}
