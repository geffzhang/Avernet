# .NET Migration Plan Audit Report

**日期:** 2026-08-08
**审核范围:** 5 份 implementation plan vs Spec + Python `src/` 源码 + openclaw.net 参考代码 + dotnet/ Phase 0 基线

## 审核结论

5 份 plan 整体方向正确，架构约束一致，TDD 流程规范。但存在 **3 个关键跨-plan 冲突** 和 **2 个 Gateway 安全语义遗漏**需要在实施前解决。

---

## 一、跨-Plan 项目创建冲突（CRITICAL）

以下项目被多个 plan 声明创建，实施时将产生文件冲突和归属混乱：

| 冲突项目 | 声明的 Plan | 冲突性质 |
|---|---|---|
| `Ocb.GrainContracts` + `Ocb.Grains` | gateway-channels Task 7, runtime-worker Task 1, backend-skills, fusion-vector | **4 个 plan 都创建同名 .csproj**，各自往 `Ocb.GrainContracts/` 下放不同子目录（`Channels/`、`Runtime/`、`Fusion/`） |
| `Ocb.Infrastructure.PostgreSql` | baas Task 1, backend-skills | 两个 plan 都创建 .csproj + `context-boundary.json`；BaaS 放 `ocb_business` schema，Backend 也放 `ocb_business` |

**建议:** 由第一个执行的 plan（按 spec 交付顺序：Foundation → Gateway → Runtime Worker → BaaS → Fusion → Backend）创建共享项目骨架（.csproj + context-boundary.json），后续 plan 只往里追加子目录和文件。`Ocb.Infrastructure.PostgreSql` 需要明确的 schema 所有权声明：`ocb_business` 是共享 schema，各领域的 EF Core DbContext 通过 bounded context 隔离。

---

## 二、Python→.NET 迁移映射审计

### 2.1 Gateway (`dotnet-gateway-channels.md`) — 覆盖率: 85%

**已正确映射的 Python Gateway 能力：**
- ✅ domain-based path routing（`_forward.py`: `domain_map.http_domain_for(path)` → `DomainMap.ResolveHttp(path)`）
- ✅ HTTP forwarding with principal injection（`_forward.py`: `_attach_identities` → `PrincipalHeaderInjector.InjectSignedPrincipal`）
- ✅ WebSocket proxy relay（`_relay_ws.py`: resolve→authenticate→dial upstream→accept→relay bidirectionally → `GatewayWebSocketEndpoint.HandleAsync`）
- ✅ Hop-by-hop header stripping（Python: `strip_hop_by_hop` + `_INBOUND_STRIP` set → plan 中的 `PrincipalHeaderInjector` 移除了 `Host` 和 `X-Avernet-Principal`）
- ✅ JWT + signed principal 双重验证（Python: `state.authenticator.authenticate(method, path, bundle)` → `PrincipalVerificationMiddleware`）
- ✅ Origin 校验（openclaw.net 参考: `WebSocketEndpoints.IsOriginAllowed` → `WebSocketOriginPolicy`）
- ✅ Schema Catalog + Served OpenAPI（Python: `state.domain_map` → `ISchemaCatalog` + `ServedOpenApiBuilder`）
- ✅ SSE backpressure + cancellation
- ✅ ConnectionDirectoryGrain + Orleans Streams 集群路由
- ✅ NBomber 性能基线

**遗漏/需要补充：**

