# Session 文件 `show` 参数 — 浏览器内联展示

**日期：** 2026-07-29
**状态：** 设计阶段，待编写实现计划
**作者：** brainstorming 会话

## 目标

为 session 文件内容接口添加一个 `show` 查询参数，让调用方可以在浏览器的默认**展示**行为（inline）与默认**下载**行为（attachment）之间选择：

```
GET /sessions/{sid}/files/{file_id}/content?show=true   // 浏览器内联展示
GET /sessions/{sid}/files/{file_id}/content?show=false  // 浏览器下载
GET /sessions/{sid}/files/{file_id}/content             // 浏览器下载（默认）
```

`local` 与 `baas` 两种 storage 后端都必须支持。对于 `baas`——它通过 302 重定向到预签名的分享链接来提供下载——inline 与下载的行为由 baas share-link 请求体中的 `show` 字段决定；需要把这个字段一路暴露回 storage plugin trait。

同时还要暴露一个能力（capability），让前端能判断是否支持内联展示。

## 硬性不变量

默认路径——即 `show` 缺省或 `show=false`——对今天的行为**逐字节地**不产生任何改变：

- Local stream 路径：相同的 header（`Content-Disposition: attachment`），相同的字节流。
- Baas 客户端请求体：与今天相同的 JSON（`{"expire_seconds","operator"}`，没有 `show` 字段）。

`show` 在所有被传递的位置都默认为 `false`。

## 背景

所有相关代码都在 `src/bcs/crates/` 下。

**内容接口。** 在 `router.rs:421-429` 以
`get(routes::session_files::download_content)` 注册。
Handler `download_content`（`routes/session_files.rs:585-601`）已经绑定了
`Query<DownloadQuery>` 提取器，但把它丢弃了（`_q`），始终调用
`download_file_by_id(&state, &sid, &file_id, None)`。

**`download_file_by_id`**（`routes/session_files.rs:607-645`）有两条分支，
由返回的 `DownloadRoute.presign` 决定走哪一条：

- _Presign 后端（baas）：_ `Redirect::to(&ticket.download_url)` —— 302 重定向到
  baas 的 `share_url`。BCS 在此不设置 `Content-Disposition`；由 baas 通过分享链接控制。
- _Stream 后端（local）：_ BCS 自己流式输出字节，并设置
  `Content-Type`（来自 `file.mime_type`）、`Content-Length`，以及
  `Content-Disposition: attachment; filename="<file_name>"`。

同一个 helper 也被 token 鉴权的 shared-file 路由
`shared_file_content`（`routes/session_files.rs:705-722`）复用。

**`DownloadQuery`**（`routes/session_files.rs:181-187`）：
```rust
#[derive(Debug, Deserialize, Default)]
pub struct DownloadQuery {
    #[serde(default)] pub ttl: Option<u64>,
    #[serde(default)] pub token: Option<String>,
}
```

**Service 层。** `SessionFileService::download_route(sid, file_id, ttl_secs)`
（`service.rs:530-571`）仅在 `supports_presign_download` 为 true（baas）时才调用
`StoragePlugin::presign_get`；否则返回 `DownloadRoute { presign: None }`，
由 HTTP 层通过 `get_stream` 流式输出。
对外暴露的 trait 位于 `application/session_files.rs:122-191`。

**Storage plugin trait**（`plugin-api/bcs-storage-api/src/lib.rs:123-149`）：
```rust
async fn presign_get(
    &self,
    handle: &StorageHandle,
    ttl_secs: u64,
    caller: Option<&ActorRef>,
) -> Result<PresignGetTicket, StorageError>;
```
`PresignGetTicket { download_url: String, expires_at: u64 }`（同文件，90-94 行）。

同一 trait 上的 `prepare_upload` 已经接收一个 `UploadPrepareRequest` **结构体**
（位于 `caller` 之前）——这正是我们为 `presign_get` 选项结构体所遵循的先例。

**`StorageCapabilities`**（`plugin-api/bcs-storage-api/src/lib.rs:23-30`）：
```rust
pub struct StorageCapabilities {
    pub supports_presign_put: bool,
    pub supports_presign_download: bool,
    pub supports_stream_put: bool,
    pub supports_stream_get: bool,
    pub max_object_size: u64,
}
```

**能力接口。** 在 `router.rs:389-392` 注册。Handler `capabilities`
（`routes/session_files.rs:564-579`）返回 `CapabilitiesView`
（`service-api/bcs-service-api/src/application/session_files.rs:114-120`）：
```rust
pub struct CapabilitiesView {
    pub storage: String,
    pub presign_upload: bool,
    pub presign_download: bool,
    pub max_size: u64,
}
```
由 `service.rs:195-202` 填充。

