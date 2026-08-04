# 系统消息按收件 bot 归属持久化与用户可见消息设计

日期: 2026-08-03
分支: fix/wrong-system-message
范围: `bcs-service-api`(契约)、`bcs-system-message`(producer + dispatcher)、`bcs-message`(历史查询)、`bcs-message-store`(owner 过滤实现)、`bcs-domain`(MessageOwnerFilter)、`bcs-test-support`(测试桩)

## 背景与问题

`SystemMessageDispatcherService::dispatch` 目前调用
`SystemMessageProducerService::produce` 只拿到一份发给各 bot 的
`Vec<SystemGroupMessage>`，随后两条"面向用户"的通道都直接复用这份 bot 消息，
产出错误内容:

1. **历史持久化**(`crates/services/bcs-system-message/src/dispatcher.rs:189`):
   `history_messages_to_persist` 取 `messages[0]` 作为全局历史记录
   (`owner_bot_id = None`)。BotJoined 时 `messages[0]` 是发给新 bot 的整段
   上下文注入消息(群 ID、参与者、历史消息列表)，被当作"系统通知"存入历史;
   Chat 类 SessionContext 时 `messages[0]` 是某个随机 bot 的个性化
   `[GROUP CONTEXT]`(内含"你是: xxx")。前端历史消息接口因此展示的是
   bot 视角的内容。
2. **前端 WS 推送**(`crates/services/bcs-system-message/src/dispatcher.rs:366`):
   对所有 bot 消息按文本去重后用 `"\n"` 拼接成一条 chat event 广播给整个
   session，把 bot 的注入/上下文消息(如各 bot 的 `[GROUP CONTEXT]`)直接暴露
   给前端用户。

目标(参照 manager_worker 现有实现推广): bot、human 发送的普通消息只按
`sender_id` 区分(`owner_bot_id = None` 继续共享可见); system 消息的 sender
恒为 `"system"`，无法区分收件人，因此**按 `SystemGroupMessage.recipients` 逐
收件人写副本、`owner_bot_id = 收件 bot uuid` 标记"这条系统消息是发给哪个 bot
的"**。历史查询按请求带入的 `view_bot_id` 返回"公共消息 + 发给该 bot 的系统
消息"。前端 WS 因无法按 tab 路由，单独推送一条用户可见文本。

## 架构约束

前端 WS 连接模型(`adapters/ws/bcs-ws/src/web/`)无法区分"哪个 bot 的 tab":

- `/ws`(前端)的 `bound_actor_id` 在 upgrade 时由 cookie 认证链解析为
  `human_{staff_no}`(`crates/bootstrap/bcs/src/server.rs` 的
  `ws_upgrade_handler`，匿名则为 `None`)，标识的是人，不是 bot;
- workbench 客户端以 bot 身份发言是消息级行为(`chat.send` 帧参数
  `bot_id`/`bot_uuid`，同一连接不同帧可扮演不同 bot);bot 自己的 WS 走
  `/ws/bot`(token 认证)，是完全不同的通道;
- `WorkbenchConnectionRegistry` 按 `session_id`(缺省 `group_id`)聚合连接，
  `FrontendDeliveryTarget::Session` 是 session 级无差别广播。

因此 WS 推送维持 **session 级统一广播**，只能推一条文本;按 tab 的个性化历史
交给"owner 副本 + `view_bot_id` 过滤"承担。HTTP 历史接口已支持
`view_bot_id` 查询参数(`GET /groups/{id}/messages`、`GET /sessions/{id}/...`
的 query 结构)，前端 tab 传 bot uuid 即可，HTTP 层无需改动。

## 设计

### 契约变更(`bcs_service_api::core::system_message`)

`SystemMessageProducerService::produce` 返回值由 `Vec<SystemGroupMessage>`
改为 tuple:

```rust
/// Produce messages for a system-message event.
///
/// Returns `(bot_messages, user_message)`:
/// - `bot_messages`: per-bot delivery messages (semantics unchanged).
///   The dispatcher persists one history record per recipient with
///   `owner_bot_id = recipient`;
/// - `user_message`: single user-facing text used ONLY for the frontend
///   WebSocket session broadcast (NOT persisted); `None` when the event
///   has no user-facing content.
///
/// Producer must return `None` (never `Some("")`) when the event has no
/// non-empty user-facing text, so the dispatcher's non-empty check and
/// producer responsibility stay aligned.
async fn produce(
    &self,
    event: &SystemMessageEvent,
    group: &Group,
    registry: &dyn BotRegistryCoreService,
    participants: &[Participant],
) -> (Vec<SystemGroupMessage>, Option<String>);
```

