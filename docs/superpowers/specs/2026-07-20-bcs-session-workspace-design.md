# BCS 会话工作区 — 共享文件框架（设计）

**日期：** 2026-07-20
**状态：** 评审稿
**作者：** zhangwu.zh
**范围：** v1 — trait + 本地文件系统后端 + baas 后端；分片/大文件支持延后

## 目标

为每个 BCS 会话提供共享文件区（"session workspace"），会话中的 bot 和 human 都可以
在其中上传、下载、列出和删除文件。BCS 拥有该框架，对外提供接口（HTTP + CLI），
对内提供可插拔的存储接口，使不同的文件存储后端（本地文件系统 / baas / OSS / NAS /
三方服务）只需实现一个 trait 即可接入。

## 参考资料

- BCS 架构与分层：`src/bcs/CLAUDE.md`、`src/bcs/AGENTS.md`
- 需对标的现有插件模式：`bcs-db-api`（`DbPlugin`）、`bcs-cache-api`（`CachePlugin`）
- baas「第四通道」设计（内部背景参考，外部贡献者可忽略；核心 baas 接口映射已内嵌于本文档
  与 API 文档 §3.2，无需访问内部 wiki 即可理解与实现）：
  https://yuque.antfin.com/securitytec/otbct4/xqtzt0qamaruiofc
- baas 文件上传/下载 API 参考（同上，内部背景参考）：
  https://yuque.antfin.com/securitytec/otbct4/gp53l55mme92fbke

## 非目标（v1）

- 超过单片 ~100 MB 的分片/切片上传（延后）
- OSS 与 NAS 后端（trait 已支持，v1 不实现）
- 文档约定的孤儿/TTL 清理钩子以外的内置清理器
- 目录/层级语义（key 是扁平的，list 仅按前缀过滤）

## 关键决策（brainstorming 阶段已锁定）

| 决策项 | 选择 |
|---|---|
| 字节流路径 | **上传一律 BCS 三阶段代理。** 客户端始终 `POST /files`(prepare)→PUT 到 BCS 给的 `upload_url`→`POST /complete`，上传链接统一由 BCS 提供、字节经 BCS；具体后端实现（本地落盘 / baas 流式转发到 OSS 直传 URL）由 `StoragePlugin` 决定，对客户端不可见。下载侧保留能力差异：预签名后端 302 直连签名 URL（字节不经 BCS），本地后端流式返回。 |
| 删除权限 | **上传者 + 会话创建者/驱动 bot。** 文件上传者（human 上传者、上传该文件的 bot、或拥有该 bot 的 human），或会话创建者 / 该 group 的 driver bot 可删除。其余通过会话成员校验的人可上传/下载/列出。 |
| v1 后端 | **trait + 本地文件系统 + baas。** OSS 延后（后续通过 trait 即可接入）。 |
| 文件生命周期 | **仅在会话删除时自动清理。** 会话完成不删除文件。v1 单片上限 ~100 MB；分片延后。 |

## 架构与 crate 布局

新增代码完全遵循现有 `DbPlugin` / `CachePlugin` 模式（trait crate 在
`crates/plugin-api/` 下，后端 crate 在 `crates/plugins/` 下）。**以下路径均相对 `src/bcs/` 目录**
（仓库实际 layout 为 `src/bcs/crates/...`），不要建到仓库根目录：

```
crates/contracts/bcs-domain/src/session_file.rs        # SessionFile 领域类型
crates/contracts/bcs-protocol/src/session_file.rs      # wire DTO（HTTP 请求/响应）

crates/plugin-api/bcs-storage-api/                     # StoragePlugin trait + 错误 + 类型 + 契约测试
crates/plugins/bcs-storage-local/                      # 本地文件系统后端（开发 / 单节点）
crates/plugins/bcs-storage-baas/                       # baas 后端（留存模式）

crates/service-api/src/application/session_files.rs    # SessionFileService 用例
crates/service-api/src/port/repo/session_file.rs       # SessionFileRepo 端口（元数据）

crates/services/bcs-session-file/                      # SessionFileService 核心实现
crates/services/bcs-session-file-store/                # SessionFileRepo 的 MySQL + memory 实现
crates/services/bcs-session/ (扩展)                    # 删除会话时的清理钩子

crates/adapters/http/bcs-http/src/routes/session_files.rs   # HTTP handler
crates/tools/bcs-cli/src/main.rs + client.rs           # CLI 子命令

migrations/mysql/006_session_files.sql                 # bcs_session_files 表
```

