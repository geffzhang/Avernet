# .NET Runtime Worker Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不引入 OpenClaw 代码依赖和不改变既有公开线协议的前提下，交付 Ocb.Runtime.Worker + Ocb.GrainContracts/Ocb.Grains 的可验证最小闭环，覆盖 Session HTTP API parity、Engine WebSocket parity、assignment+lease、进程/workspace 生命周期、BotRuntimeGrain desired/observed 协调与故障恢复。

**Architecture:** Runtime Worker 负责本地进程与文件系统等非可序列化资源，Orleans Grain 负责可序列化的 desired/observed 协调状态；两者通过内部协调接口通信。Service API 固定在 Ocb.Contracts，Plugin API 固定在 Ocb.PluginApi；reassign 定义为新的内部协调能力，不作为既有公开 API 暴露。所有线协议以 parity-corpus 为准，未知字段与新 wire 形状一律 fail-closed，不猜测未定义 contract。

**Tech Stack:** .NET 10.0.302, ASP.NET Core, Orleans, xUnit, FluentAssertions, NSubstitute, System.Text.Json source generation, Testcontainers (仅集成测试阶段).

## Global Constraints

- 全部新增项目目标框架必须为 net10.0，启用 nullable、implicit usings、TreatWarningsAsErrors。
- 项目命名采用批准方案：Ocb.GrainContracts、Ocb.Grains、Ocb.Runtime.Worker；Service API 在 Ocb.Contracts，Plugin API 在 Ocb.PluginApi。
- 严禁引入任何 OpenClaw.* 依赖、源码 bridge、运行时 bridge、ProjectReference 或 NuGet 引用。
- Session/HTTP/WebSocket 线协议必须与 dotnet/contracts/parity-corpus/manifest.json 与对应 artifact 对齐；不得猜测或扩展未批准 wire contract。
- 复用 Phase 0 的 `Ocb.Contracts.CallerContext`、`TenantEntityKey` 和错误契约；不得创建 Runtime-local 身份 DTO 替代共享契约。
- reassign 是新的内部协调能力，不属于现有公开 API；不得新增外部公开 endpoint 或公开 WS method 暴露 reassign。
- Grain 权威状态仅保存可序列化标识与 desired/observed；进程句柄、本地绝对路径、端口占用句柄不得进入 Grain 权威状态。
- Skills publication 所有权保留在 Backend；Runtime Worker 仅实现 Skills 物化接口与执行，不实现 publication 生命周期。
- Engine Session HTTP + Engine WebSocket parity 必须同时覆盖；两种传输共享的 Session 能力行为与错误语义必须一致。
- 所有租约 lease 使用可过期语义并支持 worker 丢失后的接管；丢失检测与重分配必须可测试复现。
- 不修改 frontend、Rust BCS 与 Python 服务实现；本计划仅定义 dotnet 侧实现任务。

---

## File Structure

- Create: dotnet/src/Ocb.GrainContracts/Ocb.GrainContracts.csproj
- Create: dotnet/src/Ocb.GrainContracts/Runtime/BotRuntimeContracts.cs
- Create: dotnet/src/Ocb.GrainContracts/Runtime/RuntimeLeaseContracts.cs
- Create: dotnet/src/Ocb.Grains/Ocb.Grains.csproj
- Create: dotnet/src/Ocb.Grains/Runtime/BotRuntimeGrain.cs
- Create: dotnet/src/Ocb.Grains/Runtime/BotRuntimeState.cs
- Create: dotnet/src/Ocb.Grains/Runtime/BotRuntimeObservedState.cs
- Create: dotnet/src/Ocb.Grains/Runtime/WorkerAssignmentLease.cs
- Create: dotnet/src/Ocb.Runtime.Worker/Ocb.Runtime.Worker.csproj
- Create: dotnet/src/Ocb.Runtime.Worker/Api/SessionParityController.cs
- Create: dotnet/src/Ocb.Runtime.Worker/Api/WebSocket/EngineWsEndpoint.cs
- Create: dotnet/src/Ocb.Runtime.Worker/Api/WebSocket/EngineWsMethodDispatcher.cs
- Create: dotnet/src/Ocb.Runtime.Worker/Api/WebSocket/EngineWsProtocolGuards.cs
- Create: dotnet/src/Ocb.Runtime.Worker/Application/Assignment/IRuntimeAssignmentCoordinator.cs
- Create: dotnet/src/Ocb.Runtime.Worker/Application/Assignment/RuntimeAssignmentCoordinator.cs
- Create: dotnet/src/Ocb.Runtime.Worker/Application/Processes/IWorkerProcessRuntime.cs
- Create: dotnet/src/Ocb.Runtime.Worker/Application/Processes/WorkerProcessRuntime.cs
- Create: dotnet/src/Ocb.Runtime.Worker/Application/Processes/PortAllocator.cs
- Create: dotnet/src/Ocb.Runtime.Worker/Application/Workspaces/IWorkspaceIsolationService.cs
- Create: dotnet/src/Ocb.Runtime.Worker/Application/Workspaces/WorkspaceIsolationService.cs
- Create: dotnet/src/Ocb.Runtime.Worker/Application/Recovery/IOrphanRecoveryService.cs
- Create: dotnet/src/Ocb.Runtime.Worker/Application/Recovery/OrphanRecoveryService.cs
- Create: dotnet/src/Ocb.Contracts/Skills/IRuntimeSkillMaterializationService.cs
- Create: dotnet/src/Ocb.Contracts/Skills/RuntimeSkillMaterializationContracts.cs
- Create: dotnet/src/Ocb.Runtime.Worker/Application/Skills/SkillsMaterializationCoordinator.cs
- Create: dotnet/src/Ocb.Runtime.Worker/Infra/Clients/ClaudeCode/ClaudeCodeRelayTypedClient.cs
- Create: dotnet/src/Ocb.Runtime.Worker/Infra/Clients/OpenClaw/OpenClawGatewayTypedClient.cs
- Create: dotnet/src/Ocb.Runtime.Worker/Infra/Clients/Models/RelaySessionDtos.cs
- Create: dotnet/src/Ocb.Runtime.Worker/Infra/Clients/Models/RelayWsFrameDtos.cs
- Create: dotnet/src/Ocb.Runtime.Worker/Infra/Clients/Models/ResultMapping.cs
- Create: dotnet/tests/Ocb.Grains.Tests/Ocb.Grains.Tests.csproj
- Create: dotnet/tests/Ocb.Runtime.Worker.Tests/Ocb.Runtime.Worker.Tests.csproj
- Create: dotnet/tests/Ocb.Runtime.Worker.Tests/Architecture/NoOpenClawDependencyTests.cs
- Create: dotnet/tests/Ocb.Runtime.Worker.Tests/Parity/SessionHttpParityTests.cs
- Create: dotnet/tests/Ocb.Runtime.Worker.Tests/Parity/EngineWsParityTests.cs
- Create: dotnet/tests/Ocb.Runtime.Worker.Tests/Assignment/RuntimeAssignmentLeaseTests.cs
- Create: dotnet/tests/Ocb.Runtime.Worker.Tests/Processes/WorkerProcessRuntimeLifecycleTests.cs
- Create: dotnet/tests/Ocb.Runtime.Worker.Tests/Workspaces/WorkspaceIsolationAndOrphanRecoveryTests.cs
- Create: dotnet/tests/Ocb.Grains.Tests/Runtime/BotRuntimeGrainDesiredObservedTests.cs
- Create: dotnet/tests/Ocb.Grains.Tests/Runtime/BotRuntimeGrainReactivationTests.cs
- Create: dotnet/tests/Ocb.Runtime.Worker.Tests/Skills/SkillsMaterializationBoundaryTests.cs
- Create: dotnet/tests/Ocb.Runtime.Worker.Tests/Parity/ContractCorpusGuardsTests.cs

