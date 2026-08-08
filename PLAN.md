# NoCTF 数据模型收敛重构

## Summary

将当前 26 张业务表收敛为约 17 张，删除独立审计、问答、Hint、端口和维护调度表。比赛事实统一进入 `competition_events`；用户消息、公告、问答和自然通知统一进入泛化 `notifications`；文件元数据统一进入 `files`。

不兼容旧数据库。完成模型修改后使用 EF CLI 移除现有迁移链并生成新的 `InitialBaseline`，不手改 migration 或 snapshot。

## Schema Changes

### 删除的表

- `competition_lifecycle_audits`
- `competition_leaderboard_visibility_audits`
- `email_verification_settings`
- `email_verification_tokens`
- `password_reset_tokens`
- `competition_questions`
- `competition_question_entries`
- `competition_challenge_hints`
- `runtime_published_ports`
- `durable_maintenance_schedules`
- `user_account_lifecycle_audits`

账号封禁、禁用、匿名化和物理删除不再写业务审计表，只保留平台运行日志。

### 新增 `files`

字段：

- `id uuid PK`
- `object_key varchar(1024) UNIQUE`
- `file_name varchar(260)`
- `content_type varchar(255)`
- `byte_length bigint`
- `sha256 bytea(32)`
- `created_at timestamptz`

文件元数据不可修改；一个 File 可被多个业务对象引用。没有 `deleted_at`，只有确认无任何引用并成功清理对象存储后才硬删除。

所有文件位置统一改为 FK：

- `users.avatar_file_id?`
- `teams.avatar_file_id?`，删除 `avatar_url`
- `competitions.poster_file_id?`
- `platform_settings.logo_file_id?`
- `challenge_attachments.file_id`
- `patch_uploads.file_id`
- `data_exports.file_id?`

`challenge_attachments` 保留独立 AttachmentId；`ChallengeFlag.SpecificationId` 继续引用 AttachmentId，而不是 FileId。各业务表删除 object key、文件名、MIME、长度和 hash 副本。不新增比赛级附件集合。

### 新增 `account_tokens`

合并邮箱验证与密码重置令牌：

- `id uuid PK`
- `user_id uuid FK`
- `kind smallint`：`EmailVerification | PasswordReset`
- `token_sha256 bytea(32) UNIQUE`
- `expires_at timestamptz`
- `consumed_at timestamptz?`
- `invalidated_at timestamptz?`
- `created_at timestamptz`

EmailVerification 要求 `invalidated_at IS NULL`；PasswordReset 可失效未消费令牌；消费与失效互斥。

### 重构 `platform_settings`

保持一行强类型宽表，不采用 KV：

- 保留平台名称、描述、`logo_file_id`
- 合并 Email Verification、Password Reset、SMTP 配置
- 邮件字段统一使用 `email_` 前缀
- 删除旧兼容字段 `smtp_enable_ssl`
- 整行共享一个 `revision` 和 `updated_at`
- SMTP 密码继续保存加密后的 bytea

### 重构 `competition_events`

作为比赛范围内、不可修改和不可删除的事实流：

- `id`
- `competition_id`
- `kind`
- `level`
- `visibility`
- `actor_user_id?`
- `subject_type + subject_id`，必填
- `related_type + related_id`，成对可空
- `parent_event_id?`
- `payload_json jsonb`，必填
- `occurred_at`

`subject/related` 使用受限实体枚举，由 Application 校验归属；只对 Competition、Actor 和 ParentEvent 保留普通 FK。Payload 按 EventKind 使用强类型契约。

生命周期与排行榜可见性变更的旧审计字段进入 Event payload。删除 Question 和 Announcement 专用 EventKind；问答和公告只存在于 Notification。

### 泛化 `notifications`

字段：

- `id`
- `source_type`
- `source_id?`
- `target_type`
- `target_id NOT NULL`
- `kind`
- `content_json jsonb`
- `sent_at`
- `related_type + related_id?`
- `reply_to_id?`

规则：

- Append-only，不支持编辑、删除、已读状态或线程表。
- `reply_to_id` 自引用形成链；回复必须保持根消息的 target。
- 线程中历史 source 用户继承后续回复读取权。
- 用户物理删除后保留 source_id，显示为“已删除用户”。
- Source 类型限制为 `System/User/Competition/Team/Platform`；System 允许 source_id 为空。
- Target 使用受众枚举：`User`、`CompetitionCollaborators`、`CompetitionParticipants`、`TeamMembers`、`PlatformAdministrators`。
- CompetitionCollaborators 动态解析当前 Owner、Manager、Judge、Observer。
- PlatformAdministrators 使用哨兵 TargetId `ffffffff-ffff-ffff-ffff-ffffffffffff`。
- 实体引用枚举包含 User、Competition、Team、Challenge、CompetitionChallenge、ChallengeHint、Submission、ScoringEvent、RuntimeInstance、DataExport、File、CompetitionEvent、Notification、Platform。
- 不增加 deduplication key，系统通知完全依赖 Wolverine Inbox/事务幂等。