不引入新结构体;tuple 两个元素的语义由 trait 文档注释固定。

### 各 producer 的 `user_message`(仅 WS)规则

| 事件 | user_message |
|---|---|
| BotJoined | 群里已有 bot 收到的通知文本(`format_notification` 的结果，`xxx 已加入协作群`，含能力集后缀) |
| BotLeft | 同 bot 通知文本: `xxx 已退出协作群` |
| ParticipantModeChanged | 同 bot 通知文本(用户加入/退出、Bot 禁言/自动发言) |
| HumanJoined | 同 bot 通知文本: `xxx 已加入协作群` |
| GenericNotification | 即事件的 `message` 本身;为空串时返回 `None` |
| BotHiddenNotice | `xxx 已设置为「不可协作」` |
| SessionContext | 去个性化版本，详见下节 |
| event 类型不匹配 | `(vec![], None)` |

通用行为决策: `bot_messages` 为空**不**阻止 `user_message` 产出
(例如最后一个 bot 退群时 BotLeft 无收件人,仍返回
`(vec![], Some("xxx 已退出协作群"))`,WS 照常推送)。

### SessionContext 的去个性化 WS 文本

WS 无法区分 bot tab，推送文本不能携带 bot 视角的个性化内容。规则:
**仅去个性化，保留路由说明**。

- Chat 模式: 取 lead(driver)那条 `[GROUP CONTEXT]` 的结构重新渲染，去掉
  尾部 `你是: xxx`、`你的角色: xxx` 和角色指令(Driver 指引/静默观察指引);
  保留路由指令(@mention / bcs_route 说明)、参与者列表(含 `可@` 列)、
  背景、`[任务]`。渲染不依赖任何收件人,不使用 bot 消息列表的迭代顺序;
  "lead 视角"仅指选取 Send 变体的结构(含路由指令),lead 的确定沿用
  `is_lead_participant` 的回退规则(无 lead 角色参与者时取
  `group.driver_bot`,不要求该 bot 当前在群中)。
- ManagerWorker 模式: 按 manager 视角的 `[SERVICE GROUP CONTEXT]` 结构重新
  渲染，去掉块内 `你的角色: manager` 行、协同指令(mcporter/MCP/原生工具/legacy
  的派发说明)和尾部 `你是: xxx` / `你的角色: xxx`;保留 `模式: manager_worker`、
  参与者(名称/ID/角色/摘要)、背景、`[任务]`、`[任务状态]`。不依赖收件角色,
  即使群中没有 Manager 参与者也按事实渲染;`[任务]`/`[任务状态]` 总是按
  `task_input`/`task_ledger` 事实无条件渲染(不沿用 bot 消息中 manager-only
  的填充策略)。

实现要求: producer 通过共享渲染助手**显式构造** WS 文本，不对 bot 消息做
字符串剥离。共享渲染助手的复用边界(实现时按此抽取，避免复用太少导致重复
渲染、复用太多意外影响 bot 消息):

- Chat 模式: 从 `initial_group_context_message` 抽出 background 段
  (`base_context`)、task 段(`task_line`)、roster 段(`format_roster` /
  `format_roster_with_mentions`)、routing_instruction 段为共享函数;
  bot 消息路径仍走完整模板,WS 路径组装"去个性化"版本。
- ManagerWorker 模式: 同理从 `manager_worker_initial_message` 抽出背景、
  roster、`[任务]`、`[任务状态]`(经 `format_ledger_status_line`)组装段。

bot 消息的渲染与内容完全不变。

### Dispatcher 变更(`crates/services/bcs-system-message/src/dispatcher.rs`)

1. `produce` 调用处解构为 `(bot_messages, user_message)`;
   bot 投递循环继续遍历 `bot_messages`，逻辑不变。
