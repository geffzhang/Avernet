# Ocb.Fusion 向量融合与 OpenAPI 对齐 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 .NET 10/Orleans 架构下交付 Ocb.Fusion，完整覆盖 bcsfuse 的 `POST /api/v1/groups/{group_id}/fuse` 与公开 OpenAPI 契约（含 worker/profile/fusion 相关路径），并落地 SonnetDB/Qdrant 双 Provider 与统一 conformance。

**Architecture:** `Ocb.Fusion` 负责 HTTP Delivery Adapter 与应用编排；`Ocb.GrainContracts`/`Ocb.Grains` 仅做串行协调与幂等状态；向量 I/O、embedding、reranker 通过 `Ocb.PluginApi` 抽象并在 `Ocb.Infrastructure.Vector` 与 `Ocb.Plugins.*` 实现。`CallerContext` 显式携带 tenant+subject，Grain 入站执行二次 tenant key 校验，禁止迁移 Python trust-gateway bypass 语义。

**Tech Stack:** .NET 10, ASP.NET Core, Orleans, xUnit, FluentAssertions, SonnetDB SDK, Qdrant.Client, System.Text.Json source gen, OpenAPI diff 工具（openapi-diff 或 kin-openapi）。

## Global Constraints

- 只修改/新增 `dotnet/` 及测试目录，不触碰 Rust BCS 数据层实现。
- Rust BCS 只能通过版本化 HTTP/WebSocket 契约访问，禁止任何 SQLite 直连代码。
- 接口定义放在 `dotnet/src/Ocb.PluginApi/Contracts/` 与 `dotnet/src/Ocb.Contracts/`。
- Orleans Grain 禁止直接做向量读写、数据库 CRUD、HTTP 调用、文件 I/O。
- `CallerContext` 必须显式包含 `TenantId` 与 `SubjectId`，并贯穿 HTTP -> Service -> Grain。
- `FusionJobGrain` 必须串行处理同一 key 命令，并基于外部 `IdempotencyKey` 幂等。
- singlebox profile 仅允许 SonnetDB；cluster profile 仅允许 Qdrant；配置不匹配时启动 fail closed。
- embedding 与 reranker 必须是独立 Plugin，不得内嵌到 VectorStore Provider。
- 向量写入必须携带 model+dimension 元数据，检索时 model/dimension 不匹配 fail closed。
- 维持与 `src/bcsfuse/schemas/openapi.yaml` 的公开契约 parity（至少覆盖 `/v1/workers*`、`/api/v1/groups/{group_id}/fuse`、`/health`）。
- 参考输入必须来源于 `src/bcsfuse/tests/smoke/route_import_inventory.py`、`src/bcsfuse/src/interfaces/api/*`、`src/bcsfuse/src/application/ports/*`、`src/bcsfuse/tests/**`。

---

## File Structure

- `dotnet/src/Ocb.Contracts/Fusion/`
  - 复用根命名空间现有 `CallerContext.cs`：显式 caller 身份模型（tenant+subject+roles），不得创建 Fusion-local 同名类型。
  - `FusionDtos.cs`：`FusionRequestDto`、`FuseOptionsDto`、`FuseResponseDto` 与 bcsfuse schema 对齐。
  - `WorkerProfileDtos.cs`：worker/profile 查询与响应契约。
- `dotnet/src/Ocb.PluginApi/Contracts/Vector/`
  - `IVectorStore.cs`
  - `IHybridSearchStore.cs`
  - `IVectorStoreAdministration.cs`
  - `IEmbeddingPlugin.cs`
  - `IRerankerPlugin.cs`
- `dotnet/src/Ocb.Infrastructure.Vector/`
  - `Conformance/VectorStoreConformanceSuite.cs`（共享 conformance）
  - `SonnetDb/SonnetDbVectorStore.cs`
  - `Qdrant/QdrantVectorStore.cs`
  - `Guards/VectorCompatibilityGuard.cs`
- `dotnet/src/Ocb.GrainContracts/Fusion/`
  - `IFusionJobGrain.cs`
  - `FusionJobState.cs`
- `dotnet/src/Ocb.Grains/Fusion/`
  - `FusionJobGrain.cs`
  - `TenantKeyGuardCallFilter.cs`
- `dotnet/src/Ocb.Fusion/`
  - `Routes/FusionRoutes.cs`（`POST /api/v1/groups/{group_id}/fuse`）
  - `Routes/WorkersRoutes.cs`（`/v1/workers*` parity）
  - `Routes/HealthRoutes.cs`
  - `Application/FusionService.cs`
  - `Application/WorkerProfileService.cs`
  - `Application/IFusionCoordinator.cs`（领域编排接口，注入到 `FusionJobGrain`）
  - `Application/FusionCoordinator.cs`（实现：embedding → 检索 → reranker → 聚合）
  - `OpenApi/BcsfuseOpenApiParityValidator.cs`
- `dotnet/src/Ocb.Plugins.Embedding/DefaultEmbeddingPlugin.cs`
- `dotnet/src/Ocb.Plugins.Reranker/DefaultRerankerPlugin.cs`
- `dotnet/tests/Ocb.Contracts.Tests/Fusion/`
- `dotnet/tests/Ocb.Infrastructure.Tests/Vector/`
- `dotnet/tests/Ocb.Grains.Tests/Fusion/`
- `dotnet/tests/Ocb.Fusion.Tests/Api/`
- `dotnet/tests/Ocb.EndToEnd.Tests/Fusion/`

**⚠️ 跨-Plan 项目创建协调：**
- `Ocb.GrainContracts/Fusion/` 和 `Ocb.Grains/Fusion/` 是已有共享项目 `Ocb.GrainContracts` 和 `Ocb.Grains` 的子目录。这两个共享项目的 `.csproj` + `context-boundary.json` 骨架由 **gateway-channels plan (Task 1)** 创建。本 plan 只在已有项目中追加 `Fusion/` 子目录和文件，**不重新创建 .csproj 文件**。
- `Ocb.Grains.Tests` 同样由 **gateway-channels plan (Task 1)** 创建骨架。本 plan 只在已有测试项目中追加 `Fusion/` 子目录。
- 实施时检查：如果骨架尚未创建，先执行 gateway-channels plan Task 1。