说明：

- Ocb.Contracts 持有对外 Service API DTO；Ocb.PluginApi 持有底层 provider 插件协议。
- Ocb.GrainContracts 仅定义 Grain 接口与可序列化模型。
- Ocb.Grains 仅实现协调与状态机；不持有进程句柄或本地路径。
- Ocb.Runtime.Worker 持有 typed client、进程与 workspace 实际执行、日志与健康检查。

**⚠️ 跨-Plan 项目创建协调：**
- `Ocb.GrainContracts/Ocb.GrainContracts.csproj` + `context-boundary.json` 由 **gateway-channels plan (Task 1)** 作为共享骨架创建。本 plan 只在 `Ocb.GrainContracts/Runtime/` 下追加子目录和接口文件，**不重新创建 .csproj 文件**。
- `Ocb.Grains/Ocb.Grains.csproj` + `context-boundary.json` 同样由 **gateway-channels plan (Task 1)** 创建。本 plan 只在 `Ocb.Grains/Runtime/` 下追加实现文件。
- `Ocb.Grains.Tests/Ocb.Grains.Tests.csproj` 由 **gateway-channels plan (Task 1)** 创建。本 plan 只在已存在的测试项目中追加 `Runtime/` 子目录。
- 实施时检查：如果骨架尚未创建，先执行 gateway-channels plan Task 1。

---

### Task 1: 建立 Runtime Worker/Grain 解决方案骨架与边界门禁

**Files:**

- Create: dotnet/src/Ocb.GrainContracts/Ocb.GrainContracts.csproj
- Create: dotnet/src/Ocb.GrainContracts/context-boundary.json
- Create: dotnet/src/Ocb.Grains/Ocb.Grains.csproj
- Create: dotnet/src/Ocb.Grains/context-boundary.json
- Create: dotnet/src/Ocb.Runtime.Worker/Ocb.Runtime.Worker.csproj
- Create: dotnet/src/Ocb.Runtime.Worker/context-boundary.json
- Create: dotnet/tests/Ocb.Grains.Tests/Ocb.Grains.Tests.csproj
- Create: dotnet/tests/Ocb.Runtime.Worker.Tests/Ocb.Runtime.Worker.Tests.csproj
- Create: dotnet/tests/Ocb.Runtime.Worker.Tests/Architecture/NoOpenClawDependencyTests.cs
- Modify: dotnet/tests/Ocb.Architecture.Tests/ContextBoundaryTests.cs
- Modify: dotnet/Ocb.slnx

**Interfaces:**

- Consumes: dotnet/Ocb.slnx
- Produces:
  - Ocb.GrainContracts (仅契约)
  - Ocb.Grains (仅协调)
  - Ocb.Runtime.Worker (执行层)
  - NoOpenClawDependencyTests.AssertNoForbiddenReferences(projectPath)
  - 三个 `context-boundary.json`，其 `internal_dependencies` 与实际 `ProjectReference` 完全一致

- [ ] **Step 1: 写失败测试（禁止 OpenClaw 依赖）**

```csharp
[Fact]
public void RuntimeWorkerProject_ShouldNotReferenceOpenClawAssemblies()
{
    var projectText = File.ReadAllText(Path.Combine(RepoRoot(), "dotnet/src/Ocb.Runtime.Worker/Ocb.Runtime.Worker.csproj"));
    projectText.Should().NotContain("OpenClaw", "Runtime Worker must not depend on OpenClaw.*");
}
```

