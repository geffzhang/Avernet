# 系统消息按收件 bot 归属持久化与用户可见消息设计 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 `SystemMessageDispatcherService::dispatch` 对系统消息按 `SystemGroupMessage.recipients` 逐收件人写 `owner_bot_id = 收件 bot` 的历史副本、不再把 bot 视角的注入/上下文消息当作全局历史；前端 WS 改为单独推送一条 producer 产出的用户可见文本（不入库）；历史查询以 `PublicOrOwner(view_bot_id)` 谓词回放“公共消息 + 发给该 bot 的系统消息副本”。

**Architecture:** 契约层 `SystemMessageProducerService::produce` 返回值由 `Vec<SystemGroupMessage>` 改为 `(Vec<SystemGroupMessage>, Option<String>)`，第二元素是仅用于 WS 广播、不入库的用户可见文本。7 个 producer 显式构造该文本（SessionContext 经共享渲染助手去个性化，绝不字符串剥离 bot 消息）；dispatcher 解构 tuple，逐 recipient 持久化 `owner_bot_id = Some(recipient)`、删除 `messages[0]` 全局记录分支与 MW 特化，WS 改为单条 `user_message` 推送。查询侧 `bcs-domain::MessageOwnerFilter` 新增 `PublicOrOwner(String)` 变体，mysql/memory 实现为 `(owner_bot_id IS NULL OR owner_bot_id = ?)`，`compute_session_history_query` 与老 group `get_history` 的 Chat 新存储路径接入该谓词。设计依据见 `docs/superpowers/specs/2026-08-03-system-message-user-message-design.md`。

**Tech Stack:** Rust workspace（`src/bcs/`）；`async_trait`；`serde_json`；MySQL/SQLite via `bcs-db-api`；内存 store；`tokio::test`。测试命令在 `src/bcs/` 下用 `cargo test -p <crate>`。

---

## Global Constraints

逐一摘自 spec，所有任务隐含遵守：

- **无 schema 变更**：`owner_bot_id` 列与索引已存在；不新增列、不写迁移、不做存量数据治理（兼容由 `IS NULL OR owner = ?` 谓词承担）。
- **producer 空文本契约**：`produce` 有用户可见内容时返回 `Some(non_empty)`；无可见内容时返回 `None`，**绝不**返回 `Some("")`。dispatcher 的非空校验与 producer 责任对齐。
- **user_message 不入库**：`user_message` 仅用于 WS `FrontendDeliveryTarget::Session` 广播，不写 `MessageRepoPort`。
- **bot 消息内容不变**：bot 投递路径（`chat.send`/`chat.inject` 帧内容、`DeliveryType`、provider transport、run-context 记录）完全不动；SessionContext 的 bot 消息渲染与内容完全不变。
- **空 recipients 不阻止 user_message**：例如最后一个 bot 退群时 `BotLeft` 收件人为空，仍返回 `(vec![], Some("..."))`，dispatcher 入库 0 条但 WS 照常推送。
- **UTF-8 安全**：禁止按字节下标切片字符串（`&s[..n]`）；截断一律走 `char_indices()`（见 `src/bcs/CLAUDE.md`）。
- **不做 cargo fmt**：不运行 `cargo fmt`；只改必须改的行，不顺手重排无关代码。
- **中文文案逐字保持**：所有通知/上下文模板的中文字符串（`已加入协作群`、`已退出协作群`、`已设置为「不可协作」`、`[GROUP CONTEXT]`、`[SERVICE GROUP CONTEXT]`、`[任务]`、`[任务状态]` 等）按现有渲染逐字保留；去个性化只删尾部 `你是:` / `你的角色:` / 角色指令 / 协同指令行。
- **契约变更需匹配 conformance**（仓库 `AGENTS.md`）：trait 签名变更须同步 `bcs-test-support` 的 conformance helper 与 `NoopSystemMessageProducer`，使 `tests/conformance_system_message.rs` 6 条用例在新语义下编译通过。
- **错误处理不变**：持久化/`append_message`/WS 发布失败均记 `tracing::warn!`、不影响 bot 投递与 `SystemMessageDispatchOutcome`；`produce` 不返回 `Result`，event 类型不匹配返回 `(vec![], None)`；持久化逐记录 best-effort。

**路径约定：** 本计划所有路径相对 `src/bcs/`。执行前 `cd src/bcs`。跑某 crate 测试：`cargo test -p <crate>`；跑单测：`cargo test -p <crate> <test_name>`。

---

## File Structure

| 文件 | 责任 | 任务 |
|---|---|---|
| `crates/contracts/bcs-domain/src/message.rs` | `MessageOwnerFilter` 新增 `PublicOrOwner(String)` 变体 | T1 |
| `crates/services/bcs-message-store/src/mysql.rs` | `query_messages` 与 `list_session_history` 的 owner 谓词新增 `PublicOrOwner` 分支 + 谓词测试 | T1 |
| `crates/services/bcs-message-store/src/memory.rs` | 同上内存实现 + 谓词测试 | T1 |
| `crates/test-support/bcs-test-support/src/contract/repo/mod.rs` | `PublicOrOwner` 等价用例（query + list_session_history） | T1 |
| `crates/service-api/bcs-service-api/src/core/system_message.rs` | `SystemMessageProducerService::produce` 签名改 tuple + 文档注释 | T2 |
| `crates/test-support/bcs-test-support/src/noop.rs` | `NoopSystemMessageProducer` 返回 `(vec![], None)` | T2 |
| `crates/test-support/bcs-test-support/src/contract/core/mod.rs` | conformance helper 仍为 `kind()` 锚点（签名迁移编译锚） | T2 |
| `crates/services/bcs-system-message/src/producers/bot_left.rs` | 机械化迁移签名 → 产出 `user_message` | T2, T3 |
| `crates/services/bcs-system-message/src/producers/bot_joined.rs` | 同上 | T2, T4 |
| `crates/services/bcs-system-message/src/producers/human_joined.rs` | 同上 | T2, T5 |
| `crates/services/bcs-system-message/src/producers/participant_mode_changed.rs` | 同上 | T2, T6 |
| `crates/services/bcs-system-message/src/producers/generic.rs` | 同上（空串→None） | T2, T7 |
| `crates/services/bcs-system-message/src/producers/bot_hidden_notice.rs` | 同上 | T2, T8 |
| `crates/services/bcs-system-message/src/producers/session_context.rs` | 机械化迁移签名 → 抽共享渲染助手 → 去个性化 Chat/MW WS 文本 | T2, T9, T10 |
| `crates/services/bcs-system-message/src/producers/*_test.rs` | producer 单测解构 tuple + 新断言 | T2–T10 |
| `crates/services/bcs-system-message/src/dispatcher.rs` | 解构 tuple → 逐 recipient 持久化 + WS 单条推送 + 删 MW 特化 | T2, T11 |
| `crates/services/bcs-system-message/src/dispatcher_test.rs` | 4 个 stub 签名迁移；持久化/WS 用例改写 + 新增 `RecordingFrontendDeliveryPort` | T2, T11 |
| `crates/services/bcs-message/src/lib.rs` | `compute_session_history_query` viewer 分支接入 `PublicOrOwner`；`get_history` 老 group Chat 新路径接入 owner 过滤；共享 `chat_owner_filter_for_view` | T12, T13 |
| `crates/services/bcs-system-message/tests/conformance_system_message.rs` | 6 条 conformance 用例确认编译/通过 | T14 |

---

## Task 1: `MessageOwnerFilter::PublicOrOwner` 变体 + mysql/memory 谓词

**Files:**
- Modify: `crates/contracts/bcs-domain/src/message.rs:178-188`
- Modify: `crates/services/bcs-message-store/src/mysql.rs:304-312`（`query_messages`）与 `:446-454`（`list_session_history`）
- Modify: `crates/services/bcs-message-store/src/memory.rs:122-128`（`query_messages`）与 `:200-206`（`list_session_history`）
- Modify: `crates/test-support/bcs-test-support/src/contract/repo/mod.rs`（在既有 owner 过滤用例旁补 `PublicOrOwner` 等价用例）

**Interfaces:**
- Produces: `bcs_domain::MessageOwnerFilter::PublicOrOwner(String)`，语义 `owner_bot_id IS NULL OR owner_bot_id = <viewer>`。后续任务（T12/T13）只消费该变体，不改其定义。

- [ ] **Step 1: 写失败测试 — domain 变体先就位以满足编译，store 谓词先用未实现分支使测试失败**

先在 `crates/contracts/bcs-domain/src/message.rs` 给枚举加变体（不加会导致所有 `match owner_filter` 缺臂编译失败）：

```rust
pub enum MessageOwnerFilter {
    Any,
    IsNull,
    Eq(String),
    /// `owner_bot_id IS NULL OR owner_bot_id = <viewer>` — 公共消息 + 发给该
    /// viewer 的系统消息副本。历史查询按 `view_bot_id` 回放“公共 + 自己的
    /// 系统副本”，收窄的仅是他人新增的私有副本，是旧 `Any`/`IsNull` 的超集。
    PublicOrOwner(String),
}
```

在 `crates/services/bcs-message-store/src/memory.rs` 的 `query_messages` 与 `list_session_history` 两个 `match &query.owner_filter` / `match &owner_filter` 处先加未实现臂（使新测试编译通过但跑失败）：

```rust
            MessageOwnerFilter::PublicOrOwner(_) => {
                unimplemented!("PublicOrOwner predicate — implemented in Step 3")
            }
```

在 `crates/services/bcs-message-store/src/mysql.rs` 两处 match 同样加：

```rust
            MessageOwnerFilter::PublicOrOwner(_) => {
                unimplemented!("PublicOrOwner predicate — implemented in Step 3")
            }
```

在 `crates/test-support/bcs-test-support/src/contract/repo/mod.rs`，紧跟既有 `public_owner_page`（`MessageOwnerFilter::IsNull`）断言之后追加 `PublicOrOwner` 用例（复用同一批 `mgr`/`workerA`/`sys` 三条消息：owner=`mgr`、owner=`workerA`、owner=None；时间窗 `(5000, 5200)`）：

```rust
    // PublicOrOwner → 公共(owner=None) + 命中 viewer 的副本；他人 owner 不返回。
    let public_or_mgr = repo
        .query_messages(MessageQuery {
            group_id: group_id.to_string(),
            session_id: session_id.to_string(),
            cursor: None,
            limit: 10,
            keyword: None,
            sender_id: None,
            message_type: None,
            owner_filter: MessageOwnerFilter::PublicOrOwner("mgr".to_string()),
            time_range: Some((5000, 5200)),
            visible_from_seq: None,
        })
        .await
        .expect("query public-or-mgr");
    // sys(owner=None) + mgr(owner=mgr) 命中；workerA(owner=workerA) 不返回。
    assert_eq!(public_or_mgr.messages.len(), 2);
    assert!(public_or_mgr
        .messages
        .iter()
        .all(|m| m.owner_bot_id.is_none() || m.owner_bot_id.as_deref() == Some("mgr")));
    assert!(public_or_mgr
        .messages
        .iter()
        .any(|m| m.owner_bot_id.is_none()));
    assert!(public_or_mgr
        .messages
        .iter()
        .any(|m| m.owner_bot_id.as_deref() == Some("mgr")));
```

并在 `list_session_history` 的 `Eq → only the given owner` 断言（`worker_only`，seq `[8]`）之后追加 `PublicOrOwner` 用例。contract/repo 的种子（同 session、seqs 1-9）为：**seqs 1-6 owner=None、seq 7 owner=mgr、seq 8 owner=workerA、seq 9 owner=None**（这是 contract/repo 既有 `list_session_history` 段落实际备料，与 `memory.rs` 自己的 `s3` 种子不同——勿混用）。`PublicOrOwner("workerA")` 命中 NULL 与 owner=workerA，排除 seq 7(mgr)：

```rust
    // PublicOrOwner("workerA") → 公共(NULL seqs 9,6,5,4,3,2,1) + workerA(seq 8)，DESC；
    // seq 7(mgr) 被排除。
    let public_or_wa = repo
        .list_session_history(
            session_id,
            MessageOwnerFilter::PublicOrOwner("workerA".to_string()),
            None,
            None,
            100,
        )
        .await
        .expect("list_session_history PublicOrOwner");
    assert_eq!(
        public_or_wa
            .messages
            .iter()
            .map(|m| m.session_seq)
            .collect::<Vec<_>>(),
        vec![9, 8, 6, 5, 4, 3, 2, 1]
    );
    assert!(public_or_wa.messages.iter().all(|m| {
        m.owner_bot_id.is_none() || m.owner_bot_id.as_deref() == Some("workerA")
    }));
    assert!(
        !public_or_wa.messages.iter().any(|m| m.session_seq == 7),
        "mgr-owned seq 7 must NOT appear under PublicOrOwner(workerA)"
    );
```

> 注：contract/repo 用例同时被 mysql（经 `conformance_message_repo.rs` 的 `sqlite_message_repo_passes_contract`）与 memory 跑。`session_id` 变量沿用既有上下文。

- [ ] **Step 2: 跑测试确认失败**

Run: `cd src/bcs && cargo test -p bcs-message-store -p bcs-test-support PublicOrOwner -- --nocapture`
Expected: memory 与 sqlite(mysql conformance) 两条 `PublicOrOwner` 用例 panic 于 `unimplemented!`。

- [ ] **Step 3: 实现 mysql/memory 谓词**

`crates/services/bcs-message-store/src/memory.rs` 两处把 `unimplemented!` 臂替换为同语义 retain：

`query_messages` 处（约 :122）：

```rust
            MessageOwnerFilter::PublicOrOwner(owner_bot_id) => {
                filtered.retain(|m| {
                    m.owner_bot_id.is_none()
                        || m.owner_bot_id.as_deref() == Some(owner_bot_id.as_str())
                });
            }
```

`list_session_history` 处（约 :200）：

```rust
            MessageOwnerFilter::PublicOrOwner(owner) => {
                filtered.retain(|m| {
                    m.owner_bot_id.is_none()
                        || m.owner_bot_id.as_deref() == Some(owner.as_str())
                });
            }
```

`crates/services/bcs-message-store/src/mysql.rs` 两处替换为 SQL 谓词。

`query_messages` 处（约 :304）：

```rust
            MessageOwnerFilter::PublicOrOwner(owner_bot_id) => {
                conditions.push("(owner_bot_id IS NULL OR owner_bot_id = ?)".to_string());
                params.push(DbValue::from(owner_bot_id.clone()));
            }
```

`list_session_history` 处（约 :446）：