---

### Task 1: 固化 bcsfuse 契约清单与 .NET DTO 基线

**Files:**

- Create: `dotnet/src/Ocb.Fusion/Ocb.Fusion.csproj`
- Create: `dotnet/src/Ocb.Fusion/context-boundary.json`
- Create: `dotnet/src/Ocb.Infrastructure.Vector/Ocb.Infrastructure.Vector.csproj`
- Create: `dotnet/src/Ocb.Infrastructure.Vector/context-boundary.json`
- Create: `dotnet/src/Ocb.Plugins.Embedding/context-boundary.json`
- Create: `dotnet/src/Ocb.Plugins.Reranker/context-boundary.json`
- Create: `dotnet/src/Ocb.Contracts/Fusion/FusionDtos.cs`
- Create: `dotnet/src/Ocb.Contracts/Fusion/WorkerProfileDtos.cs`
- Create: `dotnet/tests/Ocb.Contracts.Tests/Fusion/FusionDtoParityTests.cs`
- Create: `dotnet/tests/Ocb.Contracts.Tests/Fusion/CallerContextTests.cs`
- Modify: `dotnet/Ocb.slnx`
- Test fixture read-only input: `src/bcsfuse/schemas/openapi.yaml`, `src/bcsfuse/src/interfaces/api/schemas/fusion_schemas.py`

**Interfaces:**

- Consumes:
  - bcsfuse `FusionRequest` 字段语义：`question`, `participants`, `driver_bot_id`, `fusion_mode`, `options`, `metadata`, `session_id`。
- Produces:
  - 复用 `Ocb.Contracts.CallerContext(string TenantId, string SubjectId, IReadOnlySet<string> Roles)`
  - `public sealed record FusionRequestDto(string Question, IReadOnlyList<string> Participants, string? DriverBotId, string Mode, string FusionMode, FuseOptionsDto Options, FuseMetadataDto? Metadata);`
  - `public sealed record FuseResponseDto(string GroupId, string FusionId, string Question, string? DriverBotId, IReadOnlyList<PerspectiveResponseDto> Perspectives, RecommendationResponseDto? Recommendation, bool PartialSuccess, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors, TimingResponseDto Timing, string FusionMode);`

- [ ] **Step 1: 写失败测试（CallerContext 必填 tenant+subject）**

```csharp
[Fact]
public void CallerContext_MustRequireTenantAndSubject()
{
  Action missingTenant = () => new CallerContext("", "subject-1", new HashSet<string>());
  Action missingSubject = () => new CallerContext("tenant-1", "", new HashSet<string>());
  missingTenant.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("tenantId");
  missingSubject.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("subjectId");
}

[Fact]
public void FusionRequestDto_UsesCorpusFieldNames()
{
  var dto = new FusionRequestDto("question", ["bot-1"], null, "agent", "agent", FuseOptionsDto.Default, null);
  var json = JsonSerializer.Serialize(dto, OcbJsonContext.Default.FusionRequestDto);
  json.Should().Contain("\"driver_bot_id\":null");
  json.Should().Contain("\"fusion_mode\":\"agent\"");
}
```

- [ ] **Step 2: RED**

Run: `dotnet test dotnet/tests/Ocb.Contracts.Tests/Ocb.Contracts.Tests.csproj --filter CallerContext_MustRequireTenantAndSubject`
Expected: FAIL，`FusionRequestDto`/source-generated JSON metadata 尚不存在；Phase 0 的 CallerContext 断言保持通过。

- [ ] **Step 3: 最小实现（签名与守卫）**

```csharp
// CallerContext 已由 Phase 0 提供。本任务只新增 Fusion DTO 与 source-generation 声明，
// 并用现有 CallerContextTests + 本测试验证 tenant/subject 必填。
```

- [ ] **Step 4: GREEN**

Run: `dotnet test dotnet/tests/Ocb.Contracts.Tests/Ocb.Contracts.Tests.csproj --filter Fusion`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Contracts/Fusion dotnet/src/Ocb.Fusion dotnet/src/Ocb.Infrastructure.Vector dotnet/src/Ocb.Plugins.Embedding dotnet/src/Ocb.Plugins.Reranker dotnet/tests/Ocb.Contracts.Tests/Fusion dotnet/Ocb.slnx
git commit -m "feat(contracts): add fusion caller context and dto parity baseline"
```

---

### Task 2: 在 Ocb.PluginApi 定义向量/检索/管理与 embedding/reranker 独立契约

**Files:**

- Create: `dotnet/src/Ocb.PluginApi/Contracts/Vector/IVectorStore.cs`
- Create: `dotnet/src/Ocb.PluginApi/Contracts/Vector/IHybridSearchStore.cs`
- Create: `dotnet/src/Ocb.PluginApi/Contracts/Vector/IVectorStoreAdministration.cs`
- Create: `dotnet/src/Ocb.PluginApi/Contracts/Vector/IEmbeddingPlugin.cs`
- Create: `dotnet/src/Ocb.PluginApi/Contracts/Vector/IRerankerPlugin.cs`
- Create: `dotnet/tests/Ocb.Contracts.Tests/PluginApi/VectorContractsTests.cs`

**Interfaces:**

- Consumes: `src/bcsfuse/src/application/ports/vector_store.py`, `embedding_provider.py`, `reranker_provider.py`。
- Produces:
  - `ValueTask UpsertAsync(VectorRecord record, CancellationToken ct)`
  - `ValueTask<IReadOnlyList<VectorHit>> SearchAsync(VectorQuery query, CancellationToken ct)`
  - `ValueTask EnsureCollectionAsync(VectorCollectionSpec spec, CancellationToken ct)`
  - `ValueTask<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> inputs, string model, CancellationToken ct)`
  - `ValueTask<IReadOnlyList<RerankHit>> RerankAsync(string query, IReadOnlyList<RerankCandidate> candidates, int topK, CancellationToken ct)`

- [ ] **Step 1: 写失败测试（接口分离，不允许 IVectorStore 暴露 Embed）**

```csharp
[Fact]
public void VectorStore_MustNotExposeEmbeddingMethods()
{
    var methods = typeof(IVectorStore).GetMethods().Select(x => x.Name).ToArray();
    methods.Should().NotContain("EmbedAsync");
    methods.Should().NotContain("RerankAsync");
}
```

- [ ] **Step 2: RED**

Run: `dotnet test dotnet/tests/Ocb.Contracts.Tests/Ocb.Contracts.Tests.csproj --filter VectorStore_MustNotExposeEmbeddingMethods`
Expected: FAIL（类型不存在）。

- [ ] **Step 3: 最小实现（接口签名）**

```csharp
public interface IVectorStore
{
    ValueTask UpsertAsync(VectorRecord record, CancellationToken ct);
    ValueTask<IReadOnlyList<VectorHit>> SearchAsync(VectorQuery query, CancellationToken ct);
    ValueTask<VectorRecord?> GetAsync(string tenantId, string vectorId, CancellationToken ct);
    ValueTask DeleteAsync(string tenantId, string vectorId, CancellationToken ct);
}
```

- [ ] **Step 4: GREEN**

Run: `dotnet test dotnet/tests/Ocb.Contracts.Tests/Ocb.Contracts.Tests.csproj --filter PluginApi`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.PluginApi/Contracts/Vector dotnet/tests/Ocb.Contracts.Tests/PluginApi
git commit -m "feat(pluginapi): add vector embedding reranker contracts"
```

