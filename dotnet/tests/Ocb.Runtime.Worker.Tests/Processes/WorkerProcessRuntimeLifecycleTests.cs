using System.Diagnostics.CodeAnalysis;
using Ocb.Contracts.Processes;
using Ocb.Runtime.Worker.Application.Processes;

namespace Ocb.Runtime.Worker.Tests.Processes;

/// <summary>
/// Process lifecycle tests: port allocation, cancel, shutdown,
/// health check, and DTO semantics.
/// Real process tests are skipped in CI (no Python runtime
/// available); error-path tests exercise the full code path.
/// </summary>
[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class WorkerProcessRuntimeLifecycleTests : IDisposable
{
    private readonly PortAllocator _portAllocator;
    private readonly WorkerProcessRuntime _runtime;

    public WorkerProcessRuntimeLifecycleTests()
    {
        _portAllocator = new PortAllocator(rangeStart: 40000, rangeEnd: 40099);
        _runtime = new WorkerProcessRuntime(_portAllocator);
    }

    #region PortAllocator

    [Fact]
    public void PortAllocator_AllocatesUniquePorts()
    {
        var port1 = _portAllocator.Allocate();
        var port2 = _portAllocator.Allocate();

        Assert.True(port1 > 0);
        Assert.True(port2 > 0);
        Assert.NotEqual(port1, port2);
        Assert.Equal(2, _portAllocator.AllocatedCount);
    }

    [Fact]
    public void PortAllocator_Release_MakesPortReusable()
    {
        var port = _portAllocator.Allocate();
        Assert.Equal(1, _portAllocator.AllocatedCount);

        _portAllocator.Release(port);
        Assert.Equal(0, _portAllocator.AllocatedCount);
    }

    [Fact]
    public void PortAllocator_ReleaseUnknownPort_DoesNotThrow()
    {
        // No exception
        _portAllocator.Release(99999);
        Assert.Equal(0, _portAllocator.AllocatedCount);
    }

    #endregion

    #region DTO Semantics

    [Fact]
    public void ProcessRuntimeId_CanBeUsedAsDictionaryKey()
    {
        var id1 = new ProcessRuntimeId("tenant-1", "bot-1", "run-1");
        var id2 = new ProcessRuntimeId("tenant-1", "bot-1", "run-1");
        var dict = new Dictionary<ProcessRuntimeId, string> { [id1] = "value" };

        Assert.True(dict.ContainsKey(id2));
    }

    [Fact]
    public void ProcessStartResult_Failure_IncludesError()
    {
        var result = new ProcessStartResult(
            Started: false, new ProcessRuntimeId("", "", ""),
            ProcessId: 0, AssignedPort: 0, Error: "PORT_EXHAUSTED");

        Assert.False(result.Started);
        Assert.Equal("PORT_EXHAUSTED", result.Error);
    }

    [Fact]
    public void ProcessStartResult_Success_NoError()
    {
        var result = new ProcessStartResult(
            Started: true,
            new ProcessRuntimeId("t", "bot", "123"),
            ProcessId: 4567, AssignedPort: 8080, Error: null);

        Assert.True(result.Started);
        Assert.Null(result.Error);
        Assert.Equal(4567, result.ProcessId);
        Assert.Equal(8080, result.AssignedPort);
    }

    [Fact]
    public void CancelResult_Cancelled_NoReason()
    {
        var result = new CancelResult(Cancelled: true, Reason: null);
        Assert.True(result.Cancelled);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void CancelResult_NotCancelled_HasReason()
    {
        var result = new CancelResult(Cancelled: false, Reason: "RUNTIME_NOT_FOUND");
        Assert.False(result.Cancelled);
        Assert.Equal("RUNTIME_NOT_FOUND", result.Reason);
    }

    [Fact]
    public void ShutdownResult_Stopped_NoReason()
    {
        var result = new ShutdownResult(Stopped: true, Reason: null);
        Assert.True(result.Stopped);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void ProcessHealthResult_NotFound()
    {
        var id = new ProcessRuntimeId("t", "bot", "run");
        var result = new ProcessHealthResult(id, Healthy: false, Status: "NOT_FOUND", ExitCode: null);
        Assert.False(result.Healthy);
        Assert.Equal("NOT_FOUND", result.Status);
    }

    #endregion

    #region Error-path — unknown runtime

    [Fact]
    public async Task CancelAsync_UnknownRuntime_ShouldReturnNotFound()
    {
        var unknownId = new ProcessRuntimeId("t", "bot", "no-such-run");

        var result = await _runtime.CancelAsync(unknownId, default);

        Assert.False(result.Cancelled);
        Assert.Equal("RUNTIME_NOT_FOUND", result.Reason);
    }

    [Fact]
    public async Task ShutdownAsync_UnknownRuntime_ShouldReturnNotFound()
    {
        var unknownId = new ProcessRuntimeId("t", "bot", "no-such-run");

        var result = await _runtime.ShutdownAsync(unknownId, default);

        Assert.False(result.Stopped);
        Assert.Equal("RUNTIME_NOT_FOUND", result.Reason);
    }

    [Fact]
    public async Task GetHealthAsync_UnknownRuntime_ShouldReturnNotFound()
    {
        var unknownId = new ProcessRuntimeId("t", "bot", "no-such-run");

        var result = await _runtime.GetHealthAsync(unknownId, default);

        Assert.False(result.Healthy);
        Assert.Equal("NOT_FOUND", result.Status);
    }

    #endregion

    #region StreamLogs — empty for unknown

    [Fact]
    public async Task StreamLogsAsync_UnknownRuntime_ShouldYieldEmpty()
    {
        var unknownId = new ProcessRuntimeId("t", "bot", "no-such-run");
        var logs = new List<RuntimeLogEvent>();

        await foreach (var log in _runtime.StreamLogsAsync(unknownId, default))
            logs.Add(log);

        Assert.Empty(logs);
    }

    #endregion

    public void Dispose()
    {
        (_runtime as WorkerProcessRuntime)?.Dispose();
    }
}