```rust
            MessageOwnerFilter::PublicOrOwner(owner) => {
                conditions.push("(owner_bot_id IS NULL OR owner_bot_id = ?)".to_string());
                params.push(DbValue::from(owner.clone()));
            }
```

- [ ] **Step 4: 跑测试确认通过**

Run: `cd src/bcs && cargo test -p bcs-message-store -p bcs-test-support`
Expected: PASS（含新 `PublicOrOwner` 用例，既有 owner 过滤用例不回归）。

- [ ] **Step 5: 提交**

```bash
git add crates/contracts/bcs-domain/src/message.rs crates/services/bcs-message-store/src/mysql.rs crates/services/bcs-message-store/src/memory.rs crates/test-support/bcs-test-support/src/contract/repo/mod.rs
git commit -m "feat(domain): add PublicOrOwner variant to MessageOwnerFilter"
```

---

## Task 2: 契约签名迁移 tuple（机械化，保持行为）

本任务把 `produce` 返回值改为 `(Vec<SystemGroupMessage>, Option<String>)`，所有实现机械返回 `(现有 vec, None)`，dispatcher 解构后**暂忽略** `user_message`，持久化/WS 维持旧逻辑。结束于一个可编译、既有测试（解构后）全绿的检查点。后续 T3–T11 逐步填入真实 `user_message` 与 dispatcher 重写。

**Files:**
- Modify: `crates/service-api/bcs-service-api/src/core/system_message.rs:13-23`
- Modify: `crates/test-support/bcs-test-support/src/noop.rs:1990-2007`（`NoopSystemMessageProducer`）
- Modify: `crates/test-support/bcs-test-support/src/contract/core/mod.rs:112-119`（helper 不动逻辑，确认编译）
- Modify: 7 个 producer 的 `produce` 签名与返回点
- Modify: `crates/services/bcs-system-message/src/dispatcher.rs:183`（解构）、`:191`/`:228`/`:367-372`（`messages`→`bot_messages` 改名）
- Modify: 各 producer 单测与 `dispatcher_test.rs` 4 个 stub 的 produce 调用/实现

**Interfaces:**
- Produces: 新 trait 签名
  ```rust
  async fn produce(
      &self,
      event: &SystemMessageEvent,
      group: &Group,
      registry: &dyn BotRegistryCoreService,
      participants: &[Participant],
  ) -> (Vec<SystemGroupMessage>, Option<String>);
  ```
  T3–T10 实现者据此产出第二元素；T11 dispatcher 据此解构。

- [ ] **Step 1: 改 trait 签名 + 文档注释**

`crates/service-api/bcs-service-api/src/core/system_message.rs`，把 trait 体替换为：

```rust
#[async_trait]
pub trait SystemMessageProducerService: Send + Sync {
    fn kind(&self) -> SystemMessageEventKind;

    /// Produce messages for a system-message event.
    ///
    /// Returns `(bot_messages, user_message)`:
    /// - `bot_messages`: per-bot delivery messages (semantics unchanged). The
    ///   dispatcher persists one history record per recipient with
    ///   `owner_bot_id = recipient` and delivers each via `chat.send`/`chat.inject`.
    /// - `user_message`: single user-facing text used ONLY for the frontend
    ///   WebSocket session broadcast (NOT persisted); `None` when the event has
    ///   no user-facing content.
    ///
    /// Producers must return `None` (never `Some("")`) when the event has no
    /// non-empty user-facing text, so the dispatcher's non-empty check and the
    /// producer responsibility stay aligned. An empty `bot_messages` does NOT
    /// block a non-empty `user_message` (e.g. last bot leaving).
    async fn produce(
        &self,
        event: &SystemMessageEvent,
        group: &Group,
        registry: &dyn BotRegistryCoreService,
        participants: &[Participant],
    ) -> (Vec<SystemGroupMessage>, Option<String>);
}
```

- [ ] **Step 2: 改 `NoopSystemMessageProducer`**

`crates/test-support/bcs-test-support/src/noop.rs`，把 `produce` 实现改为：

```rust
    async fn produce(
        &self,
        _event: &SystemMessageEvent,
        _group: &Group,
        _registry: &dyn BotRegistryCoreService,
        _participants: &[Participant],
    ) -> (Vec<SystemGroupMessage>, Option<String>) {
        (vec![], None)
    }
```

conformance helper `system_message_producer_service_contract_tests`（`contract/core/mod.rs`）仅 `let _ = svc.kind();`，签名未变无需改；保留为 tuple 签名的编译锚。

- [ ] **Step 3: 7 个 producer 机械化迁移**

每个 producer：`async fn produce(...) -> Vec<SystemGroupMessage>` 改为 `-> (Vec<SystemGroupMessage>, Option<String>)`，每个 `return vec![...]`/`return vec![]` 改为 `return (vec![...], None)`/`return (vec![], None)`，末尾 `messages` 改为 `(messages, None)`。

`producers/bot_left.rs`：

- 签名（:21-33）返回类型改 `(Vec<SystemGroupMessage>, Option<String>)`。
- `:30` `return vec![];` → `return (vec![], None);`
- `:47` `return vec![];` → `return (vec![], None);`
- `:50-54` 末尾 `vec![SystemGroupMessage { ... }]` → `return (vec![SystemGroupMessage { recipients, message, delivery_type: DeliveryType::Inject }], None);`（保留原结构体字面量内容，仅外包 tuple）。

`producers/bot_joined.rs`：

- 签名（:40-46）返回类型改 tuple。
- `:49` `return vec![];` → `return (vec![], None);`
- `:108` `messages` → `(messages, None)`

`producers/human_joined.rs`：

- 签名（:28-34）返回类型改 tuple。
- `:37` `return vec![];` → `return (vec![], None);`
- `:53` `return vec![];` → `return (vec![], None);`
- `:56-60` 末尾 `vec![SystemGroupMessage { ... }]` → 外包 `(…, None)`。

`producers/participant_mode_changed.rs`：

- 签名（:23-29）返回类型改 tuple。
- `:40` `return vec![];` → `return (vec![], None);`
- `:47` `return vec![];` → `return (vec![], None);`
- `:75-79` 末尾外包 `(…, None)`。

`producers/generic.rs`：

- 签名（:20-26）返回类型改 tuple。
- `:32` `return vec![];` → `return (vec![], None);`
- `:44` `return vec![];` → `return (vec![], None);`
- `:45-49` 末尾外包 `(…, None)`。

`producers/bot_hidden_notice.rs`：

- 签名（:18-24）返回类型改 tuple。
- `:32` `return vec![];` → `return (vec![], None);`
- `:55` `messages` → `(messages, None)`

`producers/session_context.rs`：

- 签名（:27-33）返回类型改 tuple。
- `:43` `return vec![];` → `return (vec![], None);`
- `:108` `messages` → `(messages, None)`

- [ ] **Step 4: dispatcher 解构 + 改名（暂忽略 user_message）**

`crates/services/bcs-system-message/src/dispatcher.rs`：

`:183`：

```rust
        let (bot_messages, _user_message) = producer.produce(&event, group, self.registry.as_ref(), participants).await;
```

`:191` `history_messages_to_persist(kind, group, &messages)` → `history_messages_to_persist(kind, group, &bot_messages)`。

`:228` `for msg in &messages {` → `for msg in &bot_messages {`。

`:366-372` WS 段 `messages.iter()` → `bot_messages.iter()`（去重+拼接逻辑本任务**暂保留**，T11 重写）：

```rust
        // Publish all produced messages to frontend WebSocket clients.
        let mut seen = std::collections::HashSet::new();
        let frontend_content = bot_messages
            .iter()
            .filter(|msg| seen.insert(msg.message.clone()))
            .map(|msg| msg.message.clone())
            .collect::<Vec<_>>()
            .join("\n");
```

（其余 WS 发布逻辑不动；T11 删整段改成 `user_message` 单条推送。）

- [ ] **Step 5: 改 producer 单测与 dispatcher_test stub 的 produce 调用**

凡是 `let messages = producer.produce(...)` 形式改为 `let (messages, _) = producer.produce(...)`，断言 `messages` 不变：

- `producers/bot_joined_test.rs:182` `let messages = producer.produce(...)` → `let (messages, _) = producer.produce(...)`
- `producers/session_context_test.rs:239` `let messages = SessionContextMessageProducer.produce(...)` → `let (messages, _) = …`
- `producers/participant_mode_changed_test.rs` 四处 `let messages = producer.produce(...)` → `let (messages, _) = …`（:31, :54, :79, :97）
- `producers/bot_left.rs` 内联 `#[cfg(test)]` `bot_left_produces_leave_message_for_other_bots`：`:126` `let messages = BotLeftMessageProducer.produce(...)` → `let (messages, _) = …`
- `producers/bot_hidden_notice.rs` 内联 `produces_notice_for_mentioner_only`：`:121` → `let (messages, _) = …`

`dispatcher_test.rs` 4 个 stub（`WorkerOnlySessionContextProducer`、`FixedProducer`、`FixedSendProducer`、`FixedWebSocketSendProducer`）签名改 tuple、末尾外包 `(…, None)`：

例如 `WorkerOnlySessionContextProducer`（:1093-1106）：

```rust
    async fn produce(
        &self,
        _event: &SystemMessageEvent,
        _group: &Group,
        _registry: &dyn BotRegistryCoreService,
        _participants: &[Participant],
    ) -> (Vec<SystemGroupMessage>, Option<String>) {
        (
            vec![SystemGroupMessage {
                recipients: vec!["bot-worker".to_string()],
                message: "worker-only context".to_string(),
                delivery_type: DeliveryType::Inject,
            }],
            None,
        )
    }
```

`FixedProducer`（:1116-1129）、`FixedSendProducer`（:1139-1152）、`FixedWebSocketSendProducer`（:1162-1175）同样把返回类型改 tuple、末尾 `vec![…]` 包成 `(vec![…], None)`。这 4 个 stub 的 `kind()` 不变。显式代码：

`FixedProducer`（:1116-1129）：
```rust
    async fn produce(
        &self,
        _event: &SystemMessageEvent,
        _group: &Group,
        _registry: &dyn BotRegistryCoreService,
        _participants: &[Participant],
    ) -> (Vec<SystemGroupMessage>, Option<String>) {
        (
            vec![SystemGroupMessage {
                recipients: vec!["bot-provider".to_string()],
                message: "member changed".to_string(),
                delivery_type: DeliveryType::Inject,
            }],
            None,
        )
    }
```

`FixedSendProducer`（:1139-1152）—— **仅外包 tuple，recipient/message/delivery_type 必须保持原值**（`bot-provider` 是 `ProviderTargetRegistry` 解析为 `HttpProvider` 的唯一 bot，改 recipient 会破坏 4 个依赖 `is_http_provider()` 的用例）：
```rust
    async fn produce(
        &self,
        _event: &SystemMessageEvent,
        _group: &Group,
        _registry: &dyn BotRegistryCoreService,
        _participants: &[Participant],
    ) -> (Vec<SystemGroupMessage>, Option<String>) {
        (
            vec![SystemGroupMessage {
                recipients: vec!["bot-provider".to_string()],
                message: "member changed".to_string(),
                delivery_type: DeliveryType::Send,
            }],
            None,
        )
    }
```

`FixedWebSocketSendProducer`（:1162-1175）—— 同样仅外包 tuple，原值 `bot-ws` / `"member changed"` 不变：
```rust
    async fn produce(
        &self,
        _event: &SystemMessageEvent,
        _group: &Group,
        _registry: &dyn BotRegistryCoreService,
        _participants: &[Participant],
    ) -> (Vec<SystemGroupMessage>, Option<String>) {
        (
            vec![SystemGroupMessage {
                recipients: vec!["bot-ws".to_string()],
                message: "member changed".to_string(),
                delivery_type: DeliveryType::Send,
            }],
            None,
        )
    }
```

- [ ] **Step 6: 跑测试确认全绿（行为不变检查点）**

Run: `cd src/bcs && cargo test -p bcs-system-message -p bcs-service-api -p bcs-test-support`
Expected: PASS（既有 dispatcher 持久化/WS 用例、producer 单测、6 条 conformance 全绿；`user_message` 此时恒 `None`，WS 仍走旧 `bot_messages` 拼接——已知中间态，T11 修正）。

- [ ] **Step 7: 提交**

```bash
git add crates/service-api/bcs-service-api/src/core/system_message.rs crates/test-support/bcs-test-support/src/noop.rs crates/services/bcs-system-message/src/producers crates/services/bcs-system-message/src/dispatcher.rs crates/services/bcs-system-message/src/dispatcher_test.rs
git commit -m "refactor(bcs-system-message): migrate produce signature to (bot_messages, user_message)"
```

---

## Task 3: `BotLeft` 产出 `user_message` + 无收件人边界

**Files:**
- Modify: `crates/services/bcs-system-message/src/producers/bot_left.rs:21-56`
- Test: `crates/services/bcs-system-message/src/producers/bot_left.rs`（内联 `#[cfg(test)]` 模块）

**Interfaces:**
- Consumes: T2 的 tuple 签名。
- Produces: `BotLeftMessageProducer::produce` 对 `BotLeft` 事件恒返回 `(bot_messages, Some("xxx(id) 已退出协作群"))`；收件人为空时 `bot_messages = vec![]` 但 `user_message = Some(...)`。

- [ ] **Step 1: 写失败测试**

在 `bot_left.rs` 内联 test 模块追加（group/participants 同既有 `bot_left_produces_leave_message_for_other_bots` 的 `Group` 构造方式，但只剩退群者本人）：

```rust
    #[tokio::test]
    async fn bot_left_with_no_other_recipients_returns_user_message_only() {
        let registry = NoopBotRegistryCoreService;
        let group = Group {
            id: "g1".into(),
            label: None,
            status: bcs_domain::GroupStatus::Active,
            driver_bot: "bot-1".into(),
            originator: Some("bot-1".into()),
            routing_policy: None,
            context: None,
            participants: vec![Participant {
                bot_uuid: "bot-1".into(),
                bot_name: Some("测试Bot".into()),
                kind: None,
                role: ParticipantRole::Driver,
                actor_kind: ActorKind::Bot,
                mode: Some(ParticipantMode::Auto),
            }],
            messages: vec![],
            workspace: Default::default(),
            service_group_uuid: None,
            service_mode: None,
            created_at: 0,
            updated_at: 0,
            group_kind: bcs_domain::GroupKind::Normal,
            dm_pair_key: None,
            group_strategy: bcs_domain::GroupStrategy::Chat,
            service_spec: None,
            version: 0,
            record_status: "active".to_string(),
            visibility: "private".to_string(),
        };

        let event = SystemMessageEvent::BotLeft {
            group_id: "g1".into(),
            actor: Participant {
                bot_uuid: "bot-1".into(),
                bot_name: None,
                kind: None,
                role: ParticipantRole::Driver,
                actor_kind: ActorKind::Bot,
                mode: None,
            },
        };

        let (messages, user_message) = BotLeftMessageProducer
            .produce(&event, &group, &registry, &group.participants)
            .await;

        assert!(messages.is_empty(), "no other recipients → empty bot_messages");
        assert_eq!(
            user_message.as_deref(),
            Some("测试Bot(bot-1) 已退出协作群"),
            "user_message still produced when no bot recipients remain"
        );
    }
```

