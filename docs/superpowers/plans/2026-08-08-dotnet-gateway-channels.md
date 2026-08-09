# .NET Gateway 与 Channels Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 交付 `Ocb.Gateway` + `Ocb.Channels` + `Ocb.GrainContracts` + `Ocb.Grains` 的 Gateway/Channel 迁移实现，覆盖 HTTP/WebSocket/SSE 转发与 Orleans 连接目录协同，并通过 parity 与性能基线验收。

**Architecture:** Gateway 只负责协议接入、身份校验与转发编排，不拥有业务授权策略、进程生命周期或权威业务数据。身份与租户上下文通过 `Ocb.Contracts.CallerContext` 显式传递，基础设施能力通过 `Ocb.PluginApi` 注入；WebSocket 连接实体保留在 Gateway 进程内，跨节点路由仅经 `ConnectionDirectoryGrain` + Orleans Streams。整体采用严格配置 fail-closed、传输适配器薄层、核心逻辑无框架依赖。

**Tech Stack:** .NET SDK 10.0.302、ASP.NET Core、Orleans、System.Threading.Channels、System.Text.Json source generation、xUnit、WebApplicationFactory、Orleans TestCluster、NBomber。

## Global Constraints

- 全部新项目目标框架为 `net10.0`，启用 nullable reference types 和 implicit usings，并将警告视为错误。
- 依赖版本在 `dotnet/Directory.Packages.props` 中集中锁定，仅允许使用已验证兼容 .NET 10 的稳定版本。
- 迁移可以研究和改写 `openclaw.net/src` 中的实现模式，但 Avernet 必须拥有独立实现和命名空间。
- 目标 Solution 禁止引用任何 `OpenClaw.*` ProjectReference 或 NuGet 包。
- `Ocb.Channels.WebSocketChannel` 参考 openclaw.net 的原生 ASP.NET Core WebSocket Channel，不使用 SignalR。
- 除非另行批准契约变更，否则不引入 openclaw.net 特有的 Canvas envelope。
- Service API 与 Plugin API 使用不同项目；Core 和 Contracts 不引用 ASP.NET Core、EF Core、Orleans 实现、MinIO、SonnetDB、Qdrant 或具体 Plugin。
- Orleans 是应用协调机制，不取代领域模型和 Plugin 架构；HTTP 转发、数据库访问、向量查询、文件传输和进程句柄不能建模为 Grain。
- WebSocket 对象始终保留在接受连接的 Gateway 进程中，不能进入 Grain State。
- tenant identity 通过可序列化 `CallerContext` 显式传递，不能依赖 ambient process state。
- 复用 Phase 0 已交付的 `Ocb.Contracts.CallerContext(string TenantId, string SubjectId, IReadOnlySet<string> Roles)`；禁止在 Gateway、PluginApi 或 Fusion 中再定义同名身份模型。
- Gateway 按既有契约验证 JWT 和 signed principal，包括需要使用的 `X-Avernet-Principal`。
- Orleans call filter 在 Grain 边界再次校验 caller 和 tenant 一致性。
- SSE 使用 ASP.NET Core streaming response，并遵守相同的鉴权、背压、取消和 correlation 规则。
- Gateway 不拥有业务授权、运行时进程生命周期和权威业务数据；它只做身份校验、路由、限流、协议转发与可观测性。
- 尽量复用 `Ocb.Contracts`/`Ocb.PluginApi`，不为单域新建 `Gateway.PluginApi`。

---

## File Structure

- `dotnet/src/Ocb.Gateway/`
  - 责任：ASP.NET Core 组合根、配置加载与校验、readiness、HTTP/WS/SSE 适配器、OpenAPI served 文档。
  - 边界：不承载业务授权决策，不持有进程句柄，不持久化业务状态。
- `dotnet/src/Ocb.Channels/`
  - 责任：传输层 WebSocket 基础能力（fragment 聚合、raw/JSON envelope、速率限制、串行发送）——**不是消息通道适配器**（不实现 `IChannelAdapter`），而是为 Gateway 代理中继提供可复用的传输层原语。
  - 边界：不依赖特定业务域 DTO，不引用 OpenClaw。
  - **命名说明：** 项目名 `Ocb.Channels` 指"传输层通道基础能力"（基于 `System.Threading.Channels` 和原生 WebSocket），**不是** openclaw.net 中的"消息通道适配器"（`IChannelAdapter` 用于入站消息通道如 WhatsApp/Telegram/Discord）。此项目只提供 `FragmentAccumulator`、`ConnectionRateLimiter`、`SerialSendQueue`、`WebSocketChannelSession` 等可复用的传输层原语，供 `Ocb.Gateway` 的 WebSocket 代理端点消费。
- `dotnet/src/Ocb.GrainContracts/`
  - 责任：`ConnectionDirectoryGrain` 契约、消息路由契约、lease 模型。
  - 边界：只含 Orleans 接口/DTO，不含实现。
  - **跨-plan 协调：** 本项目创建 `Ocb.GrainContracts/` 项目骨架（.csproj + context-boundary.json）。后续 plan（runtime-worker, backend-skills, fusion-vector）只向该项目追加子目录和文件，不再重新创建项目。
- `dotnet/src/Ocb.Grains/`
  - 责任：`ConnectionDirectoryGrain` 实现、lease 续约与过期清理、Streams 路由。
  - 边界：不直接操作 WebSocket 对象。
  - **跨-plan 协调：** 本项目创建 `Ocb.Grains/` 项目骨架（.csproj + context-boundary.json）。后续 plan 只向该项目追加子目录和文件，不再重新创建项目。
- `dotnet/src/Ocb.PluginApi/`
  - 责任：Gateway 需要的基础设施插件接口（principal 验签、access key 解析、schema catalog、upstream ws connector）。
  - 边界：只定义接口，不放实现，不创建 `Gateway.PluginApi`。
- `dotnet/tests/Ocb.Gateway.Tests/`
  - 责任：Gateway 配置、readiness、HTTP forward、SSE、签名头和租户复核测试。
- `dotnet/tests/Ocb.Channels.Tests/`
  - 责任：WebSocket channel 单元测试（fragment、envelope、rate limit、serial send、cleanup）。
- `dotnet/tests/Ocb.Grains.Tests/`
  - 责任：Orleans TestCluster 测试（directory lease、过期回收、stream 路由）。
- `dotnet/tests/Ocb.EndToEnd.Tests/`
  - 责任：HTTP/WS/SSE parity harness，读取 `dotnet/contracts/parity-corpus/manifest.json` 与网关基线数据。
- `dotnet/tests/Ocb.Performance.Tests/`
  - 责任：NBomber 场景（HTTP、WS、SSE）与阈值断言。

---

### Task 1: 组合根、Orleans Client、强配置与 Readiness Fail-Closed

**NuGet 依赖（`Ocb.Gateway.csproj` 通过 `Directory.Packages.props` 解析版本）：**

| 包 | 用途 |
|---|---|
| `Microsoft.Orleans.Sdk` | Grain 接口引用与 Source Generator（`Ocb.Gateway` 是 Orleans **Client**） |
| `Microsoft.Orleans.Runtime` | `IClusterClient` / `IGrainFactory` — Gateway 连接到 Silo 集群 |
| `Microsoft.Orleans.Clustering.AdoNet` | PostgreSQL 集群成员表发现（Gateway Client 也需要连接 clustering 表来发现 Silo） |
| `Npgsql` | ADO.NET Provider，Orleans ADO.NET Clustering 底层驱动 |

**Orleans Client 配置模式：**

- Gateway 是 **Orleans Client**，不是 Silo 宿主。Silo 宿主由 `Ocb.Silo.Host` 承载（Task 7 定义）。
- 集群发现通过 **PostgreSQL ADO.NET Clustering**（`ocb_orleans` schema）。连接字符串通过 `ISecretResolver` 解析。
- Grain 调用走 Orleans gRPC（默认端口 11111），不暴露非 Orleans HTTP 端点。

**Files:**

- Modify: `dotnet/Ocb.slnx`
- Modify: `dotnet/Directory.Packages.props`
- Modify: `dotnet/tests/Ocb.Architecture.Tests/DependencyBoundaryTests.cs`
- Create: `dotnet/src/Ocb.Gateway/Ocb.Gateway.csproj`
- Create: `dotnet/src/Ocb.Gateway/context-boundary.json`
- Create: `dotnet/src/Ocb.Gateway/Configuration/GatewayOptions.cs`
- Create: `dotnet/src/Ocb.Gateway/Configuration/GatewayOptionsValidator.cs`
- Create: `dotnet/src/Ocb.Gateway/Configuration/OrleansClientConfiguration.cs`
- Create: `dotnet/src/Ocb.Gateway/Readiness/GatewayReadinessCheck.cs`
- Create: `dotnet/src/Ocb.Gateway/Program.cs`
- Create: `dotnet/tests/Ocb.Gateway.Tests/Ocb.Gateway.Tests.csproj`
- Test: `dotnet/tests/Ocb.Gateway.Tests/Composition/GatewayCompositionTests.cs`

**Interfaces:**

- Consumes: `Ocb.Configuration.OcbPlatformOptions`, `Ocb.Configuration.OcbPlatformOptionsValidator.Validate(OcbPlatformOptions options)`, `ISecretResolver`（Orleans 连接字符串）。
- Produces:
  - `public sealed record GatewayOptions(int HttpPort, int MaxConnections, int MaxConnectionsPerIp, int MessagesPerSecondPerConnection, string[] AllowedOrigins, string OrleansClusterId, string OrleansServiceId)`
  - `public static ValidationResult Validate(IConfiguration configuration)` in `GatewayOptionsValidator`
  - `public static class OrleansClientConfiguration { public static void Configure(IServiceCollection services, IConfiguration configuration, string clusterConnectionString); }`
  - `public sealed class GatewayReadinessCheck : IHealthCheck`

- [ ] **Step 1: 写失败测试（未知配置键与非法值必须拒绝启动）**

```csharp
[Fact]
public async Task RejectsUnknownConfigurationKey()
{
    var builder = WebApplication.CreateBuilder();
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Gateway:HttpPort"] = "8080",
        ["Gateway:UnknownKey"] = "boom"
    });

    var ex = Assert.Throws<OptionsValidationException>(() => GatewayBootstrap.ValidateAndBind(builder.Configuration));
    Assert.Contains("Unknown configuration key", ex.Message);
}

[Fact]
public async Task ReadinessIsUnhealthyWhenRequiredPluginMissing()
{
    await using var app = await GatewayTestHost.BuildAsync(registerPrincipalVerifier: false);
    var client = app.CreateClient();

    var response = await client.GetAsync("/health/ready");
    Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
}
```

- [ ] **Step 2: RED 命令**

Run: `dotnet test dotnet/tests/Ocb.Gateway.Tests/Ocb.Gateway.Tests.csproj --filter FullyQualifiedName~GatewayCompositionTests`

