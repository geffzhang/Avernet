# .NET 共同基座与契约基线实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立可构建、受架构门禁保护的 .NET 10 Solution，并用结构化清单和基线 corpus 固化五个 Python 服务的迁移边界。

**Architecture:** 本阶段只建立 Contracts、Core、Plugin API、Configuration、架构测试和 CI，不实现业务 endpoint、Grain 或数据库。迁移清单通过 Python AST 与 JSON/YAML 解析现有仓库生成，成为后续五个服务计划的权威输入；Python 保持原样作为行为基线。

**Tech Stack:** .NET SDK 10.0.302、C# 14、`System.Text.Json` source generation、xUnit 2.9.3、Microsoft.NET.Test.Sdk 17.14.1、xunit.runner.visualstudio 3.1.4、coverlet.collector 6.0.4、Python 3.12 AST/JSON/YAML、GitHub Actions。

## Global Constraints

- 全部项目目标框架为 `net10.0`，`LangVersion=14`，启用 nullable、implicit usings 和 warnings-as-errors。
- 只使用公共稳定 package；版本集中在 `dotnet/Directory.Packages.props`。
- 本阶段不得添加 Orleans、TickerQ、EF Core、MinIO、SonnetDB、Qdrant 或 ASP.NET Core 业务实现依赖。
- 禁止任何 `OpenClaw.*` ProjectReference、源码链接或 NuGet 依赖。
- Service API 位于 `Ocb.Contracts`，Plugin API 位于 `Ocb.PluginApi`，两者不能合并。
- `Ocb.Core` 只能引用 `Ocb.Contracts` 和 `Ocb.PluginApi`。
- Context Boundary 使用 `context-boundary.json`，字段语义等价于 `docs/arch/context-boundary-format.md` 的 `purpose`、`provides`、`consumes`、`internal_dependencies` 和 change impact。
- 迁移清单必须使用 Python AST、JSON 或 YAML 解析，不允许用正则猜测 FastAPI route。
- 不修改 frontend、Rust BCS、五个 Python 服务实现、singlebox 启动顺序或数据库所有权。
- 不创建 `.NET bastion`、Python bridge、BCS bridge 或混合生产路由。
- 每个任务完成后运行其窄测试并单独提交。
- 规格来源：`docs/superpowers/specs/2026-08-08-dotnet-orleans-python-services-migration-design.md`。

---

## File Structure

**Solution 与生产项目：**

- Create `dotnet/Ocb.slnx`：.NET Solution 清单。
- Create `dotnet/global.json`：固定 SDK `10.0.302`，允许 feature-band roll-forward。
- Create `dotnet/Directory.Build.props`：统一 `net10.0`、C# 14、nullable、warnings-as-errors。
- Create `dotnet/Directory.Packages.props`：集中锁定测试包版本。
- Create `dotnet/src/Ocb.Contracts/Ocb.Contracts.csproj`：Service API 与共享 wire model。
- Create `dotnet/src/Ocb.Core/Ocb.Core.csproj`：传输无关的领域基础类型。
- Create `dotnet/src/Ocb.PluginApi/Ocb.PluginApi.csproj`：基础设施 Plugin API。
- Create `dotnet/src/Ocb.Configuration/Ocb.Configuration.csproj`：Profile 与 Provider 组合校验。
- Create `dotnet/src/*/context-boundary.json`：每个生产项目的边界声明。

**测试项目：**

- Create `dotnet/tests/Ocb.Architecture.Tests/Ocb.Architecture.Tests.csproj`：项目引用、包引用、环境访问、边界元数据门禁。
- Create `dotnet/tests/Ocb.Contracts.Tests/Ocb.Contracts.Tests.csproj`：Service/Plugin contract 和序列化测试。
- Create `dotnet/tests/Ocb.Architecture.Tests/RepositoryPaths.cs`：稳定解析仓库根目录。
- Create `dotnet/tests/Ocb.Architecture.Tests/ContextBoundaryTests.cs`。
- Create `dotnet/tests/Ocb.Architecture.Tests/DependencyBoundaryTests.cs`。
- Create `dotnet/tests/Ocb.Contracts.Tests/CallerContextTests.cs`。
- Create `dotnet/tests/Ocb.Contracts.Tests/DeploymentProfileTests.cs`。

**契约清单：**

- Create `scripts/dotnet/export_contract_inventory.py`：结构化提取 route、OpenAPI、WebSocket/SSE 文档和 Plugin Protocol。
- Create `scripts/dotnet/tests/test_export_contract_inventory.py`：提取器单元测试。
- Create `dotnet/contracts/migration-inventory.json`：生成后提交的迁移清单。
- Create `dotnet/contracts/parity-corpus/manifest.json`：基线 artifact 与校验和清单。
- Create `dotnet/contracts/parity-corpus/backend.openapi.json`。
- Create `dotnet/contracts/parity-corpus/baas.openapi.json`。
- Copy `src/bcsfuse/schemas/openapi.yaml` to `dotnet/contracts/parity-corpus/bcsfuse.openapi.yaml`。
- Copy `src/engine/src/engine/community/claude_code_gateway/docs/websocket-protocol.md` to `dotnet/contracts/parity-corpus/engine-websocket-protocol.md`。

**CI：**

- Create `scripts/ci/dotnet_ci.sh`：restore、format、build、test 的统一入口。
- Create `scripts/ci/tests/test_pre_push_dotnet_gate.py`：验证 `dotnet/` 变更触发 .NET gate。
- Modify `scripts/ci/pre_push.sh`：增加 `dotnet/` 分发。
- Modify `.github/workflows/unit-tests.yml`：增加 .NET job。
- Create `scripts/ci/tests/test_unit_test_workflow_dotnet.py`：验证 workflow 路径检测和命令。
- Modify `docs/arch/ci.enforce.md`：记录 .NET 架构、契约和测试门禁。

---

### Task 1: 创建可构建的 .NET 10 Solution

**Files:**

- Create: `dotnet/global.json`
- Create: `dotnet/Directory.Build.props`
- Create: `dotnet/Directory.Packages.props`
- Create: `dotnet/Ocb.slnx`
- Create: `dotnet/src/Ocb.Contracts/Ocb.Contracts.csproj`
- Create: `dotnet/src/Ocb.Core/Ocb.Core.csproj`
- Create: `dotnet/src/Ocb.PluginApi/Ocb.PluginApi.csproj`
- Create: `dotnet/src/Ocb.Configuration/Ocb.Configuration.csproj`
- Create: `dotnet/tests/Ocb.Architecture.Tests/Ocb.Architecture.Tests.csproj`
- Create: `dotnet/tests/Ocb.Contracts.Tests/Ocb.Contracts.Tests.csproj`