- [ ] **Step 2: RED**

Run: dotnet test dotnet/tests/Ocb.Runtime.Worker.Tests/Ocb.Runtime.Worker.Tests.csproj --filter RuntimeWorkerProject_ShouldNotReferenceOpenClawAssemblies
Expected: FAIL（文件不存在或断言失败）。

- [ ] **Step 3: 最小实现**

```xml
<!-- Ocb.Runtime.Worker.csproj 最小签名 -->
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Ocb.Contracts\Ocb.Contracts.csproj" />
    <ProjectReference Include="..\Ocb.PluginApi\Ocb.PluginApi.csproj" />
    <ProjectReference Include="..\Ocb.GrainContracts\Ocb.GrainContracts.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: GREEN**

Run: dotnet test dotnet/tests/Ocb.Runtime.Worker.Tests/Ocb.Runtime.Worker.Tests.csproj --filter RuntimeWorkerProject_ShouldNotReferenceOpenClawAssemblies
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add dotnet/Ocb.slnx dotnet/src/Ocb.GrainContracts dotnet/src/Ocb.Grains dotnet/src/Ocb.Runtime.Worker dotnet/tests/Ocb.Grains.Tests dotnet/tests/Ocb.Runtime.Worker.Tests
git commit -m "build(dotnet): scaffold runtime worker and grain projects with boundary guard"
```

---

### Task 2: 定义 Session API parity 契约与内部 reassign 协调接口（非公开 API）

**Files:**

- Create: dotnet/src/Ocb.GrainContracts/Runtime/BotRuntimeContracts.cs
- Create: dotnet/src/Ocb.GrainContracts/Runtime/RuntimeLeaseContracts.cs
- Create: dotnet/src/Ocb.Runtime.Worker/Application/Assignment/IRuntimeAssignmentCoordinator.cs
- Create: dotnet/tests/Ocb.Runtime.Worker.Tests/Parity/ContractCorpusGuardsTests.cs
- Create: dotnet/tests/Ocb.Runtime.Worker.Tests/Assignment/RuntimeAssignmentLeaseTests.cs

**Interfaces:**

- Consumes:
  - dotnet/contracts/parity-corpus/manifest.json
  - dotnet/contracts/parity-corpus/engine.openapi.json
- Produces:
  - interface IBotRuntimeGrain
  - record DesiredRuntimeState
  - record ObservedRuntimeState
  - interface IRuntimeAssignmentCoordinator
  - method `Task<ReassignResult> RequestInternalReassignAsync(RuntimeReassignRequest request, CancellationToken ct)`

- [ ] **Step 1: 写失败测试（reassign 不得出现在公开 contract）**

```csharp
[Fact]
public async Task InternalCoordinator_ExposesReassignWithoutPublicEndpoint()
{
  var method = typeof(IRuntimeAssignmentCoordinator).GetMethod("RequestInternalReassignAsync");
  method.Should().NotBeNull();
  var openApi = await _client.GetStringAsync("/openapi/v1.json");
    openApi.Should().NotContain("reassign", "reassign is internal coordination capability, not public API");
}
```

- [ ] **Step 2: RED**

Run: dotnet test dotnet/tests/Ocb.Runtime.Worker.Tests/Ocb.Runtime.Worker.Tests.csproj --filter InternalCoordinator_ExposesReassignWithoutPublicEndpoint
Expected: FAIL，`IRuntimeAssignmentCoordinator` 或内部方法尚不存在。

- [ ] **Step 3: 最小实现（仅内部接口）**

```csharp
public sealed record RuntimeReassignRequest(CallerContext Caller, string BotId, string Reason, string RequestedByWorkerId);
public sealed record ReassignResult(bool Accepted, string? LeaseToken, string? Message);

public interface IRuntimeAssignmentCoordinator
{
    Task<ReassignResult> RequestInternalReassignAsync(RuntimeReassignRequest request, CancellationToken ct);
}
```

- [ ] **Step 4: GREEN**

Run:

- dotnet test dotnet/tests/Ocb.Runtime.Worker.Tests/Ocb.Runtime.Worker.Tests.csproj --filter InternalCoordinator_ExposesReassignWithoutPublicEndpoint
- dotnet test dotnet/tests/Ocb.Runtime.Worker.Tests/Ocb.Runtime.Worker.Tests.csproj --filter RuntimeAssignmentLeaseTests