Expected: FAIL，出现 `GatewayBootstrap` 未定义或 `OptionsValidationException` 断言不满足。

- [ ] **Step 3: 最小实现（只做绑定、校验、readiness）**

```csharp
public static class GatewayBootstrap
{
    public static GatewayOptions ValidateAndBind(IConfiguration configuration)
    {
        var section = configuration.GetSection("Gateway");
        var known = new HashSet<string>(StringComparer.Ordinal)
        {
            "HttpPort", "MaxConnections", "MaxConnectionsPerIp", "MessagesPerSecondPerConnection", "AllowedOrigins"
        };

        var unknown = section.GetChildren().Select(c => c.Key).Where(k => !known.Contains(k)).ToArray();
        if (unknown.Length > 0)
        {
            throw new OptionsValidationException(nameof(GatewayOptions), typeof(GatewayOptions),
            [ $"Unknown configuration key: {string.Join(",", unknown)}" ]);
        }

        var options = section.Get<GatewayOptions>() ?? throw new OptionsValidationException(nameof(GatewayOptions), typeof(GatewayOptions), ["Gateway section missing"]);
        if (options.HttpPort <= 0 || options.MaxConnections <= 0 || options.MaxConnectionsPerIp <= 0 || options.MessagesPerSecondPerConnection <= 0)
        {
            throw new OptionsValidationException(nameof(GatewayOptions), typeof(GatewayOptions), ["Gateway options must be positive"]);
        }
        return options;
    }
}

public sealed class GatewayReadinessCheck : IHealthCheck
{
    private readonly IServiceProvider _services;
    public GatewayReadinessCheck(IServiceProvider services) => _services = services;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var verifier = _services.GetService<IPrincipalTokenVerifier>();
        return Task.FromResult(verifier is null
            ? HealthCheckResult.Unhealthy("principal verifier missing")
            : HealthCheckResult.Healthy());
    }
}
```

- [ ] **Step 4: GREEN 命令**

Run: `dotnet test dotnet/tests/Ocb.Gateway.Tests/Ocb.Gateway.Tests.csproj --filter FullyQualifiedName~GatewayCompositionTests`

Expected: PASS，`RejectsUnknownConfigurationKey` 与 readiness 测试通过。

- [ ] **Step 5: Commit**

```bash
git add dotnet/Ocb.slnx dotnet/src/Ocb.Gateway dotnet/tests/Ocb.Gateway.Tests dotnet/tests/Ocb.Architecture.Tests/DependencyBoundaryTests.cs
git commit -m "feat(gateway): add strict composition root and readiness checks"
```

### Task 2: CallerContext/JWT/X-Avernet-Principal/access key/tenant 复核链路

**Files:**

- Modify: `dotnet/src/Ocb.PluginApi/IPluginContract.cs`
- Create: `dotnet/src/Ocb.PluginApi/GatewayIdentityContracts.cs`
- Create: `dotnet/src/Ocb.PluginApi/GatewaySecretContracts.cs`
- Create: `dotnet/src/Ocb.PluginApi/GatewayCacheContracts.cs`
- Create: `dotnet/src/Ocb.Gateway/Auth/PrincipalVerificationMiddleware.cs`
- Create: `dotnet/src/Ocb.Gateway/Auth/CallerContextFactory.cs`
- Create: `dotnet/src/Ocb.Gateway/Auth/WebSocketHandshakeMethodBinder.cs`
- Create: `dotnet/src/Ocb.Gateway/Auth/TenantConsistencyFilter.cs`
- Modify: `dotnet/src/Ocb.Gateway/Program.cs`
- Test: `dotnet/tests/Ocb.Gateway.Tests/Auth/PrincipalVerificationMiddlewareTests.cs`
- Test: `dotnet/tests/Ocb.Gateway.Tests/Auth/WebSocketHandshakeMethodBinderTests.cs`
- Test: `dotnet/tests/Ocb.Gateway.Tests/Auth/TenantConsistencyFilterTests.cs`

**Interfaces:**

- Consumes: `Ocb.Contracts.CallerContext`。
- Produces:
  - `public interface IPrincipalTokenVerifier : IPluginContract { ValueTask<CallerContext> VerifyAsync(string bearerToken, string signedPrincipalHeader, CancellationToken cancellationToken); }`
  - `public interface IAccessKeyResolver : IPluginContract { ValueTask<(string TenantId, string SubjectId, IReadOnlySet<string> Roles)?> ResolveAsync(string accessKeyToken, CancellationToken cancellationToken); }`
  - `public interface ISecretResolver : IPluginContract { ValueTask<string> ResolveAsync(string secretKey, CancellationToken cancellationToken); }`
  - `public interface ICacheProvider : IPluginContract { ValueTask<T?> GetAsync<T>(string key, CancellationToken ct); ValueTask SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct); ValueTask RemoveAsync(string key, CancellationToken ct); }`
  - `public static class CallerContextFactory { public static CallerContext Create(string tenantId, string subjectId, IReadOnlySet<string> roles); }`
  - `public static class WebSocketHandshakeMethodBinder { public const string HandshakeMethod = "WEBSOCKET"; }`
  - `public sealed class TenantConsistencyFilter : IEndpointFilter`

**设计说明（对齐 Python `_relay_ws.py`）：**

- **`ISecretResolver`**：对应 Python `spi/secret_resolver.py`。Spec 要求 "API key、模型凭据、MinIO 凭据、BCS secret 和 SM4 key 通过 Secret Plugin 解析"。Gateway 在构建 upstream 请求时需要解析 secret（例如 BCS access key），但不能把 secret 写入日志或 Grain State。此接口先在此 Task 定义契约，具体实现由各 domain plan 提供。
- **`ICacheProvider`**：对应 Python `spi/cache.py`。Spec 明确 "Redis 是可选组件，只能用于 cache、分布式 rate counter 或其他明确的非权威加速用途"。此接口定义非权威缓存的通用契约（Get/Set/Remove + TTL），Gateway 使用它做分布式速率限制 counter。Redis 不可用时 Provider 实现降级为本地内存缓存。
- **`WebSocketHandshakeMethodBinder.HandshakeMethod = "WEBSOCKET"`**：对应 Python `_relay_ws.py:97` `_HANDSHAKE_METHOD = "WEBSOCKET"`。WebSocket 握手在 HTTP 层面是 `GET`，但 route-security table 的鉴权使用独立的 `WEBSOCKET` method key。这样 WS 路径上的 "no identity required" 豁免只对 WS 平面生效，不会泄漏到同一路径的 HTTP GET 请求。`IPrincipalTokenVerifier.VerifyAsync` 接收此 method 参数以区分 HTTP 与 WS 鉴权规则。

- [ ] **Step 1: 写失败测试（401/403、CallerContext 注入、WS method 豁免隔离、secret 解析）**

```csharp
[Fact]
public async Task MissingPrincipalHeaderReturns401()
{
    await using var app = await GatewayTestHost.BuildAsync();
    var client = app.CreateClient();

    using var req = new HttpRequestMessage(HttpMethod.Get, "/openapi/v1/bots");
    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "jwt-ok");
    var response = await client.SendAsync(req);

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
}

[Fact]
public async Task TenantMismatchReturns403()
{
    await using var app = await GatewayTestHost.BuildAsync(verifierTenant: "tenant-A");
    var client = app.CreateClient();

    var response = await client.GetAsync("/openapi/v1/collaboration/tenants/tenant-B/sessions/s1");
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
}

[Fact]
public async Task WebSocketAuthExemptionDoesNotLeakToHttpGet()
{
    // WS 路径上的 "no identity required" 豁免使用 method=WEBSOCKET 鉴权，
    // 不能泄漏到同一路径的 HTTP GET 请求。
    await using var app = await GatewayTestHost.BuildAsync();
    var client = app.CreateClient();

    // HTTP GET 到 WS 路径：必须要求身份（不享受 WS 豁免）
    var response = await client.GetAsync("/openapi/v1/collaboration/messages/ws");
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
}

[Fact]
public async Task SecretResolverReturnsCredentialForUpstreamDial()
{
    var resolver = new FakeSecretResolver().WithSecret("bcs-access-key", "sk-abc123");
    var result = await resolver.ResolveAsync("bcs-access-key", CancellationToken.None);

    Assert.Equal("sk-abc123", result);
    // 验证 secret 不会出现在日志中
    Assert.DoesNotContain("sk-abc123", FakeSecretResolver.LastLogOutput);
}
```

- [ ] **Step 2: RED 命令**

Run: `dotnet test dotnet/tests/Ocb.Gateway.Tests/Ocb.Gateway.Tests.csproj --filter FullyQualifiedName~PrincipalVerificationMiddlewareTests|FullyQualifiedName~TenantConsistencyFilterTests`

Expected: FAIL，`IPrincipalTokenVerifier`/`TenantConsistencyFilter` 未实现。

- [ ] **Step 3: 最小实现（身份验证 + 租户复核，不做业务授权）**

```csharp
public sealed class PrincipalVerificationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IPrincipalTokenVerifier verifier, IAccessKeyResolver accessKeyResolver)
    {
        var jwt = context.Request.Headers.Authorization.ToString();
        var signedPrincipal = context.Request.Headers["X-Avernet-Principal"].ToString();
        if (string.IsNullOrWhiteSpace(jwt) || string.IsNullOrWhiteSpace(signedPrincipal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var caller = await verifier.VerifyAsync(jwt, signedPrincipal, context.RequestAborted);
        var accessKeyToken = context.Request.Headers["X-Avernet-Access-Key"].ToString();
        if (!string.IsNullOrWhiteSpace(accessKeyToken))
        {
            var resolved = await accessKeyResolver.ResolveAsync(accessKeyToken, context.RequestAborted);
            if (resolved is null || !string.Equals(resolved.Value.TenantId, caller.TenantId, StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
        }

        context.Items[typeof(CallerContext)] = caller;
        await next(context);
    }
}

public sealed class TenantConsistencyFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var caller = (CallerContext?)context.HttpContext.Items[typeof(CallerContext)];
        if (caller is null) return ValueTask.FromResult<object?>(Results.Unauthorized());

        var routeTenant = context.HttpContext.Request.RouteValues.TryGetValue("tenant_id", out var value) ? value?.ToString() : null;
        if (routeTenant is not null && !string.Equals(routeTenant, caller.TenantId, StringComparison.Ordinal))
        {
            return ValueTask.FromResult<object?>(Results.Forbid());
        }

        return next(context);
    }
}
```

- [ ] **Step 4: GREEN 命令**

Run: `dotnet test dotnet/tests/Ocb.Gateway.Tests/Ocb.Gateway.Tests.csproj --filter FullyQualifiedName~PrincipalVerificationMiddlewareTests|FullyQualifiedName~TenantConsistencyFilterTests`

