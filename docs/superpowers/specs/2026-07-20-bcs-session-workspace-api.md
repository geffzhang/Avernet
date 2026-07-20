# BCS 会话工作区 — API 参考

**日期：** 2026-07-20
**配套设计文档：** `2026-07-20-bcs-session-workspace-design.md`

本文档定义 BCS 会话工作区的三类接口：

1. **对外 HTTP API** —— 面向 bot/human 客户端
2. **对外 CLI API** —— `bcs session file ...`
3. **对内插件 API** —— 存储后端需实现的 `StoragePlugin` trait

## 通用约定

- 所有路径都以 `/sessions/{sid}/...` 为会话作用域前缀。
- 鉴权：bot 使用 `Authorization: Bearer <bot-token>`；human 使用 cookie/OAuth
  （通过现有 `resolve_group_chat_caller` 解析，与现有会话路由一致）。
- 所有 upload/download/list/delete 都要求 caller 是会话参与者（或拥有作为参与者的 bot）。
  delete 另外允许会话创建者 / group driver bot。
- 所有 JSON 请求/响应 body 均为 `Content-Type: application/json`；上传字节端点
  `PUT .../content` 接收原始字节 body（非 JSON）。
- 时间戳为 Unix epoch 秒（u64），对齐现有 BCS 领域类型。
- 大小单位为字节。
- `ActorRef = { "actor_kind": "Bot"|"Human", "actor_id": "<string>" }`。
- `object_handle` 为后端特定的不透明字符串（`UploadHandle`/`StorageHandle` 的序列化形式），
  客户端应视为不可解析、仅用于回传。
- 字段可空性：`sha256` 为可选 —— 后端未校验/未返回完整性哈希时为 `null`（baas 留存模式
  通常不返回 sha256）。客户端必须按可空解析，不得将 `sha256` 视为必填。

### `FileStatus` 状态机

`FileStatus = "Pending"|"Ready"|"Deleting"|"Failed"`。

| 状态 | 产生操作 | 暴露给客户端 | 含义 |
|---|---|---|---|
| `Pending` | `POST /files`（prepare） | 是 | 已 prepare，等 `PUT .../content` + `complete` |
| `Ready` | `POST /complete` 成功 | 是 | 已可下载/删除 |
| `Failed` | `complete`/上传后端失败 | 是 | 上传/后端失败；客户端可 `DELETE` 清理后重传 |
| `Deleting` | `DELETE` 开始后端删除中的瞬态 | 否（内部） | 删除进行中；客户端视角直接 204，不看到此状态 |

客户端通过 `GET /.../{file_id}` 与 `GET /.../files`（list）可见 `Pending`/`Ready`/`Failed`，
不会看到 `Deleting`（删除接口同步返回 204 或 502）。

### `SessionFile` 资源（多数接口返回）

```json
{
  "file_id": "01HZX...",
  "session_id": "g1:a1b2c3d4",
  "file_name": "report.pdf",
  "mime_type": "application/pdf",
  "size": 1048576,
  "sha256": null,
  "owner": { "actor_kind": "Human", "actor_id": "human_327325" },
  "storage_backend": "baas",
  "object_handle": "a1b2c3d4-transfer-id",
  "status": "Ready",
  "created_at": 1721462400,
  "updated_at": 1721462405
}
```

`sha256` 可空（见上）。`storage_backend` 对应 `StoragePlugin::backend_name()`（`"local"` / `"baas"` / …）。

### 错误响应体（对标现有 `session_error_to_response`）

```json
{ "error": "<ERROR_CODE>", "message": "<可读说明>" }
```

HTTP 状态码跟随错误码（见各端点说明）。通用错误码：