**Interfaces:**

- Consumes: .NET SDK `10.0.302`。
- Produces: `dotnet/Ocb.slnx`；后续任务通过 `dotnet test Ocb.slnx` 验证。

- [ ] **Step 1: 运行缺失 Solution 的失败检查**

Run:

```powershell
Test-Path dotnet/Ocb.slnx
```

Expected: 输出 `False`。

- [ ] **Step 2: 创建目录和 SDK 锁定文件**

Create `dotnet/global.json`：

```json
{
  "sdk": {
    "version": "10.0.302",
    "rollForward": "latestFeature",
    "allowPrerelease": false
  }
}
```

Create `dotnet/Directory.Build.props`：

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>14</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <IsPackable>false</IsPackable>
    <Deterministic>true</Deterministic>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
  </PropertyGroup>
</Project>
```

Create `dotnet/Directory.Packages.props`：

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="coverlet.collector" Version="6.0.4" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageVersion Include="xunit" Version="2.9.3" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: 用 SDK 创建项目和 Solution**

Run from repository root：

```powershell
New-Item -ItemType Directory -Force dotnet/src, dotnet/tests | Out-Null
dotnet new sln --name Ocb --format slnx --output dotnet
dotnet new classlib --name Ocb.Contracts --output dotnet/src/Ocb.Contracts --framework net10.0 --no-restore
dotnet new classlib --name Ocb.Core --output dotnet/src/Ocb.Core --framework net10.0 --no-restore
dotnet new classlib --name Ocb.PluginApi --output dotnet/src/Ocb.PluginApi --framework net10.0 --no-restore
dotnet new classlib --name Ocb.Configuration --output dotnet/src/Ocb.Configuration --framework net10.0 --no-restore
dotnet new xunit --name Ocb.Architecture.Tests --output dotnet/tests/Ocb.Architecture.Tests --framework net10.0 --no-restore
dotnet new xunit --name Ocb.Contracts.Tests --output dotnet/tests/Ocb.Contracts.Tests --framework net10.0 --no-restore
dotnet sln dotnet/Ocb.slnx add dotnet/src/Ocb.Contracts/Ocb.Contracts.csproj dotnet/src/Ocb.Core/Ocb.Core.csproj dotnet/src/Ocb.PluginApi/Ocb.PluginApi.csproj dotnet/src/Ocb.Configuration/Ocb.Configuration.csproj dotnet/tests/Ocb.Architecture.Tests/Ocb.Architecture.Tests.csproj dotnet/tests/Ocb.Contracts.Tests/Ocb.Contracts.Tests.csproj
```

Expected: 每条命令成功，Solution 包含 6 个项目。

- [ ] **Step 4: 设定项目引用并移除模板文件**

Run：

```powershell
dotnet add dotnet/src/Ocb.Core/Ocb.Core.csproj reference dotnet/src/Ocb.Contracts/Ocb.Contracts.csproj dotnet/src/Ocb.PluginApi/Ocb.PluginApi.csproj
dotnet add dotnet/tests/Ocb.Contracts.Tests/Ocb.Contracts.Tests.csproj reference dotnet/src/Ocb.Contracts/Ocb.Contracts.csproj dotnet/src/Ocb.PluginApi/Ocb.PluginApi.csproj dotnet/src/Ocb.Configuration/Ocb.Configuration.csproj
Remove-Item dotnet/src/Ocb.Contracts/Class1.cs, dotnet/src/Ocb.Core/Class1.cs, dotnet/src/Ocb.PluginApi/Class1.cs, dotnet/src/Ocb.Configuration/Class1.cs, dotnet/tests/Ocb.Architecture.Tests/UnitTest1.cs, dotnet/tests/Ocb.Contracts.Tests/UnitTest1.cs
```

Edit both test `.csproj` files so package references omit `Version` and keep template metadata：

```xml
<ItemGroup>
  <PackageReference Include="coverlet.collector">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  </PackageReference>
  <PackageReference Include="Microsoft.NET.Test.Sdk" />
  <PackageReference Include="xunit" />
  <PackageReference Include="xunit.runner.visualstudio">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  </PackageReference>
</ItemGroup>
```

- [ ] **Step 5: Restore 和 build**

Run：

```powershell
dotnet restore dotnet/Ocb.slnx --use-lock-file
dotnet build dotnet/Ocb.slnx --no-restore --configuration Release
```

Expected: `Build succeeded.`，0 warnings，0 errors，并生成 `dotnet/packages.lock.json` 或各项目 lock file；将生成的 lock file 一并提交。

- [ ] **Step 6: Commit**

```bash
git add dotnet
git commit -m "build(dotnet): add .NET 10 solution foundation"
```

---

### Task 2: 建立 Context Boundary 元数据门禁

**Files:**

- Create: `dotnet/src/Ocb.Contracts/context-boundary.json`
- Create: `dotnet/src/Ocb.Core/context-boundary.json`
- Create: `dotnet/src/Ocb.PluginApi/context-boundary.json`
- Create: `dotnet/src/Ocb.Configuration/context-boundary.json`
- Create: `dotnet/tests/Ocb.Architecture.Tests/RepositoryPaths.cs`
- Create: `dotnet/tests/Ocb.Architecture.Tests/ContextBoundaryTests.cs`

**Interfaces:**

- Consumes: `context-boundary.json` schema：`purpose: string`、`provides/consumes/internalDependencies: string[]`、`changeImpact: string`。
- Produces: 所有 `dotnet/src/Ocb.*` 项目的边界完整性测试。

- [ ] **Step 1: 写失败测试**

Create `RepositoryPaths.cs`：

```csharp
namespace Ocb.Architecture.Tests;

internal static class RepositoryPaths
{
    public static DirectoryInfo Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
        {
            directory = directory.Parent;
        }

        return directory ?? throw new DirectoryNotFoundException("Repository root containing AGENTS.md was not found.");
    }
}
```

Create `ContextBoundaryTests.cs`：

```csharp
using System.Text.Json;

namespace Ocb.Architecture.Tests;

