# BCS 会话工作区 — baas 存储插件（设计）

**日期：** 2026-07-20
**状态：** 评审稿
**作者：** zhangwu.zh
**范围：** 基于 `StoragePlugin` trait（定义于 `2026-07-20-bcs-session-workspace-api.md` §3 与
`2026-07-20-bcs-session-workspace-design.md`）与 baas 文件传输 HTTP API，实现 `bcs-storage-baas`
插件。**该插件 crate 独立于当前 BCS 仓库**（不在 `src/bcs/crates/plugins/` 下），只需依赖
`bcs-storage-api` trait crate 即可被 BCS 在组装根按配置装配。

## 目标

为 BCS 会话工作区提供一个 baas 后端存储插件：BCS 仍是会话维度的权威（成员校验、元数据、列表），
baas 仅作为字节承载体（文件落 OSS，留存模式，不做设备投递）。BCS 对 baas 上传是**纯转发** ——
客户端 PUT 到 BCS，BCS 把字节流式中继到 baas 签发的 OSS 直传 URL，自身不持有完整文件；下载经
预签名 `share_url` 302 直连 OSS，字节不经 BCS，落实 baas 的流量隔离原则。

## 参考资料

- 框架与 trait 契约：`2026-07-20-bcs-session-workspace-design.md`、`2026-07-20-bcs-session-workspace-api.md`（§3 `StoragePlugin`、§3.2 baas 后端表）
- baas「第四通道」设计（内部背景参考，外部贡献者可忽略；核心接口映射已内嵌本文档与 API 文档 §3.2）：
  https://yuque.antfin.com/securitytec/otbct4/xqtzt0qamaruiofc
- baas 文件上传/下载 API 参考（同上，内部背景参考）：
  https://yuque.antfin.com/securitytec/otbct4/gp53l55mme92fbke

## baas 模型概述

baas 是 **bot 中心化、异步 ticket 模型**的文件传输服务。base URL 形如
`http://{baas-host}:8890/api/v1/bots/{tenant}/{bot_uuid}/files`，所有操作以 `transfer_id` 为锚。

BCS 上传全部走 baas **留存模式（retention mode）**：`POST /upload-url` **不带 `device_path`**，
文件仅存 OSS 供会话共享，不做设备投递（`device_path` 提供 → 投递模式，文件送达设备，不适用本场景）。
留存模式下 ticket 状态机短促：`CREATED → UPLOAD_COMPLETED → DONE`（跳过 `PULLING`）。

baas 不施加 BCS 会话语义 —— BCS 才是会话维度权威，BCS 自身 DB 为列表权威来源（`GET /staging` 列表
不使用，那是 baas 的 bot 命名空间扁平列表，与会话作用域不对应）。

## 插件 crate

- crate 名：`bcs-storage-baas`（**独立于 BCS 仓库**，仅依赖 `bcs-storage-api` trait crate）。
- 装配：BCS 组合根（`crates/bootstrap/bcs/server.rs`）在 `storage_backend = "baas"` 时构造
  `BaasStoragePlugin` 并以 `Arc<dyn StoragePlugin>` 注入 `SessionFileService`。
- `backend_name()` 返回 `"baas"`；`capabilities()` 返回 `supports_presign_download = true`
  （`max_object_size` 取 baas 硬上限或配置 probe；v1 受 BCS `max_file_size` ~100 MB 约束）。

## `StoragePlugin` 方法 → baas HTTP 映射

baas 统一响应体 `{"code": 0, "data": {...}}`（错误 `code ≠ 0`，`detail` 含 `error`/`message`）。
下表给出每个 trait 方法的 baas 调用与行为（v1 仅 SINGLE 分片，≤100 MB；分段 v2+ 见末节）：

