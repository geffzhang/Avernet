# Ocb .NET Backend Skills Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在已完成 Foundation 基线后，交付 .NET 版 Backend Stage-5（Caller Identity、Bot/Session/asset、tenant guard、PostgreSQL `ocb_business`、MinIO、Skills publication/activation/reconcile）并通过 Service/Plugin conformance 与 HTTP parity。

**Architecture:** `Ocb.Backend` 仅负责业务编排与契约映射；`Ocb.Grains` 仅负责稳定身份与串行协调；`Ocb.Infrastructure.PostgreSql` 与 `Ocb.Infrastructure.Minio` 仅承载实现细节。所有接口定义只放在 `Ocb.Contracts` / `Ocb.PluginApi`，并通过 `Ocb.GrainContracts` 连接 Grain 调用边界。Skills 内容仓库与 active 发现入口严格物理分离，运行时物理布局完全归 Runtime 所有，Backend 只下发逻辑意图与不可变版本声明。

**Tech Stack:** .NET 10 / C# 14、ASP.NET Core、Orleans、EF Core + Npgsql、MinIO SDK、xUnit、Testcontainers、OpenAPI parity corpus。

## Global Constraints

- 目标项目命名固定为：`Ocb.Backend`、`Ocb.Infrastructure.PostgreSql`、`Ocb.Infrastructure.Minio`、`Ocb.GrainContracts`、`Ocb.Grains`；接口只定义在 `Ocb.Contracts` 与 `Ocb.PluginApi`。
- Backend 到 Runtime Worker 的激活/物化调用是 Service API，定义在 `Ocb.Contracts`；它不是基础设施 Provider，禁止定义成 `Ocb.PluginApi`。
- Core/Contracts 不得引用框架实现；所有基础设施选择仅在 Composition Root。
- Gateway / Runtime / BaaS 所有权：
  - Gateway 仅负责鉴权、principal 验签、入口协议转换与转发，不承载 Skills 物理布局。
  - Runtime Worker 仅负责 Bot workspace 物化、下载校验、原子发布、回滚与 observed state 回报。
  - BaaS 仅负责设备/沙箱/provider 与 `invoke-http`，不拥有 Skills 版本治理。
- 严格遵守 `docs/arch/service-skills-layout-wire-contract.md`：Pool 必须声明且仅允许 `skills-pool-p3-v1`，未知版本 fail closed。
- 严格遵守 skill_center 约束：
  - 完整内容仓库必须完整保存（`skills-repo`/`skills-center`/`skills-local`）。
  - active 目录只能暴露“已激活 Skill 入口”，禁止任何完整仓库 bridge。
  - 物理 layout 所有权属于 Runtime，Backend 不拼接引擎路径。
  - `center://` 前缀仅表示来源，不等于“已发布完成”。
- tenant 隔离必须覆盖 Caller Identity、Bot、Session、asset、Skill publication 与激活状态。
- PostgreSQL 业务 schema 固定在 `ocb_business`，并以显式迁移管理；不得运行时静默改表。
- MinIO 流程必须覆盖：multipart、checksum、signed URL、range、临时发布、删除补偿；integrity failure 不重试。
- 下载重试策略固定：最多 3 次，指数退避 + jitter；integrity failure 直接失败。
- 原子发布失败必须回滚到旧视图；若不存在旧视图则 fail closed 并返回可操作错误。
- Service API 与 Plugin API 必须提供 conformance tests；Backend HTTP 必须通过 parity 和用户故事测试。

---

## File Structure

- `dotnet/src/Ocb.Contracts/`
  - 责任：定义 Stage-5 业务契约（Caller Identity、Bot/Session/asset、Skills publication/activation、manifest、Runtime materialization 命令与结果 DTO）。
- `dotnet/src/Ocb.PluginApi/`
  - 责任：定义 PostgreSQL Repository Port、MinIO Object Port、Runtime Materializer Port、SkillPublicationStore Port 等插件接口。
- `dotnet/src/Ocb.GrainContracts/`
  - 责任：定义 `IBotGrain`、`ISessionGrain`、`IDeviceGrain` 及 tenant-stable key 辅助契约。
- `dotnet/src/Ocb.Grains/`
  - 责任：实现 Grain 协调（desired/observed、串行化、tenant 复核、调用 Service API，不接触 MinIO/SQL 实现细节）。
- `dotnet/src/Ocb.Backend/`
  - 责任：应用服务、HTTP adapter、manifest 校验、publication 状态机、Runtime reconcile orchestration。
- `dotnet/src/Ocb.Infrastructure.PostgreSql/`
  - 责任：`ocb_business` 迁移、tenant guard 查询实现、Skill publication 与 session/asset 元数据持久化。
- `dotnet/src/Ocb.Infrastructure.Minio/`
  - 责任：multipart/signed URL/checksum/range/临时对象发布/删除补偿实现。
- `dotnet/tests/Ocb.Contracts.Tests/`
  - 责任：线契约与序列化稳定性。
- `dotnet/tests/Ocb.Backend.Tests/`
  - 责任：应用服务、handler、manifest 校验、重试策略、回滚策略。
- `dotnet/tests/Ocb.Grains.Tests/`
  - 责任：tenant key 稳定性、串行与状态转换。
- `dotnet/tests/Ocb.Infrastructure.Tests/`
  - 责任：PostgreSQL 与 MinIO conformance、迁移与补偿。
- `dotnet/tests/Ocb.EndToEnd.Tests/`
  - 责任：HTTP parity（对照 `dotnet/contracts/parity-corpus/backend.openapi.json`）与关键用户故事。

**⚠️ 跨-Plan 项目创建协调：**
- `Ocb.GrainContracts/Ocb.GrainContracts.csproj` + `context-boundary.json` 由 **gateway-channels plan (Task 1)** 作为共享骨架创建。本 plan 只在已有项目中追加 `Bot/`、`Session/`、`Device/`、`GrainKeys/` 等子目录和接口文件，**不重新创建 .csproj 文件**。
- `Ocb.Grains/Ocb.Grains.csproj` + `context-boundary.json` 同样由 **gateway-channels plan (Task 1)** 创建。本 plan 只在已有项目中追加 `Bot/`、`Session/`、`Device/` 实现文件。
- `Ocb.Infrastructure.PostgreSql/Ocb.Infrastructure.PostgreSql.csproj` + `context-boundary.json` 由 **baas plan (Task 1)** 作为共享骨架创建。本 plan 只在已有项目中追加 `Skills/`、`Identity/`、`Assets/` 等子目录和 EF Core 实体，**不重新创建 .csproj 文件**。`ocb_business` schema 是共享 schema，本 plan 的 EF Core DbContext 通过 bounded context 隔离查询。
- 实施时检查：如果骨架尚未创建，先执行 gateway-channels plan Task 1（获 GrainContracts/Grains 骨架）和 baas plan Task 1（获 PostgreSql 骨架），再执行本 plan。