public sealed class ContextBoundaryTests
{
    [Fact]
    public void EveryProductionProjectDeclaresAValidContextBoundary()
    {
        var sourceRoot = Path.Combine(RepositoryPaths.Root().FullName, "dotnet", "src");
        var projects = Directory.GetDirectories(sourceRoot, "Ocb.*", SearchOption.TopDirectoryOnly);

        Assert.NotEmpty(projects);
        foreach (var project in projects)
        {
            var path = Path.Combine(project, "context-boundary.json");
            Assert.True(File.Exists(path), $"Missing {path}");
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("purpose").GetString()));
            Assert.Equal(JsonValueKind.Array, root.GetProperty("provides").ValueKind);
            Assert.Equal(JsonValueKind.Array, root.GetProperty("consumes").ValueKind);
            Assert.Equal(JsonValueKind.Array, root.GetProperty("internal_dependencies").ValueKind);
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("change_impact").GetString()));
        }
    }
}
```

- [ ] **Step 2: 运行测试并确认失败**

Run：

```powershell
dotnet test dotnet/tests/Ocb.Architecture.Tests/Ocb.Architecture.Tests.csproj --filter FullyQualifiedName~ContextBoundaryTests
```

Expected: FAIL，消息包含 `Missing ...context-boundary.json`。

- [ ] **Step 3: 添加四份边界声明**

Create `Ocb.Contracts/context-boundary.json`：

```json
{
  "purpose": "定义消费者调用 OCB 核心时使用的 Service API 与共享线协议模型。",
  "provides": ["CallerContext", "TenantEntityKey", "DomainError"],
  "consumes": [],
  "internal_dependencies": [],
  "change_impact": "字段或序列化名称变化会影响 Gateway、Grain、Runtime Worker 和外部契约。"
}
```

Create `Ocb.PluginApi/context-boundary.json`：

```json
{
  "purpose": "定义 OCB 核心调用基础设施实现时使用的 Plugin API。",
  "provides": ["IPluginContract"],
  "consumes": [],
  "internal_dependencies": [],
  "change_impact": "接口变化要求所有 Provider 与对应 conformance suite 同步更新。"
}
```

Create `Ocb.Core/context-boundary.json`：

```json
{
  "purpose": "承载传输无关、基础设施无关的领域规则与应用协调。",
  "provides": [],
  "consumes": ["Ocb.Contracts", "Ocb.PluginApi"],
  "internal_dependencies": ["Ocb.Contracts", "Ocb.PluginApi"],
  "change_impact": "领域规则变化会影响 Service API 消费者、Plugin 调用和后续 Grain 协调。"
}
```

Create `Ocb.Configuration/context-boundary.json`：

```json
{
  "purpose": "定义部署 Profile、Provider 选择与启动前配置校验。",
  "provides": ["DeploymentProfile", "OcbPlatformOptions", "OcbPlatformOptionsValidator"],
  "consumes": [],
  "internal_dependencies": [],
  "change_impact": "配置组合变化会影响 singlebox、cluster、测试 Profile 和所有 Composition Root。"
}
```

- [ ] **Step 4: 运行测试并确认通过**

Run：

```powershell
dotnet test dotnet/tests/Ocb.Architecture.Tests/Ocb.Architecture.Tests.csproj --filter FullyQualifiedName~ContextBoundaryTests
```

Expected: PASS，1 test passed。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/*/context-boundary.json dotnet/tests/Ocb.Architecture.Tests
git commit -m "test(dotnet): enforce context boundary metadata"
```

---

### Task 3: 强制项目依赖与环境访问边界

**Files:**

- Create: `dotnet/tests/Ocb.Architecture.Tests/DependencyBoundaryTests.cs`

**Interfaces:**

- Consumes: `.csproj` ProjectReference/PackageReference、C# source。
- Produces: Core/Contracts/PluginApi 的依赖白名单和原始环境访问门禁。

- [ ] **Step 1: 写会被探针违规触发的测试**

Create `DependencyBoundaryTests.cs`：

```csharp
using System.Xml.Linq;

namespace Ocb.Architecture.Tests;

public sealed class DependencyBoundaryTests
{
    private static readonly string[] FrameworkPackages =
    [
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.Orleans.Server",
        "Minio",
        "Qdrant",
        "Sonnet"
    ];

    [Theory]
    [InlineData("Ocb.Contracts")]
    [InlineData("Ocb.PluginApi")]
    public void ContractProjectsHaveNoProjectReferences(string projectName)
    {
        var project = LoadProject(projectName);
        Assert.Empty(project.Descendants("ProjectReference"));
    }

    [Fact]
    public void CoreReferencesOnlyContractsAndPluginApi()
    {
        var project = LoadProject("Ocb.Core");
        var references = project.Descendants("ProjectReference")
            .Select(element => Path.GetFileNameWithoutExtension(element.Attribute("Include")!.Value))
            .Order()
            .ToArray();

        Assert.Equal(["Ocb.Contracts", "Ocb.PluginApi"], references);
    }

    [Theory]
    [InlineData("Ocb.Contracts")]
    [InlineData("Ocb.Core")]
    [InlineData("Ocb.PluginApi")]
    public void CoreAndContractsDoNotReferenceFrameworkImplementations(string projectName)
    {
        var project = LoadProject(projectName);
        var packages = project.Descendants("PackageReference")
            .Select(element => element.Attribute("Include")!.Value)
            .ToArray();

        Assert.DoesNotContain(packages, package => FrameworkPackages.Any(package.StartsWith));
    }

    [Fact]
    public void RawEnvironmentAccessIsConfinedToConfigurationProject()
    {
        var sourceRoot = Path.Combine(RepositoryPaths.Root().FullName, "dotnet", "src");
        var offenders = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Ocb.Configuration{Path.DirectorySeparatorChar}"))
            .Where(path => File.ReadAllText(path).Contains("Environment.GetEnvironmentVariable", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(offenders);
    }

    private static XDocument LoadProject(string projectName)
    {
        var path = Path.Combine(RepositoryPaths.Root().FullName, "dotnet", "src", projectName, $"{projectName}.csproj");
        return XDocument.Load(path);
    }
}
```

- [ ] **Step 2: 添加临时违规并确认测试失败**

Temporarily add this line to a new `dotnet/src/Ocb.Core/BoundaryProbe.cs`：

```csharp
_ = Environment.GetEnvironmentVariable("OCB_BOUNDARY_PROBE");
```

Run：

```powershell
dotnet test dotnet/tests/Ocb.Architecture.Tests/Ocb.Architecture.Tests.csproj --filter FullyQualifiedName~DependencyBoundaryTests
```

Expected: FAIL at `RawEnvironmentAccessIsConfinedToConfigurationProject`，offender 包含 `BoundaryProbe.cs`。