| Python 源码位置 | 能力 | 状态 |
|---|---|---|
| `_relay_ws.py:262-279` `_has_dot_segment()` | **路径穿越攻击防护**：拒绝包含 `.` 或 `..` 的 decoded path 段 | **缺失** — plan 的 `GatewayWebSocketEndpoint` 无此检查。这是安全关键：Python GW 注释明确说明 "refusing here does not depend on assuming which of them normalises" |
| `_relay_ws.py:282-316` `_required_raw_prefix()` | **Raw path 编码守卫**：确保 decoded path（用于 auth/routing）与 raw path（用于 upstream dial）在路由前缀上一致，防止 "authorised as one resource, dialled as another" | **缺失** — plan 无此检查 |
| `_relay_ws.py:97` `_HANDSHAKE_METHOD = "WEBSOCKET"` | **WS 握手专用 auth method**：防止 "no identity required" 豁免从 WS 平面泄漏到 HTTP 平面 | **未提及** — `TenantConsistencyFilter` 是 route-level tenant 校验，不是 method-level 豁免隔离 |
| `_ws_forwarder.py:111-112` `max_size=None` | **透明帧大小**：不限制 upstream→client 方向的帧大小 | **隐式覆盖** — `FragmentAccumulator` 有 `maxBytes` 限制，但这是 client→upstream 方向；upstream→client 方向未提 |
| `_forward.py:134` `_PRINCIPAL_HEADER = "X-Avernet-Principal"` | Header 名称常量 | ✅ 已在 plan 中使用 |
| `_forward.py:140` `_INBOUND_STRIP = frozenset({"host", "x-avernet-principal"})` | 入站 header 剥离列表 | ✅ `PrincipalHeaderInjector` 实现了 Host + X-Avernet-Principal 移除 |
| `_ws_forwarder.py:45` `_HANDSHAKE_TIMEOUT_SECONDS = 10.0` | WS upstream 握手超时 | **未提及** — `IWebSocketUpstreamConnector` 接口未定义超时语义 |
| `_forward.py:48-66` `_error()` + `_request_id()` | Gateway 自身错误 envelope 格式（`{code, message, data, request_id}`） | **未提及** — plan 侧重 forwarding seam，但 Gateway 自己的错误格式应与 Python 基线对齐 |

### 2.2 Runtime Worker (`dotnet-runtime-worker.md`) — 覆盖率: 80%

**已正确映射：**
- ✅ Engine WebSocket server（Python `ws_server.py`: `handle_connection` → handshake → message loop → chat.send/chat.abort/chat.subscribe/sessions.reset → `EngineWsEndpoint` + `EngineWsMethodDispatcher`）
- ✅ Session HTTP parity（Python `session/router.py` → `SessionParityController`）
- ✅ BotRuntimeGrain desired/observed state 协调
- ✅ Worker assignment + lease（Python `EngineManager` → `RuntimeAssignmentCoordinator`）
- ✅ 进程生命周期管理（Python `gateway_client.py` OpenClaw client → `OpenClawGatewayTypedClient`）
- ✅ Workspace isolation + orphan recovery
- ✅ Skills materialization boundary（非 publication）

**遗漏/边界说明：**

| Python engine API module | Plan 覆盖 | 说明 |
|---|---|---|
| `session/` (HTTP + WS) | ✅ 覆盖 | Session HTTP parity + Engine WS parity（两者） |
| `approvals/` | ❌ 未覆盖 | 审批路由归属 Runtime Worker 还是 Backend？spec 未明确分配 |
| `aicoding/`, `aicoding_sessions/` | ❌ 未覆盖 | 属于 corp 域，oss plan 可以不覆盖，但需显式声明排除 |
| `bash/`, `web_shell/`, `file/` | ❌ 未覆盖 | 工具执行路由，应符合后续 Tool plan |
| `bot/`, `cron/`, `mcp/`, `node/` | ❌ 未覆盖 | 各归各域 plan |
| `skills/`, `work_item/` | ❌ 未覆盖 | 归属 Backend Skills plan |
| `models/`, `default_config/` | ❌ 未覆盖 | 归属后续 Config/Models plan |
| `resource_materialization/` | ✅ 部分 | Skills materialization boundary |
| `zero_check/` | ❌ 未覆盖 | Auth gate — 归属 BaaS plan? |
| `engine/` (status/switch/restart/capabilities) | ✅ 覆盖 | `EngineWsMethodDispatcher` |
| `claude_code_ws.py` | ✅ 覆盖 | `ClaudeCodeRelayTypedClient` |

