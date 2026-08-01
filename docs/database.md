# PostgreSQL 数据模型

本文给出目标模型，不兼容旧数据库。实现完成后用 `dotnet ef migrations remove` 移除旧链，再以 `dotnet ef migrations add InitialBaseline` 生成唯一基线；任何 migration、Designer、Snapshot 都不得手改。

## 全局约定

- PostgreSQL 是唯一正式业务数据库。
- 表、列、索引使用 `snake_case`。
- 应用生成 UUIDv7 主键；AWD Round SpecificationId 使用专门的十进制文本编码 Guid。
- 时间使用 `timestamp with time zone` 和 UTC `DateTimeOffset`。
- C# enum 保存为 `smallint`；开放正文使用 text，其他字符串必须设置长度。
- 最终分数为 bigint；DynamicExpresso 原始值/中间值为 numeric/decimal。
- 软删除只有 nullable `deleted_at`，不存 is_deleted/deleted_by。
- EF 优先 Data Annotations；jsonb、uuid[]、GIN、部分索引、XOR/check constraint 等必要能力才使用小型 Fluent 配置。
- 业务不使用级联删除。最终硬删除由应用按聚合顺序执行并协调对象存储清理。

## 枚举目录

数据库保存 enum 数值，但下列名字是领域与 OpenAPI 的唯一语义；不得另造同义字符串：

```text
GameMode: Ctf | Awd | Awdp | Koh
UserKind: Human | Bot
CompetitionStatus: Draft | Visible | Published | Running | Paused | Finished
TeamRegistrationStatus: Pending | Approved | Rejected
SubmissionKind: Flag | Break | Fix
EvaluationDispatchMode: Automatic | ManualBatch
SubmissionEvaluationState: Pending | Queued | Processing | Completed | PlatformFailed
ScoringEventKind: SubmissionEvaluation | AwdServiceStatus | KohObservation | HintUnlock
ScoringResult: Correct | Wrong | Duplicate | AttemptsExhausted | Rejected | PlatformFailed
SpecificationKind: Attachment | AwdRound | RuntimeDefinition | Hint
RuntimeKind: Container | Compose | OvaVm
RuntimeProvider: Docker | Kubernetes | Libvirt
RuntimeState: Queued | Provisioning | Running | Stopping | Stopped | Failed
RuntimeFailureCode: InvalidConfiguration | RunnerUnavailable | ProviderUnavailable | ProvisionTimeout | ProviderRejected | CleanupFailed | UrlExpansionFailed
```

`FailureCode` 是同一个强类型 enum，不是任意文本。各模式文件给出的名字即稳定成员；新增成员必须同步模式规则、ProblemDetails/OpenAPI 和投影测试。Submission 的 PlatformFailed 不创建 ScoringEvent；`ScoringResult.PlatformFailed` 仅用于 KoH 等必须保留失败观测的系统事实。

## users

| 列 | 类型/规则 |
|---|---|
| id | uuid PK |
| user_name / normalized_user_name | varchar(64)，Normalized 唯一 |
| kind | smallint Human/Bot |
| email / normalized_email | varchar(320) 非空，Normalized 唯一 |
| password_hash | text 非空，ASP.NET Core Identity V3 |
| role | smallint User/Organizer/Administrator |
| token_version | integer >= 0 |
| email_verified_at | timestamptz nullable |
| created_at / updated_at | timestamptz |

Human 使用注册输入的 email 和 password。Bot 使用服务端生成的
`bot-<user-id-N>@bot.invalid`/大写 NormalizedEmail，以及一次性随机 GUID 的 PasswordHash；
GUID 明文不返回、不记录，`email_verified_at` 必须为空。Bot 只能使用 User 或 Organizer
角色。Role 变更、Human 密码修改/重置、管理员全局失效会递增 token_version。

## email_verification_tokens

仅在邮箱验证开启时使用。字段：id、user_id、token_sha256 bytea(32)、expires_at、consumed_at、created_at。只保存 Token Hash；`token_sha256` 唯一。消费与设置 User.EmailVerifiedAt 同事务。

## competitions

字段：

```text
id, title, description
owner_id
manager_ids uuid[]
judge_ids uuid[]
observer_ids uuid[]
permission_revision
mode
status
start_at, end_at
running_since?
accumulated_running_seconds
team_registration_auto_approve
max_team_members
max_concurrent_runtime_instances_per_team
configuration_json jsonb
configuration_revision
configuration_updated_at
leaderboard_revision
flag_derivation_secret bytea(32)
created_at, updated_at, deleted_at?
```