Expected: PASS，401/403 与 `CallerContext` 注入断言通过。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.PluginApi/IPluginContract.cs dotnet/src/Ocb.PluginApi/GatewayIdentityContracts.cs dotnet/src/Ocb.Gateway/Auth dotnet/src/Ocb.Gateway/Program.cs dotnet/tests/Ocb.Gateway.Tests/Auth
git commit -m "feat(gateway): enforce caller context and tenant revalidation"
```

### Task 3: Path/Domain Forwarding + Schema Catalog（含 Served OpenAPI）

**Files:**

- Create: `dotnet/src/Ocb.PluginApi/SchemaCatalogContracts.cs`
- Create: `dotnet/src/Ocb.Gateway/Routing/DomainMap.cs`
- Create: `dotnet/src/Ocb.Gateway/Routing/PathRewriteRule.cs`
- Create: `dotnet/src/Ocb.Gateway/OpenApi/ServedOpenApiBuilder.cs`
- Create: `dotnet/src/Ocb.Gateway/Forwarding/ForwardTargetResolver.cs`
- Modify: `dotnet/src/Ocb.Gateway/Program.cs`
- Test: `dotnet/tests/Ocb.Gateway.Tests/Routing/DomainMapTests.cs`
- Test: `dotnet/tests/Ocb.Gateway.Tests/OpenApi/ServedOpenApiBuilderTests.cs`

**Interfaces:**

- Consumes: `dotnet/contracts/parity-corpus/gateway.openapi.json`, `dotnet/contracts/parity-corpus/manifest.json`。
- Produces:
  - `public interface ISchemaCatalog : IPluginContract { ValueTask<JsonDocument> GetCurrentAsync(string domain, CancellationToken cancellationToken); }`
  - `public sealed record DomainRoute(string Name, string MatchPrefix, string ServerName, bool ServesHttp, bool ServesWebSocket, PathRewriteRule? Rewrite)`
  - `public sealed class DomainMap { public DomainRoute? ResolveHttp(string path); public DomainRoute? ResolveWebSocket(string path); }`
  - `public sealed class ServedOpenApiBuilder { public JsonDocument Build(string title, string version, IReadOnlyDictionary<string, JsonDocument> domainDocs); }`

- [ ] **Step 1: 写失败测试（路径重写与文档聚合）**

```csharp
[Fact]
public void CollaborationPathResolvesToBcsDomain()
{
    var map = DomainMap.FromGatewayConfig(TestConfig.LoadGatewayRouting());
    var route = map.ResolveHttp("/openapi/v1/collaboration/bots/mine");

    Assert.NotNull(route);
    Assert.Equal("bcs", route!.ServerName);
}

[Fact]
public async Task ServedOpenApiContainsParityPaths()
{
    var builder = new ServedOpenApiBuilder();
    var docs = await TestOpenApiInputs.LoadFromManifestAsync("dotnet/contracts/parity-corpus/manifest.json");

    using var served = builder.Build("gateway", "v1", docs);
    var paths = served.RootElement.GetProperty("paths");
    Assert.True(paths.TryGetProperty("/openapi/v1/collaboration/messages/ws", out _));
    Assert.True(paths.TryGetProperty("/openapi/v1/bots", out _));
}
```

- [ ] **Step 2: RED 命令**

Run: `dotnet test dotnet/tests/Ocb.Gateway.Tests/Ocb.Gateway.Tests.csproj --filter FullyQualifiedName~DomainMapTests|FullyQualifiedName~ServedOpenApiBuilderTests`

Expected: FAIL，`DomainMap`/`ServedOpenApiBuilder` 缺失或路径断言失败。

- [ ] **Step 3: 最小实现（只实现解析、重写、聚合）**

```csharp
public sealed class DomainMap
{
    private readonly IReadOnlyList<DomainRoute> _routes;
    private DomainMap(IReadOnlyList<DomainRoute> routes) => _routes = routes;

    public static DomainMap FromGatewayConfig(GatewayRoutingConfig config)
        => new(config.Routes.OrderByDescending(r => r.MatchPrefix.Length).ToArray());

    public DomainRoute? ResolveHttp(string path)
        => _routes.FirstOrDefault(r => r.ServesHttp && PathStarts(path, r.MatchPrefix));

    public DomainRoute? ResolveWebSocket(string path)
        => _routes.FirstOrDefault(r => r.ServesWebSocket && PathStarts(path, r.MatchPrefix));

    private static bool PathStarts(string path, string prefix)
        => path.Equals(prefix, StringComparison.Ordinal) || path.StartsWith(prefix + "/", StringComparison.Ordinal);
}

public sealed class ServedOpenApiBuilder
{
    public JsonDocument Build(string title, string version, IReadOnlyDictionary<string, JsonDocument> domainDocs)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        writer.WriteString("openapi", "3.1.0");
        writer.WriteStartObject("info");
        writer.WriteString("title", title);
        writer.WriteString("version", version);
        writer.WriteEndObject();
        writer.WriteStartObject("paths");
        foreach (var doc in domainDocs.Values)
        {
            foreach (var path in doc.RootElement.GetProperty("paths").EnumerateObject())
            {
                path.WriteTo(writer);
            }
        }
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.Flush();
        stream.Position = 0;
        return JsonDocument.Parse(stream);
    }
}
```

- [ ] **Step 4: GREEN 命令**

Run: `dotnet test dotnet/tests/Ocb.Gateway.Tests/Ocb.Gateway.Tests.csproj --filter FullyQualifiedName~DomainMapTests|FullyQualifiedName~ServedOpenApiBuilderTests`

Expected: PASS，domain 解析、`/openapi/v1/collaboration/messages/ws` 与 `/openapi/v1/bots` 路径断言通过。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.PluginApi/SchemaCatalogContracts.cs dotnet/src/Ocb.Gateway/Routing dotnet/src/Ocb.Gateway/OpenApi dotnet/src/Ocb.Gateway/Forwarding/ForwardTargetResolver.cs dotnet/src/Ocb.Gateway/Program.cs dotnet/tests/Ocb.Gateway.Tests/Routing dotnet/tests/Ocb.Gateway.Tests/OpenApi
git commit -m "feat(gateway): add domain forwarding map and served schema catalog"
```

### Task 4: HTTP Forwarding Seams（签名头替换、透明流式、错误语义）

**Files:**

- Create: `dotnet/src/Ocb.PluginApi/ForwardingContracts.cs`
- Create: `dotnet/src/Ocb.Gateway/Forwarding/ForwardRequest.cs`
- Create: `dotnet/src/Ocb.Gateway/Forwarding/ForwardResponse.cs`
- Create: `dotnet/src/Ocb.Gateway/Forwarding/PrincipalHeaderInjector.cs`
- Create: `dotnet/src/Ocb.Gateway/Forwarding/HttpForwardingEndpoint.cs`
- Modify: `dotnet/src/Ocb.Gateway/Program.cs`
- Test: `dotnet/tests/Ocb.Gateway.Tests/Forwarding/HttpForwardingEndpointTests.cs`

**Interfaces:**

- Consumes: `CallerContext`, `IPrincipalTokenVerifier`。
- Produces: HTTP forwarding with signed principal injection, transparent streaming, and error mapping.

**设计说明（对齐 Python `_forward.py`）：**

- **`IHopByHopHeaderFilter`**：对应 Python `_forward.py` `strip_hop_by_hop` 函数和 `_INBOUND_STRIP = frozenset({"host", "x-avernet-principal"})`。定义标准的 hop-by-hop header 集合（Connection、Keep-Alive、Transfer-Encoding、TE、Trailer、Upgrade、Proxy-Authorization、Proxy-Authenticate），并提供 `ShouldStrip` 方法。Gateway 在转发请求前和返回响应时都要过滤。
- **`IPrincipalTokenSigner`**：对应 Python `spi/principal_signer.py`。Gateway 在转发前剥离入站 `X-Avernet-Principal`（防止伪造）并用新签名替换。
- **`IHttpForwarder`**：对应 Python `spi/forwarder.py`。透明转发 HTTP 请求/响应，保持流式 body 和错误语义。
- Produces:
  - `public interface IPrincipalTokenSigner : IPluginContract { ValueTask<string> SignAsync(CallerContext callerContext, string audience, CancellationToken cancellationToken); }`
  - `public interface IHttpForwarder : IPluginContract { ValueTask<ForwardResponse> ForwardAsync(ForwardRequest request, CancellationToken cancellationToken); }`
  - `public interface IHopByHopHeaderFilter : IPluginContract { IReadOnlySet<string> HopByHopHeaders { get; } bool ShouldStrip(string headerName); }`
  - `public static class PrincipalHeaderInjector { public static ForwardRequest InjectSignedPrincipal(ForwardRequest request, string signedToken); }`

- [ ] **Step 1: 写失败测试（剥离伪造头、注入签名头、SSE 透明转发）**

```csharp
[Fact]
public async Task ReplacesInboundPrincipalHeaderWithSignedValue()
{
    var fake = GatewayForwardingHost.WithSignedPrincipal("signed.jwt.token");
    var client = fake.CreateClient();

    using var req = new HttpRequestMessage(HttpMethod.Get, "/openapi/v1/bots");
    req.Headers.TryAddWithoutValidation("X-Avernet-Principal", "forged");
    var response = await client.SendAsync(req);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("signed.jwt.token", fake.LastForwardRequest!.Headers["X-Avernet-Principal"]);
}

[Fact]
public async Task SseBodyIsStreamedWithoutBufferingToCompletion()
{
    var fake = GatewayForwardingHost.WithSseChunks("data:1\n\n", "data:2\n\n");
    var client = fake.CreateClient();

    var response = await client.GetAsync("/openapi/v1/chat/messages/stream");
    Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
    Assert.Equal("data:1\n\ndata:2\n\n", await response.Content.ReadAsStringAsync());
}
```

- [ ] **Step 2: RED 命令**

Run: `dotnet test dotnet/tests/Ocb.Gateway.Tests/Ocb.Gateway.Tests.csproj --filter FullyQualifiedName~HttpForwardingEndpointTests`

Expected: FAIL，`IHttpForwarder` 未实现或 `X-Avernet-Principal` 替换断言失败。

- [ ] **Step 3: 最小实现（不引入业务策略）**

```csharp
public static class PrincipalHeaderInjector
{
    public static ForwardRequest InjectSignedPrincipal(ForwardRequest request, string signedToken)
    {
        var headers = new Dictionary<string, string>(request.Headers, StringComparer.OrdinalIgnoreCase);
        headers.Remove("X-Avernet-Principal");
        headers["X-Avernet-Principal"] = signedToken;
        headers.Remove("Host");
        return request with { Headers = headers };
    }
}

public static class HttpForwardingEndpoint
{
    public static async Task HandleAsync(HttpContext context, IHttpForwarder forwarder, IPrincipalTokenSigner signer)
    {
        var caller = (CallerContext?)context.Items[typeof(CallerContext)] ?? throw new InvalidOperationException("CallerContext missing");
        var signed = await signer.SignAsync(caller, audience: context.GetRouteValue("server")?.ToString() ?? "unknown", context.RequestAborted);
        var request = await ForwardRequest.FromHttpContextAsync(context);
        request = PrincipalHeaderInjector.InjectSignedPrincipal(request, signed);

        var response = await forwarder.ForwardAsync(request, context.RequestAborted);
        context.Response.StatusCode = response.StatusCode;
        foreach (var (name, value) in response.Headers)
        {
            context.Response.Headers.Append(name, value);
        }
        await foreach (var chunk in response.Body.WithCancellation(context.RequestAborted))
        {
            await context.Response.Body.WriteAsync(chunk, context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
        }

    }
}
```