**关键遗漏：**
- `ws_server.py:226-241` **连接数限制**（`load_max_connections()` + `ConnectionLimiter.try_acquire`）— plan 未提及 Engine WS server 的 connection admission control
- `ws_server.py:330-339` **协议版本协商**（`params.min_protocol > PROTOCOL_VERSION`）— plan 的 `EngineWsProtocolGuards` 应包含此语义

### 2.3 BaaS (`dotnet-baas.md`) — 覆盖率: 75%

**已正确映射：**
- ✅ Device + Template CRUD contracts
- ✅ Health endpoint contracts
- ✅ QPM (quota per minute) contracts
- ✅ SSE contracts
- ✅ SM4 crypto plugin
- ✅ Docker/K8s sandbox conformance suite
- ✅ Plugin API 分离（Sandbox/Crypto/Queue/ExternalGateway）
- ✅ invoke-http / API gateway 透明转发

**遗漏/需要澄清：**

| Python `src/baas/` 内容 | Plan 覆盖 | 说明 |
|---|---|---|
| `community/routers/admin/` (api_gateway, publish_admin) | ❌ 未覆盖 | Admin 路由归属 BaaS 还是 Backend？ |
| `community/routers/bot_service/cmd.py` | ❌ 未覆盖 | Bot command 路由 |
| `community/routers/bot_service/file_transfer.py` | ❌ 未覆盖 | 文件传输路由 |
| `community/routers/bot_service/http_conn.py` | ❌ 未覆盖 | HTTP connection 路由 |
| `community/routers/bot_service/management.py` | ❌ 未覆盖 | Bot 管理路由 |
| `community/routers/bot_service/publish.py` | ✅ 覆盖 | `PublishContracts` |
| `community/routers/bot_service/wss.py` | ❌ 未覆盖 | WebSocket secure? 归属 Gateway? |
| `community/routers/bcn_downlink/` | ❌ 未覆盖 | BCN downlink — 归属 BaaS 还是独立 BCN plan? |

### 2.4 Backend Skills (`dotnet-backend-skills.md`) — 覆盖率: 70%

**已正确映射：**
- ✅ Caller Identity + Bot/Session/Asset contracts
- ✅ PostgreSQL `ocb_business` schema
- ✅ MinIO multipart/checksum/signed URL/range/temporary/deletion compensation
- ✅ Skills publication state machine（Python `skill_center/` → .NET `SkillPublicationRecord`）
- ✅ Skills activation request + Runtime reconcile
- ✅ Atomic publish fail-closed（回滚到旧视图）
- ✅ Tenant isolation 覆盖 identity/bot/session/asset/skill publication
- ✅ Download retry strategy（3次指数退避+jitter）
- ✅ Manifest contract version validation（`skills-pool-p3-v1`）

**遗漏/边界说明：**
- Python Backend 有 **50+ HTTP adapter 模块**（bot_chat, bot_management, bot_public, bot_collaborator, bot_dormant, bot_render_screen, economy, expert_chat, resources, system, system_config, devices, approvals, mcp, channel, harness, quality, desktop, etc.）— plan 仅覆盖 Skills domain，这是正确的分域策略，但需要确认其他模块各自归属的计划
- `caller_identity/router.py` 的 `update_mcp_call_type` PATCH endpoint 在 plan 中未提及

### 2.5 Fusion Vector (`dotnet-fusion-vector.md`) — 覆盖率: 75%

**已正确映射：**
- ✅ `IVectorStore` + `IHybridSearchStore` + `IVectorStoreAdministration`（对应 Python `vector_store.py`）
- ✅ SonnetDB + Qdrant 双 Provider（对应 Python `faiss_sqlite_vector_store.py` + `qdrant_*.py`）
- ✅ Conformance suite 共享（对应 Python `test_*` 测试模式）
- ✅ Embedding/Reranker 独立 Plugin API
- ✅ Fusion routes (`POST /api/v1/groups/{group_id}/fuse`)
- ✅ FusionJobGrain + IdempotencyKey
- ✅ Tenant key guard call filter
- ✅ Model/dimension 不匹配 fail closed