分层与 `CLAUDE.md` 一致：`contracts` -> `service-api`(application/core/port)
-> `services`(+`*-store`) -> `adapters`，插件位于 `core` 之下。
`SessionFileService` 持有 `Arc<dyn SessionFileRepo>`、`Arc<dyn StoragePlugin>`，
以及对现有 session 查找 service 的引用。组装根（`crates/bootstrap/bcs/server.rs`）按
配置装配选定的存储插件。

## 领域与数据模型

### `SessionFile`（领域类型，`bcs-domain`）

```rust
pub struct SessionFile {
    pub file_id: String,          // ULID / "{sid}:{8hex}" 风格
    pub session_id: String,
    pub file_name: String,
    pub mime_type: String,
    pub size: u64,
    pub sha256: Option<String>,
    pub owner: ActorRef,          // { actor_kind: Bot|Human, actor_id }
    pub storage_backend: String,  // "local" | "baas" | ...
    pub object_handle: String,    // backend-specific: local path | baas transfer_id / OSS key
    pub status: FileStatus,       // Pending | Ready | Deleting | Failed
    pub created_at: u64,
    pub updated_at: u64,
}
```

`ActorRef` 复用 `bcs-domain::group` 中已有的 `ActorKind::{Bot, Human}` + `actor_id`。

### `bcs_session_files` 表（迁移 `006`）

字段：`env`、`file_id`（PK）、`session_id`、`owner_actor_kind`、`owner_actor_id`、
`file_name`、`mime_type`、`size BIGINT`、`sha256 CHAR(64)`、`storage_backend`、
`object_handle`、`status`、`created_at`、`gmt_create`、`gmt_modified`。
索引：`(env, session_id)`，唯一索引 `(env, session_id, file_id)`。
遵循现有 `env` + `gmt_create/gmt_modified` + utf8mb4 约定。

**BCS 始终以本表为文件列表的权威来源** —— 永不从后端拉取。这样无论后端如何
（baas 列表是按 bot 命名空间、扁平、异步的），list/metadata 都保持快速、统一、可过滤。

## 对内可插拔接口：`StoragePlugin`

详细 API 见配套文档 `2026-07-20-bcs-session-workspace-api.md`。上传侧为统一的三阶段
trait（与 BCS 三阶段 HTTP 流程一一对应），下载侧保留 `get_stream` / `presign_get`：

```rust
#[async_trait]
pub trait StoragePlugin: Send + Sync + 'static {
    fn backend_name(&self) -> &'static str;
    fn capabilities(&self) -> StorageCapabilities;   // { supports_presign_download, ... }

    // --- 上传：BCS 三阶段，plugin 决定具体实现 -----------------------------
    async fn prepare_upload(&self, req: UploadPrepareRequest) -> Result<UploadHandle, StorageError>;
    // part_number: v1 单片恒 None；v2 分段传对应编号，现以 Option 固定避免破坏性改 trait
    async fn stream_upload(&self, handle: &UploadHandle, part_number: Option<u16>, body: ByteStream) -> Result<(), StorageError>;
    async fn complete_upload(&self, handle: &UploadHandle) -> Result<StorageObjectMeta, StorageError>;
    async fn abort_upload(&self, handle: &UploadHandle) -> Result<(), StorageError>;

    // --- 下载 ---------------------------------------------------------------
    async fn get_stream(&self, handle: &StorageHandle) -> Result<ByteStream, StorageError>;
    async fn presign_get(&self, handle: &StorageHandle, ttl_secs: u64) -> Result<PresignGetTicket, StorageError>;
    async fn delete(&self, handle: &StorageHandle) -> Result<(), StorageError>;

    async fn health_check(&self) -> Result<StorageHealth, StorageError>;
}
```

`UploadHandle` 是后端特定的、可序列化句柄（本地：临时路径 + 终态 key；baas：
`transfer_id` + 签发的 OSS 直传 URL + key），由 BCS 在 `prepare_upload` 后持久化到
`SessionFile.object_handle` 列，供跨 HTTP 请求的 `stream_upload`/`complete_upload`/
`abort_upload`/`delete` 重建使用。`StorageError { InvalidInput, NotFound, Conflict,
Unsupported, Backend }` 对标 `DbError`；lint 禁止泄漏后端细节。

`capabilities().supports_presign_download` 仅影响下载：为 true 时 BCS 对
`GET .../content` 返回 302 跳转到 `presign_get` 签名 URL（字节不经 BCS）；为 false 时
BCS 用 `get_stream` 流式返回 body。上传侧不再有客户端可见的预签名能力。