约束：

- StartAt < EndAt；MaxTeamMembers > 0；runtime 上限 <= 0 表示无限。
- 三个权限数组内部去重、彼此互斥，且不包含 OwnerId。
- FlagDerivationSecret 创建时 CSPRNG 生成，之后不可修改、不可序列化到 DTO/日志。
- RunningSince 只在 Running 非空。Pause/Finish 时把时间差累加到 AccumulatedRunningSeconds 并清空。
- EffectiveRunningTime = AccumulatedRunningSeconds + 当前 Running 区间。
- PermissionRevision、ConfigurationRevision 与 LeaderboardRevision 单调递增；所有共享
  LeaderboardRevision 写入必须使用数据库表达式原子递增，不能先读到客户端再写回 `R+1`。

对 owner_id 与三个 UUID 数组建查询索引；数组使用 GIN。

## competition_lifecycle_audits

Competition owned child：id、competition_id、from_status、to_status、actor_user_id nullable（系统自动转换为空）、reason enum、occurred_at。只追加，不作为独立 DbSet/Application root。

## teams

```text
id, competition_id
name, normalized_name, avatar_url?
captain_id
member_ids uuid[]
invitation_token varchar(32)
registration_status
is_locked
is_banned, banned_at?, banned_by_id?, ban_reason?
registered_at
deleted_at?
```

约束与索引：

- Name 在接入时 Trim，NormalizedName=`Name.Normalize(FormKC).ToUpperInvariant()`；`(competition_id, normalized_name)` 在未删除记录中唯一。
- invitation_token 全局唯一。
- member_ids 非空、无重复、包含 captain_id，长度 <= Competition.MaxTeamMembers。
- member_ids 使用 GIN。
- “用户在同一 Competition 只能属于一队”由 `(CompetitionId, UserId)` advisory lock + GIN 查询在应用事务保证；数组元素无法使用普通唯一索引。
- 不存在 TeamMember/TeamInvitation 表。

## challenges

全局模板字段：id、owner_id、manager_ids uuid[]、mode、visibility、title、description、direction、definition_json jsonb、revision、created_at、updated_at、deleted_at。DefinitionJson 保存 provider-neutral Runtime/Checker/Flag 注入定义，不得包含 RuntimeProvider 或 RunnerPool。ManagerIds 无重复且不含 OwnerId，使用 GIN。存在未删除 CompetitionChallenge 引用时禁止删除。

## challenge_attachments

Challenge owned child：id、challenge_id、object_key、file_name、content_type、byte_length、sha256 bytea(32)、created_at、deleted_at?。

- 内容不可原位替换；新内容创建新 Id。
- 被 ChallengeFlag Specification 或 RandomOnePerTeam 选择引用时禁止删除。
- ObjectKey 唯一，Bucket 永不公开。

## competition_challenges

字段：id、competition_id、challenge_id、base_score、order、is_published、configuration_json jsonb、revision、updated_at、deleted_at。

- `(competition_id, order)` 在未删除记录唯一。
- `(competition_id, challenge_id)` 是否唯一由产品实现固定为唯一：同一模板在同一比赛只引用一次。
- BaseScore > 0。
- Mode-specific Configuration 使用 versioned typed JSON；不接受未知 schemaVersion。

## competition_challenge_hints

CompetitionChallenge owned child：id、competition_challenge_id、content、cost bigint >= 0、published_at nullable、created_at、updated_at、deleted_at。

Cost=0 且到发布时间后直接可见；Cost>0 需要 HintUnlock ScoringEvent。Hint 变更与父
CompetitionChallenge 管理写共享 Competition transaction lock，并在同一事务内递增
CompetitionChallenge.Revision 与 Competition.LeaderboardRevision。

## challenge_flags

字段：

```text
id
challenge_id?
competition_challenge_id?
team_id?
flag text
flag_sha256 bytea(32)
specification_kind?
specification_id?
valid_start?
valid_until?
created_at
deleted_at?
```

约束：