**遗漏/边界说明：**
- Python bcsfuse 有 **~250+ 文件**、30+ 领域服务 — plan 覆盖核心向量+fusion+worker paths，其他服务（worker lifecycle, profiling, team composition, conflict alignment, verify, planning, task understanding, expert diagnosis, peer review, etc.）需要后续 plan
- Python `worker_profile_content_store.py` / `worker_registry_store.py` — 这些是 `application/ports/`，应映射到 Plugin API 或 Contracts

---

## 三、openclaw.net 参考正确性审计

### 3.1 正确参考（保持模式，独立实现）

| openclaw.net 参考源 | 被参考的 Plan | 参考方式 | 评估 |
|---|---|---|---|
| `OpenClaw.Channels/WebSocketChannel.cs` | gateway-channels | Fragment 聚合、速率限制、串行发送、raw/JSON envelope 模式 | ✅ 正确：采纳了通道语义，但适配为 proxy relay（上游拨号+双向帧中继），而非 in-process message pipeline |
| `OpenClaw.Gateway/Endpoints/WebSocketEndpoints.cs` | gateway-channels | Origin 校验、auth、rate limit 三层防御 | ✅ 正确：pattern 一致，但 auth 机制换成 OCB 的 JWT+Principal 模型 |
| `OpenClaw.Core/Abstractions/IChannelAdapter.cs` | gateway-channels | Channel 抽象模式 | ✅ 正确：plan 未定义 `IChannelAdapter`（因为 GW 代理 WebSocket 而非处理 Channel 消息），这是正确的差异 |
| `OpenClaw.Core/Models/WebSocketEnvelopes.cs` | gateway-channels | WsClientEnvelope/WsServerEnvelope | ✅ 正确：plan 明确 "不引入 openclaw.net 特有的 Canvas envelope" |
| `OpenClaw.PluginKit/INativeDynamicPlugin.cs` | baas, backend-skills | Plugin 发现+注册模式 | ✅ 正确：`IPluginContract` 标记接口已定义在 Phase 0 |
| `OpenClaw.Core/Skills/SkillModels.cs` | backend-skills | SkillDefinition/SkillMetadata/SkillSource | ✅ 正确：OCB 使用独立 DTO（`SkillPublicationRecord`, `SkillVersionRef`, `SkillSourceScheme`），不复制 openclaw.net 的全量 SkillDefinition |
| `OpenClaw.Testing/ScenarioModels.cs` | gateway-channels, backend-skills | AgentScenario/ScenarioExpected/OracleResult 模式 | ✅ 正确：parity harness 参考了 Scenario 模式，但使用 OCB 自己的 parity-corpus |
| `OpenClaw.Core/Models/WebSocketEnvelopes.cs:123-130` `SkillStageGateEvent` | backend-skills | Artifact/stage-gate 模式 | ✅ 正确：Backend plan 的 atomic publish fail-closed 等价于 stage-gate 语义 |

### 3.2 差异（正确且必要的分歧）

| 领域 | openclaw.net 做法 | Avernet 做法 | 评估 |
|---|---|---|---|
| WebSocket 架构 | Kestrel in-process channel (server IS the gateway) | Proxy relay (Gateway 转发到上游 Engine) | ✅ spec 明确要求此差异 |
| Channel 模型 | `IChannelAdapter` 事件驱动 (`OnMessageReceived` event) | `WebSocketChannelSession` 双向帧中继 | ✅ 正确差异：代理不需要消息路由事件 |
| Plugin 模型 | `INativeDynamicPlugin` 原生动态加载 | `IPluginContract` 标记接口 + DI 组合 | ✅ 正确差异：OCB 使用 DI Compose，不需要动态 Plugin 发现 |
| Skill 模型 | `SkillDefinition` (Instructions + Metadata + Composition) | `SkillPublicationRecord` (publication/activation 元数据) | ✅ 正确差异：Backend 管理 publication 元数据，Runtime 管理物理物化 |