| code | http | 含义 |
|---|---|---|
| `UNAUTHORIZED` | 401 | caller 未解析 |
| `FORBIDDEN` | 403 | 非会话参与者，或无删除/取消权限 |
| `SESSION_NOT_FOUND` | 404 | `sid` 不存在 |
| `FILE_NOT_FOUND` | 404 | 当前会话下 `file_id` 不存在 |
| `INVALID_TRANSITION` | 409 | 生命周期**转换动作**（upload/complete/abort/delete）在当前状态下不合法：对非 `Pending` 文件重复 PUT、对已 complete/cancel 的文件再 complete、`complete` 时字节尚未上传等 |
| `PAYLOAD_TOO_LARGE` | 413 | 超过 `max_size`（= min(BCS `max_file_size` 配置, 后端 `max_object_size`)，bootstrap 阶段静态化，v1 ~100 MB） |
| `INVALID_STATE` | 422 | 资源状态不满足**读取/下载**前提：如对非 `Ready` 文件执行 `GET .../content` |
| `UNSUPPORTED_BACKEND` | 501 | 后端不支持所请求能力（如本地后端的 `presign_get` 下载预签名） |
| `STORAGE_BACKEND` | 500/502 | 后端操作失败（不泄漏后端内部信息） |

---

# 1. 对外 HTTP API

Base：`http://{bcs-host}/sessions/{sid}/files`

## 1.1 查询能力 — `GET /sessions/{sid}/files/capabilities`

可选。上传一律走 BCS 三阶段，无需先查能力；该端点主要供客户端预判**下载**是否直连。

**响应 200：**
```json
{
  "storage": "baas",
  "presign_download": true,
  "max_size": 104857600
}
```

- `storage`：存储后端名称，对应 `StoragePlugin::backend_name()`（`"local"`/`"baas"`/…）。
- `presign_download=true`：`GET .../content` 会 302 跳转到后端签名 URL，字节不经 BCS。
- `presign_download=false`（如本地后端）：`GET .../content` 由 BCS 流式返回 body。
- `max_size` = min(BCS `max_file_size` 配置, 后端 `capabilities().max_object_size`)，在 bootstrap
  阶段计算并注入 `SessionFileService`，运行时不再动态调用 `capabilities()`（baas `capabilities()` 会 IO）。

## 1.2 发起上传（prepare） — `POST /sessions/{sid}/files`

三阶段上传第一步。创建 `Pending` 的 `SessionFile` 行，返回 BCS 自有的上传链接。

**请求：** `application/json`
```json
{
  "file_name": "model.bin",
  "size": 52428800,
  "mime_type": "application/octet-stream"
}
```

`size` 是权威值，后端据此在单片与分段间自动分流（阈值由后端 `capabilities().max_object_size`
与分段阈值决定，对标 baas 的 `MULTIPART_THRESHOLD`，默认 100 MB）。客户端无需选择模式。

### 1.2.a 单片上传（`size` < 分段阈值，**v1 默认路径**）

**响应 201：**
```json
{
  "file_id": "01HZX...",
  "mode": "single",
  "upload_url": "http://{bcs-host}/sessions/g1:a1b2c3d4/files/01HZX.../content",
  "method": "PUT",
  "expires_at": 1721466000
}
```

客户端一次 PUT 全部字节到 `upload_url`，再调 `complete`。

### 1.2.b 分段上传（`size` ≥ 分段阈值，**v2+；v1 单片上限内不会出现**）

> v1 强制 ~100 MB 单片上限，prepare 不会返回分段响应；超限直接 `413`。
> 以下为 v2+ 大文件分段形态，与 baas `MULTIPART` 响应对齐，此处先约定。

**响应 201：**
```json
{
  "file_id": "01HZX...",
  "mode": "multipart",
  "part_size": 10485760,
  "part_count": 50,
  "parts": [
    { "part_number": 1, "upload_url": "http://{bcs-host}/sessions/g1:a1b2c3d4/files/01HZX.../content?part=1", "method": "PUT", "expires_at": 1721466000 },
    { "part_number": 2, "upload_url": "http://{bcs-host}/sessions/g1:a1b2c3d4/files/01HZX.../content?part=2", "method": "PUT", "expires_at": 1721466000 }
  ],
  "expires_at": 1721466000
}
```

`part_count = ceil(size / part_size)`；一次 prepare 返回所有分片的 `upload_url`，可并行上传。
每个 `upload_url` 仍由 BCS 提供（`PUT .../content?part={n}`），字节经 BCS 转发到后端对应分片
（baas 的 OSS 分片直传 URL；OSS multipart 的对应 part）。客户端 PUT 各分片后调一次 `complete`，
由 `StoragePlugin::complete_upload` 在后端组装（baas/OSS：list/组装分片；本地：按 part 顺序拼接
临时段文件），客户端无需收集 ETag。中途可 `DELETE` 取消（后端 abort 分段会话）。