- 本地：`supports_presign_download = false`；`max_object_size` 取**配置项**（不以动态磁盘剩余空间
  作为静态 capability，剩余空间在 `stream_upload` 中实际校验）；`prepare_upload` 开临时文件，
  `stream_upload` 写入，`complete_upload` fsync + 原子改名为终态 key，`abort_upload` 删临时文件。
- baas：`supports_presign_download = true`；`prepare_upload` 调 baas `POST /upload-url`（留存
  模式）拿到 `transfer_id` + OSS 直传 URL；`stream_upload` 把客户端字节**流式转发**到该 OSS 直传
  URL（BCS 只做转发，不全量落盘）；`complete_upload` 调 baas `complete` + 轮询到 `DONE`；
  `abort_upload` 调 baas `DELETE /upload-url/{id}`；`health_check` 仅探测 baas base_url 可达性，不依赖真实 transfer_id。
- OSS（v2+）：`supports_presign_download = true`；`prepare_upload` 发 OSS 直传 URL（单片），
  大文件 v2 走 multipart；其余同构。

## 对外 HTTP API

在 `router.rs` 中注册的会话级路由，复用 `State<HttpAppState>`、
`resolve_group_chat_caller` 和现有会话成员校验。完整请求/响应契约见配套 API 文档。

**上传一律走 BCS 三阶段、上传链接统一由 BCS 提供**，客户端不需要知道后端是什么：

1. `POST /sessions/{sid}/files`（JSON `{file_name, size, mime_type}`）→ 创建 `Pending` 的
   `SessionFile` 行，返回 BCS 自有的 `upload_url`（指向 BCS 的 `PUT .../content`）+ `file_id`。
2. 客户端 PUT 字节到该 `upload_url`（经 BCS）。BCS 流式接收并交给 `StoragePlugin` 的
   `stream_upload` —— 本地直接落盘；baas 由 BCS 转发到 baas 签发的 OSS 直传 URL（流式转发，
   不在 BCS 全量落盘）。
3. `POST /sessions/{sid}/files/{file_id}/complete` 完成（本地 finalize；baas 调 complete + 轮询到
   `DONE`）。任意时刻可用 `DELETE /sessions/{sid}/files/{file_id}` 取消（`Pending` 时为 abort，
   `Ready` 时为删除对象 + 行）。

这样对外上传语义只有一种形态，三阶段 + 一个取消端点；`direct`/`proxy` 之分对客户端不可见。
下游 `StoragePlugin` 也以统一的三阶段 trait（`prepare_upload`/`stream_upload`/
`complete_upload`/`abort_upload`）承载，不再有面向客户端的 `presign_put`。
下载侧保持原设计（预签名后端 302 直连签名 URL / 本地流式返回 body）。

| 方法 | 路径 | 用途 |
|---|---|---|
| `POST` | `/sessions/{sid}/files` | **发起上传**（JSON），返回 BCS 自有的 `upload_url` + `file_id`，行置 `Pending`。 |
| `PUT`  | `/sessions/{sid}/files/{file_id}/content` | **上传字节**（经 BCS）。与下载 `GET .../content` 同路径不同方法。 |
| `POST` | `/sessions/{sid}/files/{file_id}/complete` | **完成上传**（后端 finalize / complete + 轮询）。 |
| `GET`  | `/sessions/{sid}/files` | **列文件**（分页：prefix、limit、marker）。 |
| `GET`  | `/sessions/{sid}/files/capabilities` | **后端能力**（`{storage, presign_download, max_size}`），可选，供客户端预判下载是否直连。 |
| `GET`  | `/sessions/{sid}/files/{file_id}` | **文件元数据**（`SessionFile`）。 |
| `GET`  | `/sessions/{sid}/files/{file_id}/content` | **下载字节** —— 预签名后端 302 跳转到签名 URL；本地后端流式返回 body。 |
| `DELETE` | `/sessions/{sid}/files/{file_id}` | **取消上传**（`Pending`）或 **删除文件**（`Ready`）。 |

v1 强制 ~100 MB 单片上限（`size ≥` 分段阈值时直接 `413`，prepare 仅返回 `mode: "single"`）；
分段上传（`mode: "multipart"`，prepare 一次返回所有分片 `upload_url`，由 `complete_upload`
在后端组装）为 v2+，响应形态已约定见配套 API 文档 §1.2.b。

## 对外 CLI API

扩展 `SessionCommands`（clap-derive）。完整参数见配套 API 文档。**上传只暴露 `upload` 一个
子命令**，内部自动串联三阶段（`POST /files` prepare → PUT 字节 → `POST /complete`），对用户
保持单入口、单参数（只需本地文件路径）；需要分阶段控制的脚本可直接走 HTTP 三阶段接口。