并把既有 `bot_left_produces_leave_message_for_other_bots` 的解构改为 `let (messages, user_message) = …`，末尾追加：

```rust
        assert_eq!(
            user_message.as_deref(),
            Some("测试Bot(bot-1) 已退出协作群")
        );
```

- [ ] **Step 2: 跑测试确认失败**

Run: `cd src/bcs && cargo test -p bcs-system-message --lib bot_left`
Expected: FAIL — `user_message` 仍为 `None`（T2 机械化返回 `None`）；且有收件人用例的 user_message 断言失败。

- [ ] **Step 3: 实现**

替换 `bot_left.rs` 的 `produce` 主体（:21-56）。先算通知文本，`user_message` 恒 `Some`（注意 `user_text` 需在 move 进 `SystemGroupMessage` 前 clone 给 `user_message`，否则 borrow-after-move）：

```rust
    async fn produce(
        &self,
        event: &SystemMessageEvent,
        _group: &Group,
        registry: &dyn BotRegistryCoreService,
        participants: &[Participant],
    ) -> (Vec<SystemGroupMessage>, Option<String>) {
        let SystemMessageEvent::BotLeft { actor, .. } = event else {
            return (vec![], None);
        };

        let left_id = actor.bot_uuid.clone();
        let registered = registry.get(&left_id).await;
        let name = registered
            .as_ref()
            .and_then(|b| b.capabilities.name.clone())
            .unwrap_or_else(|| left_id.clone());
        let user_text = format!("{}({}) 已退出协作群", name, left_id);
        let user_message = Some(user_text.clone());

        let recipients: Vec<String> = participants
            .iter()
            .filter(|p| p.bot_uuid != left_id)
            .filter(|p| p.is_bot())
            .map(|p| p.bot_uuid.clone())
            .collect();
        let bot_messages = if recipients.is_empty() {
            vec![]
        } else {
            vec![SystemGroupMessage {
                recipients,
                message: user_text,
                delivery_type: DeliveryType::Inject,
            }]
        };
        // empty recipients does NOT block user_message (last bot leaving)
        (bot_messages, user_message)
    }
```

- [ ] **Step 4: 跑测试确认通过**

Run: `cd src/bcs && cargo test -p bcs-system-message --lib bot_left`
Expected: PASS。

- [ ] **Step 5: 提交**

```bash
git add crates/services/bcs-system-message/src/producers/bot_left.rs
git commit -m "feat(bcs-system-message): BotLeft produces user-facing WS text incl. no-recipient case"
```

---

## Task 4: `BotJoined` 产出 `user_message`（通知文本）

**Files:**
- Modify: `crates/services/bcs-system-message/src/producers/bot_joined.rs:40-79`
- Test: `crates/services/bcs-system-message/src/producers/bot_joined_test.rs`

**Interfaces:**
- Produces: `BotJoinedMessageProducer::produce` 的 `user_message = Some(format_notification(...))` —— 即发给其他 bot 的那条通知文本（`xxx(id) 已加入协作群`，含能力集后缀），**不**含给新 bot 的上下文注入消息。

- [ ] **Step 1: 写失败测试**

`bot_joined_test.rs` 既有 `bot_joined_produces_context_injection_and_notification` 解构改 `let (messages, user_message) = producer.produce(...).await`，在末尾追加：

```rust
    // user_message is the OTHER-bots notification text (NOT the new-bot injection).
    assert_eq!(
        user_message.as_deref(),
        Some("NewBot(new-bot-id) 已加入协作群 - 能力集: {name: \"coding\"}")
    );
    assert!(
        !user_message.as_deref().unwrap().contains("你加入了 BCS 协作群"),
        "user_message must not leak the new-bot context injection"
    );
```

并新增“无其他 bot”边界用例（driver+new-bot，通知无收件人，user_message 仍有）：

```rust
#[tokio::test]
async fn bot_joined_emits_user_message_even_when_only_new_bot_present() {
    let driver = Participant::bot("driver-id", ParticipantRole::Driver);
    let new_bot = Participant::bot("new-bot-id", ParticipantRole::Consultant);
    let group = Group::new("group-1", "driver-id", vec![driver.clone(), new_bot.clone()]);

    let mut registry = MockRegistry::default();
    registry.bots.insert(
        "new-bot-id".to_string(),
        RegisteredBot {
            bot_uuid: "new-bot-id".to_string(),
            capabilities: BotCapabilities {
                name: Some("NewBot".to_string()),
                skills: vec![Skill::new("coding")],
                ..Default::default()
            },
            dynamic_status: BotDynamicStatus::default(),
            env: None,
            created_by: None,
            actor_kind: ActorKind::Bot,
            status: ActorStatus::default(),
        },
    );

    let producer = BotJoinedMessageProducer::new(Arc::new(bcs_test_support::NoopGroupMessageHistoryService));
    let event = SystemMessageEvent::BotJoined {
        group_id: "group-1".to_string(),
        actor: new_bot.clone(),
    };

    let (messages, user_message) = producer.produce(&event, &group, &registry, &[driver, new_bot]).await;

    // only driver + new bot: other-recipients includes driver → notification has 1 recipient.
    let notification = messages
        .iter()
        .find(|m| m.recipients != vec!["new-bot-id".to_string()])
        .expect("notification message");
    assert_eq!(notification.recipients, vec!["driver-id".to_string()]);
    assert_eq!(
        user_message.as_deref(),
        Some("NewBot(new-bot-id) 已加入协作群 - 能力集: {name: \"coding\"}")
    );
}
```

> 这条边界用例的 participants 仍含 driver，故 `others` 非空。“纯独 bot”场景不强制；T3 已覆盖“无其他收件人仍返回 user_message”的通用边界。

- [ ] **Step 2: 跑测试确认失败**

Run: `cd src/bcs && cargo test -p bcs-system-message --lib bot_joined_produces_context_injection_and_notification`
Expected: FAIL — `user_message` 为 `None`。

- [ ] **Step 3: 实现**

`bot_joined.rs` 的 `produce`（:40-79）：`summary` 已由 `format_notification` 算出，将其作 `user_message`。改末尾：

```rust
        // 2. Notification to other bots.
        let registered = registry.get(&new_bot_uuid).await;
        let summary = format_notification(&new_bot_uuid, registered.as_ref());
        let user_message = Some(summary.clone());
        let others: Vec<String> = participants
            .iter()
            .filter(|p| p.bot_uuid != new_bot_uuid)
            .map(|p| p.bot_uuid.clone())
            .collect();
        let mut messages = Vec::new();
        messages.push(SystemGroupMessage {
            recipients: vec![new_bot_uuid.clone()],
            message: new_bot_content,
            delivery_type: DeliveryType::Inject,
        });
        if !others.is_empty() {
            messages.push(SystemGroupMessage {
                recipients: others,
                message: summary,
                delivery_type: DeliveryType::Inject,
            });
        }
        (messages, user_message)
```

注意现有代码顺序是先 push 注入消息再 push 通知（:56-77）。保持该顺序；仅把 `summary` 在 move 进 struct 前 `clone` 给 `user_message`（`summary` move 进通知 struct）。即 `let user_message = Some(summary.clone());` 放在 `let summary = …` 之后、push 通知之前。

- [ ] **Step 4: 跑测试确认通过**

Run: `cd src/bcs && cargo test -p bcs-system-message --lib bot_joined`
Expected: PASS。

- [ ] **Step 5: 提交**

```bash
git add crates/services/bcs-system-message/src/producers/bot_joined.rs crates/services/bcs-system-message/src/producers/bot_joined_test.rs
git commit -m "feat(bcs-system-message): BotJoined user_message is the join notification text"
```

---

## Task 5: `HumanJoined` 产出 `user_message`

**Files:**
- Modify: `crates/services/bcs-system-message/src/producers/human_joined.rs:28-61`
- Test: 新增内联 `#[cfg(test)]` 模块（该文件当前无 test 模块）

**Interfaces:**
- Produces: `HumanJoinedMessageProducer::produce` 的 `user_message = Some("xxx(id) 已加入协作群")`；无 bot 收件人时 `bot_messages = vec![]`、`user_message` 仍 `Some`。

- [ ] **Step 1: 写失败测试**

在 `human_joined.rs` 末尾追加：

```rust
#[cfg(test)]
mod tests {
    use super::*;
    use bcs_domain::{ActorKind, Group, Participant, ParticipantRole};
    use bcs_test_support::NoopBotRegistryCoreService;

    #[tokio::test]
    async fn human_joined_emits_user_message() {
        let group = Group::new("g1", "bot-1", vec![Participant::bot("bot-1", ParticipantRole::Driver)]);
        let actor = Participant {
            bot_uuid: "human_42".into(),
            bot_name: Some("Alice".into()),
            kind: None,
            role: ParticipantRole::Observer,
            actor_kind: ActorKind::Human,
            mode: None,
        };
        let event = SystemMessageEvent::HumanJoined { group_id: "g1".into(), actor };

        let (messages, user_message) = HumanJoinedMessageProducer::new()
            .produce(&event, &group, &NoopBotRegistryCoreService, &group.participants)
            .await;

        assert_eq!(messages.len(), 1);
        assert_eq!(messages[0].recipients, vec!["bot-1".to_string()]);
        assert_eq!(user_message.as_deref(), Some("Alice(human_42) 已加入协作群"));
    }

    #[tokio::test]
    async fn human_joined_emits_user_message_even_with_no_bot_recipients() {
        let group = Group::new("g1", "bot-1", vec![]);
        let actor = Participant {
            bot_uuid: "human_42".into(),
            bot_name: Some("Alice".into()),
            kind: None,
            role: ParticipantRole::Observer,
            actor_kind: ActorKind::Human,
            mode: None,
        };
        let event = SystemMessageEvent::HumanJoined { group_id: "g1".into(), actor };

        let (messages, user_message) = HumanJoinedMessageProducer::new()
            .produce(&event, &group, &NoopBotRegistryCoreService, &group.participants)
            .await;

        assert!(messages.is_empty());
        assert_eq!(user_message.as_deref(), Some("Alice(human_42) 已加入协作群"));
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `cd src/bcs && cargo test -p bcs-system-message --lib human_joined`
Expected: FAIL — `user_message` 为 `None`。

- [ ] **Step 3: 实现**

替换 `produce` 主体（:28-61）：

```rust
    async fn produce(
        &self,
        event: &SystemMessageEvent,
        _group: &Group,
        _registry: &dyn BotRegistryCoreService,
        participants: &[Participant],
    ) -> (Vec<SystemGroupMessage>, Option<String>) {
        let SystemMessageEvent::HumanJoined { actor, .. } = event else {
            return (vec![], None);
        };

        let display_name = actor
            .bot_name
            .as_deref()
            .unwrap_or(&actor.bot_uuid);

        let message = format!("{}({}) 已加入协作群", display_name, actor.bot_uuid);
        let user_message = Some(message.clone());

        let recipients: Vec<String> = participants
            .iter()
            .filter(|p| p.is_bot() && p.bot_uuid != actor.bot_uuid)
            .map(|p| p.bot_uuid.clone())
            .collect();

        let bot_messages = if recipients.is_empty() {
            vec![]
        } else {
            vec![SystemGroupMessage {
                recipients,
                message,
                delivery_type: DeliveryType::Inject,
            }]
        };
        (bot_messages, user_message)
    }
```

- [ ] **Step 4: 跑测试确认通过**

Run: `cd src/bcs && cargo test -p bcs-system-message --lib human_joined`
Expected: PASS。

- [ ] **Step 5: 提交**

```bash
git add crates/services/bcs-system-message/src/producers/human_joined.rs
git commit -m "feat(bcs-system-message): HumanJoined user_message is the join notification text"
```

---

## Task 6: `ParticipantModeChanged` 产出 `user_message`

**Files:**
- Modify: `crates/services/bcs-system-message/src/producers/participant_mode_changed.rs:23-80`
- Test: `crates/services/bcs-system-message/src/producers/participant_mode_changed_test.rs`

**Interfaces:**
- Produces: `user_message = Some(content)`（即既有 `content` 通知文本：用户加入/退出、Bot 禁言/自动发言）；`from=None` 的 Bot 异常分支返回 `(vec![], None)`（不噪扰 WS）。

- [ ] **Step 1: 写失败测试**

既有 4 个用例解构改 `let (messages, user_message) = producer.produce(...).await`。`human_joined_from_none_produces_message`、`human_mode_change_produces_message`、`bot_mode_change_produces_message` 末尾各加：

```rust
        assert_eq!(user_message, Some(messages[0].message.clone()));
```

`bot_joined_from_none_produces_empty` 末尾加：

```rust
        assert_eq!(user_message, None, "anomalous from=None Bot yields no user_message");
```

- [ ] **Step 2: 跑测试确认失败**

Run: `cd src/bcs && cargo test -p bcs-system-message --lib participant_mode_changed`
Expected: FAIL — `user_message` 为 `None`。

- [ ] **Step 3: 实现**

`produce`（:23-80）：把 `content` move 前 clone 给 `user_message`。`from=None` Bot 分支返回 `(vec![], None)`。其余分支 `user_message = Some(content.clone())`，`bot_messages` 在 recipients 空时为 `vec![]` 否则 `vec![msg]`：

```rust
        if *actor_kind == ActorKind::Bot && from.is_none() {
            tracing::warn!(
                "ParticipantModeChanged with from=None for Bot; BotJoined should handle this"
            );
            return (vec![], None);
        }

        let content = match (*actor_kind, *to) {
            (ActorKind::Human, ParticipantMode::Present) => {
                format!("用户 {} 已加入协作群", actor_name)
            }
            (ActorKind::Human, ParticipantMode::Absent) => {
                format!("用户 {} 已退出协作群", actor_name)
            }
            (ActorKind::Bot, ParticipantMode::Muted) => {
                format!("Bot {} 已切换成禁言模式", actor_name)
            }
            (ActorKind::Bot, ParticipantMode::Auto) => {
                format!("Bot {} 已切换成自动发言模式", actor_name)
            }
            _ => format!("{} 的状态变成了 {:?}", actor_name, to),
        };
        let user_message = Some(content.clone());

        let recipients: Vec<String> = participants
            .iter()
            .filter(|p| p.is_bot())
            .map(|p| p.bot_uuid.clone())
            .collect();

        let bot_messages = if recipients.is_empty() {
            vec![]
        } else {
            vec![SystemGroupMessage {
                recipients,
                message: content,
                delivery_type: DeliveryType::Inject,
            }]
        };
        (bot_messages, user_message)
