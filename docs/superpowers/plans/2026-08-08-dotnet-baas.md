# .NET Ocb.Baas 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不引入 Python bridge、不缩减能力面的前提下，交付 `Ocb.Baas` 在 .NET 10 上的完整服务能力：health、Device、Template、tenant、透明 invoke-http/API gateway、发布/运行队列、QPM、TTL、SSE、SM4、Docker/K8s sandbox conformance，并完成 BaaS HTTP parity 与生命周期 E2E。

**Architecture:** 采用“Contracts/PluginApi/Core/Adapters/Providers/Infra/PostgreSql/Test”分层。`Ocb.Baas` 仅实现 BaaS 领域编排；`Ocb.Infrastructure.PostgreSql` 承担 `ocb_business` 读写；`Ocb.Plugins.*` 承担第三方边界（Docker、K8s、Crypto、Queue）；跨域边界上，`Ocb.Baas` 不直接读取 Backend/Fusion 内部数据，仅使用 `Ocb.Contracts` 与 `Ocb.PluginApi` 对外契约。

**Tech Stack:** .NET 10, ASP.NET Core, Orleans (仅协调层), EF Core + Npgsql, PostgreSQL schema `ocb_business`, xUnit, Testcontainers, NBomber（性能基线在后续阶段）。

## Global Constraints

- 全部新增项目使用 `net10.0`，保留 nullable + warnings as errors。
- 命名必须采用 `Ocb.Baas`、`Ocb.Infrastructure.PostgreSql`、`Ocb.PluginApi`、`Ocb.Contracts`；Provider 项目使用 `Ocb.Plugins.*`。
- 保持 Service API 与 Plugin API 分离：Service 合同进 `Ocb.Contracts`，Provider 合同进 `Ocb.PluginApi`。
- 不缩减 BaaS 范围为 CRUD，必须覆盖 health、Device、Template、tenant、invoke-http、发布与运行队列、QPM、TTL、SSE、第三方边界。
- 数据层只做 PostgreSQL 新系统：`ocb_business`，不先做 SQLite parity。
- billing 现状无完整收费产品：本计划只实现 quota/usage 边界、限流与审计，不臆造 payment 产品流程。
- 仅实现 SM4（known-answer、roundtrip、invalid-key、padding、tamper）；SM2 明确排除。
- Docker/K8s 需要同一 conformance suite；singlebox/cluster 都要可验证。
- 与 Backend/Fusion 边界明确：BaaS 不越权直接依赖 Backend/Fusion 内部实现，不修改其所有权模型。
- 透明 invoke-http/API gateway 行为保持现有 HTTP 语义，包含 hop-by-hop header 过滤与错误映射。
- 全任务按 TDD：先 RED，再最小实现 GREEN，再提交。

---

## File Structure

- `dotnet/src/Ocb.Contracts/Baas/*`
  - BaaS 服务契约、DTO、错误码、SSE 事件模型、publish/run queue 合同。
- `dotnet/src/Ocb.PluginApi/Baas/*`
  - Sandbox/Crypto/Queue/ExternalGateway Provider 协议；第三方边界能力声明。
- `dotnet/src/Ocb.Baas/*`
  - 领域服务与应用编排（tenant/template/device/health/publish/qpm/ttl/sse/invoke-http）。
- `dotnet/src/Ocb.Infrastructure.PostgreSql/*`
  - `ocb_business` 的 EF Core DbContext、实体映射、仓储与迁移。
- `dotnet/src/Ocb.Plugins.Sandbox.Docker/*`
  - Docker sandbox provider 及其 conformance 实现。
- `dotnet/src/Ocb.Plugins.Sandbox.K8s/*`
  - K8s sandbox provider 及其 conformance 实现。
- `dotnet/src/Ocb.Plugins.Crypto.SM4/*`
  - SM4 Provider（BugFree.Security 为首选，必要时 BouncyCastle 兼容回退）。
- `dotnet/src/Ocb.Plugins.Queue.PostgreSql/*`
  - 发布/运行队列 Provider（去耦调度器）。
- `dotnet/src/Ocb.Baas/Web/*`
  - `Ocb.Baas` 内的 ASP.NET Core Delivery Adapter：HTTP parity、SSE、invoke-http、gateway；不新建额外生产程序集。
- `dotnet/tests/Ocb.Baas.Contracts.Tests/*`
  - 契约与序列化测试。
- `dotnet/tests/Ocb.Baas.Core.Tests/*`
  - 核心服务 TDD。
- `dotnet/tests/Ocb.Baas.Provider.Conformance.Tests/*`
  - Docker/K8s/SM4/Queue Provider conformance。
- `dotnet/tests/Ocb.Baas.Web.Tests/*`
  - Router 行为与错误映射。
- `dotnet/tests/Ocb.Baas.EndToEnd.Tests/*`
  - 生命周期 E2E 与 parity（对齐 `dotnet/contracts/parity-corpus/baas.openapi.json`）。

---

### Task 1: 固化 BaaS Service Contract（health/device/template/tenant/qpm/publish/sse）

**⚠️ 跨-Plan 项目创建协调：**
- `Ocb.Infrastructure.PostgreSql/Ocb.Infrastructure.PostgreSql.csproj` + `context-boundary.json` 由 **本 plan (Task 1)** 作为共享骨架创建。Backend Skills plan 只在 `Ocb.Infrastructure.PostgreSql/` 下追加 `Skills/`、`Identity/`、`Assets/` 等子目录和 EF Core 实体文件，**不重新创建 .csproj 文件**。
- `ocb_business` schema 是共享 schema。本 plan 创建 `Ocb.Infrastructure.PostgreSql/Baas/` 子目录及其 `BaasDbContext`（bounded context）；Backend Skills plan 在 `Ocb.Infrastructure.PostgreSql/` 下追加各自的 bounded context DbContext。
- 实施顺序约束：本 plan Task 1 必须先执行（创建 PostgreSql 项目骨架），Backend Skills plan 的 PostgreSql 部分才能在此基础上追加文件。