```
bcs session file upload       --session <sid> --path <local> [--mime] [--name]   # 三阶段，唯一上传入口
bcs session file list         --session <sid> [--prefix] [--limit] [--marker]
bcs session file download     --session <sid> --file-id <> [--out <path>]
bcs session file delete       --session <sid> --file-id <>                        # 删除 / 取消
bcs session file capabilities --session <sid>
```

`BcsClient` 新增三阶段上传的 `prepare`/`put-stream`/`complete` 助手方法（仅 `upload` 子命令内部
使用），字节流式 PUT。默认 JSON 输出；`--no-json` 为人读，对齐现有 CLI 约定。

## 鉴权与权限

- **Caller 解析：** 每个 route handler 复用 `resolve_group_chat_caller`
  -> `GroupChatCaller::Bot{bot_uuid}` 或 `GroupChatCaller::Human{...}`
  -> 构造 `ActorRef`。
- **成员校验：** human 的 `actor_id` 是会话参与者，或拥有作为参与者的 bot，即通过
  （复用 `human_has_group_access` 风格逻辑）。bot 通过自身 token + 会话参与者身份通过。
  upload/download/list 均需成员校验。
- **删除：** `owner == caller`（human 上传者；若上传者是 bot 则需拥有该 bot），或 caller
  是会话创建者 / 拥有该 group 的 driver bot。对标现有 `delete_session` 规则。
- 不在 BCS 现有能力之外新增 RBAC 层。

## baas 适配器映射（`bcs-storage-baas`）

baas 是 bot 中心化的异步 ticket 模型，适配器将其封装隐藏。BCS 上传全部走 baas
**留存模式（retention mode）**（不带 `device_path`；文件仅存 OSS 供共享 —— 即 baas API
文档中的「留存模式」）。

| BCS StoragePlugin 调用 | baas HTTP | 说明 |
|---|---|---|
| `capabilities()` | — | `supports_presign_download = true` |
| `prepare_upload` | `POST /upload-url`（不带 `device_path`，带 `filename`、`file_size`、`expire_seconds`） | 返回 OSS 直传 `upload_url` + `transfer_id`；`UploadHandle` 含 `transfer_id`、该 OSS 直传 URL、key，持久化为 `object_handle`。v1 仅 SINGLE 分片（≤100 MB）。 |
| `stream_upload` | 把客户端字节**流式转发 PUT** 到 baas 签发的 OSS 直传 URL | BCS 只做转发，不在本地全量落盘。 |
| `complete_upload` | `POST /upload-url/{id}/complete` 后轮询 `GET /transfers/{id}` 直到 `DONE` | 留存模式直接跳到 DONE（无 pull）。返回 `StorageObjectMeta`。 |
| `abort_upload` | `DELETE /upload-url/{transfer_id}` | 终态 `CANCELLED`。 |
| `presign_get` | `POST /transfers/{id}/share-link` | 返回预签名 `share_url`，用于 BCS `GET .../content` 的 302 跳转。 |
| `get_stream`（回退） | `share-link` -> `GET share_url` -> 流式返回 | 一般不用；`supports_presign_download = true` 时下载走 302。 |
| `delete`（`Ready` 文件） | `DELETE /staging?key={oss key}` | 仅对终态 ticket 有效；BCS 保证调用前文件 `Ready`（`DONE`）。baas `404 OSS_OBJECT_NOT_FOUND` 映射为 `Ok`（幂等）。 |

用哪个 bot 身份？会话中执行上传的 bot。BCS 将 baas `tenant`/`bot_uuid` 凭证作为
存储插件配置存储（默认使用一个配置好的 baas service bot；按会话分配是后续选项）。
baas 不施加 BCS 会话语义 —— BCS 才是会话维度的权威。`GET /staging` 列表不使用；
BCS 列表来自自身 DB。

BCS 对 baas 的上传是**纯转发**：客户端 PUT 到 BCS，BCS 把字节流式中继到 baas 签发的 OSS
直传 URL，自身不持有完整文件，也不参与 baas 的 ticket 状态机细节（这些封装在
`prepare_upload`/`complete_upload`/`abort_upload` 内）。

适配器同时落实了 baas 的流量隔离原则：文件字节在客户端与 OSS 之间通过预签名 URL
直接流转，不经过 BCS 或 baas 服务实例。

## 生命周期与清理

- **上传：** `prepare_upload` 创建 `Pending` 行（`object_handle` = `UploadHandle` 序列化）；
  `stream_upload` 接收字节；`complete_upload` 后置 `Ready`（`object_handle` 转 `StorageHandle`）。