| `StoragePlugin` 方法 | baas HTTP | 行为与说明 |
|---|---|---|
| `capabilities()` | — | `supports_presign_download = true` |
| `prepare_upload` | `POST /upload-url`（**不带 `device_path`**；带 `filename`、`file_size`、`expire_seconds`） | 返回 `transfer_id` + OSS 直传 `upload_url`（`type:"SINGLE"`）。构造 `UploadHandle`（见下），持久化为 `SessionFile.object_handle`。 |
| `stream_upload`（`part_number=None`） | `PUT {oss direct-put upload_url}` 客户端字节**流式转发** | BCS 只做流式中继，不在本地全量落盘、不进内存全量缓冲。`Content-Length` 用 prepare 的 `file_size`。 |
| `complete_upload` | `POST /upload-url/{transfer_id}/complete`（空 body）→ 轮询 `GET /transfers/{transfer_id}` 直到 `status == "DONE"` | 留存模式直接跳到 `DONE`（无 pull）。轮询间隔与超时由配置控制。返回 `StorageObjectMeta`（`size`，可选 `sha256` —— baas 通常不返回，留 `Option`）。 |
| `abort_upload` | `DELETE /upload-url/{transfer_id}` | ticket 转 `CANCELLED` 终态，幂等。 |
| `presign_get` | `POST /transfers/{transfer_id}/share-link`（`expire_seconds`） | 仅 `status == "DONE"` 可调用；返回 `share_url`（OSS 预签名 GET URL），作为 BCS `GET .../content` 的 302 目标。若 ticket 非 `DONE`，baas 返 `INVALID_TRANSITION` → `StorageError::Conflict`。 |
| `get_stream`（回退） | `share-link` → `GET share_url` → 流式返回 | `supports_presign_download = true` 时下载走 302，一般不用此方法。 |
| `delete`（`Ready` 文件） | `DELETE /staging?key={oss_key}` | 仅对终态 ticket（`DONE`/`FAILED`/`CANCELLED`）有效；BCS 保证调用前文件 `Ready`（`DONE`）。`404 OSS_OBJECT_NOT_FOUND` 映射为 `Ok`（幂等）。 |
| `health_check` | baas base_url 可达性探测（`HEAD/GET` base 或配置的 `health_probe_path`） | **不依赖任何真实 transfer_id**，避免污染/依赖生产数据。仅探测 base_url 可达 + 鉴权可用，不探测真实对象。 |

### `UploadHandle` 形态（baas）

`prepare_upload` 返回、`stream_upload`/`complete_upload`/`abort_upload` 重建用的句柄，序列化进
`object_handle`：

```jsonc
// UploadHandle.backend_handle (baas, single, v1)
{
  "transfer_id": "a1b2c3d4...",
  "oss_direct_put_url": "https://oss-cn-xxx.aliyuncs.com/...?Signature=...",
  "oss_key": "file-transfers/.../model.bin",
  "expires_at": 1721466000
}
```

```jsonc
// StorageHandle.backend_handle (baas, Ready 后)
{
  "transfer_id": "a1b2c3d4...",
  "oss_key": "file-transfers/.../model.bin"
}
```

`complete_upload` 成功后，BCS 把 `object_handle` 从 `UploadHandle` 形态替换为 `StorageHandle` 形态
（保留 `transfer_id`/`oss_key`，去掉已过期的 OSS 直传 URL）。`presign_get`/`delete` 用 `StorageHandle`
里的 `transfer_id`（`share-link`）/`oss_key`（`DELETE /staging`）寻址。

## 错误映射

baas 错误码 → `StorageError`（trait 方法不泄漏 baas 内部信息到客户端，仅用于 BCS 内部日志/重试决策）：

- baas `TRANSFER_NOT_FOUND` → `StorageError::NotFound`
- baas `TRANSFER_STATE_CONFLICT` / `NOT_TERMINAL_STATE` / `DIRECTORY_NOT_EMPTY` → `StorageError::Conflict`
- baas `INVALID_TRANSITION` → `StorageError::Conflict`（如对非 `DONE` 的 ticket 请求 `share-link`）
- baas `NOT_IMPLEMENTED` → `StorageError::Unsupported("baas")`
- baas `OSS_OBJECT_NOT_FOUND`（`DELETE /staging` 时）→ 映射为 `Ok`（幂等语义）
- 其他 `code != 0` → `StorageError::Backend`

`StorageError` 再由 BCS HTTP 层映射为对外错误码（如 `Conflict→INVALID_TRANSITION(409)`、
`Backend→STORAGE_BACKEND(502)`），详见 `api.md` 通用错误码表。

## 身份 / 租户