- [ ] **Step 3: 删除探针并确认全部通过**

Run：

```powershell
Remove-Item dotnet/src/Ocb.Core/BoundaryProbe.cs
dotnet test dotnet/tests/Ocb.Architecture.Tests/Ocb.Architecture.Tests.csproj --filter FullyQualifiedName~DependencyBoundaryTests
```

Expected: PASS，4 theories/facts 全部通过。

- [ ] **Step 4: Commit**

```bash
git add dotnet/tests/Ocb.Architecture.Tests/DependencyBoundaryTests.cs
git commit -m "test(dotnet): enforce dependency boundaries"
```

---

### Task 4: 定义最小 Service API 与序列化契约

**Files:**

- Create: `dotnet/src/Ocb.Contracts/CallerContext.cs`
- Create: `dotnet/src/Ocb.Contracts/TenantEntityKey.cs`
- Create: `dotnet/src/Ocb.Contracts/DomainError.cs`
- Create: `dotnet/src/Ocb.Contracts/OcbJsonContext.cs`
- Create: `dotnet/src/Ocb.PluginApi/IPluginContract.cs`
- Create: `dotnet/tests/Ocb.Contracts.Tests/CallerContextTests.cs`

**Interfaces:**

- Produces: `CallerContext(string TenantId, string SubjectId, IReadOnlySet<string> Roles)`、`TenantEntityKey.Create(string, string)`、`DomainError`、`IPluginContract`。
- Consumes: `System.Text.Json` source generation。

- [ ] **Step 1: 写失败测试**

Create `CallerContextTests.cs`：

```csharp
using System.Text.Json;
using Ocb.Contracts;

namespace Ocb.Contracts.Tests;

public sealed class CallerContextTests
{
    [Fact]
    public void CallerContextRoundTripsWithStableWireNames()
    {
        var caller = new CallerContext("tenant-1", "user-7", new HashSet<string> { "admin" });

        var json = JsonSerializer.Serialize(caller, OcbJsonContext.Default.CallerContext);
        var roundTrip = JsonSerializer.Deserialize(json, OcbJsonContext.Default.CallerContext);

        Assert.Equal("{\"tenant_id\":\"tenant-1\",\"subject_id\":\"user-7\",\"roles\":[\"admin\"]}", json);
        Assert.Equal(caller.TenantId, roundTrip!.TenantId);
        Assert.Equal(caller.SubjectId, roundTrip.SubjectId);
        Assert.Equal(caller.Roles, roundTrip.Roles);
    }

    [Theory]
    [InlineData("", "bot-1")]
    [InlineData("tenant-1", "")]
    public void TenantEntityKeyRejectsMissingRequiredParts(string tenantId, string entityId)
    {
        Assert.Throws<ArgumentException>(() => TenantEntityKey.Create(tenantId, entityId));
    }
}
```

- [ ] **Step 2: 运行测试并确认编译失败**

Run：

```powershell
dotnet test dotnet/tests/Ocb.Contracts.Tests/Ocb.Contracts.Tests.csproj --filter FullyQualifiedName~CallerContextTests
```

Expected: FAIL to compile，`CallerContext`、`OcbJsonContext` 和 `TenantEntityKey` 不存在。

- [ ] **Step 3: 实现最小契约**

Create `CallerContext.cs`：

```csharp
using System.Text.Json.Serialization;

namespace Ocb.Contracts;

public sealed record CallerContext(
    [property: JsonPropertyName("tenant_id")] string TenantId,
    [property: JsonPropertyName("subject_id")] string SubjectId,
    [property: JsonPropertyName("roles")] IReadOnlySet<string> Roles);
```

Create `TenantEntityKey.cs`：

```csharp
namespace Ocb.Contracts;

public readonly record struct TenantEntityKey(string TenantId, string EntityId)
{
    public static TenantEntityKey Create(string tenantId, string entityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        return new TenantEntityKey(tenantId, entityId);
    }

    public override string ToString() => $"{TenantId}:{EntityId}";
}
```

Create `DomainError.cs`：

```csharp
namespace Ocb.Contracts;

public sealed record DomainError(string Code, string Message, bool Retryable = false);
```

Create `OcbJsonContext.cs`：

```csharp
using System.Text.Json.Serialization;

namespace Ocb.Contracts;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(CallerContext))]
[JsonSerializable(typeof(DomainError))]
public partial class OcbJsonContext : JsonSerializerContext;
```

Create `IPluginContract.cs`：

```csharp
namespace Ocb.PluginApi;

public interface IPluginContract;
```

- [ ] **Step 4: 运行测试和架构门禁**

Run：

```powershell
dotnet test dotnet/tests/Ocb.Contracts.Tests/Ocb.Contracts.Tests.csproj --filter FullyQualifiedName~CallerContextTests
dotnet test dotnet/tests/Ocb.Architecture.Tests/Ocb.Architecture.Tests.csproj
```

Expected: 两条命令均 PASS。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Contracts dotnet/src/Ocb.PluginApi dotnet/tests/Ocb.Contracts.Tests/CallerContextTests.cs
git commit -m "feat(dotnet): add shared service contract primitives"
```

---

### Task 5: 建立严格部署 Profile 校验

**Files:**

- Create: `dotnet/src/Ocb.Configuration/DeploymentProfile.cs`
- Create: `dotnet/src/Ocb.Configuration/OcbPlatformOptions.cs`
- Create: `dotnet/src/Ocb.Configuration/OcbPlatformOptionsValidator.cs`
- Create: `dotnet/tests/Ocb.Contracts.Tests/DeploymentProfileTests.cs`

**Interfaces:**

- Produces: `DeploymentProfile.Singlebox|Cluster|Test`、`VectorProvider.SonnetDb|Qdrant|InMemory`、`ValidationResult`。
- Enforces: singlebox→SonnetDB、cluster→Qdrant、test→InMemory。

- [ ] **Step 1: 写失败测试**

Create `DeploymentProfileTests.cs`：

```csharp
using Ocb.Configuration;

namespace Ocb.Contracts.Tests;

public sealed class DeploymentProfileTests
{
    [Theory]
    [InlineData(DeploymentProfile.Singlebox, VectorProvider.SonnetDb)]
    [InlineData(DeploymentProfile.Cluster, VectorProvider.Qdrant)]
    [InlineData(DeploymentProfile.Test, VectorProvider.InMemory)]
    public void ValidProfileProviderPairsPass(DeploymentProfile profile, VectorProvider provider)
    {
        Assert.True(OcbPlatformOptionsValidator.Validate(new OcbPlatformOptions(profile, provider)).IsValid);
    }

