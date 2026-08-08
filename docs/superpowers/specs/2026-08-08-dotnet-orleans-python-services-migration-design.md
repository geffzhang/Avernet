# .NET 10 + Orleans Python 服务迁移设计

## 状态

已于 2026-08-08 批准。

## 摘要

使用 .NET 10、ASP.NET Core 和 Orleans 重写 OCB 的全部 Python 服务，同时保留 frontend 和 Rust BCS。所有新代码都在 Avernet 内独立实现；相邻仓库 `openclaw.net/src` 仅作为设计和代码参考，不构成运行时、源码、项目或 NuGet 依赖。

本项目采用整体替换方案，不形成长期 Strangler 部署。工程实施仍按模块和依赖顺序分阶段推进，以便在最终整体切换前分别验证每个边界。

## 目标

- 替换 `backend`、`engine`、`baas`、`gateway` 和 `bcsfuse` 中的全部 Python 代码。
- 使用 ASP.NET Core 和 Kestrel 替换 FastAPI 和 Uvicorn。
- 使用 Orleans 处理具有身份、生命周期、状态和并发串行需求的协调逻辑。
- 保持现有 HTTP、WebSocket、SSE、Service API 和 Plugin API 契约；任何变更都必须经过独立的契约评审。
- 保持仓库现有微内核架构及其 CI 门禁。
- 生产切换时从全新系统开始，不导入用户数据或运行历史。
- 使用 PostgreSQL 存储 .NET 业务数据和 Orleans 基础设施数据。
- 使用 MinIO 存储文件和大对象。
- 单机部署使用 SonnetDB，集群部署使用 Qdrant；启动时二选一。
- 暂时保留使用 SQLite 的 Rust BCS，并为后续 Orleans 重写预留替换边界。

## 非目标

- 本项目不重写 Rust BCS。
- 不迁移历史 MySQL、SQLite、FAISS 或 Qdrant 数据。
- 不重写 TypeScript frontend。
- 不重写 OpenClaw、BCN 插件或 Node 版本的 Claude Code gateway。
- 不依赖 `OpenClaw.*` 程序集，也不引入 Canvas、Dashboard、Payment 等与 OCB 目标无关的 openclaw.net 功能。
- 目标平台不保留 MySQL。
- Rust BCS 使用 SQLite 的过渡阶段不支持水平扩展。

## 约束与权威来源

- `docs/arch/arch.rules.md` 继续作为强制架构宪法。
- `docs/arch/ci.enforce.md` 继续作为强制 CI 规则。
- `docs/arch/context-boundary-format.md` 继续定义模块上下文边界模型。
- `docs/arch/protocol-contract-tests.md` 继续定义 Plugin API 一致性测试模型。
- `docs/arch/service-skills-layout-wire-contract.md` 继续定义 Skills 布局线协议。
- 现有 OpenAPI、JSON Schema、WebSocket 帧、SSE 和 BCS 协议定义是兼容性判断依据。
- `openclaw.net/src` 是 .NET 代码参考，重点参考 Channels、TickerQ 调度、Gateway 组合、Plugin、Skill 和 Testing 模式。

## 可行性结论

迁移在技术上可行，但工程风险高。主要风险不是缺少 .NET 组件，而是需要在约 2,200 个 Python 文件和现有测试体系的范围内保证行为一致性。

合理的交付量级如下：

- 8 至 12 名熟悉 C#、分布式系统和现有业务的工程师：18 至 24 个月。
- 4 至 6 名同等经验的工程师：24 至 36 个月。

不应承诺在 6 至 12 个月内完成全部替换。

## 目标架构

### 运行拓扑

目标架构将交付、协调、进程执行和持久基础设施分离：

```text
Frontend / 外部客户端
          |
          | HTTP / WebSocket / SSE
          v
Ocb.Gateway（ASP.NET Core、Orleans client）
          |
          v
Ocb.Silo（Orleans Grain 与应用协调）
          |
          +--> Ocb.Runtime.Worker --> OpenClaw / Claude Code 进程
          +--> Rust BCS（版本化 HTTP/WebSocket 契约）
          +--> PostgreSQL
          +--> MinIO
          +--> SonnetDB 或 Qdrant
          +--> 按需启用的 Redis

Ocb.Scheduler（TickerQ）
          |
          +--> PostgreSQL lease/outbox --> Orleans / 应用消息管线
```

singlebox 可以将这些 Host 放入同一容器，但仍须保持进程和所有权边界。集群部署中，Gateway、Silo、Runtime Worker 和 Scheduler 分别部署和扩缩容。