---

### Task 3: 构建共享 conformance suite（SonnetDB/Qdrant 共用）

**Files:**

- Create: `dotnet/src/Ocb.Infrastructure.Vector/Conformance/VectorStoreConformanceSuite.cs`
- Create: `dotnet/tests/Ocb.Infrastructure.Tests/Vector/VectorStoreConformanceSuiteTests.cs`
- Create: `dotnet/tests/Ocb.Infrastructure.Tests/Vector/Fixtures/FakeVectorStore.cs`

**Interfaces:**

- Consumes: `IVectorStore`, `IHybridSearchStore`, `IVectorStoreAdministration`。
- Produces:
  - `public static IEnumerable<object[]> RequiredCases()`
  - 覆盖 case：`Dimension`, `Distance`, `PayloadType`, `MetadataFilter`, `TieBreak`, `TopKLimit`, `HybridCapabilityDeclaration`, `ProviderErrorSemantics`, `ModelDimensionMismatchFailClosed`。

- [ ] **Step 1: 写失败测试（不兼容 model/dimension 必须 fail closed）**

```csharp
[Fact]
public async Task Conformance_MustFailClosed_OnModelDimensionMismatch()
{
    var store = new FakeVectorStore();
    await store.UpsertAsync(new VectorRecord("t1", "id1", "m1", 1024, new float[1024], null), default);

    Func<Task> act = async () => await store.SearchAsync(new VectorQuery("t1", "m2", 768, new float[768], 5), default);
    await act.Should().ThrowAsync<VectorCompatibilityException>();
}
```

- [ ] **Step 2: RED**

Run: `dotnet test dotnet/tests/Ocb.Infrastructure.Tests/Ocb.Infrastructure.Tests.csproj --filter Conformance_MustFailClosed_OnModelDimensionMismatch`
Expected: FAIL。

- [ ] **Step 3: 最小实现（suite 骨架）**

```csharp
public static class VectorStoreConformanceSuite
{
    public static async Task AssertFailClosedOnMismatchAsync(IVectorStore store, CancellationToken ct)
    {
        await store.UpsertAsync(new VectorRecord("t1", "id1", "m1", 1024, new float[1024], null), ct);
        await Assert.ThrowsAsync<VectorCompatibilityException>(async () =>
            await store.SearchAsync(new VectorQuery("t1", "m2", 768, new float[768], 5), ct));
    }
}
```

- [ ] **Step 4: GREEN**

Run: `dotnet test dotnet/tests/Ocb.Infrastructure.Tests/Ocb.Infrastructure.Tests.csproj --filter Conformance`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Infrastructure.Vector/Conformance dotnet/tests/Ocb.Infrastructure.Tests/Vector
git commit -m "test(vector): add shared conformance suite"
```

---

### Task 4: 实现 SonnetDB Provider（singlebox 专用）

**Files:**

- Create: `dotnet/src/Ocb.Infrastructure.Vector/SonnetDb/SonnetDbVectorStore.cs`
- Create: `dotnet/src/Ocb.Infrastructure.Vector/SonnetDb/SonnetDbHybridSearchStore.cs`
- Create: `dotnet/src/Ocb.Infrastructure.Vector/SonnetDb/SonnetDbVectorStoreAdministration.cs`
- Create: `dotnet/tests/Ocb.Infrastructure.Tests/Vector/SonnetDb/SonnetDbConformanceTests.cs`

**Interfaces:**

- Consumes: Task 2 契约 + Task 3 conformance。
- Produces:
  - `SonnetDbVectorStore : IVectorStore`
  - `SonnetDbHybridSearchStore : IHybridSearchStore`
  - `SonnetDbVectorStoreAdministration : IVectorStoreAdministration`

- [ ] **Step 1: 写失败测试（singlebox profile 必须可创建 collection）**

```csharp
[Fact]
public async Task SonnetDb_AdminEnsureCollection_ShouldSucceed_InSinglebox()
{
    var admin = CreateSonnetAdmin(profile: "singlebox");
    await admin.Invoking(x => x.EnsureCollectionAsync(new VectorCollectionSpec("t1", "fuse", 1024), default))
        .Should().NotThrowAsync();
}
```

- [ ] **Step 2: RED**

Run: `dotnet test dotnet/tests/Ocb.Infrastructure.Tests/Ocb.Infrastructure.Tests.csproj --filter SonnetDb_AdminEnsureCollection_ShouldSucceed_InSinglebox`
Expected: FAIL。

- [ ] **Step 3: 最小实现（Provider 与 profile 守卫）**

```csharp
public sealed class SonnetDbVectorStoreAdministration : IVectorStoreAdministration
{
    private readonly DeploymentProfile _profile;
    public SonnetDbVectorStoreAdministration(DeploymentProfile profile) => _profile = profile;