    [Theory]
    [InlineData(DeploymentProfile.Singlebox, VectorProvider.Qdrant)]
    [InlineData(DeploymentProfile.Cluster, VectorProvider.SonnetDb)]
    [InlineData(DeploymentProfile.Cluster, VectorProvider.InMemory)]
    public void InvalidProfileProviderPairsFailClosed(DeploymentProfile profile, VectorProvider provider)
    {
        var result = OcbPlatformOptionsValidator.Validate(new OcbPlatformOptions(profile, provider));
        Assert.False(result.IsValid);
        Assert.Equal("vector_provider_not_allowed_for_profile", result.ErrorCode);
    }
}
```

- [ ] **Step 2: 运行测试并确认编译失败**

Run：

```powershell
dotnet test dotnet/tests/Ocb.Contracts.Tests/Ocb.Contracts.Tests.csproj --filter FullyQualifiedName~DeploymentProfileTests
```

Expected: FAIL to compile，配置类型不存在。

- [ ] **Step 3: 实现配置模型与校验器**

Create `DeploymentProfile.cs`：

```csharp
namespace Ocb.Configuration;

public enum DeploymentProfile { Singlebox, Cluster, Test }
public enum VectorProvider { SonnetDb, Qdrant, InMemory }
```

Create `OcbPlatformOptions.cs`：

```csharp
namespace Ocb.Configuration;

public sealed record OcbPlatformOptions(DeploymentProfile Profile, VectorProvider VectorProvider);
public sealed record ValidationResult(bool IsValid, string? ErrorCode)
{
    public static ValidationResult Success { get; } = new(true, null);
    public static ValidationResult Failure(string code) => new(false, code);
}
```

Create `OcbPlatformOptionsValidator.cs`：

```csharp
namespace Ocb.Configuration;

public static class OcbPlatformOptionsValidator
{
    public static ValidationResult Validate(OcbPlatformOptions options)
    {
        var valid = options.Profile switch
        {
            DeploymentProfile.Singlebox => options.VectorProvider is VectorProvider.SonnetDb,
            DeploymentProfile.Cluster => options.VectorProvider is VectorProvider.Qdrant,
            DeploymentProfile.Test => options.VectorProvider is VectorProvider.InMemory,
            _ => false
        };

        return valid
            ? ValidationResult.Success
            : ValidationResult.Failure("vector_provider_not_allowed_for_profile");
    }
}
```

- [ ] **Step 4: 运行窄测试**

Run：

```powershell
dotnet test dotnet/tests/Ocb.Contracts.Tests/Ocb.Contracts.Tests.csproj --filter FullyQualifiedName~DeploymentProfileTests
```

Expected: PASS，6 cases passed。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Configuration dotnet/tests/Ocb.Contracts.Tests/DeploymentProfileTests.cs
git commit -m "feat(dotnet): validate deployment provider profiles"
```

---

### Task 6: 生成结构化迁移契约清单

**Files:**

- Create: `scripts/dotnet/export_contract_inventory.py`
- Create: `scripts/dotnet/tests/test_export_contract_inventory.py`
- Create: `dotnet/contracts/migration-inventory.json`

**Interfaces:**

- Consumes: Python AST；`src/bcsfuse/schemas/openapi.yaml`；五个服务的 `app.py`、`router.py`、Schema 与协议文档。
- Produces: JSON object `{version, generated_from_commit, services}`；每个 service 包含 `entrypoints`、`http_routes`、`websocket_routes`、`sse_routes`、`schemas`、`plugin_protocols`、`protocol_documents`。

- [ ] **Step 1: 写提取器单元测试**

Create `test_export_contract_inventory.py`：

```python
import ast
import importlib.util
from pathlib import Path


_SCRIPT = Path(__file__).parents[1] / "export_contract_inventory.py"
_SPEC = importlib.util.spec_from_file_location("export_contract_inventory", _SCRIPT)
assert _SPEC and _SPEC.loader
_MODULE = importlib.util.module_from_spec(_SPEC)
_SPEC.loader.exec_module(_MODULE)


def test_extract_fastapi_routes_uses_ast_and_captures_method_path(tmp_path: Path) -> None:
    source = tmp_path / "router.py"
    source.write_text(
        "from fastapi import APIRouter\n"
        "router = APIRouter()\n"
        "@router.get('/health')\n"
        "def health(): return {'ok': True}\n",
        encoding="utf-8",
    )

    routes = _MODULE.extract_fastapi_routes(source)

    assert routes == [{"method": "GET", "path": "/health", "source": str(source)}]
    ast.parse(source.read_text(encoding="utf-8"))


def test_inventory_declares_all_migrated_services(repo_root: Path = Path(__file__).parents[3]) -> None:
    inventory = _MODULE.build_inventory(repo_root)
    assert set(inventory["services"]) == {"backend", "engine", "baas", "gateway", "bcsfuse"}
    for service in inventory["services"].values():
        assert service["entrypoints"]
```

- [ ] **Step 2: 运行测试并确认失败**

Run：

```powershell
python -m pytest scripts/dotnet/tests/test_export_contract_inventory.py -v
```

Expected: FAIL，`export_contract_inventory.py` 不存在。

- [ ] **Step 3: 实现 AST route 提取和固定服务配置**

Create `export_contract_inventory.py`，使用以下核心结构；实现中保持字段和函数签名不变：