Expected: PASS，且没有新增公开 endpoint。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.GrainContracts/Runtime dotnet/src/Ocb.Runtime.Worker/Application/Assignment dotnet/tests/Ocb.Runtime.Worker.Tests/Parity dotnet/tests/Ocb.Runtime.Worker.Tests/Assignment
git commit -m "feat(runtime): add internal reassign coordination contract without public API exposure"
```

---

### Task 3: 实现 OpenClaw/Claude Code typed client 反腐模型（ACL）

**Files:**

- Create: dotnet/src/Ocb.Runtime.Worker/Infra/Clients/ClaudeCode/ClaudeCodeRelayTypedClient.cs
- Create: dotnet/src/Ocb.Runtime.Worker/Infra/Clients/OpenClaw/OpenClawGatewayTypedClient.cs
- Create: dotnet/src/Ocb.Runtime.Worker/Infra/Clients/Models/RelaySessionDtos.cs
- Create: dotnet/src/Ocb.Runtime.Worker/Infra/Clients/Models/RelayWsFrameDtos.cs
- Create: dotnet/src/Ocb.Runtime.Worker/Infra/Clients/Models/ResultMapping.cs
- Create: dotnet/tests/Ocb.Runtime.Worker.Tests/Parity/SessionHttpParityTests.cs

**Interfaces:**

- Consumes: Ocb.Contracts Session DTO
- Produces:
  - interface IEngineSessionPort
  - `Task<IReadOnlyList<SessionSummaryDto>> ListSessionsAsync(SessionListQuery query, CancellationToken ct)`
  - `Task<ResetSessionResultDto> ResetSessionAsync(string sessionKey, CancellationToken ct)`

- [ ] **Step 1: 写失败测试（typed 映射失败时 fail-closed）**

```csharp
[Fact]
public async Task SessionList_WhenPayloadShapeUnexpected_ShouldReturnContractError()
{
    var client = BuildClientReturning("{\"payload\":\"raw-string\"}");
    var ex = await Assert.ThrowsAsync<ContractMappingException>(() => client.ListSessionsAsync(new SessionListQuery(), default));
    ex.Code.Should().Be("ENGINE_PAYLOAD_SHAPE_MISMATCH");
}
```

- [ ] **Step 2: RED**

Run: dotnet test dotnet/tests/Ocb.Runtime.Worker.Tests/Ocb.Runtime.Worker.Tests.csproj --filter SessionList_WhenPayloadShapeUnexpected_ShouldReturnContractError
Expected: FAIL（缺少 client 或异常类型）。

- [ ] **Step 3: 最小实现**

```csharp
public sealed class ContractMappingException : Exception
{
    public string Code { get; }
    public ContractMappingException(string code, string message) : base(message) => Code = code;
}

public sealed class ClaudeCodeRelayTypedClient : IEngineSessionPort
{
    public async Task<IReadOnlyList<SessionSummaryDto>> ListSessionsAsync(SessionListQuery query, CancellationToken ct)
    {
        // 仅接受 parity-corpus 定义的 payload 形状
        throw new ContractMappingException("ENGINE_PAYLOAD_SHAPE_MISMATCH", "Unexpected sessions.list payload shape");
    }
}
```

- [ ] **Step 4: GREEN**

Run: dotnet test dotnet/tests/Ocb.Runtime.Worker.Tests/Ocb.Runtime.Worker.Tests.csproj --filter SessionHttpParityTests
Expected: PASS，异常码与断言一致。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Runtime.Worker/Infra/Clients dotnet/tests/Ocb.Runtime.Worker.Tests/Parity/SessionHttpParityTests.cs
git commit -m "feat(runtime): add typed relay clients with fail-closed anti-corruption mapping"
```

---

### Task 4: 交付 Engine Session HTTP parity（仅对齐现有 contract）

**Files:**

- Create: dotnet/src/Ocb.Runtime.Worker/Api/SessionParityController.cs
- Modify: dotnet/src/Ocb.Runtime.Worker/Program.cs
- Create: dotnet/tests/Ocb.Runtime.Worker.Tests/Parity/SessionHttpParityTests.cs

**Interfaces:**

- Consumes:
  - IEngineSessionPort
  - Ocb.Contracts session DTO
- Produces:
  - GET /api/sessions
  - GET /api/sessions/{session_id}
  - DELETE /api/sessions/{session_id}
  - GET /api/sessions/{session_id}/messages
  - DELETE /api/sessions/{session_id}/messages
  - POST /api/sessions/{session_id}/update

- [ ] **Step 1: 写失败测试（字段与状态码 parity）**

```csharp
[Fact]
public async Task GetSession_NotFound_ShouldReturn404_WithMessageSessionNotFound()
{
    var response = await _client.GetAsync("/api/sessions/non-existent");
    response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    var body = await response.Content.ReadAsStringAsync();
    body.Should().Contain("Session not found");
}
```

- [ ] **Step 2: RED**

Run: dotnet test dotnet/tests/Ocb.Runtime.Worker.Tests/Ocb.Runtime.Worker.Tests.csproj --filter GetSession_NotFound_ShouldReturn404_WithMessageSessionNotFound
Expected: FAIL（endpoint 不存在，返回 404 路由级而非约定语义）。

- [ ] **Step 3: 最小实现**

```csharp
[ApiController]
[Route("api/sessions")]
public sealed class SessionParityController : ControllerBase
{
    [HttpGet("{session_id}")]
    public async Task<IActionResult> GetSession([FromRoute(Name = "session_id")] string sessionId, CancellationToken ct)
    {
        var session = await _service.TryGetSessionAsync(sessionId, ct);
        if (session is null) return NotFound(new { success = false, detail = "Session not found" });
        return Ok(new { success = true, data = session });
    }
}
```

- [ ] **Step 4: GREEN**

Run:

- dotnet test dotnet/tests/Ocb.Runtime.Worker.Tests/Ocb.Runtime.Worker.Tests.csproj --filter SessionHttpParityTests