### 3.3 潜在错误参考

| 问题 | 涉及 Plan | 说明 |
|---|---|---|
| `IChannelAdapter` 语义混淆 | gateway-channels | openclaw.net 的 `IChannelAdapter` 是**入站通道抽象**（WhatsApp/Telegram/Discord），plan 的 `Ocb.Channels.WebSocketChannel` 其实是**出站代理中继**。plan 正确避免了直接套用 `IChannelAdapter`，但 `Ocb.Channels` 项目的命名可能产生误导 — 建议明确其为 "传输层通道基础能力" 而非 "消息通道适配器" |
| `INativeDynamicPlugin` 未引用 | baas, backend-skills | Phase 0 只定义了 `IPluginContract`（空标记接口），plan 中定义的 `IPrincipalTokenVerifier : IPluginContract` 是正确的 OCB 模式，不继承 openclaw.net 的 `INativeDynamicPlugin` |

---

## 四、Spec 合规性检查

| Spec 约束 | Gateway | Runtime | BaaS | Backend | Fusion |
|---|---|---|---|---|---|
| `CallerContext(string TenantId, string SubjectId, IReadOnlySet<string> Roles)` 统一使用 | ✅ | ✅ | ✅ | ✅ | ✅ |
| Service API / Plugin API 分离 | ✅ | ✅ | ✅ | ✅ | ✅ |
| Core/Contracts 不引用框架实现 | ✅ | ✅ | ✅ | ✅ | ✅ |
| 禁止 `OpenClaw.*` 依赖 | ✅ | ✅ | ✅ | ✅ | ✅ |
| WebSocket 对象不入 Grain State | ✅ | ✅ | N/A | N/A | N/A |
| Grain 仅薄协调层 | ✅ | ✅ | ⚠️ | ✅ | ✅ |
| 不使用 SignalR | ✅ | N/A | N/A | N/A | N/A |
| PostgreSQL `ocb_business` schema | N/A | N/A | ✅ | ✅ | N/A |
| tenant-scoped Grain key `(tenant_id, entity_id)` | ✅ | ✅ | ⚠️ | ✅ | ✅ |
| SM2 排除 | N/A | N/A | ✅ | N/A | N/A |
| Profile fail-closed | ✅ | N/A | N/A | N/A | ✅ |
| SSE 背压+取消+correlation | ✅ | N/A | ✅ | N/A | N/A |
| Skills 原子发布失败回滚 | N/A | N/A | N/A | ✅ | N/A |

⚠️ **BaaS Tenant:** Spec 要求 tenant identity 通过 `CallerContext` 显式传递。BaaS plan 的 `IDeviceServiceContract` 等方法签名未展示 `CallerContext` 参数传递——建议在所有 Service Contract 方法中显式包含 `CallerContext`。

⚠️ **BaaS Grain:** BaaS plan 未明确哪些对象用 Grain（Device? Template? Tenant?）。Spec 要求 "仅当对象具有稳定身份，且至少符合 lifecycle/state/concurrency/coordination 之一时使用 Grain"。BaaS 的 Device 和 Template 可能用 Grain（有 lifecycle+state），但 `invoke-http` 和 `API gateway` 应保持无状态转发。

---

## 五、其他发现

### 5.1 Python Gateway `spi/` → Ocb.PluginApi 映射完整性

Python Gateway 的 `spi/` 目录定义了以下 Port（协议接口），需要映射到 `Ocb.PluginApi`:

