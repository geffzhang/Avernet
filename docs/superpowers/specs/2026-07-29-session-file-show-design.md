# Session File `show` Parameter — Inline Browser Display

**Date:** 2026-07-29
**Status:** Design, awaiting implementation plan
**Author:** brainstorming session

## Goal

Add a `show` query parameter to the session file content endpoint so that callers
can choose between the browser's default **display** behavior (inline) and the
default **download** behavior (attachment):

```
GET /sessions/{sid}/files/{file_id}/content?show=true   // browser displays inline
GET /sessions/{sid}/files/{file_id}/content?show=false  // browser downloads
GET /sessions/{sid}/files/{file_id}/content             // browser downloads (default)
```

Both the `local` and `baas` storage backends must support it. For `baas`, which
serves downloads via a 302 redirect to a presigned share link, the inline vs.
download behavior is decided by a `show` field on the baas share-link request
body — expose that field all the way back through the storage plugin trait.

Also expose a capability so the frontend can tell whether inline display is
supported.

## Hard invariant

The default path — `show` absent or `show=false` — changes **nothing** about
today's behavior, byte-for-byte:

- Local stream path: identical headers (`Content-Disposition: attachment`), identical bytes.
- Baas client request body: identical JSON to today (`{"expire_seconds","operator"}`, no `show` field).

`show` defaults to `false` everywhere it is threaded.

## Background

All relevant code lives under `src/bcs/crates/`.

**Content endpoint.** Routed at `router.rs:421-429` as
`get(routes::session_files::download_content)`. Handler
`download_content` (`routes/session_files.rs:585-601`) already binds a
`Query<DownloadQuery>` extractor but discards it (`_q`) and always calls
`download_file_by_id(&state, &sid, &file_id, None)`.

**`download_file_by_id`** (`routes/session_files.rs:607-645`) has two branches
driven by the returned `DownloadRoute.presign`:

- _Presign backend (baas):_ `Redirect::to(&ticket.download_url)` — a 302 to the
  baas `share_url`. BCS sets no `Content-Disposition` here; baas controls it
  via the share link.
- _Stream backend (local):_ BCS streams bytes itself and sets
  `Content-Type` (from `file.mime_type`), `Content-Length`, and
  `Content-Disposition: attachment; filename="<file_name>"`.

The same helper is reused by the token-authed shared-file route
`shared_file_content` (`routes/session_files.rs:705-722`).

**`DownloadQuery`** (`routes/session_files.rs:181-187`):
```rust
#[derive(Debug, Deserialize, Default)]
pub struct DownloadQuery {
    #[serde(default)] pub ttl: Option<u64>,
    #[serde(default)] pub token: Option<String>,
}
```

**Service layer.** `SessionFileService::download_route(sid, file_id, ttl_secs)`
(`service.rs:530-571`) calls `StoragePlugin::presign_get` only when
`supports_presign_download` is true (baas); otherwise returns
`DownloadRoute { presign: None }` and the HTTP layer streams via `get_stream`.
The application-facing trait lives at `application/session_files.rs:122-191`.

**Storage plugin trait** (`plugin-api/bcs-storage-api/src/lib.rs:123-149`):
```rust
async fn presign_get(
    &self,
    handle: &StorageHandle,
    ttl_secs: u64,
    caller: Option<&ActorRef>,
) -> Result<PresignGetTicket, StorageError>;
```
`PresignGetTicket { download_url: String, expires_at: u64 }` (same file, 90-94).

`prepare_upload` on the same trait already takes a `UploadPrepareRequest` **struct**
(upstream of `caller`) — that is the precedent we follow for the `presign_get`
options struct.

**`StorageCapabilities`** (`plugin-api/bcs-storage-api/src/lib.rs:23-30`):
```rust
pub struct StorageCapabilities {
    pub supports_presign_put: bool,
    pub supports_presign_download: bool,
    pub supports_stream_put: bool,
    pub supports_stream_get: bool,
    pub max_object_size: u64,
}
```

**Capabilities endpoint.** Routed at `router.rs:389-392`. Handler `capabilities`
(`routes/session_files.rs:564-579`) returns `CapabilitiesView`
(`service-api/bcs-service-api/src/application/session_files.rs:114-120`):
```rust
pub struct CapabilitiesView {
    pub storage: String,
    pub presign_upload: bool,
    pub presign_download: bool,
    pub max_size: u64,
}
```
Populated by `service.rs:195-202`.