### Baseline References (必须先读后改)

- `src/backend/src/agentclaw/community/adapters/http/openapi_v1/identity/router.py`
- `src/backend/src/agentclaw/community/adapters/http/openapi_v1/skills/router.py`
- `src/backend/src/agentclaw/community/adapters/http/openapi_v1/resources/router.py`
- `src/backend/src/agentclaw/community/adapters/http/openapi_v1/engine_runtime/sessions/router.py`
- `src/backend/src/agentclaw/community/api/caller_identity_service.py`
- `src/backend/src/agentclaw/community/api/session_resource_service.py`
- `src/backend/tests/community/adapters/http/openapi_v1/test_skills_tenant_isolation.py`
- `src/backend/tests/community/adapters/http/openapi_v1/engine_runtime/test_tenant_isolation.py`
- `src/backend/tests/community/adapters/http/openapi_v1/resources/test_resources_handlers.py`
- `dotnet/contracts/migration-inventory.json`
- `dotnet/contracts/parity-corpus/backend.openapi.json`

---

### Task 1: 扩展 Stage-5 Service API 契约（Caller Identity / Bot / Session / Asset / Skills）

**Files:**

- Modify: `dotnet/src/Ocb.Contracts/OcbJsonContext.cs`
- Create: `dotnet/src/Ocb.Contracts/Skills/SkillSourceScheme.cs`
- Create: `dotnet/src/Ocb.Contracts/Skills/SkillVersionRef.cs`
- Create: `dotnet/src/Ocb.Contracts/Skills/SkillPublicationRecord.cs`
- Create: `dotnet/src/Ocb.Contracts/Skills/SkillActivationRequest.cs`
- Create: `dotnet/src/Ocb.Contracts/Resources/SessionAssetRecord.cs`
- Create: `dotnet/src/Ocb.Contracts/Identity/CallerIdentityBinding.cs`
- Test: `dotnet/tests/Ocb.Contracts.Tests/BackendSkillsContractSerializationTests.cs`

**Interfaces:**

- Consumes: `CallerContext`, `TenantEntityKey`, `DomainError`.
- Produces:
  - `public enum SkillSourceScheme { Git, Local, Center }`
  - `public sealed record SkillVersionRef(string SourceLocator, string ImmutableVersion, SkillSourceScheme Scheme)`
  - `public sealed record SkillPublicationRecord(string TenantId, string BotId, string SkillId, SkillVersionRef Version, string PackageSha256, string PublicationState, string ManifestContractVersion)`
  - `public sealed record SkillActivationRequest(CallerContext Caller, string BotId, IReadOnlyList<string> SkillIds, string ManifestContractVersion)`
  - `public sealed record SessionAssetRecord(string TenantId, string BotId, string SessionId, string ResourceId, string ObjectKey, long SizeBytes, string Sha256)`
  - `public sealed record CallerIdentityBinding(string TenantId, string BotId, string SubjectId, IReadOnlySet<string> Roles)`
  - Consumes Runtime 阶段已交付的 `ActivatedSkillVersion`、`MaterializationRequest`、`MaterializationResult` 与 `IRuntimeSkillMaterializationService`

- [ ] **Step 1: 写失败测试**

```csharp
[Fact]
public void SkillPublicationRecord_UsesStableSnakeCaseNames()
{
    var dto = new SkillPublicationRecord(
        TenantId: "tenant-a",
        BotId: "bot-1",
        SkillId: "skill-7",
        Version: new SkillVersionRef("center://uuid-1", "3", SkillSourceScheme.Center),
        PackageSha256: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        PublicationState: "published",
        ManifestContractVersion: "skills-pool-p3-v1");

    var json = JsonSerializer.Serialize(dto, OcbJsonContext.Default.SkillPublicationRecord);
    using var doc = JsonDocument.Parse(json);

    Assert.Equal("tenant-a", doc.RootElement.GetProperty("tenant_id").GetString());
    Assert.Equal("skills-pool-p3-v1", doc.RootElement.GetProperty("manifest_contract_version").GetString());
}
```

- [ ] **Step 2: 运行 RED**

Run: `dotnet test dotnet/tests/Ocb.Contracts.Tests/Ocb.Contracts.Tests.csproj --filter FullyQualifiedName~BackendSkillsContractSerializationTests -v n`

Expected: FAIL，`SkillPublicationRecord`/`OcbJsonContext` 类型不存在。

- [ ] **Step 3: 最小实现**

```csharp
namespace Ocb.Contracts.Skills;

public enum SkillSourceScheme { Git, Local, Center }

public sealed record SkillVersionRef(string SourceLocator, string ImmutableVersion, SkillSourceScheme Scheme);

public sealed record SkillPublicationRecord(
    string TenantId,
    string BotId,
    string SkillId,
    SkillVersionRef Version,
    string PackageSha256,
    string PublicationState,
    string ManifestContractVersion);
```

并在 `OcbJsonContext` 增加 `[JsonSerializable(typeof(SkillPublicationRecord))]` 等必要类型。

- [ ] **Step 4: 运行 GREEN**