```python
from __future__ import annotations

import argparse
import ast
import json
import subprocess
from pathlib import Path
from typing import Any


SERVICE_CONFIG = {
    "backend": {
        "root": "src/backend",
        "entrypoints": ["src/agentclaw/community/adapters/http/app.py"],
        "route_globs": ["src/agentclaw/community/adapters/http/**/*router.py"],
        "protocol_globs": ["src/agentclaw/community/plugins/**/*.py"],
        "protocol_documents": [],
    },
    "engine": {
        "root": "src/engine",
        "entrypoints": ["start.py", "src/engine/community/api/app.py"],
        "route_globs": ["src/engine/community/api/**/*.py"],
        "protocol_globs": ["src/engine/community/**/*.py"],
        "protocol_documents": ["src/engine/community/claude_code_gateway/docs/websocket-protocol.md"],
    },
    "baas": {
        "root": "src/baas",
        "entrypoints": ["src/secbaas/community/main.py", "src/secbaas/community/adapters/web/app.py"],
        "route_globs": ["src/secbaas/community/api/**/*.py"],
        "protocol_globs": ["src/secbaas/community/**/*.py"],
        "protocol_documents": [],
    },
    "gateway": {
        "root": "src/gateway",
        "entrypoints": ["src/gateway/community/main.py", "src/gateway/community/adapters/web/app.py"],
        "route_globs": ["src/gateway/community/adapters/web/**/*.py"],
        "protocol_globs": ["src/gateway/community/spi/**/*.py"],
        "protocol_documents": [],
    },
    "bcsfuse": {
        "root": "src/bcsfuse",
        "entrypoints": ["main.py", "src/interfaces/api/app.py"],
        "route_globs": ["src/interfaces/api/**/*.py", "servers/web/**/*.py"],
        "protocol_globs": ["src/**/*.py"],
        "protocol_documents": ["schemas/openapi.yaml", "FUSE_API_LOGIC.md"],
    },
}


def extract_fastapi_routes(path: Path) -> list[dict[str, str]]:
    tree = ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
    routes: list[dict[str, str]] = []
    for node in ast.walk(tree):
        if not isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
            continue
        for decorator in node.decorator_list:
            if not isinstance(decorator, ast.Call) or not isinstance(decorator.func, ast.Attribute):
                continue
            method = decorator.func.attr.upper()
            if method not in {"GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS", "HEAD", "WEBSOCKET"}:
                continue
            if not decorator.args or not isinstance(decorator.args[0], ast.Constant) or not isinstance(decorator.args[0].value, str):
                continue
            routes.append({"method": method, "path": decorator.args[0].value, "source": str(path)})
    return sorted(routes, key=lambda item: (item["path"], item["method"], item["source"]))


  def extract_protocols(path: Path) -> list[dict[str, str]]:
    tree = ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
    protocols: list[dict[str, str]] = []
    for node in tree.body:
      if not isinstance(node, ast.ClassDef):
        continue
      is_protocol = any(
        (isinstance(base, ast.Name) and base.id == "Protocol")
        or (isinstance(base, ast.Attribute) and base.attr == "Protocol")
        for base in node.bases
      )
      if is_protocol:
        protocols.append({"name": node.name, "source": str(path)})
    return protocols


def build_inventory(repo_root: Path) -> dict[str, Any]:
    services: dict[str, Any] = {}
    for name, config in SERVICE_CONFIG.items():
        service_root = repo_root / config["root"]
        route_files = sorted({path for pattern in config["route_globs"] for path in service_root.glob(pattern)})
        protocol_files = sorted({path for pattern in config["protocol_globs"] for path in service_root.glob(pattern)})
        routes = [route for path in route_files for route in extract_fastapi_routes(path)]
        protocols = [protocol for path in protocol_files for protocol in extract_protocols(path)]
        services[name] = {
            "entrypoints": [str(service_root / path) for path in config["entrypoints"]],
            "http_routes": [route for route in routes if route["method"] != "WEBSOCKET"],
            "websocket_routes": [route for route in routes if route["method"] == "WEBSOCKET"],
            "sse_routes": [route for route in routes if "sse" in route["source"].lower()],
            "schemas": sorted(str(path) for path in service_root.rglob("schemas.py")),
            "plugin_protocols": sorted(protocols, key=lambda item: (item["name"], item["source"])),
            "protocol_documents": [str(service_root / path) for path in config["protocol_documents"]],
        }
    commit = subprocess.run(["git", "rev-parse", "HEAD"], cwd=repo_root, check=True, capture_output=True, text=True).stdout.strip()
    return {"version": "ocb-dotnet-migration-inventory-v1", "generated_from_commit": commit, "services": services}


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-root", type=Path, default=Path(__file__).parents[2])
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    inventory = build_inventory(args.repo_root.resolve())
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(inventory, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
```

在 `test_extract_fastapi_routes_uses_ast_and_captures_method_path` 后添加精确的 Protocol 测试：

```python
def test_extract_protocols_captures_protocol_class(tmp_path: Path) -> None:
  source = tmp_path / "ports.py"
  source.write_text(
    "from typing import Protocol\n"
    "class StoragePlugin(Protocol):\n"
    "    def get(self, key: str) -> bytes: ...\n",
    encoding="utf-8",
  )

  assert _MODULE.extract_protocols(source) == [
    {"name": "StoragePlugin", "source": str(source)}
  ]
```

- [ ] **Step 4: 运行单元测试并生成清单**

Run：

```powershell
python -m pytest scripts/dotnet/tests/test_export_contract_inventory.py -v
python scripts/dotnet/export_contract_inventory.py --output dotnet/contracts/migration-inventory.json
python -m json.tool dotnet/contracts/migration-inventory.json > $null
```

Expected: pytest PASS；JSON 校验退出码 0；`services` 精确包含五个目标服务。

- [ ] **Step 5: 审核清单非空约束**

Run：

```powershell
$inventory = Get-Content dotnet/contracts/migration-inventory.json -Raw | ConvertFrom-Json
$inventory.services.backend.http_routes.Count -gt 0
$inventory.services.engine.http_routes.Count -gt 0
$inventory.services.baas.http_routes.Count -gt 0
$inventory.services.bcsfuse.http_routes.Count -gt 0
```

Expected: 四行均输出 `True`。Gateway 是配置驱动转发面，不要求装饰器 route 非空；其 forwarding、access-key、authn SPI 路径必须出现在 `plugin_protocols` 或后续人工审核记录中。

- [ ] **Step 6: Commit**

```bash
git add scripts/dotnet dotnet/contracts/migration-inventory.json
git commit -m "test(dotnet): inventory Python service contracts"
```

---

### Task 7: 固化 parity corpus 基线与校验和

**Files:**

- Create: `dotnet/contracts/parity-corpus/backend.openapi.json`
- Create: `dotnet/contracts/parity-corpus/baas.openapi.json`
- Create: `dotnet/contracts/parity-corpus/bcsfuse.openapi.yaml`
- Create: `dotnet/contracts/parity-corpus/engine-websocket-protocol.md`
- Create: `dotnet/contracts/parity-corpus/manifest.json`
- Create: `scripts/dotnet/tests/test_parity_corpus.py`

**Interfaces:**

- Consumes: 两个现有 `dump_openapi.py`、BCSFuse OpenAPI、Engine WebSocket 文档。
- Produces: 具有 SHA-256、source、kind、service 的 immutable baseline manifest。

- [ ] **Step 1: 写失败测试**