2. 历史持久化 — **system 消息按收件人归属**:
   - `history_messages_to_persist` 替换为按 recipients 展开的规则:
     每条 `SystemGroupMessage` 对每个 recipient 写一条记录
     (`owner_bot_id = Some(recipient)`)，内容即该消息的原文;
     `recipients` 为空则**不写记录**(例如最后一个 bot 退群时 BotLeft 无收件人,
     入库 0 条,WS 仍推送 user_message，此为已接受的行为边界);
   - 不再存在"取 `messages[0]` 作全局记录"的分支,ManagerWorker 的
     `has_global` / `is_manager_context_message` / `participant_has_role`
     特化逻辑整体删除(MW 的 worker 私有上下文由"按收件人写副本"自然覆盖,
     无需特判);
   - `user_message` **不入库**;
   - 记录的 `sender_id = "system"`、`sender_type = System`、
     `message_type = "system"` 不变。
3. 前端 WS 推送: `user_message` 为 `Some` 且非空时，按单条文本构造 chat event
   frame 走 `FrontendDeliveryTarget::Session` 广播;`None` 或空串时不发布。
   删除对 bot 消息列表的去重 + `"\n"` 拼接逻辑。
4. `SystemMessageDispatchOutcome`、持久化与 WS 推送的 best-effort
   warn 错误处理不变。

### 历史查询变更(`crates/services/bcs-message/src/lib.rs`)

`MessageOwnerFilter`(`bcs-domain` `message.rs`)新增变体:

```rust
pub enum MessageOwnerFilter {
    Any,
    IsNull,
    Eq(String),
    /// owner_bot_id IS NULL OR owner_bot_id = <viewer>
    PublicOrOwner(String),
}
```

`mysql.rs` 实现为 `(owner_bot_id IS NULL OR owner_bot_id = ?)` 谓词;
`memory.rs` 为同语义 retain;其他 match 站点同步补齐。

`GroupUseCaseError` 不变。`compute_session_history_query`
(chat/manager-worker 共享的唯一口径)改为:

| 策略 | viewer | 过滤 |
|---|---|---|
| ManagerWorker | worker(view_bot_id 是 worker bot) | `Eq(worker)`(不变：worker 只看自己的消息) |
| ManagerWorker | manager(view_bot_id 是 manager bot) | `PublicOrOwner(manager)`(**变更**: 原 `IsNull`，否则 manager 看不到自己的副本) |
| ManagerWorker | 无 view_bot_id / `human_*` | `IsNull`(Public，不变) |
| 非 MW(Chat 等) | bot(非空、非 `human_*` 的 view_bot_id,不校验成员身份) | `PublicOrOwner(view_bot_id)`(**变更**: 原 `Any`) |
| 非 MW(Chat 等) | 无 view_bot_id / `human_*` | `IsNull`(**变更**: 原 `Any`;正常消息 `owner=None` 照常见,系统消息人类视角不再出现在历史) |

注: `GET /sessions/{sid}/messages` 路由对 StateMachine 策略在 HTTP 层分流到
`collaboration_runtime.get_state_machine_session_history`(不经过本函数),
compute 层的"非 MW"分支在 compute_session_history_query 内部仍覆盖
StateMachine 语义(供老 group 接口等其余调用方使用),两组行为保持一致。

- `compute_visible_from_seq`(新参与者可见起点)逻辑不变,与
  `PublicOrOwner` 叠加:新加入的 bot 看不到他人此前的 owner 副本(owner 过滤
  天然排除)，公共消息仍受可见起点限制。
- V1 `bcs-app-session` message-history facade 与本函数共享同一口径(注释已
  声明 single source of truth)，行为随本改动一并生效,spec 将其列为受影响面
  而非另设规则。
- 语义表述(与需求一致): bot/human 消息按 `sender_id` 区分、
  `owner_bot_id = None` 继续共享可见;system 消息 sender 恒为 `system`,
  靠 `owner_bot_id` 记录收件 bot,按 view id 正确回放。
- Chat 分支的 viewer 判定: 对任何非空、非 `human_*` 前缀的 `view_bot_id`
  按 `PublicOrOwner(view_bot_id)` 处理,**不校验是否群成员**
  (与既有 chat 分支不校验成员身份的行为一致);
  `view_bot_id` 的鉴权/授权边界沿用各接口现有逻辑,本 spec 不扩展。
- 派生行为变化: `BotJoinedMessageProducer::fetch_history` 以
  `view_bot_id = driver` 调 `get_history` 为注入消息取最近 10 条历史,
  owner 过滤生效后取到的是"driver 视角"历史(公共 + driver 副本),
  不再是无差别的全量视图——方向更符合注入语义,不再向新 bot 泄露
  他人私有副本,spec 记为可接受的派生变化。