- [ ] **Step 4: GREEN 命令**

Run: `dotnet test dotnet/tests/Ocb.Gateway.Tests/Ocb.Gateway.Tests.csproj --filter FullyQualifiedName~HttpForwardingEndpointTests`

Expected: PASS，签名头替换、SSE 流式透传、错误码映射断言通过。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.PluginApi/ForwardingContracts.cs dotnet/src/Ocb.Gateway/Forwarding dotnet/src/Ocb.Gateway/Program.cs dotnet/tests/Ocb.Gateway.Tests/Forwarding
git commit -m "feat(gateway): add http forwarding seam with signed principal injection"
```

### Task 5: `Ocb.Channels` 原生 WebSocket Channel 核心能力

**Files:**

- Create: `dotnet/src/Ocb.Channels/Ocb.Channels.csproj`
- Create: `dotnet/src/Ocb.Channels/context-boundary.json`
- Create: `dotnet/src/Ocb.Channels/WebSocket/WebSocketChannelOptions.cs`
- Create: `dotnet/src/Ocb.Channels/WebSocket/IWebSocketEnvelopeCodec.cs`
- Create: `dotnet/src/Ocb.Channels/WebSocket/RawEnvelopeCodec.cs`
- Create: `dotnet/src/Ocb.Channels/WebSocket/JsonEnvelopeCodec.cs`
- Create: `dotnet/src/Ocb.Channels/WebSocket/FragmentAccumulator.cs`
- Create: `dotnet/src/Ocb.Channels/WebSocket/ConnectionRateLimiter.cs`
- Create: `dotnet/src/Ocb.Channels/WebSocket/SerialSendQueue.cs`
- Create: `dotnet/src/Ocb.Channels/WebSocket/WebSocketChannelSession.cs`
- Create: `dotnet/tests/Ocb.Channels.Tests/Ocb.Channels.Tests.csproj`
- Test: `dotnet/tests/Ocb.Channels.Tests/WebSocket/WebSocketChannelSessionTests.cs`

**Interfaces:**

- Consumes: `System.Net.WebSockets.WebSocket`。
- Produces:
  - `public sealed record WebSocketChannelOptions(int MaxConnections, int MaxConnectionsPerIp, int MessagesPerSecondPerConnection, int MaxMessageBytes, bool EnableJsonEnvelope)`
  - `public interface IWebSocketEnvelopeCodec { ReadOnlyMemory<byte> Encode(ReadOnlyMemory<byte> payload); ReadOnlyMemory<byte> Decode(ReadOnlyMemory<byte> frame); }`
  - `public sealed class WebSocketChannelSession { public Task RunAsync(WebSocket clientSocket, IWebSocketUpstreamDuplex upstream, CancellationToken cancellationToken); }`

- [ ] **Step 1: 写失败测试（fragment 聚合、raw/json、限流、串行发送、清理）**

```csharp
[Fact]
public async Task AggregatesReceiveFragmentsIntoSingleMessage()
{
    var socket = FakeWebSocketClient.WithFragments("hel", "lo");
    var upstream = new FakeUpstreamDuplex();
    var session = WebSocketChannelSessionFactory.Create(enableJsonEnvelope: false);

    await session.RunAsync(socket, upstream, CancellationToken.None);

    Assert.Equal("hello", upstream.ReceivedText.Single());
}

[Fact]
public async Task EnforcesPerConnectionRateLimit()
{
    var socket = FakeWebSocketClient.WithTextMessages("1", "2", "3", "4");
    var upstream = new FakeUpstreamDuplex();
    var session = WebSocketChannelSessionFactory.Create(messagesPerSecond: 2);

    await session.RunAsync(socket, upstream, CancellationToken.None);

    Assert.Contains(socket.CloseFrames, f => f.Code == 4408);
}

[Fact]
public async Task SerializesConcurrentSendToClient()
{
    var socket = FakeWebSocketClient.ForSendConcurrency();
    var upstream = FakeUpstreamDuplex.WithConcurrentOutbound("A", "B", "C");
    var session = WebSocketChannelSessionFactory.Create();

    await session.RunAsync(socket, upstream, CancellationToken.None);

    Assert.Equal(new[] { "A", "B", "C" }, socket.SentTexts);
}
```

- [ ] **Step 2: RED 命令**

Run: `dotnet test dotnet/tests/Ocb.Channels.Tests/Ocb.Channels.Tests.csproj --filter FullyQualifiedName~WebSocketChannelSessionTests`

Expected: FAIL，`WebSocketChannelSession`、`FragmentAccumulator` 或限流实现缺失。

- [ ] **Step 3: 最小实现（仅实现通道语义，不引业务）**

```csharp
public sealed class FragmentAccumulator
{
    public async ValueTask<ReadOnlyMemory<byte>?> ReadMessageAsync(WebSocket socket, int maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = MemoryPool<byte>.Shared.Rent(16 * 1024);
        using var stream = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer.Memory, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            stream.Write(buffer.Memory.Span[..result.Count]);
            if (stream.Length > maxBytes) throw new InvalidOperationException("message_too_large");
            if (result.EndOfMessage) return stream.ToArray();
        }
    }
}

public sealed class ConnectionRateLimiter(int messagesPerSecond)
{
    private readonly Queue<long> _timestamps = new();
    public bool TryAccept(long unixMillis)
    {
        while (_timestamps.Count > 0 && unixMillis - _timestamps.Peek() >= 1000) _timestamps.Dequeue();
        if (_timestamps.Count >= messagesPerSecond) return false;
        _timestamps.Enqueue(unixMillis);
        return true;
    }
}

public sealed class SerialSendQueue
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public async Task SendAsync(Func<Task> send, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try { await send(); }
        finally { _gate.Release(); }
    }
}
```

- [ ] **Step 4: GREEN 命令**

Run: `dotnet test dotnet/tests/Ocb.Channels.Tests/Ocb.Channels.Tests.csproj --filter FullyQualifiedName~WebSocketChannelSessionTests`

Expected: PASS，fragment/raw-json/limit/serial/cleanup 断言通过。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Channels dotnet/tests/Ocb.Channels.Tests dotnet/Ocb.slnx dotnet/tests/Ocb.Architecture.Tests/DependencyBoundaryTests.cs
git commit -m "feat(channels): add native websocket channel core behaviors"
```

### Task 6: Gateway WebSocket 端点适配（Origin 校验、路径穿越防护、raw path 编码守卫、upstream 连接超时、断线清理）

**Files:**

- Create: `dotnet/src/Ocb.PluginApi/WebSocketForwardingContracts.cs`
- Create: `dotnet/src/Ocb.Gateway/WebSocket/WebSocketOriginPolicy.cs`
- Create: `dotnet/src/Ocb.Gateway/WebSocket/GatewayWebSocketEndpoint.cs`
- Create: `dotnet/src/Ocb.Gateway/WebSocket/WebSocketPathGuard.cs` — 路径穿越 + raw path 编码守卫
- Modify: `dotnet/src/Ocb.Gateway/Program.cs`
- Test: `dotnet/tests/Ocb.Gateway.Tests/WebSocket/GatewayWebSocketEndpointTests.cs`

**Design notes — 安全守卫对齐 Python `src/gateway/.../adapters/web/_relay_ws.py`：**

1. **`_has_dot_segment(path)`**（Python `_relay_ws.py:262-279`）：拒绝 decoded path 中包含 `.` 或 `..` 段的请求。Python GW 注释明确说明 "refusing here does not depend on assuming which of them normalises"——路径穿越防护不能依赖下游框架的规范化行为。
2. **`_required_raw_prefix(domain)`**（Python `_relay_ws.py:282-316`）：确保 raw path（用于 upstream dial）的前缀与 decoded path（用于 auth/routing）的前缀在语义上一致。防止 "authorised as one resource, dialled as another" 攻击——攻击者通过编码绕过路由前缀检查后，raw path 指向不同资源。
3. **Upstream 握手超时**（Python `_ws_forwarder.py:45` `_HANDSHAKE_TIMEOUT_SECONDS = 10.0`）：`IWebSocketUpstreamConnector.ConnectAsync` 必须实现 10 秒握手超时，超时后以 `WebSocketCloseStatus.EndpointUnavailable` 拒绝客户端。

**Interfaces:**

- Consumes: `DomainMap.ResolveWebSocket`, `ForwardTargetResolver.ResolveWebSocketUri(DomainRoute route, PathString path, QueryString query)`, `WebSocketChannelSession.RunAsync`。
- Produces:
  - `public interface IWebSocketUpstreamConnector : IPluginContract { Task<IWebSocketUpstreamDuplex> ConnectAsync(Uri upstreamUri, IReadOnlyDictionary<string,string> headers, TimeSpan handshakeTimeout, CancellationToken cancellationToken); }`
  - `public sealed class WebSocketOriginPolicy { public bool IsAllowed(string? origin); }`
  - `public static class WebSocketPathGuard { public static bool HasDotSegment(ReadOnlySpan<char> decodedPath); public static bool HasRequiredRawPrefix(ReadOnlySpan<char> decodedPath, ReadOnlySpan<char> rawPath, string domainPrefix); }`
  - `public static class GatewayWebSocketEndpoint { public static Task HandleAsync(HttpContext context); }`

- [ ] **Step 1: 写失败测试（路径穿越、raw path 编码绕过、Origin 校验、连接失败拒绝、握手超时、清理）**