### 通用约定

`upload_url`（单片）与 `parts[].upload_url`（分段）始终由 BCS 提供，无论后端是本地还是 baas。
后端差异（本地落盘 / baas 签发 OSS 直传 URL 并转发）封装在 `StoragePlugin` 内，对客户端不可见。
所有上传链接在 `expires_at` 前有效；超时未上传可用 `DELETE` 取消后重新发起。
`expires_at` = BCS 给出的上传链接/句柄过期时间（取 `ttl_secs` 配置与后端签发 URL 过期时间的更早者）；
超时未 `complete` 的 `Pending` 文件由后台 sweep 转为 `Failed` 并 `abort_upload` 清理后端，v1 由 sweep
兜底（非 v1 阻塞）；`Failed` 后再 `complete` 收到 `INVALID_TRANSITION`（409），客户端可 `DELETE` 清理重传。

**错误：** 超 `max_size` 前置校验拒返 `413 PAYLOAD_TOO_LARGE`；非参与者返 `403 FORBIDDEN`。

## 1.3 上传字节 — `PUT /sessions/{sid}/files/{file_id}/content`

三阶段第二步。客户端把原始字节 PUT 到 1.2 返回的 `upload_url`（即本端点）。BCS 流式接收并
交给 `StoragePlugin::stream_upload`：本地直接落盘到临时文件；baas 把字节**流式转发**到 baas
签发的 OSS 直传 URL。BCS 不在内存/磁盘全量持有文件。单片上传时 `part_number = None`；
v2 分段上传时 URL 带 `?part={n}`，BCS 解析后以 `Some(n)` 传入 `stream_upload`，各分片独立落盘/
转发，最后由 `complete_upload` 在后端组装。

与下载端点 `GET /sessions/{sid}/files/{file_id}/content` 同路径、不同方法。

**请求：** 原始字节 body；`Content-Length` 为文件大小；`Content-Type` 可选（按 prepare 的 mime）。

**响应 202：**
```json
{ "file_id": "01HZX...", "status": "Pending" }
```

文件仍为 `Pending`，待 `complete` 转 `Ready`。重复 PUT 同一 `file_id`：`409 INVALID_TRANSITION`。

**错误：** `404 FILE_NOT_FOUND`；`409 INVALID_TRANSITION`（文件非 `Pending`）；
`413 PAYLOAD_TOO_LARGE`（超 `max_size` 或 `Content-Length` 与 prepare 的 `size` 不符）；
`502 STORAGE_BACKEND`（后端接收失败，不泄漏后端内部信息）。

## 1.4 完成上传 — `POST /sessions/{sid}/files/{file_id}/complete`

三阶段第三步。
- 本地：finalize（fsync + 原子改名到终态 key），校验 size/sha；
- baas：调 baas `complete` + 轮询到 `DONE`，取回 `StorageObjectMeta`。

**请求 body：** 空 `{}`。

**响应 200：** `SessionFile`，`status: "Ready"`。

**错误：** `404 FILE_NOT_FOUND`；`409 INVALID_TRANSITION`（已 complete/cancel，或字节尚未上传）；
`502 STORAGE_BACKEND`（后端无法确认对象）。

## 1.5 删除文件 / 取消上传 — `DELETE /sessions/{sid}/files/{file_id}`

单一入口，按 `file_id` 当前 `status` 自动分流两种语义：

单一入口，按 `file_id` 当前 `status` 自动分流三种语义：

### 1.5.a 删除文件（`status: Ready`）

会话工作区里一个已完成上传的文件，从后端存储和 BCS 元数据中移除。

**后端行为：** 调 `StoragePlugin::delete`（`StorageHandle` 由 `object_handle` 重建）
- 本地：unlink 终态文件（幂等，文件已不存在返 `Ok`）；
- baas：`DELETE /staging?key={oss key}`（仅终态 ticket 有效；BCS 保证调用前文件 `Ready`=`DONE`，
  baas `404 OSS_OBJECT_NOT_FOUND` 映射为 `Ok`，幂等）。