### Solution 目录

```text
dotnet/
  Ocb.slnx
  Directory.Build.props
  Directory.Packages.props
  src/
    Ocb.Contracts/
    Ocb.Core/
    Ocb.PluginApi/
    Ocb.Channels/
    Ocb.Scheduling/
    Ocb.GrainContracts/
    Ocb.Grains/
    Ocb.Gateway/
    Ocb.Runtime.Worker/
    Ocb.Backend/
    Ocb.Baas/
    Ocb.Fusion/
    Ocb.Infrastructure.PostgreSql/
    Ocb.Infrastructure.Minio/
    Ocb.Infrastructure.Vector/
    Ocb.Bcs.Client/
    Ocb.Plugins.*/
    Ocb.Silo.Host/
    Ocb.Scheduler.Host/
  tests/
    Ocb.Architecture.Tests/
    Ocb.Contracts.Tests/
    Ocb.Core.Tests/
    Ocb.Grains.Tests/
    Ocb.Infrastructure.Tests/
    Ocb.EndToEnd.Tests/
```

全部项目目标框架为 `net10.0`，启用 nullable reference types 和 implicit usings，并将警告视为错误。依赖版本在 `Directory.Packages.props` 中集中锁定，仅允许使用已验证兼容 .NET 10 的稳定版本。

NativeAOT 不是首期验收条件。虽然 openclaw.net Gateway 使用 NativeAOT，但 Orleans 和新增基础设施 Provider 在通过专项兼容测试前不得继承 `PublishAot=true`。

## 架构边界

### Service API 与 Plugin API

- Service API 定义消费者调用 OCB 核心的能力。
- Plugin API 定义 OCB 核心调用基础设施 Provider 的能力。
- 两类契约使用不同项目、文档和一致性测试套件。
- Core 和 Contracts 不引用 ASP.NET Core、EF Core、Orleans 实现、MinIO、SonnetDB、Qdrant 或具体 Plugin。
- 具体实现只能在 Composition Root 中选择。

### Orleans 边界

Orleans 是应用协调机制，不取代领域模型和 Plugin 架构。

仅当对象具有稳定身份，且至少符合以下一种情况时使用 Grain：

- 具有可变持久状态；
- 需要串行处理并发命令；
- 具有激活和停用生命周期；
- 需要分布式协调。

典型 Grain 包括 `BotGrain`、`SessionGrain`、`DeviceGrain`、`FusionJobGrain` 和 `ConnectionDirectoryGrain`。

HTTP 转发、密码计算、向量查询、数据库访问、文件传输和进程句柄不能建模为 Grain。Grain 实现仅作为薄协调层，通过 Service API 和 Plugin API 完成工作。

### Context Boundary

每个影响架构边界的 C# 模块都必须提供与现有 Context Boundary 等价的机器可读元数据，包括职责、公开契约、消费契约、允许的内部依赖和变更影响。架构测试强制验证依赖图。

## Python 服务映射

| 现有服务 | 目标职责 |
| --- | --- |
| `gateway` | `Ocb.Gateway`：ASP.NET Core endpoint、鉴权、路由、限流、Schema Catalog 和遥测 |
| `engine` | `Ocb.Runtime.Worker`：运行时 API、OpenClaw/Claude Code 反腐层、进程和 workspace 生命周期 |
| `backend` | `Ocb.Backend` 服务及 Bot、Session、Skill activation、asset Grain |
| `baas` | `Ocb.Baas` 服务及 Device、Template Grain 和 sandbox/provider Plugin |
| `bcsfuse` | `Ocb.Fusion` 服务及 Worker、Fusion Job、Group Profile Grain |

BaaS 迁移范围包括 sandbox provisioning、Docker/Kubernetes Provider、透明 `invoke-http`、API gateway、QPM、SSE、Bot 发布、运行队列、Device TTL、billing control 和第三方集成边界，不能被缩减为 Device 和 Template CRUD。

## ASP.NET Core 替换方案

| Python 能力 | .NET 实现 |
| --- | --- |
| FastAPI Router | ASP.NET Core Endpoint Group 或 Controller；每个模块统一选择一种模式 |
| Uvicorn | Kestrel |
| Pydantic DTO | C# record 和 source-generated `System.Text.Json` context |
| Pydantic Validation | Endpoint Filter 和显式 Validator |
| Injector / dependency-injector | `Microsoft.Extensions.DependencyInjection` |
| FastAPI Middleware | ASP.NET Core Middleware |
| `HTTPException` | Domain Error，经 `IExceptionHandler` 映射 |
| FastAPI lifespan | Generic Host lifecycle 和 `IHostedService` |
| `httpx` / `aiohttp` | `IHttpClientFactory` 创建的 typed client |
| Python async queue | `System.Threading.Channels` |
| SQLAlchemy | EF Core + Npgsql |

