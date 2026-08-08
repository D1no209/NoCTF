# NoCTF PostgreSQL 数据模型（InitialBaseline）

当前数据库是一次性重建的业务基线，不提供旧数据库、旧 migration 或 persistence/API 兼容层。EF migration 必须使用 CLI 生成，业务 schema 固定为 17 张表：

`users`、`competitions`、`competition_events`、`teams`、`challenges`、`challenge_attachments`、`competition_challenges`、`challenge_flags`、`runtime_instances`、`patch_uploads`、`account_tokens`、`platform_settings`、`notifications`、`submissions`、`scoring_events`、`data_exports`、`files`。

## 共通约定

- 主键由应用生成 UUIDv7；时间是 UTC `timestamptz`；领域 enum 存 `smallint`。
- 所有业务 FK 使用 `ON DELETE RESTRICT`。业务硬删除由应用按引用顺序执行。
- `jsonb` 必须是 object/array 约定的强类型 payload，并带 `schemaVersion`；未知版本拒绝写入。
- 文件元数据创建后不可变；FileId 可以被多个业务对象显式复用。
- Wolverine 的 inbox/outbox/scheduled/dead-letter 表属于 Wolverine，不是业务表。

## 表清单与字段

### `users`

`id uuid PK`、`user_name varchar(64)`、`normalized_user_name varchar(64) UNIQUE`、
`kind smallint`、`email varchar(320)`、`normalized_email varchar(320) UNIQUE`、`password_hash text`、
`role smallint`、`token_version int`、`email_verified_at timestamptz?`、`description varchar(2000)?`、
`is_email_public bool`、`avatar_file_id uuid?`、`account_status smallint`、`created_at`、`updated_at`。

### `competitions`

拥有者/协作者直接存 `owner_id uuid`、`manager_ids uuid[]`、`judge_ids uuid[]`、`observer_ids uuid[]`；
另有 `id`、`title`、`description`、`mode`、`status`、`start_at`、`end_at`、运行时钟字段、团队限制、
`configuration_json jsonb`、`configuration_revision`、`permission_revision`、`leaderboard_revision`、
`flag_derivation_secret bytea(32)`、`leaderboard_visibility` 状态字段、`poster_file_id uuid?`、时间戳和 `deleted_at?`。

### `competition_events`

`id`、`competition_id`、`kind`、`level`、`visibility`、`actor_user_id?`、
`subject_type`/`subject_id`、`related_type?`/`related_id?`、`parent_event_id?`、`payload_json jsonb`、`occurred_at`。

这是比赛范围内 append-only 事实流。生命周期和排行榜可见性 payload 至少包含 `from`、`to`、
`automatic`、`reason`（可见性另含 `dataCutoffAt`）。`related_type` 与 `related_id` 必须成对出现。

### `teams`

`id`、`competition_id`、`name`、`normalized_name`、`avatar_file_id?`、`captain_id`、`member_ids uuid[]`、
`invitation_token varchar(32)`、注册/锁定/封禁字段、`registered_at`、`deleted_at?`。成员数组无重复且包含 Captain；
不存在 TeamMember/TeamInvitation 表。

### `challenges`

全局模板：`id`、`owner_id`、`manager_ids uuid[]`、`mode`、`visibility`、`title`、`description`、`direction`、
`definition_json jsonb`、`revision`、时间戳、`deleted_at?`。不保存比赛、排序、分数、Hint 或 RuntimeProvider。

### `challenge_attachments`

`id`、`challenge_id`、`file_id`、`created_at`、`deleted_at?`。全部对象存储元数据位于 `files`。

### `competition_challenges`

`id`、`competition_id`、`challenge_id`、`base_score bigint`、`order`、`is_published`、`rules_json jsonb`、
`revision`、AWD 调度游标、`updated_at`、`deleted_at?`、`hints_json jsonb`（EF owned JSON collection）。

Hint 元素为 `{ id, content, cost, publishedAt, hiddenAt }`；Id 在数组内稳定且唯一，只能追加或隐藏，
修改递增 challenge revision 与 competition leaderboard revision。

### `challenge_flags`

Flag 原文/Hash、Challenge 或 CompetitionChallenge 作用域、Specification、Team、有效时间、创建/删除时间。
模板级与比赛级作用域互斥；Submission、ScoringEvent、RuntimeInstance 始终引用 CompetitionChallenge。

### `runtime_instances`

运行实例状态、Generation、Provider receipt、URL、Checker 状态、Runner assignment、时间戳和 `published_ports_json jsonb`。
端口元素为 `{ serviceName?, containerPort, hostPort, allocatedAt }`，同实例 service/containerPort 唯一，端口范围 1–65535。