### 老 group 历史接口(`GET /groups/{id}/messages`)的修复

排查确认: `MessageService::get_history`(老 group 接口,Chat 新存储路径,
`bcs-message/src/lib.rs`)当前**硬编码** `owner_filter = MessageOwnerFilter::Any`
且 `visible_from_seq = None`,query 参数中的 `view_bot_id` 完全被忽略
(只在"结果为空时 fallback 到 bot 自身 transcript"的分支里被读取)。
本次必须一并修复,否则 `view_bot_id` 在前端最常调用的历史接口上不生效:

- `get_history` 的 Chat new-path 改为按 `view_bot_id` 计算 owner 过滤:
  bot viewer → `PublicOrOwner(view_bot_id)`;无 / `human_*` → `IsNull`;
- 该路径没有 session 对象(`compute_session_history_query` 需要 `&Session`
  计算 Chat 分支的 `visible_from_seq`),`get_history` 场景沿用现状
  `visible_from_seq = None`(不计新参与者可见起点,保持与老接口既有行为一致);
  实现上可抽出 Chat 分支的公共判定(或令 compute 接受 `Option<&Session>`),
  但不得以"无 session"为由退回 `Any`;
- 老 group 的 legacy 回退路径(group 未迁移到新存储时不经过
  `MessageRepoPort`)无法做 owner 过滤,维持现状,列为行为边界;
- 空结果时"fallback 到 bot 自身 transcript"的分支保持原触发条件
  (`messages.is_empty() && view_bot_id.is_some()`)。

### 历史数据兼容

存量系统消息的 `owner_bot_id` 全部为空(旧逻辑只写 owner=None 的
"全局"记录;MW 的 worker 私有副本为 owner=worker)。新过滤对存量的影响:

| 存量记录 | bot viewer (`PublicOrOwner(v)`) | 无 view / human (`IsNull`) | MW worker (`Eq(worker)`,不变) |
|---|---|---|---|
| 公共消息(普通 chat/bot 消息,owner=None) | 可见(IS NULL 支) | 可见 | worker 本来不可见,不变 |
| 旧全局 system 记录(owner=None) | 可见(IS NULL 支) | 可见 | worker 本来不可见,不变 |
| MW worker 私有副本(owner=worker) | 仅该 worker 可见 | 不可见(不变) | 该 worker 可见(不变) |

要点:

- **无数据丢失**: 新谓词是旧谓词的超集(bot viewer 旧 Chat 为 `Any`,
  新为 `PublicOrOwner(v)`,收窄的仅是"他人新增的私有副本";
  MW manager 旧为 `IsNull`,新为 `PublicOrOwner(manager)`,是严格超集);
- **无需数据迁移/治理**: 兼容由 `IS NULL OR owner = ?` 谓词本身承担;
- **已知残留**: 旧全局 system 记录中含此前 `messages[0]` 的脏内容
  (新 bot 的上下文注入、随机 bot 的"你是: xxx"),会继续对**所有**
  bot 视角可见——它们在库中无法与新产生的公共记录区分,spec 接受该残留
  不做治理;
- 由此不需要"按时间切分新旧口径"的兼容逻辑,新口径直接全量生效。

### 数据流

```text
SystemMessageEvent
      │
      ▼
producer.produce() ──► (bot_messages, user_message)
      │                        │
      │                        └─► FrontendDeliveryTarget::Session (仅 WS 广播,不入库)
      │
      ├─► 逐 recipient 持久化 (owner_bot_id=recipient, sender="system")
      │
      └─► 逐 recipient 解析 target → chat.send/chat.inject 投递 bot

历史查询(view_bot_id = botX):
      PublicOrOwner(botX) → 公共消息(owner=None) + 发给 botX 的系统消息副本
```

### 改动面

- 契约: `bcs-service-api::core::system_message::SystemMessageProducerService`
  (trait 签名 + 文档注释);
- 7 个 producer: `bot_joined`、`bot_left`、`human_joined`、
  `participant_mode_changed`、`generic`、`bot_hidden_notice`、
  `session_context`(`crates/services/bcs-system-message/src/producers/`);
  其中 `session_context` 新增共享渲染助手并在 `producers/mod.rs` re-export;