Delivery Adapter 只负责解析鉴权和协议数据、调用 Service API，并将领域结果映射回既有线协议，不承载业务策略。

## openclaw.net 参考策略

迁移可以研究和改写 `openclaw.net/src` 中的实现模式，但 Avernet 必须拥有独立实现和命名空间。

重点参考以下区域：

- `OpenClaw.Core`：Abstractions、Models、Security 和 Pipeline；
- `OpenClaw.Channels`：`IChannelAdapter`、`WebSocketChannel`、`CronChannel`；
- `OpenClaw.Gateway`：endpoint validation、Composition 和 inbound worker；
- `OpenClaw.PluginKit`：Plugin 发现和生命周期；
- `OpenClaw.SkillKit`：Skill 模型和加载模式；
- `OpenClaw.Testing`：测试基础设施。

目标 Solution 禁止引用任何 `OpenClaw.*` ProjectReference 或 NuGet 包。改写后的代码必须使用 OCB 契约、命名、配置、错误语义、租户模型和测试。两个代码库共同使用的第三方组件仍可作为正常 NuGet 依赖。

## WebSocket 与 SSE

### WebSocket Channel

`Ocb.Channels.WebSocketChannel` 参考 openclaw.net 的原生 ASP.NET Core WebSocket Channel，不使用 SignalR。它必须支持：

- raw text 和 JSON envelope 两种模式；
- 跨 receive fragment 组装完整消息；
- 总连接数和单 IP 连接数限制；
- 单连接消息速率限制；
- 每连接串行发送；
- 流式响应 envelope；
- 关联认证身份；
- Origin 校验；
- 正确处理断线和并发发送清理。

外部 wire format 继续使用现有 OCB 格式。除非另行批准契约变更，否则不引入 openclaw.net 特有的 Canvas envelope。

### 集群路由

WebSocket 对象始终保留在接受连接的 Gateway 进程中，不能进入 Grain State。

`ConnectionDirectoryGrain` 记录 Gateway instance、connection id、tenant、user、session 和 lease expiry。Orleans Streams 将 outbound notification 路由到拥有连接的 Gateway，由本地 `WebSocketChannel` 发送。断线和 lease 过期会清理陈旧路由。

SSE 使用 ASP.NET Core streaming response，并遵守相同的鉴权、背压、取消和 correlation 规则。

## 调度与可靠任务

TickerQ 替换 APScheduler，也替换早期方案中的 Quartz.NET。首期实现对齐 openclaw.net 的职责拆分：

- TickerQ 触发周期性扫描函数；
- `CronScheduler` 负责解析任务、时区和重叠规则；
- 到期任务写入应用消息管线；
- 应用 Worker 或 Grain 执行实际任务。

集群不能让每个 Host 独立执行同一次扫描。`Ocb.Scheduler.Host` 在每轮调度前获取 PostgreSQL lease。每个投递任务携带由 schedule identity 和 occurrence time 组成的幂等键。

不同时间语义分别使用：

- TickerQ：用户和系统 Cron；
- Orleans Reminders：持久化 Grain 生命周期提醒；
- Orleans Timers：activation 内的短周期计时；
- PostgreSQL outbox 和 worker：可靠异步命令、重试及 dead letter。

## Runtime Worker 与 OpenClaw 集成

`Ocb.Runtime.Worker` 持有不能进入 Grain State 的进程和文件系统资源：

- OpenClaw 和 Claude Code gateway 进程；
- workspace 创建和清理；
- 端口分配；
- 进程健康检查、重启、取消和关闭；
- stdout/stderr 日志收集；
- Engine WebSocket/HTTP 反腐适配器；
- 已激活 Skill 的物化。

`BotRuntimeGrain` 协调 desired state 并分配 Runtime Worker。Worker 回报 observed state，并使用 lease 使其他 Worker 能接管失联任务。Grain State 只保存标识和期望/观察状态，不能把进程句柄或本地路径作为权威业务状态。

## 租户、身份与安全

