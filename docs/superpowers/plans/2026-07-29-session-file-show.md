# Session File `show` Parameter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `show` query param to the session-file content endpoint so `?show=true` makes the browser display the file inline (instead of downloading), backed by both `local` and `baas` storage, and expose an `inline_view` capability.

**Architecture:** Thread `show: bool` from the HTTP query through `download_file_by_id` → `SessionFileService::download_route` → `StoragePlugin::presign_get`. Replace `presign_get`'s positional `ttl_secs: u64` with a `PresignGetOptions { ttl_secs, show }` struct (mirrors `UploadPrepareRequest`). Local backend applies inline/attachment `Content-Disposition` itself; baas backend forwards `show` into the share-link request body. Add `supports_inline_view` to `StorageCapabilities` and surface it as `inline_view` in `CapabilitiesView`.

**Tech Stack:** Rust, axum (HTTP), wiremock (baas plugin tests), `FakeStoragePlugin` (HTTP/service test double), serde. Workspace root `src/bcs/`.

## Global Constraints

- **No `cargo fmt`.** Per `src/bcs/CLAUDE.md`: never run `cargo fmt`/`cargo fmt --all`; keep style edits limited to lines that must change. Do not reformat unrelated code.
- **Hard invariant:** `show` absent or `show=false` changes today's behavior byte-for-byte — local: `Content-Disposition: attachment`; baas share-link body omits `show` (still just `{"expire_seconds","operator"}`). `show` defaults to `false` everywhere.
- **No `T | None` style.** Per repo `CLAUDE.md`/`AGENTS.md`: required values stay non-optional. `show` is a plain `bool` (serde default), not `Option<bool>`.
- **Layering.** HTTP routes call `application::*Service`; `CapabilitiesView`/`download_route` trait live in `bcs-service-api::application`. Storage trait/structs live in `plugin-api/bcs-storage-api`. Don't cross layers.
- **Tests run from `src/bcs/`:** `cargo test --package <crate>` (e.g. `bcs-http`, `bcs-storage-baas`, `bcs-session-file`, `bcs-service-api`, `bcs-storage-api`, `bcs-storage-local`).
- **Pre-push:** changes are all under `src/bcs/` → the `src/bcs/` gate fires (BCS unit tests fast-fail + singlebox E2E coverage, 100% HTTP endpoint coverage). Full gate opt-in via `OCB_PRE_PUSH_RUN_CI=1 git push`; default hook is lint-only.

---

## File Structure

| File | Responsibility | Change |
|---|---|---|
| `plugin-api/bcs-storage-api/src/lib.rs` | Storage trait + DTOs | Add `PresignGetOptions`; add `supports_inline_view` to `StorageCapabilities`; change `presign_get` signature |
| `plugin-api/bcs-storage-api/src/fake.rs` | `FakeStoragePlugin` test double | Record last `PresignGetOptions`; update impl signature; add accessor |
| `plugins/bcs-storage-baas/src/lib.rs` | Baas plugin | New `presign_get` sig; conditional `show` in share-link body; `supports_inline_view=true` |
| `plugins/bcs-storage-baas/tests/presign.rs` | Baas presign wiremock tests | Migrate callers; add `show` body tests |
| `plugins/bcs-storage-local/src/lib.rs` | Local plugin | New `presign_get` sig (body unchanged); `supports_inline_view=true`; migrate unit-test caller |
| `service-api/bcs-service-api/src/application/session_files.rs` | App-layer traits/DTOs | `CapabilitiesView.inline_view`; `download_route` gains `show: bool` |
| `services/bcs-session-file/src/service.rs` | `SessionFileService` impl + tests | `download_route(show)` threads into `presign_get`; `capabilities()` surfaces `inline_view`; migrate test caps + callers; new forwarding test |
| `services/bcs-session-file/src/noop.rs` | Noop impl | `download_route` new sig |
| `adapters/http/bcs-http/src/routes/session_files.rs` | HTTP routes + tests | `DownloadQuery.show`; `download_file_by_id(show)` + inline/attachment; `download_content`/`shared_file_content` forward `show`; migrate test caps; new HTTP tests |

---

### Task 1: `supports_inline_view` capability + `CapabilitiesView.inline_view`

Adds a new capability field and surfaces it on the wire. Default download behavior is untouched. This task compiles and tests green on its own.

**Files:**
- Modify: `src/bcs/crates/plugin-api/bcs-storage-api/src/lib.rs:24-30` (`StorageCapabilities`)
- Modify: `src/bcs/crates/plugins/bcs-storage-baas/src/lib.rs:32-38` (caps literal)
- Modify: `src/bcs/crates/plugins/bcs-storage-local/src/lib.rs:47-53` (caps literal)
- Modify: `src/bcs/crates/plugin-api/bcs-storage-api/src/fake.rs:119-125` (test `caps()`)
- Modify: `src/bcs/crates/services/bcs-session-file/src/service.rs:1008-1016` (`local_caps`), `:1018-1026` (`presign_caps`), `:1132-1138` (unbounded caps), `:1748-1755` (`PresignSizelessComplete::capabilities`)
- Modify: `src/bcs/crates/adapters/http/bcs-http/src/routes/session_files.rs:884-892` (`local_caps`)
- Modify: `src/bcs/crates/service-api/bcs-service-api/src/application/session_files.rs:114-120` (`CapabilitiesView`)
- Modify: `src/bcs/crates/services/bcs-session-file/src/service.rs:195-202` (`capabilities()` impl)
- Test: `src/bcs/crates/services/bcs-session-file/src/service.rs` (extend `capabilities_reports_backend_and_max_size`, ~1534-1542)
- Test: `src/bcs/crates/adapters/http/bcs-http/src/routes/session_files.rs` (extend `capabilities_route_not_shadowed_by_file_id`, 1264-1283)