| Python SPI Port | .NET Plugin API 映射 | 状态 |
|---|---|---|
| `spi/auth.py` — `Authenticator.authenticate(method, path, bundle)` | `IPrincipalTokenVerifier.VerifyAsync(bearerToken, signedPrincipalHeader, ct)` | ✅ 已映射 |
| `spi/authn.py` — `Principal`, `PrincipalType`, `CredentialBundle` | `CallerContext` + `IAccessKeyResolver` | ✅ 已映射（但 `CredentialBundle` 的 headers/cookies/query 三源提取未在 plan 中出现） |
| `spi/principal_signer.py` — `PrincipalSigner.sign(identities, audience)` | `IPrincipalTokenSigner.SignAsync(callerContext, audience, ct)` | ✅ 已映射 |
| `spi/forwarder.py` — `Forwarder.forward(request)`, `strip_hop_by_hop` | `IHttpForwarder.ForwardAsync(request, ct)` | ✅ 已映射（但 `strip_hop_by_hop` 函数未在 Plugin API 中独立定义） |
| `spi/ws_forwarder.py` — `WebSocketForwarder.connect(request)`, `WEBSOCKET_HANDSHAKE_HEADERS` | `IWebSocketUpstreamConnector.ConnectAsync(uri, headers, ct)` | ✅ 已映射 |
| `spi/schema_catalog.py` — Schema 提供 | `ISchemaCatalog` | ✅ 已映射 |
| `spi/cache.py` — 缓存抽象 | **未映射** | ❌ 如需 Redis 缓存，需定义 `ICacheProvider : IPluginContract` |
| `spi/database.py` — 数据库抽象 | **部分映射** | ⚠️ `Ocb.Infrastructure.PostgreSql` 是具体实现，但缺少 `IDatabaseProvider : IPluginContract` 抽象（如果 Gateway 需要直接访问数据库的话 — 通常不需要，因为 DB 访问在 Backend/BaaS 层） |
| `spi/secret_resolver.py` — Secret 解析 | **未映射** | ❌ Spec 明确要求 "API key、模型凭据、MinIO 凭据、BCS secret 和 SM4 key 通过 Secret Plugin 解析"。应定义 `ISecretResolver : IPluginContract` |

### 5.2 EF Core vs Raw SQL

BaaS plan 和 Backend plan 都操作 `ocb_business` schema，但：
- BaaS plan 使用 "EF Core + Npgsql"
- Backend plan 使用 "EF Core + Npgsql"

两者一致，但未说明是否共享同一个 `DbContext` 还是各自拥有 bounded context 的 `DbContext`。建议每个领域模块有自己的 bounded `DbContext`（如 `BaasDbContext`、`BackendDbContext`），共享 migration 但隔离查询。

### 5.3 ConnectionDirectoryGrain Key 设计

Gateway plan 的 `IConnectionDirectoryGrain : IGrainWithStringKey` 使用单一 string key（`"directory"`）。这意味着**所有连接共享一个 Grain**，在集群中可能成为热点。Spec 要求 tenant-scoped key `(tenant_id, entity_id)`。建议按 tenant 分片：`IConnectionDirectoryGrain : IGrainWithStringKey`，key = `TenantEntityKey.Create(tenantId, "directory").ToString()`。

---

## 六、修复优先级

### CRITICAL（实施前必须解决）
1. 确定 `Ocb.GrainContracts`/`Ocb.Grains`/`Ocb.Infrastructure.PostgreSql` 的创建者（建议：由第一个执行 plan 创建骨架）
2. Gateway plan 补充 `_has_dot_segment` 路径穿越检查和 `_required_raw_prefix` 编码守卫

### HIGH（首期实施中解决）
3. Gateway plan 补充 `_HANDSHAKE_METHOD = "WEBSOCKET"` 语义（WS auth 豁免不泄漏到 HTTP）
4. 明确 BaaS 哪些实体使用 Grain，哪些保持无状态
5. 定义 `ISecretResolver : IPluginContract`

### MEDIUM（后续 plan 覆盖）
6. Spy Gateway `spi/cache` → `ICacheProvider : IPluginContract`
7. ConnectionDirectoryGrain 改为 tenant-scoped key
8. BaaS plan 中的 Service Contract 方法签名加入 `CallerContext` 参数
9. Engine WS server admission control（连接数限制 + 协议版本协商）