Create `test_parity_corpus.py`：

```python
import hashlib
import json
from pathlib import Path


_CORPUS = Path(__file__).parents[3] / "dotnet" / "contracts" / "parity-corpus"


def test_manifest_hashes_match_committed_artifacts() -> None:
    manifest = json.loads((_CORPUS / "manifest.json").read_text(encoding="utf-8"))
    assert manifest["version"] == "ocb-parity-corpus-v1"
    assert {entry["service"] for entry in manifest["artifacts"]} == {"backend", "baas", "bcsfuse", "engine"}
    for entry in manifest["artifacts"]:
        payload = (_CORPUS / entry["file"]).read_bytes()
        assert hashlib.sha256(payload).hexdigest() == entry["sha256"]
```

- [ ] **Step 2: 运行测试并确认失败**

Run：

```powershell
python -m pytest scripts/dotnet/tests/test_parity_corpus.py -v
```

Expected: FAIL，`manifest.json` 不存在。

- [ ] **Step 3: 生成和复制基线 artifact**

Run：

```powershell
New-Item -ItemType Directory -Force dotnet/contracts/parity-corpus | Out-Null
Push-Location src/backend
uv run python scripts/dump_openapi.py ../../dotnet/contracts/parity-corpus/backend.openapi.json
Pop-Location
Push-Location src/baas
uv run python scripts/dump_openapi.py ../../dotnet/contracts/parity-corpus/baas.openapi.json
Pop-Location
Copy-Item src/bcsfuse/schemas/openapi.yaml dotnet/contracts/parity-corpus/bcsfuse.openapi.yaml
Copy-Item src/engine/src/engine/community/claude_code_gateway/docs/websocket-protocol.md dotnet/contracts/parity-corpus/engine-websocket-protocol.md
```

- [ ] **Step 4: 创建 manifest**

Run：

```powershell
$root = Resolve-Path dotnet/contracts/parity-corpus
$items = @(
  @{ service='backend'; file='backend.openapi.json'; kind='openapi'; source='src/backend/scripts/dump_openapi.py' },
  @{ service='baas'; file='baas.openapi.json'; kind='openapi'; source='src/baas/scripts/dump_openapi.py' },
  @{ service='bcsfuse'; file='bcsfuse.openapi.yaml'; kind='openapi'; source='src/bcsfuse/schemas/openapi.yaml' },
  @{ service='engine'; file='engine-websocket-protocol.md'; kind='websocket'; source='src/engine/src/engine/community/claude_code_gateway/docs/websocket-protocol.md' }
)
foreach ($item in $items) { $item.sha256 = (Get-FileHash (Join-Path $root $item.file) -Algorithm SHA256).Hash.ToLowerInvariant() }
@{ version='ocb-parity-corpus-v1'; artifacts=$items } | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $root 'manifest.json') -Encoding utf8NoBOM
```

- [ ] **Step 5: 运行 corpus 测试**

Run：

```powershell
python -m pytest scripts/dotnet/tests/test_parity_corpus.py -v
```

Expected: PASS，1 test passed。

- [ ] **Step 6: Commit**

```bash
git add dotnet/contracts/parity-corpus scripts/dotnet/tests/test_parity_corpus.py
git commit -m "test(dotnet): capture protocol parity baseline"
```

---

### Task 8: 增加本地与 GitHub .NET CI 门禁

**Files:**

- Create: `scripts/ci/dotnet_ci.sh`
- Create: `scripts/ci/tests/test_pre_push_dotnet_gate.py`
- Create: `scripts/ci/tests/test_unit_test_workflow_dotnet.py`
- Modify: `scripts/ci/pre_push.sh`
- Modify: `.github/workflows/unit-tests.yml`
- Modify: `docs/arch/ci.enforce.md`

**Interfaces:**

- Produces: `scripts/ci/dotnet_ci.sh [--configuration Release]`；`dotnet/` 变更在 pre-push 和 GitHub Actions 中触发。
- Consumes: `dotnet format`、`dotnet build`、`dotnet test`。

- [ ] **Step 1: 写 pre-push 失败测试**

Create `test_pre_push_dotnet_gate.py`：

```python
from pathlib import Path


_ROOT = Path(__file__).parents[3]


def test_pre_push_routes_dotnet_changes_to_dotnet_ci() -> None:
    script = (_ROOT / "scripts/ci/pre_push.sh").read_text(encoding="utf-8")
  assert "dotnet/" in script
    assert 'run_heavy "$repo_root/scripts/ci/dotnet_ci.sh"' in script
```

Create `test_unit_test_workflow_dotnet.py`：

```python
from pathlib import Path
import yaml


_ROOT = Path(__file__).parents[3]


def test_unit_test_workflow_has_dotnet_job() -> None:
    workflow = yaml.safe_load((_ROOT / ".github/workflows/unit-tests.yml").read_text(encoding="utf-8"))
    job = workflow["jobs"]["dotnet"]
    rendered = str(job)
    assert "actions/setup-dotnet@v4" in rendered
    assert "10.0.302" in rendered
    assert "scripts/ci/dotnet_ci.sh" in rendered
    assert "dotnet" in rendered
```

- [ ] **Step 2: 运行测试并确认失败**

Run：

```powershell
python -m pytest scripts/ci/tests/test_pre_push_dotnet_gate.py scripts/ci/tests/test_unit_test_workflow_dotnet.py -v
```

Expected: 两个测试均 FAIL，尚无 .NET gate/job。

- [ ] **Step 3: 创建统一 CI 脚本**

Create `scripts/ci/dotnet_ci.sh`：

```bash
#!/usr/bin/env bash
set -euo pipefail

repo_root="$(git rev-parse --show-toplevel)"
solution="$repo_root/dotnet/Ocb.slnx"
configuration="Release"

if [[ "${1:-}" == "--configuration" ]]; then
  configuration="${2:?--configuration requires a value}"
fi

dotnet restore "$solution" --locked-mode
dotnet format "$solution" --verify-no-changes --no-restore
dotnet build "$solution" --configuration "$configuration" --no-restore
dotnet test "$solution" --configuration "$configuration" --no-build --collect:"XPlat Code Coverage"
python3 "$repo_root/scripts/dotnet/export_contract_inventory.py" --output "$repo_root/dotnet/contracts/migration-inventory.generated.json"
diff -u "$repo_root/dotnet/contracts/migration-inventory.json" "$repo_root/dotnet/contracts/migration-inventory.generated.json"
rm "$repo_root/dotnet/contracts/migration-inventory.generated.json"
python3 -m pytest "$repo_root/scripts/dotnet/tests" -v
```