### `patch_uploads`

保留业务范围、上传者、消费状态和 `submission_id?`；存储元数据只保留 `file_id`。

### `account_tokens`

`id uuid PK`、`user_id`、`kind smallint`（`EmailVerification|PasswordReset`）、`token_sha256 bytea(32) UNIQUE`、
`expires_at`、`consumed_at?`、`invalidated_at?`、`created_at`。`(user_id, kind, created_at DESC)` 索引；时间约束保证有效顺序，
PasswordReset 生成新令牌时失效旧有效令牌。

### `platform_settings`

强类型 singleton：`id smallint CHECK(id=1)`、基础信息、`logo_file_id?`、邮箱验证/密码重置生命周期和冷却、SMTP 主机/端口/安全模式/账号、
`email_smtp_password_ciphertext bytea?`、发件人、超时、`revision bigint`、`updated_at`。所有管理意图共享同一 Revision。

### `notifications`

`id`、`source_type`/`source_id?`、`target_type`/`target_id`、`kind`、`content_json jsonb`、`sent_at`、
`related_type?`/`related_id?`、`reply_to_id?`。Source 为 User/Competition/Team 时 SourceId 必填；System/Platform 为空。
Target 支持 User、CompetitionCollaborators、CompetitionParticipants、TeamMembers、PlatformAdministrators（固定全 F UUID）。
动态受众在读取时解析，不为每个 User 复制行；`reply_to_id` 唯一过滤索引保证线性链。无 ReadAt、未读计数、编辑或删除。

管理员比赛公告：`SourceType=User`、`SourceId=发送者 UserId`、`TargetId=CompetitionId`、默认 `TargetType=CompetitionCollaborators`；
面向选手的公告显式使用 CompetitionParticipants。

### `submissions`

`id`、比赛/队伍/题目/提交者 FK、`kind`、`received_at`、`submitted_flag?`、`submitted_flag_sha256?`、`patch_upload_id?`、
评估状态/失败原因、版本栅栏、`current_scoring_event_id?` 和评估结果摘要 Hash。

Kind 字段约束：Flag/Break 保存原文和 32 字节 Hash；Fix 只保存 PatchUploadId；HintUnlock 保存小写 canonical UUID 文本；
ManualAdjust 保存 canonical signed Int32 十进制文本（例如 `-10`、`25`），拒绝 `+25`、前导零、`-0`、零值和越界。
不新增 `additional_id`、score 或 delta 列。HintUnlock 与 ManualAdjust 都必须有 CompetitionChallengeId。

### `scoring_events`

`id`、比赛/题目/队伍/受害队伍/Submission/Specification 引用、`kind`、`result`、`failure_code?`、处理版本、配置版本、时间戳、`deleted_at?`。
ManualAdjust 始终 `Kind=ManualAdjust, Result=Correct`；HintUnlock 使用 `Kind=HintUnlock, SpecificationKind=Hint`。
每次新建或替换当前事件都原子递增 LeaderboardRevision，并通过 Outbox 失效排行榜；事件不保存累计分。

### `data_exports`

导出范围、申请人、状态、时间、失败原因和 `file_id?`；下载时由 File 提供文件名/MIME/长度/Hash。

### `files`

`id uuid PK`、`object_key varchar(1024) UNIQUE`、`file_name varchar(260)`、`content_type varchar(255)`、
`byte_length bigint CHECK(byte_length>=0)`、`sha256 bytea CHECK(octet_length=32)`、`created_at`。
没有 `deleted_at`。替换先创建新 File 并建立业务引用，旧 File 投递 `CleanupFile(FileId)`；Worker 锁行、检查全部引用、删除对象成功后硬删行。

## 索引重点

- CompetitionEvent：按 competition/occurred、kind、level、visibility、subject、related 和 parent 建组合索引。
- Notification：`(target_type,target_id,sent_at DESC,id DESC)`、source/related 过滤索引、唯一 `reply_to_id` 过滤索引。
- Submission：比赛时间线、题/队/Kind 组合、Flag Hash、当前 ScoringEvent 唯一索引。
- ScoringEvent：未删除 SubmissionId 唯一、比赛/occurred、Specification/Team 查询索引。
- Files：`object_key` 唯一；所有业务 File FK Restrict。

## 删除的表

`competition_lifecycle_audits`、`competition_leaderboard_visibility_audits`、`email_verification_settings`、
`email_verification_tokens`、`password_reset_tokens`、`competition_questions`、`competition_question_entries`、
`competition_challenge_hints`、`runtime_published_ports`、`durable_maintenance_schedules`、`user_account_lifecycle_audits` 均不存在。