- tenant-scoped Grain key 使用 `(tenant_id, entity_id)`。
- tenant identity 通过可序列化 `CallerContext` 显式传递，不能依赖 ambient process state。
- Gateway 按既有契约验证 JWT 和 signed principal，包括需要使用的 `X-Avernet-Principal`。
- Orleans call filter 在 Grain 边界再次校验 caller 和 tenant 一致性。
- PostgreSQL 查询包含显式 tenant predicate，并尽可能增加数据库约束。
- API key、模型凭据、MinIO 凭据、BCS secret 和 SM4 key 通过 Secret Plugin 解析，不能进入 Grain State。
- 日志、trace、metric、异常和健康检查不能泄露 secret 或消息敏感正文。
- CI 必须进行依赖、容器和 secret 扫描。

## 数据与持久化

### PostgreSQL

PostgreSQL 替换全部迁移服务的 MySQL。新系统不保留旧 MySQL Schema。

使用独立 Schema 和数据库角色划分所有权：

- `ocb_business`：EF Core 业务实体；
- `ocb_orleans`：membership、reminder 和 Grain persistence；
- `ocb_jobs`：outbox、job lease、retry 和 dead letter。

EF Core migration 与 Orleans 基础表 migration 是不同的发布步骤。启动时验证 Schema 兼容性，但不在生产环境静默修改 Schema。

### Rust BCS SQLite

Rust BCS 保持单实例有状态服务，私有 SQLite 文件启用 WAL、busy timeout、persistent volume 和周期备份。任何 .NET 组件都不能直接访问其数据库。

所有协调访问都经过 `Ocb.Bcs.Client` 和现有版本化 HTTP/WebSocket 契约。客户端实现 `IBcsClient`，使后续 Orleans 实现可以替换 Rust BCS 而不改变消费者。未来替换时不迁移 BCS SQLite 数据，除非新的独立规格另有要求。

### MinIO

MinIO 存储文件、Skill package 和大对象，PostgreSQL 存储对象元数据和所有权。

对象流程必须支持 multipart upload、checksum、大小限制、tenant key prefix、临时对象发布、signed URL 有效期、range read、retention、删除补偿和可选的 malware scanning Plugin API。元数据事务失败后，只能遗留可被后台清理的临时对象。

### Redis

Redis 是可选组件，只能用于 cache、分布式 rate counter 或其他明确的非权威加速用途。系统正确性和持久状态不能依赖 Redis。

## 向量存储

`Ocb.Fusion` 依赖抽象契约而不是具体向量引擎：

- `IVectorStore`：upsert、delete、get、Top-K、distance 和 metadata filter；
- `IHybridSearchStore`：文本与向量混合搜索能力；
- `IVectorStoreAdministration`：collection/index 创建和健康检查。

实现包括：

- `SonnetDbVectorStore`：singlebox 和边缘部署；
- `QdrantVectorStore`：集群部署。

启动时只能选择一个 Provider，不进行长期双写。cluster profile 配置 SonnetDB 时必须拒绝启动。两个 Provider 运行同一套 conformance suite，覆盖 dimension、distance、payload type、filter、同分排序、Top-K 限制、hybrid capability 声明和错误语义。

Embedding 与 reranker 是独立 Plugin API。向量记录携带 model 和 dimension 元数据；不兼容的 embedding 变更必须 fail closed，不能污染既有索引。

## SM4 与密码能力

目标 Crypto Plugin 在行为符合契约时使用 BugFree.Security 实现 BaaS 的 SM4 能力，并允许使用 BouncyCastle.Cryptography 作为底层实现和兼容回退。依赖版本应尽可能与已验证的 openclaw.net 基线对齐。

由于生产环境不保留历史密文，新实现无需解密旧 gmssl 记录。known-answer、round-trip、invalid-key、padding 和 tamper 测试定义新契约。SM2 不在当前范围。

## Skills 管理与交付

保留现有三种来源语义：

- `git://`：Repository 管理的公共内容；
- `local://`：用户上传内容；
- `center://`：受治理的 Skill Center 内容。

PostgreSQL 存储 metadata、publication、activation 和 immutable manifest。MinIO 存储版本化 Skill 内容和 package。Runtime Worker 只把 Bot 已激活的 Skill 物化到对应 workspace。

Service Skills manifest 保持 engine-agnostic，并继续支持 `skills-pool-p3-v1`。Backend 输出既有布局变量，Runtime image 根据 manifest 映射物理路径。任何 Bot 都不能获得指向完整 Skills 内容仓库的 bridge 或 mount。