**Interfaces:**
- Produces: `StorageCapabilities.supports_inline_view: bool` (default `false` via `#[derive(Default)]`), set `true` by both real plugins. `CapabilitiesView.inline_view: bool` mapped from `self.caps.supports_inline_view`.

- [ ] **Step 1: Write the failing service test**

In `services/bcs-session-file/src/service.rs`, find `capabilities_reports_backend_and_max_size` (~line 1534). Add an assertion that `inline_view` is present and `true` for the local-backed service. (The test already builds a service via `build_svc(local_caps())` or similar; mirror its existing assertions.)

```rust
// inside capabilities_reports_backend_and_max_size, after the existing asserts:
assert_eq!(
    c.inline_view,
    true,
    "local backend must advertise inline_view support"
);
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd src/bcs && cargo test --package bcs-session-file capabilities_reports_backend_and_max_size`
Expected: FAIL — `no field 'inline_view' on type 'CapabilitiesView'` (compile error).

- [ ] **Step 3: Add the capability field to `StorageCapabilities`**

In `plugin-api/bcs-storage-api/src/lib.rs`, add `supports_inline_view` after `supports_stream_get` (keeps field order tidy; `Default` derive already present gives `false`):

```rust
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, Default)]
pub struct StorageCapabilities {
    pub supports_presign_put: bool,
    pub supports_presign_download: bool,
    pub supports_stream_put: bool,
    pub supports_stream_get: bool,
    pub supports_inline_view: bool,
    pub max_object_size: u64,
}
```

- [ ] **Step 4: Set `supports_inline_view: true` at every caps construction site**

Run a grep to find all literal construction sites so none is missed:
```bash
cd src/bcs && grep -rn "StorageCapabilities {" --include=*.rs crates/
```

At each hit, add `supports_inline_view: true,` (real backends). The known sites and their exact inserts:

`bcs-storage-baas/src/lib.rs` (the `let caps = StorageCapabilities { ... }` block around line 32):
```rust
let caps = StorageCapabilities {
    supports_presign_put: true,
    supports_presign_download: true,
    supports_stream_put: true,
    supports_stream_get: true,
    supports_inline_view: true,
    max_object_size,
};
```

`bcs-storage-local/src/lib.rs` (around line 47):
```rust
let caps = StorageCapabilities {
    supports_presign_put: false,
    supports_presign_download: false,
    supports_stream_put: true,
    supports_stream_get: true,
    supports_inline_view: true,
    max_object_size: cfg.max_object_size,
};
```