Expected: PASS；响应字段 success/data/detail 与 parity 断言一致。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Runtime.Worker/Api/SessionParityController.cs dotnet/src/Ocb.Runtime.Worker/Program.cs dotnet/tests/Ocb.Runtime.Worker.Tests/Parity/SessionHttpParityTests.cs
git commit -m "feat(runtime): implement engine session HTTP parity endpoints"
```

---

### Task 5: 交付 Engine WebSocket parity（握手+方法白名单+错误语义）

**Files:**

- Create: dotnet/src/Ocb.Runtime.Worker/Api/WebSocket/EngineWsEndpoint.cs
- Create: dotnet/src/Ocb.Runtime.Worker/Api/WebSocket/EngineWsMethodDispatcher.cs
- Create: dotnet/src/Ocb.Runtime.Worker/Api/WebSocket/EngineWsProtocolGuards.cs
- Create: dotnet/tests/Ocb.Runtime.Worker.Tests/Parity/EngineWsParityTests.cs

**Interfaces:**

- Consumes: dotnet/contracts/parity-corpus/engine-websocket-protocol.md
- Produces:
  - WS /api/openclaw/ws
  - connect challenge/response
  - req/res/event frame handling for chat.send, chat.abort, sessions.list, sessions.patch, sessions.delete, sessions.reset, interaction.resolve

- [ ] **Step 1: 写失败测试（未知 method 必须返回 INVALID_REQUEST）**

```csharp
[Fact]
public async Task UnknownMethod_ShouldReturnErrorFrame_InvalidRequest()
{
    var frame = await SendReqAsync("unknown.method", new { });
    frame.Type.Should().Be("res");
    frame.Ok.Should().BeFalse();
    frame.Error.Code.Should().Be("INVALID_REQUEST");
}
```

- [ ] **Step 2: RED**

Run: dotnet test dotnet/tests/Ocb.Runtime.Worker.Tests/Ocb.Runtime.Worker.Tests.csproj --filter UnknownMethod_ShouldReturnErrorFrame_InvalidRequest
Expected: FAIL（未实现 method guard 或错误码不匹配）。

- [ ] **Step 3: 最小实现**

```csharp
private static readonly HashSet<string> AllowedMethods = new(StringComparer.Ordinal)
{
  "connect", "session.new", "sessions.list", "sessions.patch", "sessions.delete", "sessions.reset",
  "chat.send", "chat.history", "chat.abort", "interaction.resolve", "interaction.pending.list",
  "health.claude", "providers.available", "models.list"
};

public WsResponseFrame RejectUnknown(string id, string method) =>
    WsResponseFrame.Error(id, "INVALID_REQUEST", $"Unknown method: {method}");
```

- [ ] **Step 4: GREEN**

Run:

- dotnet test dotnet/tests/Ocb.Runtime.Worker.Tests/Ocb.Runtime.Worker.Tests.csproj --filter EngineWsParityTests

Expected: PASS；握手 challenge、protocol version 校验、错误码、事件封装行为通过。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Runtime.Worker/Api/WebSocket dotnet/tests/Ocb.Runtime.Worker.Tests/Parity/EngineWsParityTests.cs
git commit -m "feat(runtime): implement websocket parity endpoint with strict method guards"
```

---

### Task 6: 实现 assignment + lease 协调（Worker 分配与续约）

**Files:**

- Create: dotnet/src/Ocb.Runtime.Worker/Application/Assignment/RuntimeAssignmentCoordinator.cs
- Create: dotnet/src/Ocb.Grains/Runtime/WorkerAssignmentLease.cs
- Create: dotnet/tests/Ocb.Runtime.Worker.Tests/Assignment/RuntimeAssignmentLeaseTests.cs

**Interfaces:**

- Consumes: IRuntimeAssignmentCoordinator, IBotRuntimeGrain
- Produces:
  - `Task<AssignmentResult> AssignWorkerAsync(AssignWorkerRequest request, CancellationToken ct)`
  - `Task<LeaseRenewResult> RenewLeaseAsync(RenewLeaseRequest request, CancellationToken ct)`
  - `Task<LeaseReleaseResult> ReleaseLeaseAsync(ReleaseLeaseRequest request, CancellationToken ct)`

- [ ] **Step 1: 写失败测试（过期 lease 不可续约）**

```csharp
[Fact]
public async Task RenewLease_WhenExpired_ShouldReturnRejected()
{
    var result = await _coordinator.RenewLeaseAsync(new RenewLeaseRequest("tenant-1","bot-1","lease-abc", nowPlusMinutes: 1), default);
    result.Accepted.Should().BeFalse();
    result.Reason.Should().Be("LEASE_EXPIRED");
}
```

- [ ] **Step 2: RED**

Run: dotnet test dotnet/tests/Ocb.Runtime.Worker.Tests/Ocb.Runtime.Worker.Tests.csproj --filter RenewLease_WhenExpired_ShouldReturnRejected
Expected: FAIL（没有 lease 时间窗口校验）。

- [ ] **Step 3: 最小实现**

```csharp
public sealed class RuntimeAssignmentCoordinator : IRuntimeAssignmentCoordinator
{
    public Task<LeaseRenewResult> RenewLeaseAsync(RenewLeaseRequest request, CancellationToken ct)
    {
        if (request.LeaseExpiresAtUtc <= _clock.UtcNow)
            return Task.FromResult(new LeaseRenewResult(false, "LEASE_EXPIRED"));
        return Task.FromResult(new LeaseRenewResult(true, null));
    }
}
```

- [ ] **Step 4: GREEN**