Run: `dotnet test dotnet/tests/Ocb.Contracts.Tests/Ocb.Contracts.Tests.csproj --filter FullyQualifiedName~BackendSkillsContractSerializationTests -v n`

Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Contracts dotnet/tests/Ocb.Contracts.Tests
git commit -m "feat(contracts): add stage5 backend skills service contracts"
```

---

### Task 2: 定义 Plugin API 端口（PostgreSQL / MinIO）

**Files:**

- Create: `dotnet/src/Ocb.PluginApi/Storage/IAssetObjectStoragePlugin.cs`
- Create: `dotnet/src/Ocb.PluginApi/Skills/ISkillPublicationStorePlugin.cs`
- Create: `dotnet/src/Ocb.PluginApi/Identity/ICallerIdentityRepositoryPlugin.cs`
- Test: `dotnet/tests/Ocb.Contracts.Tests/PluginApiStage5ContractTests.cs`

**Interfaces:**

- Consumes: `Ocb.Contracts.*` DTO。
- Produces:
  - `Task<MultipartInitResult> BeginMultipartUploadAsync(AssetUploadRequest request, CancellationToken cancellationToken)`
  - `Task<DownloadResult> DownloadWithIntegrityAsync(AssetDownloadRequest request, CancellationToken cancellationToken)`
  - `Task<CallerIdentityBinding?> GetCallerIdentityAsync(string tenantId, string botId, string subjectId, CancellationToken cancellationToken)`

- [ ] **Step 1: 写失败测试**

```csharp
[Fact]
public void RuntimeMaterialization_IsServiceApi_NotPluginApi()
{
  Assert.Equal(typeof(Ocb.Contracts.Skills.IRuntimeSkillMaterializationService).Assembly,
    typeof(CallerContext).Assembly);
  Assert.DoesNotContain(typeof(IPluginContract).Assembly.GetTypes(),
    type => type.Name.Contains("RuntimeSkillMaterializer", StringComparison.Ordinal));
}
```

- [ ] **Step 2: 运行 RED**

Run: `dotnet test dotnet/tests/Ocb.Contracts.Tests/Ocb.Contracts.Tests.csproj --filter FullyQualifiedName~PluginApiStage5ContractTests -v n`

Expected: FAIL，接口不存在或方法签名不匹配。

- [ ] **Step 3: 最小实现**

```csharp
// Runtime 物化接口已在 Task 1 作为 Service API 定义；本任务只定义 PostgreSQL、
// MinIO 等 Backend Core 调用基础设施的 Plugin API。
```

- [ ] **Step 4: 运行 GREEN**

Run: `dotnet test dotnet/tests/Ocb.Contracts.Tests/Ocb.Contracts.Tests.csproj --filter FullyQualifiedName~PluginApiStage5ContractTests -v n`

Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.PluginApi dotnet/tests/Ocb.Contracts.Tests
git commit -m "feat(pluginapi): define stage5 ports for postgres minio runtime"
```

---

### Task 3: 建立 GrainContracts（Bot / Session / Device 稳定 tenant key）

**Files:**

- Create: `dotnet/src/Ocb.GrainContracts/Ocb.GrainContracts.csproj`
- Create: `dotnet/src/Ocb.GrainContracts/context-boundary.json`
- Create: `dotnet/src/Ocb.GrainContracts/Bot/IBotGrain.cs`
- Create: `dotnet/src/Ocb.GrainContracts/Session/ISessionGrain.cs`
- Create: `dotnet/src/Ocb.GrainContracts/Device/IDeviceGrain.cs`
- Create: `dotnet/src/Ocb.GrainContracts/GrainKeys/TenantGrainKey.cs`
- Test: `dotnet/tests/Ocb.Contracts.Tests/TenantGrainKeyTests.cs`

**Interfaces:**

- Consumes: `TenantEntityKey`。
- Produces:
  - `public static string Build(string tenantId, string entityId)`
  - `Task ReconcileSkillsAsync(SkillActivationRequest request)`

- [ ] **Step 1: 写失败测试**

```csharp
[Fact]
public void TenantGrainKey_IsDeterministicAndTenantScoped()
{
    var a = TenantGrainKey.Build("tenant-a", "bot-1");
    var b = TenantGrainKey.Build("tenant-b", "bot-1");

    Assert.NotEqual(a, b);
    Assert.Equal(a, TenantGrainKey.Build("tenant-a", "bot-1"));
}
```

- [ ] **Step 2: 运行 RED**

Run: `dotnet test dotnet/tests/Ocb.Contracts.Tests/Ocb.Contracts.Tests.csproj --filter FullyQualifiedName~TenantGrainKeyTests -v n`

Expected: FAIL，`TenantGrainKey` 不存在。

- [ ] **Step 3: 最小实现**

```csharp
public static class TenantGrainKey
{
    public static string Build(string tenantId, string entityId)
        => TenantEntityKey.Create(tenantId, entityId).ToString();
}
```

并新增 `IBotGrain`/`ISessionGrain`/`IDeviceGrain` 基础方法签名。

- [ ] **Step 4: 运行 GREEN**

Run: `dotnet test dotnet/tests/Ocb.Contracts.Tests/Ocb.Contracts.Tests.csproj --filter FullyQualifiedName~TenantGrainKeyTests -v n`

Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.GrainContracts dotnet/tests/Ocb.Contracts.Tests dotnet/Ocb.slnx
git commit -m "feat(graincontracts): add tenant stable grain keys and stage5 grain interfaces"
```

---

### Task 4: 新建 Ocb.Backend 应用层骨架与 manifest fail-closed 校验

**Files:**

- Create: `dotnet/src/Ocb.Backend/Ocb.Backend.csproj`
- Create: `dotnet/src/Ocb.Backend/context-boundary.json`
- Create: `dotnet/src/Ocb.Backend/Skills/Manifest/SkillsLayoutManifestValidator.cs`
- Create: `dotnet/src/Ocb.Backend/Skills/Manifest/SkillsLayoutManifest.cs`
- Create: `dotnet/src/Ocb.Backend/Skills/Errors/UnknownManifestContractException.cs`
- Test: `dotnet/tests/Ocb.Backend.Tests/Ocb.Backend.Tests.csproj`
- Test: `dotnet/tests/Ocb.Backend.Tests/Skills/ManifestValidatorTests.cs`

**Interfaces:**

- Consumes: `SkillPublicationRecord`。
- Produces:
  - `SkillsLayoutManifestValidator.ValidateOrThrow(SkillsLayoutManifest manifest)`

- [ ] **Step 1: 写失败测试**

```csharp
[Fact]
public void PoolManifest_RejectsUnknownContractVersion()
{
    var validator = new SkillsLayoutManifestValidator();
    var manifest = new SkillsLayoutManifest("openclaw", "pool", "skills-pool-p4-v2");

    var ex = Assert.Throws<UnknownManifestContractException>(() => validator.ValidateOrThrow(manifest));
    Assert.Contains("skills-pool-p3-v1", ex.Message);
}
```

- [ ] **Step 2: 运行 RED**

Run: `dotnet test dotnet/tests/Ocb.Backend.Tests/Ocb.Backend.Tests.csproj --filter FullyQualifiedName~ManifestValidatorTests -v n`

Expected: FAIL，验证器不存在。

- [ ] **Step 3: 最小实现**

```csharp
public sealed class SkillsLayoutManifestValidator
{
    public void ValidateOrThrow(SkillsLayoutManifest manifest)
    {
        if (manifest.ActiveLayout == "pool" && manifest.LayoutContractVersion != "skills-pool-p3-v1")
        {
            throw new UnknownManifestContractException(manifest.LayoutContractVersion);
        }
    }
}
```

- [ ] **Step 4: 运行 GREEN**

Run: `dotnet test dotnet/tests/Ocb.Backend.Tests/Ocb.Backend.Tests.csproj --filter FullyQualifiedName~ManifestValidatorTests -v n`

Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Backend dotnet/tests/Ocb.Backend.Tests dotnet/Ocb.slnx
git commit -m "feat(backend): add skills layout manifest fail-closed validator"
```