### LOW（文档级别）
10. `Ocb.Channels` 项目 README 明确其职责为 "传输层通道基础能力" 而非 "消息通道适配器"

---

## 附录：Python 源码映射速查表

### Gateway `spi/` → .NET Plugin API

```
src/gateway/src/gateway/community/spi/auth.py            → dotnet/src/Ocb.PluginApi/GatewayIdentityContracts.cs (IPrincipalTokenVerifier)
src/gateway/src/gateway/community/spi/authn.py           → dotnet/src/Ocb.Contracts/CallerContext.cs + IAccessKeyResolver
src/gateway/src/gateway/community/spi/principal_signer.py → dotnet/src/Ocb.PluginApi/ForwardingContracts.cs (IPrincipalTokenSigner)
src/gateway/src/gateway/community/spi/forwarder.py        → dotnet/src/Ocb.PluginApi/ForwardingContracts.cs (IHttpForwarder)
src/gateway/src/gateway/community/spi/ws_forwarder.py     → dotnet/src/Ocb.PluginApi/WebSocketForwardingContracts.cs (IWebSocketUpstreamConnector)
src/gateway/src/gateway/community/spi/schema_catalog.py   → dotnet/src/Ocb.PluginApi/SchemaCatalogContracts.cs (ISchemaCatalog)
src/gateway/src/gateway/community/spi/cache.py            → 未映射 → ICacheProvider
src/gateway/src/gateway/community/spi/database.py         → Ocb.Infrastructure.PostgreSql (具体实现)
src/gateway/src/gateway/community/spi/secret_resolver.py  → 未映射 → ISecretResolver
```

### Gateway web adapters → .NET Gateway

```
src/gateway/.../adapters/web/_forward.py    → dotnet/src/Ocb.Gateway/Forwarding/HttpForwardingEndpoint.cs
src/gateway/.../adapters/web/_relay_ws.py   → dotnet/src/Ocb.Gateway/WebSocket/GatewayWebSocketEndpoint.cs
src/gateway/.../adapters/web/_ws_forwarder.py → dotnet/src/Ocb.PluginApi/WebSocketForwardingContracts.cs + 具体实现（在 Composition Root 注册）
src/gateway/.../adapters/web/_auth.py       → dotnet/src/Ocb.Gateway/Auth/PrincipalVerificationMiddleware.cs
src/gateway/.../adapters/web/app.py         → dotnet/src/Ocb.Gateway/Program.cs
```

### Engine WS transport → .NET Runtime Worker

```
src/engine/.../api/transport/ws_server.py   → dotnet/src/Ocb.Runtime.Worker/Api/WebSocket/EngineWsEndpoint.cs + EngineWsMethodDispatcher.cs
src/engine/.../api/transport/openclaw_client_proxy.py → dotnet/src/Ocb.Runtime.Worker/Infra/Clients/OpenClaw/OpenClawGatewayTypedClient.cs
src/engine/.../api/transport/claude_code_ws.py → dotnet/src/Ocb.Runtime.Worker/Infra/Clients/ClaudeCode/ClaudeCodeRelayTypedClient.cs
src/engine/.../api/app.py                   → dotnet/src/Ocb.Runtime.Worker/Program.cs
```

### Backend → .NET Backend

```
src/backend/.../adapters/http/caller_identity/router.py → dotnet/src/Ocb.Backend/Http/CallerIdentityRoutes.cs
src/backend/.../adapters/http/openapi_v1/skills/router.py → dotnet/src/Ocb.Backend/Http/SkillsRoutes.cs
src/backend/.../adapters/http/openapi_v1/resources/router.py → dotnet/src/Ocb.Backend/Http/ResourcesRoutes.cs
src/backend/.../adapters/http/openapi_v1/engine_runtime/sessions/router.py → dotnet/src/Ocb.Backend/Http/SessionRoutes.cs
```