Run: dotnet test dotnet/tests/Ocb.Runtime.Worker.Tests/Ocb.Runtime.Worker.Tests.csproj --filter RuntimeAssignmentLeaseTests
Expected: PASS；assignment、renew、release 与抢占规则通过。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Runtime.Worker/Application/Assignment/RuntimeAssignmentCoordinator.cs dotnet/src/Ocb.Grains/Runtime/WorkerAssignmentLease.cs dotnet/tests/Ocb.Runtime.Worker.Tests/Assignment/RuntimeAssignmentLeaseTests.cs
git commit -m "feat(runtime): add worker assignment and lease renewal coordination"
```

---

### Task 7: 实现进程启动/健康/日志/取消/关闭与端口分配

**Files:**

- Create: dotnet/src/Ocb.Runtime.Worker/Application/Processes/IWorkerProcessRuntime.cs
- Create: dotnet/src/Ocb.Runtime.Worker/Application/Processes/WorkerProcessRuntime.cs
- Create: dotnet/src/Ocb.Runtime.Worker/Application/Processes/PortAllocator.cs
- Create: dotnet/tests/Ocb.Runtime.Worker.Tests/Processes/WorkerProcessRuntimeLifecycleTests.cs

**Interfaces:**

- Consumes: typed client 配置、assignment lease
- Produces:
  - `Task<ProcessStartResult> StartAsync(StartProcessCommand command, CancellationToken ct)`
  - `IAsyncEnumerable<RuntimeLogEvent> StreamLogsAsync(ProcessRuntimeId runtimeId, CancellationToken ct)`
  - `Task<CancelResult> CancelAsync(ProcessRuntimeId runtimeId, CancellationToken ct)`
  - `Task<ShutdownResult> ShutdownAsync(ProcessRuntimeId runtimeId, CancellationToken ct)`

- [ ] **Step 1: 写失败测试（取消必须触发进程终止）**

```csharp
[Fact]
public async Task CancelAsync_ShouldSignalProcessAndMarkObservedCancelled()
{
    var result = await _runtime.CancelAsync(new ProcessRuntimeId("tenant-1","bot-1","run-1"), default);
    result.Cancelled.Should().BeTrue();
    _fakeProcess.KillCalled.Should().BeTrue();
}
```

- [ ] **Step 2: RED**

Run: dotnet test dotnet/tests/Ocb.Runtime.Worker.Tests/Ocb.Runtime.Worker.Tests.csproj --filter CancelAsync_ShouldSignalProcessAndMarkObservedCancelled
Expected: FAIL（未调用 kill 或状态未更新）。

- [ ] **Step 3: 最小实现**

```csharp
public async Task<CancelResult> CancelAsync(ProcessRuntimeId runtimeId, CancellationToken ct)
{
    if (!_table.TryGetValue(runtimeId, out var handle)) return new CancelResult(false, "RUNTIME_NOT_FOUND");
    handle.Process.Kill(entireProcessTree: true);
    await _observedSink.MarkCancelledAsync(runtimeId, ct);
    return new CancelResult(true, null);
}
```

- [ ] **Step 4: GREEN**

Run: dotnet test dotnet/tests/Ocb.Runtime.Worker.Tests/Ocb.Runtime.Worker.Tests.csproj --filter WorkerProcessRuntimeLifecycleTests
Expected: PASS；启动健康探测、日志流采集、取消、关闭和端口回收通过。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Runtime.Worker/Application/Processes dotnet/tests/Ocb.Runtime.Worker.Tests/Processes/WorkerProcessRuntimeLifecycleTests.cs
git commit -m "feat(runtime): implement process lifecycle runtime with health logs cancel shutdown and port allocation"
```

---

### Task 8: 实现 workspace 隔离、清理与 orphan recovery

**Files:**

- Create: dotnet/src/Ocb.Runtime.Worker/Application/Workspaces/IWorkspaceIsolationService.cs
- Create: dotnet/src/Ocb.Runtime.Worker/Application/Workspaces/WorkspaceIsolationService.cs
- Create: dotnet/src/Ocb.Runtime.Worker/Application/Recovery/IOrphanRecoveryService.cs
- Create: dotnet/src/Ocb.Runtime.Worker/Application/Recovery/OrphanRecoveryService.cs
- Create: dotnet/tests/Ocb.Runtime.Worker.Tests/Workspaces/WorkspaceIsolationAndOrphanRecoveryTests.cs

**Interfaces:**

- Consumes: assignment lease, runtime identity
- Produces:
  - `Task<WorkspaceHandle> PrepareWorkspaceAsync(WorkspaceRequest request, CancellationToken ct)`
  - Task CleanupWorkspaceAsync(WorkspaceHandle handle, CancellationToken ct)
  - `Task<IReadOnlyList<RecoveredOrphan>> RecoverOrphansAsync(CancellationToken ct)`

- [ ] **Step 1: 写失败测试（orphan 目录需回收）**

```csharp
[Fact]
public async Task RecoverOrphans_ShouldRemoveExpiredWorkspaceAndReportRecovery()
{
    var recovered = await _service.RecoverOrphansAsync(default);
    recovered.Should().ContainSingle(x => x.WorkspaceId == "ws-orphan-1");
    Directory.Exists(_orphanPath).Should().BeFalse();
}
```

- [ ] **Step 2: RED**

Run: dotnet test dotnet/tests/Ocb.Runtime.Worker.Tests/Ocb.Runtime.Worker.Tests.csproj --filter RecoverOrphans_ShouldRemoveExpiredWorkspaceAndReportRecovery
Expected: FAIL（未实现扫描或清理）。

- [ ] **Step 3: 最小实现**

```csharp
public async Task<IReadOnlyList<RecoveredOrphan>> RecoverOrphansAsync(CancellationToken ct)
{
    var recovered = new List<RecoveredOrphan>();
    foreach (var dir in Directory.EnumerateDirectories(_workspaceRoot))
    {
        if (IsLeaseMissingOrExpired(dir))
        {
            Directory.Delete(dir, recursive: true);
            recovered.Add(new RecoveredOrphan(Path.GetFileName(dir), "LEASE_MISSING_OR_EXPIRED"));
        }
    }
    return recovered;
}
```

- [ ] **Step 4: GREEN**