- **`expires_at` / Pending 超时：** prepare 返回的 `expires_at` = BCS 给出的上传链接/句柄过期时间
  （取 `ttl_secs` 配置与后端签发的 URL 过期时间的**更早者**）。客户端须在该时间前完成 PUT+complete；
  **超时未 `complete` 的 `Pending` 文件由后台 sweep 转为 `Failed` 并调用 `abort_upload` 清理后端**。
  转为 `Failed` 后客户端若再 `complete` 收到 `INVALID_TRANSITION`（409）。该 sweep 为非 v1 阻塞项，
  v1 可先由 sweep 兜底，前端按 `Failed` 可 `DELETE` 清理重传。
- **删除/取消：** `Pending` 与 `Failed` 都走 `abort_upload`、`Ready` 走 `delete`（`Failed` 本质尚未
  完成上传，按 `Pending` 处理，清理后端 staging/临时段再删行，**不走** `delete`/`DELETE /staging`
  以免 baas 找不到 staging 对象）。统一先删后端对象（baas：`DELETE /staging` / `DELETE /upload-url/{id}`），
  再删元数据行。先后端再行（已删行对应的孤儿对象比「幽灵行」更安全，由 sweep 对账两者）。HTTP
  `DELETE` 端点**完全幂等**：DB 行已不存在时仍对后端做一次幂等探测后返 204（不返 404），与
  `StoragePlugin::delete` 的 Idempotent 一致。
- **删除会话钩子：** `delete_session` 执行时调用
  `SessionFileService::delete_all_for_session(sid)` -> 遍历文件 ->
  逐个 `StoragePlugin::delete` -> 删行。会话**完成**不触发此钩子；完成会话的文件仍可下载。
  v1 先同步逐个删除保正确；后续若会话文件量大可改为「先 mark `Deleting`，后台批量 + 异步 sweep」，
  与 `Deleting` 作为内部瞬态的定义一致（非 v1 阻塞）。
- **孤儿对账：** 一个解耦的定时器（按 baas 文档 TTL 原则；非 v1 阻塞项）清理后端孤儿对象
  与 `Pending` 超时的元数据行（转 `Failed` + `abort_upload`）。

## 测试

- **契约测试 + in-memory fake**（`bcs-storage-api`，对标 `bcs-test-support`）：除每个
  `StoragePlugin` 实现跑同一套契约用例外，crate 内**同步提供一个 `FakeStoragePlugin`**（in-memory
  实现，覆盖三阶段上传 / 下载 / 删除 / 幂等 / 分段路径）。现有 `DbPlugin`/`CachePlugin` 都通过
  fake 注入测试，`StoragePlugin` 同此模式 —— 各层（service 单测、HTTP 层）复用同一 fake，避免
  重复手写 mock、避免遗漏 multipart 路径。本地文件系统和 baas 须通过同一契约测试（baas 通过
  stub/fake server 或 integration tag 测试）。
- **单元测试**（`SessionFileService`）：能力路由、鉴权决策（上传者 vs 创建者）、生命周期钩子。
- **memory store**：对标现有 `*-store/src/memory.rs` 实现 `SessionFileRepo::memory`，
  用于无 MySQL 的快速 HTTP 层测试。
- **HTTP 层测试：** 三阶段上传（prepare/stream/complete）、`abort`/删除分流（含 `Failed`→abort
  分支）、list 分页、预签名下载 302 跳转流、delete 403/204 + 幂等 204。

## 配置

新增到 bootstrap 配置（`config.rs`），对标现有插件配置块。**`max_size`（=`min(BCS max_file_size,
后端 max_object_size)`）在 bootstrap 阶段计算并注入 `SessionFileService`，运行时不再动态调用
`capabilities().max_object_size`**（baas `capabilities()` 会 IO，不可每请求调用）：

```toml
[session_files]
storage_backend = "baas"        # "local" | "baas"
max_file_size = 104857600       # 100 MB v1；max_size = min(max_file_size, 后端 max_object_size)
data_dir = "/var/bcs/session-files"   # local only

[session_files.baas]
base_url = "http://{baas-host}:8890/api/v1/bots/{tenant}/{bot_uuid}/files"
tenant = "<tenant>"             # 必填
bot_uuid = "<service-bot-uuid>" # 必填，v1 用一个配置好的 baas service bot
health_probe_path = "/"         # health_check 探测路径（相对 base_url），不依赖真实 transfer_id
# 其余可选：鉴权凭证 / 超时 / complete 轮询间隔 / share-link 默认 ttl
```