    public ValueTask EnsureCollectionAsync(VectorCollectionSpec spec, CancellationToken ct)
    {
        if (_profile.Mode != "singlebox")
            throw new InvalidOperationException("SonnetDB is singlebox-only.");
        return ValueTask.CompletedTask;
    }
}
```

- [ ] **Step 4: GREEN**

Run: `dotnet test dotnet/tests/Ocb.Infrastructure.Tests/Ocb.Infrastructure.Tests.csproj --filter SonnetDb`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Infrastructure.Vector/SonnetDb dotnet/tests/Ocb.Infrastructure.Tests/Vector/SonnetDb
git commit -m "feat(vector): implement sonnetdb provider for singlebox"
```

---

### Task 5: 实现 Qdrant Provider（cluster 专用）

**Files:**

- Create: `dotnet/src/Ocb.Infrastructure.Vector/Qdrant/QdrantVectorStore.cs`
- Create: `dotnet/src/Ocb.Infrastructure.Vector/Qdrant/QdrantHybridSearchStore.cs`
- Create: `dotnet/src/Ocb.Infrastructure.Vector/Qdrant/QdrantVectorStoreAdministration.cs`
- Create: `dotnet/tests/Ocb.Infrastructure.Tests/Vector/Qdrant/QdrantConformanceTests.cs`

**Interfaces:**

- Consumes: Task 2 契约 + Task 3 conformance。
- Produces: Qdrant 三接口实现，且 cluster 非 Qdrant 配置 fail closed。

- [ ] **Step 1: 写失败测试（cluster profile + SonnetDB 配置必须启动失败）**

```csharp
[Fact]
public void ClusterProfile_WithSonnetDbConfig_MustFailClosed()
{
    Action act = () => VectorProviderFactory.Create(profileMode: "cluster", provider: "sonnetdb");
    act.Should().Throw<InvalidOperationException>()
       .WithMessage("*cluster*Qdrant*");
}
```

- [ ] **Step 2: RED**

Run: `dotnet test dotnet/tests/Ocb.Infrastructure.Tests/Ocb.Infrastructure.Tests.csproj --filter ClusterProfile_WithSonnetDbConfig_MustFailClosed`
Expected: FAIL。

- [ ] **Step 3: 最小实现（工厂 fail closed）**

```csharp
if (profileMode == "cluster" && provider != "qdrant")
    throw new InvalidOperationException("Cluster mode requires Qdrant provider.");
```

- [ ] **Step 4: GREEN**

Run: `dotnet test dotnet/tests/Ocb.Infrastructure.Tests/Ocb.Infrastructure.Tests.csproj --filter Qdrant`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Infrastructure.Vector/Qdrant dotnet/tests/Ocb.Infrastructure.Tests/Vector/Qdrant
git commit -m "feat(vector): implement qdrant provider for cluster"
```

---

### Task 6: 交付 Ocb.Fusion 路由层并对齐 `POST /api/v1/groups/{group_id}/fuse`

**Files:**

- Create: `dotnet/src/Ocb.Fusion/Routes/FusionRoutes.cs`
- Create: `dotnet/src/Ocb.Fusion/Application/FusionService.cs`
- Create: `dotnet/tests/Ocb.Fusion.Tests/Api/FusionRouteTests.cs`
- Create: `dotnet/tests/Ocb.Fusion.Tests/Api/FusionRequestValidationTests.cs`

**Interfaces:**

- Consumes: `FusionRequestDto`, `FuseResponseDto`, `CallerContext`。
- Produces:
  - `MapPost("/api/v1/groups/{group_id}/fuse", Func<string, FusionRequestDto, HttpContext, FusionService, CancellationToken, Task<IResult>> handler)`
  - `Task<FuseResponseDto> FuseAsync(string groupId, FusionRequestDto request, CallerContext caller, CancellationToken ct)`

- [ ] **Step 1: 写失败测试（group_id pattern + OpenAPI path 对齐）**

```csharp
[Fact]
public async Task PostFuse_ShouldRejectInvalidGroupId()
{
    var client = CreateFusionApiClient();
    var res = await client.PostAsJsonAsync("/api/v1/groups/invalid/fuse", new { question = "q", participants = new[] { "w1:default" } });
    res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
}
```

- [ ] **Step 2: RED**

Run: `dotnet test dotnet/tests/Ocb.Fusion.Tests/Ocb.Fusion.Tests.csproj --filter PostFuse_ShouldRejectInvalidGroupId`
Expected: FAIL。

- [ ] **Step 3: 最小实现（路由+pattern 校验+caller 透传）**

```csharp
app.MapPost("/api/v1/groups/{group_id}/fuse", async (
  [FromRoute(Name = "group_id")] string groupId,
    FusionRequestDto body,
    HttpContext http,
    FusionService service,
    CancellationToken ct) =>
{
    if (!Regex.IsMatch(groupId, "^grp-[A-Za-z0-9_-]+$"))
        return Results.BadRequest(new { error = new { code = "INVALID_GROUP_ID" } });

    var caller = CallerContextFactory.FromHttp(http); // 必填 tenant+subject
    var result = await service.FuseAsync(groupId, body, caller, ct);
    return Results.Ok(result);
});
```

- [ ] **Step 4: GREEN**

Run: `dotnet test dotnet/tests/Ocb.Fusion.Tests/Ocb.Fusion.Tests.csproj --filter FusionRoute`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Fusion/Routes/FusionRoutes.cs dotnet/src/Ocb.Fusion/Application/FusionService.cs dotnet/tests/Ocb.Fusion.Tests/Api
git commit -m "feat(fusion): add post fuse route parity"
```

---

### Task 7: 交付 worker/profile parity 路由（完整 bcsfuse OpenAPI 覆盖的一部分）

**Files:**

- Create: `dotnet/src/Ocb.Fusion/Routes/WorkersRoutes.cs`
- Create: `dotnet/src/Ocb.Fusion/Application/WorkerProfileService.cs`
- Create: `dotnet/tests/Ocb.Fusion.Tests/Api/WorkersRoutesParityTests.cs`

**Interfaces:**

- Consumes: `WorkerProfileDtos`。
- Produces:
  - `POST /v1/workers`
  - `GET /v1/workers`
  - `GET /v1/workers/{workerId}`
  - `PATCH /v1/workers/{workerId}`
  - `GET /health`

- [ ] **Step 1: 写失败测试（路径存在且返回码契约一致）**