**Files:**

- Create: `dotnet/src/Ocb.Baas/Ocb.Baas.csproj`
- Create: `dotnet/src/Ocb.Baas/context-boundary.json`
- Create: `dotnet/src/Ocb.Infrastructure.PostgreSql/Ocb.Infrastructure.PostgreSql.csproj`
- Create: `dotnet/src/Ocb.Infrastructure.PostgreSql/context-boundary.json`
- Modify: `dotnet/Ocb.slnx`
- Create: `dotnet/src/Ocb.Contracts/Baas/Health/HealthContracts.cs`
- Create: `dotnet/src/Ocb.Contracts/Baas/Device/DeviceContracts.cs`
- Create: `dotnet/src/Ocb.Contracts/Baas/Template/TemplateContracts.cs`
- Create: `dotnet/src/Ocb.Contracts/Baas/Tenant/TenantContracts.cs`
- Create: `dotnet/src/Ocb.Contracts/Baas/Qpm/QpmContracts.cs`
- Create: `dotnet/src/Ocb.Contracts/Baas/Publish/PublishContracts.cs`
- Create: `dotnet/src/Ocb.Contracts/Baas/Sse/SseContracts.cs`
- Test: `dotnet/tests/Ocb.Baas.Contracts.Tests/BaasContractShapeTests.cs`

**Interfaces:**

- Consumes: `dotnet/contracts/migration-inventory.json` 中 baas route/protocol 清单。
- Produces:
  - `public interface IDeviceServiceContract`
  - `public interface ITemplateServiceContract`
  - `public interface ITenantServiceContract`
  - `public interface IQpmServiceContract`
  - `public interface IPublishServiceContract`
  - `public interface IHealthServiceContract`
  - 新生产项目的 `context-boundary.json`；`internal_dependencies` 必须逐项匹配 csproj 的 `ProjectReference`

- [ ] **Step 1: 写失败测试（契约缺失）**

```csharp
[Fact]
public void DeviceContract_MustExpose_InvokeHttp_And_WsInfo()
{
    var type = typeof(IDeviceServiceContract);
    Assert.NotNull(type.GetMethod("InvokeHttpAsync"));
    Assert.NotNull(type.GetMethod("ResolveWsInfoAsync"));
}
```

- [ ] **Step 2: RED 验证**
Run: `dotnet test dotnet/tests/Ocb.Baas.Contracts.Tests/Ocb.Baas.Contracts.Tests.csproj --filter DeviceContract_MustExpose_InvokeHttp_And_WsInfo`
Expected: FAIL，`IDeviceServiceContract` 或方法不存在。

- [ ] **Step 3: 最小实现（签名先行）**

```csharp
public interface IDeviceServiceContract
{
    Task<InvokeHttpResult> InvokeHttpAsync(InvokeHttpRequest request, CancellationToken ct);
    Task<WsConnectionInfo> ResolveWsInfoAsync(ResolveWsInfoRequest request, CancellationToken ct);
}
```