后端删除成功后删除 `bcs_session_files` 元数据行（先后端再行，避免幽灵行；若后端成功而行删除
失败，留下待对账的孤儿对象，由解耦 sweep 收敛）。

### 1.5.b 取消上传（`status: Pending` 或 `Failed`）

一个尚未 `complete`（`Pending`）或上传/后端已失败（`Failed`）的文件，中止并清理。`Failed` 本质
是尚未完成上传，**按 `Pending` 处理**走 `abort_upload` 清理后端 staging/临时文件再删行——不走
`delete`/`DELETE /staging`，否则 baas 可能找不到 staging 对象。

**后端行为：** 调 `StoragePlugin::abort_upload`（`UploadHandle` 由 `object_handle` 重建）
- 本地：unlink 临时 `.part` 段文件（单片）/ 所有分片段文件（分段），幂等；
- baas：`DELETE /upload-url/{transfer_id}`，ticket 转 `CANCELLED` 终态（幂等）。

随后删除 `bcs_session_files` 元数据行。

### 权限

删除与取消权限一致：**上传者**（human 上传者；若上传者是 bot 则需拥有该 bot）**或** 会话创建者 /
该 group 的 driver bot。镜像现有 `delete_session` 规则。会话普通成员可上传/下载/列出，但不可
删除他人文件。

### 响应与错误（完全幂等）

**响应 204：** 无 body。`DELETE` 对**已删除/已取消**的 `file_id` 重复调用也返回 204，不报 404 ——
BCS 无软删除审计表，无法区分"从未存在"与"已删干净"，故统一按完全幂等处理：若 DB 行已不存在，
跳过元数据删除、对后端做一次幂等探测（`StoragePlugin::delete`/`abort_upload` 对已不存在对象
返 `Ok`）后仍返 204。这与 `StoragePlugin::delete` 的 Idempotent 语义一致。

**错误：**
- `403 FORBIDDEN` —— 非上传者且非会话创建者/driver；
- `502 STORAGE_BACKEND` —— 后端删除/取消失败（不泄漏后端内部信息，可重试）；
- （不再返回 `404 FILE_NOT_FOUND`：重复删除/取消走幂等 204。）

## 1.6 列文件 — `GET /sessions/{sid}/files`

**查询参数：**

| 参数 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `prefix` | string | 否 | 文件名前缀过滤 |
| `limit` | int | 否 | 默认 100，最大 1000 |
| `marker` | string | 否 | 不透明分页游标，取自上次的 `next_marker` |

**响应 200：**
```json
{
  "items": [ { /* SessionFile */ }, { /* SessionFile */ } ],
  "truncated": false,
  "next_marker": null
}
```

- 默认按 `created_at` **升序**返回（保证分页稳定、不漏页不重页）；`marker` 为不透明游标，
  客户端原样回传，不得解析。
- `prefix` 对 `file_name` 做**大小写敏感**的前缀过滤。
- 列表权威来源为 BCS 自身 DB，从不取自后端。
- 同一会话**允许存在多个同名文件**：`file_id` 是唯一标识（DB 唯一索引在 `(env, session_id, file_id)`，
  不在 `file_name`）。客户端不得按 `file_name` 去重。

## 1.7 查询文件元数据 — `GET /sessions/{sid}/files/{file_id}`

**响应 200：** `SessionFile`。

**错误：** `404 FILE_NOT_FOUND`。

## 1.8 下载字节 — `GET /sessions/{sid}/files/{file_id}/content`

- 预签名后端（`supports_presign_download=true`）：**302 跳转** 到短期有效的预签名 `download_url`。字节在客户端 <-> 后端直接流转。
- 本地后端（`supports_presign_download=false`）：流式返回 body，含 `Content-Type`、`Content-Length`、
  `Content-Disposition: attachment; filename="..."`。

可选查询 `?ttl=<秒>`（默认按后端；baas `share-link` 60–604800）控制预签名 URL 有效期。