**Local 实现。** `bcs-storage-local/src/lib.rs:399-406`。`presign_get` 返回
`StorageError::Unsupported("local")`，且永远不会被调用
（`supports_presign_download = false`）。
Local 能力声明在 `bcs-storage-local/src/lib.rs:47-53`。

**Baas 实现。** `bcs-storage-baas/src/lib.rs:301-316`。POST 到
`{endpoint}/api/v1/sessions/{tenant}/{session_id}/files/transfers/{transfer_id}/share-link`，
使用内联的 `serde_json::json!` 请求体：
```rust
let body = serde_json::json!({ "expire_seconds": ttl_secs, "operator": operator_str(caller) });
```
从 `data` 信封中解析出 `share_url` 和 `expires_at`。当前**没有**类型化的
share-link 请求结构体。Baas 能力声明在
`bcs-storage-baas/src/lib.rs:31-38`（`presign_download = true`）。

baas 的 share-link API 支持在请求体中带 `show` 字段：
```json
{"expire_seconds": 3600, "show": true, "operator": "user@example.com"}
```

## 设计

### 1. HTTP 层 + local 行为

**`DownloadQuery`** 增加一个字段（默认 `false`，所以缺省 = 下载）：
```rust
#[derive(Debug, Deserialize, Default)]
pub struct DownloadQuery {
    #[serde(default)] pub ttl: Option<u64>,
    #[serde(default)] pub token: Option<String>,
    #[serde(default)] pub show: bool,
}
```

**`download_content`** 不再丢弃 query：读取 `q.show` 并传给
`download_file_by_id`。**`shared_file_content`**（token 鉴权，同一个 helper）同样处理——
两条下载路径都尊重 `show`。（`download_content` 仍然像今天一样为 TTL 参数传 `None`。）

**`download_file_by_id`** 增加一个 `show: bool` 参数，并把它透传到两条分支：

- _baas（presign）：_ 将 `show` 转发给 `download_route` → `presign_get`。Handler
  依旧 302 重定向到 baas 的 `share_url`；baas 通过 share-link 的 `show` 字段控制
  inline 的 disposition。
- _local（stream）：_ 由 handler 自己选择 disposition：
  - `show=true` → `Content-Disposition: inline; filename="<file_name>"`
    （保留 filename，这样"另存为"仍有文件名可用）。
  - `show=false`/缺省 → `Content-Disposition: attachment; filename="<file_name>"`
    （今天的行为，逐字节一致）。
  - `Content-Type` 继续来自 `file.mime_type`（已设置）。对未知 MIME 类型的内联展示
    会回落到浏览器默认行为——猜测或覆盖 MIME 不在本期范围内。

### 2. Service 层 + `presign_get` trait/实现（方案 B）

**新结构体**，位于 `plugin-api/bcs-storage-api/src/lib.rs`（紧邻
`PresignGetTicket`）：
```rust
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct PresignGetOptions {
    pub ttl_secs: u64,
    pub show: bool,
}
```
`Copy` 因此按值传递。把已有的 `ttl_secs` 折叠进来（而不是搞一个投机性的单字段
结构体）意味着该结构体在第一天就有其实用价值，且未来的 presign 选项可作为
非破坏性的字段追加。

**Trait** `presign_get` 变为：
```rust
async fn presign_get(
    &self,
    handle: &StorageHandle,
    opts: PresignGetOptions,
    caller: Option<&ActorRef>,
) -> Result<PresignGetTicket, StorageError>;
```
`caller` 保持独立——它是身份/审计信息，不是 presign 选项，镜像了
`prepare_upload` 把 `caller` 放在 `UploadPrepareRequest` 之外的做法。

**Service。** 对外的 `download_route` 签名增加 `show: bool`：
`download_route(&self, session_id: &str, file_id: &str, ttl_secs: Option<u64>, show: bool)`。
在 presign 分支内部，`show` 被透传给 `presign_get`：
```rust
self.cfg.storage
    .presign_get(&handle, PresignGetOptions { ttl_secs: ttl, show }, None)
    .await
```
local/stream 分支在 service 层并不消费 `show`——disposition 是在 HTTP handler 中
施加的——所以 `download_route` 只在 presign 路径上读取 `show`。
`opts.ttl_secs` 取代旧的位置参数 `ttl_secs`。

**Local 实现：** 仅更新签名；函数体不变，仍然返回
`StorageError::Unsupported("local")`。永远不会被调用
（`supports_presign_download = false`）。

**Baas 实现：** 构造 share-link 请求体时，让默认下载路径的 baas 请求与今天
**逐字节**一致——仅当 `show` 为 true 时才包含 `show`：
```rust
let mut body = serde_json::json!({
    "expire_seconds": opts.ttl_secs,
    "operator": operator_str(caller),
});
if opts.show {
    body["show"] = serde_json::Value::Bool(true);
}
```
因此 `?show=true` 请求会产生
`{"expire_seconds":N,"operator":"bcs","show":true}`；缺省或 `false` 则产生
未改变的 `{"expire_seconds":N,"operator":"bcs"}`。

