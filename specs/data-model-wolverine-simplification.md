# NoCTF 数据模型与 Wolverine 调度简化权威规范

> 状态：**目标架构权威规范（Normative）**
>
> 规范版本：`1.0`
>
> 生效日期：`2026-08-23`
>
> 适用版本：NoCTF Alpha 下一次破坏性基线
>
> Wolverine 基准版本：`6.29.2`
>
> 实施状态：待按本文阶段门禁执行

## 1. 文档身份与优先级

本文不是建议稿、调研报告或兼容性说明，而是 NoCTF 下一版数据模型、消息拓扑、调度、Runtime、排行榜、通知线程和数据导出的**权威目标规范**。

在迁移完成前，仓库代码仍代表旧实现；旧实现只能用于识别迁移范围，不能反向改变本文目标。本文与下列文档发生冲突时，以本文为准，直至相关文档同步完成：

- `AGENTS.md` 中关于 Revision、`data_exports`、`LeaderboardDirty` 和旧 Runtime 字段的现状描述；
- `specs/database.md`；
- `specs/processes-messaging.md`；
- `specs/scoring-projection.md`；
- `specs/runtime.md`；
- `specs/realtime-notifications.md`；
- `specs/api.md` 与 `specs/api-conventions.md`；
- `specs/development.md`、`specs/testing.md`、`specs/backup-recovery.md`；
- 仓库历史提交、旧 OpenAPI、旧生成 SDK 和外部说明。

实施阶段 0 必须先同步 `AGENTS.md` 和上述规范索引，使项目级约束不再与本文冲突；在此之前不得开始业务代码迁移。

本文使用以下规范词：

- **必须（MUST）**：不满足即不允许合并或发布；
- **禁止（MUST NOT）**：不得以兼容、临时或性能优化为由绕过；
- **应该（SHOULD）**：除非有记录在案的技术阻塞，否则必须执行；
- **可以（MAY）**：实现可选择，但不得破坏本文不变量。

## 2. 开发前强制阅读与证据

任何修改 Wolverine 配置、消息路由、持久化、Agent、Inbox/Outbox 或 Handler 组合行为的开发者，在开始编码前必须完整阅读：