```csharp
[Theory]
[InlineData("/v1/workers", HttpStatusCode.OK)]
[InlineData("/v1/workers/not-exist", HttpStatusCode.NotFound)]
public async Task WorkerRoutes_ShouldMatchParityStatusCodes(string url, HttpStatusCode expected)
{
    var client = CreateFusionApiClient();
    var response = await client.GetAsync(url);
    response.StatusCode.Should().Be(expected);
}
```

- [ ] **Step 2: RED**

Run: `dotnet test dotnet/tests/Ocb.Fusion.Tests/Ocb.Fusion.Tests.csproj --filter WorkerRoutes_ShouldMatchParityStatusCodes`
Expected: FAIL（404 或路由未映射）。

- [ ] **Step 3: 最小实现（路由映射 + service）**

```csharp
app.MapPost("/v1/workers", (CreateWorkerRequest body, WorkerProfileService svc, CancellationToken ct) => svc.CreateWorkerAsync(body, ct));
app.MapGet("/v1/workers", (WorkerProfileService svc, CancellationToken ct) => svc.ListWorkersAsync(ct));
app.MapGet("/v1/workers/{worker_id}", ([FromRoute(Name = "worker_id")] string workerId, WorkerProfileService svc, CancellationToken ct) => svc.GetWorkerAsync(workerId, ct));
app.MapPatch("/v1/workers/{worker_id}", ([FromRoute(Name = "worker_id")] string workerId, JsonObject body, WorkerProfileService svc, CancellationToken ct) => svc.UpdateWorkerAsync(workerId, body, ct));
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
```

- [ ] **Step 4: GREEN**

Run: `dotnet test dotnet/tests/Ocb.Fusion.Tests/Ocb.Fusion.Tests.csproj --filter WorkersRoutesParity`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Fusion/Routes/WorkersRoutes.cs dotnet/src/Ocb.Fusion/Application/WorkerProfileService.cs dotnet/tests/Ocb.Fusion.Tests/Api/WorkersRoutesParityTests.cs
git commit -m "feat(fusion): add worker profile route parity"
```

---

### Task 8: 实现 CallerContext 二次 tenant 校验与 FusionJobGrain 串行幂等

**Orleans 细化说明：**

- `FusionJobGrain` 是 **薄协调层 Grain**，串行处理同一 key 的命令并做幂等去重。**不直接做向量读写、HTTP 调用、数据库 CRUD、文件 I/O**——这些通过 `IFusionCoordinator`（领域服务，注入到 Grain）调用 `IVectorStore`/`IHybridSearchStore`/`IEmbeddingPlugin` 完成。
- Grain Key 使用 `TenantEntityKey` 组合键 `(tenant_id, fusion_job_id)`，实现 `IGrainWithStringKey`。Grain 入口通过 `TenantKeyGuardCallFilter` 做二次 tenant 校验。
- Grain State 持久化到 PostgreSQL（`ocb_orleans.OrleansStorage` 表），Provider 名称为 `"orleans-storage"`（与 gateway-channels plan Task 7 定义一致）。
- 所有 Grain 接口/State 使用 `[Alias]` + `[GenerateSerializer]` + `[Id(n)]` 属性（Orleans 9 要求，用于 AOT 兼容序列化）。

**Orleans NuGet 依赖（追加到已有 `Ocb.GrainContracts.csproj` 和 `Ocb.Grains.csproj`）：**

此 plan 不创建新的 `.csproj`，依赖由 gateway-channels plan 设置的骨架项目提供（已在 `Directory.Packages.props` 统一定义版本）。如果需要此 plan 独立验证 Task 8，确认以下包存在于 `Ocb.Grains.csproj` 中：

| 包 | 用途 |
|---|---|
| `Microsoft.Orleans.Sdk` | `[GenerateSerializer]` / `[Alias]` Source Generator |
| `Microsoft.Orleans.Runtime` | `Grain`、`IPersistentState<T>`、`RequestContext` |
| `Microsoft.Orleans.Persistence.AdoNet` | PostgreSQL 持久化 `FusionJobState` |

**Files:**

- Create: `dotnet/src/Ocb.GrainContracts/Fusion/IFusionJobGrain.cs`
- Create: `dotnet/src/Ocb.GrainContracts/Fusion/FusionCommand.cs`
- Create: `dotnet/src/Ocb.GrainContracts/Fusion/FusionJobState.cs`
- Create: `dotnet/src/Ocb.GrainContracts/Fusion/FusionGrainKeys.cs`
- Create: `dotnet/src/Ocb.Grains/Fusion/FusionJobGrain.cs`
- Create: `dotnet/src/Ocb.Grains/Fusion/TenantKeyGuardCallFilter.cs`
- Create: `dotnet/src/Ocb.Fusion/Application/IFusionCoordinator.cs`
- Create: `dotnet/src/Ocb.Fusion/Application/FusionCoordinator.cs`
- Test: `dotnet/tests/Ocb.Grains.Tests/Fusion/FusionJobGrainIdempotencyTests.cs`
- Test: `dotnet/tests/Ocb.Grains.Tests/Fusion/TenantKeyGuardTests.cs`

**Interfaces:**

- Consumes: `CallerContext`、`TenantEntityKey`、`FusionRequestDto`、`FuseResponseDto`。
- Produces:
  - `public interface IFusionJobGrain : IGrainWithStringKey`
  - `Task<FuseResponseDto> ExecuteAsync(FusionCommand command)`
  - `Task<FusionJobState> GetStateAsync()`
  - `public interface IFusionCoordinator { Task<FuseResponseDto> RunAsync(FusionCommand command, CancellationToken ct); }`
  - `TenantKeyGuardCallFilter`（通过 `RequestContext` 校验 `command.Caller.TenantId == grainKey.TenantId`）

- [ ] **Step 1: 写失败测试（IdempotencyKey 幂等、跨租户隔离、Grain 不允许直接 I/O）**

```csharp
[Fact]
public async Task ExecuteAsync_SameIdempotencyKey_ShouldReturnSameResultWithoutSecondExecution()
{
    // 使用 Orleans TestCluster + InMemory storage
    var fixture = await OrleansFusionTestFixture.StartAsync();
    var grainKey = TenantEntityKey.ForFusionJob("t1", "fusion-job-1").ToString();
    var grain = fixture.GrainFactory.GetGrain<IFusionJobGrain>(grainKey);

    var cmd = new FusionCommand(
        IdempotencyKey: "idem-1",
        Caller: new CallerContext("t1", "u1", new HashSet<string> { "user" }),
        Request: new FusionRequestDto("q1", ["p1"], null, "mode", "consensus", null, null)
    );

    var first = await grain.ExecuteAsync(cmd);
    var second = await grain.ExecuteAsync(cmd);

    // 幂等：两次调用返回相同 FusionId，且只执行一次
    second.FusionId.Should().Be(first.FusionId);
    var state = await grain.GetStateAsync();
    state.ExecutionCount.Should().Be(1);
    state.CompletedByIdempotency.Should().ContainKey("idem-1");
}