### 3. 能力 + 错误/边界策略 + 测试

**能力。** `StorageCapabilities` 增加 `pub supports_inline_view: bool`。
local 与 baas 都设为 `true`——`local` 通过 stream 路径的 `inline` disposition
实现内联展示，`baas` 通过 share-link 的 `show` 字段实现——所以这是一项与
`supports_presign_download` 不同、真实存在的能力，并非同义词。
未来某个不支持内联的后端应将其设为 `false`。

**对外视图。** `CapabilitiesView` 增加一个短名字段，沿用现有
`presign_download` 的命名风格：
```rust
pub struct CapabilitiesView {
    pub storage: String,
    pub presign_upload: bool,
    pub presign_download: bool,
    pub inline_view: bool,   // 新增
    pub max_size: u64,
}
```
`service.rs:195-202` 从 `self.caps.supports_inline_view` 映射该字段。

**语义。** 该能力_对前端而言是建议性的_（例如显示"查看"vs"下载"按钮）。
service **不**依据它对 `show` 做门禁——无论能力值如何都会尊重 `show`。
现在不增加 service 层的强制校验（YAGNI——两种后端都支持）。

**错误处理。** 不引入新的错误情形：

- `show=foo`（非布尔值）反序列化 → axum 的 `Query` 提取器会以
  400 Bad Request 拒绝，与今天任何其他畸形 query 值的处理一致。
- Baas 的错误继续走现有的 `map_storage_err`。
- 不会因为任一后端上的 `show=true` 而引入 4xx。

**测试。**

- _HTTP/local stream：_ `?show=true` → `Content-Disposition: inline; filename="..."`；
  `?show=false` 与缺省 → `attachment`（与今天回归一致）。
- _HTTP/baas（mock baas）：_ `?show=true` → 302 且 share-link 请求体
  包含 `"show":true`；缺省/`false` → 请求体省略 `show`（与今天逐字节一致）。
- _Shared-file 路由：_ `?show=true&token=...` → inline disposition（local）/
  `show` 被转发（baas）。
- _能力：_ `GET .../files/capabilities` 对两种后端都返回 `inline_view: true`。
- _Service/plugin：_ `download_route` 把 `show` 透传进 `PresignGetOptions`
  （mock storage）；baas 的 `presign_get` 请求体按条件包含 `show`；local
  实现仍返回 `Unsupported`。
- _迁移：_ 将现有的每个 `download_route` / `presign_get` 调用点（含测试）
  更新到新签名。

### 范围之外

MIME 嗅探/猜测、按文件类型的可展示性门禁、针对内联媒体的 Range 请求，以及任何
前端改动。未来某个设置了 `supports_inline_view = false` 的后端，应在其自身实现中
忽略 `show`（service 层的强制校验延后处理）。

## 涉及的组件

| 层 | 文件 | 改动 |
|---|---|---|
| HTTP | `bcs-http/src/routes/session_files.rs` | `DownloadQuery.show`；`download_content`/`shared_file_content` 读取它；`download_file_by_id(show)` + 在 local 分支选择 inline/attachment disposition |
| Service trait | `bcs-service-api/src/application/session_files.rs` | `download_route` 增加 `show: bool` |
| Service impl | `bcs-session-file/src/service.rs` | 把 `show` 透传给 `presign_get` 选项；`capabilities()` 暴露 `inline_view` |
| Plugin API | `bcs-storage-api/src/lib.rs` | `PresignGetOptions`；`presign_get` 签名；`StorageCapabilities.supports_inline_view` |
| 对外 DTO | `bcs-service-api/src/application/session_files.rs` | `CapabilitiesView.inline_view` |
| Baas 插件 | `bcs-storage-baas/src/lib.rs` | `presign_get` 新签名 + share-link 请求体中按条件带 `show`；`supports_inline_view=true` |
| Local 插件 | `bcs-storage-local/src/lib.rs` | `presign_get` 新签名（函数体不变）；`supports_inline_view=true` |

## 为何不选其它方案

- _A —— 直接加 `show: bool` 位置参数：_ 改动最小，但 `presign_get(&handle, ttl, None, true)` 在调用点可读性差，且下一个 presign 旋钮又要再次改动 trait 签名。
- _C —— 完整的 `PresignGetRequest {handle, opts, caller}`：_ 破坏了 trait 现有的位置参数 handle 约定（`get_stream(&handle)`、`delete(&handle)`），收益却很小——`handle` 和 `caller` 作为位置参数已经很好用。

B 与现有的 `UploadPrepareRequest` 先例一致，并且是真正的可扩展而非投机
（`ttl_secs` 是真实存在的，`show` 是第二个选项，不是第一个）。