**重定向行为说明**：GET 的 302/307 会被标准 HTTP 客户端自动跟随（reqwest 默认跟随、`curl -L`、
浏览器原生 GET），客户端最终从后端（OSS）取字节，对客户端透明、无需适配。重定向目标是跨主机
（OSS 而非 BCS），规范客户端会在跨主机重定向时**剥离 `Authorization` 头**（reqwest 默认如此），
Bearer token 不泄漏给 OSS；OSS 预签名 URL 自带 query 签名，自洽。v1 客户端为 bot/CLI，302 完全够用；
若后续 Web UI 经浏览器 `fetch` 直连遇到 CORS，可对那类客户端让 BCS 走中继回退（流式返回 body）。

**错误：** `404 FILE_NOT_FOUND`；文件非 `Ready` 返 `422`。

> 文件的删除与上传取消见 1.5。

---

# 2. 对外 CLI API

`bcs-cli`（clap-derive），扩展 `SessionCommands`。token/URL 解析遵循现有 `bcs` CLI 约定
（`--token`/env/`session.json`，`--url`/env/`127.0.0.1:21000`）。默认 JSON 输出；
`--no-json` 为人读。

```
bcs session file <SUBCOMMAND>
```

子命令：

### 2.1 `upload` —— 上传（自动封装三阶段）
```
bcs session file upload \
  --session <sid> \
  --path <local-file> \
  [--name <override-filename>] \
  [--mime <mime-type>] \
  [--token <t>] [--url <bcs-url>] [--no-json]
```
内部自动串起三阶段：`POST /files`（prepare）→ PUT 字节到 `upload_url` → `POST /complete`，
客户端只需指定本地文件路径。打印最终 `SessionFile`（`Ready`）。上传中失败会尝试 `DELETE`
取消再退出。上传是 CLI 唯一的上传入口，不单独暴露 `upload-url`/`complete` 低阶命令，保持
接口简单；需要分阶段控制的脚本可直接走 HTTP 三阶段接口。

### 2.2 `list` —— 列出工作区文件
```
bcs session file list \
  --session <sid> \
  [--prefix <p>] [--limit <n>] [--marker <m>] \
  [--token <t>] [--url <bcs-url>]
```
打印 `{items, truncated, next_marker}`（`--no-json` 下为表格）。

### 2.3 `download` —— 下载文件字节
```
bcs session file download \
  --session <sid> \
  --file-id <id> \
  [--out <local-path>] \
  [--ttl <seconds>] \
  [--token <t>] [--url <bcs-url>]
```
跟随预签名跳转并流式写到 `--out`（默认当前目录，文件名取自元数据 `file_name`）。打印保存路径。

### 2.4 `delete` —— 删除/取消
```
bcs session file delete --session <sid> --file-id <id> [--token <t>] [--url <bcs-url>]
```
按服务端文件状态生效：`Pending` 时取消上传，`Ready` 时删除文件。成功时打印空/确认信息。

### 2.5 `capabilities` —— 查询后端能力
```
bcs session file capabilities --session <sid> [--token <t>] [--url <bcs-url>]
```
打印 `{storage, presign_download, max_size}`，可选用以预判下载是否直连。

---

# 3. 对内插件 API —— `StoragePlugin`

crate：`bcs-storage-api`（位于 `crates/plugin-api/`，相对 `src/bcs/`）。后端实现该 trait；
`SessionFileService` 只调用该 trait，不依赖具体后端。crate 同时提供
**`FakeStoragePlugin`**（in-memory 实现），覆盖三阶段上传 / 下载 / 删除 / 幂等 / 分段路径，供
service 单测与 HTTP 层测试注入复用（对标 `DbPlugin`/`CachePlugin` 的 fake 模式）。