baas base URL 含 `{tenant}/{bot_uuid}`。BCS 在存储插件配置中存储配置好的 `tenant` + `bot_uuid`
凭证。v1 使用**一个配置好的 baas service bot**（所有会话的上传/下载共享该 bot 命名空间下的
staging）；按会话/按上传者分配 baas bot 身份为后续扩展（需要 BCS 持有每个会话内 bot 的 baas
凭证，本期不做）。

baas 凭证（鉴权头/token）由 `bcs-storage-baas` 插件内部持有，不暴露给 BCS 上层或客户端。客户端
只看到 BCS 的 `upload_url`（指向 BCS 自己），不会接触到 baas 的 OSS 直传 URL 或 share_url
（share_url 仅在下载 302 时短暂暴露给客户端，且为自签名的 OSS URL，不含 baas 凭证）。

## 流量隔离

文件字节在客户端与 OSS 之间通过预签名 URL 直接流转：
- 上传：客户端 → BCS →（流式转发）→ OSS 直传 URL。BCS 只中继，不落盘。
- 下载：客户端 ← OSS `share_url`（BCS 302 跳转，字节不经 BCS/baas 服务实例）。

这与 baas「第四通道」流量隔离原则一致，文件大流量不挤占 BCS 业务/聊天通道。

## 配置

`storage_backend = "baas"` 时，BCS bootstrap 读取以下配置块并构造 `BaasStoragePlugin`：

```toml
[session_files]
storage_backend = "baas"
max_file_size = 104857600       # 100 MB v1；max_size = min(max_file_size, 后端 max_object_size)

[session_files.baas]
base_url = "http://{baas-host}:8890/api/v1/bots/{tenant}/{bot_uuid}/files"
tenant = "<tenant>"             # 必填
bot_uuid = "<service-bot-uuid>" # 必填，v1 用一个配置好的 baas service bot
health_probe_path = "/"         # health_check 探测路径（相对 base_url），不依赖真实 transfer_id
# 其余可选：鉴权凭证 / 超时 / complete 轮询间隔与超时 / share-link 默认 ttl（60–604800）/ stream 转发缓冲
```

`max_size` 在 bootstrap 阶段计算并注入 `SessionFileService`，运行时不再调用 `capabilities()`。

## 测试

- **契约测试**：`bcs-storage-baas` 须通过 `bcs-storage-api` 的通用契约用例（与 `bcs-storage-local`
  同套），通过对 baas 的 stub/fake server（建议在插件 crate 内提供一个 baas HTTP 协议的 wiremock
  fixture）或 integration tag 测试覆盖：三阶段上传往返、`abort` 幂等、`delete` 幂等、`share-link`
  302 下载往返。
- **轮询与超时**：单元测试 `complete_upload` 的轮询→`DONE` 与超时→`StorageError::Backend` 路径。
- **错误映射**：表驱动测试覆盖 baas 各错误码 → `StorageError` 的映射，含 `OSS_OBJECT_NOT_FOUND→Ok`。
- 因为 crate 独立于 BCS 仓库，其测试在插件 crate 仓库内独立运行；BCS 仓库内对 baas 路径的端到端
  验证依赖 `FakeStoragePlugin`（`bcs-storage-api` 内）注入，不强制真实 baas。

## v2+ 分段上传（约定，非 v1）

`size ≥ baas MULTIPART_THRESHOLD`（默认 100 MB，v1 由 BCS `max_file_size` 直接 `413` 拦截）时，
`prepare_upload` 对应 baas `POST /upload-url` 返回的 `type:"MULTIPART"` 响应（含 `upload_session_id`、
`part_size`、`parts:[{part_number, upload_url, expires_at}]`）。`UploadHandle.backend_handle` 形态：

```jsonc
{ "transfer_id": "...", "upload_session_id": "oss-session-xxxxx",
  "parts": [{ "part_number": 1, "oss_direct_put_url": "..." }, { "part_number": 2, "oss_direct_put_url": "..." }],
  "oss_key": "...", "expires_at": ... }
```

`stream_upload(handle, Some(n), body)` 流式转发到 `parts[n].oss_direct_put_url`；`complete_upload`
调 `POST /upload-url/{id}/complete`（空 body，baas 自行 `list_parts` 校验组装，客户端不收集 ETag）→
轮询 `DONE`；`abort_upload` 调 `DELETE /upload-url/{id}` 同时 abort OSS 会话。trait 已预留
`part_number: Option<u16>`，无需破坏性改动。