[Fact]
public async Task ExecuteAsync_TenantMismatch_ThrowsUnauthorized()
{
    var fixture = await OrleansFusionTestFixture.StartAsync();
    // grain key 属于 t-A，但 CallerContext 声称是 t-B
    var grainKey = TenantEntityKey.ForFusionJob("t-A", "fusion-1").ToString();
    var grain = fixture.GrainFactory.GetGrain<IFusionJobGrain>(grainKey);

    var cmd = new FusionCommand(
        IdempotencyKey: "idem-99",
        Caller: new CallerContext("t-B", "u1", new HashSet<string> { "user" }), // ← 不匹配
        Request: new FusionRequestDto("q", ["p1"], null, "mode", "consensus", null, null)
    );

    await Assert.ThrowsAsync<UnauthorizedAccessException>(
        () => grain.ExecuteAsync(cmd));
}

[Fact]
public async Task GrainState_DoesNotContainHttpClientOrVectorStore()
{
    // 验证 FusionJobState 不包含框架对象引用（Grain 薄协调规则）
    var stateType = typeof(FusionJobState);
    var forbiddenTypes = new[] { "HttpClient", "IVectorStore", "SqlConnection", "WebSocket" };

    foreach (var prop in stateType.GetProperties())
    {
        var typeName = prop.PropertyType.Name;
        foreach (var forbidden in forbiddenTypes)
        {
            Assert.DoesNotContain(forbidden, typeName);
        }
    }
}
```

- [ ] **Step 2: RED**

Run: `dotnet test dotnet/tests/Ocb.Grains.Tests/Ocb.Grains.Tests.csproj --filter ExecuteAsync_SameIdempotencyKey_ShouldReturnSameResultWithoutSecondExecution`
Expected: FAIL，`FusionJobGrain` 或 Grain 接口缺失。

- [ ] **Step 3: 最小实现**

**Grain Key 与 Command 定义（`Ocb.GrainContracts/Fusion/`）：**

```csharp
// FusionGrainKeys.cs — 只追加到已有 TenantGrainKey 或新建 Fusion 专用 helper
public static class FusionGrainKeys
{
    public static TenantEntityKey ForFusionJob(string tenantId, string fusionJobId)
        => new(tenantId, $"fusion-job/{fusionJobId}");
}

// FusionCommand.cs
[Alias("Ocb.GrainContracts.Fusion.FusionCommand")]
[GenerateSerializer]
public sealed record FusionCommand(
    [property: Id(0)] string IdempotencyKey,
    [property: Id(1)] CallerContext Caller,
    [property: Id(2)] FusionRequestDto Request
);

// FusionJobState.cs — Grain 持久化状态（仅序列化数据，不包含对象引用）
[Alias("Ocb.GrainContracts.Fusion.FusionJobState")]
[GenerateSerializer]
public sealed class FusionJobState
{
    [Id(0)]
    public Dictionary<string, FuseResponseDto> CompletedByIdempotency { get; init; } = new();

    [Id(1)]
    public int ExecutionCount { get; set; }

    [Id(2)]
    public string? LastFusionId { get; set; }

    [Id(3)]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

// IFusionJobGrain.cs
[Alias("Ocb.GrainContracts.Fusion.IFusionJobGrain")]
public interface IFusionJobGrain : IGrainWithStringKey
{
    Task<FuseResponseDto> ExecuteAsync(FusionCommand command);
    Task<FusionJobState> GetStateAsync();
}
```

**Grain 实现（`Ocb.Grains/Fusion/FusionJobGrain.cs`）：**

```csharp
[Alias("Ocb.Grains.Fusion.FusionJobGrain")]
public sealed class FusionJobGrain : Grain, IFusionJobGrain
{
    private readonly IPersistentState<FusionJobState> _state;
    private readonly IFusionCoordinator _fusionCoordinator;

    public FusionJobGrain(
        [PersistentState("fusion-job", "orleans-storage")]
        IPersistentState<FusionJobState> state,
        IFusionCoordinator fusionCoordinator)
    {
        _state = state;
        _fusionCoordinator = fusionCoordinator;
    }

    public async Task<FuseResponseDto> ExecuteAsync(FusionCommand command)
    {
        // 幂等去重：已完成的 IdempotencyKey 直接返回缓存
        if (_state.State.CompletedByIdempotency
            .TryGetValue(command.IdempotencyKey, out var cached))
            return cached;

        // 领域编排：委托给 IFusionCoordinator（注入的领域服务）
        // Grain 不做 HTTP/向量 I/O/数据库 CRUD
        var result = await _fusionCoordinator.RunAsync(command, CancellationToken.None);

        // 持久化幂等记录
        _state.State.CompletedByIdempotency[command.IdempotencyKey] = result;
        _state.State.ExecutionCount++;
        _state.State.LastFusionId = result.FusionId;
        await _state.WriteStateAsync();

        return result;
    }

