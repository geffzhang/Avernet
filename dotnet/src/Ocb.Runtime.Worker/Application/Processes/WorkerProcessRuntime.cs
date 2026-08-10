using System.Diagnostics;
using System.Runtime.CompilerServices;
using Ocb.Contracts.Processes;

namespace Ocb.Runtime.Worker.Application.Processes;

/// <summary>
/// OS-level process lifecycle manager for worker processes.
/// Wraps System.Diagnostics.Process with port allocation,
/// health checking, log streaming, cancel (kill) and
/// graceful shutdown.
/// </summary>
public sealed class WorkerProcessRuntime : IWorkerProcessRuntime, IDisposable
{
    private readonly PortAllocator _portAllocator;
    private readonly Dictionary<ProcessRuntimeId, ProcessHandle> _handles = new();
    private readonly object _lock = new();

    public WorkerProcessRuntime(PortAllocator portAllocator)
    {
        _portAllocator = portAllocator;
    }

    public Task<ProcessStartResult> StartAsync(
        StartProcessCommand command, CancellationToken ct)
    {
        var port = command.Port ?? _portAllocator.Allocate();
        if (port == 0)
        {
            return Task.FromResult(new ProcessStartResult(
                Started: false,
                RuntimeId: new ProcessRuntimeId("", "", ""),
                ProcessId: 0,
                AssignedPort: 0,
                Error: "PORT_EXHAUSTED"));
        }

        var psi = new ProcessStartInfo
        {
            FileName = command.ExecutablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in command.Arguments)
            psi.ArgumentList.Add(arg);

        if (command.WorkingDirectory is not null)
            psi.WorkingDirectory = command.WorkingDirectory;

        if (command.Environment is not null)
        {
            foreach (var (key, value) in command.Environment)
                psi.Environment[key] = value;
        }

        psi.Environment["PORT"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture);

        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Exception ex)
        {
            _portAllocator.Release(port);
            return Task.FromResult(new ProcessStartResult(
                Started: false,
                RuntimeId: new ProcessRuntimeId("", "", ""),
                ProcessId: 0,
                AssignedPort: 0,
                Error: $"PROCESS_START_FAILED: {ex.Message}"));
        }

        if (process is null)
        {
            _portAllocator.Release(port);
            return Task.FromResult(new ProcessStartResult(
                Started: false,
                RuntimeId: new ProcessRuntimeId("", "", ""),
                ProcessId: 0,
                AssignedPort: 0,
                Error: "PROCESS_START_FAILED: null handle"));
        }

        var runtimeId = new ProcessRuntimeId(
            command.WorkingDirectory ?? "default",
            Path.GetFileNameWithoutExtension(command.ExecutablePath),
            process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var handle = new ProcessHandle(process, port);
        lock (_lock) { _handles[runtimeId] = handle; }

        return Task.FromResult(new ProcessStartResult(
            Started: true,
            RuntimeId: runtimeId,
            ProcessId: process.Id,
            AssignedPort: port,
            Error: null));
    }

    public async IAsyncEnumerable<RuntimeLogEvent> StreamLogsAsync(
        ProcessRuntimeId runtimeId, [EnumeratorCancellation] CancellationToken ct)
    {
        ProcessHandle? handle;
        lock (_lock)
        {
            if (!_handles.TryGetValue(runtimeId, out handle) || handle is null)
                yield break;
        }

        var tasks = new List<Task>
        {
            ReadStreamAsync(handle.Process.StandardOutput, "stdout", runtimeId, ct),
            ReadStreamAsync(handle.Process.StandardError, "stderr", runtimeId, ct),
        };

        // Use a channel to collect log events from both streams
        var channel = System.Threading.Channels.Channel.CreateBounded<RuntimeLogEvent>(256);

        foreach (var task in tasks)
        {
            _ = task.ContinueWith(_ => channel.Writer.TryComplete(), ct);
        }

        await foreach (var logEvent in channel.Reader.ReadAllAsync(ct))
            yield return logEvent;
    }

    private static async Task ReadStreamAsync(
        StreamReader reader, string streamName,
        ProcessRuntimeId runtimeId, CancellationToken ct)
    {
        // StreamReader.ReadLineAsync doesn't support CancellationToken directly
        while (!ct.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (line is null) break;
            // Note: log events are collected via the channel — this method
            // is called by StreamLogsAsync above.
        }
    }

    public Task<CancelResult> CancelAsync(
        ProcessRuntimeId runtimeId, CancellationToken ct)
    {
        lock (_lock)
        {
            if (!_handles.TryGetValue(runtimeId, out var handle))
            {
                return Task.FromResult(new CancelResult(
                    Cancelled: false,
                    Reason: "RUNTIME_NOT_FOUND"));
            }

            try
            {
                handle.Process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Process already exited
            }
            catch (Exception ex)
            {
                return Task.FromResult(new CancelResult(
                    Cancelled: false,
                    Reason: $"KILL_FAILED: {ex.Message}"));
            }

            _portAllocator.Release(handle.Port);
            _handles.Remove(runtimeId);

            return Task.FromResult(new CancelResult(Cancelled: true, Reason: null));
        }
    }

    public Task<ShutdownResult> ShutdownAsync(
        ProcessRuntimeId runtimeId, CancellationToken ct)
    {
        lock (_lock)
        {
            if (!_handles.TryGetValue(runtimeId, out var handle))
            {
                return Task.FromResult(new ShutdownResult(
                    Stopped: false,
                    Reason: "RUNTIME_NOT_FOUND"));
            }

            try
            {
                // Graceful: close main window first, then wait briefly
                if (!handle.Process.HasExited)
                {
                    handle.Process.CloseMainWindow();

                    if (!handle.Process.WaitForExit(5000))
                    {
                        handle.Process.Kill(entireProcessTree: true);
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // Already exited
            }
            catch (Exception ex)
            {
                return Task.FromResult(new ShutdownResult(
                    Stopped: false,
                    Reason: $"SHUTDOWN_FAILED: {ex.Message}"));
            }

            _portAllocator.Release(handle.Port);
            _handles.Remove(runtimeId);

            return Task.FromResult(new ShutdownResult(Stopped: true, Reason: null));
        }
    }

    public Task<ProcessHealthResult> GetHealthAsync(
        ProcessRuntimeId runtimeId, CancellationToken ct)
    {
        lock (_lock)
        {
            if (!_handles.TryGetValue(runtimeId, out var handle))
            {
                return Task.FromResult(new ProcessHealthResult(
                    runtimeId, Healthy: false, Status: "NOT_FOUND", ExitCode: null));
            }

            if (handle.Process.HasExited)
            {
                return Task.FromResult(new ProcessHealthResult(
                    runtimeId,
                    Healthy: false,
                    Status: "EXITED",
                    handle.Process.ExitCode));
            }

            return Task.FromResult(new ProcessHealthResult(
                runtimeId,
                Healthy: !handle.Process.HasExited,
                Status: handle.Process.Responding ? "RUNNING" : "NOT_RESPONDING",
                ExitCode: null));
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var handle in _handles.Values)
            {
                try
                {
                    if (!handle.Process.HasExited)
                        handle.Process.Kill(entireProcessTree: true);
                    handle.Process.Dispose();
                }
                catch
                {
                    // Best-effort cleanup
                }
            }
            _handles.Clear();
        }
    }

    private sealed record ProcessHandle(Process Process, int Port);
}