Run once:

```bash
chmod +x scripts/ci/dotnet_ci.sh
```

- [ ] **Step 4: 修改 pre-push 分发**

在 `scripts/ci/pre_push.sh` 的 Gateway block 前加入：

```bash
if matches_any '^dotnet/|^scripts/dotnet/'; then
  # .NET 默认没有独立 SAST-only 命令；完整 build/test 在 full CI 模式运行。
  run_heavy "$repo_root/scripts/ci/dotnet_ci.sh"
fi
```

同时把架构文档或 CI 脚本变更触发模式扩展为：

```bash
if matches_any '^(dotnet/|scripts/dotnet/|docs/arch/(arch\.rules|ci\.enforce|context-boundary-format|protocol-contract-tests)\.md)'; then
  run_heavy "$repo_root/scripts/ci/dotnet_ci.sh"
fi
```

只保留一个 .NET block，使用扩展后的模式，避免重复运行。

- [ ] **Step 5: 添加 GitHub Actions job**

在 `.github/workflows/unit-tests.yml` 的 `jobs:` 下增加：

```yaml
  dotnet:
    name: .NET architecture and contract tests
    runs-on: ubuntu-latest
    env:
      DOTNET_BASE_REF: ${{ github.event_name == 'pull_request' && 'HEAD^1' || 'origin/dev' }}
    steps:
      - name: Check out repository
        uses: actions/checkout@v4
        with:
          fetch-depth: 0
      - name: Detect .NET changes vs base
        id: changes
        shell: bash
        run: |
          git fetch --no-tags --depth=1 origin "${DOTNET_BASE_REF#origin/}" 2>/dev/null || true
          if git diff --quiet "$DOTNET_BASE_REF" -- dotnet scripts/dotnet docs/arch; then
            echo "skip=true" >> "$GITHUB_OUTPUT"
          else
            echo "skip=false" >> "$GITHUB_OUTPUT"
          fi
      - name: Set up .NET
        if: steps.changes.outputs.skip != 'true'
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.302'
          cache: true
          cache-dependency-path: dotnet/**/packages.lock.json
      - name: Set up Python
        if: steps.changes.outputs.skip != 'true'
        uses: actions/setup-python@v5
        with:
          python-version: '3.12'
      - name: Install Python test dependencies
        if: steps.changes.outputs.skip != 'true'
        run: python -m pip install pytest pyyaml
      - name: Run .NET gate
        if: steps.changes.outputs.skip != 'true'
        run: bash scripts/ci/dotnet_ci.sh
```

- [ ] **Step 6: 更新 CI 规则文档**

在 `docs/arch/ci.enforce.md` 增加 `.NET 10 gate` 小节，逐字记录：

```markdown
### .NET 10 gate

Changes under `dotnet/`, `scripts/dotnet/`, or the architecture contract documents trigger `scripts/ci/dotnet_ci.sh`. The gate requires locked restore, formatting, warnings-as-errors build, xUnit architecture/contract tests, a reproducible migration inventory, and parity-corpus checksum tests. Core and contract projects must remain framework-independent; Service API and Plugin API stay in separate projects and test suites.
```

- [ ] **Step 7: 运行测试和完整 .NET gate**

Run：

```powershell
python -m pytest scripts/ci/tests/test_pre_push_dotnet_gate.py scripts/ci/tests/test_unit_test_workflow_dotnet.py -v
bash scripts/ci/dotnet_ci.sh
```

Expected: Python tests PASS；`OCB pre-push` 相关断言通过；.NET restore/format/build/test、inventory diff 和 corpus tests 全部通过。

- [ ] **Step 8: Commit**

```bash
git add scripts/ci/dotnet_ci.sh scripts/ci/pre_push.sh scripts/ci/tests .github/workflows/unit-tests.yml docs/arch/ci.enforce.md
git commit -m "ci(dotnet): enforce architecture and contract gates"
```

---

### Task 9: 执行阶段 0 完整验收

**Files:**

- Verify only; no production file changes expected。

**Interfaces:**

- Consumes: Tasks 1-8 的 Solution、测试、inventory、corpus 与 CI。
- Produces: 阶段 1-5 计划可依赖的已验证基线。

- [ ] **Step 1: 验证工作区与工具链**

Run：

```powershell
dotnet --version
git status --short
```

Expected: `10.0.302`；工作区为空。

- [ ] **Step 2: 运行完整 .NET gate**

Run：

```powershell
bash scripts/ci/dotnet_ci.sh
```

Expected: restore、format、build、test、inventory reproducibility、corpus checksum 全部 PASS。

- [ ] **Step 3: 验证 pre-push 选择逻辑**

Run：

```powershell
$env:OCB_PRE_PUSH_RUN_CI='1'
bash scripts/ci/pre_push.sh --base HEAD^1 --head HEAD --dry-run
Remove-Item Env:OCB_PRE_PUSH_RUN_CI
```

Expected: 输出包含 `scripts/ci/dotnet_ci.sh` 的 required gate，且 dry-run 不执行重测试。

- [ ] **Step 4: 验证未提前引入实现依赖**

Run：

```powershell
$forbidden = Select-String -Path dotnet/**/*.csproj -Pattern 'OpenClaw\.|Microsoft\.Orleans\.Server|TickerQ|EntityFrameworkCore|Minio|Qdrant|Sonnet' -AllMatches
if ($forbidden) { $forbidden; exit 1 }
Write-Output 'foundation dependency boundary: pass'
```

Expected: `foundation dependency boundary: pass`。

- [ ] **Step 5: 验证五服务清单与基线来源**

Run：

```powershell
$inventory = Get-Content dotnet/contracts/migration-inventory.json -Raw | ConvertFrom-Json
$inventory.services.PSObject.Properties.Name | Sort-Object
python -m pytest scripts/dotnet/tests -v
```

Expected: 服务名依次为 `baas`、`backend`、`bcsfuse`、`engine`、`gateway`；全部 Python contract baseline 测试通过。

- [ ] **Step 6: 写阶段验收提交（仅在验证产生必要 lock file 变化时）**

```bash
git add dotnet/**/packages.lock.json
git diff --cached --quiet || git commit -m "build(dotnet): lock foundation dependencies"
```

阶段 0 完成后，使用 `migration-inventory.json` 为 Gateway、Runtime Worker、BaaS、Fusion、Backend/Skills 分别编写执行计划，不直接开始跨服务实现。