- dispatcher: `crates/services/bcs-system-message/src/dispatcher.rs`
  (按收件人持久化 + WS 单条推送);
- 查询: `crates/services/bcs-message/src/lib.rs`
  (`compute_session_history_query` viewer 分支、`get_history` 老 group 接口
  Chat new-path 的 owner 过滤接入);
- 领域与存储: `bcs-domain` `MessageOwnerFilter` 新变体;
  `bcs-message-store` 的 `mysql.rs` / `memory.rs` 谓词实现;
- 测试桩: `bcs-test-support` 的 `NoopSystemMessageProducer`(更新签名为
  返回 `(vec![], None)`);
  `dispatcher_test.rs` 的 `WorkerOnlySessionContextProducer`、
  `FixedProducer`、`FixedSendProducer`、`FixedWebSocketSendProducer`;
- 调用点: `produce` 仅 dispatcher 一处真实调用，其余为单测。

无 schema 变更(`owner_bot_id` 列与索引已存在)。存量数据的兼容性与已知
残留见"历史数据兼容"小节,此处不再展开。

## 错误处理

- 持久化失败、`append_message` 失败、WS 发布失败均维持现状: 记
  `tracing::warn!`，不影响 bot 投递结果与 `SystemMessageDispatchOutcome`;
- `produce` 不返回 `Result`,event 类型不匹配返回 `(vec![], None)`;
- 持久化循环逐记录 best-effort(单条失败不影响其余副本)。

## 测试

- 7 个 producer 单测: 断言 tuple 两个元素。SessionContext 单测新增断言:
  WS 文本不含"你是"、"你的角色"、角色指令/协同指令，且保留路由说明与
  `[GROUP CONTEXT]`/`[SERVICE GROUP CONTEXT]` 事实内容;ManagerWorker 用例
  断言 WS 文本含 `[任务]`/`[任务状态]` 且不依赖是否有 Manager 参与者。
- 空/边界断言:
  - `GenericNotification` 事件 `message` 为空串时 `user_message = None`;
  - Chat 模式无 lead(无 driver 角色 bot)时 WS 文本仍按回退规则渲染;
  - `BotLeft` 无其余收件人时 `produce` 返回空 `bot_messages`、
    `user_message = Some(...)`,dispatcher 入库 0 条但 WS 正常推送。
- dispatcher 测试:
  - 持久化用例改为断言"每收件人一条记录、owner=收件人 uuid、内容=原文"
    (BotJoined: 注入记录 owner=新 bot、通知记录 owner=每个已有 bot;
    SessionContext: 每个 bot 一条 owner=自己的上下文记录;
    MW worker 私有副本用例的断言要点不变——worker 副本 owner=worker
    且不产生公共副本,但 manager 的断言从"全局副本"改为
    "owner=manager 副本",相关用例同步改写);
  - 无收件人 → 入库 0 条;
  - 前端发布断言改为"仅发布 user_message",BotJoined 用例断言 WS payload
    不含注入消息。
- bcs-message 查询测试:
  - Chat: bot viewer → 公共消息 + 自己的 system 副本,看不到其他 bot 的副本;
    无 view_bot_id/human → 仅公共消息;
  - `get_history`(老 group 接口): 断言 `view_bot_id` 生效
    (bot viewer 只见公共+自己副本,由此回归"此前硬编码 Any 忽略 view_bot_id"
    的问题);
  - MW: worker → `Eq(worker)` 不变;manager → 公共 + manager 副本;
  - `PublicOrOwner` 谓词测试: 在 mysql.rs / memory.rs 各自既有的
    `query_messages` owner 过滤测试中,按同等粒度为 `PublicOrOwner`
    补等价用例(NULL 与命中 uuid 的记录都返回,他人 owner 的记录不返回)。
- 现有基于 `messages[0]` 全局记录语义的持久化断言同步改写。

## 非目标

- 前端 WS 的 tab/视角级路由(需协议扩展，见"架构约束");
- bot 投递内容、`DeliveryType`、provider transport、run-context 记录逻辑;
- HTTP 历史接口的参数与鉴权(`view_bot_id` 查询参数已存在);
- group callback 等非 dispatcher 路径产生的 `"system"` 记录
  (`group_flow.rs` 的 `try_persist_group_message` owner=None 全局写入,不属于
  本次 dispatcher 体系,保持不变);
- 存量 `owner=None` 系统消息脏数据的治理。