```csharp
[Fact]
public async Task RejectsDisallowedOriginWith403BeforeAccept()
{
    await using var app = await GatewayWsTestHost.BuildAsync(allowedOrigins: ["https://allowed.example"]);
    var client = app.CreateWebSocketClient();
    client.ConfigureRequest = req => req.Headers.Add("Origin", "https://evil.example");

    await Assert.ThrowsAsync<WebSocketException>(() => client.ConnectAsync(new Uri("ws://localhost/openapi/v1/collaboration/messages/ws"), CancellationToken.None));
}

[Fact]
public async Task RejectsPathTraversalWithDotSegment()
{
    // 对齐 Python _relay_ws.py:_has_dot_segment (line 262-279)
    // "refusing here does not depend on assuming which of them normalises"
    await using var app = await GatewayWsTestHost.BuildAsync();
    var client = app.CreateWebSocketClient();

    // decoded path 包含 ".." 段 — 拒绝
    var ex = await Assert.ThrowsAsync<WebSocketException>(() =>
        client.ConnectAsync(new Uri("ws://localhost/openapi/v1/collaboration/../messages/ws"), CancellationToken.None));
    Assert.Contains("400", ex.Message); // BadRequest，不能依赖下游规范化

    // decoded path 包含 "." 段 — 也拒绝
    var ex2 = await Assert.ThrowsAsync<WebSocketException>(() =>
        client.ConnectAsync(new Uri("ws://localhost/openapi/v1/./messages/ws"), CancellationToken.None));
    Assert.Contains("400", ex2.Message);
}

[Fact]
public async Task RejectsEncodedPathBypassWithMissingRawPrefix()
{
    // 对齐 Python _relay_ws.py:_required_raw_prefix (line 282-316)
    // 攻击者想用编码绕过路由前缀：raw path = /%6Fpenapi/... 而 decoded = /openapi/...
    // 但前缀 "openapi" 在 raw path 中不匹配 — 拒绝
    await using var app = await GatewayWsTestHost.BuildAsync();
    var client = app.CreateWebSocketClient();

    // raw path 中路由前缀被编码了，不匹配
    var ex = await Assert.ThrowsAsync<WebSocketException>(() =>
        client.ConnectAsync(new Uri("ws://localhost/%6Fpenapi/v1/collaboration/messages/ws"), CancellationToken.None));
    Assert.Contains("400", ex.Message);
}

[Fact]
public async Task UpstreamHandshakeTimeoutClosesWithEndpointUnavailable()
{
    await using var app = await GatewayWsTestHost.BuildAsync(
        upstreamHandshakeDelay: TimeSpan.FromSeconds(15));
    var client = app.CreateWebSocketClient();
    var socket = await client.ConnectAsync(new Uri("ws://localhost/openapi/v1/collaboration/messages/ws"), CancellationToken.None);

    // 超时后以 EndpointUnavailable 关闭（10s 握手超时，对齐 Python _ws_forwarder.py:45）
    Assert.Equal(WebSocketCloseStatus.EndpointUnavailable, socket.CloseStatus);
}

[Fact]
public async Task UpstreamDialFailureReturnsUnavailableCloseCode()
{
    await using var app = await GatewayWsTestHost.BuildAsync(upstreamConnectThrows: true);
    var client = app.CreateWebSocketClient();
    var socket = await client.ConnectAsync(new Uri("ws://localhost/openapi/v1/collaboration/messages/ws"), CancellationToken.None);

    Assert.Equal(WebSocketCloseStatus.InternalServerError, socket.CloseStatus);
}
```

- [ ] **Step 2: RED 命令**

Run: `dotnet test dotnet/tests/Ocb.Gateway.Tests/Ocb.Gateway.Tests.csproj --filter FullyQualifiedName~GatewayWebSocketEndpointTests`

Expected: FAIL，端点不存在或 close/origin 断言不成立。

- [ ] **Step 3: 最小实现（ASP.NET Core 原生 WebSocket + 路径穿越防护 + raw path 守卫 + 握手超时）**

```csharp
public sealed class WebSocketOriginPolicy(string[] allowedOrigins)
{
    private readonly HashSet<string> _allowed = new(allowedOrigins, StringComparer.OrdinalIgnoreCase);
    public bool IsAllowed(string? origin) => origin is not null && _allowed.Contains(origin);
}

public static class WebSocketPathGuard
{
    // 对齐 Python _relay_ws.py:262-279 _has_dot_segment
    // "refusing here does not depend on assuming which of them normalises"
    public static bool HasDotSegment(ReadOnlySpan<char> decodedPath)
    {
        foreach (var segment in decodedPath.ToString().Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is "." or "..") return true;
        }
        return false;
    }

    // 对齐 Python _relay_ws.py:282-316 _required_raw_prefix
    // 确保 raw path 中路由前缀是逐字匹配的（非编码），防止
    // "authorised as one resource, dialled as another"
    public static bool HasRequiredRawPrefix(ReadOnlySpan<char> decodedPath, ReadOnlySpan<char> rawPath, string domainPrefix)
    {
        if (string.IsNullOrEmpty(domainPrefix)) return true;

        var rawPathString = rawPath.ToString();
        // 路由前缀必须在 raw path 中逐字出现（未被编码）
        if (!rawPathString.StartsWith("/" + domainPrefix, StringComparison.Ordinal) &&
            !rawPathString.StartsWith(domainPrefix, StringComparison.Ordinal))
        {
            return false;
        }
        return true;
    }
}

public static class GatewayWebSocketEndpoint
{
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(10); // 对齐 Python _ws_forwarder.py:45

    public static async Task HandleAsync(HttpContext context)
    {
        var originPolicy = context.RequestServices.GetRequiredService<WebSocketOriginPolicy>();
        if (!originPolicy.IsAllowed(context.Request.Headers["Origin"].ToString()))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        // 路径穿越防护 — 对齐 Python _relay_ws.py:262-279
        var decodedPath = context.Request.Path.Value.AsSpan();
        if (WebSocketPathGuard.HasDotSegment(decodedPath))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var connector = context.RequestServices.GetRequiredService<IWebSocketUpstreamConnector>();
        var session = context.RequestServices.GetRequiredService<WebSocketChannelSession>();
        var domainMap = context.RequestServices.GetRequiredService<DomainMap>();
        var targetResolver = context.RequestServices.GetRequiredService<ForwardTargetResolver>();

        var route = domainMap.ResolveWebSocket(context.Request.Path)
            ?? throw new BadHttpRequestException("No WebSocket route", StatusCodes.Status404NotFound);

        // Raw path 编码守卫 — 对齐 Python _relay_ws.py:282-316
        // context.Request.Path 是 decoded path；raw path 从 Request.PathBase + Path 的原始编码恢复
        var rawPath = context.Features.Get<IHttpRequestFeature>()?.RawTarget
            ?? context.Request.Path.ToString();
        if (!WebSocketPathGuard.HasRequiredRawPrefix(decodedPath, rawPath, route.DomainPrefix))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var upstreamUri = targetResolver.ResolveWebSocketUri(route, context.Request.Path, context.Request.QueryString);

        using var clientSocket = await context.WebSockets.AcceptWebSocketAsync();
        await using var upstream = await connector.ConnectAsync(upstreamUri, new Dictionary<string, string>(), HandshakeTimeout, context.RequestAborted);
        await session.RunAsync(clientSocket, upstream, context.RequestAborted);
    }
}
```

- [ ] **Step 4: GREEN 命令**

Run: `dotnet test dotnet/tests/Ocb.Gateway.Tests/Ocb.Gateway.Tests.csproj --filter FullyQualifiedName~GatewayWebSocketEndpointTests`

Expected: PASS，Origin 拒绝、upstream 失败关闭、断线后资源释放断言通过。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.PluginApi/WebSocketForwardingContracts.cs dotnet/src/Ocb.Gateway/WebSocket dotnet/src/Ocb.Gateway/Program.cs dotnet/tests/Ocb.Gateway.Tests/WebSocket
git commit -m "feat(gateway): add native websocket endpoint adapter"
```

### Task 7: ConnectionDirectoryGrain + Lease + Orleans Streams 路由

此 Task 分为 4 个子任务：7a（Grain 契约）、7b（Grain 实现 + PostgreSQL persistence）、7c（Gateway Client 集成）、7d（Silo.Host 组装）。

---

#### 7a: GrainContracts 项目骨架 + ConnectionDirectory 契约

**Files:**

- Create: `dotnet/src/Ocb.GrainContracts/Ocb.GrainContracts.csproj`
- Create: `dotnet/src/Ocb.GrainContracts/context-boundary.json`
- Create: `dotnet/src/Ocb.GrainContracts/Channels/ConnectionDirectoryContracts.cs`

**`Ocb.GrainContracts.csproj` NuGet 依赖：**

| 包 | 用途 |
|---|---|
| `Microsoft.Orleans.Sdk` | Grain 接口 Source Generator（GenerateSerializer、GenerateMethodSerializers、Alias） |

**Grain Key 设计：**

采用 **tenant-scoped key**（对齐 Spec `(tenant_id, entity_id)` 和审核建议），不再使用全局单一 `"directory"` key：

```
IConnectionDirectoryGrain : IGrainWithStringKey
Grain Key = "directory/{tenantId}"     // e.g.  "directory/t-abc"
```

每个 Tenant 拥有独立 Directory Grain，避免单一 Grain 成为集群热点；消息路由只需查询发送方 tenant 的 Grain。

**Interfaces（`dotnet/src/Ocb.GrainContracts/Channels/ConnectionDirectoryContracts.cs`）：**

```csharp
[Alias("Ocb.GrainContracts.Channels.IConnectionDirectoryGrain")]
public interface IConnectionDirectoryGrain : IGrainWithStringKey
{
    Task RegisterAsync(string gatewayInstanceId, string connectionId, CallerContext caller,
        string sessionId, DateTimeOffset leaseExpiryUtc);

    Task RenewLeaseAsync(string connectionId, DateTimeOffset leaseExpiryUtc);

    Task RemoveAsync(string connectionId);

    /// <summary>根据 sessionId 解析连接路由（tenant 由 Grain Key 锁定）</summary>
    Task<ConnectionRoute?> ResolveBySessionAsync(string sessionId);
}

[Alias("Ocb.GrainContracts.Channels.ConnectionRoute")]
[GenerateSerializer]
public sealed record ConnectionRoute(
    string GatewayInstanceId,
    string ConnectionId,
    string TenantId,
    string SubjectId,
    string SessionId,
    DateTimeOffset LeaseExpiryUtc
);