- ChallengeId 与 CompetitionChallengeId 必须且只能有一个。
- 模板级 Flag 的 TeamId 必须为空，SpecificationKind 只允许 Attachment/RuntimeDefinition；TeamId 非空时必须是 CompetitionChallenge scope 且 Team 与实例属于同一 Competition。
- SpecificationKind/Id 同时为空或同时非空。
- Attachment Id 必须属于 scope Challenge（实例 scope 时属于实例链接的 Challenge）；AwdRound 必须是 CompetitionChallenge+Team scope；Hint 永远不得用于 ChallengeFlag。RuntimeDefinition Id 是 versioned configuration JSON 内稳定、不复用的 `definitionId`。
- 若两个窗口都非空，ValidStart < ValidUntil。
- 不存在 Active/Status/PendingInjection。
- Flag 长度为 1..4096 UTF-8 bytes、不得含 NUL；数据库 text 长度不是字节约束，应用必须二次验证。
- FlagSha256 只做候选查询；最终使用原文固定时间、ordinal、区分大小写比较。
- Hash 索引组合 scope、team、specification 与窗口；不设 Flag 全局唯一，允许第 10 次生成碰撞后保存重复值。

## patch_uploads

字段：id、competition_id、competition_challenge_id、team_id、uploaded_by_user_id、object_key、original_file_name、content_type、byte_length、sha256 bytea(32)、uploaded_at、consumed_at nullable、submission_id nullable。

- 每个 Team/CompetitionChallenge 最多一个 `consumed_at is null`（部分唯一索引）。
- consumed_at 与 submission_id 同时为空或同时存在；Submission 一对一唯一。
- 无过期时间。新上传替换并硬删除旧未消费行/对象；消费后保留到 Competition 最终硬删除。

## submissions

字段：

```text
id
competition_id
competition_challenge_id
team_id
submitted_by_user_id
kind
submitted_flag?
submitted_flag_sha256? bytea(32)
patch_upload_id?
received_at
evaluation_state
evaluation_failure_code?
evaluation_updated_at
processing_version
current_scoring_event_id?
```

约束：

- Flag/Break 必须有 Flag 且没有 PatchUpload；Fix 反之。
- PatchUpload 一对一唯一。
- 原始输入、ReceivedAt、身份不可修改；只更新 EvaluationState、EvaluationFailureCode、EvaluationUpdatedAt、ProcessingVersion 与 CurrentScoringEventId。
- EvaluationFailureCode 只在 EvaluationState=PlatformFailed 非空，保存本次平台失败原因；重新 Queued 时清空。Completed 的业务结果/FailureCode 只读 CurrentScoringEvent，不复制到 Submission。
- 不存 batch id/index、victim、specification、分数或软删除字段。
- 管理查询索引覆盖 CompetitionChallenge、Team、User、Kind、State、ReceivedAt/Id 与 FlagSha256。
- 默认 keyset 顺序 `received_at desc, id desc`。

## scoring_events

字段：

```text
id
competition_id
competition_challenge_id
submission_id?
team_id?
victim_team_id?
kind
result
failure_code?
specification_kind?
specification_id?
processing_version?
competition_configuration_revision
competition_challenge_revision
occurred_at
created_at
deleted_at?
```

Kind 只允许：

- SubmissionEvaluation；
- AwdServiceStatus（Correct=Up，Wrong=Down）；
- KohObservation；
- HintUnlock。

规则：

- 永不保存 Points/ScoreDelta/累计分。
- SubmissionEvaluation 必须有 SubmissionId/ProcessingVersion，Result 只允许 Correct/Wrong/Duplicate/AttemptsExhausted/Rejected；Submission 平台故障不建事件。当前未删除事件对 SubmissionId 唯一。
- CTF/AWD/AWDP Flag 匹配到具体 Flag 时可把其 SpecificationKind/Id 复制到事件；AWD Correct/Duplicate 必须是 AwdRound 且有 VictimTeamId。AWDP Achievement 轮次不固化到事件，投影按 ReceivedAt、当前 RoundDuration 与生命周期审计计算。
- AwdServiceStatus 必须有 TeamId、SubmissionId/ProcessingVersion/VictimTeamId/Specification 均为空，只有 Correct=Up、Wrong=Down；平台失败不建事件。
- KoH：SubmissionId/VictimTeamId/Specification 为空；Correct+TeamId=Controlled；Wrong+FailureCode=Uncontrolled 且 TeamId 空；PlatformFailed+FailureCode=ProducerUnavailable/ProducerTimeout/AmbiguousFlagMatch 且 TeamId 空。
- HintUnlock：Correct、TeamId、SpecificationKind.Hint、SpecificationId=HintId；同队同 Hint 当前事件唯一。
- 重判事务：插入新事件、软删除旧事件、更新 Submission.CurrentScoringEventId 与状态，全部原子完成。