`fake.rs` test `caps()` (~line 119), and the four `service.rs` test caps (`local_caps`, `presign_caps`, unbounded, `PresignSizelessComplete::capabilities`), and the HTTP `local_caps` (~line 884): add `supports_inline_view: true,` to each. (Test doubles advertise `true` so capability assertions pass; `Default::default()` callers, if any, stay `false` — that's fine.)

- [ ] **Step 5: Add `inline_view` to `CapabilitiesView` and map it**

In `service-api/bcs-service-api/src/application/session_files.rs`:
```rust
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct CapabilitiesView {
    pub storage: String,
    pub presign_upload: bool,
    pub presign_download: bool,
    pub inline_view: bool,
    pub max_size: u64,
}
```

In `services/bcs-session-file/src/service.rs` `capabilities()` impl (~line 195):
```rust
async fn capabilities(&self) -> CapabilitiesView {
    CapabilitiesView {
        storage: self.cfg.storage.backend_name().to_string(),
        presign_upload: self.caps.supports_presign_put,
        presign_download: self.caps.supports_presign_download,
        inline_view: self.caps.supports_inline_view,
        max_size: self.max_size(),
    }
}
```

- [ ] **Step 6: Extend the HTTP capabilities test**

In `routes/session_files.rs` `capabilities_route_not_shadowed_by_file_id` (~line 1264), after the existing `presign_upload` assertion, add:
```rust
assert_eq!(
    body.get("inline_view").and_then(|v| v.as_bool()),
    Some(true),
    "expected inline_view in capabilities: {body:?}"
);
```

- [ ] **Step 7: Run affected tests to verify they pass**

Run:
```bash
cd src/bcs && cargo test --package bcs-session-file capabilities_reports_backend_and_max_size
cd src/bcs && cargo test --package bcs-http capabilities_route_not_shadowed_by_file_id
cd src/bcs && cargo test --package bcs-storage-api fake
```
Expected: PASS.

- [ ] **Step 8: Compile the whole workspace to catch any missed caps site**

Run: `cd src/bcs && cargo build --workspace`
Expected: PASS. If a `StorageCapabilities { ... }` literal is missing the new field, the compiler names the file — add `supports_inline_view: true,` there.

- [ ] **Step 9: Commit**

```bash
git add -A && git commit -m "feat(bcs): add supports_inline_view capability + CapabilitiesView.inline_view"
```

---

### Task 2: Introduce `PresignGetOptions` and migrate `presign_get` signature (show=false)

Pure refactor: replace the positional `ttl_secs: u64` arg with `PresignGetOptions { ttl_secs, show }`. `show` is `false` at every call site, so baas request bodies are byte-identical to today. No new behavior yet — existing tests are the regression gate.

**Files:**
- Modify: `src/bcs/crates/plugin-api/bcs-storage-api/src/lib.rs:140-145` (trait decl) + new struct near `UploadPrepareRequest` (32-39)
- Modify: `src/bcs/crates/plugin-api/bcs-storage-api/src/fake.rs:97` (impl)
- Modify: `src/bcs/crates/plugins/bcs-storage-baas/src/lib.rs:301` (impl)
- Modify: `src/bcs/crates/plugins/bcs-storage-local/src/lib.rs:399` (impl)
- Modify: `src/bcs/crates/services/bcs-session-file/src/service.rs:550` (production caller), `:1718-1724` (`FailingDeleteStorage` delegate), `:1801-1811` (`PresignSizelessComplete` impl)
- Modify: `src/bcs/crates/plugins/bcs-storage-baas/tests/presign.rs:28,40,53-54,76` (callers)
- Modify: `src/bcs/crates/plugins/bcs-storage-local/src/lib.rs:525` (unit-test caller)

**Interfaces:**
- Produces: `pub struct PresignGetOptions { pub ttl_secs: u64, pub show: bool }` (derives `Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize`).
- Produces: new trait signature `async fn presign_get(&self, handle: &StorageHandle, opts: PresignGetOptions, caller: Option<&ActorRef>) -> Result<PresignGetTicket, StorageError>;`

- [ ] **Step 1: Add the `PresignGetOptions` struct**

In `plugin-api/bcs-storage-api/src/lib.rs`, add it just before the `StoragePlugin` trait (near `PresignGetTicket`/`UploadPrepareRequest`). Mirror `UploadPrepareRequest`'s derives plus `Copy`:

```rust
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct PresignGetOptions {
    pub ttl_secs: u64,
    pub show: bool,
}
```

- [ ] **Step 2: Change the trait signature**

In the same file (lines 140-145):
```rust
async fn presign_get(
    &self,
    handle: &StorageHandle,
    opts: PresignGetOptions,
    caller: Option<&ActorRef>,
) -> Result<PresignGetTicket, StorageError>;
```

- [ ] **Step 3: Update the four trait impls**

`fake.rs:97` — rename param and use `opts.ttl_secs`:
```rust
async fn presign_get(&self, handle: &StorageHandle, opts: PresignGetOptions, _caller: Option<&crate::ActorRef>) -> Result<PresignGetTicket, StorageError> {
    Ok(PresignGetTicket {
        download_url: format!("fake://{}", handle.key),
        expires_at: opts.ttl_secs,
    })
}
```

`bcs-storage-local/src/lib.rs:399` — signature only (body stays `Unsupported`):
```rust
async fn presign_get(
    &self,
    _handle: &StorageHandle,
    _opts: PresignGetOptions,
    _caller: Option<&ActorRef>,
) -> Result<PresignGetTicket, StorageError> {
    Err(StorageError::Unsupported("local"))
}
```

`bcs-storage-baas/src/lib.rs:301` — change the param to `opts: PresignGetOptions` and use `opts.ttl_secs` in the body. Keep the body field set exactly as today (no `show` yet — Task 3 adds it):
```rust
async fn presign_get(&self, handle: &StorageHandle, opts: PresignGetOptions, caller: Option<&ActorRef>) -> Result<PresignGetTicket, StorageError> {
    let ready: BaasReadyHandle = serde_json::from_value(handle.backend_handle.clone())
        .or_else(|_| serde_json::from_value::<BaasPendingHandle>(handle.backend_handle.clone())
                    .map(|p| BaasReadyHandle { transfer_id: p.transfer_id }))
        .map_err(|e| StorageError::Backend(e.into()))?;
    let session_id = session_id_from_key(&handle.key);
    let base = self.base_for_session(session_id);
    let body = serde_json::json!({ "expire_seconds": opts.ttl_secs, "operator": operator_str(caller) });
    let resp = self.auth(self.http.post(format!("{base}/transfers/{}/share-link",
                percent_encode_path(&ready.transfer_id))).json(&body)).send().await
        .map_err(|e| StorageError::Backend(e.into()))?;
    let data = baas_data(resp).await?;
    let share_url = data["share_url"].as_str().ok_or_else(|| bad("missing share_url"))?.to_string();
    let expires_at = parse_iso_to_unix(data["expires_at"].as_str().unwrap_or("")).unwrap_or_else(|| now_unix_secs().saturating_add(opts.ttl_secs));
    Ok(PresignGetTicket { download_url: share_url, expires_at })
}
```

- [ ] **Step 4: Update the two in-crate trait impls in `service.rs`**

`FailingDeleteStorage` delegate (~line 1718): change the impl signature to `opts: PresignGetOptions` and forward `opts`:
```rust
async fn presign_get(&self, handle: &StorageHandle, opts: PresignGetOptions, caller: Option<&ActorRef>) -> Result<PresignGetTicket, StorageError> {
    self.inner.presign_get(handle, opts, caller).await
}
```

`PresignSizelessComplete` impl (~line 1801): change signature to `opts: PresignGetOptions`; if its body references `ttl_secs` by name, use `opts.ttl_secs`. (Read the body in-file first; only change what the rename requires.)

- [ ] **Step 5: Update the production caller in `service.rs`**

At `service.rs:550` (inside `download_route`), replace:
```rust
.presign_get(&handle, ttl, None).await
```
with:
```rust
.presign_get(&handle, PresignGetOptions { ttl_secs: ttl, show: false }, None).await
```
(`show: false` keeps today's behavior. Task 4 wires the real `show`.)

- [ ] **Step 6: Update the test callers in `presign.rs`**

In `bcs-storage-baas/tests/presign.rs`, migrate every `p.presign_get(...)` call. Examples at the known lines:

Line 28 / 40 / 53-54 (caller `None`):
```rust
p.presign_get(&ready_handle(...), PresignGetOptions { ttl_secs: 3600, show: false }, None).await
```

Line 76 (with `caller`):
```rust
let ticket = p.presign_get(&h, PresignGetOptions { ttl_secs: 3600, show: false }, caller).await.unwrap();
```

Add the import at the top of the file:
```rust
use bcs_storage_api::PresignGetOptions;
```
(Check the existing `use bcs_storage_api::{...}` line and add `PresignGetOptions` to it rather than duplicating.)

- [ ] **Step 7: Update the local unit-test caller**

In `bcs-storage-local/src/lib.rs:525`:
```rust
p.presign_get(&h, PresignGetOptions { ttl_secs: 300, show: false }, None)
```
Add `PresignGetOptions` to the file's existing `use bcs_storage_api::{...}` import (or qualify it as `bcs_storage_api::PresignGetOptions`).

- [ ] **Step 8: Build the workspace to confirm signature migration is complete**

Run: `cd src/bcs && cargo build --workspace`
Expected: PASS. The compiler flags any caller/impl still using the old signature — fix each by name.

- [ ] **Step 9: Run the presign suites as the regression gate**

Run:
```bash
cd src/bcs && cargo test --package bcs-storage-baas presign
cd src/bcs && cargo test --package bcs-storage-local
cd src/bcs && cargo test --package bcs-session-file download_route
```
Expected: PASS (behavior unchanged; `show: false` everywhere).

- [ ] **Step 10: Commit**

```bash
git add -A && git commit -m "refactor(bcs): presign_get takes PresignGetOptions struct (show=false, no behavior change)"
```

---

### Task 3: Baas `presign_get` honors `show` (conditional share-link body field)

Makes the baas backend forward `show` into the share-link request body, only when `true` (keeps the default-request byte-identical).

**Files:**
- Test: `src/bcs/crates/plugins/bcs-storage-baas/tests/presign.rs` (add two tests)
- Modify: `src/bcs/crates/plugins/bcs-storage-baas/src/lib.rs:301` (the share-link body construction)

**Interfaces:**
- Consumes: `PresignGetOptions { ttl_secs, show }` from Task 2.
- Produces: baas share-link body includes `"show": true` only when `opts.show`.

- [ ] **Step 1: Write the failing test — `show=true` posts `show` in the body**

Append to `bcs-storage-baas/tests/presign.rs`:
```rust
#[tokio::test]
async fn presign_get_includes_show_when_true() {
    let server = MockServer::start().await;
    Mock::given(method("POST"))
        .and(path("/api/v1/sessions/t/sid/files/transfers/tid/share-link"))
        .and(body_partial_json(json!({ "show": true })))
        .respond_with(ResponseTemplate::new(200).set_body_json(json!({
            "code":0,"data":{
                "share_url":"https://oss/get?sig=S","transfer_id":"tid","expires_at":"2026-07-23T12:00:00Z"
            }
        })))
        .mount(&server).await;

    let p = plugin(server.uri());
    let h = ready_handle("session-files/prod/sid/fid/f", "tid");
    let ticket = p.presign_get(&h, PresignGetOptions { ttl_secs: 3600, show: true }, None).await.unwrap();
    assert_eq!(ticket.download_url, "https://oss/get?sig=S");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd src/bcs && cargo test --package bcs-storage-baas presign_get_includes_show_when_true`
Expected: FAIL — the mock's `body_partial_json({"show": true})` matcher doesn't match today's body (no `show`), so wiremock returns 404 and `presign_get` errors.

- [ ] **Step 3: Write the regression test — `show=false` omits `show` from the body**

Append to `presign.rs`. Two mocks: mock A asserts the `show:true` shape is *never* matched (`.expect(0)`); mock B matches the path + `expire_seconds` and responds 200. For a `show=false` request, A won't match (body has no `show`), B will:
```rust
#[tokio::test]
async fn presign_get_omits_show_when_false() {
    let server = MockServer::start().await;
    // Never matched: a body containing "show" must not be posted when show=false.
    Mock::given(method("POST"))
        .and(path("/api/v1/sessions/t/sid/files/transfers/tid/share-link"))
        .and(body_partial_json(json!({ "show": true })))
        .expect(0)
        .mount(&server).await;
    Mock::given(method("POST"))
        .and(path("/api/v1/sessions/t/sid/files/transfers/tid/share-link"))
        .and(body_partial_json(json!({ "expire_seconds": 3600 })))
        .respond_with(ResponseTemplate::new(200).set_body_json(json!({
            "code":0,"data":{
                "share_url":"https://oss/get?sig=N","transfer_id":"tid","expires_at":"2026-07-23T12:00:00Z"
            }
        })))
        .expect(1)
        .mount(&server).await;

    let p = plugin(server.uri());
    let h = ready_handle("session-files/prod/sid/fid/f", "tid");
    let ticket = p.presign_get(&h, PresignGetOptions { ttl_secs: 3600, show: false }, None).await.unwrap();
    assert_eq!(ticket.download_url, "https://oss/get?sig=N");
}
```

- [ ] **Step 4: Implement — conditionally add `show` to the body**

In `bcs-storage-baas/src/lib.rs` `presign_get`, replace the single-line `let body = ...` with a mutable body that adds `show` only when true:
```rust
let mut body = serde_json::json!({
    "expire_seconds": opts.ttl_secs,
    "operator": operator_str(caller),
});
if opts.show {
    body["show"] = serde_json::Value::Bool(true);
}
```
Leave the rest of the function (the POST, `baas_data`, parsing) unchanged.

- [ ] **Step 5: Run both new tests + the existing presign suite**

Run:
```bash
cd src/bcs && cargo test --package bcs-storage-baas presign_get_includes_show_when_true
cd src/bcs && cargo test --package bcs-storage-baas presign_get_omits_show_when_false
cd src/bcs && cargo test --package bcs-storage-baas presign
```
Expected: PASS. The existing `presign_passes_caller_as_operator` test still passes because it uses `show: false` (Task 2 migration) and its `body_partial_json({"operator": ...})` matcher matches the operator field regardless of the absent `show`.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat(bcs-storage-baas): presign_get forwards show into share-link body when true"
```

---

### Task 4: `download_route` threads `show` into `presign_get`; add fake recording

Extends the service-layer `download_route` to accept `show` and forward it into `PresignGetOptions`. The HTTP layer is updated only enough to keep compiling (passes `false`); real HTTP wiring is Task 5. Adds a recording field on `FakeStoragePlugin` so the forwarding is observable in tests.

**Files:**
- Modify: `src/bcs/crates/plugin-api/bcs-storage-api/src/fake.rs` (record opts + accessor)
- Modify: `src/bcs/crates/service-api/bcs-service-api/src/application/session_files.rs:160-166` (`download_route` trait decl)
- Modify: `src/bcs/crates/services/bcs-session-file/src/service.rs:530-535` (impl sig) + `:550` (forward `show`) + noop `:88-95` + test callers `:1340,1351,1367,1378`
- Modify: `src/bcs/crates/adapters/http/bcs-http/src/routes/session_files.rs:616` (production caller — pass `false` for now)
- Test: `src/bcs/crates/services/bcs-session-file/src/service.rs` (new forwarding test near 1344)

**Interfaces:**
- Consumes: `PresignGetOptions` (Task 2).
- Produces: `download_route(&self, session_id, file_id, ttl_secs: Option<u64>, show: bool)`. `FakeStoragePlugin::last_presign_opts(&self) -> Option<PresignGetOptions>`.

- [ ] **Step 1: Add recording + accessor to `FakeStoragePlugin`**

In `fake.rs`, add a field and setter/accessor. `PresignGetOptions` is `Copy` (from Task 2), so store `Option<PresignGetOptions>`:

```rust
#[derive(Clone, Default)]
pub struct FakeStoragePlugin {
    caps: StorageCapabilities,
    objects: Arc<Mutex<HashMap<String, Bytes>>>,
    staging: Arc<Mutex<HashMap<String, HashMap<Option<u16>, Bytes>>>>,
    last_presign_opts: Arc<Mutex<Option<PresignGetOptions>>>,
}
```

Add the import: include `PresignGetOptions` in the `use crate::{ ... }` list at the top.

Add an accessor after `new`:
```rust
impl FakeStoragePlugin {
    pub fn new(caps: StorageCapabilities) -> Self {
        Self { caps, ..Default::default() }
    }

    /// Last `PresignGetOptions` passed to `presign_get`, if any.
    pub fn last_presign_opts(&self) -> Option<PresignGetOptions> {
        *self.last_presign_opts.lock().unwrap()
    }
}
```

In `presign_get`, record the opts before returning (the impl signature is already `opts: PresignGetOptions` from Task 2):
```rust
async fn presign_get(&self, handle: &StorageHandle, opts: PresignGetOptions, _caller: Option<&crate::ActorRef>) -> Result<PresignGetTicket, StorageError> {
    *self.last_presign_opts.lock().unwrap() = Some(opts);
    Ok(PresignGetTicket {
        download_url: format!("fake://{}", handle.key),
        expires_at: opts.ttl_secs,
    })
}
```

- [ ] **Step 2: Write the failing forwarding test**

In `services/bcs-session-file/src/service.rs`, near the existing `download_route_presign_backend_returns_ticket` (~line 1344). The test uses the `presign_caps()` service whose storage is a `FakeStoragePlugin`; reach it via the service's storage handle. Look at how `download_route_presign_backend_returns_ticket` builds its service (`build_svc(presign_caps())`) and mirror it. To read the recorded opts, hold a clone of the `Arc<FakeStoragePlugin>` (the `build_svc` helper constructs it internally — if it doesn't expose the handle, construct the service inline in this test the same way `build_svc` does, keeping your own `Arc<FakeStoragePlugin>` clone):

```rust
#[tokio::test]
async fn download_route_presign_forwards_show_true() {
    // Build a presign-backed service and keep a handle to its FakeStoragePlugin
    // so we can inspect the opts reached presign_get. Mirror build_svc(presign_caps())
    // but retain the storage clone.
    let storage = Arc::new(FakeStoragePlugin::new(presign_caps()));
    let svc = build_svc_with_storage(storage.clone()); // see note below
    let r = seed_ready_file(&svc).await; // reuse the seeding helper the neighboring
                                         // download_route tests use (e.g. the one at ~1344)

    let (_file, route) = svc
        .download_route("g1:abcd1234", &r.file.file_id, None, true)
        .await
        .unwrap();
    let ticket = route.presign.expect("presign backend yields a ticket");
    assert!(ticket.download_url.starts_with("fake://"));
    let opts = storage.last_presign_opts().expect("presign_get was called");
    assert_eq!(opts.show, true, "download_route(show=true) must forward show");
    assert_eq!(opts.ttl_secs, 7777, "ttl must fall back to share_link_ttl");
}
```

NOTE for the implementer: `build_svc_with_storage` may not exist. Read `build_svc` (around line 1035) — if it builds the `FakeStoragePlugin` internally and does not expose it, either (a) add a thin `build_svc_with_storage(storage: Arc<FakeStoragePlugin>)` helper next to `build_svc` that constructs the service from the provided storage, or (b) inline the exact construction `build_svc` does but with your own `storage` clone. Pick whichever matches the existing helpers' style. `seed_ready_file` / `r` should reuse the same seeding the neighboring `download_route` tests use — copy that setup verbatim rather than inventing new fixtures. Also add a second assertion test `download_route_presign_forwards_show_false` identical but with `false` and `assert_eq!(opts.show, false)`.

- [ ] **Step 3: Run test to verify it fails**

Run: `cd src/bcs && cargo test --package bcs-session-file download_route_presign_forwards_show`
Expected: FAIL — compile error: `download_route` takes 4 args, not 5 (no `show`).

- [ ] **Step 4: Change `download_route` signature — trait decl + impl + noop**

Trait decl in `application/session_files.rs:160-166`:
```rust
async fn download_route(
    &self,
    session_id: &str,
    file_id: &str,
    ttl_secs: Option<u64>,
    show: bool,
) -> Result<(SessionFile, DownloadRoute), SessionFileUseCaseError>;
```

Impl in `service.rs:530-535`:
```rust
async fn download_route(
    &self,
    session_id: &str,
    file_id: &str,
    ttl_secs: Option<u64>,
    show: bool,
) -> Result<(SessionFile, DownloadRoute), SessionFileUseCaseError> {
```

Noop in `noop.rs:88-95`:
```rust
async fn download_route(
    &self,
    _session_id: &str,
    _file_id: &str,
    _ttl_secs: Option<u64>,
    _show: bool,
) -> Result<(SessionFile, DownloadRoute), SessionFileUseCaseError> {
    Err(SessionFileUseCaseError::Internal("noop".into()))
}
```
(Keep the noop's existing body — only add the `_show: bool` param. Read the current body and preserve it.)

- [ ] **Step 5: Forward `show` into `presign_get` in the impl**

In `service.rs` `download_route` body, replace the Task-2 caller:
```rust
.presign_get(&handle, PresignGetOptions { ttl_secs: ttl, show: false }, None).await
```
with:
```rust
.presign_get(&handle, PresignGetOptions { ttl_secs: ttl, show }, None).await
```
(Ensure `PresignGetOptions` is imported in `service.rs` — `use bcs_storage_api::...` or qualified path. Check the existing imports.)

- [ ] **Step 6: Update the 4 service-test callers**

In `service.rs` at lines ~1340, 1351, 1367, 1378, add `, false` to each `download_route(...)` call (these tests don't care about `show`; `false` preserves their behavior). Example:
```rust
s.download_route("g1:abcd1234", &r.file.file_id, None, false).await.unwrap()
```
(For the `unwrap_err` case at ~1378, also add `, false`.)

- [ ] **Step 7: Update the production caller in the HTTP layer (pass `false` for now)**

In `routes/session_files.rs` `download_file_by_id` (~line 614), change:
```rust
.download_route(sid, file_id, ttl).await
```
to:
```rust
.download_route(sid, file_id, ttl, false).await
```
(Task 5 replaces this `false` with the real `show`.)

- [ ] **Step 8: Run the forwarding tests + the existing download_route suite**

Run:
```bash
cd src/bcs && cargo test --package bcs-session-file download_route
cd src/bcs && cargo build --workspace
```
Expected: PASS. The build confirms the HTTP caller compiles.

- [ ] **Step 9: Commit**

```bash
git add -A && git commit -m "feat(bcs-session-file): download_route forwards show into PresignGetOptions"
```

---

### Task 5: HTTP `show` query param — inline disposition (local) + forwarding (both paths)

Wires the `show` query param end-to-end at the HTTP layer: `DownloadQuery.show`, `download_file_by_id(show)`, local branch picks `inline` vs `attachment`, and both `download_content` / `shared_file_content` forward `q.show`.

**Files:**
- Modify: `src/bcs/crates/adapters/http/bcs-http/src/routes/session_files.rs:181-187` (`DownloadQuery`)
- Modify: `src/bcs/crates/adapters/http/bcs-http/src/routes/session_files.rs:585-601` (`download_content`)
- Modify: `src/bcs/crates/adapters/http/bcs-http/src/routes/session_files.rs:607-645` (`download_file_by_id`)
- Modify: `src/bcs/crates/adapters/http/bcs-http/src/routes/session_files.rs:705-722` (`shared_file_content`)
- Test: `src/bcs/crates/adapters/http/bcs-http/src/routes/session_files.rs` (new tests near 1484)

**Interfaces:**
- Consumes: `download_route(..., show: bool)` (Task 4), `DownloadQuery`.
- Produces: `GET .../content?show=true` → inline `Content-Disposition` (local) / `show` forwarded to baas (presign). Absent or `false` → today's attachment/download.

- [ ] **Step 1: Write the failing test — local `?show=true` → inline**

Add near `local_download_streams_with_content_disposition` (~line 1484):
```rust
#[tokio::test]
async fn local_download_show_true_uses_inline_disposition() {
    let app = build_test_app().await;
    let (file_id, _) = upload_complete(&app, "view.txt", b"abc").await;
    let uri = format!("/sessions/{}/files/{}/content?show=true", app.sid, file_id);
    let req = auth_request(Method::GET, &uri, &app.bot_a_token, None);
    let (status, _body, cd) = send(&app, req).await;
    assert_eq!(status, StatusCode::OK);
    let cd = cd.expect("content-disposition header");
    let s = cd.to_str().unwrap();
    assert!(s.contains("inline"), "expected inline disposition for show=true, got: {s}");
    assert!(s.contains("view.txt"), "filename must still be present: {s}");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd src/bcs && cargo test --package bcs-http local_download_show_true_uses_inline_disposition`
Expected: FAIL — `?show=true` is parsed but ignored today, so the disposition is still `attachment`; `assert!(s.contains("inline"))` fails.

- [ ] **Step 3: Add `show` to `DownloadQuery`**

In `routes/session_files.rs` (line 181):
```rust
#[derive(Debug, Deserialize, Default)]
pub struct DownloadQuery {
    #[serde(default)]
    pub ttl: Option<u64>,
    #[serde(default)]
    pub token: Option<String>,
    #[serde(default)]
    pub show: bool,
}
```
(`bool` + `#[serde(default)]` → absent parses as `false`. Non-boolean like `show=foo` → axum `Query` rejects with 400, same as any malformed query value today.)

- [ ] **Step 4: Add `show` to `download_file_by_id` and pick inline/attachment on the local branch**

Change the signature and the local disposition. The presign branch just threads `show` into `download_route` (the handler still 302-redirects; baas controls disposition via the share-link):
```rust
async fn download_file_by_id(
    state: &HttpAppState,
    sid: &str,
    file_id: &str,
    ttl: Option<u64>,
    show: bool,
) -> Response {
    match state.services.session_files
        .download_route(sid, file_id, ttl, show).await
    {
        Ok((file, route)) => match route.presign {
            Some(ticket) => Redirect::to(&ticket.download_url).into_response(),
            None => match state.services.session_files.get_stream(sid, file_id).await {
                Ok((_f, stream)) => {
                    let mut h = HeaderMap::new();
                    if let Ok(v) = file.mime_type.parse() {
                        h.insert(header::CONTENT_TYPE, v);
                    }
                    if let Ok(v) = file.size.to_string().parse() {
                        h.insert(header::CONTENT_LENGTH, v);
                    }
                    let disposition = if show { "inline" } else { "attachment" };
                    if let Ok(v) = format!(
                        "{}; filename=\"{}\"",
                        disposition,
                        file.file_name.replace('"', "\\\"")
                    ).parse() {
                        h.insert(header::CONTENT_DISPOSITION, v);
                    }
                    (h, Body::from_stream(stream)).into_response()
                }
                Err(e) => err_to_response(e),
            },
        },
        Err(e) => err_to_response(e),
    }
}
```
(Read the current `download_file_by_id` body and preserve any header logic not shown — only the signature, the `download_route` call, and the disposition string change.)

- [ ] **Step 5: Forward `q.show` from `download_content` and `shared_file_content`**

`download_content` (~line 585): change `Query(_q)` to `Query(q)` and pass `q.show`:
```rust
Query(q): Query<DownloadQuery>,
) -> Response {
    // ... caller resolution + member check unchanged ...
    download_file_by_id(&state, &sid, &file_id, None, q.show).await
}
```
(Preserve the caller-resolution and member-check code; only the binding name `_q`→`q` and the final call change. TTL stays `None`.)

`shared_file_content` (~line 705): pass `q.show`:
```rust
download_file_by_id(&state, &sid_owned, &fid, None, q.show).await
```

- [ ] **Step 6: Write the regression test — local `?show=false` and absent still attach**

Add:
```rust
#[tokio::test]
async fn local_download_show_false_and_absent_still_attach() {
    let app = build_test_app().await;
    let (file_id, _) = upload_complete(&app, "dl.txt", b"abc").await;
    for q in &["?show=false", ""] {
        let uri = format!("/sessions/{}/files/{}/content{}", app.sid, file_id, q);
        let req = auth_request(Method::GET, &uri, &app.bot_a_token, None);
        let (status, _body, cd) = send(&app, req).await;
        assert_eq!(status, StatusCode::OK);
        let s = cd.expect("content-disposition").to_str().unwrap().to_string();
        assert!(s.contains("attachment"), "absent/show=false must attach: {s}");
        assert!(s.contains("dl.txt"), "cd: {s}");
    }
}
```

- [ ] **Step 7: Write the shared-file `?show=true` test**

Add (mirroring `share_mint_then_consume` ~line 1322):
```rust
#[tokio::test]
async fn shared_file_show_true_uses_inline_disposition() {
    let app = build_test_app().await;
    // Mint a share token the same way share_mint_then_consume does; copy its
    // minting steps verbatim rather than reinventing them.
    let token = /* mint token for a file "share.txt" as in share_mint_then_consume */;
    let content_uri = format!("/sessions/shared-file/content?token={}&show=true", token);
    let req = auth_request(Method::GET, &content_uri, &app.bot_a_token, None);
    let (status, _body, cd) = send(&app, req).await;
    assert_eq!(status, StatusCode::OK);
    let s = cd.expect("content-disposition").to_str().unwrap();
    assert!(s.contains("inline"), "shared-file show=true must be inline: {s}");
    assert!(s.contains("share.txt"), "cd: {s}");
}
```
NOTE: copy the token-minting steps from `share_mint_then_consume` (lines ~1322-1356) verbatim — do not paraphrase. Only the request URI adds `&show=true` and the assertions change to `inline`.

- [ ] **Step 8: Run all new and existing HTTP download tests**

Run:
```bash
cd src/bcs && cargo test --package bcs-http local_download_show_true_uses_inline_disposition
cd src/bcs && cargo test --package bcs-http local_download_show_false_and_absent_still_attach
cd src/bcs && cargo test --package bcs-http shared_file_show_true_uses_inline_disposition
cd src/bcs && cargo test --package bcs-http session_files
```
Expected: PASS. The existing `local_download_streams_with_content_disposition`, `three_stage_upload_complete_download_roundtrip`, and `share_mint_then_consume` tests still pass (they request without `show` → `attachment`, unchanged).

- [ ] **Step 9: Commit**

```bash
git add -A && git commit -m "feat(bcs-http): ?show=true on file content endpoint selects inline disposition"
```

---

## Self-Review

**Spec coverage** (against `docs/superpowers/specs/2026-07-29-session-file-show-design.md`):
- `DownloadQuery.show` + `download_content`/`shared_file_content` forward it → Task 5.
- `download_file_by_id(show)` + local inline/attachment → Task 5.
- `PresignGetOptions` struct → Task 2.
- `presign_get` signature (option B), `caller` separate → Task 2.
- `download_route(show)` threads into `presign_get` → Task 4.
- Local impl signature-only update → Task 2.
- Baas conditional `show` in share-link body (byte-identical when false) → Task 3.
- `supports_inline_view` on `StorageCapabilities` (both backends `true`) → Task 1.
- `CapabilitiesView.inline_view` + `capabilities()` mapping → Task 1.
- Service does not gate `show` on the capability → no enforcement added (correct, by design).
- Error handling: no new errors; `show=foo` → 400 via axum → covered by `bool` serde (Task 5).
- Tests: HTTP/local inline + attach regression (Task 5); HTTP shared-file (Task 5); capabilities (Task 1); service forwarding (Task 4); baas body conditional (Task 3); signature migration (Task 2). Baas HTTP E2E is covered transitively: HTTP→`download_route(show)` link is the same code as the local path (proved by Task 5 local tests), service forwarding proved by Task 4, baas body proved by Task 3; the real baas path is additionally exercised by the singlebox E2E gate (100% HTTP endpoint coverage).

**Placeholder scan:** one instructive placeholder remains — Task 5 Step 7's `let token = /* mint ... */`, which explicitly directs the implementer to copy the minting steps verbatim from a named neighboring test rather than paraphrasing. This is intentional (the helper is test-internal and must be copied, not reinvented); all other steps contain concrete code. No "TBD"/"TODO"/"add error handling" patterns.

**Type consistency:**
- `PresignGetOptions { ttl_secs: u64, show: bool }` — identical in Task 2 (definition), Task 3 (`opts.ttl_secs`, `opts.show`), Task 4 (constructor `{ ttl_secs: ttl, show }`, `opts.show`, `opts.ttl_secs`), and the fake recorder (`Option<PresignGetOptions>`).
- `download_route(..., show: bool)` — identical signature in Task 4 (trait decl, impl, noop) and Task 5 (caller `download_route(sid, file_id, ttl, show)` / `download_route(sid, file_id, ttl, false)` in Task 4 Step 7 then replaced in Task 5 Step 4).
- `last_presign_opts() -> Option<PresignGetOptions>` — defined and used consistently in Task 4.
- `CapabilitiesView.inline_view: bool` / `StorageCapabilities.supports_inline_view: bool` — consistent in Task 1.

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-07-29-session-file-show.md`. Two execution options:

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints.

Which approach?