```

- [ ] **Step 4: 跑测试确认通过**

Run: `cd src/bcs && cargo test -p bcs-system-message --lib participant_mode_changed`
Expected: PASS。

- [ ] **Step 5: 提交**

```bash
git add crates/services/bcs-system-message/src/producers/participant_mode_changed.rs crates/services/bcs-system-message/src/producers/participant_mode_changed_test.rs
git commit -m "feat(bcs-system-message): ParticipantModeChanged user_message is the mode-change text"
```

---

## Task 7: `GenericNotification` 产出 `user_message`（空串→None）+ 无收件人边界

**Files:**
- Modify: `crates/services/bcs-system-message/src/producers/generic.rs:20-50`
- Test: 新增内联 `#[cfg(test)]` 模块

**Interfaces:**
- Produces: `user_message = if message.trim().is_empty() { None } else { Some(message.clone()) }`；`bot_messages` 在 recipients 空时为 `vec![]` 但**不**阻止非空 `user_message`。

- [ ] **Step 1: 写失败测试**

`generic.rs` 末尾追加：

```rust
#[cfg(test)]
mod tests {
    use super::*;
    use bcs_domain::{ActorKind, Group, Participant, ParticipantRole};
    use bcs_test_support::NoopBotRegistryCoreService;

    fn group_with(bot: &str) -> Group {
        Group::new("g1", bot, vec![Participant::bot("bot-a", ParticipantRole::Driver)])
    }

    #[tokio::test]
    async fn generic_emits_user_message_equal_to_event_message() {
        let group = group_with("bot-a");
        let event = SystemMessageEvent::GenericNotification {
            group_id: "g1".into(),
            message: "维护开始".into(),
            receivers: vec![],
        };
        let (messages, user_message) = GenericNotificationMessageProducer
            .produce(&event, &group, &NoopBotRegistryCoreService, &group.participants)
            .await;
        assert_eq!(messages.len(), 1);
        assert_eq!(user_message.as_deref(), Some("维护开始"));
    }

    #[tokio::test]
    async fn generic_empty_message_yields_none_user_message() {
        let group = group_with("bot-a");
        let event = SystemMessageEvent::GenericNotification {
            group_id: "g1".into(),
            message: String::new(),
            receivers: vec![],
        };
        let (messages, user_message) = GenericNotificationMessageProducer
            .produce(&event, &group, &NoopBotRegistryCoreService, &group.participants)
            .await;
        assert_eq!(messages.len(), 1);
        assert_eq!(user_message, None, "empty message → None, never Some(\"\")");
    }

    #[tokio::test]
    async fn generic_no_recipients_still_emits_user_message() {
        let group = Group::new("g1", "bot-a", vec![]); // no bots in group
        let event = SystemMessageEvent::GenericNotification {
            group_id: "g1".into(),
            message: "维护开始".into(),
            receivers: vec![],
        };
        let (messages, user_message) = GenericNotificationMessageProducer
            .produce(&event, &group, &NoopBotRegistryCoreService, &group.participants)
            .await;
        assert!(messages.is_empty());
        assert_eq!(user_message.as_deref(), Some("维护开始"));
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `cd src/bcs && cargo test -p bcs-system-message --lib generic`
Expected: FAIL — `user_message` 为 `None`。

- [ ] **Step 3: 实现**

替换 `produce`（:20-50）：

```rust
    async fn produce(
        &self,
        event: &SystemMessageEvent,
        _group: &Group,
        _registry: &dyn BotRegistryCoreService,
        participants: &[Participant],
    ) -> (Vec<SystemGroupMessage>, Option<String>) {
        let SystemMessageEvent::GenericNotification {
            message, receivers, ..
        } = event
        else {
            return (vec![], None);
        };
        let user_message = if message.trim().is_empty() {
            None
        } else {
            Some(message.clone())
        };
        let recipients: Vec<String> = if receivers.is_empty() {
            participants
                .iter()
                .filter(|p| p.is_bot())
                .map(|p| p.bot_uuid.clone())
                .collect()
        } else {
            receivers.iter().map(|p| p.bot_uuid.clone()).collect()
        };
        let bot_messages = if recipients.is_empty() {
            vec![]
        } else {
            vec![SystemGroupMessage {
                recipients,
                message: message.clone(),
                delivery_type: DeliveryType::Inject,
            }]
        };
        (bot_messages, user_message)
    }
```

- [ ] **Step 4: 跑测试确认通过**

Run: `cd src/bcs && cargo test -p bcs-system-message --lib generic`
Expected: PASS。

- [ ] **Step 5: 提交**

```bash
git add crates/services/bcs-system-message/src/producers/generic.rs
git commit -m "feat(bcs-system-message): GenericNotification user_message (empty → None, no-recipient safe)"
```

---

## Task 8: `BotHiddenNotice` 产出 `user_message`

**Files:**
- Modify: `crates/services/bcs-system-message/src/producers/bot_hidden_notice.rs:18-57`
- Test: `crates/services/bcs-system-message/src/producers/bot_hidden_notice.rs`（内联 test 模块）

**Interfaces:**
- Produces: `user_message = Some("xxx 已设置为「不可协作」")`；其他 bot 收件人空时仍 `Some`。

- [ ] **Step 1: 写失败测试**

既有 `produces_notice_for_mentioner_only` 解构改 `let (messages, user_message) = …`，末尾加：

```rust
        assert_eq!(user_message.as_deref(), Some("HiddenBot 已设置为「不可协作」"));
```

并新增“无其他 bot”边界用例（参考既有 group 构造，participants 仅 mentioner）：

```rust
    #[tokio::test]
    async fn bot_hidden_emits_user_message_with_only_mentioner() {
        let registry = NoopBotRegistryCoreService;
        let group = Group {
            id: "g1".into(),
            label: None,
            status: bcs_domain::GroupStatus::Active,
            driver_bot: "bot-driver".into(),
            originator: Some("bot-driver".into()),
            routing_policy: None,
            context: None,
            participants: vec![Participant {
                bot_uuid: "bot-driver".into(),
                bot_name: Some("Driver".into()),
                kind: None,
                role: ParticipantRole::Driver,
                actor_kind: ActorKind::Bot,
                mode: Some(ParticipantMode::Auto),
            }],
            messages: vec![],
            workspace: Default::default(),
            service_group_uuid: None,
            service_mode: None,
            created_at: 0,
            updated_at: 0,
            group_kind: bcs_domain::GroupKind::Normal,
            dm_pair_key: None,
            group_strategy: bcs_domain::GroupStrategy::Chat,
            service_spec: None,
            version: 0,
            record_status: "active".to_string(),
            visibility: "private".to_string(),
        };
        let event = SystemMessageEvent::BotHiddenNotice {
            group_id: "g1".into(),
            mentioner_bot_id: "bot-driver".into(),
            hidden_bot_name: "HiddenBot".into(),
        };
        let (messages, user_message) = BotHiddenNoticeProducer
            .produce(&event, &group, &registry, &group.participants)
            .await;
        // Only mentioner present → 1 Send to mentioner, no Inject to others.
        assert_eq!(messages.len(), 1);
        assert_eq!(messages[0].recipients, vec!["bot-driver"]);
        assert_eq!(user_message.as_deref(), Some("HiddenBot 已设置为「不可协作」"));
    }
```

- [ ] **Step 2: 跑测试确认失败**

Run: `cd src/bcs && cargo test -p bcs-system-message --lib bot_hidden`
Expected: FAIL — `user_message` 为 `None`。

- [ ] **Step 3: 实现**

替换 `produce`（:18-57），`message` move 前 clone 给 `user_message`：

```rust
    async fn produce(
        &self,
        event: &SystemMessageEvent,
        _group: &Group,
        _registry: &dyn BotRegistryCoreService,
        participants: &[Participant],
    ) -> (Vec<SystemGroupMessage>, Option<String>) {
        let SystemMessageEvent::BotHiddenNotice {
            mentioner_bot_id,
            hidden_bot_name,
            ..
        } = event
        else {
            return (vec![], None);
        };

        let message = format!("{} 已设置为「不可协作」", hidden_bot_name);
        let user_message = Some(message.clone());
        let mut messages = vec![SystemGroupMessage {
            recipients: vec![mentioner_bot_id.clone()],
            message: message.clone(),
            delivery_type: DeliveryType::Send,
        }];

        let others: Vec<String> = participants
            .iter()
            .filter(|p| p.bot_uuid != *mentioner_bot_id)
            .filter(|p| p.is_bot())
            .map(|p| p.bot_uuid.clone())
            .collect();
        if !others.is_empty() {
            messages.push(SystemGroupMessage {
                recipients: others,
                message,
                delivery_type: DeliveryType::Inject,
            });
        }

        (messages, user_message)
    }
```

- [ ] **Step 4: 跑测试确认通过**

Run: `cd src/bcs && cargo test -p bcs-system-message --lib bot_hidden`
Expected: PASS。

- [ ] **Step 5: 提交**

```bash
git add crates/services/bcs-system-message/src/producers/bot_hidden_notice.rs
git commit -m "feat(bcs-system-message): BotHiddenNotice user_message is the hidden-notice text"
```

---

## Task 9: SessionContext 共享渲染助手 + Chat 去个性化 WS 文本

**Files:**
- Modify: `crates/services/bcs-system-message/src/producers/session_context.rs`（抽 `base_context_block`/`task_block`/`routing_instruction_block`，重构 `initial_group_context_message` 调用它们；新增 `depersonalized_chat_group_context`；`produce` 后置 `user_message` for Chat）
- Test: `crates/services/bcs-system-message/src/producers/session_context_test.rs`

**Interfaces:**
- Produces:
  - `fn base_context_block(user_context: Option<&str>) -> String` —— `背景: {}\n` 或 `""`。
  - `fn task_block(task_input: Option<&str>) -> String` —— `\n[任务]\n{}\n[/任务]\n` 或 `""`。
  - `fn routing_instruction_block(use_at_mention: bool) -> &'static str` —— 路由说明（@mention 或 bcs_route）。
  - `fn depersonalized_chat_group_context(group, session_id, topic, user_context, use_at_mention, task_input) -> String` —— WS 用：保留 `[GROUP CONTEXT]`/群组ID/会话ID/主题/背景/参与者(roster)/[任务]/路由说明，**删** `你是:`/`你的角色:`/`role_instruction`。
  - T10 会加 MW 版。

- [ ] **Step 1: 写失败测试**

`session_context_test.rs` 既有 Chat 用的 fixture 目前只有 MW。新增 Chat 用例。先把既有 MW 用例解构改 `let (messages, user_message) = SessionContextMessageProducer.produce(...).await`（保留 `messages` 断言）。然后追加 Chat 去个性化用例：

```rust
async fn chat_session_context_produce(group: Group, participants: Vec<Participant>) -> (Vec<SystemGroupMessage>, Option<String>) {
    let registry = NamedRegistry::new(&[]);
    let event = SystemMessageEvent::SessionContext {
        group_id: group.id.clone(),
        session_id: format!("{}:7c18e4be", group.id),
        reason: "普通协作".to_string(),
        session_input: None,
        task_ledger: None,
    };
    SessionContextMessageProducer
        .produce(&event, &group, &registry, &participants)
        .await
}

#[tokio::test]
async fn chat_session_context_user_message_is_depersonalized_group_context() {
    let mut driver = Participant::bot("bot-driver", ParticipantRole::Driver);
    driver.bot_name = Some("Driver".to_string());
    let mut peer = Participant::bot("bot-peer", ParticipantRole::Consultant);
    peer.bot_name = Some("Peer".to_string());
    let mut group = Group::new("group-chat", "bot-driver", vec![driver, peer]);
    group.group_strategy = GroupStrategy::Chat;
    let participants = group.participants.clone();

    let (messages, user_message) = chat_session_context_produce(group, participants).await;

    // bot_messages unchanged: one per bot.
    assert_eq!(messages.len(), 2);
    let ws = user_message.expect("chat SessionContext must emit user_message");

    // Facts preserved.
    assert!(ws.contains("[GROUP CONTEXT]"));
    assert!(ws.contains("[/GROUP CONTEXT]"));
    assert!(ws.contains("群组ID: group-chat"));
    assert!(ws.contains("主题: 普通协作"));
    assert!(ws.contains("参与者:"));
    // Routing instruction preserved (no provider downlink → bcs_route variant).
    assert!(ws.contains("路由工具 (bcs_route)"));
    // Personalization stripped.
    assert!(!ws.contains("你是:"));
    assert!(!ws.contains("你的角色:"));
    assert!(!ws.contains("你是本次协作的 Driver"));
    assert!(!ws.contains("应静默观察"));
    assert!(!ws.contains("等待 @mention"));
}

#[tokio::test]
async fn chat_session_context_user_message_at_mention_when_provider_downlink_present() {
    // NamedRegistry resolves bot-provider as HTTP provider via resolve_delivery_target? 
    // The producer uses contains_provider_downlink_bot which calls resolve_delivery_target.
    // NamedRegistry below overrides resolve_delivery_target to mark bot-provider as HTTP provider.
    let mut driver = Participant::bot("bot-driver", ParticipantRole::Driver);
    driver.bot_name = Some("Driver".to_string());
    let mut provider = Participant::bot("bot-provider", ParticipantRole::Consultant);
    provider.bot_name = Some("Provider".to_string());
    let mut group = Group::new("group-chat", "bot-driver", vec![driver, provider]);
    group.group_strategy = GroupStrategy::Chat;
    let participants = group.participants.clone();

    let registry = NamedRegistry::new(&[]).with_http_provider("bot-provider");
    let event = SystemMessageEvent::SessionContext {
        group_id: group.id.clone(),
        session_id: format!("{}:7c18e4be", group.id),
        reason: "普通协作".to_string(),
        session_input: None,
        task_ledger: None,
    };
    let (_messages, user_message) = SessionContextMessageProducer
        .produce(&event, &group, &registry, &participants)
        .await;
    let ws = user_message.expect("ws text");

    assert!(ws.contains("路由工具 (@mention)"));
    assert!(ws.contains("可@:"));
    assert!(!ws.contains("路由工具 (bcs_route)"));
    assert!(!ws.contains("你是:"));
}

#[tokio::test]
async fn chat_session_context_user_message_renders_without_driver_role_bot() {
    // No participant has the Driver role; driver_bot fallback still renders.
    let mut peer_a = Participant::bot("bot-a", ParticipantRole::Consultant);
    peer_a.bot_name = Some("A".to_string());
    let mut peer_b = Participant::bot("bot-b", ParticipantRole::Consultant);
    peer_b.bot_name = Some("B".to_string());
    let mut group = Group::new("group-chat", "bot-a", vec![peer_a, peer_b]);
    group.group_strategy = GroupStrategy::Chat;
    let participants = group.participants.clone();

    let (messages, user_message) = chat_session_context_produce(group, participants).await;
    assert_eq!(messages.len(), 2);
    let ws = user_message.expect("ws text");
    assert!(ws.contains("[GROUP CONTEXT]"));
    assert!(ws.contains("参与者:"));
    assert!(!ws.contains("你是:"));
}
```