Runtime activation 和 recovery 流程如下：

1. 解析不可变 Skill 版本；
2. 下载并校验内容；
3. 物化到临时 workspace；
4. 原子发布 active view；
5. 回报 observed activation state；
6. Runtime Worker 重启或重新分配后重新 reconcile。

失败不能用不完整视图替换既有 active workspace。瞬时下载错误使用 exponential backoff 和 jitter 重试三次；integrity failure 不重试。解析、校验或物化失败时删除临时内容、保留既有 active view，并记录 desired/observed failure state。原子发布失败时将 active pointer 回滚到上一个视图。没有旧视图时，Bot activation 必须 fail closed 并返回可操作错误。Runtime Worker 重启或重新分配后，从权威 desired state 重新执行完整 reconcile。

openclaw.net SkillKit 只作为模型和加载方式参考，OCB 的发布、来源治理和布局契约仍是权威。

## 配置与 Profile

使用 strongly typed options 和启动校验。未知 key、缺失必填值、无效 Provider 与 Profile 组合都必须导致启动失败。只有配置加载、Composition Root 和测试可以读取原始环境变量。

必须支持以下 Profile：

| Profile | 拓扑 |
| --- | --- |
| `singlebox` | 单 Gateway/Silo/Worker、一个 TickerQ Scheduler Host、PostgreSQL、MinIO、SonnetDB、使用 SQLite 的 Rust BCS |
| `cluster` | 多 Gateway/Silo/Worker、一个持有 PostgreSQL lease 的 active TickerQ Scheduler Host、PostgreSQL、MinIO、Qdrant、使用 SQLite 的 Rust BCS |
| `test` | Orleans TestCluster 加 InMemory Plugin 或 Testcontainers |

开始提供流量前必须验证 Provider capability。禁止硬编码生产 URL、token 和私有 endpoint。

## 部署与打包

singlebox 提供单一用户入口容器，但内部仍保持进程边界。镜像包含 .NET runtime、Rust BCS binary、Node/OpenClaw runtime、BCN plugin 和已构建 frontend。PostgreSQL、MinIO、SonnetDB 作为显式本地依赖运行，不能成为隐藏的嵌入状态。

集群部署为 Gateway、Silo、Runtime Worker、Scheduler 和 BCS 提供独立镜像。Qdrant、PostgreSQL、MinIO 和可选 Redis 是外部服务。BCS 在持有 SQLite 期间保持单副本。

构建支持 x64 和 arm64、公共官方 package source 以及现有中国镜像开关。Community artifact 不能包含企业内部包、endpoint、凭据或源码依赖。

## 可观测性

OpenTelemetry 覆盖 ASP.NET Core、outbound HTTP、Orleans call、PostgreSQL、Runtime Worker operation、MinIO 和 vector client。在策略允许时，correlation 字段包含 tenant、Bot、session、request、task 和 connection id。

必须提供以下运行指标：

- HTTP/WebSocket latency 和 error rate；
- 当前及拒绝的 WebSocket connection；
- Orleans activation、call 和 reminder 健康状态；
- scheduler lag、duplicate suppression、queue depth 和 dead letter；
- Runtime Worker assignment、process restart 和 orphan recovery；
- PostgreSQL pool 和 migration 状态；
- MinIO transfer 和 cleanup failure；
- vector indexing/query latency 和 Provider health。

## 测试与 CI

### 测试层级

- 不启动 ASP.NET Core 或 Orleans Host 的 xUnit 领域测试；
- 使用 Orleans TestCluster 验证 activation、concurrency、persistence、Streams、Reminders 和 call filter；
- 使用 `WebApplicationFactory` 验证 HTTP、WebSocket、SSE、auth、错误和 JSON；
- 使用 Testcontainers 验证 PostgreSQL、MinIO、SonnetDB、Qdrant、Redis 和 BCS；
- Service API 与 Plugin API conformance suite；
- vector Provider conformance suite；
- Docker Compose 端到端用户故事；
- NBomber 性能和长连接测试；
- 进程丢失、Silo 丢失、网络超时、重复投递和存储恢复故障测试。

### 强制门禁

- format、compile、nullable analysis 和 analyzer；
- 架构依赖测试；
- 禁止 Core 引用框架和禁止越界读取环境变量；
- 严格配置 Schema 测试；
- protocol 和 Provider conformance test；
- Public endpoint coverage；
- Contract 和 Grain State 的 source-generated serialization 兼容测试；
- changed-line coverage 不低于被替换模块的现有门禁；
- 保持现有 Rust BCS 和 frontend CI，不得削弱；
- Security 和 license scanning。