**Local impl.** `bcs-storage-local/src/lib.rs:399-406`. `presign_get` returns
`StorageError::Unsupported("local")` and is never invoked
(`supports_presign_download = false`). Local capabilities at
`bcs-storage-local/src/lib.rs:47-53`.

**Baas impl.** `bcs-storage-baas/src/lib.rs:301-316`. POSTs to
`{endpoint}/api/v1/sessions/{tenant}/{session_id}/files/transfers/{transfer_id}/share-link`
with an inline `serde_json::json!` body:
```rust
let body = serde_json::json!({ "expire_seconds": ttl_secs, "operator": operator_str(caller) });
```
Parses `share_url` and `expires_at` out of the `data` envelope. There is **no
typed** share-link request struct today. Baas capabilities at
`bcs-storage-baas/src/lib.rs:31-38` (`presign_download = true`).

The baas share-link API accepts a `show` field in the request body:
```json
{"expire_seconds": 3600, "show": true, "operator": "user@example.com"}
```

## Design

### 1. HTTP layer + local behavior

**`DownloadQuery`** gains one field (default `false`, so absent = download):
```rust
#[derive(Debug, Deserialize, Default)]
pub struct DownloadQuery {
    #[serde(default)] pub ttl: Option<u64>,
    #[serde(default)] pub token: Option<String>,
    #[serde(default)] pub show: bool,
}
```

**`download_content`** stops discarding the query: read `q.show` and pass it to
`download_file_by_id`. **`shared_file_content`** (token-authed, same helper) does
the same — both download paths honor `show`. (`download_content` still passes
`None` for the TTL argument, as today.)

**`download_file_by_id`** gains a `show: bool` param and threads it into both
branches:

- _baas (presign):_ forwards `show` to `download_route` → `presign_get`. Handler
  still 302-redirects to baas's `share_url`; baas controls the inline
  disposition via the share-link `show` field.