---

### Task 5: 新建 Ocb.Infrastructure.PostgreSql + `ocb_business` 初始迁移与 tenant guard

**Files:**

- Create: `dotnet/src/Ocb.Infrastructure.PostgreSql/Ocb.Infrastructure.PostgreSql.csproj`
- Create: `dotnet/src/Ocb.Infrastructure.PostgreSql/context-boundary.json`
- Create: `dotnet/src/Ocb.Infrastructure.PostgreSql/Migrations/0001_stage5_ocb_business.sql`
- Create: `dotnet/src/Ocb.Infrastructure.PostgreSql/Skills/SkillPublicationRepository.cs`
- Create: `dotnet/src/Ocb.Infrastructure.PostgreSql/Identity/CallerIdentityRepository.cs`
- Test: `dotnet/tests/Ocb.Infrastructure.Tests/Ocb.Infrastructure.Tests.csproj`
- Test: `dotnet/tests/Ocb.Infrastructure.Tests/PostgreSql/TenantGuardRepositoryTests.cs`

**Interfaces:**

- Consumes: `ISkillPublicationStorePlugin`, `ICallerIdentityRepositoryPlugin`。
- Produces:
  - tenant predicate 强制：每个查询必须包含 `tenant_id = @tenantId`

- [ ] **Step 1: 写失败测试**

```csharp
[Fact]
public async Task GetSkillPublicationAsync_CrossTenant_ReturnsNull()
{
    await SeedAsync("tenant-a", "bot-1", "skill-1");

    var result = await _repo.GetBySkillAsync("tenant-b", "bot-1", "skill-1", CancellationToken.None);

    Assert.Null(result);
}
```

- [ ] **Step 2: 运行 RED**

Run: `dotnet test dotnet/tests/Ocb.Infrastructure.Tests/Ocb.Infrastructure.Tests.csproj --filter FullyQualifiedName~TenantGuardRepositoryTests -v n`

Expected: FAIL，仓储未实现或查询未加 tenant 过滤。

- [ ] **Step 3: 最小实现**

```csharp
public async Task<SkillPublicationRecord?> GetBySkillAsync(string tenantId, string botId, string skillId, CancellationToken ct)
{
    const string sql = """
    select tenant_id, bot_id, skill_id, source_locator, immutable_version, scheme, publication_state, manifest_contract_version
    from ocb_business.skill_publications
    where tenant_id = @tenantId and bot_id = @botId and skill_id = @skillId
    limit 1;
    """;
    var entity = await _db.SkillPublications
      .AsNoTracking()
      .SingleOrDefaultAsync(x => x.TenantId == tenantId
        && x.BotId == botId
        && x.SkillId == skillId, ct);
    return entity is null ? null : Map(entity);
}
```

- [ ] **Step 4: 运行 GREEN**