## runtime_instances

字段：

```text
id, competition_id, competition_challenge_id, team_id?
generation
runtime_kind, runtime_provider
runner_id?, runner_pool
state
failure_code?
processing_version
replaces_runtime_instance_id?
provider_receipt_json? jsonb
urls text[]
participant_url_indexes integer[]
control_check_url?
awd_checker_target_host?
checker_status, checker_status_updated_at?
checker_sequence
last_applied_checker_sequence
next_checker_due_at?
created_at, running_at?, expires_at?, stopped_at?
```

- `(competition_challenge_id, team_id, generation)` 唯一。
- CTF/AWD 同队同题最多一个 Queued/Provisioning/Running/Stopping；KoH TeamId null 时同题最多一个共享活动实例。使用部分唯一索引。
- ReplacesRuntimeInstanceId 只能指向同 CompetitionChallenge/Team 的较小 Generation；仅 Reset 设置。替换前后 Generation 在并发额度中合并算一个槽。
- RunnerId、实际 RuntimeProvider/RunnerPool、receipt、URL 在平台调度/Provider 成功后逐步填写；题目定义和 Competition 不保存 placement。URLs 与 ParticipantUrlIndexes 缺省空数组。indexes 是严格递增、无重复、0-based 且必须落在 URLs 范围内，固化该 Generation 的 Exposure；玩家自己的 `urls` 返回全部，AWD/KoH 跨队公开只按 indexes 选择。
- RuntimeInstance 不保存 Challenge 定义版本；定义修改只影响下一次 Start/Reset。
- CheckerStatus 为 Unknown/Up/Down/CheckerAbnormalExit/CheckerTimedOut，后一次 internal callback 覆盖前一次；诊断状态不直接计分。
- AwdCheckerTargetHost 只保存平台从本次 Runtime receipt 得到的内部 DNS host，不保存 URL、端口或题目定义版本。
- FailureCode 必须且只能在 State=Failed 非空；对玩家只返回 code，管理详情另可返回脱敏 receipt/error。
- URL 是展开后的完整受保护字符串；只有 Running 才返回选手。
- ControlCheckUrl 只允许 KoH，Running 时必须是展开后的绝对 URL；只供 Worker 轮询，绝不进入玩家 DTO/SignalR/日志。
- Failed 在资源尚未收敛时保留 receipt 供幂等清理但不返回 URL；原 Runner 的资源审计确认
  identity cleanup 与容量释放都成功后清除 receipt/assignment marker，FailureCode 仍保留。
- 不软删除，不存在 runtime_operations 表。

## 外键与删除规则

- 所有业务 FK 使用 `ON DELETE RESTRICT/NO ACTION`；数据库不靠 cascade 清业务聚合。
- CompetitionChallenge.CompetitionId/ChallengeId、Team.CompetitionId、Hint.CompetitionChallengeId、Attachment.ChallengeId 必须存在且未删除才可在应用中创建新引用。
- Submission 的 CompetitionId、CompetitionChallengeId、TeamId 必须属于同一 Competition；PatchUpload 同理。数据库 FK 保证存在性，应用在同一事务校验跨表归属。
- ScoringEvent 的 Competition/CompetitionChallenge/Team/VictimTeam/Submission 必须归属一致；VictimTeam 只允许 AWD SubmissionEvaluation。
- Submission.CurrentScoringEventId 必须回指同一 Submission 的当前未删除事件。事件先插入，再更新指针，不需要可延迟 FK。
- 最终硬删除顺序固定为：停止并清理 Runtime/对象 -> Notification/ScoringEvent -> Submission/PatchUpload/Flag/Hint -> CompetitionChallenge -> Team/LifecycleAudit -> Competition。Challenge 只有在无未删除引用后，按 Flag/Attachment -> Challenge 删除。

## 必需唯一索引

除各节已写索引外，至少包含：