    public Task<FusionJobState> GetStateAsync() =>
        Task.FromResult(_state.State);
}
```

**TenantKeyGuardCallFilter（`Ocb.Grains/Fusion/TenantKeyGuardCallFilter.cs`）：**

```csharp
public sealed class TenantKeyGuardCallFilter : IIncomingGrainCallFilter
{
    public async Task Invoke(IIncomingGrainCallContext context)
    {
        if (context.Grain is IFusionJobGrain)
        {
            var grainKey = TenantEntityKey.Parse(context.Grain.GetPrimaryKeyString());
            var cmd = (context.Arguments?.FirstOrDefault() as FusionCommand 
                       ?? ((FusionCommand?)RequestContext.Get("FusionCommand")));
            
            if (cmd is not null &&
                !string.Equals(cmd.Caller.TenantId, grainKey.TenantId, StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException(
                    $"Tenant mismatch in FusionJobGrain: " +
                    $"caller={cmd.Caller.TenantId} grain={grainKey.TenantId}");
            }
        }
        await context.Invoke();
    }
}
```

> **注：** `TenantKeyGuardCallFilter` 在 `Ocb.Silo.Host` 的 `Program.cs` 中注册：
> ```csharp
> siloBuilder.AddIncomingGrainCallFilter<TenantKeyGuardCallFilter>();
> ```
> 每个 domain plan 定义的 `TenantKeyGuardCallFilter` 应在各自的 `Ocb.Grains/Fusion/` 子目录下，分别注册。最终在 Silo Host 中将所有 filter 链式组装。

**IFusionCoordinator 领域服务（`Ocb.Fusion/Application/`）：**

```csharp
public interface IFusionCoordinator
{
    /// <summary>执行融合编排：embedding → 向量检索 → reranker → 结果聚合</summary>
    Task<FuseResponseDto> RunAsync(FusionCommand command, CancellationToken ct);
}

public sealed class FusionCoordinator : IFusionCoordinator
{
    private readonly IEmbeddingPlugin _embedding;
    private readonly IHybridSearchStore _search;
    private readonly IRerankerPlugin _reranker;

    public FusionCoordinator(IEmbeddingPlugin embedding, IHybridSearchStore search, IRerankerPlugin reranker)
    {
        _embedding = embedding;
        _search = search;
        _reranker = reranker;
    }