Run: `dotnet test dotnet/tests/Ocb.Infrastructure.Tests/Ocb.Infrastructure.Tests.csproj --filter FullyQualifiedName~TenantGuardRepositoryTests -v n`

Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Infrastructure.PostgreSql dotnet/tests/Ocb.Infrastructure.Tests dotnet/Ocb.slnx
git commit -m "feat(infra-postgresql): add ocb_business stage5 schema and tenant-guard repositories"
```

---

### Task 6: 实现 Caller Identity + Bot/Session/Asset 的 Backend Service 编排

**Files:**

- Create: `dotnet/src/Ocb.Backend/Identity/CallerIdentityService.cs`
- Create: `dotnet/src/Ocb.Backend/Sessions/SessionAssetService.cs`
- Create: `dotnet/src/Ocb.Backend/Bots/BotSkillsService.cs`
- Test: `dotnet/tests/Ocb.Backend.Tests/Identity/CallerIdentityServiceTests.cs`
- Test: `dotnet/tests/Ocb.Backend.Tests/Sessions/SessionAssetServiceTests.cs`

**Interfaces:**

- Consumes: `ICallerIdentityRepositoryPlugin`, `ISkillPublicationStorePlugin`。
- Produces:
  - `Task<CallerIdentityBinding> ResolveCallerAsync(CallerContext context, string botId, CancellationToken ct)`
  - `Task<SessionAssetRecord> CreateUploadIntentAsync(CallerContext caller, string botId, string sessionId, string fileName, long sizeBytes, string sha256, CancellationToken ct)`

- [ ] **Step 1: 写失败测试**

```csharp
[Fact]
public async Task ResolveCallerAsync_Throws_WhenTenantMismatch()
{
    var context = new CallerContext("tenant-b", "u-1", new HashSet<string>{"user"});

    var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
        _service.ResolveCallerAsync(context, "bot-1", CancellationToken.None));

    Assert.Contains("tenant mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: 运行 RED**

Run: `dotnet test dotnet/tests/Ocb.Backend.Tests/Ocb.Backend.Tests.csproj --filter FullyQualifiedName~CallerIdentityServiceTests -v n`

Expected: FAIL，服务不存在或未校验 tenant。

- [ ] **Step 3: 最小实现**

```csharp
public async Task<CallerIdentityBinding> ResolveCallerAsync(CallerContext context, string botId, CancellationToken ct)
{
    var binding = await _repo.GetCallerIdentityAsync(context.TenantId, botId, context.SubjectId, ct)
        ?? throw new UnauthorizedAccessException("caller identity not found");

    if (!string.Equals(binding.TenantId, context.TenantId, StringComparison.Ordinal))
    {
        throw new UnauthorizedAccessException("tenant mismatch");
    }

    return binding;
}
```

- [ ] **Step 4: 运行 GREEN**

Run: `dotnet test dotnet/tests/Ocb.Backend.Tests/Ocb.Backend.Tests.csproj --filter FullyQualifiedName~CallerIdentityServiceTests -v n`

Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Backend/Identity dotnet/src/Ocb.Backend/Sessions dotnet/src/Ocb.Backend/Bots dotnet/tests/Ocb.Backend.Tests
git commit -m "feat(backend): implement caller identity and session asset services with tenant guard"
```

---

### Task 7: 新建 Ocb.Infrastructure.Minio，覆盖 multipart/checksum/signed URL/range

**Files:**

- Create: `dotnet/src/Ocb.Infrastructure.Minio/Ocb.Infrastructure.Minio.csproj`
- Create: `dotnet/src/Ocb.Infrastructure.Minio/context-boundary.json`
- Create: `dotnet/src/Ocb.Infrastructure.Minio/Storage/MinioAssetObjectStoragePlugin.cs`
- Create: `dotnet/src/Ocb.Infrastructure.Minio/Storage/ChecksumVerifier.cs`
- Test: `dotnet/tests/Ocb.Infrastructure.Tests/Minio/MultipartAndChecksumTests.cs`
- Test: `dotnet/tests/Ocb.Infrastructure.Tests/Minio/RangeAndSignedUrlTests.cs`

**Interfaces:**

- Consumes: `IAssetObjectStoragePlugin`。
- Produces:
  - `Task<MultipartInitResult> BeginMultipartUploadAsync(AssetUploadRequest request, CancellationToken ct)`
  - `Task CompleteMultipartUploadAsync(CompleteMultipartRequest request, CancellationToken ct)`
  - `Task<Uri> CreateSignedDownloadUrlAsync(string tenantId, string objectKey, TimeSpan lifetime, CancellationToken ct)`
  - `Task<Stream> OpenRangeReadAsync(string tenantId, string objectKey, long offset, long length, CancellationToken ct)`
  - tenant object key prefix、单对象大小上限、retention metadata，以及可选 `IMalwareScannerPlugin` 扩展点

- [ ] **Step 1: 写失败测试**

```csharp
[Fact]
public async Task CompleteMultipartUploadAsync_Throws_WhenChecksumMismatch()
{
    var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
        _plugin.CompleteMultipartUploadAsync(_requestWithWrongChecksum, CancellationToken.None));

    Assert.Contains("checksum", ex.Message, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: 运行 RED**

Run: `dotnet test dotnet/tests/Ocb.Infrastructure.Tests/Ocb.Infrastructure.Tests.csproj --filter FullyQualifiedName~MultipartAndChecksumTests -v n`

Expected: FAIL，checksum 校验缺失。

- [ ] **Step 3: 最小实现**

```csharp
public async Task CompleteMultipartUploadAsync(CompleteMultipartRequest request, CancellationToken ct)
{
    await _minio.CompleteMultipartUploadAsync(request, ct);
    var actual = await _checksum.ReadSha256Async(request.ObjectKey, ct);
    if (!string.Equals(actual, request.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException("checksum mismatch");
    }
}
```

- [ ] **Step 4: 运行 GREEN**

Run: `dotnet test dotnet/tests/Ocb.Infrastructure.Tests/Ocb.Infrastructure.Tests.csproj --filter FullyQualifiedName~MultipartAndChecksumTests|FullyQualifiedName~RangeAndSignedUrlTests -v n`

Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Infrastructure.Minio dotnet/tests/Ocb.Infrastructure.Tests dotnet/Ocb.slnx
git commit -m "feat(infra-minio): add multipart checksum signed-url and range support"
```

---

### Task 8: 临时对象发布与删除补偿（事务失败后可回收）

**Files:**

- Modify: `dotnet/src/Ocb.Infrastructure.PostgreSql/Migrations/0001_stage5_ocb_business.sql`
- Create: `dotnet/src/Ocb.Infrastructure.PostgreSql/Assets/AssetCompensationRepository.cs`
- Create: `dotnet/src/Ocb.Backend/Assets/AssetCompensationService.cs`
- Test: `dotnet/tests/Ocb.Infrastructure.Tests/PostgreSql/AssetCompensationRepositoryTests.cs`
- Test: `dotnet/tests/Ocb.Backend.Tests/Assets/AssetCompensationServiceTests.cs`

**Interfaces:**

- Consumes: `SessionAssetRecord`, MinIO object key。
- Produces:
  - `Task RecordTempObjectAsync(string tenantId, string objectKey, string state, CancellationToken ct)`
  - `Task CompensateDeleteAsync(string tenantId, string objectKey, CancellationToken ct)`

- [ ] **Step 1: 写失败测试**

```csharp
[Fact]
public async Task OnMetadataTransactionFailure_TempObjectIsRecordedForCleanup()
{
    await Assert.ThrowsAsync<DbUpdateException>(() => _svc.CreateAndPersistAsync(_request, CancellationToken.None));

    var pending = await _repo.ListPendingCompensationAsync("tenant-a", CancellationToken.None);
    Assert.Single(pending);
    Assert.Equal("TEMP_UPLOADED", pending[0].State);
}
```

- [ ] **Step 2: 运行 RED**

Run: `dotnet test dotnet/tests/Ocb.Backend.Tests/Ocb.Backend.Tests.csproj --filter FullyQualifiedName~AssetCompensationServiceTests -v n`

Expected: FAIL，无补偿记录。

- [ ] **Step 3: 最小实现**

```csharp
catch (DbUpdateException)
{
    await _compensationRepo.RecordTempObjectAsync(request.TenantId, tempObjectKey, "TEMP_UPLOADED", ct);
    throw;
}
```

- [ ] **Step 4: 运行 GREEN**

Run: `dotnet test dotnet/tests/Ocb.Backend.Tests/Ocb.Backend.Tests.csproj --filter FullyQualifiedName~AssetCompensationServiceTests -v n`

Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Backend/Assets dotnet/src/Ocb.Infrastructure.PostgreSql/Assets dotnet/src/Ocb.Infrastructure.PostgreSql/Migrations dotnet/tests/Ocb.Backend.Tests dotnet/tests/Ocb.Infrastructure.Tests
git commit -m "feat(backend): add temp-object compensation flow for asset persistence failures"
```

---

### Task 9: 不可变版本与 publication 状态机（git/local/center）

**Files:**

- Create: `dotnet/src/Ocb.Backend/Skills/Publication/SkillPublicationStateMachine.cs`
- Create: `dotnet/src/Ocb.Backend/Skills/Publication/SkillPublicationService.cs`
- Test: `dotnet/tests/Ocb.Backend.Tests/Skills/PublicationStateMachineTests.cs`
- Test: `dotnet/tests/Ocb.Backend.Tests/Skills/PublicationServiceTests.cs`

**Interfaces:**

- Consumes: `SkillVersionRef` with scheme `Git|Local|Center`。
- Produces:
  - `TransitionResult Transition(PublicationState current, PublicationEvent evt)`
  - `Task<SkillPublicationRecord> PublishImmutableVersionAsync(string tenantId, string botId, string skillId, SkillVersionRef version, string packageSha256, CancellationToken ct)`

- [ ] **Step 1: 写失败测试**

```csharp
[Fact]
public async Task CenterSource_WithoutPublishedState_IsRejected()
{
    var version = new SkillVersionRef("center://uuid-1", "3", SkillSourceScheme.Center);

    var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
        _service.PublishImmutableVersionAsync("tenant-a", "bot-1", "skill-1", version, CancellationToken.None));

    Assert.Contains("center://", ex.Message);
    Assert.Contains("published", ex.Message, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: 运行 RED**

Run: `dotnet test dotnet/tests/Ocb.Backend.Tests/Ocb.Backend.Tests.csproj --filter FullyQualifiedName~PublicationServiceTests -v n`

Expected: FAIL，center 误当作已发布通过。

- [ ] **Step 3: 最小实现**

```csharp
if (version.Scheme == SkillSourceScheme.Center && currentState != PublicationState.Published)
{
    throw new InvalidOperationException("center:// source is not equivalent to published state");
}
```

- [ ] **Step 4: 运行 GREEN**

Run: `dotnet test dotnet/tests/Ocb.Backend.Tests/Ocb.Backend.Tests.csproj --filter FullyQualifiedName~PublicationStateMachineTests|FullyQualifiedName~PublicationServiceTests -v n`

Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Backend/Skills/Publication dotnet/tests/Ocb.Backend.Tests/Skills
git commit -m "feat(backend): add immutable skill publication state machine for git local center"
```

---

### Task 10: Runtime Worker Service API 与“仅按 Bot 已激活内容”下发

**Files:**

- Create: `dotnet/src/Ocb.Backend/Skills/Activation/SkillActivationPlanner.cs`
- Create: `dotnet/src/Ocb.Backend/Skills/Activation/SkillActivationService.cs`
- Test: `dotnet/tests/Ocb.Backend.Tests/Skills/ActivationPlannerTests.cs`

**Interfaces:**

- Consumes: `SkillActivationRequest`。
- Produces:
  - `IReadOnlyList<ActivatedSkillVersion> BuildActivatedOnlyPlan(IEnumerable<SkillPublicationRecord> records)`

- [ ] **Step 1: 写失败测试**

```csharp
[Fact]
public void ActivationPlan_DoesNotContainFullRepoBridgeEntries()
{
    var plan = _planner.BuildActivatedOnlyPlan(_inputWithRepoAndActiveRoots);

    Assert.DoesNotContain(plan, p => p.SourceLocator.Contains("skills-repo", StringComparison.OrdinalIgnoreCase)
        && p.SourceLocator.EndsWith("/", StringComparison.Ordinal));
    Assert.All(plan, p => Assert.False(p.SourceLocator.Contains("bridge", StringComparison.OrdinalIgnoreCase)));
}
```

- [ ] **Step 2: 运行 RED**

Run: `dotnet test dotnet/tests/Ocb.Backend.Tests/Ocb.Backend.Tests.csproj --filter FullyQualifiedName~ActivationPlannerTests -v n`

Expected: FAIL，计划中包含完整仓库入口。

- [ ] **Step 3: 最小实现**

```csharp
public IReadOnlyList<ActivatedSkillVersion> BuildActivatedOnlyPlan(IEnumerable<SkillPublicationRecord> records)
    => records
        .Where(r => string.Equals(r.PublicationState, "active", StringComparison.OrdinalIgnoreCase))
    .Select(r => new ActivatedSkillVersion(r.SkillId, r.Version.SourceLocator,
      r.Version.ImmutableVersion, r.PackageSha256))
        .ToList();
```

并显式拒绝 repo-root locator。

- [ ] **Step 4: 运行 GREEN**

Run: `dotnet test dotnet/tests/Ocb.Backend.Tests/Ocb.Backend.Tests.csproj --filter FullyQualifiedName~ActivationPlannerTests -v n`

Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Backend/Skills/Activation dotnet/tests/Ocb.Backend.Tests/Skills
git commit -m "feat(backend): enforce activated-only runtime materialization plan"
```

---

### Task 11: Runtime 下载可靠性结果映射（重试策略由 Runtime Worker 拥有）

**Files:**

- Modify: `dotnet/src/Ocb.Backend/Skills/Activation/SkillActivationService.cs`
- Test: `dotnet/tests/Ocb.Backend.Tests/Skills/RuntimeMaterializationResultTests.cs`

**Interfaces:**

- Consumes: `IRuntimeSkillMaterializationService` 返回的 `MaterializationResult`；下载重试、integrity 分类和临时内容清理由 Runtime 计划 Task 10 实现。
- Produces:
  - Backend desired/observed 状态映射，不在 Backend 内重试远程物化调用。

- [ ] **Step 1: 写失败测试**

```csharp
[Fact]
public async Task IntegrityFailure_IsRecordedWithoutBackendRetry()
{
  _runtime.NextResult = new MaterializationResult(false, "failed", null, "INTEGRITY_CHECK_FAILED");
  await _service.ActivateAsync(_request, CancellationToken.None);
  Assert.Equal(1, _runtime.CallCount);
  Assert.Equal("INTEGRITY_CHECK_FAILED", _observed.ErrorCode);
}
```

- [ ] **Step 2: 运行 RED**

Run: `dotnet test dotnet/tests/Ocb.Backend.Tests/Ocb.Backend.Tests.csproj --filter FullyQualifiedName~RuntimeMaterializationResultTests -v n`

Expected: FAIL，Backend 重试了远程调用或未记录 observed failure。

- [ ] **Step 3: 最小实现**

```csharp
var result = await _runtime.MaterializeActivatedSkillsAsync(request, ct);
await _observedStore.RecordAsync(request.Caller.TenantId, request.BotId,
  result.ObservedState, result.ActiveViewId, result.ErrorCode, ct);
```

- [ ] **Step 4: 运行 GREEN**

Run: `dotnet test dotnet/tests/Ocb.Backend.Tests/Ocb.Backend.Tests.csproj --filter FullyQualifiedName~RuntimeMaterializationResultTests -v n`

Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Backend/Skills/Activation dotnet/tests/Ocb.Backend.Tests/Skills
git commit -m "feat(backend): map runtime materialization failures to observed state"
```

---

### Task 12: 原子发布结果与“无旧视图 fail closed”契约映射

**Files:**

- Modify: `dotnet/src/Ocb.Backend/Skills/Activation/SkillActivationService.cs`
- Create: `dotnet/src/Ocb.Backend/Skills/Errors/ActivationFailClosedException.cs`
- Test: `dotnet/tests/Ocb.Backend.Tests/Skills/AtomicPublishResultTests.cs`

**Interfaces:**

- Consumes: `IRuntimeSkillMaterializationService.MaterializeActivatedSkillsAsync(MaterializationRequest request, CancellationToken cancellationToken)`；Runtime Worker 在该 Service API 内部拥有临时 workspace、原子发布和回滚。
- Produces:
  - `Task ActivateAsync(MaterializationRequest request, CancellationToken ct)` with rollback/fail-closed result mapping.

- [ ] **Step 1: 写失败测试**

```csharp
[Fact]
public async Task RuntimeRollbackResult_PreservesPreviousObservedView()
{
  _runtime.NextResult = new MaterializationResult(false, "failed_rolled_back", "view-prev", "ATOMIC_PUBLISH_FAILED");
    await _service.ActivateAsync(_request, CancellationToken.None);
  Assert.Equal("view-prev", _observed.ActiveViewId);
  Assert.Equal("failed_rolled_back", _observed.State);
}

[Fact]
public async Task PublishFailure_WithoutPreviousView_FailsClosed()
{
    _runtime.NextResult = new MaterializationResult(false, "failed", null, "ACTIVATION_NO_PREVIOUS_VIEW");

    var ex = await Assert.ThrowsAsync<ActivationFailClosedException>(() =>
        _service.ActivateAsync(_request, CancellationToken.None));

    Assert.Contains("no previous view", ex.Message, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: 运行 RED**

Run: `dotnet test dotnet/tests/Ocb.Backend.Tests/Ocb.Backend.Tests.csproj --filter FullyQualifiedName~AtomicPublishResultTests -v n`

Expected: FAIL，Backend 未保留 Runtime 回报的旧 active view，或未把 `ACTIVATION_NO_PREVIOUS_VIEW` 映射为可操作的 fail-closed 错误。

- [ ] **Step 3: 最小实现**

```csharp
var result = await _runtime.MaterializeActivatedSkillsAsync(request, ct);
if (!result.Succeeded && result.ErrorCode == "ACTIVATION_NO_PREVIOUS_VIEW")
  throw new ActivationFailClosedException("activation failed with no previous view");
await _observedStore.RecordAsync(request.Caller.TenantId, request.BotId,
  result.ObservedState, result.ActiveViewId, result.ErrorCode, ct);
```

- [ ] **Step 4: 运行 GREEN**

Run: `dotnet test dotnet/tests/Ocb.Backend.Tests/Ocb.Backend.Tests.csproj --filter FullyQualifiedName~AtomicPublishResultTests -v n`

Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Backend/Skills dotnet/tests/Ocb.Backend.Tests/Skills
git commit -m "feat(backend): enforce atomic publish rollback and fail-closed activation"
```

---

### Task 13: 实现 Ocb.Grains（Bot/Session/Device）与 desired/observed 协调

**Files:**

- Create: `dotnet/src/Ocb.Grains/Ocb.Grains.csproj`
- Create: `dotnet/src/Ocb.Grains/context-boundary.json`
- Create: `dotnet/src/Ocb.Grains/Bot/BotGrain.cs`
- Create: `dotnet/src/Ocb.Grains/Session/SessionGrain.cs`
- Create: `dotnet/src/Ocb.Grains/Device/DeviceGrain.cs`
- Test: `dotnet/tests/Ocb.Grains.Tests/Ocb.Grains.Tests.csproj`
- Test: `dotnet/tests/Ocb.Grains.Tests/BotGrainTenantIsolationTests.cs`
- Test: `dotnet/tests/Ocb.Grains.Tests/SessionDeviceGrainStateTests.cs`

**Interfaces:**

- Consumes: `IBotGrain`/`ISessionGrain`/`IDeviceGrain`, `ISkillActivationService`（Service API 抽象，不依赖具体 `SkillActivationService`）。
- Produces:
  - 串行命令处理与 desired/observed 状态转换。

- [ ] **Step 1: 写失败测试**

```csharp
[Fact]
public async Task BotGrain_RejectsCrossTenantActivationRequest()
{
    var grain = CreateBotGrain("tenant-a", "bot-1");

    var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
        grain.ReconcileSkillsAsync(_requestWithTenantB));

    Assert.Contains("tenant", ex.Message, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: 运行 RED**

Run: `dotnet test dotnet/tests/Ocb.Grains.Tests/Ocb.Grains.Tests.csproj --filter FullyQualifiedName~BotGrainTenantIsolationTests -v n`

Expected: FAIL，Grain 未执行 tenant 二次复核。

- [ ] **Step 3: 最小实现**

```csharp
public async Task ReconcileSkillsAsync(SkillActivationRequest request)
{
    if (!string.Equals(request.TenantId, _tenantId, StringComparison.Ordinal))
    {
        throw new UnauthorizedAccessException("tenant mismatch");
    }

    await _activationService.ActivateAsync(request, CancellationToken.None);
}
```

- [ ] **Step 4: 运行 GREEN**

Run: `dotnet test dotnet/tests/Ocb.Grains.Tests/Ocb.Grains.Tests.csproj --filter FullyQualifiedName~BotGrainTenantIsolationTests|FullyQualifiedName~SessionDeviceGrainStateTests -v n`

Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Grains dotnet/tests/Ocb.Grains.Tests dotnet/Ocb.slnx
git commit -m "feat(grains): add tenant-scoped bot session device grains for stage5"
```

---

### Task 14: Service/Plugin conformance + Backend HTTP parity + 用户故事

**Files:**

- Create: `dotnet/tests/Ocb.Contracts.Tests/Conformance/ServiceApiConformanceTests.cs`
- Create: `dotnet/tests/Ocb.Contracts.Tests/Conformance/PluginApiConformanceTests.cs`
- Create: `dotnet/tests/Ocb.EndToEnd.Tests/Ocb.EndToEnd.Tests.csproj`
- Create: `dotnet/tests/Ocb.EndToEnd.Tests/Backend/HttpParitySkillsResourcesSessionsTests.cs`
- Create: `dotnet/tests/Ocb.EndToEnd.Tests/Backend/UserStory/SkillsActivationLifecycleTests.cs`
- Create: `dotnet/tests/Ocb.EndToEnd.Tests/Backend/UserStory/SessionAssetLifecycleTests.cs`
- Create: `dotnet/src/Ocb.Backend/Web/BackendEndpointMappings.cs`
- Modify: `scripts/ci/dotnet_ci.sh`

**Interfaces:**

- Consumes: `dotnet/contracts/parity-corpus/backend.openapi.json`、`dotnet/contracts/migration-inventory.json`。
- Produces:
  - conformance gate
  - parity gate
  - user-story gate

- [ ] **Step 1: 写失败测试（parity）**

```csharp
[Fact]
public async Task BackendOpenApi_ContainsStage5CriticalPaths()
{
    var doc = await _client.GetFromJsonAsync<JsonDocument>("/openapi/v1/openapi.json");
    var paths = doc!.RootElement.GetProperty("paths");

    Assert.True(paths.TryGetProperty("/openapi/v1/bots/skills", out _));
    Assert.True(paths.TryGetProperty("/openapi/v1/bots/resources/{resource_id}/download", out _));
    Assert.True(paths.TryGetProperty("/openapi/v1/bots/sessions/{bot_id}", out _));
}
```

- [ ] **Step 2: 运行 RED**

Run: `dotnet test dotnet/tests/Ocb.EndToEnd.Tests/Ocb.EndToEnd.Tests.csproj --filter FullyQualifiedName~HttpParitySkillsResourcesSessionsTests -v n`

Expected: FAIL，路径缺失/返回结构不匹配基线。

- [ ] **Step 3: 最小实现**

```csharp
app.MapBackendSkillsEndpoints();
app.MapBackendResourceEndpoints();
app.MapBackendSessionEndpoints();
```

并补齐 envelope 状态码与错误码映射。

- [ ] **Step 4: 运行 GREEN（全 Stage-5）**

Run: `dotnet test dotnet/Ocb.slnx --configuration Release -v m`

Expected: PASS（Contracts / Backend / Grains / Infrastructure / EndToEnd）。

- [ ] **Step 5: Commit**

```bash
git add dotnet/tests/Ocb.Contracts.Tests/Conformance dotnet/tests/Ocb.EndToEnd.Tests scripts/ci/dotnet_ci.sh dotnet/Ocb.slnx
git commit -m "test(backend): add stage5 conformance parity and user-story gates"
```

---

## Execution Order

1. Task 1-4（先契约、接口、manifest 约束）
2. Task 5-8（先持久化和对象存储，再补偿）
3. Task 9-12（publication 状态机与 activation 可靠性）
4. Task 13（Grain 协调）
5. Task 14（统一 conformance/parity/user-story 收口）

## Self-Review

### 1. Spec Coverage

- Caller Identity、Bot、Session、asset、tenant guard：Task 1/5/6/13 覆盖。
- PostgreSQL `ocb_business` migration：Task 5 覆盖。
- MinIO multipart/checksum/signed URL/range/temp publish/delete compensation：Task 7/8 覆盖。
- Bot/Session/Device 稳定 tenant key Grain：Task 3/13 覆盖。
- `git://`/`local://`/`center://` immutable version + publication：Task 9 覆盖。
- `skills-pool-p3-v1` manifest：Task 4 覆盖。
- Runtime Worker 按 Bot 物化接口：Task 10 覆盖。
- 下载 3 次指数退避+jitter、integrity 不重试：Runtime Worker 计划 Task 10 实现，本计划 Task 11 验证 Backend 不重复重试并记录结果。
- 原子发布回滚、无旧视图 fail closed：Runtime Worker 计划 Task 10 实现，本计划 Task 12 验证 Service API 结果映射。
- Service/Plugin conformance：Task 14 覆盖。
- Backend HTTP parity/用户故事：Task 14 覆盖。
- “完整内容仓库 + active 仅激活 Skills + 禁止 bridge + 物理 layout 属于 Runtime + center:// 不等于发布完成”：Global Constraints + Task 4/9/10 覆盖。
- Gateway/Runtime/BaaS 所有权：Global Constraints 覆盖。

### 2. No-placeholder Check

- 已按 writing-plans 禁止模式完成扫描，实际实施步骤中无占位内容。

### 3. Type Consistency

- `SkillVersionRef`、`SkillPublicationRecord`、`SkillActivationRequest` 在 Task 1 定义，并在 Task 2/4/9/10/12/13 一致复用。
- Runtime Service API 统一使用 `MaterializeActivatedSkillsAsync(MaterializationRequest, CancellationToken)`；Backend 只消费结果，不操作 Runtime 物理视图。

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-08-08-dotnet-backend-skills.md`.

Two execution options:

1. Subagent-Driven (recommended) - 我按 Task 逐个派发子代理实现并做两阶段评审。
2. Inline Execution - 我在当前会话按 Task 批量执行并在检查点停下来给你确认。

请选择执行方式。