- _local (stream):_ the handler picks the disposition itself:
  - `show=true` → `Content-Disposition: inline; filename="<file_name>"`
    (filename kept so "Save As" still has a name).
  - `show=false`/absent → `Content-Disposition: attachment; filename="<file_name>"`
    (today's behavior, byte-identical).
  - `Content-Type` continues to come from `file.mime_type` (already set). Inline
    display of an unknown MIME type falls back to the browser's default — out of
    scope to guess or override MIME.

### 2. Service layer + `presign_get` trait/impls (option B)

**New struct** in `plugin-api/bcs-storage-api/src/lib.rs` (next to
`PresignGetTicket`):
```rust
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct PresignGetOptions {
    pub ttl_secs: u64,
    pub show: bool,
}
```
`Copy` so it is passed by value. Folding the existing `ttl_secs` in (rather than
making a speculative one-field struct) means the struct earns its keep on day
one and future presign options are a non-breaking field add.

**Trait** `presign_get` becomes:
```rust
async fn presign_get(
    &self,
    handle: &StorageHandle,
    opts: PresignGetOptions,
    caller: Option<&ActorRef>,
) -> Result<PresignGetTicket, StorageError>;
```
`caller` stays separate — it is identity/audit, not a presign option, mirroring
how `prepare_upload` keeps `caller` out of `UploadPrepareRequest`.

**Service.** The application-facing `download_route` signature gains
`show: bool`:
`download_route(&self, session_id: &str, file_id: &str, ttl_secs: Option<u64>, show: bool)`.
Inside the presign branch, `show` is threaded into `presign_get`:
```rust
self.cfg.storage
    .presign_get(&handle, PresignGetOptions { ttl_secs: ttl, show }, None)
    .await
```
The local/stream branch does not consume `show` at the service layer —
disposition is applied in the HTTP handler — so `download_route` only reads
`show` on the presign path. `opts.ttl_secs` replaces the old positional
`ttl_secs` argument.

**Local impl:** signature update only; body unchanged, still returns
`StorageError::Unsupported("local")`. Never invoked
(`supports_presign_download = false`).

**Baas impl:** build the share-link body so the default download path's baas
request stays byte-identical to today — include `show` only when true:
```rust
let mut body = serde_json::json!({
    "expire_seconds": opts.ttl_secs,
    "operator": operator_str(caller),
});
if opts.show {
    body["show"] = serde_json::Value::Bool(true);
}
```
The `?show=true` request thus produces
`{"expire_seconds":N,"operator":"bcs","show":true}`; absent or `false` produces
the unchanged `{"expire_seconds":N,"operator":"bcs"}`.

### 3. Capability + error/edge policy + testing

**Capability.** `StorageCapabilities` gains `pub supports_inline_view: bool`.
Both local and baas set it `true` — `local` achieves inline via the stream
path's `inline` disposition, `baas` via the share-link `show` field — so it is
a real, distinct ability from `supports_presign_download`, not a synonym.
A future backend that cannot do inline would set it `false`.

**Wire view.** `CapabilitiesView` gains a short-name field matching the existing
`presign_download` style:
```rust
pub struct CapabilitiesView {
    pub storage: String,
    pub presign_upload: bool,
    pub presign_download: bool,
    pub inline_view: bool,   // new
    pub max_size: u64,
}
```
`service.rs:195-202` maps it from `self.caps.supports_inline_view`.

**Semantics.** The capability is _advisory for the frontend_ (e.g., show a
"view" vs "download" button). The service does **not** gate `show` on it — it
honors `show` regardless of the capability value. We do not add service-layer
enforcement now (YAGNI — both backends support it).

**Error handling.** No new error cases:

- `show=foo` (non-boolean) deserialization → axum's `Query` extractor rejects it
  with 400 Bad Request, the same as any other malformed query value today.
- Baas errors keep flowing through the existing `map_storage_err`.
- No 4xx is introduced for `show=true` on either backend.

**Testing.**

- _HTTP/local stream:_ `?show=true` → `Content-Disposition: inline; filename="..."`;
  `?show=false` and absent → `attachment` (regression-identical to today).
- _HTTP/baas (mocked baas):_ `?show=true` → 302 and the share-link request body
  contains `"show":true`; absent/`false` → body omits `show` (byte-identical to
  today).
- _Shared-file route:_ `?show=true&token=...` → inline disposition (local) /
  `show` forwarded (baas).
- _Capabilities:_ `GET .../files/capabilities` returns `inline_view: true` for
  both backends.
- _Service/plugin:_ `download_route` threads `show` into `PresignGetOptions`
  (mock storage); baas `presign_get` body conditionally includes `show`; local
  impl still `Unsupported`.
- _Migration:_ update every existing `download_route` / `presign_get` call site
  (including tests) to the new signatures.

### Out of scope

MIME sniffing/guessing, per-file-type viewability gating, Range requests for
inline media, and any frontend changes. A future backend that sets
`supports_inline_view = false` is expected to ignore `show` in its own impl
(service-layer enforcement is deferred).

## Components touched

| Layer | File | Change |
|---|---|---|
| HTTP | `bcs-http/src/routes/session_files.rs` | `DownloadQuery.show`; `download_content`/`shared_file_content` read it; `download_file_by_id(show)` + inline/attachment disposition on local branch |
| Service trait | `bcs-service-api/src/application/session_files.rs` | `download_route` gains `show: bool` |
| Service impl | `bcs-session-file/src/service.rs` | thread `show` → `presign_get` options; `capabilities()` surfaces `inline_view` |
| Plugin API | `bcs-storage-api/src/lib.rs` | `PresignGetOptions`; `presign_get` signature; `StorageCapabilities.supports_inline_view` |
| Wire DTO | `bcs-service-api/src/application/session_files.rs` | `CapabilitiesView.inline_view` |
| Baas plugin | `bcs-storage-baas/src/lib.rs` | `presign_get` new sig + conditional `show` in share-link body; `supports_inline_view=true` |
| Local plugin | `bcs-storage-local/src/lib.rs` | `presign_get` new sig (body unchanged); `supports_inline_view=true` |

## Why not the alternatives

- _A — direct `show: bool` positional param:_ smallest diff, but `presign_get(&handle, ttl, None, true)` reads poorly at call sites, and the next presign knob churns the trait signature again.
- _C — full `PresignGetRequest {handle, opts, caller}:_ breaks the trait's positional-handle convention (`get_stream(&handle)`, `delete(&handle)`) for little gain — `handle` and `caller` are comfortably positional already.

B matches the existing `UploadPrepareRequest` precedent and is genuinely
extensible without being speculative (`ttl_secs` is real, `show` is the second
option, not the first).