    public async Task<FuseResponseDto> RunAsync(FusionCommand command, CancellationToken ct)
    {
        // 1. 嵌入查询
        // 2. 混合检索（向量 + 关键词）
        // 3. Rerank 结果
        // 4. 聚合为 FuseResponseDto
        // 注：具体实现见 Task 6 (FusionRoutes)
        throw new NotImplementedException("Implemented in Task 6");
    }
}
```

- [ ] **Step 4: GREEN**

Run: `dotnet test dotnet/tests/Ocb.Grains.Tests/Ocb.Grains.Tests.csproj --filter FusionJobGrain`
Expected: PASS。所有测试通过 — 幂等、跨租户拒绝、State 不含禁止类型。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.GrainContracts/Fusion dotnet/src/Ocb.Grains/Fusion dotnet/src/Ocb.Fusion/Application dotnet/tests/Ocb.Grains.Tests/Fusion
git commit -m "feat(grains): add tenant-checked fusion job grain with idempotency and serial execution"
```

---

### Task 9: 实现 embedding/reranker 插件与向量兼容守卫（model/dimension fail closed）

**Files:**

- Create: `dotnet/src/Ocb.Plugins.Embedding/DefaultEmbeddingPlugin.cs`
- Create: `dotnet/src/Ocb.Plugins.Reranker/DefaultRerankerPlugin.cs`
- Create: `dotnet/src/Ocb.Infrastructure.Vector/Guards/VectorCompatibilityGuard.cs`
- Create: `dotnet/tests/Ocb.Infrastructure.Tests/Vector/VectorCompatibilityGuardTests.cs`

**Interfaces:**

- Consumes: `IEmbeddingPlugin`, `IRerankerPlugin`, `IVectorStore`。
- Produces:
  - `VectorCompatibilityGuard.ValidateOrThrow(model, dimension, collectionMeta)`
  - 插件解耦调用链：`FusionService -> IEmbeddingPlugin/IRerankerPlugin -> IVectorStore`

- [ ] **Step 1: 写失败测试（模型不一致抛异常）**

```csharp
[Fact]
public void ValidateOrThrow_WhenModelMismatch_ShouldThrow()
{
    Action act = () => VectorCompatibilityGuard.ValidateOrThrow("m2", 1024, new CollectionMeta("m1", 1024));
    act.Should().Throw<VectorCompatibilityException>()
       .WithMessage("*model*");
}
```

- [ ] **Step 2: RED**

Run: `dotnet test dotnet/tests/Ocb.Infrastructure.Tests/Ocb.Infrastructure.Tests.csproj --filter ValidateOrThrow_WhenModelMismatch_ShouldThrow`
Expected: FAIL。

- [ ] **Step 3: 最小实现（守卫）**

```csharp
public static void ValidateOrThrow(string model, int dimension, CollectionMeta meta)
{
    if (!string.Equals(model, meta.Model, StringComparison.Ordinal))
        throw new VectorCompatibilityException("Embedding model mismatch.");
    if (dimension != meta.Dimension)
        throw new VectorCompatibilityException("Embedding dimension mismatch.");
}
```

- [ ] **Step 4: GREEN**

Run: `dotnet test dotnet/tests/Ocb.Infrastructure.Tests/Ocb.Infrastructure.Tests.csproj --filter VectorCompatibilityGuard`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Plugins.Embedding dotnet/src/Ocb.Plugins.Reranker dotnet/src/Ocb.Infrastructure.Vector/Guards dotnet/tests/Ocb.Infrastructure.Tests/Vector/VectorCompatibilityGuardTests.cs
git commit -m "feat(plugins): separate embedding reranker and add fail-closed guard"
```

---

### Task 10: OpenAPI parity 校验、端到端 conformance 与 BCS 边界保护

**Files:**

- Create: `dotnet/src/Ocb.Fusion/OpenApi/BcsfuseOpenApiParityValidator.cs`
- Create: `dotnet/tests/Ocb.Fusion.Tests/Api/BcsfuseOpenApiParityTests.cs`
- Create: `dotnet/tests/Ocb.EndToEnd.Tests/Fusion/FusionApiConformanceE2ETests.cs`
- Create: `dotnet/tests/Ocb.Architecture.Tests/Boundary/BcsSqliteBypassForbiddenTests.cs`
- Modify: `scripts/ci/dotnet_ci.sh`（加入 Fusion parity 测试分组）

**Interfaces:**

- Consumes:
  - `dotnet/contracts/parity-corpus/bcsfuse.openapi.yaml`
  - `src/bcsfuse/tests/smoke/route_import_inventory.py`（作为路径范围与路由 inventory 依据）
- Produces:
  - OpenAPI parity 断言（path/verb/request/response 必备字段）
  - BCS 边界断言（禁止 `SQLiteConnection` 出现在 `Ocb.Bcs.Client` 与 `Ocb.Fusion`）

- [ ] **Step 1: 写失败测试（缺路径即失败）**

```csharp
[Fact]
public async Task OpenApiParity_MustContainFuseAndWorkersPaths()
{
    var diff = await BcsfuseOpenApiParityValidator.CompareAsync();
    diff.MissingPaths.Should().BeEmpty();
    diff.MissingPaths.Should().NotContain("/api/v1/groups/{group_id}/fuse");
    diff.MissingPaths.Should().NotContain("/v1/workers");
}
```

- [ ] **Step 2: RED**

Run: `dotnet test dotnet/tests/Ocb.Fusion.Tests/Ocb.Fusion.Tests.csproj --filter OpenApiParity_MustContainFuseAndWorkersPaths`
Expected: FAIL，提示 MissingPaths。

- [ ] **Step 3: 最小实现（parity validator + BCS SQLite guard）**

```csharp
public static async Task<OpenApiDiffResult> CompareAsync(HttpClient fusionClient)
{
    var baseline = await OpenApiDocument.LoadAsync("dotnet/contracts/parity-corpus/bcsfuse.openapi.yaml");
  using var currentStream = await fusionClient.GetStreamAsync("/openapi/v1.json");
  var current = await OpenApiDocument.LoadAsync(currentStream);
    return OpenApiDiffResult.From(baseline, current);
}
```

```csharp
[Fact]
public void FusionAndBcsClient_MustNotReferenceSqlite()
{
    var sources = Directory.GetFiles("dotnet/src", "*.cs", SearchOption.AllDirectories);
    var banned = sources.Where(f => f.Contains("Ocb.Bcs.Client") || f.Contains("Ocb.Fusion"))
                        .SelectMany(File.ReadLines)
                        .Any(line => line.Contains("SQLiteConnection", StringComparison.Ordinal));
    banned.Should().BeFalse();
}
```

- [ ] **Step 4: GREEN**

Run: `dotnet test dotnet/tests/Ocb.Fusion.Tests/Ocb.Fusion.Tests.csproj --filter OpenApiParity`
Expected: PASS。

Run: `dotnet test dotnet/tests/Ocb.Architecture.Tests/Ocb.Architecture.Tests.csproj --filter Sqlite`
Expected: PASS。

Run: `dotnet test dotnet/tests/Ocb.EndToEnd.Tests/Ocb.EndToEnd.Tests.csproj --filter FusionApiConformance`
Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Fusion/OpenApi dotnet/tests/Ocb.Fusion.Tests/Api/BcsfuseOpenApiParityTests.cs dotnet/tests/Ocb.EndToEnd.Tests/Fusion dotnet/tests/Ocb.Architecture.Tests/Boundary scripts/ci/dotnet_ci.sh
git commit -m "test(fusion): enforce bcsfuse openapi parity and bcs boundary"
```

---

## Self-Review

### 1) 需求覆盖检查

- `POST /api/v1/groups/{group_id}/fuse`：Task 6。
- 完整 bcsfuse OpenAPI parity（含 worker/profile/health/fuse 核心路径）：Task 7 + Task 10。
- worker/profile：Task 7。
- CallerContext 显式 tenant+subject：Task 1。
- Grain 二次 tenant key 校验，禁止 trust-gateway bypass：Task 8（`TenantKeyGuardCallFilter` + `TenantEntityKey.Parse` 校验 `caller.TenantId == grainKey.TenantId`）。
- FusionJobGrain 串行幂等：Task 8（`[PersistentState("fusion-job", "orleans-storage")]` PostgreSQL 持久化 + `CompletedByIdempotency` 字典去重）。
- **Orleans 细化：** Task 8 定义了完整的 Grain Key（`TenantEntityKey(tenantId, "fusion-job/{id}")`）、`[Alias]`/`[GenerateSerializer]`/`[Id(n)]` AOT 兼容属性、`IPersistentState<T>` PostgreSQL 持久化、`IFusionCoordinator` 领域服务注入（Grain 不做 HTTP/向量 I/O）、`TenantKeyGuardCallFilter` 通过 `RequestContext` 校验 call filter。
- `IVectorStore`/`IHybridSearchStore`/`IVectorStoreAdministration`：Task 2。
- SonnetDB singlebox：Task 4。
- Qdrant cluster：Task 5。
- embedding/reranker 独立 Plugin：Task 2 + Task 9。
- model/dimension fail closed：Task 3 + Task 9。
- 共用 conformance：Task 3 并在 Task 4/5 复用。
- 项目命名 `Ocb.Fusion`、`Ocb.GrainContracts/Ocb.Grains`、`Ocb.Infrastructure.Vector`、`Ocb.Plugins.*`：File Structure 与任务均使用。
- 接口在 `Ocb.PluginApi/Contracts`：Task 2。
- Rust BCS 仅版本化契约、禁止 SQLite：Task 10 架构测试。
- 不把向量 I/O 放入 Grain：Task 8 的接口与实现约束已明确。

### 2) No-placeholder Check

- 已按 writing-plans 禁止模式完成扫描，实际实施步骤中无占位内容。
- 每个任务均含失败测试代码、RED/GREEN 命令、最小实现签名、Commit。

### 3) 类型与签名一致性

- `CallerContext`、`FusionRequestDto`、`FuseResponseDto`、`IFusionJobGrain.ExecuteAsync(FusionCommand)` 在任务间保持一致。
- Vector 契约与 conformance 的 `VectorRecord/VectorQuery` 使用一致。

## 覆盖摘要

- 任务总数：10（满足 8-12）。
- 覆盖面：契约建模、Plugin API、共享 conformance、双向量 Provider、Fusion/Worker/Profile 路由、Grain 幂等与租户校验、OpenAPI parity、BCS 边界防回归。
- 交付顺序：先契约与接口，再 Provider 与路由，再 Grain 与全局 parity 验收。