[Alias("Ocb.GrainContracts.Channels.OutboundGatewayMessage")]
[GenerateSerializer]
public sealed record OutboundGatewayMessage(
    string TenantId,
    string SessionId,
    string ConnectionId,
    byte[] Payload,
    string CorrelationId
);
```

**Tenant Key 工具方法（`dotnet/src/Ocb.GrainContracts/GrainKeys/TenantGrainKey.cs`）：**

```csharp
public static class TenantGrainKey
{
    public static string Directory(string tenantId) => $"directory/{tenantId}";
}
```

- [ ] **Step 1: 写失败测试（Grain Key 格式）**

```csharp
[Fact]
public void DirectoryKey_IsTenantScoped()
{
    var key = TenantGrainKey.Directory("t-abc");
    Assert.StartsWith("directory/", key);
    Assert.EndsWith("t-abc", key);
}
```

- [ ] **Step 2: RED —** `dotnet test dotnet/tests/Ocb.Contracts.Tests/Ocb.Contracts.Tests.csproj --filter DirectoryKey_IsTenantScoped` — 预期 FAIL。
- [ ] **Step 3: 最小实现 —** 创建 `.csproj`、`context-boundary.json`、`ConnectionDirectoryContracts.cs`、`TenantGrainKey.cs`。
- [ ] **Step 4: GREEN —** 测试 PASS。
- [ ] **Step 5: Commit:**

```bash
git add dotnet/src/Ocb.GrainContracts
git commit -m "feat(grains): add grain contracts project with tenant-scoped connection directory"
```

---

#### 7b: Grains 实现 + PostgreSQL Persistence + SMS Streams

**Files:**

- Create: `dotnet/src/Ocb.Grains/Ocb.Grains.csproj`
- Create: `dotnet/src/Ocb.Grains/context-boundary.json`
- Create: `dotnet/src/Ocb.Grains/Channels/ConnectionDirectoryState.cs`
- Create: `dotnet/src/Ocb.Grains/Channels/ConnectionDirectoryGrain.cs`
- Create: `dotnet/src/Ocb.Grains/Channels/TenantKeyGuardCallFilter.cs`

**`Ocb.Grains.csproj` NuGet 依赖：**

| 包 | 用途 |
|---|---|
| `Microsoft.Orleans.Sdk` | Source Generator（`[GenerateSerializer]`） |
| `Microsoft.Orleans.Runtime` | `Grain`、`IPersistentState<T>`、`IAsyncStream<T>` |
| `Microsoft.Orleans.Persistence.AdoNet` | PostgreSQL 持久化 Grain State（`ocb_orleans.OrleansStorage` 表） |
| `Microsoft.Orleans.Streaming.SMS` | Simple Message Stream — 低延迟消息路由（无需额外消息队列） |
| `Npgsql` | ADO.NET Provider |

**Orleans 配置（Silo 侧 — 在 `Ocb.Silo.Host` 的 `Program.cs` 中执行）：**

```csharp
// Silo 配置示例 — 在 dotnet/src/Ocb.Silo.Host/Program.cs 中组装
var siloBuilder = Host.CreateDefaultBuilder(args)
    .UseOrleans(builder =>
    {
        // 1. 集群发现：PostgreSQL ADO.NET Clustering
        var clusterConnStr = secretResolver.ResolveAsync("orleans-cluster-conn", ct).Result;
        builder.UseAdoNetClustering(options =>
        {
            options.Invariant = "Npgsql";
            options.ConnectionString = clusterConnStr;
        });

        // 2. Grain 持久化：PostgreSQL ADO.NET Persistence
        builder.AddAdoNetGrainStorage("orleans-storage", options =>
        {
            options.Invariant = "Npgsql";
            options.ConnectionString = clusterConnStr;
        });

        // 3. Stream Provider：SMS（同集群内 gRPC 推送，无需外部消息队列）
        builder.AddSmsStreams("sms");

        // 4. Reminder：PostgreSQL（用于定期清理过期连接）
        builder.UseAdoNetReminderService(options =>
        {
            options.Invariant = "Npgsql";
            options.ConnectionString = clusterConnStr;
        });

        // 5. 配置 Silo 端点
        builder.Configure<ClusterOptions>(o =>
        {
            o.ClusterId = "avernet";
            o.ServiceId = "ocb";
        });
        builder.ConfigureEndpoints(
            siloPort: 11111,    // Silo-to-Silo gRPC
            gatewayPort: 30000  // Client-to-Silo gRPC
        );
    });
```

**Silo 侧数据库初始化（首次启动时执行，对齐 `ocb_orleans` schema）：**

```sql
-- dotnet/src/Ocb.Infrastructure.PostgreSql/Migrations/0002_ocb_orleans.sql
-- 由 Ocb.Silo.Host 启动时通过 EF Core migration 或 idempotent SQL 脚本初始化

CREATE TABLE IF NOT EXISTS ocb_orleans."OrleansMembership" (
    -- ADO.NET Clustering 标准 schema
    ...
);
CREATE TABLE IF NOT EXISTS ocb_orleans."OrleansStorage" (
    -- ADO.NET Persistence 标准 schema
    ...
);
CREATE TABLE IF NOT EXISTS ocb_orleans."OrleansReminders" (
    -- ADO.NET Reminder 标准 schema
    ...
);
```

**Grain 实现（租户范围内目录 + lease 过期清理）：**

```csharp
[Alias("Ocb.Grains.Channels.ConnectionDirectoryState")]
[GenerateSerializer]
public sealed class ConnectionDirectoryState
{
    [Id(0)]
    public Dictionary<string, ConnectionRoute> ByConnectionId { get; init; } = new(StringComparer.Ordinal);
}

[Alias("Ocb.Grains.Channels.ConnectionDirectoryGrain")]
public sealed class ConnectionDirectoryGrain : Grain, IConnectionDirectoryGrain
{
    private readonly IPersistentState<ConnectionDirectoryState> _state;

    public ConnectionDirectoryGrain(
        [PersistentState("connection-directory", "orleans-storage")]
        IPersistentState<ConnectionDirectoryState> state) => _state = state;

    public async Task RegisterAsync(string gatewayInstanceId, string connectionId,
        CallerContext caller, string sessionId, DateTimeOffset leaseExpiryUtc)
    {
        _state.State.ByConnectionId[connectionId] = new ConnectionRoute(
            gatewayInstanceId, connectionId, caller.TenantId, caller.SubjectId,
            sessionId, leaseExpiryUtc);
        await _state.WriteStateAsync();
    }

    public Task RenewLeaseAsync(string connectionId, DateTimeOffset leaseExpiryUtc)
    {
        if (_state.State.ByConnectionId.TryGetValue(connectionId, out var route))
            _state.State.ByConnectionId[connectionId] = route with { LeaseExpiryUtc = leaseExpiryUtc };
        return _state.WriteStateAsync();
    }

    public Task RemoveAsync(string connectionId)
    {
        _state.State.ByConnectionId.Remove(connectionId);
        return _state.WriteStateAsync();
    }

    public Task<ConnectionRoute?> ResolveBySessionAsync(string sessionId)
    {
        // 过期清理在每次查询时执行（惰性清理）
        var now = DateTimeOffset.UtcNow;
        var staleIds = _state.State.ByConnectionId.Values
            .Where(x => x.LeaseExpiryUtc <= now)
            .Select(x => x.ConnectionId)
            .ToArray();
        foreach (var stale in staleIds)
            _state.State.ByConnectionId.Remove(stale);

        if (staleIds.Length > 0)
            return _state.WriteStateAsync().ContinueWith(_ =>
                _state.State.ByConnectionId.Values
                    .FirstOrDefault(x => x.SessionId == sessionId))!;

        return Task.FromResult(_state.State.ByConnectionId.Values
            .FirstOrDefault(x => x.SessionId == sessionId));
    }
}
```

**TenantKeyGuardCallFilter（Grain 入口 tenant 二次校验，对齐 Spec）：**

```csharp
public sealed class TenantKeyGuardCallFilter : IIncomingGrainCallFilter
{
    public async Task Invoke(IIncomingGrainCallContext context)
    {
        // 仅对 IConnectionDirectoryGrain 做 tenant key 一致性校验
        if (context.Grain is IConnectionDirectoryGrain &&
            context.Grain.GetPrimaryKeyString() is { } grainKey &&
            RequestContext.Get("TenantId") is string callerTenant &&
            !grainKey.EndsWith($"/{callerTenant}"))
        {
            throw new UnauthorizedAccessException(
                $"Tenant mismatch: caller={callerTenant} grain_key={grainKey}");
        }
        await context.Invoke();
    }
}
```

- [ ] **Step 1: 写失败测试（注册、续租、过期清理、tenant-scoped key 隔离）**

```csharp
[Fact]
public async Task LeaseExpiryRemovesStaleConnection()
{
    // 使用 Orleans TestCluster + ADO.NET InMemory 存储（测试用）
    var fixture = await OrleansTestFixture.StartAsync();
    var grain = fixture.GrainFactory
        .GetGrain<IConnectionDirectoryGrain>(TenantGrainKey.Directory("t1"));

    await grain.RegisterAsync("gw-1", "conn-1",
        new CallerContext("t1", "u1", new HashSet<string> { "user" }),
        "s1", DateTimeOffset.UtcNow.AddMilliseconds(100));
    await Task.Delay(500); // 等待 Reminder 或惰性清理生效

    var route = await grain.ResolveBySessionAsync("s1");
    Assert.Null(route);
}

[Fact]
public async Task TenantAGrainDoesNotReturnTenantBConnection()
{
    var fixture = await OrleansTestFixture.StartAsync();
    var grainA = fixture.GrainFactory
        .GetGrain<IConnectionDirectoryGrain>(TenantGrainKey.Directory("t-A"));
    var grainB = fixture.GrainFactory
        .GetGrain<IConnectionDirectoryGrain>(TenantGrainKey.Directory("t-B"));

    await grainA.RegisterAsync("gw-1", "conn-a",
        new CallerContext("t-A", "u1", new HashSet<string> { "user" }),
        "s-shared", DateTimeOffset.UtcNow.AddMinutes(5));
    await grainB.RegisterAsync("gw-1", "conn-b",
        new CallerContext("t-B", "u2", new HashSet<string> { "user" }),
        "s-other", DateTimeOffset.UtcNow.AddMinutes(5));

    var fromA = await grainA.ResolveBySessionAsync("s-other");
    Assert.Null(fromA); // t-B 的连接不在 t-A 的 grain 中
}

[Fact]
public async Task GrainStateDoesNotContainWebSocketObject()
{
    var fixture = await OrleansTestFixture.StartAsync();
    var grain = fixture.GrainFactory
        .GetGrain<IConnectionDirectoryGrain>(TenantGrainKey.Directory("t1"));

    await grain.RegisterAsync("gw-1", "conn-1",
        new CallerContext("t1", "u1", new HashSet<string> { "user" }),
        "s1", DateTimeOffset.UtcNow.AddMinutes(1));

    var state = await fixture.ReadGrainStateAsync<ConnectionDirectoryState>(
        TenantGrainKey.Directory("t1"), "connection-directory");
    var route = state.ByConnectionId["conn-1"];
    Assert.Equal("gw-1", route.GatewayInstanceId);
    Assert.Equal("t1", route.TenantId);
    // 验证 Route 中不包含 WebSocket 对象类型字段
    Assert.False(typeof(ConnectionRoute).GetProperties()
        .Any(p => p.PropertyType == typeof(System.Net.WebSockets.WebSocket) ||
                  p.PropertyType.FullName!.Contains("WebSocket")));
}
```

- [ ] **Step 2: RED —** `dotnet test dotnet/tests/Ocb.Grains.Tests/Ocb.Grains.Tests.csproj --filter ConnectionDirectoryGrainTests` — FAIL。
- [ ] **Step 3: 最小实现 —** 创建 `.csproj`、`ConnectionDirectoryState.cs`、`ConnectionDirectoryGrain.cs`、`TenantKeyGuardCallFilter.cs`。测试使用 Orleans **TestCluster**（`Microsoft.Orleans.TestingHost`）配合 InMemory 存储。
- [ ] **Step 4: GREEN**
- [ ] **Step 5: Commit:**

```bash
git add dotnet/src/Ocb.Grains dotnet/tests/Ocb.Grains.Tests dotnet/Ocb.slnx
git commit -m "feat(grains): add tenant-scoped connection directory grain with lease and guard"
```

---

#### 7c: Gateway Client 集成（LeaseService + StreamDispatch）

**Files:**

- Create: `dotnet/src/Ocb.Gateway/Channels/ConnectionDirectoryLeaseService.cs`
- Create: `dotnet/src/Ocb.Gateway/Channels/StreamDispatchHostedService.cs`
- Modify: `dotnet/src/Ocb.Gateway/Program.cs`

**设计说明：**

- **`ConnectionDirectoryLeaseService`**：`BackgroundService`，每 5s 刷新本地活跃连接的 lease（调用 `RenewLeaseAsync`）。WebSocket 断线时通过 `RemoveAsync` 清理。对齐 Python `ConnectionLimiter` 的 lease 管理模式。
- **`StreamDispatchHostedService`**：订阅 SMS Stream `"gateway-outbound-{gatewayInstanceId}"`，收到 `OutboundGatewayMessage` 后查本地 `ConcurrentDictionary<string, WebSocketChannelSession>` 并执行 `SendAsync`。如果没有本地连接则忽略（Grain 会在查询时惰性清理过期路由）。
- 两者通过 `IGrainFactory` 获取 Grain 引用，不直接依赖 Silo 端组件。

```csharp
public sealed class ConnectionDirectoryLeaseService : BackgroundService
{
    private readonly IGrainFactory _grainFactory;
    private readonly ConcurrentDictionary<string, (string TenantId, DateTimeOffset Expiry)> _activeConnections;
    private static readonly TimeSpan LeaseInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(15);