### 内嵌 Hint 和 Runtime 端口

`competition_challenges` 新增 `hints_json jsonb`。每项包含稳定 `id`、`content`、`cost`、`publishedAt?`、`hiddenAt?`：

- Hint 禁止删除，只允许隐藏。
- Cost 允许修改，并按当前 JSON 重新计算历史 HintUnlock 扣分。
- 修改 Hint 时递增 CompetitionChallenge revision 和 LeaderboardRevision。

`runtime_instances` 新增非空 `published_ports_json jsonb` 数组，每项包含 `serviceName?`、`containerPort`、`hostPort`、`allocatedAt`。Application 验证端口范围及服务/容器端口唯一性。

## Submission、Scoring 与 API

### Submission 联合值

新增：

- `SubmissionKind.HintUnlock`
- `SubmissionKind.ManualAdjust`
- `ScoringEventKind.ManualAdjust`
- Hint failure code：`InsufficientScore`、`HintUnavailable`

不新增 `additional_id`。按 Kind 解释现有字段：

- Flag/Break：`submitted_flag` 是 Flag，hash 必填。
- Fix：使用 `patch_upload_id`。
- HintUnlock：`submitted_flag` 是 canonical UUID 文本，hash 为空。
- ManualAdjust：`submitted_flag` 是 canonical signed int32 十进制文本，hash 为空。

ManualAdjust 始终要求 `competition_challenge_id`，正负数均允许，输入在 API 入库前校验。

### 处理与重判

HintUnlock 和 ManualAdjust 都进入普通 Submission 队列：

- ManualAdjust 成功生成 `ScoringEvent(ManualAdjust, Correct)`。
- HintUnlock 成功为 Correct；重复为 Duplicate；分数不足为 `Rejected + InsufficientScore`；不可用为 `Rejected + HintUnavailable`。
- 平台故障仍只设置 Submission.PlatformFailed，不创建 ScoringEvent。
- 两种 Kind 都允许重判，但原始 `submitted_flag` 永远不可修改；重判只替换当前 ScoringEvent。
- ManualAdjust 投影解析 signed int32 delta。
- HintUnlock 投影通过 hint_id 读取当前 hints JSON cost，因此改价会重算历史排行榜。

### API

继续使用意图型 FastEndpoints，客户端不能自行填写 source/target：

- 新增管理员比赛题目校分端点，接收 `teamId + competitionChallengeId + delta`，返回 202 和 Submission 状态地址。
- Hint unlock 端点改为创建异步 Submission，返回 202。
- 现有 Question 创建、回复、状态查询接口改由 Notification 链实现；根 NotificationId 即 QuestionId。
- 删除 Question publication 流程；需要公开的答复改为独立 CompetitionAnnouncement。
- 新增管理员公告端点，受众仅允许 CompetitionCollaborators 或 CompetitionParticipants。
- Notification feed/thread 接口按动态受众和线程参与者权限查询。
- Event 只按需派生用户通知，不进行一比一复制。
- 更新 OpenAPI、生成 ClientApp API 类型，但不新增前端页面。

## Infrastructure、Migration 与 Tests

- 移除 DurableMaintenanceSchedule 实体和消息 ProcessingVersion。使用固定周期消息生产器向 Wolverine durable queue 发布 Runner reconciliation（30 秒）、Competition lifecycle（30 秒）、AWD checker dispatch（1 秒）；Handler 使用 PostgreSQL advisory lock、状态查询及 Inbox/Outbox 保证幂等，分页满时才发布立即续页消息。
- 更新对象存储流程：上传成功后创建 File；业务事务失败则补偿删除对象；孤儿清理必须扫描所有 File FK 后再删除对象和行。
- 使用 `dotnet ef migrations remove` 清除旧迁移链，随后由 EF 生成新的 `InitialBaseline` 和 snapshot；不编写数据迁移。
- 更新 `CONTEXT.md` 中 Submission、ScoringEvent、Notification、CompetitionEvent、File、Hint 的领域定义，并记录 Event/Notification 双流与统一 File 模型的 ADR。
- PostgreSQL Testcontainers 覆盖新 FK、check、jsonb、索引、文件复用和硬删除保护。
- TUnit 覆盖 Event append-only、生命周期事实查询、动态通知受众、回复链权限、Token Kind、Hint 动态改价、ManualAdjust 队列/重判、Runtime ports JSON 和多 Worker 维护 tick。
- 架构/OpenAPI 测试确认所有端点继续使用 strongly typed FastEndpoints、ExecuteAsync、TypedResults。
- 运行完整 build、单元测试、集成测试、E2E 和 OpenAPI drift 检查。
- 实施时保留当前 `new-frontend` 分支上的未提交修改，避免覆盖无关工作。