```rust
use async_trait::async_trait;

#[async_trait]
pub trait StoragePlugin: Send + Sync + 'static {
    // Stable backend name ("local", "baas", "oss", ...). Stored on each
    // SessionFile.storage_backend for auditability.
    fn backend_name(&self) -> &'static str;

    // Advertised capabilities. Only affects download routing now (upload is
    // always the BCS three-stage flow).
    fn capabilities(&self) -> StorageCapabilities;

    // --- upload: BCS three-stage, plugin decides concrete implementation ---
    // prepare -> stream -> complete, with abort for cancel. The UploadHandle
    // returned by prepare is serialized into SessionFile.object_handle and
    // reconstructed for the later stages (HTTP is stateless across requests).

    /// Reserve a staging upload. Local: open a temp file. Baas: call
    /// POST /upload-url (retention mode) to get an OSS direct-put URL + transfer_id.
    async fn prepare_upload(
        &self,
        req: UploadPrepareRequest,
    ) -> Result<UploadHandle, StorageError>;

    /// Feed the client's bytes into staging as a stream. Local: write to temp
    /// file (single) or a per-part temp segment (multipart). Baas: stream-relay
    /// (PUT) the bytes to the OSS direct-put URL issued in prepare_upload —
    /// BCS never buffers the whole file.
    ///
    /// `part_number`: v1 单片上传恒为 `None`；v2 分段上传传对应分片编号（1-based）。
    /// 现在就以 `Option` 形式固定在签名里，避免后续为支持 multipart 而破坏性改 trait。
    async fn stream_upload(
        &self,
        handle: &UploadHandle,
        part_number: Option<u16>,
        body: ByteStream,
    ) -> Result<(), StorageError>;

    /// Finalize the upload. Local: fsync + atomic rename to final key. Baas:
    /// POST /upload-url/{id}/complete then poll /transfers until DONE.
    async fn complete_upload(
        &self,
        handle: &UploadHandle,
    ) -> Result<StorageObjectMeta, StorageError>;

    /// Cancel an in-progress (Pending) upload. Local: unlink temp (single) or
    /// all per-part temp segments (multipart). Baas: DELETE /upload-url/{id}.
    /// Idempotent.
    async fn abort_upload(&self, handle: &UploadHandle) -> Result<(), StorageError>;

    // --- download / delete (operate on a finalized Ready object) -----------

    /// Streaming read of a finalized object. Used when supports_presign_download
    /// is false, or as a fallback.
    async fn get_stream(
        &self,
        handle: &StorageHandle,
    ) -> Result<ByteStream, StorageError>;

    /// Issue a short-lived presigned download URL. Used when
    /// supports_presign_download is true (BCS GET .../content returns 302 to it).
    async fn presign_get(
        &self,
        handle: &StorageHandle,
        ttl_secs: u64,
    ) -> Result<PresignGetTicket, StorageError>;

    /// Permanently remove a finalized object. Idempotent: missing object is Ok.
    async fn delete(&self, handle: &StorageHandle) -> Result<(), StorageError>;

    async fn health_check(&self) -> Result<StorageHealth, StorageError>;
}
```

### 辅助类型

```rust
pub struct StorageCapabilities {
    pub supports_presign_download: bool, // true: GET .../content 302s; false: BCS streams
    pub supports_stream_get: bool,       // true for all known backends
    pub max_object_size: u64,            // backend hard limit; BCS also enforces its own max_file_size
}

pub struct UploadPrepareRequest {
    pub key: String,                // BCS-derived final key, e.g. session-files/{env}/{sid}/{file_id}/{file_name}
    pub file_name: String,
    pub mime_type: String,
    pub size: u64,                  // authoritative; backends also enforce max_object_size
    pub ttl_secs: u64,              // staging / upload_url lifetime
}

/// Backend-specific, serializable handle persisted as SessionFile.object_handle.
/// Reconstructed for stream_upload / complete_upload / abort_upload / delete.
pub struct UploadHandle {
    pub backend: &'static str,
    pub key: String,
    // local (single): { temp_path, final_path }
    // local (multipart, v2): { final_path, parts: [{ part_number, temp_path }] }
    // baas (single):  { transfer_id, oss_direct_put_url, oss_key, expires_at }
    // baas (multipart, v2): { transfer_id, parts: [{ part_number, oss_direct_put_url }], oss_key, upload_session_id, expires_at }
    pub backend_handle: serde_json::Value,
    pub expires_at: u64,
}

/// Handle for a finalized (Ready) object — derived from UploadHandle after
/// complete_upload, or reconstructed from SessionFile.object_handle.
pub struct StorageHandle {
    pub backend: &'static str,
    pub key: String,
    pub backend_handle: serde_json::Value,
}

pub struct PresignGetTicket {
    pub download_url: String,
    pub expires_at: u64,
}

pub struct StorageObjectMeta {
    pub key: String,
    pub size: u64,
    pub sha256: Option<String>,      // backends that can verify integrity return it
}

pub struct StorageHealth {
    pub ok: bool,
    pub detail: Option<String>,
}

// ByteStream is an async byte stream. Concrete alias to be finalized in the
// `bcs-storage-api` crate (e.g. `Box<dyn Stream<Item = Result<Bytes, io::Error>> + Send + Unpin>`
// over `bytes::Bytes`), matching the streaming types already used in `bcs-http`/`bcs-ws`.
pub type ByteStream = Box<dyn ByteStreamTrait + Send + Unpin>;

pub trait ByteStreamTrait: futures_core::Stream<Item = Result<bytes::Bytes, std::io::Error>> {}
```