- users.normalized_user_name、users.normalized_email、teams.invitation_token、challenge_attachments.object_key、patch_uploads.object_key 全局唯一；
- teams `(competition_id, normalized_name) WHERE deleted_at IS NULL`；
- competition_challenges `(competition_id, challenge_id) WHERE deleted_at IS NULL` 与 `(competition_id, order) WHERE deleted_at IS NULL`；
- patch_uploads `(team_id, competition_challenge_id) WHERE consumed_at IS NULL`，以及非空 submission_id 唯一；
- scoring_events `submission_id WHERE submission_id IS NOT NULL AND deleted_at IS NULL`；HintUnlock 使用 `(team_id, specification_id) WHERE kind=HintUnlock AND deleted_at IS NULL`；
- challenge_flags 对 `(competition_challenge_id, team_id, specification_kind, specification_id) WHERE deleted_at IS NULL AND specification_kind IN (AwdRound, RuntimeDefinition)` 唯一，保证每队每轮/RuntimeDefinition 一条；Attachment 允许同附件多条答案，不能套用此唯一索引；
- runtime_instances 活动实例部分唯一索引；ChallengeFlag 不设 Flag 原文/Hash 全局唯一。

所有部分索引、GIN、jsonb、XOR/check 和跨列值转换是允许使用 Fluent configuration 的必要例外；普通列/长度/精度/简单索引仍用 Data Annotations。

## 必需查询索引

- challenge_flags：`(challenge_id, flag_sha256) WHERE deleted_at IS NULL` 与 `(competition_challenge_id, flag_sha256) WHERE deleted_at IS NULL`；另有 `(competition_challenge_id, team_id, specification_kind, specification_id)` 支持 Round/RandomOne/预生成查询。
- submissions：管理列表使用 `(competition_id, received_at DESC, id DESC)`，题/队/类型/状态各以前导 CompetitionId 建组合索引；Flag 精确查使用 `(competition_id, submitted_flag_sha256, received_at DESC, id DESC)`。
- scoring_events：投影使用 `(competition_id, deleted_at, occurred_at, id)`，Submission 历史使用 `(submission_id, created_at, id)`，AWD 攻击去重使用 CompetitionChallenge/Specification/Team/VictimTeam/Result 的组合索引。
- runtime_instances：当前实例使用 `(competition_challenge_id, team_id, generation DESC)`；Runner claim 使用 `(runner_pool, state, created_at, id)`；Checker 调度使用 `(state, next_checker_due_at) WHERE next_checker_due_at IS NOT NULL`。
- notifications：`(user_id, created_at DESC, id DESC)` 与唯一 `(user_id, source_event_key)`；LifecycleAudit：`(competition_id, occurred_at, id)`。

索引只优化候选集，不能替代 Application 的归属、权限、Flag 原文固定时间比较或计分规则。

## notifications

字段：id、user_id、kind、competition_id nullable、entity_id nullable、payload_json jsonb、created_at。Kind 与 Payload 使用强类型映射；没有 ReadAt、未读计数、删除或过期逻辑，保留到 User 最终硬删除。

## Wolverine 系统表

Wolverine 管理 Inbox、Outbox、Scheduled 与 Dead Letter 表；它们不是 Domain Entity/DbSet，不手写或复用为业务状态。Runner/Worker/API 共享 PostgreSQL transport，但使用各自 durable queue。

## 关键事务

### Submission 接入

验证/限流 -> 非 AWD 获取 Team/题/Kind advisory lock -> 次数预检 -> Insert Submission(s) -> 消费 PatchUpload（Fix）-> Outbox -> commit。AWD batch 在完成全部内存格式验证后用一次集合 Insert/Outbox 事务，不获取次数锁；失败整体回滚。

### Manual drain / 集合重判

接入事务只固定筛选 cutoff 并写 durable drain Outbox。Worker 循环：按索引查询最多 500 -> `FOR UPDATE SKIP LOCKED` -> 再校验 State/CurrentScoringEventId -> 更新 ProcessingVersion/Queued -> 写逐项 Outbox -> commit。不得对完整筛选集开启长事务，也不保存 Batch/Rejudge 行。

### 重判替换

锁 Submission -> 校验 ProcessingVersion -> 新 ScoringEvent -> 旧事件 DeletedAt -> 更新
CurrentScoringEventId/State -> 数据库原子 LeaderboardRevision++ -> Outbox -> commit。

### Hint 解锁

锁 Competition/Team 解锁键 -> 用数据库事实与当前配置计算权威总分 -> 校验覆盖 Cost ->
Insert HintUnlock Event -> 数据库原子 LeaderboardRevision++ -> Outbox -> commit。不同 Team
可以并发解锁，因此不能依赖该锁串行化 Competition 的共享 revision。

### Runtime 状态

锁 RuntimeInstance -> 版本栅栏 -> 更新 State/receipt/URLs/timestamps -> Outbox -> commit。外部 Provider 调用不得发生在持有业务事务期间。