1. [Wolverine 官方文档](https://wolverinefx.net/)；
2. [Wolverine 官方 `llms.txt`](https://wolverinefx.net/llms.txt)；
3. `llms.txt` 中与本次修改直接相关的页面，至少包括：
   - Handler 发现与多个 Handler 的组合方式；
   - Sticky Handler 与 endpoint 绑定；
   - PostgreSQL transport；
   - PostgreSQL durability；
   - EF Core transactional inbox/outbox；
   - Leader Election、Singular Agent 与自定义 Agent；
   - durable inbox、重试与 dead letter。

### 2.1 版本约束

在线文档可能先于仓库固定版本。开发者必须以 `backend/Directory.Packages.props` 固定的 Wolverine `6.29.2` 为实现基准，并完成以下证据闭环：

- 在 PR 描述中列出实际阅读的官方页面；
- 对照 `6.29.2` 包 API 或对应版本源码确认配置 API 可用；
- 通过编译和真实 PostgreSQL/Wolverine 集成测试证明行为；
- 禁止仅依据博客、搜索摘要、模型记忆或其他版本示例实现；
- 若官方最新版语义与 `6.29.2` 不一致，先形成升级决策，不得偷偷混用新版本 API。

### 2.2 必须验证的官方行为

实施前的 Wolverine 技术 Spike 必须证明：

- Sticky Handler 在缺少同名 endpoint 时会退化为自动本地队列，因此 NoCTF 必须增加启动期硬校验；
- PostgreSQL persistence 与节点间控制通道启用后，集群才能执行 Leader Election 和 Agent 分配；
- EF Core transactional inbox/outbox 能把业务事务与消息持久化绑定；
- 同一逻辑事件发往多个目的地时，消息身份可按 `IdAndDestination` 去重；
- 未设置全局 `MultipleHandlerBehavior.Separated` 时，多 Handler 的默认组合语义与本文 fan-out 设计不冲突。

若任一行为在 `6.29.2` 无法得到官方文档、源码或集成测试证明，必须暂停相关阶段并提交 ADR；不得用进程内队列、手工数据库标志或静默本地 endpoint 替代。

## 3. 目标、非目标与接受的代价

### 3.1 目标

- 删除持久化乐观并发字段和协议层 `ExpectedRevision`，统一采用后写覆盖；
- 删除业务表中的调度栅栏、配置快照、Checker 进度和排行榜 Dirty/Snapshot 状态；
- 用 Wolverine PostgreSQL durable queues 表达 competing consumers 与显式 fan-out；
- 用集群 Singular Agent 承载可重建、无需补跑的内存调度；
- 以不可变事实和事件作为计分、服务状态和审计来源；
- 简化 RuntimeInstance，使其只保存资源关系、外部回执和生命周期结果；
- 删除异步数据导出实体，改为权限受控的同步流式 HTTP 下载；
- 重新生成一个 EF 工具拥有的 `InitialBaseline`。

### 3.2 非目标

- 不保留旧数据库、旧迁移、旧 API、旧 SDK 或旧 Revision 协议兼容；
- 不新增 `team_members`、调度表、排行榜快照表、Runtime operation 表、Question 表或 DataExport 表；
- 不提供逐字段合并、集合 CRDT 或客户端自动冲突解决；
- 不补跑 Worker 停机期间错过的 AWD、KoH、Checker 或 lifecycle tick；
- 不把 Redis 变为业务事实源；
- 不在本次迁移中升级 Wolverine，除非另有经批准的 ADR。

### 3.3 明确接受的代价

- 两个 HTTP 写请求并发修改同一实体时，最后一次 `SaveChanges` 获胜，集合元素可能丢失；
- 不同消息 Envelope 乱序完成时，Runtime 最后完成的 Handler 结果获胜，状态可能短暂倒退；
- 已创建 Runtime 的后续 Checker、Fix 和 Flag 注入读取最新题目定义，可能与资源创建时不同；
- Worker 停机期间的周期任务不追赶、不补算执行次数；
- Team 的 UUID 数组成员没有逐元素数据库外键，完整性由应用规则保证；
- 赛道邀请码以明文存在于数据库和备份，但只在最小权限管理协议中返回；
- 同步大导出受严格大小与超时限制，超过限制直接失败，不回退为后台任务。

## 4. 全局不变量

### 4.1 保留的版本概念

以下三类版本不是并发控制，必须保留：

- `User.TokenVersion`：密码修改或管理操作后的 JWT 全局失效；
- JSON `schemaVersion`：题目定义、比赛配置、规则和事件 Payload 的结构版本；
- 排行榜协议版本：客户端识别榜单传输/缓存结构，不能作为数据库并发令牌。

除此以外，实体 `Revision`、`*Revision`、`ConcurrencyVersion`、`CriticalSectionVersion`、`ProcessingVersion` 和请求 `ExpectedRevision` 均禁止存在。

### 4.2 数据事实与缓存

- PostgreSQL 是业务事实源；
- `competition_events`、`notifications`、`gameplay_facts` 与 `files` 的不可变记录是重建依据；
- Redis 仅保存缓存、Runner 心跳/容量索引、限流、SignalR backplane 和短期合并状态；
- Redis 全量丢失后，业务事实不得丢失，榜单必须能够从 PostgreSQL 重建；
- Wolverine 自有 PostgreSQL 表不计入 NoCTF 业务表数量，但必须纳入备份、恢复和监控。

### 4.3 最终业务表清单

新基线只允许以下 **15 张业务表**：

1. `users`
2. `competitions`
3. `competition_events`
4. `teams`
5. `challenges`
6. `challenge_attachments`
7. `competition_challenges`
8. `challenge_flags`
9. `runtime_instances`
10. `patch_uploads`
11. `account_tokens`
12. `platform_settings`
13. `notifications`
14. `gameplay_facts`
15. `files`

`data_exports` 必须删除。Wolverine persistence/transport 表、PostgreSQL 系统表和 EF migrations history 不是业务表。

## 5. 数据模型规范

### 5.1 通用并发模型

必须删除：

- 所有实体上的 `Revision`、`*Revision`；
- `ConcurrencyVersion`；
- `CriticalSectionVersion`；
- `ProcessingVersion`；
- API、Application command、Store 方法中的 `ExpectedRevision`；
- Revision mismatch、Revision conflict、重试读取新 Revision 的分支；
- OpenAPI、TS SDK、表单隐藏字段、客户端 revision 缓存和“内容已被他人修改”专用提示。

标量和集合写入统一使用 EF 最后一次 `SaveChanges` 的结果。数据库仍必须保留：

- 主键、外键；
- 业务唯一索引；
- Check Constraint；
- 软删除规则；
- 不可变记录的禁止更新规则；
- 幂等业务键与 Wolverine Inbox/Outbox。

禁止把删除的 Revision 换名为 `Epoch`、`VersionStamp`、`Sequence` 或隐藏 JSON 字段重新引入。

### 5.2 `users`

删除：

- `normalized_email`；
- `is_email_public`；
- `concurrency_version`。

保留：

- 单一 `email`；
- `normalized_user_name`；
- `token_version`。

写入规则：

```text
canonicalEmail = input.Trim().ToLowerInvariant()
```

- `email` 存储 canonical 值并建立唯一索引；
- 注册、管理员修改邮箱、邮箱验证和密码找回必须走同一规范化函数；
- 公共用户协议、比赛成员协议、通知作者协议和非管理导出不得包含邮箱；
- 只有 Platform Administrator 的管理端点和审计授权导出可以返回邮箱；
- 日志不得记录完整邮箱，结构化日志只允许脱敏值或 UserId。

### 5.3 `competitions`

删除列：

- `permission_revision`；
- `configuration_revision`；
- `configuration_updated_at`；
- `track_configuration_revision`；
- `track_configuration_updated_at`；
- `running_since`；
- `accumulated_running_seconds`；
- `leaderboard_dirty`；
- `leaderboard_visibility`；
- `leaderboard_visibility_starts_at`；
- `leaderboard_visibility_applied_at`；
- `leaderboard_visibility_revision`；
- `frozen_leaderboard_snapshot_json`。

新增列：

- `frozen_start_at timestamptz null`；
- `hidden_start_at timestamptz null`。

#### 5.3.1 排行榜可见性算法

设当前时间为 `now`，只有 `<= now` 的时间生效：

| Frozen | Hidden | 状态 | 投影行为 |
|---|---|---|---|
| 空或未来 | 空或未来 | Normal | 使用全部当前有效事实 |
| 已生效 | 空或未来 | Frozen | 只使用 `occurred_at <= frozen_start_at` 的事实/事件 |
| 空或未来 | 已生效 | Hidden | 不返回榜单内容 |
| 均已生效，Frozen 较晚 | 任意 | Frozen | cutoff 为 `frozen_start_at` |
| 均已生效，Hidden 较晚 | 任意 | Hidden | 不返回榜单内容 |
| 同一时刻生效 | 同一时刻生效 | Hidden | Hidden 优先 |

恢复 Normal 必须同时清空两个字段。每次设置、预约、切换或恢复必须追加不可变 `LeaderboardVisibilityChanged` 事件，Payload 至少包含：

- `schemaVersion`；
- 旧的派生状态；
- 新的派生状态；
- `frozenStartAt`；
- `hiddenStartAt`；
- 操作者；
- 操作时间。

事件是审计事实，两个时间列是当前查询入口；不得保存第三份可见性枚举或 Snapshot JSON。

#### 5.3.2 有效运行时间

`start_at`/`end_at` 只表示计划窗口。有效运行时间必须从 `CompetitionLifecycleChanged` 事件按区间推导：

1. 按 `occurred_at, id` 排序；
2. 仅 `Running` 区间累计；
3. `Paused`、未发布、已结束区间不累计；
4. 当前仍 Running 时，区间终点取 `min(now, end_at)`；
5. 重复或无效状态跳转按领域状态机拒绝，不以时间列修补；
6. AWD 轮次和 KoH 时间片使用同一派生时钟。

禁止重新增加 `running_since` 或 `accumulated_running_seconds` 缓存列。

#### 5.3.3 赛道邀请码

赛道配置中的 `InvitationCodeHash` 改为明文 `InvitationCode`。该值仍存于比赛配置 JSON，不新增表。

- Owner、Manager、Platform Administrator 的管理协议可以回显；
- Judge、Observer、参赛者、公开 API、普通日志和非管理导出必须省略；
- 比较必须使用固定时间比较，避免产生可观察时序差；
- 备份文档必须把它列为敏感数据；
- 不再保留 PBKDF2 哈希、salt 或验证兼容分支。

### 5.4 `teams`

删除：

- `normalized_name`；
- `concurrency_version`；
- `critical_section_version`。

保留：

- `member_ids uuid[]`；
- `member_ids` GIN 索引；
- `captain_id`；
- 唯一、随机的邀请令牌与现有约束。

规则：

- 不新增 `team_members`；
- 同一比赛允许完全相同或仅大小写不同的队名；
- 任何关系、命令、缓存键和 URL 都使用 TeamId，不以队名寻址；
- 当一个界面同时出现重名队伍时，UI 显示 `队名 · <Guid 前 8 位>`；
- `captain_id` 必须在 `member_ids` 中；
- `member_ids` 非空、无重复；
- 应用层在写入前验证所有 UserId 存在并满足比赛成员规则。

### 5.5 `challenges` 与 `competition_challenges`

`challenges` 删除 `revision`。题库模板更新立即覆盖当前定义。

`competition_challenges` 删除：

- `critical_section_version`；
- `revision`；
- `last_scheduled_awd_round`；
- `awd_schedule_competition_revision`；
- `awd_schedule_challenge_revision`；
- `awd_schedule_due_at`。

运行语义：

- Runtime 创建、重置、后续 Checker、Fix 和 Flag 注入都读取最新题目定义；
- RuntimeInstance 不保存题目定义 Snapshot、定义 Revision 或 source revision；
- 已存在外部资源不会因模板修改自动重建；
- 管理界面必须明确提示“修改影响未来动作，不重建现有资源”；
- 排行榜仍基于事实发生时可适用的当前规则全量投影；如需历史冻结，使用事实/事件时间，不恢复配置 Revision。

### 5.6 `gameplay_facts`

保持 `Processing`，不新增 `Judging`。GameplayFact 只保存当前业务结果和稳定失败码，不保存执行阶段、自由文本诊断、分值或累计分。

#### 5.6.1 AWD Checker

每次 Checker 执行都必须先创建独立 GameplayFact，而不是只在状态变化时创建：

- 健康：`ServiceUp`；
- 不健康：`ServiceDown`；
- `occurred_at` 决定所属轮次；
- 当前服务状态取 `occurred_at, id` 最大的 Checker Fact；
- 计分只从 GameplayFacts 推导；
- 重投以 FactId 和 Wolverine Inbox 保持幂等，不创建第二条事实。

#### 5.6.2 AWDP Fix 结果

| Checker 业务结果 | GameplayFact State | GameplayFact Result |
|---|---|---|
| `DefenseSucceeded` | Completed | Correct |
| `ExploitSucceeded` | Completed | Wrong |
| `ServiceAbnormal` | Completed | Rejected |
| `PatchFailed` | Completed | Rejected |
| `PatchTimeout` | Completed | Rejected |
| `PlatformFailed` | Completed | Rejected |

详细诊断只写入强类型、版本化 `AwdpFixResolved` CompetitionEvent Payload。最低 Payload：

```json
{
  "schemaVersion": 1,
  "gameplayFactId": "uuid",
  "patchUploadId": "uuid",
  "runtimeInstanceId": "uuid",
  "teamId": "uuid",
  "competitionChallengeId": "uuid",
  "outcome": "DefenseSucceeded|ExploitSucceeded|ServiceAbnormal|PatchFailed|PatchTimeout|PlatformFailed",
  "failureCode": "bounded-enum-or-null",
  "resolvedAt": "timestamptz"
}
```

事件中禁止写明文 Flag、补丁正文、Token、密码或未经脱敏的 Runner stderr。

### 5.7 `runtime_instances`

删除：

- `awdp_fix_stage`；
- 三个 `source_*_revision`；
- `generation`；
- `processing_version`；
- `runner_pool`；
- `runner_assignment_release_token`；
- `runner_unavailable_at`；
- `replaces_runtime_instance_id`；
- `participant_url_indexes`；
- `control_check_url`；
- `awd_checker_target_host`；
- `checker_status`；
- `checker_status_updated_at`；
- `checker_sequence`；
- `last_applied_checker_sequence`；
- `next_checker_due_at`；
- `checker_deadline_at`；
- PublishedPort 的 `allocated_at`。

保留：

- Runtime、Competition、CompetitionChallenge、Team、Purpose 关系；
- `gameplay_fact_id`，用于 AWDP Target/Fix 关联；
- `runtime_kind`、`runtime_provider`；
- `runner_id`；
- `state`、`failure_code`；
- `provider_receipt_json`；
- `urls`；
- `published_ports_json`；
- 申请、开始、停止、失败等生命周期时间字段。

#### 5.7.1 创建、重置与状态

- 每次创建或 Reset 都插入全新 RuntimeInstance 和 UUID；
- 不复用旧 Runtime ID，不保存 generation，不记录 replaces 关系；
- 旧 Runtime 保留为历史记录；
- 不用版本字段阻止晚到状态；不同 Envelope 最后完成的 Handler 获胜；
- 每个 Handler 仍必须对同一 MessageId 幂等；
- 外部资源清理只能依据 `provider_receipt_json` 和 runner/provider 事实，禁止仅改数据库状态假装清理成功。

#### 5.7.2 URL、Checker 与 Runner

- Participant URL 由读取 API 根据最新题目定义过滤，不保存 URL index；
- Checker 目标由 Runner 在执行时从 Provider Receipt 解析；
- Worker 从 Redis Runner Registry 按 provider 与容量选择具体 RunnerId；
- Worker 直接投递该 Runner 的 node queue；
- Runner 不再监听 pool queue；
- `runner_id` 是已选择节点事实，不表示资源一定创建成功；
- Provider Receipt 是停止与清理外部资源的唯一资源回执。

### 5.8 `notifications` 与咨询线程

新增可空 `thread_root_id`，删除 `reply_to_id` 唯一约束和严格线性链遍历。

线程规则：

- QuestionOpened 根通知：`thread_root_id = null`；
- 所有回复和状态事件：`thread_root_id = 根通知 Id`；
- `reply_to_id` 可以保留为可空的“回复上下文”，但不参与线程成员判定且不唯一；
- 线程按 `sent_at, id` 稳定排序；
- 当前状态取最后一条有状态语义的通知；
- 并发回复全部 append，不返回 Revision conflict；
- 私有咨询正文只对原队伍和有权限工作人员可见；
- 删除根通知必须被禁止，通知保持 append-only。

### 5.9 删除 `data_exports`

必须删除：

- `data_exports` 表、DbSet、领域实体和枚举；
- Store、Processor、Application use case；
- `GenerateDataExport`、`ExpireDataExport`、`PurgeDataExport` 消息和 Handler；
- 导出列表、状态、DataExportId 下载、过期与清理端点；
- `DataExportReady`、`DataExportFailed` NotificationKind；
- `EntityReferenceKind.DataExport` 及硬删除引用；
- 前端导出任务列表、轮询和生成 SDK 对应类型。

保留两个同步端点：

- `POST /admin/competitions/{competitionId}/data-export`；
- `POST /admin/platform/audit-logs/data-export`。

端点必须：

- 使用强类型 FastEndpoints `Endpoint<TRequest, Results<...>>` 与 `ExecuteAsync`；
- 以具体 `TypedResults` 流式返回 ZIP；
- 在请求开始前完成权限、范围和受保护 Flag 权限检查；
- 支持 `CancellationToken`，客户端断开后立即停止生成；
- 设置压缩前记录数、压缩后字节数、执行时间和内存预算上限；
- 超限返回稳定 Problem，不创建 File、Notification 或后台任务；
- 记录审计事件，但不记录导出内容；
- 不在磁盘或对象存储保留临时导出；确需临时文件时使用受控临时目录并在 `finally` 删除。

## 6. Wolverine 消息拓扑

### 6.1 消息分类

| 类型 | 语义 | 路由 | Handler 数 | 幂等边界 |
|---|---|---|---:|---|
| 命令 | 一个业务动作只应执行一次 | 单个 named PostgreSQL queue | 1 | MessageId + durable inbox |
| 单消费者事件 | 当前只有一个独立副作用 | 单个 named PostgreSQL queue | 1 | MessageId + durable inbox |
| Fan-out 事件 | 多个副作用必须独立成功/失败 | 每个订阅者独立 PG queue | N | MessageId + Destination |
| Runner 节点命令 | 已选定 Runner 执行资源动作 | `runner-node-{runnerId}` | 1 类 Handler | MessageId + node destination |
| 调度触发 | Singular Agent 产生的 durable 命令 | 对应 control/gameplay queue | 1 | 预生成 FactId/业务键 |

### 6.2 Competing consumers

单 Handler 消息必须显式路由到一个 named PostgreSQL queue：

- 多个 Worker/Runner 实例监听同名 queue；
- 使用 durable inbox；
- 并发度由 endpoint 配置控制；
- 不使用 `[StickyHandler]`；
- 业务事务内发布使用 EF transactional outbox；
- Handler 不得在成功提交前直接向 SignalR、Redis 或外部系统发布不可撤销副作用。

### 6.3 Fan-out

需要多个独立订阅者的事件必须满足：

1. 每个 Handler 使用唯一 `[StickyHandler("queue-name")]`；
2. 每个 Sticky 名称注册同名 durable PostgreSQL listening endpoint；
3. 每个 endpoint 启用 durable inbox；
4. 发布端为每个目的地生成独立 Envelope；
5. 消息身份使用 `IdAndDestination`；
6. 保持默认 `ClassicCombineIntoOneLogicalHandler`；
7. 禁止全局设置 `MultipleHandlerBehavior.Separated`；
8. 一个订阅者失败只重试自身，不回滚或阻塞已完成订阅者；
9. Sticky endpoint 缺失必须启动失败，禁止自动本地 queue。

第一批 fan-out 事件为 `CompetitionEventCommitted`：

- `noctf-competition-events-realtime`：SignalR/Redis 发布；
- `noctf-competition-events-leaderboard`：排行榜缓存失效与投影合并。

队列名是稳定运维协议。改名必须包含部署期双队列排空方案或确认旧队列为空，禁止静默改名遗留消息。

### 6.4 启动期拓扑校验

启动校验必须在开始接收业务流量前执行：

- 所有 Sticky 名称都存在同名 PostgreSQL endpoint；
- 同一 fan-out 消息的 Sticky 名称唯一；
- endpoint transport 必须是 PostgreSQL，不接受 local queue；
- fan-out endpoint 已启用 durable inbox；
- single-handler 消息没有多个可执行 Handler；
- 未全局启用 `Separated`；
- Runner 节点 queue 与 Registry 中节点命名规则一致。

任一失败必须终止启动，并输出不含敏感信息的结构化错误；不得只警告后继续。

### 6.5 Inbox、Outbox、重试与死信

- 业务写事务和消息持久化必须使用 EF transactional outbox；
- 消费端必须使用 durable inbox；
- 重试策略按 queue 配置，不能全局一刀切；
- control/gameplay 的瞬时故障可重试，确定性业务失败不得无限重试；
- dead letter 必须带 queue、message type、failure kind 和 trace id 监控；
- 日志可记录 CompetitionId/RuntimeId/FactId，但这些 ID 禁止作为 Prometheus 高基数标签。

## 7. Singular Agent 与内存调度

### 7.1 Agent 职责

使用 Wolverine 集群 Singular Agent，集群同一时刻只允许一个活动调度器。Agent 只调度：

- AWD Round；
- AWD Checker；
- KoH Poll；
- Competition lifecycle tick；
- 排行榜 500ms debounce 集合。

Agent 不直接执行 Runtime、Checker、计分或投影重活，只生成带稳定业务键/FactId 的 durable 消息。

集群运行前置条件：

- 所有承担 Worker 角色的实例必须连接同一套 Wolverine PostgreSQL persistence/transport；
- 部署配置必须启用并允许 Wolverine leader election/agent assignment，禁止在多实例环境退化为各节点独立调度；
- 就绪检查必须暴露当前 leader、Singular Agent 所属节点和最近一次接管时间，但不得暴露数据库凭据或内部消息正文；
- Agent 无法完成选主、续租或状态恢复时，相关 Worker 不得报告调度能力就绪；API 可继续提供不依赖调度的只读能力；
- 故障转移后必须由新 leader 从 PostgreSQL 业务事实重建内存调度，不复制旧节点内存状态。

### 7.2 重建与不补跑语义

启动或故障转移时：

1. 从当前时间读取活动比赛、配置和生命周期事件；
2. 从已有 GameplayFacts 判断当前轮次/最近执行；
3. 计算第一个 `>= now` 的后续 tick；
4. 重建内存优先队列；
5. 不为停机窗口生成补偿 tick；
6. 不写业务表或 Wolverine scheduled-message 表保存 next-run。

### 7.3 Checker 调度幂等

- 每次 tick 先生成 FactId；
- 创建 Processing GameplayFact 与 durable 执行消息必须在同一事务；
- MessageId/业务键必须能识别同一 FactId；
- Runner 完成后更新对应 Fact；
- Agent 重投不得创建第二个 Fact；
- 超时由 durable control message 或当前事实扫描收敛，不恢复 `checker_deadline_at` 等 Runtime 字段。

## 8. 排行榜投影

### 8.1 事件驱动失效

删除 `Competition.LeaderboardDirty` 和 15 秒 Dirty 扫描。排行榜 Sticky Handler 收到影响计分/资格/可见性的事件后：

1. 立即删除对应 FusionCache/Redis 榜单键；
2. 将 CompetitionId 加入 Singular Agent 的进程内合并集合；
3. 固定 500ms debounce；
4. 同一窗口只执行一次全量 PostgreSQL 投影；
5. 投影成功后原子替换缓存并发布 SignalR；
6. 投影失败保持 cache miss，并按 durable 消息重试；
7. Agent 在失效后崩溃时，下一次 API 读取触发重建，不返回旧缓存。

### 8.2 Cache miss 重建

- API cache miss 使用进程内 keyed lock 合并同一实例的并发请求；
- 多 API 实例之间允许重复投影，但最终缓存值必须完整且可由事实重建；
- Hidden 直接返回隐藏协议，不执行或返回榜单；
- Frozen 使用 `frozen_start_at` cutoff；
- Normal 使用全部当前有效事实；
- 禁止恢复数据库 Snapshot 或 Dirty 标记作为兜底。

### 8.3 触发矩阵

至少下列变化必须失效榜单：

- GameplayFact 创建、完成、重判；
- ManualAdjustment；
- 队伍报名通过、封禁、解禁、删除资格；
- Challenge 发布、取消发布、规则或分值配置修改；
- Competition 配置、生命周期、赛道和可见性变化；
- 影响资格或计分的用户状态变化。

纯展示信息修改不得触发投影。

## 9. API、OpenAPI、SDK 与前端

### 9.1 API

- 所有管理更新请求和响应删除 `ExpectedRevision`/Revision；
- 删除 revision conflict 响应，保留 NotFound、Forbidden 和真实业务 Conflict；
- Leaderboard 管理改为设置/清空 `FrozenStartAt`、`HiddenStartAt`；
- Team 响应删除 normalized name 与版本；
- Runtime 响应删除 generation、stage、pool、source revisions、participant indexes 和 Checker 内部状态；
- Challenge、CompetitionChallenge、PlatformSettings 响应删除 revision；
- 公共用户协议删除 Email，管理员用户协议保留 canonical Email；
- Question 协议以 ThreadRootId 表示线程；
- 导出端点同步返回流，不返回 202、statusUrl 或 DataExport DTO。

所有端点继续遵守根 `AGENTS.md` 的强类型 FastEndpoints 约束。业务规则必须位于 Application/Infrastructure 能力中，Endpoint 只处理传输、认证、授权、限流与协议映射。

### 9.2 OpenAPI 与前端

接口变更后必须依次：

1. Release build 后端；
2. 运行 `dotnet run --project backend/src/NoCTF.API/NoCTF.API.csproj --configuration Release --no-build --no-launch-profile -- --export-openapi`；
3. 在 ClientApp 运行 `bun run api:gen`；
4. 确认 `app/api/` 生成幂等；
5. 删除所有手写 Revision、DataExport、旧 Runtime 和旧 Question DTO；
6. 前端只能调用生成 SDK，不手写 URL、DTO、枚举或失败码；
7. 删除“修订冲突”通用提示，409 只按强类型业务失败码翻译；
8. 同步中英文资源。

## 10. 安全、审计与可观测性

### 10.1 安全

- 邮箱、赛道邀请码、Flag、Token、密码、补丁正文不得进入普通日志；
- 同步导出必须重新执行权限判定，不能复用前端是否显示按钮；
- Participant URL 过滤在 API 读取时执行；
- Provider Receipt 只对有权限管理角色返回；
- Sticky/queue 配置错误必须 fail closed；
- 所有 SQL、Redis Lua 和队列名输入来自受控枚举/值对象。

### 10.2 审计

以下操作必须追加结构化审计/CompetitionEvent：

- 排行榜可见性变化；
- 同步数据导出；
- 赛道邀请码查看或轮换；
- Runtime 强制清理；
- 用户敏感信息管理访问；
- AWDP Fix 最终解析。

删除 Revision 不得降低审计可追溯性。审计记录事实，不参与并发控制。

### 10.3 指标

最低指标：

- 每个 PostgreSQL queue 的待处理数、最旧消息年龄、完成/重试/死信速率；
- Singular Agent 所属节点、换主次数、重建耗时；
- Scheduler tick 延迟与错过窗口数（错过只统计，不补跑）；
- Sticky fan-out 各 destination 成功/失败；
- 榜单缓存失效、500ms 合并、cache miss 重建、投影耗时；
- Runtime node routing、资源创建/清理结果；
- 同步导出时长、取消、超限和返回字节数。

Prometheus 标签只能使用 queue、message、outcome、mode、provider 等有限枚举；具体 ID 放入 Trace/日志，不作为标签。

## 11. 实施总原则

- 迁移是 Alpha 阶段破坏性基线，不做旧协议双写和数据库兼容层；
- 每阶段必须独立提交，提交范围清晰；
- 每阶段开始前工作树必须可解释，不覆盖其他修改；
- 任何阶段未通过退出门禁，不得进入下一阶段；
- 迁移期间主分支必须始终能编译；如某一步无法独立编译，应在同一提交内完成最小闭环；
- EF migrations 和 Snapshot 只由 `dotnet ef` 生成；
- 生成 SDK 不手改；
- 真实 PostgreSQL、Redis、Wolverine 行为使用 Testcontainers，不用 EF InMemory 证明；
- 不在生产数据上试运行 migration remove、database drop 或基线重建。

## 12. 分阶段迁移清单与验收门禁

### 阶段 0：治理、基线与 Wolverine Spike

任务：

- [ ] 将本文加入 `specs/README.md` 首位阅读顺序；
- [ ] 同步根 `AGENTS.md`，删除与本文冲突的 Revision、16 表、Dirty 和旧 Runtime 规则；
- [ ] 在 `specs/database.md`、`processes-messaging.md`、`scoring-projection.md`、`runtime.md` 标注迁移目标；
- [ ] 记录当前 main commit、数据库 schema、OpenAPI hash、SDK hash、测试数量；
- [ ] 完整阅读 Wolverine 官方文档与 `llms.txt`；
- [ ] 建立 `6.29.2` Spike，验证 Sticky、本地 fallback、PostgreSQL endpoint、`IdAndDestination`、EF outbox、Singular Agent；
- [ ] 写集成测试原型，但不改业务语义；
- [ ] 确认最终 15 表清单和破坏性切换窗口。

退出门禁：

- 文档冲突清单归零；
- Wolverine Spike 在真实 PostgreSQL 上通过；
- 主分支 Release build 与现有测试通过；
- 有经过评审的切换/回滚负责人。

回滚：仅文档和 Spike 测试提交，可直接 revert；不得产生数据变更。

### 阶段 1：删除 Revision 协议闭环

任务：

- [ ] 删除 Domain 所有持久化 Revision/Concurrency 字段；
- [ ] 删除 Application command/query 中 ExpectedRevision；
- [ ] 删除 Infrastructure 的 revision predicate、递增和 mismatch 分支；
- [ ] 删除 API request/response/Validator 中 revision；
- [ ] 删除 revision conflict protocol 与 mapper；
- [ ] 导出 OpenAPI并生成 TS SDK；
- [ ] 删除前端 revision state、隐藏字段和冲突提示；
- [ ] 改写单元/架构/API 测试为 last-write-wins；
- [ ] 保留 JWT token_version、JSON schemaVersion、榜单协议版本测试。

退出门禁：

- `rg -i "ExpectedRevision|CriticalSectionVersion|ConcurrencyVersion|ProcessingVersion"` 只允许出现在本文/历史 ADR 的说明中；
- API/OpenAPI/SDK 不包含 revision concurrency 字段；
- 两个并发写的 PostgreSQL 集成测试证明最后提交者获胜；
- 不可变实体仍无法被更新。

回滚：回滚本阶段完整提交；不得只恢复 API 字段而不恢复数据库语义。

### 阶段 2：核心实体和隐私模型

任务：

- [ ] `users` 删除旧列并统一 email canonicalization；
- [ ] 建立 email 唯一索引与隐私投影；
- [ ] `teams` 删除 normalized/concurrency 字段并允许重名；
- [ ] UI 增加重名短 Guid；
- [ ] `competitions` 删除 Revision、运行累计、可见性、Dirty/Snapshot 字段；
- [ ] 增加 frozen/hidden start；
- [ ] 用生命周期事件实现有效运行时间；
- [ ] 赛道邀请码改明文并收紧协议可见性；
- [ ] `challenges`/`competition_challenges` 删除 Revision 和 AWD 调度列。

退出门禁：

- 邮箱规范化、唯一约束、隐私测试通过；
- 重名 Team、GIN 查询、Captain 约束测试通过；
- 生命周期区间测试覆盖暂停/恢复/结束；
- 排行榜可见性真值表全部通过；
- 邀请码不出现在非授权 API、日志和导出。

回滚：代码可 revert；数据库迁移尚未生成最终基线，不允许在共享环境应用中间模型。

### 阶段 3：Notifications/Questions 线程化

任务：

- [ ] Notification 增加 ThreadRootId；
- [ ] 删除 ReplyToId 唯一约束；
- [ ] 创建根/回复/状态 append 规则；
- [ ] 查询改为 ThreadRootId + `sent_at,id`；
- [ ] 当前状态取最后状态事件；
- [ ] 删除 Question ExpectedRevision 与冲突；
- [ ] OpenAPI/SDK/前端线程 UI 同步；
- [ ] 权限查询保持题目所有者、裁判、管理员和队伍边界。

退出门禁：

- 多成员并发回复全部保留；
- 顺序稳定；
- 状态计算确定；
- 私有正文不泄露；
- 不新增 Question/Conversation/Participant/ReadState 表。

### 阶段 4：同步流式导出并删除 DataExport

任务：

- [ ] 实现两个同步流式端点；
- [ ] 把生成逻辑改成请求内可取消流；
- [ ] 设置记录数、压缩大小、耗时、内存上限；
- [ ] 保留权限、Flag 保护和审计；
- [ ] 删除 DataExport Domain/Application/Infrastructure/Worker/API；
- [ ] 删除消息路由、NotificationKind、EntityReferenceKind；
- [ ] 删除前端任务列表/轮询，改直接下载；
- [ ] OpenAPI/SDK 重新生成。

退出门禁：

- 成功下载 ZIP 可解压且内容正确；
- 取消、超限、Forbidden、NotFound 均有强类型结果；
- 失败不留下 File、Notification、临时对象或队列消息；
- 全仓 `DataExport` 只允许出现在本文和迁移说明中。

### 阶段 5：Runtime 最小模型与节点直投

任务：

- [ ] 删除本规范列出的 Runtime 字段；
- [ ] Reset 改为新 UUID 插入；
- [ ] URL 在读取时按最新定义过滤；
- [ ] Runner 从 receipt 解析 Checker 目标；
- [ ] Worker 从 Redis Registry 选择具体 RunnerId；
- [ ] 只向 node queue 投递；
- [ ] Runner 停止监听 pool queue；
- [ ] 删除 generation/source revision/sequence 防护；
- [ ] 保留 MessageId 幂等和 receipt 清理；
- [ ] 管理/API/SDK/前端删除内部字段。

退出门禁：

- 每次 Reset 产生新 UUID、旧行保留；
- node queue 路由在多 Runner 集成测试中正确；
- pool queue 不再注册；
- 晚到消息测试证明“最后完成者获胜”；
- stop/cleanup 只依据真实 receipt，资源无泄漏。

### 阶段 6：GameplayFact 与 AWDP 结果

任务：

- [ ] 每次 AWD Checker 创建 Fact；
- [ ] 当前服务状态改为最新 Fact；
- [ ] AWDP 六种结果完成映射；
- [ ] 增加版本化 `AwdpFixResolved` Payload；
- [ ] 删除 Runtime/Facts 的阶段和自由文本诊断依赖；
- [ ] 重投按 FactId 幂等。

退出门禁：

- AWD 每次健康/不健康检查均有一条且仅一条事实；
- AWDP 六种映射全部通过；
- 诊断事件不含敏感值；
- 计分只读取 GameplayFact。

### 阶段 7：Wolverine competing consumers 与 fan-out

任务：

- [ ] 分类所有 message/handler；
- [ ] 单 Handler 映射 named PG queues；
- [ ] `CompetitionEventCommitted` 拆为两个 Sticky Handler；
- [ ] 注册两个同名 durable PG endpoints；
- [ ] 配置 IdAndDestination；
- [ ] 增加启动期拓扑校验；
- [ ] 确认未设置全局 Separated；
- [ ] 为每个 queue 配置独立重试/并发/死信指标。

退出门禁：

- 多 listener 对单 Handler 只执行一次；
- 两个 Sticky destination 各收到一次；
- 一个失败不阻塞另一个；
- 缺少 Sticky endpoint 时进程启动失败；
- PostgreSQL 重启和 Worker 重投后无重复业务事实。

### 阶段 8：Singular Agent 调度

任务：

- [x] 实现/重构自定义 Agent/AgentFamily；
- [x] 迁移 AWD Round、AWD Checker、KoH Poll、lifecycle tick；
- [x] 删除旧 Scheduled Message next-run 与业务调度字段；
- [x] Agent 只派发 durable message；
- [x] 故障转移时从当前状态重建；
- [x] 增加不补跑行为与指标。

退出门禁：

- 多 Worker 只有一个活动 Agent；
- 主节点停止后另一个接管；
- 停机窗口没有补跑洪峰；
- 恢复后从当前轮次继续；
- Agent 不直接执行重活。

实施证据见 [阶段 8：Singular Agent 周期调度](data-model-wolverine-stage8-singular-agent.md)。

### 阶段 9：排行榜事件驱动投影

任务：

- [ ] 删除 Dirty 字段、扫描和 Maintenance dirty handler；
- [ ] Leaderboard Sticky Handler 先删缓存；
- [ ] 实现 500ms CompetitionId 合并集合；
- [ ] 实现全量 PostgreSQL 投影；
- [ ] API cache miss + keyed lock 重建；
- [ ] 实现 Frozen cutoff 与 Hidden 响应；
- [ ] 更新 SignalR 失效/发布；
- [ ] 更新监控指标，删除 Dirty 指标。

退出门禁：

- 相关事件立即使缓存 miss；
- 500ms 内多事件只投影一次；
- 失效后模拟 Agent 崩溃，下一读可重建；
- Frozen/Hidden/Normal 全部通过；
- 无 `LeaderboardDirty` 代码、列或指标。

### 阶段 10：单一 EF 基线

前置条件：阶段 1–9 全部通过，最终模型稳定，已有数据库已备份并完成恢复演练。

任务：

- [ ] 在一次性开发数据库上记录旧 schema；
- [ ] 使用 `dotnet ef migrations remove` 按逆序移除源码迁移；
- [ ] 禁止用文件删除代替 EF 工具；
- [ ] 生成新的 `InitialBaseline`；
- [ ] 检查 migration 与 snapshot 均为工具生成；
- [ ] 在空 PostgreSQL 应用基线；
- [ ] 检查 15 张业务表和所有约束/索引；
- [ ] 检查 Wolverine 表正常创建；
- [ ] 运行 model drift 检查。

在 `backend` 目录使用的命令模板：

```powershell
dotnet ef migrations list `
  --project src/NoCTF.Infrastructure/NoCTF.Infrastructure.csproj `
  --startup-project src/NoCTF.API/NoCTF.API.csproj `
  --context NoCtfDbContext

# 仅针对源码迁移链和可丢弃开发数据库，按逆序重复执行；禁止对生产库使用 --force。
dotnet ef migrations remove `
  --project src/NoCTF.Infrastructure/NoCTF.Infrastructure.csproj `
  --startup-project src/NoCTF.API/NoCTF.API.csproj `
  --context NoCtfDbContext

dotnet ef migrations add InitialBaseline `
  --project src/NoCTF.Infrastructure/NoCTF.Infrastructure.csproj `
  --startup-project src/NoCTF.API/NoCTF.API.csproj `
  --context NoCtfDbContext `
  --output-dir Migrations
```

退出门禁：

- 迁移列表只有一个 `InitialBaseline`；
- 新空库可一次创建；
- 业务表正好 15 张；
- 删除列和 `data_exports` 不存在；
- `dotnet ef migrations has-pending-model-changes` 返回无漂移；
- migration/snapshot 没有人工修改痕迹。

回滚：不回滚为旧 schema。切换失败时恢复旧应用和切换前整库备份；重新排期修复新基线。

### 阶段 11：契约、文档、全量验证与切换

任务：

- [x] 更新所有受影响权威文档和 ADR；
- [x] 导出 OpenAPI，生成 SDK并验证幂等；
- [x] 完成中文/英文 UI；
- [x] 运行全量 backend/frontend/integration/E2E；
- [ ] 构建镜像并在全新测试环境部署；
- [x] 完成可丢弃开发环境的 backup/restore、Redis loss、Worker failover、Runner cleanup 演练；
- [x] 更新 HANDOFF、发布说明和运维 Runbook；
- [x] 版本号递增 Alpha；
- [ ] 通过 Go/No-Go 审批后才切换。

退出门禁：见第 14、15 节。

## 13. 代码迁移范围索引

实施者必须以 `rg` 再次生成实时清单。当前已知范围至少包括：

| 能力 | 主要目录/文件模式 | 必须完成的动作 |
|---|---|---|
| Domain 模型 | `backend/src/NoCTF.Domain/**` | 删除 Revision、DataExport、旧 Runtime/调度字段，增加 ThreadRootId/可见性时间 |
| Application 契约 | `backend/src/NoCTF.Application/**` | 删除 ExpectedRevision/DataExport 消息，增加强类型结果与事件 Payload |
| DbContext | `backend/src/NoCTF.Infrastructure/Persistence/NoCtfDbContext.cs` | 最终 15 DbSet，更新 provider-specific mapping |
| Stores | `backend/src/NoCTF.Infrastructure/**` | 删除版本谓词/Dirty mark，按新语义查询与写入 |
| Data Export | `Domain/Application/Infrastructure/API/Worker` 的 `DataExport*` | 全链删除并替换同步流式端点 |
| Worker | `backend/src/NoCTF.Worker/**` | 新队列、Agent、Fact 调度、删除 Dirty scan |
| Hosting | `backend/src/NoCTF.Hosting/MessageRouting.cs` | 重建消息分类与 destination |
| Runner | `backend/src/NoCTF.Runner/RunnerRole.cs` 及 handlers | 删除 pool listener，保留 node queue 与 receipt 清理 |
| Leaderboard | `Infrastructure/Scoring/Leaderboard/**` | 事件驱动失效、500ms 合并、cache miss 重建 |
| API | `backend/src/NoCTF.API/Endpoints/**` | 强类型协议、同步导出、删除 Revision/旧 DTO |
| OpenAPI/SDK | `wwwroot/openapi/v1.json`、`ClientApp/app/api/**` | 仅通过生成工具更新 |
| Frontend | `ClientApp/app/**` | 删除 revision/导出任务/旧 Runtime 字段，更新线程和错误映射 |
| Migrations | `Infrastructure/Migrations/**` | 仅 EF 工具移除并生成单一基线 |
| Tests | `backend/tests/**`、`ClientApp/tests/**` | 按第 14 节重写与补充 |
| Monitoring | `Application/Infrastructure/Administration/Monitoring/**` | 删除 Dirty 指标，增加新投影/Agent/queue 指标 |

## 14. 实施级测试矩阵

### 14.1 架构与静态检查

- [ ] 仅 15 张业务表；
- [ ] 无 `data_exports`；
- [ ] 无持久化 Revision/ExpectedRevision；
- [ ] 无 Runtime generation/sequence/source revision/pool 字段；
- [ ] 无排行榜 Dirty/Snapshot；
- [ ] 无全局 `MultipleHandlerBehavior.Separated`；
- [ ] Sticky 名称和 PostgreSQL endpoint 一一对应；
- [ ] API endpoint 全部强类型 `ExecuteAsync`/`TypedResults`；
- [ ] 无手写 URL、DTO、枚举、SDK 和 migration；
- [ ] 无 LINQ query-expression syntax。

### 14.2 PostgreSQL/Testcontainers

- [ ] 新基线空库创建与 model drift；
- [ ] email canonicalization 与唯一索引；
- [ ] 公共/管理邮箱投影；
- [ ] Team 重名、GIN、Captain 约束；
- [ ] Notification ThreadRootId 并发 append；
- [ ] 生命周期有效时间；
- [ ] 可见性真值表；
- [ ] Runtime Reset 新 UUID；
- [ ] AWD 每次 Checker 一条 Fact；
- [ ] AWDP 六种结果映射；
- [ ] last-write-wins 并发写。

### 14.3 Wolverine/PostgreSQL

- [ ] 单 Handler + 多 listener 仅执行一次；
- [ ] 两 Sticky Handler 各自收到一次；
- [ ] `IdAndDestination` 不把第二 destination 当重复；
- [ ] 订阅 A 失败不阻塞 B；
- [ ] 缺 endpoint 启动失败；
- [ ] durable inbox/outbox 在进程中断后重投；
- [ ] 业务事务回滚时消息不发布；
- [ ] 多 Worker 只有一个 Singular Agent；
- [ ] Agent failover；
- [ ] 停机窗口不补跑；
- [ ] dead letter 与指标按 queue 区分。

### 14.4 Runtime/Runner

- [ ] Redis Registry provider/capacity 选择；
- [ ] node queue 精准投递；
- [ ] 不监听 pool queue；
- [ ] receipt 解析 Checker target；
- [ ] participant URL 读取时过滤；
- [ ] 晚到完成顺序覆盖；
- [ ] 重投不重复创建资源；
- [ ] stop/cleanup 释放容器、网络、端口和容量。

### 14.5 排行榜

- [ ] 事件后缓存立即失效；
- [ ] 500ms 合并；
- [ ] cache miss 同实例并发合并；
- [ ] Agent 崩溃后读取重建；
- [ ] Normal/Frozen/Hidden；
- [ ] 同刻 Hidden 优先；
- [ ] cutoff 后事实不进入 Frozen；
- [ ] 封禁/解禁触发全量重投；
- [ ] Redis 全丢后 PostgreSQL 重建。

### 14.6 同步导出

- [ ] 比赛导出成功；
- [ ] 审计导出成功；
- [ ] 客户端取消；
- [ ] 记录数/字节/时间超限；
- [ ] Forbidden/NotFound；
- [ ] Flag 权限；
- [ ] 无临时文件/对象/File/Notification/后台消息残留；
- [ ] 审计记录完整。

### 14.7 API、前端与 E2E

- [ ] OpenAPI 无旧字段/端点；
- [ ] SDK 生成两次 git diff 为空；
- [ ] 前端不发送 revision；
- [ ] Question 并发回复 UI 稳定；
- [ ] 导出直接下载并有 loading/error；
- [ ] 中英文无原始失败码；
- [ ] 四种模式完整 E2E；
- [ ] 浏览器刷新后状态与 PostgreSQL 事实一致。

## 15. CI 验收门禁

所有阶段最终合并前必须运行：

```powershell
dotnet restore backend/NoCTF.slnx
dotnet build backend/NoCTF.slnx --configuration Release --no-restore
dotnet test backend/NoCTF.slnx --configuration Release --no-build
dotnet run --file backend/tests/e2e.cs -- --mode all --suite full
```

前端：

```powershell
Set-Location backend/src/NoCTF.API/ClientApp
bun install --frozen-lockfile
bun run test
bun run typecheck
bun run build
bun run generate
```

契约与仓库：

```powershell
dotnet run --project backend/src/NoCTF.API/NoCTF.API.csproj `
  --configuration Release --no-build --no-launch-profile -- --export-openapi

Set-Location backend/src/NoCTF.API/ClientApp
bun run api:gen
Set-Location ../../../..
git diff --check
git status --short
```

如仓库新增 lint 脚本，必须加入 CI；当前没有 lint 脚本时不得把 lint 报告为已通过。任何跳过、阻塞或环境不可用必须明确报告，不能视为通过。

## 16. 发布、切换与回滚

### 16.1 发布前

- 冻结旧版本写流量；
- 完成 PostgreSQL 全量备份、对象存储清单和 Wolverine 队列状态记录；
- 在独立环境完成备份恢复演练；
- 确认旧 durable queues 已排空或有明确丢弃审批；
- 确认新应用使用全新数据库或经批准的数据搬运结果；
- 确认 Redis 可清空重建；
- 验证新基线、OpenAPI、SDK、镜像 commit 一致。

### 16.2 数据策略

本规范不提供旧 schema 的原位兼容 migration。默认切换为全新基线数据库。

若业务要求保留旧数据，必须另行设计一次性、可审计的数据搬运程序：

- 输入是冻结的旧库备份；
- 输出是全新基线；
- 逐表映射并验证计数、外键、哈希和业务不变量；
- 不把兼容列带入新模型；
- 搬运程序不成为长期运行代码；
- 未经单独评审不得执行生产搬运。

### 16.3 回滚

- 发布失败时停止新写入；
- 回滚旧应用并恢复切换前旧库备份；
- 禁止让旧应用连接已经写入新 schema 的数据库；
- 新版已产生的数据不尝试在线反向转换；
- 记录丢失窗口和恢复点，重新排期切换。

## 17. Definition of Done

只有同时满足以下条件，迁移才算完成：

- 本文及所有权威文档、`AGENTS.md`、代码、数据库、OpenAPI、SDK、UI 一致；
- 业务表正好 15 张；
- 只有一个 EF 生成 `InitialBaseline`；
- 无 Revision/ExpectedRevision、DataExport、Dirty/Snapshot、旧 Runtime/调度字段；
- Wolverine competing consumers、fan-out、Sticky 校验、Inbox/Outbox 和 Singular Agent 的真实依赖测试通过；
- Runtime node routing 和资源清理 E2E 通过；
- 排行榜事件驱动失效、500ms 合并、cache miss 重建通过；
- 四种模式 E2E、完整后端测试、前端测试/typecheck/build 通过；
- 备份恢复、Redis 丢失、Worker failover 演练通过；
- 发布 Runbook、监控、告警、HANDOFF 和 Alpha 版本更新完成；
- 没有未说明的跳过项或兼容分支。

## 18. 禁止的捷径

- 禁止把 Revision 改名后继续持久化；
- 禁止用 EF InMemory 证明 PostgreSQL 并发/数组/约束/Wolverine 行为；
- 禁止手改 migration 或 snapshot；
- 禁止 Sticky Handler 自动退化为本地 queue；
- 禁止全局启用 `MultipleHandlerBehavior.Separated`；
- 禁止用进程内 Channel 替代 durable business queue；
- 禁止为了补跑而恢复业务 next-run 字段；
- 禁止用 `LeaderboardDirty`、Snapshot JSON 或新表兜底；
- 禁止把同步导出失败回退成 DataExport 后台任务；
- 禁止在 API/前端手写 DTO、URL、枚举或失败码；
- 禁止以“兼容旧版”为由保留旧数据模型；
- 禁止在生产数据库运行 migration remove、drop 或未演练的数据搬运。

## 19. 需求—证据追踪表

每个实现 PR 必须更新下表对应证据链接；证据可以是测试名、CI artifact、OpenAPI diff、SQL schema 报告或演练记录。

| 需求 | 必要证据 | 本次实现证据 |
|---|---|---|
| 删除并发字段 | 架构测试 + OpenAPI/SDK 搜索结果 + 并发 PostgreSQL 测试 | [阶段 1](data-model-wolverine-stage1-revision-removal.md) + 完整 Architecture/真实 PostgreSQL 测试 |
| 15 表基线 | EF migration 列表 + schema 查询 + model drift | [阶段 10](data-model-wolverine-stage10-ef-baseline.md) + `DataModelSchemaTests` |
| Sticky fan-out | 两 destination 集成测试 + 缺 endpoint 启动失败测试 | [阶段 7](data-model-wolverine-stage7-messaging-topology.md) + `NoCtfWolverineTopologyTests` |
| Singular Agent | 多 Worker 选主与 failover 测试 | [阶段 8](data-model-wolverine-stage8-singular-agent.md) + `WolverineTransactionalOutboxTests` |
| 不补跑 | 停机跨多个 tick 的恢复测试 | [阶段 8](data-model-wolverine-stage8-singular-agent.md) 的 no-catch-up Testcontainers 场景 |
| Runtime node queue | 多 Runner 路由 E2E + pool queue 不存在证明 | [阶段 5](data-model-wolverine-stage5-runtime-routing.md) + 四模式 Runtime E2E |
| AWD 每检一 Fact | 连续健康/不健康/恢复测试 | [阶段 6](data-model-wolverine-stage6-gameplay-facts.md) + AWD 完整 E2E |
| AWDP 映射 | 六种 outcome 参数化测试 + 事件 Payload 校验 | [阶段 6](data-model-wolverine-stage6-gameplay-facts.md) + `AwdpFixResolvedEventPayloadTests` |
| 排行榜重建 | cache miss、崩溃、Redis loss、Frozen/Hidden 测试 | [阶段 9](data-model-wolverine-stage9-leaderboard.md) + `LeaderboardProjectionPersistenceTests` |
| 同步导出 | stream/cancel/limit/permission/audit 测试 | [阶段 4](data-model-wolverine-stage4-streaming-exports.md) + 完整 API/真实 PostgreSQL 测试 |
| 邮箱隐私 | 协议快照 + 权限测试 + 日志扫描 | [阶段 2](data-model-wolverine-stage2-core-privacy.md) + 权限/协议完整测试 |
| 赛道邀请码 | 角色矩阵 + 日志/导出泄漏扫描 | [阶段 2](data-model-wolverine-stage2-core-privacy.md) + 赛道权限与导出完整测试 |

未经证据闭环的勾选不算完成。