### 错误类型

对标 `DbError` 的后端无关形态；lint 禁止在 message 中泄漏后端内部信息。

```rust
#[derive(Debug, thiserror::Error)]
pub enum StorageError {
    #[error("invalid input: {0}")]
    InvalidInput(String),
    #[error("object not found")]
    NotFound,
    #[error("state conflict: {0}")]
    Conflict(String),
    #[error("unsupported by backend {0}")]
    Unsupported(&'static str),
    #[error("backend error")]
    Backend(#[from] anyhow::Error),
}
```

### 契约测试（位于 `bcs-storage-api`，所有后端共用）

每个 `StoragePlugin` 实现须通过同一套用例（对标 `bcs-test-support`）：
- `prepare_upload` -> `stream_upload(handle, None /* part_number */, 已知字节)` -> `complete_upload`
  得到一个 `Ready` 对象，其 `StorageObjectMeta.size` 与输入一致
- v2 分段契约（v1 跳过）：`stream_upload(handle, Some(n), part_bytes)` 多次 -> `complete_upload`
  在后端按序组装，往返原始字节
- `complete_upload` 后 `get_stream`（或 `supports_presign_download` 时 `presign_get` + GET）
  能往返原始字节
- `abort_upload`（在 `complete_upload` 之前）使对象不存在，后续 `delete`/`get_stream` 返回
  `NotFound`
- `delete` 幂等，且使后续 `get_stream` 返回 `NotFound`
- `capabilities()` 的 `backend_name` 一致且非空
- `health_check` 对真实（或 stub）后端返回 ok

### key 派生约定

`StoragePlugin` 以 key 寻址。BCS 为每个文件派生 `key`，按会话/后端隔离避免冲突，例如
`session-files/{env}/{session_id}/{file_id}/{file_name}`。后端不应假设 key 之外存在层级。
`SessionFile` 行上存储的 `object_handle` 是 `UploadHandle`（上传中）或 `StorageHandle`
（`Ready` 后）的序列化形式，BCS 将其重建后传给 `stream_upload`/`complete_upload`/
`abort_upload`/`delete`/`get_stream`/`presign_get`。

## 3.1 后端：`bcs-storage-local`（`crates/plugins/bcs-storage-local/`）

- `capabilities`：`supports_presign_download = false`，`max_object_size` 取**配置项**（默认等于 BCS
  `max_file_size` 或合理上限），不以磁盘剩余空间作为静态 capability（剩余空间动态变化无法在启动时
  固定）；`stream_upload` 中再实际校验磁盘可用空间，不足返 `StorageError::Backend`。
- key 映射到 `$BCS_DATA_DIR/session-files/...`（或配置的 `data_dir`）下的文件。
- `prepare_upload`：开临时文件待写；`UploadHandle.backend_handle`（单片）含
  `{ temp_path, final_path }`。**`temp_path` 必须包含 `file_id`（key 中已含）并建议附加随机后缀**，
  v2 分段时各段路径含 `part_number`，保证多客户端/多 worker 同 key 并发不冲突（
  `{data_dir}/{key}.{rand}.part` / 分段 `{data_dir}/{key}.p{part_number}.{rand}.part`）。
- `stream_upload`：流式写入 temp 文件，校验累计 size ≤ prepare size。
- `complete_upload`：fsync + 原子改名为终态 `final_path`，返回 `StorageObjectMeta`。
- `abort_upload`：unlink temp（幂等）。
- `get_stream`：打开终态文件流式返回；`presign_get` 返回 `StorageError::Unsupported`；
  `delete`：unlink 终态文件（幂等）。