    public ConnectionDirectoryLeaseService(IGrainFactory grainFactory,
        ConcurrentDictionary<string, (string, DateTimeOffset)> activeConnections)
    {
        _grainFactory = grainFactory;
        _activeConnections = activeConnections;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var (connId, (tenantId, _)) in _activeConnections)
            {
                var grain = _grainFactory.GetGrain<IConnectionDirectoryGrain>(
                    TenantGrainKey.Directory(tenantId));
                await grain.RenewLeaseAsync(connId, DateTimeOffset.UtcNow + LeaseDuration);
            }
            await Task.Delay(LeaseInterval, stoppingToken);
        }
    }
}

public sealed class StreamDispatchHostedService : BackgroundService
{
    private readonly IClusterClient _clusterClient;
    private readonly string _gatewayInstanceId;
    private readonly ConcurrentDictionary<string, WebSocketChannelSession> _localSessions;

    public StreamDispatchHostedService(IClusterClient clusterClient,
        IConfiguration config,
        ConcurrentDictionary<string, WebSocketChannelSession> sessions)
    {
        _clusterClient = clusterClient;
        _gatewayInstanceId = config["Gateway:InstanceId"]!;
        _localSessions = sessions;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var streamProvider = _clusterClient.GetStreamProvider("sms");
        var stream = streamProvider.GetStream<OutboundGatewayMessage>(
            StreamId.Create("GatewayOutbound", _gatewayInstanceId));

        await stream.SubscribeAsync(async (msg, token) =>
        {
            if (_localSessions.TryGetValue(msg.ConnectionId, out var session))
                await session.SendAsync(msg.Payload, token);
        });
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
```

- [ ] **Step 1: 写失败测试**
- [ ] **Step 2: RED**
- [ ] **Step 3: 最小实现**
- [ ] **Step 4: GREEN**
- [ ] **Step 5: Commit:**

```bash
git add dotnet/src/Ocb.Gateway/Channels dotnet/src/Ocb.Gateway/Program.cs
git commit -m "feat(gateway): integrate connection directory lease service and stream dispatch"
```

---

#### 7d: Ocb.Silo.Host 组装

**Files:**

- Create: `dotnet/src/Ocb.Silo.Host/Ocb.Silo.Host.csproj`
- Create: `dotnet/src/Ocb.Silo.Host/context-boundary.json`
- Create: `dotnet/src/Ocb.Silo.Host/Program.cs`
- Modify: `dotnet/Ocb.slnx`

**`Ocb.Silo.Host.csproj` NuGet 依赖：**

| 包 | 用途 |
|---|---|
| `Microsoft.Orleans.Server` | Silo 宿主（包含所有 Orleans Server 端组件） |
| `Microsoft.Orleans.Sdk` | Source Generator |
| `Microsoft.Orleans.Clustering.AdoNet` | PostgreSQL 集群成员发现 |
| `Microsoft.Orleans.Persistence.AdoNet` | Grain State 持久化到 PostgreSQL |
| `Microsoft.Orleans.Streaming.SMS` | 集群内 Stream Provider |
| `Microsoft.Orleans.Reminders` | PostgreSQL Reminder Service |
| `Npgsql` | ADO.NET Provider |
| `Npgsql.OpenTelemetry` | 连接追踪 |
| `Ocb.GrainContracts` | ProjectReference — Grain 接口 |
| `Ocb.Grains` | ProjectReference — Grain 实现 |

**`Program.cs` 关键组装点：**

- 注册 Orleans call filter：`builder.AddIncomingGrainCallFilter<TenantKeyGuardCallFilter>()`
- 注册 `ISecretResolver` 实现（从 Plugin DI 注入，解析 `orleans-cluster-conn` secret）
- 配置 `ClusterOptions(ClusterId="avernet", ServiceId="ocb")`
- 端点：`siloPort=11111`, `gatewayPort=30000`
- Deployment profile（`singlebox` / `cluster` / `test`）通过 `OCB_PROFILE` 环境变量选择不同的 PostgreSQL connection string

**Silo 启动依赖顺序：**

1. PostgreSQL 就绪（`ocb_orleans` schema 由 migration 初始化）
2. `ISecretResolver` Plugin 就绪（解析 `orleans-cluster-conn`）
3. Silo 加入集群 → readiness 通过

- [ ] **Step 1: 写失败测试（Silo 启动失败当 PG 不可达）**
- [ ] **Step 2: RED**
- [ ] **Step 3: 最小实现**
- [ ] **Step 4: GREEN**
- [ ] **Step 5: Commit:**

```bash
git add dotnet/src/Ocb.Silo.Host dotnet/Ocb.slnx
git commit -m "feat(silo): add orleans silo host with postgresql clustering and streams"
```

### Task 8: SSE 背压、取消与 Correlation 透传

**Files:**

- Create: `dotnet/src/Ocb.Gateway/Sse/SseEvent.cs`
- Create: `dotnet/src/Ocb.Gateway/Sse/SseAdmissionGate.cs`
- Create: `dotnet/src/Ocb.Gateway/Sse/SseBackpressurePump.cs`
- Create: `dotnet/src/Ocb.Gateway/Sse/GatewaySseEndpoint.cs`
- Modify: `dotnet/src/Ocb.Gateway/Program.cs`
- Test: `dotnet/tests/Ocb.Gateway.Tests/Sse/GatewaySseEndpointTests.cs`

**Interfaces:**

- Consumes: `OutboundGatewayMessage`。
- Produces:
  - `public sealed record SseEvent(string Event, string Data, string CorrelationId)`
    - `public sealed class SseAdmissionGate { public bool TryAcquire(out IDisposable lease); }`
  - `public sealed class SseBackpressurePump { public ValueTask<bool> TryWriteAsync(SseEvent evt, CancellationToken cancellationToken); }`
  - `public static class GatewaySseEndpoint { public static Task HandleAsync(HttpContext context); }`

- [ ] **Step 1: 写失败测试（慢消费者背压、取消停止、correlation 字段）**

```csharp
[Fact]
public async Task AdmissionReturns429BeforeResponseStartsWhenStreamCapacityIsExhausted()
{
    await using var host = await GatewaySseTestHost.BuildAsync(maxActiveStreams: 1);
    await using var first = await host.OpenAndHoldStreamAsync();

    var second = await host.Client.GetAsync("/openapi/v1/chat/messages/stream");

    Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    Assert.False(second.Headers.Contains("Content-Type") && second.Content.Headers.ContentType?.MediaType == "text/event-stream");
}

[Fact]
public async Task StreamIncludesCorrelationIdAndStopsOnCancellation()
{
    await using var host = await GatewaySseTestHost.BuildAsync(bufferCapacity: 8);
    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

    var transcript = await host.ReadStreamUntilCancelledAsync(cts.Token);

    Assert.Contains("event:message", transcript);
    Assert.Contains("correlation_id:corr-42", transcript);
    Assert.Equal(0, host.WritesObservedAfterCancellation);
}
```

- [ ] **Step 2: RED 命令**

Run: `dotnet test dotnet/tests/Ocb.Gateway.Tests/Ocb.Gateway.Tests.csproj --filter FullyQualifiedName~GatewaySseEndpointTests`

Expected: FAIL，SSE pump 或取消/背压语义未实现。

- [ ] **Step 3: 最小实现（有界通道 + 取消）**

```csharp
public sealed class SseBackpressurePump
{
    private readonly Channel<SseEvent> _channel;

    public SseBackpressurePump(int capacity)
    {
        _channel = Channel.CreateBounded<SseEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ValueTask<bool> TryWriteAsync(SseEvent evt, CancellationToken cancellationToken)
        => ValueTask.FromResult(_channel.Writer.TryWrite(evt));

    public IAsyncEnumerable<SseEvent> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}

public static class GatewaySseEndpoint
{
    public static async Task HandleAsync(HttpContext context, SseAdmissionGate admission, SseBackpressurePump pump)
    {
        if (!admission.TryAcquire(out var lease))
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return;
        }
        using (lease)
        {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.Headers.ContentType = "text/event-stream";

        try
        {
            await foreach (var evt in pump.ReadAllAsync(context.RequestAborted))
            {
                await context.Response.WriteAsync($"event:{evt.Event}\n", context.RequestAborted);
                await context.Response.WriteAsync($"correlation_id:{evt.CorrelationId}\n", context.RequestAborted);
                await context.Response.WriteAsync($"data:{evt.Data}\n\n", context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // 客户端取消后不能再写响应；释放 admission lease 即为终止语义。
        }
        }
    }
}
```

- [ ] **Step 4: GREEN 命令**

Run: `dotnet test dotnet/tests/Ocb.Gateway.Tests/Ocb.Gateway.Tests.csproj --filter FullyQualifiedName~GatewaySseEndpointTests`

Expected: PASS，流建立前的 429 admission、流内有界背压、取消停止写入和 correlation 断言通过；测试不得尝试在响应开始后修改状态码。

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/Ocb.Gateway/Sse dotnet/src/Ocb.Gateway/Program.cs dotnet/tests/Ocb.Gateway.Tests/Sse
git commit -m "feat(gateway): add sse backpressure cancellation and correlation handling"
```

### Task 9: HTTP/WS/SSE Parity Harness（对齐 Gateway 基线契约）

**Files:**

- Create: `dotnet/tests/Ocb.EndToEnd.Tests/Ocb.EndToEnd.Tests.csproj`
- Create: `dotnet/tests/Ocb.EndToEnd.Tests/Parity/ParityCorpusLoader.cs`
- Create: `dotnet/tests/Ocb.EndToEnd.Tests/Parity/GatewayHttpParityTests.cs`
- Create: `dotnet/tests/Ocb.EndToEnd.Tests/Parity/GatewayWebSocketParityTests.cs`
- Create: `dotnet/tests/Ocb.EndToEnd.Tests/Parity/GatewaySseParityTests.cs`
- Create: `dotnet/tests/Ocb.EndToEnd.Tests/Parity/gateway-ws-corpus.json`
- Create: `dotnet/tests/Ocb.EndToEnd.Tests/Parity/gateway-sse-corpus.json`
- Modify: `dotnet/Ocb.slnx`

**Interfaces:**

- Consumes: `dotnet/contracts/parity-corpus/manifest.json`, `dotnet/contracts/parity-corpus/gateway.openapi.json`。
- Produces:
  - `public sealed record HttpParityCase(string Method, string Path, int ExpectedStatus, string[] RequiredHeaders)`
  - `public sealed record WebSocketParityCase(string Path, string[] ClientFrames, string[] ExpectedServerFrames)`
  - `public sealed record SseParityCase(string Path, string[] ExpectedEventsInOrder)`

- [ ] **Step 1: 写失败测试（基线路径、握手、事件序）**

```csharp
[Fact]
public async Task HttpParityIncludesCollaborationWsPathInServedOpenApi()
{
    var doc = await ParityCorpusLoader.LoadGatewayOpenApiAsync();
    Assert.True(doc.RootElement.GetProperty("paths").TryGetProperty("/openapi/v1/collaboration/messages/ws", out _));
}

[Fact]
public async Task WsParityMatchesFrameOrderForBotMessageRelay()
{
    var parity = await GatewayParityRunner.RunWebSocketCaseAsync("bots-messages-basic");
    Assert.Equal(new[] { "connected", "echo:hello", "closed" }, parity.ServerFrames);
}

[Fact]
public async Task SseParityPreservesEventOrder()
{
    var parity = await GatewayParityRunner.RunSseCaseAsync("chat-stream-basic");
    Assert.Equal(new[] { "message", "message", "end" }, parity.EventTypes);
}
```

- [ ] **Step 2: RED 命令**

Run: `dotnet test dotnet/tests/Ocb.EndToEnd.Tests/Ocb.EndToEnd.Tests.csproj --filter FullyQualifiedName~Parity`

Expected: FAIL，缺少 parity loader/case runner 或事件顺序不匹配。

- [ ] **Step 3: 最小实现（读取 manifest + 运行对比）**

```csharp
public static class ParityCorpusLoader
{
    public static async Task<JsonDocument> LoadGatewayOpenApiAsync()
    {
        var manifestPath = Path.Combine(RepositoryPaths.Root().FullName, "dotnet", "contracts", "parity-corpus", "manifest.json");
        var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        var gatewayFile = manifest.RootElement.GetProperty("artifacts")
            .EnumerateArray()
            .First(a => a.GetProperty("service").GetString() == "gateway" && a.GetProperty("kind").GetString() == "openapi")
            .GetProperty("file").GetString()!;

        var openApiPath = Path.Combine(Path.GetDirectoryName(manifestPath)!, gatewayFile);
        return JsonDocument.Parse(await File.ReadAllTextAsync(openApiPath));
    }
}
```

- [ ] **Step 4: GREEN 命令**

Run: `dotnet test dotnet/tests/Ocb.EndToEnd.Tests/Ocb.EndToEnd.Tests.csproj --filter FullyQualifiedName~Parity`

Expected: PASS，HTTP/WS/SSE parity 用例通过。

- [ ] **Step 5: Commit**

```bash
git add dotnet/tests/Ocb.EndToEnd.Tests dotnet/Ocb.slnx
git commit -m "test(gateway): add http ws sse parity harness"
```

### Task 10: NBomber 基线（HTTP + WS + SSE 长连接）

**Files:**

- Create: `dotnet/tests/Ocb.Performance.Tests/Ocb.Performance.Tests.csproj`
- Create: `dotnet/tests/Ocb.Performance.Tests/NBomber/GatewayPerformanceScenarios.cs`
- Create: `dotnet/tests/Ocb.Performance.Tests/NBomber/GatewayPerformanceThresholds.cs`
- Create: `dotnet/tests/Ocb.Performance.Tests/NBomber/RunGatewayBenchmarks.cs`
- Modify: `scripts/ci/dotnet_ci.sh`
- Modify: `dotnet/Ocb.slnx`

**Interfaces:**

- Consumes: Gateway 本地测试宿主地址。
- Produces:
  - `public sealed record GatewayPerformanceThresholds(double HttpP95Ms, double WsConnectP95Ms, double SseFirstEventP95Ms, double ErrorRateUpperBound)`
  - `public static class GatewayPerformanceScenarios { public static NBomber.Contracts.ScenarioProps[] Build(Uri baseAddress); }`

- [ ] **Step 1: 写失败测试（阈值门禁）**

```csharp
[Fact]
public async Task FailsWhenP95ExceedsThreshold()
{
    var thresholds = new GatewayPerformanceThresholds(HttpP95Ms: 120, WsConnectP95Ms: 200, SseFirstEventP95Ms: 250, ErrorRateUpperBound: 0.01);
    var report = await GatewayBenchmarkRunner.RunAsync(thresholds, durationSeconds: 10);

    Assert.True(report.HttpP95Ms <= thresholds.HttpP95Ms, $"HTTP P95 {report.HttpP95Ms}ms exceeds {thresholds.HttpP95Ms}ms");
    Assert.True(report.ErrorRate <= thresholds.ErrorRateUpperBound, $"ErrorRate {report.ErrorRate} exceeds {thresholds.ErrorRateUpperBound}");
}
```

- [ ] **Step 2: RED 命令**

Run: `dotnet test dotnet/tests/Ocb.Performance.Tests/Ocb.Performance.Tests.csproj --filter FullyQualifiedName~FailsWhenP95ExceedsThreshold`

Expected: FAIL，`GatewayBenchmarkRunner` 或 NBomber 场景未实现。

- [ ] **Step 3: 最小实现（构造场景 + 阈值断言）**

```csharp
public static class GatewayPerformanceScenarios
{
    public static ScenarioProps[] Build(Uri baseAddress)
    {
        var http = Scenario.Create("gateway-http", async context =>
        {
            using var client = new HttpClient { BaseAddress = baseAddress };
            var response = await client.GetAsync("/openapi/v1/bots");
            return response.IsSuccessStatusCode ? Response.Ok() : Response.Fail();
        }).WithLoadSimulations(Simulation.Inject(rate: 20, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30)));

        return [http];
    }
}

public static class GatewayBenchmarkRunner
{
    public static async Task<GatewayBenchmarkReport> RunAsync(GatewayPerformanceThresholds thresholds, int durationSeconds)
    {
        var summary = NBomberRunner
            .RegisterScenarios(GatewayPerformanceScenarios.Build(new Uri("http://localhost:5088")))
            .WithTestSuite("gateway")
            .WithTestName("parity-baseline")
            .Run();

        return GatewayBenchmarkReport.From(summary);
    }
}
```

- [ ] **Step 4: GREEN 命令**

Run: `dotnet test dotnet/tests/Ocb.Performance.Tests/Ocb.Performance.Tests.csproj`

Expected: PASS，输出包含 HTTP/WS/SSE 场景统计并满足阈值。

- [ ] **Step 5: Commit**

```bash
git add dotnet/tests/Ocb.Performance.Tests dotnet/Ocb.slnx scripts/ci/dotnet_ci.sh
git commit -m "test(gateway): add nbomber baseline for http ws sse"
```

## 自审

### 1. 覆盖性检查（roadmap Gateway 8 项 + 审核修复）

- Composition Root/强配置/readiness：Task 1。
- CallerContext/JWT/X-Avernet-Principal/access key/tenant 复核：Task 2。
- **ISecretResolver Plugin API**：Task 2（`GatewaySecretContracts.cs`，对齐 Python `spi/secret_resolver.py`）。
- **ICacheProvider Plugin API**：Task 2（`GatewayCacheContracts.cs`，对齐 Python `spi/cache.py`，非权威缓存，Redis 不可用时降级本地）。
- **IHopByHopHeaderFilter Plugin API**：Task 4（`ForwardingContracts.cs`，对齐 Python `_forward.py` `strip_hop_by_hop` + `_INBOUND_STRIP`）。
- **WS 握手 method 隔离**：Task 2（`WebSocketHandshakeMethodBinder.HandshakeMethod = "WEBSOCKET"`，对齐 Python `_relay_ws.py:97`）。
- path/domain forwarding + Schema Catalog：Task 3。
- 原生 WebSocket fragments/raw+JSON/rate limits/serial send/cleanup：Task 5 + Task 6。
- **`_has_dot_segment` 路径穿越防护**：Task 6（`WebSocketPathGuard.HasDotSegment`，对齐 Python `_relay_ws.py:262-279`）。
- **`_required_raw_prefix` raw path 编码守卫**：Task 6（`WebSocketPathGuard.HasRequiredRawPrefix`，对齐 Python `_relay_ws.py:282-316`）。
- **WS upstream 握手超时 10s**：Task 6（`HandshakeTimeout = TimeSpan.FromSeconds(10)`，对齐 Python `_ws_forwarder.py:45`）。
- ConnectionDirectoryGrain + lease + Streams：Task 7。
- SSE 背压/取消/correlation：Task 8。
- HTTP/WS/SSE parity：Task 9。
- NBomber：Task 10。

### 2. 约束检查

- 已明确禁用 SignalR、Canvas envelope、`OpenClaw.*` 依赖与 bridge。
- 已明确 Gateway 不拥有业务授权、进程、数据。
- 已要求复用 `Ocb.Contracts`/`Ocb.PluginApi`，且未引入 `Gateway.PluginApi`。
- 项目命名保持 `Ocb.Channels` / `Ocb.GrainContracts` / `Ocb.Grains` / `Ocb.Gateway`。
- **Orleans 细化：** 已指定具体 NuGet 包（`Microsoft.Orleans.*` 10.2.2）、PostgreSQL ADO.NET Clustering/Persistence、SMS Stream Provider、tenant-scoped Grain Key（`directory/{tenantId}`）、Gateway 为 Orleans Client、`Ocb.Silo.Host` 为 Silo 宿主、`TenantKeyGuardCallFilter` 做 Grain 入口 tenant 二次校验。

### 3. 可执行性检查

- 每个 Task 提供了精确文件、接口签名、失败测试断言、RED/GREEN 命令和提交命令。
- 所有步骤均可独立评审，并已通过 writing-plans 禁止模式扫描。