切换前，contract parity test 使用同一 request/frame corpus 同时验证 Python 基线和 .NET 实现，并比较状态码、契约要求的 Header、JSON shape/value、消息顺序和失败行为。

## 交付计划

整体替换按依赖顺序实施：

1. 建立 .NET Solution、契约、架构测试和基础设施 Profile；
2. 建立协议 corpus 和 Python/.NET parity harness；
3. 实现 Gateway 和 Channel 交付面；
4. 实现 Runtime Worker 和 Engine 反腐层；
5. 实现 BaaS 能力和 Provider；
6. 实现 Fusion 服务及 SonnetDB/Qdrant conformance；
7. 实现 Backend 领域、租户、asset 和 Skills；
8. 完成集成、性能、恢复和安全验证；
9. 在维护窗口整体切换并观察；
10. 验收后删除 Python。

模块开发可以并行，但生产环境不进入长期 Python/.NET 混合拓扑。最终整体替换通过验收之前，Python 继续作为行为基线。

## 切换与回退

生产切换从全新系统开始：

1. 冻结写入并进入维护窗口；
2. 应用 PostgreSQL 业务和 Orleans migration；
3. 创建 MinIO bucket 和 lifecycle rule；
4. 初始化所选 vector Provider 和 index；
5. 初始化全新 BCS SQLite 状态；
6. 仅加载必要系统 seed 和配置；
7. 启动 Scheduler、Silo、Runtime Worker、Gateway、BCS 和 frontend；
8. 执行 readiness 和关键用户故事；
9. 开放流量。

不导入历史数据，也不进行反向同步。开放流量前可以回滚到 Python 部署。新系统开始接受持久写入后，回滚到 Python 会丢失数据，因此不能作为自动回退方案。正式 go-live 决策即代表切换不可逆；此后的恢复依赖新平台备份和修复。

## 验收标准

- 目标服务不再导入或依赖被迁移的 Python package。
- 现有 HTTP、WebSocket 和 SSE 契约全部通过 parity test，或具备已批准的版本化变更。
- 所有 Service API 和 Plugin API 实现通过 conformance test。
- singlebox 使用 SonnetDB 通过验收；cluster 使用 Qdrant 通过验收。
- PostgreSQL、MinIO、vector Provider 和 BCS SQLite 备份恢复演练通过。
- 多 Silo 重启、Grain reactivation、Streams、Reminders、scheduler lease 和 Runtime Worker reassignment 测试通过。
- frontend 到 backend、.NET 到 Rust BCS 的关键用户故事通过。
- 峰值负载下 P95、P99 和吞吐满足批准的 Python 基线；资源回归具有明确的接受记录。
- 安全评审确认租户隔离、secret handling、authorization、upload control 和 dependency posture。
- singlebox、Docker、部署、API 和贡献者文档完成更新。

## 主要风险与缓解措施

| 风险 | 缓解措施 |
| --- | --- |
| Python 隐式行为和测试规模 | 协议 corpus、契约优先、按模块验证 |
| Orleans 被过度使用 | Grain 薄协调规则和架构测试 |
| 集群重复调度 | 独立 TickerQ Host、PostgreSQL lease、occurrence idempotency |
| 多副本 WebSocket 所有权 | 本地 Socket、Connection Directory Grain、Orleans Streams |
| Runtime 进程丢失 | Worker lease、observed state、reassignment 和 workspace reconcile |
| Bot 之间 Skills 泄露 | immutable manifest、tenant key、按 Bot 物化、禁止完整仓库 mount |
| Vector Provider 语义漂移 | 统一 conformance suite 和 capability validation |
| BCS SQLite 可用性 | 明确过渡单实例、WAL、persistent volume 和备份 |
| 第三方库成熟度 | Plugin 包装、版本锁定、契约测试和可替换实现 |
| 写入后无法无损回滚 | 维护窗口、明确 go-live gate、新平台恢复方案 |
| 双栈维护周期过长 | 对已迁移表面实施 feature freeze，并追踪 parity propagation |

## 后续 BCS 替换

后续独立项目使用 Orleans Grain 替换 `IBcsClient` 的 Rust 实现，覆盖协调、路由、群组、消息及相关状态。本设计禁止消费者依赖 BCS SQLite、进程布局或 Rust 实现细节，使后续替换成为 Composition Root 和 contract conformance 变更，而不是再次进行应用级全面重写。