> `NamedRegistry` 当前未实现 `resolve_delivery_target`（用默认 trait 方法？`BotRegistryCoreService` 是否有默认实现？检查：trait 无默认 `resolve_delivery_target`，`NamedRegistry` 在 `session_context_test.rs` 已实现若干方法但 `resolve_delivery_target` 未显式实现会编译失败——实际上 `NamedRegistry` 必须已实现该 trait 的所有方法才能编译现有测试。在 Step 1a 里给 `NamedRegistry` 加 `with_http_provider` 字段并实现 `resolve_delivery_target`。）

**Step 1a:** 在 `session_context_test.rs` 的 `NamedRegistry` 加字段与实现。当前 `NamedRegistry { bots, surfaces }`。加 `http_providers: HashSet<String>` 与 `with_http_provider` builder，并实现 `resolve_delivery_target`：

```rust
struct NamedRegistry {
    bots: HashMap<String, RegisteredBot>,
    surfaces: HashMap<String, CoordinationSurface>,
    http_providers: std::collections::HashSet<String>,
}

impl NamedRegistry {
    fn new(entries: &[(&str, &str, Option<&str>)]) -> Self {
        // ... unchanged bots/surfaces ...
        Self { bots, surfaces, http_providers: std::collections::HashSet::new() }
    }

    fn with_surface(mut self, bot_id: &str, surface: CoordinationSurface) -> Self {
        self.surfaces.insert(bot_id.to_string(), surface);
        self
    }

    fn with_http_provider(mut self, bot_id: &str) -> Self {
        self.http_providers.insert(bot_id.to_string());
        self
    }
}
```

并在 `impl BotRegistryCoreService for NamedRegistry` 内补 `resolve_delivery_target`（若已存在则替换其体以识别 `http_providers`）：

```rust
    async fn resolve_delivery_target(&self, bot_id: &str) -> ServiceResult<bcs_service_api::BotDeliveryTarget> {
        use bcs_service_api::{BotDeliveryTarget, RedactedToken};
        if self.http_providers.contains(bot_id) {
            return Ok(BotDeliveryTarget::HttpProvider {
                bot_id: bot_id.to_string(),
                provider_id: "provider-1".to_string(),
                provider_bot_ref: "ref".to_string(),
                webhook_url: "https://provider.example.com/bcs/webhook".to_string(),
                bcs_to_provider_token: RedactedToken::new("secret"),
                protocol_version: "2.0".to_string(),
            });
        }
        Ok(BotDeliveryTarget::WebSocket { bot_id: bot_id.to_string() })
    }
```

> 若 `NamedRegistry` 当前已实现 `resolve_delivery_target`（返回 WebSocket 之类），改为上述体。若 trait 还有其他未实现方法导致现有编译依赖了别处，按编译器提示补齐——但既有测试已编译，说明 `NamedRegistry` 已实现全部 trait 方法；只需要替换 `resolve_delivery_target` 体并加 `http_providers`/`with_http_provider`。

- [ ] **Step 2: 跑测试确认失败**

Run: `cd src/bcs && cargo test -p bcs-system-message --lib chat_session_context_user_message`
Expected: FAIL — `user_message` 为 `None`（T2 机械化）。

- [ ] **Step 3: 抽共享助手 + 重构 `initial_group_context_message` + 新增 `depersonalized_chat_group_context`**

在 `session_context.rs`，把现有 `initial_group_context_message` 内联的 `base_context`/`task_line`/`role_instruction`/`routing_instruction` 抽为函数，bot 路径与 WS 路径共用：

```rust
fn base_context_block(user_context: Option<&str>) -> String {
    user_context
        .filter(|ctx| !ctx.trim().is_empty())
        .map(|ctx| format!("背景: {}\n", ctx.trim()))
        .unwrap_or_default()
}

fn task_block(task_input: Option<&str>) -> String {
    task_input
        .filter(|task| !task.trim().is_empty())
        .map(|task| format!("\n[任务]\n{}\n[/任务]\n", task.trim()))
        .unwrap_or_default()
}

fn routing_instruction_block(use_at_mention_routing: bool) -> &'static str {
    if use_at_mention_routing {
        "路由工具 (@mention):\n\
           消息中任何 @ 标识都会触发路由，让被 @ 的 Bot 收到消息并被要求响应。\n\
           只有希望某个 Bot 响应时才使用 @，不要用 @ 表示引用、收到或转述某个 Bot 的消息。\n\
           优先使用名称；名称为空、重复或不确定时，使用 Bot ID。"
    } else {
        "路由工具 (bcs_route):\n\
           使用 bcs_route 工具指定下一个响应者（替代 @mention）。\n\
           - to: 目标 Bot 列表，支持按名称或 bot_id 选择\n\
             - 按名称: {\"type\": \"name\", \"value\": \"DBA\"}\n\
             - 按ID: {\"type\": \"bot\", \"value\": \"bot_54123f4f\"}\n\
           - reason: 路由原因"
    }
}
```

重构 `initial_group_context_message` 用它们（bot 消息内容不变——仅是把内联 let 换成函数调用，输出字符串完全相同）：

```rust
fn initial_group_context_message(
    group: &Group,
    session_id: &str,
    recipient: &Participant,
    topic: &str,
    user_context: Option<&str>,
    delivery_type: DeliveryType,
    use_at_mention_routing: bool,
    task_input: Option<&str>,
) -> String {
    let base_context = base_context_block(user_context);
    let task_line = task_block(task_input);
    let role_instruction = match delivery_type {
        DeliveryType::Send => "你是本次协作的 Driver。请介绍协作目标，判断下一步需要谁参与，并开始协调。",
        DeliveryType::Inject if use_at_mention_routing => "你当前通过 chat.inject 收到初始化上下文，应静默观察，不要主动回复；等待 @mention 或任务点名后再响应。",
        DeliveryType::Inject => "你当前通过 chat.inject 收到初始化上下文，应静默观察，不要主动回复；等待 @mention、bcs_route 或任务点名后再响应。",
    };
    let routing_instruction = routing_instruction_block(use_at_mention_routing);
    let roster = if use_at_mention_routing {
        format_roster_with_mentions(group)
    } else {
        format_roster(group)
    };

    format!(
        "[GROUP CONTEXT]\n\
         群组ID: {}\n\
         会话ID: {}\n\
         主题: {}\n\
         {}\
         参与者:\n{}\n\
         {}\
         \n\
         {}\n\
         [/GROUP CONTEXT]\n\
         \n\
         你是: {}\n\
         你的角色: {}\n\
         \n\
         {}",
        group.id, session_id, topic, base_context, roster, task_line,
        routing_instruction,
        display_participant(recipient), role_slug(recipient.role), role_instruction,
    )
}
```

新增 WS 去个性化构造（不依赖 recipient；复用 roster/routing/task/base）：

```rust
/// Depersonalized `[GROUP CONTEXT]` for the frontend WebSocket broadcast:
/// keeps group facts and the lead(Send)-variant routing instruction, strips
/// the recipient-tailored `你是:` / `你的角色:` / role instruction. Rendered
/// purely from group-level state (no recipient dependence, no bot-message
/// iteration order).
fn depersonalized_chat_group_context(
    group: &Group,
    session_id: &str,
    topic: &str,
    user_context: Option<&str>,
    use_at_mention_routing: bool,
    task_input: Option<&str>,
) -> String {
    let base_context = base_context_block(user_context);
    let task_line = task_block(task_input);
    let routing_instruction = routing_instruction_block(use_at_mention_routing);
    let roster = if use_at_mention_routing {
        format_roster_with_mentions(group)
    } else {
        format_roster(group)
    };

    format!(
        "[GROUP CONTEXT]\n\
         群组ID: {}\n\
         会话ID: {}\n\
         主题: {}\n\
         {}\
         参与者:\n{}\n\
         {}\
         \n\
         {}\n\
         [/GROUP CONTEXT]",
        group.id, session_id, topic, base_context, roster, task_line, routing_instruction,
    )
}
```

- [ ] **Step 4: `produce` 后置 Chat `user_message`（MW 仍 None，T10 填）**

`produce`（当前 :27-109）在算完 `bot_messages` 循环后、return 前加 Chat 分支。注意循环里 MW 路径调 `manager_worker_initial_message`、Chat 路径调 `initial_group_context_message`。WS 文本按 strategy 选构造器，参数 group-level，循环外算一次：

```rust
        let user_message = if render_group.group_strategy == GroupStrategy::ManagerWorker {
            // Filled in Task 10.
            None
        } else {
            Some(depersonalized_chat_group_context(
                &render_group,
                session_id,
                reason,
                render_group.context.as_deref(),
                has_provider_downlink_bot,
                task_input_text.as_deref(),
            ))
        };
        (messages, user_message)
```

（`session_id` 是 `SystemMessageEvent::SessionContext { session_id, .. }` 解出的 `&String`，直接传 `session_id`。`reason` 同。`has_provider_downlink_bot` 已算好。）

- [ ] **Step 5: 跑测试确认通过（Chat 部分；MW 用例 user_message 此刻为 None，既有 MW 用例不检 user_message，应仍绿）**

Run: `cd src/bcs && cargo test -p bcs-system-message --lib session_context`
Expected: 3 个新 Chat 用例 PASS；既有 MW 用例 PASS（user_message 未断言）。

> **字节级不变性验证**：重构 `initial_group_context_message` 为调用共享助手后，bot 消息的输出字符串必须与重构前**字节级一致**。既有 `session_context_test.rs` 的 MW 用例断言了 bot 消息的精确内容（含 `你是:`、`你的角色:`、`[SERVICE GROUP CONTEXT]` 等），若重构引入任何格式偏差（多余空格、换行、缩进），这些断言会立即失败。因此 Step 5 全绿即隐式验证了字节级不变性；若需额外保障，可在重构前 `git stash` 后跑一次旧测试记录 baseline 输出，重构后 diff。

- [ ] **Step 6: 提交**

```bash
git add crates/services/bcs-system-message/src/producers/session_context.rs crates/services/bcs-system-message/src/producers/session_context_test.rs
git commit -m "feat(bcs-system-message): depersonalized Chat SessionContext WS text via shared render helpers"
```

---

## Task 10: SessionContext ManagerWorker 去个性化 WS 文本

**Files:**
- Modify: `crates/services/bcs-system-message/src/producers/session_context.rs`（抽 MW 共享块，重构 `manager_worker_initial_message`，新增 `depersonalized_service_group_context`，`produce` MW 分支填 `user_message`）
- Test: `crates/services/bcs-system-message/src/producers/session_context_test.rs`

**Interfaces:**
- Produces:
  - `fn mw_context_block(context: Option<&str>) -> String` —— `\n{}\n` 或 `""`（无条件，非 manager-gated）。
  - `fn mw_task_block(task_input: Option<&str>) -> String` —— `\n[任务]\n{}\n[/任务]\n` 或 `""`（无条件）。
  - `fn mw_status_block(task_ledger: Option<&LedgerSummary>) -> String` —— 经 `format_ledger_status_line`，非空则 `\n{line}`，否则 `""`（无条件）。
  - `fn depersonalized_service_group_context(group, session_id, context, bot_summaries, task_input, task_ledger) -> String` —— 保留 `[SERVICE GROUP CONTEXT]`/群组ID/会话ID/`模式: manager_worker`/参与者(roster_with_role)/背景/[任务]/[任务状态]，**删** `你的角色: manager` 行、协同指令块、尾部 `你是:`/`你的角色:`。

- [ ] **Step 1: 写失败测试**

`session_context_test.rs` 追加 MW 去个性化用例（含“无 Manager 参与者仍渲染事实”与“[任务]/[任务状态] 无条件渲染”）。复用既有 `manager_worker_session_context_messages_with_ledger` 但它返回的是 `messages`（Vec）。新增 helper 返回 tuple，或直接在用例内 produce：