- [ ] **Step 4: GREEN 验证**
Run: `dotnet test dotnet/tests/Ocb.Baas.Contracts.Tests/Ocb.Baas.Contracts.Tests.csproj --filter DeviceContract_MustExpose_InvokeHttp_And_WsInfo`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Contracts/Baas dotnet/tests/Ocb.Baas.Contracts.Tests/BaasContractShapeTests.cs
git commit -m "feat(baas): define baas service contracts for device template tenant health publish qpm sse"
```

---

### Task 2: 建立 Plugin API 与第三方边界（Docker/K8s/Crypto/Queue/Gateway）

**Files:**

- Create: `dotnet/src/Ocb.PluginApi/Baas/Sandbox/ISandboxProvider.cs`
- Create: `dotnet/src/Ocb.PluginApi/Baas/Sandbox/ISandboxConformanceProvider.cs`
- Create: `dotnet/src/Ocb.PluginApi/Baas/Crypto/ISm4CryptoProvider.cs`
- Create: `dotnet/src/Ocb.PluginApi/Baas/Queue/IBotRunQueueProvider.cs`
- Create: `dotnet/src/Ocb.PluginApi/Baas/Gateway/IExternalGatewayProxy.cs`
- Create: `dotnet/src/Ocb.PluginApi/Baas/Persistence/IBaasRepositories.cs`
- Create: `dotnet/src/Ocb.PluginApi/Baas/Crypto/ISm4KeyResolver.cs`
- Test: `dotnet/tests/Ocb.Baas.Provider.Conformance.Tests/PluginBoundaryTests.cs`

**Interfaces:**

- Consumes: `src/baas/src/secbaas/community/spi/sandbox/*/_protocols.py`, `src/baas/src/secbaas/community/spi/crypto/_protocols.py`。
- Produces:
  - `ISandboxProvider.CreateAsync/DestroyAsync/InvokeHttpAsync`
  - `ISm4CryptoProvider.Encrypt/Decrypt`
  - `IBotRunQueueProvider.EnqueueAsync/LeaseAsync/AckAsync`
  - `ITenantRepository`、`ITemplateRepository`、`IDeviceRepository`、`IPublishRepository`、`IBotRunQueueRepository`（Core 消费的持久化端口）

- [ ] **Step 1: 写失败测试（边界隔离）**

```csharp
[Fact]
public void PluginApi_MustNotReference_AspNetCore_Or_EfCore()
{
    var asm = typeof(ISandboxProvider).Assembly;
    var refs = asm.GetReferencedAssemblies().Select(x => x.Name).ToArray();
    Assert.DoesNotContain("Microsoft.AspNetCore.Http", refs);
    Assert.DoesNotContain("Microsoft.EntityFrameworkCore", refs);
}
```

- [ ] **Step 2: RED 验证**
Run: `dotnet test dotnet/tests/Ocb.Baas.Provider.Conformance.Tests/Ocb.Baas.Provider.Conformance.Tests.csproj --filter PluginApi_MustNotReference_AspNetCore_Or_EfCore`
Expected: FAIL（接口项目未建立或引用污染）。

- [ ] **Step 3: 最小实现（纯协议）**

```csharp
public interface ISm4CryptoProvider
{
    string Encrypt(string plaintext, string keyId);
    string Decrypt(string ciphertext, string keyId);
}
```

- [ ] **Step 4: GREEN 验证**
Run: `dotnet test dotnet/tests/Ocb.Baas.Provider.Conformance.Tests/Ocb.Baas.Provider.Conformance.Tests.csproj --filter PluginApi_MustNotReference_AspNetCore_Or_EfCore`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.PluginApi/Baas dotnet/tests/Ocb.Baas.Provider.Conformance.Tests/PluginBoundaryTests.cs
git commit -m "feat(baas): add plugin api boundaries for sandbox crypto queue gateway"
```

---

### Task 3: 落地 PostgreSQL `ocb_business` 基线（不做 SQLite parity）

**Files:**

- Create: `dotnet/src/Ocb.Infrastructure.PostgreSql/Baas/OcbBusinessDbContext.cs`
- Create: `dotnet/src/Ocb.Infrastructure.PostgreSql/Baas/Entities/TenantEntity.cs`
- Create: `dotnet/src/Ocb.Infrastructure.PostgreSql/Baas/Entities/TemplateEntity.cs`
- Create: `dotnet/src/Ocb.Infrastructure.PostgreSql/Baas/Entities/DeviceEntity.cs`
- Create: `dotnet/src/Ocb.Infrastructure.PostgreSql/Baas/Entities/PublishEntity.cs`
- Create: `dotnet/src/Ocb.Infrastructure.PostgreSql/Baas/Entities/BotRunQueueEntity.cs`
- Create: `dotnet/src/Ocb.Infrastructure.PostgreSql/Baas/Migrations/20260808_InitialOcbBusiness.cs`
- Test: `dotnet/tests/Ocb.Baas.Core.Tests/PostgreSqlSchemaTests.cs`

**Interfaces:**

- Consumes: `src/baas/sqls/baas_core_tables.sql`、`src/baas/sqls/migrate_bot_run_queue.sql`。
- Produces: Task 2 持久化端口的 EF Core/Npgsql 实现与显式 migration；Infrastructure 不重新定义仓储接口。

- [ ] **Step 1: 写失败测试（Schema 与唯一键）**

```csharp
[Fact]
public async Task Should_Create_Tenant_Template_Device_Publish_Tables_In_OcbBusiness()
{
    await using var db = CreateDbContext();
    var tables = await db.Database.SqlQueryRaw<string>("select tablename from pg_tables where schemaname='ocb_business'").ToListAsync();
    Assert.Contains("baas_tenant", tables);
    Assert.Contains("baas_device_template", tables);
    Assert.Contains("baas_device", tables);
    Assert.Contains("baas_publish", tables);
}
```

- [ ] **Step 2: RED 验证**
Run: `dotnet test dotnet/tests/Ocb.Baas.Core.Tests/Ocb.Baas.Core.Tests.csproj --filter Should_Create_Tenant_Template_Device_Publish_Tables_In_OcbBusiness`
Expected: FAIL，表不存在或 schema 未创建。

- [ ] **Step 3: 最小实现（DbContext + 迁移）**

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.HasDefaultSchema("ocb_business");
    modelBuilder.Entity<TenantEntity>().ToTable("baas_tenant");
}
```

- [ ] **Step 4: GREEN 验证**
Run: `dotnet test dotnet/tests/Ocb.Baas.Core.Tests/Ocb.Baas.Core.Tests.csproj --filter Should_Create_Tenant_Template_Device_Publish_Tables_In_OcbBusiness`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Infrastructure.PostgreSql/Baas dotnet/tests/Ocb.Baas.Core.Tests/PostgreSqlSchemaTests.cs
git commit -m "feat(baas): initialize ocb_business postgres schema for baas domain"
```

---

### Task 4: 完成 tenant/template/device/health 核心服务（含租户隔离）

**Files:**

- Create: `dotnet/src/Ocb.Baas/Core/Tenant/TenantService.cs`
- Create: `dotnet/src/Ocb.Baas/Core/Template/TemplateService.cs`
- Create: `dotnet/src/Ocb.Baas/Core/Device/DeviceService.cs`
- Create: `dotnet/src/Ocb.Baas/Core/Health/HealthService.cs`
- Create: `dotnet/src/Ocb.Baas/Core/Common/TenantGuard.cs`
- Test: `dotnet/tests/Ocb.Baas.Core.Tests/TenantTemplateDeviceHealthServiceTests.cs`

**Interfaces:**

- Consumes: Task 1 合同 + Task 3 仓储。
- Produces:
  - `TenantService : ITenantServiceContract`
  - `TemplateService : ITemplateServiceContract`
  - `DeviceService : IDeviceServiceContract`
  - `HealthService : IHealthServiceContract`

- [ ] **Step 1: 写失败测试（租户隔离）**

```csharp
[Fact]
public async Task GetTemplate_ShouldReturn404_WhenTenantMismatch()
{
    var svc = CreateTemplateService();
    await Assert.ThrowsAsync<ResourceNotFoundException>(() => svc.GetTemplateAsync("tenant-b", "template-of-tenant-a", CancellationToken.None));
}
```

- [ ] **Step 2: RED 验证**
Run: `dotnet test dotnet/tests/Ocb.Baas.Core.Tests/Ocb.Baas.Core.Tests.csproj --filter GetTemplate_ShouldReturn404_WhenTenantMismatch`
Expected: FAIL，当前返回了跨租户数据或异常类型不符。

- [ ] **Step 3: 最小实现（显式 tenant predicate）**

```csharp
public async Task<TemplateDto> GetTemplateAsync(string tenant, string templateUuid, CancellationToken ct)
{
    var entity = await _templateRepository.GetByTenantAndUuidAsync(tenant, templateUuid, ct);
    return entity is null ? throw new ResourceNotFoundException("TEMPLATE_NOT_FOUND") : Map(entity);
}
```

- [ ] **Step 4: GREEN 验证**
Run: `dotnet test dotnet/tests/Ocb.Baas.Core.Tests/Ocb.Baas.Core.Tests.csproj --filter GetTemplate_ShouldReturn404_WhenTenantMismatch`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Baas/Core dotnet/tests/Ocb.Baas.Core.Tests/TenantTemplateDeviceHealthServiceTests.cs
git commit -m "feat(baas): implement tenant template device health services with tenant guard"
```

---

### Task 5: Docker/K8s sandbox Provider 与统一 conformance 套件

**Files:**

- Create: `dotnet/src/Ocb.Plugins.Sandbox.Docker/DockerSandboxProvider.cs`
- Create: `dotnet/src/Ocb.Plugins.Sandbox.Docker/Ocb.Plugins.Sandbox.Docker.csproj`
- Create: `dotnet/src/Ocb.Plugins.Sandbox.Docker/context-boundary.json`
- Create: `dotnet/src/Ocb.Plugins.Sandbox.Docker/DockerSandboxDriver.cs`
- Create: `dotnet/src/Ocb.Plugins.Sandbox.K8s/K8sSandboxProvider.cs`
- Create: `dotnet/src/Ocb.Plugins.Sandbox.K8s/Ocb.Plugins.Sandbox.K8s.csproj`
- Create: `dotnet/src/Ocb.Plugins.Sandbox.K8s/context-boundary.json`
- Create: `dotnet/src/Ocb.Plugins.Sandbox.K8s/K8sSandboxDriver.cs`
- Create: `dotnet/tests/Ocb.Baas.Provider.Conformance.Tests/SandboxProviderContractTests.cs`
- Create: `dotnet/tests/Ocb.Baas.Provider.Conformance.Tests/SandboxProviderDockerTests.cs`
- Create: `dotnet/tests/Ocb.Baas.Provider.Conformance.Tests/SandboxProviderK8sTests.cs`

**Interfaces:**

- Consumes: `ISandboxProvider`, `ISandboxConformanceProvider`。
- Produces: Docker/K8s 对同一测试断言集的通过证据。

- [ ] **Step 1: 写失败测试（同构能力）**

```csharp
[Theory]
[InlineData("docker")]
[InlineData("k8s")]
public async Task Provider_MustSupport_Create_Destroy_InvokeHttp(string profile)
{
    var provider = CreateProvider(profile);
    var sandbox = await provider.CreateAsync(CreateRequest(profile), CancellationToken.None);
    var ping = await provider.InvokeHttpAsync(new SandboxInvokeHttpRequest(sandbox.SandboxId, "GET", 8080, "/health"), CancellationToken.None);
    Assert.Equal(200, ping.StatusCode);
    await provider.DestroyAsync(sandbox.SandboxId, CancellationToken.None);
}
```

- [ ] **Step 2: RED 验证**
Run: `dotnet test dotnet/tests/Ocb.Baas.Provider.Conformance.Tests/Ocb.Baas.Provider.Conformance.Tests.csproj --filter Provider_MustSupport_Create_Destroy_InvokeHttp`
Expected: FAIL，至少一个 provider 未实现能力。

- [ ] **Step 3: 最小实现（双 Provider）**

```csharp
public sealed class DockerSandboxProvider(IDockerSandboxDriver driver) : ISandboxProvider
{
  public Task<SandboxHandle> CreateAsync(SandboxCreateRequest request, CancellationToken ct) => driver.CreateAsync(request, ct);
  public Task DestroyAsync(string sandboxId, CancellationToken ct) => driver.DestroyAsync(sandboxId, ct);
  public Task<SandboxHttpResponse> InvokeHttpAsync(SandboxInvokeHttpRequest request, CancellationToken ct) => driver.InvokeHttpAsync(request, ct);
}

public sealed class K8sSandboxProvider(IK8sSandboxDriver driver) : ISandboxProvider
{
  public Task<SandboxHandle> CreateAsync(SandboxCreateRequest request, CancellationToken ct) => driver.CreateAsync(request, ct);
  public Task DestroyAsync(string sandboxId, CancellationToken ct) => driver.DestroyAsync(sandboxId, ct);
  public Task<SandboxHttpResponse> InvokeHttpAsync(SandboxInvokeHttpRequest request, CancellationToken ct) => driver.InvokeHttpAsync(request, ct);
}
```

- [ ] **Step 4: GREEN 验证**
Run: `dotnet test dotnet/tests/Ocb.Baas.Provider.Conformance.Tests/Ocb.Baas.Provider.Conformance.Tests.csproj --filter Provider_MustSupport_Create_Destroy_InvokeHttp`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Plugins.Sandbox.Docker dotnet/src/Ocb.Plugins.Sandbox.K8s dotnet/tests/Ocb.Baas.Provider.Conformance.Tests/SandboxProvider*.cs
git commit -m "feat(baas): add docker and k8s sandbox providers with shared conformance tests"
```

---

### Task 6: 透明 invoke-http 与 API gateway 路由 parity

**Files:**

- Create: `dotnet/src/Ocb.Baas/Web/Controllers/BotHttpProxyController.cs`
- Create: `dotnet/src/Ocb.Baas/Web/Controllers/GatewayMessageController.cs`
- Create: `dotnet/src/Ocb.Baas/Core/InvokeHttp/InvokeHttpDispatcher.cs`
- Test: `dotnet/tests/Ocb.Baas.Web.Tests/BotHttpProxyControllerTests.cs`
- Test: `dotnet/tests/Ocb.Baas.Web.Tests/GatewayMessageControllerTests.cs`

**Interfaces:**

- Consumes: `IDeviceServiceContract.InvokeHttpAsync`、`IExternalGatewayProxy`。
- Produces:
  - `ANY /api/v1/bots/{tenant}/{bot_uuid}/invoke-http/{port}/{**path}`
  - `POST /openapi/v1/chat`
  - `POST /openapi/v1/chat/stream`

- [ ] **Step 1: 写失败测试（header 过滤 + 错误映射）**

```csharp
[Fact]
public async Task InvokeHttp_ShouldStripHopByHopHeaders_And_Map_NoActiveDevices_To503()
{
    var response = await _client.SendAsync(BuildInvokeRequestWithConnectionHeader());
    Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    Assert.DoesNotContain("connection", _capturedForwardHeaders.Keys, StringComparer.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: RED 验证**
Run: `dotnet test dotnet/tests/Ocb.Baas.Web.Tests/Ocb.Baas.Web.Tests.csproj --filter InvokeHttp_ShouldStripHopByHopHeaders_And_Map_NoActiveDevices_To503`
Expected: FAIL，状态码或 header 行为不符。

- [ ] **Step 3: 最小实现（透明转发）**

```csharp
[HttpGet, HttpPost, HttpPut, HttpDelete("/api/v1/bots/{tenant}/{bot_uuid}/invoke-http/{port:int}/{**path}")]
public async Task<IActionResult> InvokeHttp(
  string tenant,
  [FromRoute(Name = "bot_uuid")] string botUuid,
  int port,
  string path,
  CancellationToken ct)
```

- [ ] **Step 4: GREEN 验证**
Run: `dotnet test dotnet/tests/Ocb.Baas.Web.Tests/Ocb.Baas.Web.Tests.csproj --filter InvokeHttp_ShouldStripHopByHopHeaders_And_Map_NoActiveDevices_To503`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Baas/Web/Controllers dotnet/src/Ocb.Baas/Core/InvokeHttp dotnet/tests/Ocb.Baas.Web.Tests/BotHttpProxyControllerTests.cs
git commit -m "feat(baas): implement transparent invoke-http and gateway parity routes"
```

---

### Task 7: 发布流程与运行队列（Bot 发布/运行队列）

**Files:**

- Create: `dotnet/src/Ocb.Baas/Core/Publish/PublishService.cs`
- Create: `dotnet/src/Ocb.Baas/Core/RunQueue/BotRunQueueDispatcher.cs`
- Create: `dotnet/src/Ocb.Plugins.Queue.PostgreSql/PostgreSqlBotRunQueueProvider.cs`
- Create: `dotnet/src/Ocb.Plugins.Queue.PostgreSql/Ocb.Plugins.Queue.PostgreSql.csproj`
- Create: `dotnet/src/Ocb.Plugins.Queue.PostgreSql/context-boundary.json`
- Create: `dotnet/src/Ocb.Baas/Web/Controllers/PublishController.cs`
- Test: `dotnet/tests/Ocb.Baas.Core.Tests/PublishAndRunQueueTests.cs`
- Test: `dotnet/tests/Ocb.Baas.Web.Tests/PublishControllerTests.cs`

**Interfaces:**

- Consumes: `IPublishServiceContract`, `IBotRunQueueProvider`。
- Produces:
  - 发布接口：create/get/progress/approve/reject/revoke/retry/execute/complete
  - 运行队列：enqueue + lease + ack + dead-letter。

- [ ] **Step 1: 写失败测试（状态机 + 幂等）**

```csharp
[Fact]
public async Task Approve_ShouldTransition_Pending_To_Active_And_Reject_InvalidTransitions()
{
    var publish = await _service.CreatePublishAsync(CreateRequest());
    var approved = await _service.ApproveStageAsync(publish.Id, "operator-a", CancellationToken.None);
    Assert.Equal(PublishStatus.Active, approved.Status);
    await Assert.ThrowsAsync<DomainConflictException>(() => _service.ApproveStageAsync(publish.Id, "operator-a", CancellationToken.None));
}
```

- [ ] **Step 2: RED 验证**
Run: `dotnet test dotnet/tests/Ocb.Baas.Core.Tests/Ocb.Baas.Core.Tests.csproj --filter Approve_ShouldTransition_Pending_To_Active_And_Reject_InvalidTransitions`
Expected: FAIL，状态机未实现或幂等冲突处理不正确。

- [ ] **Step 3: 最小实现（状态机 + 队列入列）**

```csharp
public Task<PublishDto> ApproveStageAsync(long publishId, string operatorId, CancellationToken ct);
public Task EnqueueAsync(BotRunQueueItem item, CancellationToken ct);
```

- [ ] **Step 4: GREEN 验证**
Run: `dotnet test dotnet/tests/Ocb.Baas.Core.Tests/Ocb.Baas.Core.Tests.csproj --filter Approve_ShouldTransition_Pending_To_Active_And_Reject_InvalidTransitions`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Baas/Core/Publish dotnet/src/Ocb.Baas/Core/RunQueue dotnet/src/Ocb.Plugins.Queue.PostgreSql dotnet/src/Ocb.Baas/Web/Controllers/PublishController.cs dotnet/tests/Ocb.Baas.Core.Tests/PublishAndRunQueueTests.cs
git commit -m "feat(baas): add publish lifecycle state machine and postgres run queue"
```

---

### Task 8: QPM、TTL、quota/usage billing control（无收费产品臆造）

**Files:**

- Create: `dotnet/src/Ocb.Baas/Core/Qpm/QpmService.cs`
- Create: `dotnet/src/Ocb.Baas/Core/Ttl/DeviceTtlRenewalJob.cs`
- Create: `dotnet/src/Ocb.Baas/Core/Billing/BillingQuotaGuard.cs`
- Create: `dotnet/src/Ocb.Baas/Web/Controllers/QpmController.cs`
- Test: `dotnet/tests/Ocb.Baas.Core.Tests/QpmTtlBillingGuardTests.cs`

**Interfaces:**

- Consumes: `IQpmServiceContract`, `IBotRunQueueProvider`, `ISandboxProvider`。
- Produces:
  - QPM CRUD
  - TTL 续期任务
  - UsageCounter + QuotaDecision（仅控制边界，不实现支付）。

- [ ] **Step 1: 写失败测试（超配额拒绝）**

```csharp
[Fact]
public async Task BillingQuotaGuard_ShouldReject_WhenUsageExceedsQuota()
{
    var guard = CreateGuard(limitPerMinute: 10, currentUsage: 11);
    var result = await guard.DecideAsync("tenant-a", "bot-1", CancellationToken.None);
    Assert.False(result.Allowed);
    Assert.Equal("QUOTA_EXCEEDED", result.Code);
}
```

- [ ] **Step 2: RED 验证**
Run: `dotnet test dotnet/tests/Ocb.Baas.Core.Tests/Ocb.Baas.Core.Tests.csproj --filter BillingQuotaGuard_ShouldReject_WhenUsageExceedsQuota`
Expected: FAIL。

- [ ] **Step 3: 最小实现（边界控制）**

```csharp
public sealed record QuotaDecision(bool Allowed, string Code, int Remaining);
public Task<QuotaDecision> DecideAsync(string tenant, string botId, CancellationToken ct);
```

- [ ] **Step 4: GREEN 验证**
Run: `dotnet test dotnet/tests/Ocb.Baas.Core.Tests/Ocb.Baas.Core.Tests.csproj --filter BillingQuotaGuard_ShouldReject_WhenUsageExceedsQuota`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Baas/Core/Qpm dotnet/src/Ocb.Baas/Core/Ttl dotnet/src/Ocb.Baas/Core/Billing dotnet/src/Ocb.Baas/Web/Controllers/QpmController.cs dotnet/tests/Ocb.Baas.Core.Tests/QpmTtlBillingGuardTests.cs
git commit -m "feat(baas): add qpm ttl and quota usage billing guard"
```

---

### Task 9: SSE 流式输出与心跳/背压控制

**Files:**

- Create: `dotnet/src/Ocb.Baas/Core/Sse/SseEventConverter.cs`
- Create: `dotnet/src/Ocb.Baas/Web/Controllers/OpenApiStreamController.cs`
- Create: `dotnet/src/Ocb.Baas/Web/Sse/SseHeartbeatMiddleware.cs`
- Test: `dotnet/tests/Ocb.Baas.Web.Tests/SseStreamTests.cs`

**Interfaces:**

- Consumes: `SseContracts` + `IPublishServiceContract`/`IBotRunQueueProvider` chunk 流。
- Produces: `POST /openapi/v1/chat/stream` 返回 `text/event-stream`，ready/data/error/heartbeat 序列。

- [ ] **Step 1: 写失败测试（SSE 协议头 + ready 事件）**

```csharp
[Fact]
public async Task StreamEndpoint_ShouldReturn_TextEventStream_And_ReadyEventFirst()
{
    var response = await _client.PostAsJsonAsync("/openapi/v1/chat/stream", new { bot_id = "bot-1", message = "hello" });
    Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
    var firstFrame = await ReadFirstFrameAsync(response);
    Assert.Contains("event: ready", firstFrame);
}
```

- [ ] **Step 2: RED 验证**
Run: `dotnet test dotnet/tests/Ocb.Baas.Web.Tests/Ocb.Baas.Web.Tests.csproj --filter StreamEndpoint_ShouldReturn_TextEventStream_And_ReadyEventFirst`
Expected: FAIL。

- [ ] **Step 3: 最小实现（`IAsyncEnumerable<SseEvent>`）**

```csharp
public IAsyncEnumerable<SseEvent> ConvertAsync(IAsyncEnumerable<StreamChunk> chunks, string runId, CancellationToken ct);
```

- [ ] **Step 4: GREEN 验证**
Run: `dotnet test dotnet/tests/Ocb.Baas.Web.Tests/Ocb.Baas.Web.Tests.csproj --filter StreamEndpoint_ShouldReturn_TextEventStream_And_ReadyEventFirst`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Baas/Core/Sse dotnet/src/Ocb.Baas/Web/Controllers/OpenApiStreamController.cs dotnet/src/Ocb.Baas/Web/Sse dotnet/tests/Ocb.Baas.Web.Tests/SseStreamTests.cs
git commit -m "feat(baas): add sse stream conversion heartbeat and backpressure handling"
```

---

### Task 10: SM4 Provider 与 5 类安全测试（SM2 排除）

**Files:**

- Create: `dotnet/src/Ocb.Plugins.Crypto.SM4/Sm4CryptoProvider.cs`
- Create: `dotnet/src/Ocb.Plugins.Crypto.SM4/Sm4Padding.cs`
- Create: `dotnet/src/Ocb.Plugins.Crypto.SM4/Ocb.Plugins.Crypto.SM4.csproj`
- Create: `dotnet/src/Ocb.Plugins.Crypto.SM4/context-boundary.json`
- Test: `dotnet/tests/Ocb.Baas.Provider.Conformance.Tests/Sm4ConformanceTests.cs`
- Modify: `dotnet/src/Ocb.Baas/Core/Device/DeviceHeaderEncryptionService.cs`

**Interfaces:**

- Consumes: `ISm4CryptoProvider`、`ISm4KeyResolver`；key material 只经 Secret Plugin 解析，不进入请求 DTO、日志或 Grain State。
- Produces: `Encrypt(string plaintext, string keyId)` / `Decrypt(string ciphertext, string keyId)`；wire envelope 固定为 version + IV + ciphertext + HMAC tag。

- [ ] **Step 1: 写失败测试（known-answer + tamper）**

```csharp
[Fact]
public void Sm4_ShouldFail_OnTamperedCiphertext()
{
    var provider = CreateSm4Provider();
    var cipher = provider.Encrypt("Bearer secret", "k1");
    var bytes = Convert.FromBase64String(cipher);
    bytes[^1] ^= 0x01;
    var tampered = Convert.ToBase64String(bytes);
    Assert.Throws<CryptographicException>(() => provider.Decrypt(tampered, "k1"));
}
```

- [ ] **Step 2: RED 验证**
Run: `dotnet test dotnet/tests/Ocb.Baas.Provider.Conformance.Tests/Ocb.Baas.Provider.Conformance.Tests.csproj --filter Sm4_ShouldFail_OnTamperedCiphertext`
Expected: FAIL。

- [ ] **Step 3: 最小实现（SM4-CBC + PKCS7 + Encrypt-then-MAC）**

```csharp
public string Encrypt(string plaintext, string keyId)
{
  var keys = _keys.Resolve(keyId);
  var envelope = Sm4Cbc.EncryptPkcs7(plaintext, keys.EncryptionKey);
  return envelope.WithTag(HMACSHA256.HashData(keys.AuthenticationKey, envelope.AuthenticatedBytes)).ToBase64();
}

public string Decrypt(string ciphertext, string keyId)
{
  var envelope = Sm4Envelope.Parse(ciphertext);
  var keys = _keys.Resolve(keyId);
  if (!CryptographicOperations.FixedTimeEquals(envelope.Tag,
    HMACSHA256.HashData(keys.AuthenticationKey, envelope.AuthenticatedBytes)))
    throw new CryptographicException("SM4 envelope authentication failed");
  return Sm4Cbc.DecryptPkcs7(envelope, keys.EncryptionKey);
}
```

- [ ] **Step 4: GREEN 验证**
Run: `dotnet test dotnet/tests/Ocb.Baas.Provider.Conformance.Tests/Ocb.Baas.Provider.Conformance.Tests.csproj --filter Sm4`
Expected: PASS，覆盖 known-answer/roundtrip/invalid-key/padding/tamper。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Plugins.Crypto.SM4 dotnet/src/Ocb.Baas/Core/Device/DeviceHeaderEncryptionService.cs dotnet/tests/Ocb.Baas.Provider.Conformance.Tests/Sm4ConformanceTests.cs
git commit -m "feat(baas): implement sm4 provider with known-answer and tamper tests"
```

---

### Task 11: Third-party boundary 防腐层与 Backend/Fusion 边界验证

**Files:**

- Create: `dotnet/src/Ocb.Baas/Boundary/ExternalDependencyGuard.cs`
- Create: `dotnet/tests/Ocb.Baas.Core.Tests/BoundaryIsolationTests.cs`
- Create: `dotnet/tests/Ocb.Architecture.Tests/BaasBoundaryRulesTests.cs`

**Interfaces:**

- Consumes: `Ocb.Contracts`、`Ocb.PluginApi`。
- Produces:
  - BaaS 只通过 Plugin API 访问 Docker/K8s/Crypto/Queue。
  - BaaS 不直接引用 `Ocb.Backend`、`Ocb.Fusion` 实现程序集。

- [ ] **Step 1: 写失败测试（引用隔离）**

```csharp
[Fact]
public void OcbBaas_MustNotReference_Backend_Or_Fusion_Implementations()
{
    var refs = typeof(Ocb.Baas.Core.Marker).Assembly.GetReferencedAssemblies().Select(x => x.Name).ToArray();
    Assert.DoesNotContain("Ocb.Backend", refs);
    Assert.DoesNotContain("Ocb.Fusion", refs);
}
```

- [ ] **Step 2: RED 验证**
Run: `dotnet test dotnet/tests/Ocb.Architecture.Tests/Ocb.Architecture.Tests.csproj --filter OcbBaas_MustNotReference_Backend_Or_Fusion_Implementations`
Expected: FAIL（若出现越界引用）。

- [ ] **Step 3: 最小实现（装配点收口）**

```csharp
public static IServiceCollection AddBaasCore(this IServiceCollection services)
{
    services.TryAddScoped<IDeviceServiceContract, DeviceService>();
    return services;
}
```

- [ ] **Step 4: GREEN 验证**
Run: `dotnet test dotnet/tests/Ocb.Architecture.Tests/Ocb.Architecture.Tests.csproj --filter OcbBaas_MustNotReference_Backend_Or_Fusion_Implementations`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Baas/Boundary dotnet/tests/Ocb.Baas.Core.Tests/BoundaryIsolationTests.cs dotnet/tests/Ocb.Architecture.Tests/BaasBoundaryRulesTests.cs
git commit -m "test(baas): enforce third-party and backend fusion boundary isolation"
```

---

### Task 12: BaaS HTTP parity + 生命周期 E2E + sandbox conformance 回归

**Files:**

- Create: `dotnet/tests/Ocb.Baas.EndToEnd.Tests/Parity/BaasOpenApiParityTests.cs`
- Create: `dotnet/tests/Ocb.Baas.EndToEnd.Tests/Lifecycle/BotRunLifecycleTests.cs`
- Create: `dotnet/tests/Ocb.Baas.EndToEnd.Tests/Lifecycle/PublishLifecycleTests.cs`
- Create: `dotnet/tests/Ocb.Baas.EndToEnd.Tests/Conformance/SandboxProfileConformanceTests.cs`
- Create: `dotnet/tests/Ocb.Baas.EndToEnd.Tests/TestData/baas-parity-allowlist.json`

**Interfaces:**

- Consumes:
  - `dotnet/contracts/parity-corpus/baas.openapi.json`
  - Python 侧参考：`src/baas/tests/e2e/asgi/baseline/*`, `src/baas/tests/e2e/asgi/bot_run_lifecycle/*`。
- Produces:
  - HTTP parity 报告
  - 生命周期 E2E 报告
  - singlebox/docker 与 cluster/k8s profile conformance 报告。

- [ ] **Step 1: 写失败测试（openapi 路径 parity）**

```csharp
[Fact]
public async Task OpenApi_ShouldContain_AllBaasParityPaths()
{
    var expected = await LoadExpectedPathsAsync("dotnet/contracts/parity-corpus/baas.openapi.json");
    var actual = await FetchCurrentOpenApiPathsAsync();
    Assert.Subset(expected, actual);
}
```

- [ ] **Step 2: RED 验证**
Run: `dotnet test dotnet/tests/Ocb.Baas.EndToEnd.Tests/Ocb.Baas.EndToEnd.Tests.csproj --filter OpenApi_ShouldContain_AllBaasParityPaths`
Expected: FAIL，缺少 path+HTTP method、响应码或必需 schema 字段；比较器必须验证 `expected ⊆ actual`，不得只比较路径字符串。

- [ ] **Step 3: 最小实现（补路由 + 生命周期流程）**

```csharp
// Program.cs
app.MapControllers();
app.MapBaasLifecycleEndpoints();
```

- [ ] **Step 4: GREEN 验证**
Run:
- `dotnet test dotnet/tests/Ocb.Baas.EndToEnd.Tests/Ocb.Baas.EndToEnd.Tests.csproj --filter OpenApi_ShouldContain_AllBaasParityPaths`
- `dotnet test dotnet/tests/Ocb.Baas.EndToEnd.Tests/Ocb.Baas.EndToEnd.Tests.csproj --filter BotRunLifecycle`
- `dotnet test dotnet/tests/Ocb.Baas.EndToEnd.Tests/Ocb.Baas.EndToEnd.Tests.csproj --filter SandboxProfileConformance`
Expected: 全部 PASS。

- [ ] **Step 5: Commit**

```bash
git add dotnet/tests/Ocb.Baas.EndToEnd.Tests
git commit -m "test(baas): add http parity and lifecycle e2e with sandbox profile conformance"
```

---

## 自审（Spec Coverage）

- health、Device、Template、tenant：Task 1、Task 4、Task 12 覆盖。
- Docker/K8s sandbox conformance：Task 5、Task 12 覆盖。
- 透明 invoke-http/API gateway：Task 6 覆盖。
- Bot 发布/运行队列：Task 7 覆盖。
- QPM、TTL、billing control：Task 8 覆盖（billing 仅 quota/usage 边界，不扩展收费产品）。
- 第三方边界：Task 2、Task 11 覆盖。
- SSE：Task 9 覆盖。
- PostgreSQL `ocb_business`：Task 3 覆盖。
- SM4 known-answer/roundtrip/invalid-key/padding/tamper：Task 10 覆盖。
- BaaS HTTP parity 与生命周期 E2E：Task 12 覆盖。
- SM2 排除：Global Constraints 明确排除。
- 不做 SQLite parity，采用 Postgres 新系统：Global Constraints + Task 3 明确。
- 与 Backend/Fusion 边界清晰：Global Constraints + Task 11 明确。

## 自审（No-placeholder Check）

- 已按 writing-plans 禁止模式完成扫描，实际实施步骤中无占位内容。
- 每个任务均包含失败测试断言、RED/GREEN 命令、最小实现签名、Commit。

## 自审（Type/Interface Consistency）

- Service contract 统一由 `Ocb.Contracts/Baas/*` 输出，核心实现在 `Ocb.Baas/Core/*`。
- Provider contract 统一由 `Ocb.PluginApi/Baas/*` 输出，插件实现在 `Ocb.Plugins.*`。
- 路由层仅依赖 Service contract，不越过到 provider 细节。