- 用于开发、测试和单节点部署。

## 3.2 后端：`bcs-storage-baas`（`crates/plugins/bcs-storage-baas/`）

baas base URL：`http://{baas-host}:8890/api/v1/bots/{tenant}/{bot_uuid}/files`（配置）。
使用**留存模式**（不带 `device_path`），文件仅存 OSS 供会话共享，不做设备投递。
BCS 对 baas 上传是**纯转发**：客户端 PUT 到 BCS，BCS 把字节流式中继到 baas 签发的 OSS
直传 URL，自身不持有完整文件。

| StoragePlugin 方法 | baas 调用 | 说明 |
|---|---|---|
| `backend_name` | — | `"baas"` |
| `capabilities` | — | `supports_presign_download = true` |
| `prepare_upload` | `POST /upload-url`（不带 `device_path`；带 `filename`、`file_size`、`expire_seconds`） | 返回 OSS 直传 `upload_url` + `transfer_id`；`UploadHandle.backend_handle` 含 `{ transfer_id, oss_direct_put_url, oss_key, expires_at }`。v1 仅 SINGLE 分片（≤100 MB）。 |
| `stream_upload` | 流式 PUT 客户端字节到 `oss_direct_put_url` | BCS 只做转发，不全量落盘。 |
| `complete_upload` | `POST /upload-url/{id}/complete` 后轮询 `GET /transfers/{id}` 直到 `status == "DONE"` | 留存模式直接跳到 DONE。返回 `StorageObjectMeta`（size，可选 sha256）。 |
| `abort_upload` | `DELETE /upload-url/{transfer_id}` | 终态 `CANCELLED`；幂等。 |
| `presign_get` | `POST /transfers/{id}/share-link`（`expire_seconds`） | `share_url` 作为预签名下载 URL，供 BCS `GET .../content` 的 302 跳转。 |
| `get_stream`（回退） | `share-link` -> `GET share_url` -> 流式返回 | `supports_presign_download = true` 时下载走 302，一般不用。 |
| `delete` | `DELETE /staging?key={oss key}` | 仅对终态 ticket（`DONE`/`FAILED`/`CANCELLED`）有效；BCS 保证调用前文件 `Ready`（`DONE`）。baas `404 OSS_OBJECT_NOT_FOUND` 映射为 `Ok`（幂等）。 |
| `health_check` | baas base_url 可达性探测（`HEAD/GET` baas 根或配置的 probe path） | **不依赖任何真实 transfer_id**，避免污染/依赖生产数据与"某已知 id"不可复现问题。仅探测 base_url 可达 + 鉴权可用，不探测真实对象。 |

**错误映射：**
- baas `TRANSFER_NOT_FOUND` -> `StorageError::NotFound`
- baas `TRANSFER_STATE_CONFLICT` / `NOT_TERMINAL_STATE` / `DIRECTORY_NOT_EMPTY` ->
  `StorageError::Conflict`
- baas `INVALID_TRANSITION` -> `StorageError::Conflict`
- baas `NOT_IMPLEMENTED` -> `StorageError::Unsupported("baas")`
- 其他 `code != 0` -> `StorageError::Backend`

**身份 / 租户：** BCS 在存储插件配置中存储配置好的 baas `tenant` + `bot_uuid`（凭证）。
按会话分配 bot 身份为后续扩展；v1 使用一个配置好的 service bot。baas 不施加 BCS 会话语义
—— BCS 是会话维度权威，并拥有列表权威来源。

## 3.3 未来后端（trait 就绪，v2+）

- `bcs-storage-oss`：`supports_presign_download = true`；`prepare_upload` 发 OSS 直传 URL
  （单片），大文件 v2 走 multipart init 并在 `stream_upload`/`complete_upload` 编排分片；
  `delete` 删对象；`list` 不使用（BCS 从 DB 列）。
- `bcs-storage-nas`：`supports_presign_download = false`；通过挂载的 NFS 路径流式，对标
  `bcs-storage-local`（temp + rename）。
- 三方服务：实现该 trait；`SessionFileService` 及 HTTP/CLI 表面均无需改动。