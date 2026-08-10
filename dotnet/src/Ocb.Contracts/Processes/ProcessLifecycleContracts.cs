namespace Ocb.Contracts.Processes;

/// <summary>
/// Unique identifier for a process runtime instance.
/// </summary>
public sealed record ProcessRuntimeId(
    string TenantId,
    string BotId,
    string RunId);

/// <summary>
/// Command to start a new worker process.
/// </summary>
public sealed record StartProcessCommand(
    string ExecutablePath,
    string[] Arguments,
    string? WorkingDirectory,
    Dictionary<string, string>? Environment,
    int? Port);

/// <summary>
/// Result of a process start attempt.
/// </summary>
public sealed record ProcessStartResult(
    bool Started,
    ProcessRuntimeId RuntimeId,
    int ProcessId,
    int AssignedPort,
    string? Error);

/// <summary>
/// Result of a cancel (force kill) operation.
/// </summary>
public sealed record CancelResult(
    bool Cancelled,
    string? Reason);

/// <summary>
/// Result of a shutdown (graceful stop) operation.
/// </summary>
public sealed record ShutdownResult(
    bool Stopped,
    string? Reason);

/// <summary>
/// A log event emitted by a running process.
/// </summary>
public sealed record RuntimeLogEvent(
    ProcessRuntimeId RuntimeId,
    DateTimeOffset Timestamp,
    string Stream,  // "stdout" or "stderr"
    string Text);

/// <summary>
/// Health status of a running process.
/// </summary>
public sealed record ProcessHealthResult(
    ProcessRuntimeId RuntimeId,
    bool Healthy,
    string? Status,
    int? ExitCode);