```rust
#[tokio::test]
async fn manager_worker_session_context_user_message_is_depersonalized_service_group_context() {
    let manager_id = "20260416_a5clr6ig:12345678";
    let worker_id = "20260528_vobmrqo6:12345678";
    let mut manager = Participant::bot(manager_id, ParticipantRole::Manager);
    manager.bot_name = Some(manager_id.to_string());
    let mut worker = Participant::bot(worker_id, ParticipantRole::Worker);
    worker.bot_name = Some(worker_id.to_string());
    let mut group = Group::new("851c7a6a-42bc-4be2-8785-1106ef4393a0", manager_id, vec![manager, worker]);
    group.group_strategy = GroupStrategy::ManagerWorker;
    let participants = vec![
        Participant::bot(manager_id, ParticipantRole::Manager),
        Participant::bot(worker_id, ParticipantRole::Worker),
    ];
    let registry = NamedRegistry::new(&[
        (manager_id, "Demo Worker的分身", Some("Demo Worker的分身")),
        (worker_id, "Demo Worker测试0528", None),
    ]);
    let event = SystemMessageEvent::SessionContext {
        group_id: group.id.clone(),
        session_id: format!("{}:7c18e4be", group.id),
        reason: "协作任务".to_string(),
        session_input: Some(serde_json::json!("执行慢查询审计")),
        task_ledger: Some(LedgerSummary {
            pending: vec!["B".to_string()],
            replied: vec!["A".to_string()],
            failed: Vec::new(),
            timed_out: Vec::new(),
        }),
    };

    let (messages, user_message) = SessionContextMessageProducer
        .produce(&event, &group, &registry, &participants)
        .await;
    assert_eq!(messages.len(), 2);
    let ws = user_message.expect("MW SessionContext must emit user_message");

    // Facts preserved.
    assert!(ws.contains("[SERVICE GROUP CONTEXT]"));
    assert!(ws.contains("[/SERVICE GROUP CONTEXT]"));
    assert!(ws.contains("模式: manager_worker"));
    assert!(ws.contains(&format!("群组ID: {}", group.id)));
    assert!(ws.contains("参与者:"));
    assert!(ws.contains("Demo Worker的分身"));
    // [任务] and [任务状态] rendered unconditionally from facts.
    assert!(ws.contains("[任务]"));
    assert!(ws.contains("执行慢查询审计"));
    assert!(ws.contains("[任务状态] 待回复: B | 已回复: A | 失败: - | 超时: -"));
    // Personalization / coordination stripped.
    assert!(!ws.contains("你是:"));
    assert!(!ws.contains("你的角色:"));
    assert!(!ws.contains("[协同提醒]"));
    assert!(!ws.contains("bcs_assign_task"));
    assert!(!ws.contains("bcs_send_task_message"));
}

#[tokio::test]
async fn manager_worker_session_context_user_message_renders_without_manager_participant() {
    // No Manager in roster; facts still render.
    let worker = Participant::bot("worker-only", ParticipantRole::Worker);
    let mut group = Group::new("g-mw", "worker-only", vec![worker]);
    group.group_strategy = GroupStrategy::ManagerWorker;
    let participants = vec![Participant::bot("worker-only", ParticipantRole::Worker)];
    let registry = NamedRegistry::new(&[]);
    let event = SystemMessageEvent::SessionContext {
        group_id: group.id.clone(),
        session_id: format!("{}:7c18e4be", group.id),
        reason: "协作任务".to_string(),
        session_input: Some(serde_json::json!("任务X")),
        task_ledger: None,
    };
    let (_messages, user_message) = SessionContextMessageProducer
        .produce(&event, &group, &registry, &participants)
        .await;
    let ws = user_message.expect("ws text");
    assert!(ws.contains("[SERVICE GROUP CONTEXT]"));
    assert!(ws.contains("模式: manager_worker"));
    assert!(ws.contains("[任务]"));
    assert!(ws.contains("任务X"));
    assert!(!ws.contains("你是:"));
    assert!(!ws.contains("[协同提醒]"));
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `cd src/bcs && cargo test -p bcs-system-message --lib manager_worker_session_context_user_message`
Expected: FAIL — MW `user_message` 仍为 `None`。

- [ ] **Step 3: 抽 MW 共享块 + 重构 + 新增 `depersonalized_service_group_context`**

在 `session_context.rs` 加：

```rust
fn mw_context_block(context: Option<&str>) -> String {
    context
        .filter(|ctx| !ctx.trim().is_empty())
        .map(|ctx| format!("\n{}\n", ctx.trim()))
        .unwrap_or_default()
}

fn mw_task_block(task_input: Option<&str>) -> String {
    task_input
        .filter(|task| !task.trim().is_empty())
        .map(|task| format!("\n[任务]\n{}\n[/任务]\n", task.trim()))
        .unwrap_or_default()
}

fn mw_status_block(task_ledger: Option<&LedgerSummary>) -> String {
    task_ledger
        .map(format_ledger_status_line)
        .filter(|line| !line.is_empty())
        .map(|line| format!("\n{}", line))
        .unwrap_or_default()
}
```

重构 `manager_worker_initial_message` 用它们（bot 消息内容不变——`context_line`/`task_line`/`status_line` 原本 `is_manager`-gated，bot 路径**保持** `is_manager` gating：manager 才显示背景/任务/状态。所以 bot 路径里仍要 gate by `is_manager`，只是把构造字符串的函数抽出，gating 逻辑保留在调用处）：

```rust
fn manager_worker_initial_message(
    group: &Group,
    session_id: &str,
    recipient: &Participant,
    context: Option<&str>,
    delivery_type: DeliveryType,
    bot_summaries: &HashMap<String, String>,
    task_input: Option<&str>,
    task_ledger: Option<&LedgerSummary>,
    coordination_surface: &CoordinationSurface,
) -> String {
    let is_manager = recipient.role == ParticipantRole::Manager;
    let context_line = if is_manager { mw_context_block(context) } else { String::new() };
    let task_line = if is_manager { mw_task_block(task_input) } else { String::new() };
    let role_label = if is_manager { "manager" } else { "worker" };
    let status_line = if is_manager { mw_status_block(task_ledger) } else { String::new() };
    let instruction = manager_worker_coordination_instruction(
        is_manager, delivery_type, coordination_surface, &status_line,
    );

    format!(
        "[SERVICE GROUP CONTEXT]\n\
         群组ID: {}\n\
         会话ID: {}\n\
         模式: manager_worker\n\
         你的角色: {}\n\
         参与者:\n{}\n\
         {}\
         {}\
         {}\n\
         [/SERVICE GROUP CONTEXT]\n\
         \n\
         你是: {}\n\
         你的角色: {}",
        group.id, session_id, role_label,
        format_roster_with_role(group, bot_summaries),
        context_line, task_line, instruction,
        display_participant(recipient), role_label,
    )
}
```

> 验证：原内联 `context_line`/`task_line`/`status_line` 的 `is_manager`-gate 与字符串格式与上述 `mw_*_block` 输出完全一致（背景 `\n{}\n`、任务 `\n[任务]\n{}\n[/任务]\n`、状态 `\n{line}`），因此 bot 消息字节级不变。

新增 WS 去个性化（不 gate by is_manager；不显示 `你的角色:`/`你是:`/协同指令）：

```rust
/// Depersonalized `[SERVICE GROUP CONTEXT]` for the frontend WebSocket
/// broadcast: keeps `模式: manager_work`, roster(name/ID/role/summary),
/// background, `[任务]`, `[任务状态]` rendered unconditionally from facts;
/// strips `你的角色: manager`, the coordination reminder, and the recipient
/// tail `你是:` / `你的角色:`. Rendered purely from group-level facts — even
/// when no Manager is a participant.
fn depersonalized_service_group_context(
    group: &Group,
    session_id: &str,
    context: Option<&str>,
    bot_summaries: &HashMap<String, String>,
    task_input: Option<&str>,
    task_ledger: Option<&LedgerSummary>,
) -> String {
    let context_line = mw_context_block(context);
    let task_line = mw_task_block(task_input);
    let status_line = mw_status_block(task_ledger);

    format!(
        "[SERVICE GROUP CONTEXT]\n\
         群组ID: {}\n\
         会话ID: {}\n\
         模式: manager_worker\n\
         参与者:\n{}\n\
         {}\
         {}\
         {}\n\
         [/SERVICE GROUP CONTEXT]",
        group.id, session_id,
        format_roster_with_role(group, bot_summaries),
        context_line, task_line, status_line,
    )
}
```

- [ ] **Step 4: `produce` MW 分支填 `user_message`**

把 T9 里 `produce` 的 MW 分支 `None` 替换为：

```rust
        let user_message = if render_group.group_strategy == GroupStrategy::ManagerWorker {
            Some(depersonalized_service_group_context(
                &render_group,
                session_id,
                render_group.context.as_deref(),
                &bot_summaries,
                task_input_text.as_deref(),
                task_ledger.as_ref(),
            ))
        } else {
            Some(depersonalized_chat_group_context(
                &render_group,
                session_id,
                reason,
                render_group.context.as_deref(),
                has_provider_downlink_bot,
                task_input_text.as_deref(),
            ))
        };
        (messages, user_message)
```

- [ ] **Step 5: 跑测试确认通过**

Run: `cd src/bcs && cargo test -p bcs-system-message --lib session_context`
Expected: 全部 producer 单测 PASS（含 MW 去个性化、Chat 去个性化、既有 bot 消息字节级断言不变）。

> **字节级不变性验证**：同 T9 Step 5，重构 `manager_worker_initial_message` 为调用 `mw_*_block` 共享助手后，bot 消息的输出必须与重构前字节级一致。既有 MW 用例断言了 bot 消息中 `你是:`、`你的角色: manager`/`worker`、`[协同提醒]` 等精确内容，全绿即验证不变性。特别注意 `mw_context_block`/`mw_task_block`/`mw_status_block` 的输出格式（`\n{}\n`、`\n[任务]\n{}\n[/任务]\n`、`\n{line}`）须与原内联构造完全一致，包括前导换行与尾部换行。

- [ ] **Step 6: 提交**

```bash
git add crates/services/bcs-system-message/src/producers/session_context.rs crates/services/bcs-system-message/src/producers/session_context_test.rs
git commit -m "feat(bcs-system-message): depersonalized ManagerWorker SessionContext WS text"
```

---

## Task 11: dispatcher 逐收件人持久化 + WS 单条 `user_message` 推送

**Files:**
- Modify: `crates/services/bcs-system-message/src/dispatcher.rs`（删 `history_messages_to_persist`/`is_manager_context_message`/`participant_has_role`；重写持久化循环与 WS 段）
- Test: `crates/services/bcs-system-message/src/dispatcher_test.rs`（改写既有持久化用例 + 新增 `RecordingFrontendDeliveryPort` 与 BotJoined WS / 无收件人用例）

**Interfaces:**
- Consumes: T2–T10 的 `(bot_messages, user_message)`；所有 producer 现已产出真实 `user_message`。
- Produces: dispatcher 持久化 = 每 `SystemGroupMessage` 对每个 recipient 写一条 `NewMessage{ owner_bot_id: Some(recipient), content: msg.message, sender_id:"system", sender_type:System, message_type:"system" }`；`recipients` 空 → 入库 0 条。WS = `user_message` 非空时单条 `FrontendDeliveryTarget::Session` 广播；`None`/空 → 不发布。

- [ ] **Step 1: 写失败测试 — 先加 `RecordingFrontendDeliveryPort`，再加新断言**

在 `dispatcher_test.rs` 顶部 import 与 `RecordingMessageRepo` 旁加：

```rust
#[derive(Default, Clone)]
struct RecordingFrontendDeliveryPort {
    published: Arc<Mutex<Vec<bcs_service_api::FrontendDeliveryCommand>>>,
}

#[async_trait]
impl bcs_service_api::FrontendDeliveryPort for RecordingFrontendDeliveryPort {
    async fn publish(
        &self,
        cmd: bcs_service_api::FrontendDeliveryCommand,
    ) -> bcs_service_api::ServiceResult<bcs_service_api::FrontendDeliveryResult> {
        self.published.lock().unwrap().push(cmd.clone());
        Ok(bcs_service_api::FrontendDeliveryResult {
            target: cmd.target,
            delivered: 1,
        })
    }