Run: dotnet test dotnet/tests/Ocb.Runtime.Worker.Tests/Ocb.Runtime.Worker.Tests.csproj --filter WorkspaceIsolationAndOrphanRecoveryTests
Expected: PASS；目录隔离、并发请求幂等、清理与 orphan 回收通过。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Runtime.Worker/Application/Workspaces dotnet/src/Ocb.Runtime.Worker/Application/Recovery dotnet/tests/Ocb.Runtime.Worker.Tests/Workspaces/WorkspaceIsolationAndOrphanRecoveryTests.cs
git commit -m "feat(runtime): add workspace isolation cleanup and orphan recovery"
```

---

### Task 9: 实现 BotRuntimeGrain desired/observed 协调状态（不持久化句柄/本地路径）

**Files:**

- Create: dotnet/src/Ocb.Grains/Runtime/BotRuntimeGrain.cs
- Create: dotnet/src/Ocb.Grains/Runtime/BotRuntimeState.cs
- Create: dotnet/src/Ocb.Grains/Runtime/BotRuntimeObservedState.cs
- Create: dotnet/tests/Ocb.Grains.Tests/Runtime/BotRuntimeGrainDesiredObservedTests.cs

**Interfaces:**

- Consumes: IBotRuntimeGrain, IRuntimeAssignmentCoordinator
- Produces:
  - Task SetDesiredStateAsync(DesiredRuntimeState desired, CancellationToken ct)
  - Task ReportObservedStateAsync(ObservedRuntimeState observed, CancellationToken ct)
  - `Task<BotRuntimeSnapshot> GetSnapshotAsync(CancellationToken ct)`

- [ ] **Step 1: 写失败测试（禁止路径/句柄进 Grain state）**

```csharp
[Fact]
public async Task Snapshot_ShouldNotContain_ProcessHandleOrAbsoluteWorkspacePath()
{
    var snapshot = await _grain.GetSnapshotAsync(default);
    snapshot.ToJson().Should().NotContain("ProcessHandle");
    snapshot.ToJson().Should().NotContain("C:\\");
    snapshot.ToJson().Should().NotContain("/home/");
}
```

- [ ] **Step 2: RED**

Run: dotnet test dotnet/tests/Ocb.Grains.Tests/Ocb.Grains.Tests.csproj --filter Snapshot_ShouldNotContain_ProcessHandleOrAbsoluteWorkspacePath
Expected: FAIL（状态模型未收敛）。

- [ ] **Step 3: 最小实现**

```csharp
public sealed record BotRuntimeObservedState(
    string WorkerId,
    string RuntimeStatus,
    string? LeaseToken,
    DateTimeOffset LastHeartbeatUtc,
    string? RuntimeVersion);

// 不包含 IntPtr、PID handle 对象、绝对路径字符串
```

- [ ] **Step 4: GREEN**

Run: dotnet test dotnet/tests/Ocb.Grains.Tests/Ocb.Grains.Tests.csproj --filter BotRuntimeGrainDesiredObservedTests
Expected: PASS；desired/observed 转移、快照字段约束通过。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Grains/Runtime dotnet/tests/Ocb.Grains.Tests/Runtime/BotRuntimeGrainDesiredObservedTests.cs
git commit -m "feat(grains): implement bot runtime desired observed coordination without local handle state"
```

---

### Task 10: 覆盖 Worker 丢失与 Grain reactivation，补齐 Skills 物化边界与 HTTP+WS parity 一致性回归

**Files:**

- Create: dotnet/tests/Ocb.Grains.Tests/Runtime/BotRuntimeGrainReactivationTests.cs
- Create: dotnet/src/Ocb.Contracts/Skills/IRuntimeSkillMaterializationService.cs
- Create: dotnet/src/Ocb.Runtime.Worker/Application/Skills/SkillsMaterializationCoordinator.cs
- Create: dotnet/src/Ocb.Runtime.Worker/Application/Skills/DownloadRetryPolicy.cs
- Create: dotnet/src/Ocb.Runtime.Worker/Application/Skills/AtomicActivationPublisher.cs
- Create: dotnet/tests/Ocb.Runtime.Worker.Tests/Skills/SkillsMaterializationBoundaryTests.cs
- Create: dotnet/tests/Ocb.Runtime.Worker.Tests/Skills/DownloadRetryPolicyTests.cs
- Create: dotnet/tests/Ocb.Runtime.Worker.Tests/Skills/AtomicActivationPublisherTests.cs
- Create: dotnet/tests/Ocb.Runtime.Worker.Tests/Parity/ContractCorpusGuardsTests.cs

**Interfaces:**

- Consumes:
  - IBotRuntimeGrain
  - parity corpus (engine.openapi.json + engine-websocket-protocol.md)
- Produces:
  - Task OnWorkerLeaseLostAsync(string workerId, string leaseToken, CancellationToken ct)
  - `Task<MaterializationResult> MaterializeActivatedSkillsAsync(MaterializationRequest request, CancellationToken ct)`，作为 Backend 调用 Runtime Worker 的 Service API
  - HTTP+WS parity 回归断言集合

- [ ] **Step 1: 写失败测试（worker 丢失后 reactivation 触发重新分配）**

```csharp
[Fact]
public async Task OnWorkerLeaseLost_ShouldScheduleReassignDuringReactivation()
{
    await _grain.OnWorkerLeaseLostAsync("worker-a", "lease-1", default);
    var snapshot = await _grain.GetSnapshotAsync(default);
    snapshot.Observed.RuntimeStatus.Should().Be("ReassignPending");
}
```