    async fn unregister_run(&self, _run_id: &str) -> bcs_service_api::ServiceResult<()> {
        Ok(())
    }
}
```

新增 BotJoined 持久化 + WS 用例（用 `RecordingMessageRepo` + `RecordingFrontendDeliveryPort`）：

```rust
#[tokio::test]
async fn dispatch_bot_joined_persists_per_recipient_and_ws_shows_notification_only() {
    let new_bot_id = "new-bot-001".to_string();
    let existing_bot_id = "existing-bot-001".to_string();
    let group = Group {
        id: "group-001".into(),
        label: None,
        status: GroupStatus::Active,
        driver_bot: existing_bot_id.clone(),
        originator: Some(existing_bot_id.clone()),
        routing_policy: None,
        context: None,
        participants: vec![
            Participant {
                bot_uuid: existing_bot_id.clone(),
                bot_name: None, kind: None,
                role: ParticipantRole::Driver,
                actor_kind: ActorKind::Bot,
                mode: Some(ParticipantMode::Auto),
            },
            Participant {
                bot_uuid: new_bot_id.clone(),
                bot_name: None, kind: None,
                role: ParticipantRole::Consultant,
                actor_kind: ActorKind::Bot,
                mode: Some(ParticipantMode::Auto),
            },
        ],
        messages: vec![], workspace: Default::default(),
        service_group_uuid: None, service_mode: None,
        created_at: 0, updated_at: 0,
        group_kind: GroupKind::Normal, dm_pair_key: None,
        group_strategy: GroupStrategy::Chat, service_spec: None,
        version: 0, record_status: "active".to_string(), visibility: "private".to_string(),
    };
    let event = SystemMessageEvent::BotJoined {
        group_id: group.id.clone(),
        actor: Participant {
            bot_uuid: new_bot_id.clone(), bot_name: None, kind: None,
            role: ParticipantRole::Consultant, actor_kind: ActorKind::Bot,
            mode: Some(ParticipantMode::Auto),
        },
    };

    let registry = Arc::new(ProviderTargetRegistry::default());
    let delivery = Arc::new(MockDeliveryPort::default());
    let frontend_delivery = Arc::new(RecordingFrontendDeliveryPort::default());
    let message_repo = Arc::new(RecordingMessageRepo::default());

    let dispatcher = SystemMessageDispatcherImpl::builder()
        .with_registry(registry)
        .with_delivery(delivery.clone())
        .with_frontend_delivery(frontend_delivery.clone())
        .with_message_repo(message_repo.clone())
        .register(BotJoinedMessageProducer::new(Arc::new(bcs_test_support::NoopGroupMessageHistoryService)))
        .build()
        .expect("build dispatcher");

    dispatcher.dispatch(event, &group, "session-test", &group.participants)
        .await.expect("dispatch");

    // Persistence: one record per recipient.
    let appended = message_repo.appended().await;
    assert_eq!(appended.len(), 2);
    let injection = appended.iter().find(|m| m.owner_bot_id.as_deref() == Some(&new_bot_id))
        .expect("new-bot injection record");
    assert_eq!(injection.sender_id, "system");
    assert_eq!(injection.message_type, "system");
    assert!(content_text(injection).contains("你加入了 BCS 协作群."),
        "new-bot context injection persisted under owner=new-bot");
    let notice = appended.iter().find(|m| m.owner_bot_id.as_deref() == Some(&existing_bot_id))
        .expect("existing-bot notification record");
    assert!(content_text(notice).contains("已加入协作群"));
    assert!(!content_text(notice).contains("你加入了 BCS 协作群."));

    // WS: exactly one publish, content = user_message (join notification),
    // NOT the new-bot context injection.
    let published = frontend_delivery.published.lock().unwrap();
    assert_eq!(published.len(), 1, "WS publishes a single user_message");
    let payload = &published[0].event_json;
    assert!(payload.contains("已加入协作群"));
    assert!(!payload.contains("你加入了 BCS 协作群."),
        "WS must not leak the new-bot context injection");
}
```

新增无收件人 → 入库 0 条、WS 仍推送用例（用 `BotLeftMessageProducer` + 单 participant 退群）：

```rust
#[tokio::test]
async fn dispatch_bot_left_with_no_recipients_persists_zero_but_pushes_ws() {
    let leaving = "bot-only".to_string();
    let group = Group {
        id: "group-left".into(), label: None, status: GroupStatus::Active,
        driver_bot: leaving.clone(), originator: Some(leaving.clone()),
        routing_policy: None, context: None,
        participants: vec![Participant {
            bot_uuid: leaving.clone(), bot_name: Some("Solo".into()), kind: None,
            role: ParticipantRole::Driver, actor_kind: ActorKind::Bot,
            mode: Some(ParticipantMode::Auto),
        }],
        messages: vec![], workspace: Default::default(),
        service_group_uuid: None, service_mode: None,
        created_at: 0, updated_at: 0, group_kind: GroupKind::Normal,
        dm_pair_key: None, group_strategy: GroupStrategy::Chat, service_spec: None,
        version: 0, record_status: "active".to_string(), visibility: "private".to_string(),
    };
    let event = SystemMessageEvent::BotLeft {
        group_id: group.id.clone(),
        actor: Participant {
            bot_uuid: leaving.clone(), bot_name: Some("Solo".into()), kind: None,
            role: ParticipantRole::Driver, actor_kind: ActorKind::Bot, mode: None,
        },
    };
    let registry = Arc::new(ProviderTargetRegistry::default());
    let delivery = Arc::new(MockDeliveryPort::default());
    let frontend_delivery = Arc::new(RecordingFrontendDeliveryPort::default());
    let message_repo = Arc::new(RecordingMessageRepo::default());

    let dispatcher = SystemMessageDispatcherImpl::builder()
        .with_registry(registry)
        .with_delivery(delivery)
        .with_frontend_delivery(frontend_delivery.clone())
        .with_message_repo(message_repo.clone())
        .register(crate::producers::bot_left::BotLeftMessageProducer)
        .build()
        .expect("build dispatcher");

    dispatcher.dispatch(event, &group, "session-left", &group.participants)
        .await.expect("dispatch");

    assert_eq!(message_repo.appended().await.len(), 0, "no recipients → 0 persisted records");
    let published = frontend_delivery.published.lock().unwrap();
    assert_eq!(published.len(), 1);
    assert!(published[0].event_json.contains("已退出协作群"));
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `cd src/bcs && cargo test -p bcs-system-message --lib dispatch_bot_joined_persists_per_recipient dispatch_bot_left_with_no_recipients`
Expected: FAIL — dispatcher 仍走旧 `messages[0]` 持久化（owner=None）与旧 WS 拼接。

- [ ] **Step 3: 重写 dispatcher 持久化循环**

`dispatcher.rs`：把 `_user_message` 解构改回 `user_message`（:183）：

```rust
        let (bot_messages, user_message) = producer.produce(&event, group, self.registry.as_ref(), participants).await;
```

替换持久化块（:189-219）为逐 recipient 展开：

```rust
        // Persist system messages per recipient: one record per recipient with
        // owner_bot_id = recipient, content = the original message. Empty
        // recipients → no record (e.g. last bot leaving). user_message is NOT
        // persisted (frontend-only).
        if let Some(ref repo) = self.message_repo {
            let mut persisted_count = 0usize;
            for msg in &bot_messages {
                for recipient in &msg.recipients {
                    let new_msg = NewMessage {
                        group_id: group.id.clone(),
                        session_id: session_id.to_string(),
                        sender_id: "system".to_string(),
                        sender_type: SenderType::System,
                        message_type: "system".to_string(),
                        content: serde_json::Value::String(msg.message.clone()),
                        client_msg_id: None,
                        owner_bot_id: Some(recipient.clone()),
                        created_at: now_ms(),
                        run_id: String::new(),
                    };
                    if let Err(e) = repo.append_message(new_msg).await {
                        tracing::warn!(
                            group_id = %group.id, error = %e,
                            "failed to persist system message to message store"
                        );
                    } else {
                        persisted_count += 1;
                    }
                }
            }
            tracing::info!(group_id = %group.id, count = persisted_count, "system message persisted");
        }
```

- [ ] **Step 4: 重写 WS 推送段**

替换 :365-390 整段为单条 `user_message` 推送：

```rust
        // Publish the user-facing text to frontend WebSocket clients (single
        // session-level broadcast; NOT persisted). bot_messages are never
        // broadcast to the frontend.
        if let Some(content) = user_message.filter(|s| !s.trim().is_empty()) {
            let event_json = build_frontend_system_event_frame(&group.id, content, session_id);
            let target = FrontendDeliveryTarget::Session { session_id: session_id.to_string() };
            if let Err(e) = self.frontend_delivery.publish(FrontendDeliveryCommand {
                target,
                event_json,
                delivery_kind: FrontendDeliveryKind::WorkbenchEvent,
                run_fallback: None,
                exclude_conn_id: None,
            }).await {
                tracing::warn!(
                    group_id = %group.id, %session_id, error = %e,
                    "system message frontend delivery failed"
                );
            }
        }
```

- [ ] **Step 5: 删除 `history_messages_to_persist` / `is_manager_context_message` / `participant_has_role`**

删除 `dispatcher.rs` 中这三个自由函数（:447-493 整段），它们不再被引用。

- [ ] **Step 6: 改写既有 dispatcher 持久化用例**

`dispatch_manager_worker_session_context_persists_worker_private_context`（:312-371）：把对“global manager context(owner=None)”的断言改为 owner=manager：

```rust
    let appended = message_repo.appended().await;
    assert_eq!(appended.len(), 2);

    let manager_context = appended
        .iter()
        .find(|msg| msg.owner_bot_id.as_deref() == Some("bot-manager"))
        .expect("manager-owned context record");
    assert_eq!(manager_context.group_id, group.id);
    assert_eq!(manager_context.session_id, session_id);
    assert_eq!(manager_context.sender_id, "system");
    assert_eq!(manager_context.message_type, "system");
    assert!(content_text(manager_context).contains("你的角色: manager"));

    let worker_context = appended
        .iter()
        .find(|msg| msg.owner_bot_id.as_deref() == Some("bot-worker"))
        .expect("worker-owned context record");
    assert_eq!(worker_context.sender_id, "system");
    assert_eq!(worker_context.message_type, "system");
    assert!(content_text(worker_context).contains("你的角色: worker"));
```

`dispatch_manager_worker_session_context_persists_each_worker_private_context`（:373-426）：改为每个 bot（manager + 两 worker）一条 owner=self 记录：

```rust
    let appended = message_repo.appended().await;
    assert_eq!(appended.len(), 3);
    assert!(
        appended.iter().all(|msg| msg.owner_bot_id.is_some()),
        "no global (owner=None) record; each bot owns its context copy"
    );
    let manager_ctx = appended.iter()
        .find(|msg| msg.owner_bot_id.as_deref() == Some("bot-manager"))
        .expect("manager copy");
    assert!(content_text(manager_ctx).contains("你的角色: manager"));
    for worker_id in ["bot-worker-a", "bot-worker-b"] {
        let worker_context = appended.iter()
            .find(|msg| msg.owner_bot_id.as_deref() == Some(worker_id))
            .unwrap_or_else(|| panic!("worker-owned context for {worker_id}"));
        assert!(content_text(worker_context).contains("你的角色: worker"));
    }
```

`dispatch_non_manager_worker_session_context_persists_single_global_record`（:428-471）改名为 `..._persists_per_recipient_records`，断言改为：

```rust
    let appended = message_repo.appended().await;
    assert_eq!(appended.len(), 2);
    assert_eq!(
        appended.iter().filter(|m| m.owner_bot_id.is_none()).count(),
        0,
        "no global record; each recipient owns a copy"
    );
    for owner in ["bot-driver", "bot-consultant"] {
        let rec = appended.iter()
            .find(|m| m.owner_bot_id.as_deref() == Some(owner))
            .unwrap_or_else(|| panic!("owner record for {owner}"));
        assert!(content_text(rec).contains("[GROUP CONTEXT]"));
    }
```

`dispatch_manager_worker_generic_system_message_persists_single_global_record`（:515-553）：`FixedProducer` 返回 recipient=`bot-provider`。断言改为 owner=`bot-provider`：

```rust
    let appended = message_repo.appended().await;
    assert_eq!(appended.len(), 1);
    assert_eq!(appended[0].owner_bot_id.as_deref(), Some("bot-provider"));
    assert_eq!(content_text(&appended[0]), "member changed");
```

`dispatch_manager_worker_session_context_does_not_make_worker_context_public`（:473-513）：用 `WorkerOnlySessionContextProducer`（T2 后返回 `(vec![recipient=bot-worker "worker-only context"], None)`）。断言已为 owner=Some("bot-worker")、content="worker-only context" —— **保持不变**（仍 PASS）。无需改。

`dispatch_session_context_preserves_manager_worker_group_type`（:263-310）与 `dispatch_bot_joined_delivers_to_all_participants`（:170-261）：未设 message_repo/无 `RecordingFrontendDeliveryPort`，不检持久化/WS 内容，**保持不变**仍 PASS。

- [ ] **Step 7: 跑全 dispatcher 测试确认通过**

Run: `cd src/bcs && cargo test -p bcs-system-message --lib`
Expected: PASS（含新 BotJoined/无收件人 WS 用例、改写的 MW/Chat 持久化用例）。

- [ ] **Step 8: 提交**

```bash
git add crates/services/bcs-system-message/src/dispatcher.rs crates/services/bcs-system-message/src/dispatcher_test.rs
git commit -m "feat(bcs-system-message): per-recipient system-message ownership + WS single user_message push"
```

---

## Task 12: `compute_session_history_query` viewer 分支接入 `PublicOrOwner`

**Files:**
- Modify: `crates/services/bcs-message/src/lib.rs`（`compute_session_history_query` MW manager 与非 MW Chat 分支；新增 `chat_owner_filter_for_view` 共享 helper；legacy session 分支与 MW worker 不变）
- Test: `crates/services/bcs-message/src/lib.rs` 内联 test 模块
- Verify: `crates/application/v1/bcs-app-session/tests/v1_session_service.rs`（无源码改动，跑通即可）

**Interfaces:**
- Produces: `MessageService::chat_owner_filter_for_view(view_bot_id: Option<&str>) -> MessageOwnerFilter`：
  - `Some(v)` 且 `!v.is_empty()` 且 `!v.starts_with("human_")` → `PublicOrOwner(v)`
  - 否则 → `IsNull`
  T13 的 `get_history` 复用此 helper。

- [ ] **Step 1: 写失败测试**

既有 `chat_history_uses_chat_cutoff_and_keeps_owner_filter_disabled`（:798-812）断言 owner=worker-a 的消息在 worker-a view 可见。在新语义下仍可见（`PublicOrOwner(worker-a)` 命中 owner=worker-a）。但需补“看不到他人副本”断言并改名。追加新用例（追加到 test 模块末尾，:1119 后）：

```rust
    #[tokio::test]
    async fn chat_bot_viewer_sees_public_and_own_system_copies_not_others() {
        let (service, repo, _sessions, fallback, session_id) =
            service_fixture(GroupStrategy::Chat, 0, u64::MAX, Vec::new()).await;
        append_history(&repo, "group-1", &session_id, "human_1", "public-human", None).await;
        append_history(&repo, "group-1", &session_id, "system", "sys-to-worker-a", Some("worker-a")).await;
        append_history(&repo, "group-1", &session_id, "system", "sys-to-worker-b", Some("worker-b")).await;

        // worker-a view: public + own system copy; NOT worker-b's copy.
        let res_a = service
            .get_session_history(session_cmd("group-1", &session_id, Some("worker-a")))
            .await
            .expect("worker-a chat history");
        let contents_a: Vec<&str> = res_a.messages.iter().map(|m| m.content.as_str()).collect();
        assert!(contents_a.contains(&"public-human"));
        assert!(contents_a.contains(&"sys-to-worker-a"));
        assert!(!contents_a.contains(&"sys-to-worker-b"),
            "other bot's system copy must be hidden under PublicOrOwner");

        // no view_bot_id: only public (IsNull).
        let res_none = service
            .get_session_history(session_cmd("group-1", &session_id, None))
            .await
            .expect("public chat history");
        let contents_none: Vec<&str> = res_none.messages.iter().map(|m| m.content.as_str()).collect();
        assert!(contents_none.contains(&"public-human"));
        assert!(!contents_none.contains(&"sys-to-worker-a"));
        assert!(!contents_none.contains(&"sys-to-worker-b"));
        let _ = fallback;
    }

    #[tokio::test]
    async fn mw_manager_viewer_sees_public_and_own_system_copies() {
        let (service, repo, _sessions, _fallback, session_id) =
            service_fixture(GroupStrategy::ManagerWorker, 0, 0, Vec::new()).await;
        append_history(&repo, "group-1", &session_id, "human_1", "public-human", None).await;
        append_history(&repo, "group-1", &session_id, "system", "sys-to-manager", Some("mgr")).await;
        append_history(&repo, "group-1", &session_id, "system", "sys-to-worker-a", Some("worker-a")).await;

        let res = service
            .get_session_history(session_cmd("group-1", &session_id, Some("mgr")))
            .await
            .expect("manager history");
        let contents: Vec<&str> = res.messages.iter().map(|m| m.content.as_str()).collect();
        assert!(contents.contains(&"public-human"));
        assert!(contents.contains(&"sys-to-manager"),
            "manager now sees own system copy under PublicOrOwner(mgr)");
        assert!(!contents.contains(&"sys-to-worker-a"));
    }
```

既有 `manager_worker_manager_view_reads_public_rows_after_cutoff`（:1045）appended 只有 owner=None + worker 副本，mgr view 在新旧语义下都只见 2 条 public —— **保持不变**仍 PASS（无需改；新 MW manager 用例已覆盖 owner=mgr 可见）。

- [ ] **Step 2: 跑测试确认失败**

Run: `cd src/bcs && cargo test -p bcs-message --lib chat_bot_viewer_sees_public_and_own_system_copies_not_others mw_manager_viewer_sees_public_and_own_system_copies`
Expected: FAIL — 当前非 MW 恒 `Any`（worker-a view 能看到 worker-b 副本）；MW manager 恒 `IsNull`（看不到 sys-to-manager）。

- [ ] **Step 3: 加共享 helper + 改 compute 分支**

在 `impl MessageService` 内（`compute_session_history_query` 旁）加：

```rust
    /// Chat/non-MW viewer → owner filter: a bot viewer reads public messages
    /// plus its own system-message copies (`PublicOrOwner`); no view_bot_id or
    /// a `human_*` viewer reads public-only (`IsNull`). Membership is NOT
    /// verified here (mirrors the existing chat-branch behavior).
    pub fn chat_owner_filter_for_view(view_bot_id: Option<&str>) -> MessageOwnerFilter {
        match view_bot_id {
            Some(v) if !v.is_empty() && !v.starts_with("human_") => {
                MessageOwnerFilter::PublicOrOwner(v.to_string())
            }
            _ => MessageOwnerFilter::IsNull,
        }
    }
```

改 `compute_session_history_query`（:167-193）—— MW 分支 Public→按 viewer 是否真 bot 分流；非 MW 分支 owner_filter 用 helper（`visible_from_seq` 计算不变）：

```rust
    pub fn compute_session_history_query(
        group: &Group,
        session: &Session,
        view_bot_id: Option<&str>,
        new_participant_visible_limit: u64,
    ) -> Result<(MessageOwnerFilter, Option<i64>), GroupUseCaseError> {
        let is_manager_worker = group.group_strategy == GroupStrategy::ManagerWorker;
        if is_manager_worker {
            let view = Self::manager_worker_history_view(group, session, view_bot_id)?;
            let owner_filter = match view {
                ManagerWorkerHistoryView::Worker(worker_id) => MessageOwnerFilter::Eq(worker_id),
                ManagerWorkerHistoryView::Public => match view_bot_id {
                    // Non-worker bot viewer (the manager) reads public + own copies.
                    Some(v) if !v.is_empty() && !v.starts_with("human_") => {
                        MessageOwnerFilter::PublicOrOwner(v.to_string())
                    }
                    _ => MessageOwnerFilter::IsNull,
                },
            };
            Ok((owner_filter, None))
        } else {
            let visible_from_seq = match view_bot_id {
                Some(view_bot_id) => Self::compute_visible_from_seq(
                    session.participant_join_seq.as_ref(),
                    session.current_msg_seq,
                    view_bot_id,
                    new_participant_visible_limit,
                ),
                None => None,
            };
            Ok((Self::chat_owner_filter_for_view(view_bot_id), visible_from_seq))
        }
    }
```

同步更新该函数上方的文档注释（:154-166）：把 “non-worker/manager viewer → `(IsNull, None)`” 与 “Chat/other → `(Any, visible_from_seq)`” 改为 “non-worker bot manager viewer → `(PublicOrOwner(view), None)`; 无/human → `(IsNull, None)`” 与 “Chat/other bot viewer → `(PublicOrOwner(view), visible_from_seq)`; 无/human → `(IsNull, visible_from_seq)`”。

> legacy session 分支（`:481-499`）的 Chat→`Any`、MW→`IsNull`/`Eq` 是**老 group panel-anchor 查询**（非 compute 口径），spec 未要求改动，保持现状。

- [ ] **Step 4: 既有 `chat_history_uses_chat_cutoff_and_keeps_owner_filter_disabled` 是否仍绿**

该用例 append owner=worker-a 的消息并以 worker-a view 查 → `PublicOrOwner(worker-a)` 命中 → 仍可见 1 条。**保持不变**，PASS。但语义已变（owner 过滤启用）；不必改名（断言未失效）。

- [ ] **Step 5: 跑 bcs-message 全测试 + V1 facade 测试**

Run: `cd src/bcs && cargo test -p bcs-message && cargo test -p bcs-app-session`
Expected: PASS。

> V1 `bcs-app-session` 的 MW authz 用例（`human_caller_can_explicitly_select_an_owned_bot_message_view` 等）走 `compute_session_history_query`：worker-a view → `Eq(worker-a)`（不变，PASS）；MW manager/driver view（若有）→ `PublicOrOwner`，但 V1 fixture 未 seed manager/driver 的 owner 副本，结果与旧 `IsNull` 一致（只见 owner=None），PASS。Chat V1 用例 owner 全为 None → `PublicOrOwner`/`IsNull` 均命中 owner=None，PASS。若某 V1 用例因新语义红，按实测调整断言（预期仅 owner 副本可见性相关，且 fixture 无此类 seed）。

- [ ] **Step 5a: 更新 V1 facade 注释**

`crates/application/v1/bcs-app-session/src/lib.rs` 约 :822-826 处有注释引用
`MessageOwnerFilter` 语义（含 "incl. ManagerWorker public-only `IsNull`"），
T12 将 MW manager 从 `IsNull` 改为 `PublicOrOwner` 后该注释过时。定位该注释，
将 `IsNull` 相关描述更新为 `PublicOrOwner`（如 "incl. ManagerWorker
manager-viewer `PublicOrOwner`"），保持注释与 `compute_session_history_query`
的新行为一致。此为纯注释变更，不影响编译与测试。

- [ ] **Step 6: 提交**

```bash
git add crates/services/bcs-message/src/lib.rs crates/application/v1/bcs-app-session/src/lib.rs
git commit -m "feat(bcs-message): PublicOrOwner viewer scoping in compute_session_history_query"
```

---

## Task 13: 老 group `get_history` Chat 新路径接入 owner 过滤

**Files:**
- Modify: `crates/services/bcs-message/src/lib.rs:325-385`（`get_history` Chat new-path）
- Test: `crates/services/bcs-message/src/lib.rs` 内联 test 模块

**Interfaces:**
- Consumes: T12 的 `MessageService::chat_owner_filter_for_view`。
- Produces: `get_history` Chat new-path 的 `MessageQuery.owner_filter` 由硬编码 `Any` 改为 `chat_owner_filter_for_view(cmd.view_bot_id.as_deref())`；`visible_from_seq` 维持 `None`（老接口既有行为，无 session 不可算 `visible_from_seq`）；legacy 回退路径与空结果 fallback 分支不变。

- [ ] **Step 1: 写失败测试**

追加到 test 模块末尾（用 `group_cmd` helper，Chat strategy，cutoff=0 走新路径）：

```rust
    #[tokio::test]
    async fn get_history_chat_view_bot_id_now_filters_by_public_or_owner() {
        let (service, repo, _sessions, _fallback, _session_id) =
            service_fixture(GroupStrategy::Chat, 0, u64::MAX, Vec::new()).await;
        // group-1 is the fixture group; append to its session_id-or-empty? 
        // get_history new-path uses session_id "" (see query builder). Seed via repo directly
        // with session_id "" to match get_history's query.
        let gid = "group-1";
        append_history(&repo, gid, "", "human_1", "public-human", None).await;
        append_history(&repo, gid, "", "system", "sys-to-a", Some("worker-a")).await;
        append_history(&repo, gid, "", "system", "sys-to-b", Some("worker-b")).await;

        // Regression: view_bot_id was previously ignored (hardcoded Any).
        let res_a = service
            .get_history(group_cmd(gid, Some("worker-a")))
            .await
            .expect("worker-a group history");
        let contents_a: Vec<&str> = res_a.messages.iter().map(|m| m.content.as_str()).collect();
        assert!(contents_a.contains(&"public-human"));
        assert!(contents_a.contains(&"sys-to-a"));
        assert!(!contents_a.contains(&"sys-to-b"),
            "get_history must now honor view_bot_id (was hardcoded Any)");

        let res_none = service
            .get_history(group_cmd(gid, None))
            .await
            .expect("public group history");
        let contents_none: Vec<&str> = res_none.messages.iter().map(|m| m.content.as_str()).collect();
        assert!(contents_none.contains(&"public-human"));
        assert!(!contents_none.contains(&"sys-to-a"));
        assert!(!contents_none.contains(&"sys-to-b"));
    }
```

> 注意：`get_history` 新路径的 `MessageQuery.session_id` 恒为 `String::new()`（见 :334），所以 seed 也用 `""`。`append_history` 第三参 session_id 传 `""`。fixture 的 `service_fixture` 用 cutoff=0 使 Chat group 走新路径（`should_use_new_path` 返回 true）。

- [ ] **Step 2: 跑测试确认失败**

Run: `cd src/bcs && cargo test -p bcs-message --lib get_history_chat_view_bot_id_now_filters_by_public_or_owner`
Expected: FAIL — 当前 `owner_filter: MessageOwnerFilter::Any`，worker-a view 能看到 worker-b 副本。

- [ ] **Step 3: 实现**

`get_history` 新路径（:332-343）的 `MessageQuery` 构造，把 `owner_filter: MessageOwnerFilter::Any` 改为：

```rust
            let owner_filter = Self::chat_owner_filter_for_view(cmd.view_bot_id.as_deref());
            let query = MessageQuery {
                group_id: cmd.group_id.clone(),
                session_id: String::new(),
                cursor: cmd.before,
                limit: self.effective_limit(cmd.limit),
                keyword: None,
                sender_id: None,
                message_type: None,
                owner_filter,
                time_range: None,
                visible_from_seq: None,
            };
```

`visible_from_seq` 维持 `None`（spec: get_history 无 session，沿用现状不计新参与者可见起点）。

- [ ] **Step 4: 跑全 bcs-message 测试确认通过**

Run: `cd src/bcs && cargo test -p bcs-message`
Expected: PASS（含新 get_history 回归用例，既有 manager_worker_group_history_is_rejected 等不回归）。

> 派生影响确认：`BotJoinedMessageProducer::fetch_history`（`producers/bot_joined.rs:190-215`）以 `view_bot_id = driver` 调 `get_history`，owner 过滤生效后取到“driver 视角”历史（公共 + driver 副本），不再无差别全量——spec 记为可接受派生变化，无需断言改动。bot_joined_test 不据此断言，PASS。

- [ ] **Step 5: 提交**

```bash
git add crates/services/bcs-message/src/lib.rs
git commit -m "fix(bcs-message): GET /groups/{id}/messages honors view_bot_id via PublicOrOwner"
```

---

## Task 14: conformance 确认 + 全量验证

**Files:**
- Verify: `crates/services/bcs-system-message/tests/conformance_system_message.rs`（无源码改动，确认 6 条用例编译/通过）
- Verify: `crates/test-support/bcs-test-support/src/contract/core/mod.rs`（helper 仍是 `kind()` 锚点）

**Interfaces:** 无新增。

- [ ] **Step 1: 跑 system-message conformance 与相关 crate 全测试**

Run:
```bash
cd src/bcs
cargo test -p bcs-system-message
cargo test -p bcs-message
cargo test -p bcs-message-store
cargo test -p bcs-service-api
cargo test -p bcs-test-support
cargo test -p bcs-app-session
```
Expected: 全 PASS。`bcs-system-message` 的 `tests/conformance_system_message.rs` 6 条（5 per-producer + 1 dispatcher + 1 service-impl）在 tuple 签名下编译通过（helper 只调 `kind()`，trait `kind()` 未变；`NoopSystemMessageProducer` 已在 T2 返回 `(vec![], None)`）。

- [ ] **Step 2: workspace 编译 + 受影响下游编译**

Run:
```bash
cd src/bcs
cargo build --workspace
```
Expected: 成功。重点确认：所有 `match MessageOwnerFilter` 站点（mysql `:304`/`:446`、memory `:122`/`:200`、contract/repo 用例、`bcs-collaboration-runtime/tests/runtime_progression.rs` 只用 `Any` 不 match 全 enum 故不受影响）均覆盖 `PublicOrOwner`。

- [ ] **Step 3: 派生影响回归 — collaboration-runtime 的 panel 历史**

Run: `cd src/bcs && cargo test -p bcs-collaboration-runtime`
Expected: PASS（runtime_progression 用 `MessageOwnerFilter::Any` 查 panel，未 match 全 enum，不受 `PublicOrOwner` 影响）。

- [ ] **Step 4: 预推送门（按 `AGENTS.md`）**

Run:
```bash
cd src/bcs
OCB_PRE_PUSH_RUN_CI=1 git push  # 仅当用户要求推送时执行；否则跳过
```
> 默认 pre-push 仅跑 lint(SAST)。若需跑完整模块门，设 `OCB_PRE_PUSH_RUN_CI=1`；merge target 默认 `origin/dev`，可用 `AVERNET_PRE_PUSH_MERGE_TARGET` 临时覆盖。本计划不强制推送——由用户决定是否开 PR。

- [ ] **Step 5: 提交（无源码改动则跳过 commit；若有 conformance helper 文档微调则提交）**

若 Step 1–3 全绿且无源码改动，本任务无 commit。若期间发现需补 conformance helper 文档注释，单独 `docs(bcs-system-message): note tuple signature anchor in conformance helper` 提交。

---

## 自检（spec 覆盖核对）

- 契约 tuple 签名 + 文档注释 → T2。
- 7 producer `user_message` 规则表 → T3–T8(bot_left/joined/human/participant/generic/bot_hidden)、T9–T10(session_context Chat & MW)。
- 空/边界：GenericNotification 空串→None(T7)；BotLeft 无收件人仍 Some(T3)；Chat 无 lead 渲染(T9)；无收件人 dispatcher 入库 0 + WS 推送(T11)。
- SessionContext 去个性化 Chat(T9) / MW(T10)：共享渲染助手、删 `你是:` / `你的角色:` / 角色指令 / 协同指令、保留路由/roster/背景/`[任务]`/`[任务状态]`、不依赖收件人、`[任务]`/`[任务状态]` 无条件渲染、bot 消息字节不变。`producers/mod.rs` 共享助手为模块内自由函数（T9/T10）；spec “re-export” 非必须（producer 内复用）。
- dispatcher 逐 recipient 持久化 owner=recipient(T11)、删 `messages[0]`/MW 特化(T11)、user_message 不入库(T11)、WS 单条推送(T11)、best-effort warn 不变(T11)。
- `MessageOwnerFilter::PublicOrOwner` 变体 + mysql/memory 谓词(T1)、其他 match 站点同步(T1 之 mysql/memory；contract/repo 用例 T1；runtime_progression 不 match 全 enum 无需改)。
- `compute_session_history_query` viewer 分支表（MW worker Eq 不变、MW manager PublicOrOwner、MW 无/human IsNull、非 MW bot PublicOrOwner、非 MW 无/human IsNull）→ T12。`compute_visible_from_seq` 不变(T12 保留)。V1 facade 共享口径不另设规则(T12 Step 5 验证)。
- 老 group `get_history` Chat 新路径 view_bot_id 生效(T13)；legacy 回退与空结果 fallback 不变(T13)。
- 历史数据兼容：`IS NULL OR owner=?` 谓词超集(T1)，无治理、无时间切分（计划无迁移任务）。
- 测试桩 `NoopSystemMessageProducer`(T2)、conformance helper(T2)、`dispatcher_test.rs` 4 stub(T2) + 用例改写(T11)、`tests/conformance_system_message.rs`(T14 确认)。
- 非目标：WS tab 路由、bot 投递内容/transport/run-context、HTTP 参数鉴权、group callback 路径——计划均不触碰。