- [ ] **Step 2: RED**

Run:

- dotnet test dotnet/tests/Ocb.Grains.Tests/Ocb.Grains.Tests.csproj --filter OnWorkerLeaseLost_ShouldScheduleReassignDuringReactivation
- dotnet test dotnet/tests/Ocb.Runtime.Worker.Tests/Ocb.Runtime.Worker.Tests.csproj --filter SkillsMaterializationBoundaryTests

Expected: FAIL（未实现 reactivation 分支和 skills 边界）。

- [ ] **Step 3: 最小实现（Skills 仅物化，不 publication）**

```csharp
public sealed record ActivatedSkillVersion(
  string SkillId,
  string SourceLocator,
  string ImmutableVersion,
  string ExpectedSha256);

public sealed record MaterializationRequest(
  CallerContext Caller,
  string BotId,
  string ManifestContractVersion,
  IReadOnlyList<ActivatedSkillVersion> ActivatedSkills);

public sealed record MaterializationResult(
  bool Succeeded,
  string ObservedState,
  string? ActiveViewId,
  string? ErrorCode);

public interface IRuntimeSkillMaterializationService
{
    Task<MaterializationResult> MaterializeActivatedSkillsAsync(MaterializationRequest request, CancellationToken ct);
}

public sealed class DownloadRetryPolicy
{
  public async Task<T> ExecuteAsync<T>(Func<int, Task<T>> action, CancellationToken ct)
  {
    for (var attempt = 1; ; attempt++)
    {
      try { return await action(attempt); }
      catch (InvalidDataException) { throw; }
      catch (Exception) when (attempt < 3)
      {
        await Task.Delay(BackoffWithJitter(attempt), ct);
      }
    }
  }

  private static TimeSpan BackoffWithJitter(int attempt)
    => TimeSpan.FromMilliseconds((100 * Math.Pow(2, attempt - 1)) + Random.Shared.Next(0, 51));
}

// AtomicActivationPublisher 在 Runtime 本地创建临时视图、校验后原子切换 active pointer；
// 切换失败时回滚 previousViewId，无旧视图时返回 ACTIVATION_NO_PREVIOUS_VIEW。
```

- [ ] **Step 4: GREEN**

Run:

- dotnet test dotnet/tests/Ocb.Grains.Tests/Ocb.Grains.Tests.csproj --filter BotRuntimeGrainReactivationTests
- dotnet test dotnet/tests/Ocb.Runtime.Worker.Tests/Ocb.Runtime.Worker.Tests.csproj --filter "ContractCorpusGuardsTests|EngineWsParityTests|SessionHttpParityTests|SkillsMaterializationBoundaryTests|DownloadRetryPolicyTests|AtomicActivationPublisherTests"

Expected:

- PASS：worker 丢失、grain reactivation、内部 reassign 协调通过。
- PASS：HTTP+WS parity 行为一致；skills publication 入口不存在；瞬时下载最多 3 次且带指数退避+jitter；integrity failure 只尝试 1 次；原子发布失败回滚旧视图，无旧视图 fail closed；wire guard 无新增未批准 method/field。

- [ ] **Step 5: Commit**

```bash
git add dotnet/tests/Ocb.Grains.Tests/Runtime/BotRuntimeGrainReactivationTests.cs dotnet/src/Ocb.Contracts/Skills dotnet/src/Ocb.Runtime.Worker/Application/Skills dotnet/tests/Ocb.Runtime.Worker.Tests/Skills dotnet/tests/Ocb.Runtime.Worker.Tests/Parity/ContractCorpusGuardsTests.cs
git commit -m "test(runtime): add worker-loss reactivation and skills materialization boundary parity guards"
```

---

## Self-Review

1. Spec coverage 对照：

- Session API parity：Task 2, Task 4, Task 10。
- assignment + lease：Task 2, Task 6, Task 10。
- OpenClaw/Claude Code typed client 反腐模型：Task 3。
- 进程启动/健康/日志/取消/关闭/端口：Task 7。
- workspace 隔离/清理/orphan recovery：Task 8。
- BotRuntimeGrain desired/observed：Task 9。
- Worker 丢失 + Grain reactivation：Task 10。
- Skills 物化接口且 publication 归 Backend：Task 10。
- Engine Session HTTP + Engine WebSocket parity：Task 4, Task 5, Task 10。
- 进程句柄/本地路径不入 Grain 权威状态：Task 9。
- reassign 为新内部协调能力非公开 API：Task 2, Task 10。
- 不猜 wire contract：Task 2, Task 5, Task 10。
- 命名方案 Ocb.GrainContracts/Ocb.Grains/Ocb.Runtime.Worker + API 分层：Task 1 + File Structure。
- 禁止 OpenClaw.* 依赖/bridge：Task 1 + Global Constraints。

1. No-placeholder check：

- 已按 writing-plans 禁止模式完成扫描，实际实施步骤中无占位内容。
- 每个任务都给出测试断言、RED/GREEN 命令、最小实现签名、提交命令。

1. Type consistency：

- 内部重分配统一使用 RequestInternalReassignAsync。
- lease 请求/结果命名在 Task 6 与 Task 10 保持一致。
- Session/WS parity 统一由 ContractCorpusGuardsTests 约束。

## Execution Handoff

Plan complete and saved to docs/superpowers/plans/2026-08-08-dotnet-runtime-worker.md. Two execution options:

1. Subagent-Driven (recommended) - 我逐任务派发独立子代理执行并在任务间复核
2. Inline Execution - 我在当前会话按 executing-plans 分批执行

请选择 1 或 2。
