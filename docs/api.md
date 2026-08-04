# API v1 清单

本文列出目标资源面。每个路由对应一个 StronglyTyped FastEndpoint 文件；请求/响应/Validator 放在该文件。业务码和精确 result union 依 [API 通用规范](api-conventions.md)。

## Authentication

```text
POST /api/v1/auth/register
POST /api/v1/auth/login
POST /api/v1/auth/refresh
POST /api/v1/auth/logout
POST /api/v1/auth/email-verification/verify
POST /api/v1/auth/email-verification/resend
GET  /api/v1/auth/me
PUT  /api/v1/auth/me/profile
POST /api/v1/auth/me/avatar
PUT  /api/v1/auth/password
POST /api/v1/auth/logout-all
GET  /api/v1/users/{userId}/avatar
```

Login/Refresh 返回 AccessToken 与 ExpiresAt；Refresh Cookie 不出现在 body。

## Competition 与 Team

```text
GET  /api/v1/competitions
GET  /api/v1/competitions/{competitionId}
GET  /api/v1/competitions/{competitionId}/leaderboard

POST /api/v1/competitions/{competitionId}/teams
GET  /api/v1/competitions/{competitionId}/teams
GET  /api/v1/competitions/{competitionId}/teams/me
GET  /api/v1/competitions/{competitionId}/teams/{teamId}
PUT  /api/v1/competitions/{competitionId}/teams/{teamId}
DELETE /api/v1/competitions/{competitionId}/teams/{teamId}
POST /api/v1/competitions/{competitionId}/teams/join
POST /api/v1/competitions/{competitionId}/teams/{teamId}/invitation-token/rotate
POST /api/v1/competitions/{competitionId}/teams/{teamId}/captain/transfer
DELETE /api/v1/competitions/{competitionId}/teams/{teamId}/members/{userId}
DELETE /api/v1/competitions/{competitionId}/teams/me/membership
POST /api/v1/competitions/{competitionId}/teams/{teamId}/registration/resubmit
```

Team response 使用 CaptainId 与 MemberIds 数组，不返回成员顺序。

Competition 列表只返回调用者可见状态：匿名可见 Visible/Published/Running/Paused/Finished，Draft 仅管理者。Team 私有字段（InvitationToken、Ban 原因）只按权限返回；公开 Team DTO 永不包含 InvitationToken。

Leaderboard GET 的 statusUrl 指回自身：无快照且投影中返回 202+targetRevision+Retry-After；有旧快照返回 200 并标 stale/revision；无快照且最后投影失败返回 503 ProblemDetails。它不创建独立 ProjectionOperation。
CTF Leaderboard 的 `bloods[]` 同时返回每题一血、二血、三血，每项带强类型 `bloodRank`；
`subjects[].slots[]` 以 nullable `bloodRank`/`bloodAt` 表示该队在该题是否获得前三血。

## Player Challenge

```text
GET  /api/v1/competitions/{competitionId}/challenges
GET  /api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}
GET  /api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/attachments
GET  /api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/attachments/{attachmentId}
GET  /api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/attachment
POST /api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/hints/{hintId}/unlock
```

`attachments` 列表与带 Id 下载只用于 `All`；`attachment` 单数路由只用于 `RandomOnePerTeam`，抽取发生在该 GET 内且请求没有 AttachmentId。策略不匹配返回 404。KoH 详情在 Running 时对本队返回 Control Flag 与 shared Hill 的公开 urls，不返回 ControlCheckUrl；CTF PerTeamRuntime Flag 不单独返回，注入 Runtime 环境。

Hint unlock 成功首次返回 201+HintUnlock 事实；已解锁或 Cost=0 直接可见时返回 200 且不创建新事件。未发布/不可见返回 404，权威当前分数不足返回 409 `InsufficientScoreForHint`。

玩家 Challenge 列表/详情绝不返回 FlagId、正确答案、Flag 数量、RandomOne 候选附件数量/文件名、内部 Runtime 配置或 ObjectKey。All 的列表返回 AttachmentId、显示名、MIME、字节数；RandomOne 首次单数下载请求完成原子抽取后直接 302/stream 被选文件，客户端不能选择，之后固定返回同一附件。

## Submission

```text
POST /api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/flag-submissions
POST /api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/patch-upload
POST /api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/fix-submissions
GET  /api/v1/competitions/{competitionId}/submissions/{submissionId}
GET  /api/v1/competitions/{competitionId}/submissions
```

Flag request：

```json
{ "flag": "flag{...}", "flags": null }
```

AWD 可二选一使用 flag 或 flags；CTF/AWDP Break 必须只用 flag。AWD flags 不限元素数、无尝试上限；任一项格式非法则整个请求 400、零写入。每项独立 Submission/ScoringEvent；数据库不保存 batch。单 flag 的 202 body 返回一个 SubmissionId/statusUrl；flags 的 202 body 返回保持输入顺序的 `submissions` 项数组，不返回 BatchId 或批次状态 URL。

Patch upload 是 multipart 单文件，成功返回 PatchUploadId；Fix trigger body 只含该 Id。上传不创建 Submission，触发才做尝试预检与消费。

## Runtime

```text
GET  /api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/runtime
POST /api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/runtime/start
POST /api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/runtime/stop
POST /api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/runtime/reset
POST /api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/runtime/extend
GET  /api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/targets
```

变更返回 202、RuntimeInstanceId 与 status URL。只有 Running 返回展开后的 `urls: string[]`。

CTF 开放四个动作；AWD 玩家 GET/Reset，Start/Stop 由平台生命周期控制且 Extend 不支持；KoH/AWDP 玩家调用这些路由返回 404。路由保留统一形状，但每个 Endpoint 的 Application policy 必须按 GameMode/RuntimeKind 拒绝不支持动作。

`targets` 只用于 AWD：加固期只返回本队 TeamId/Name/Running urls；加固期结束后返回所有 Approved、未 Ban、未删除队伍的 TeamId/Name 与其 Running `Exposure=Participants` urls，未 Running 的目标保留 Team 条目但 urls 为空。本队仍可从 Runtime GET 取得 OwnerOnly urls。排序 Team.RegisteredAt/TeamId。targets 永不返回 Flag、服务状态、Provider receipt 或内部地址；其他模式返回 404。

## Admin Competition

`/admin` 是管理资源命名空间，不代表只允许平台 Administrator；每个 Endpoint 按产品权限矩阵允许 Administrator 或该 Competition 的 Owner/Manager/Judge/Observer。写 Endpoint 再按动作收紧，Observer 永远只读，Judge 只执行判题/重判类写入。

```text
GET  /api/v1/admin/competitions
POST /api/v1/admin/competitions
GET  /api/v1/admin/competitions/{competitionId}
PUT  /api/v1/admin/competitions/{competitionId}
GET  /api/v1/admin/competitions/{competitionId}/configuration
PUT  /api/v1/admin/competitions/{competitionId}/configuration
DELETE /api/v1/admin/competitions/{competitionId}
POST /api/v1/admin/competitions/{competitionId}/restore
DELETE /api/v1/admin/competitions/{competitionId}/hard-delete
POST /api/v1/admin/competitions/{competitionId}/publish
POST /api/v1/admin/competitions/{competitionId}/make-visible
POST /api/v1/admin/competitions/{competitionId}/start
POST /api/v1/admin/competitions/{competitionId}/pause
POST /api/v1/admin/competitions/{competitionId}/resume
POST /api/v1/admin/competitions/{competitionId}/finish
GET  /api/v1/admin/competitions/{competitionId}/permissions
PUT  /api/v1/admin/competitions/{competitionId}/permissions
GET  /api/v1/admin/competitions/{competitionId}/permission-candidates
POST /api/v1/admin/competitions/{competitionId}/owner/transfer
GET  /api/v1/admin/competitions/{competitionId}/start-validation
POST /api/v1/admin/competitions/{competitionId}/flags/generate-missing
```

Lifecycle Endpoint 复用同一 Application state machine，但每个动作仍是独立文件/路由/TypedResults。

权限 snapshot 与候选用户只允许 Competition Owner 或平台 Administrator 读取。候选响应仅含
Id、UserName、Kind、Role 与 EmailVerified，不开放平台用户目录中的 Email、TokenVersion。
权限全量替换必须携带 `expectedPermissionRevision`；成功后专用 PermissionRevision 加一，
Owner transfer 也递增该 revision。Manager 必须是 Organizer/Administrator，Judge/Observer
必须完成邮箱验证；revision 或资格冲突返回 typed 409。

`start-validation` 是只读 GET，返回当前完整结构化错误数组。`flags/generate-missing` 在 Competition advisory lock 下同步只补 CTF PerTeam/KoH 缺失 Flag，成功返回 200 与 `failures` 数组；每项包含 CompetitionChallengeId、TeamId、稳定 code 和说明。响应不返回 Flag 原文、FlagId、生成/现有数量；空数组表示全部目标已满足。请求不处理 RandomOne 或 AWD Round。

## Admin Team

```text
GET  /api/v1/admin/competitions/{competitionId}/teams
POST /api/v1/admin/competitions/{competitionId}/teams/{teamId}/approve
POST /api/v1/admin/competitions/{competitionId}/teams/{teamId}/reject
POST /api/v1/admin/competitions/{competitionId}/teams/{teamId}/ban
POST /api/v1/admin/competitions/{competitionId}/teams/{teamId}/unban
```

## Global Challenge Bank

```text
GET  /api/v1/admin/challenges
POST /api/v1/admin/challenges
GET  /api/v1/admin/challenges/{challengeId}
PUT  /api/v1/admin/challenges/{challengeId}
DELETE /api/v1/admin/challenges/{challengeId}
POST /api/v1/admin/challenges/{challengeId}/restore
PUT  /api/v1/admin/challenges/{challengeId}/permissions
POST /api/v1/admin/challenges/{challengeId}/owner/transfer
GET  /api/v1/admin/challenges/{challengeId}/attachments
POST /api/v1/admin/challenges/{challengeId}/attachments
PUT  /api/v1/admin/challenges/{challengeId}/attachments/{attachmentId}
DELETE /api/v1/admin/challenges/{challengeId}/attachments/{attachmentId}
POST /api/v1/admin/challenges/{challengeId}/attachments/{attachmentId}/restore
GET  /api/v1/admin/challenges/{challengeId}/flags
POST /api/v1/admin/challenges/{challengeId}/flags
GET  /api/v1/admin/challenges/{challengeId}/flags/{flagId}
PUT  /api/v1/admin/challenges/{challengeId}/flags/{flagId}
DELETE /api/v1/admin/challenges/{challengeId}/flags/{flagId}
POST /api/v1/admin/challenges/{challengeId}/flags/{flagId}/restore
```

## Admin Runtime

```text
GET  /api/v1/admin/competitions/{competitionId}/runtimes
GET  /api/v1/admin/competitions/{competitionId}/runtimes/{runtimeInstanceId}
POST /api/v1/admin/competitions/{competitionId}/teams/{teamId}/challenges/{competitionChallengeId}/runtime/start
POST /api/v1/admin/competitions/{competitionId}/teams/{teamId}/challenges/{competitionChallengeId}/runtime/stop
POST /api/v1/admin/competitions/{competitionId}/teams/{teamId}/challenges/{competitionChallengeId}/runtime/reset
POST /api/v1/admin/competitions/{competitionId}/teams/{teamId}/challenges/{competitionChallengeId}/runtime/extend
POST /api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/runtime/start
POST /api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/runtime/stop
POST /api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/runtime/reset
```

列表按 CreatedAt desc/Id desc keyset，可筛选 CompetitionChallengeId、TeamId、RuntimeKind、Provider、RunnerPool、RunnerId、State、ExpiresBefore。Manager 可执行动作；Judge/Observer 只读；ProviderReceipt/内部错误只在管理详情返回。带 Team 的动作服务 CTF/AWD，复用玩家 Runtime 状态机，不提供绕过额度、状态或 Generation 栅栏的“强制成功”。不带 Team 的三个动作只服务 KoH shared Runtime，不伪造 TeamId；KoH 没有 Extend。

## CompetitionChallenge Management

```text
GET  /api/v1/admin/competitions/{competitionId}/challenges
POST /api/v1/admin/competitions/{competitionId}/challenges
GET  /api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}
PUT  /api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}
GET  /api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/configuration
PUT  /api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/configuration
DELETE /api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}
POST /api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/restore
GET  /api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/hints
POST /api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/hints
GET  /api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/hints/{hintId}
PUT  /api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/hints/{hintId}
DELETE /api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/hints/{hintId}
POST /api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/hints/{hintId}/restore
GET  /api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/flags
POST /api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/flags
GET  /api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/flags/{flagId}
PUT  /api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/flags/{flagId}
DELETE /api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/flags/{flagId}
POST /api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/flags/{flagId}/restore
```

CompetitionChallenge create/update/delete/restore 的并发冲突统一返回强类型
`CompetitionChallengeConflictResponse`。update body 必须包含完整的 BaseScore、Order、
IsPublished 与非负 expectedRevision；delete/restore 必须通过 required query
`expectedRevision` 携带当前聚合 revision。成功的 delete/restore 各递增一次 revision；
陈旧 revision 返回 `RevisionConflict`，当前 revision 但删除状态方向错误返回
`LifecycleStateConflict`，不自动合并或静默覆盖。restore 还会在同一事务内重验活动 Challenge
template、Competition mode 与活动唯一约束。稳定 conflict code 为 `ResourceIdConflict`、
`ChallengeOrderConflict`、`ChallengeTemplateConflict`、`RevisionConflict`、
`LifecycleStateConflict`、`ChallengeTemplateNotFound` 和 `ChallengeTemplateModeMismatch`。
Hint 没有独立 revision，但其增删恢复与 CompetitionChallenge 管理写共享 Competition
transaction lock，并在同一事务内递增父聚合 revision 与 leaderboard revision。

## Admin Submission 与判定

```text
GET  /api/v1/admin/competitions/{competitionId}/submissions
GET  /api/v1/admin/competitions/{competitionId}/submissions/{submissionId}
POST /api/v1/admin/competitions/{competitionId}/submissions/queue-evaluation
POST /api/v1/admin/competitions/{competitionId}/submissions/rejudge
POST /api/v1/admin/competitions/{competitionId}/submissions/{submissionId}/rejudge
```

列表筛选：competitionChallengeId、teamId、userId、submissionKind、evaluationState、scoringResult、failureCode、receivedFrom/To、submittedFlag 精确匹配、hasCurrentScoringEvent。failureCode 对 Completed 匹配当前 ScoringEvent.FailureCode，对 PlatformFailed 匹配 Submission.EvaluationFailureCode。顺序 ReceivedAt desc/Id desc，limit 50..200，不算 total。

queue-evaluation 用于 ManualBatch 的 Pending/无旧事件 PlatformFailed 集合；rejudge 集合只允许有当前事件的 Flag/Break 按题筛选。两者请求都必须指定 CompetitionChallengeId，接入事务只写带筛选 cutoff 的 durable drain message，Worker 再以 500 条 SKIP LOCKED 短事务更新实体状态/ProcessingVersion 与逐项 Outbox。单 Submission rejudge 用于任意管理员重判以及 AWDP Fix/Patch 精确重判。不建 Batch/Rejudge 实体。

## Notifications

```text
GET /api/v1/notifications
GET /api/v1/notifications/feed
```

仅返回当前用户永久事件流，按 CreatedAt desc/Id desc keyset 分页。没有读取、标记已读、删除或计数 Endpoint。

## Platform Administration

以下只允许 `UserRole.Administrator`，Competition Owner/Manager/Judge/Observer 不因比赛权限获得访问：

```text
GET  /api/v1/admin/platform/users
GET  /api/v1/admin/platform/users/{userId}
GET  /api/v1/admin/platform/users/{userId}/deletion-preview
DELETE /api/v1/admin/platform/users/{userId}
POST /api/v1/admin/platform/bots
POST /api/v1/admin/platform/bots/{userId}/tokens
PUT  /api/v1/admin/platform/users/{userId}/role
POST /api/v1/admin/platform/users/{userId}/tokens/invalidate
GET  /api/v1/admin/platform/dead-letters
GET  /api/v1/admin/platform/dead-letters/{messageId}
POST /api/v1/admin/platform/dead-letters/{messageId}/requeue
GET  /api/v1/admin/platform/email-verification/configuration
PUT  /api/v1/admin/platform/email-verification/configuration
PUT  /api/v1/admin/platform/email-verification/password
POST /api/v1/admin/platform/email-verification/test
```

Bot 创建请求只包含 UserName 和固定的 `UserRole.Organizer`，不能创建 User 或 Administrator
Bot，也不接受 Email 或 Password。服务端生成不可用的非空 dummy Email 和 PasswordHash。
Token 签发请求包含正数 ExpiresInSeconds，只接受 Bot 身份，返回一次普通 AccessToken 与
ExpiresAt，不签发 Refresh Token。Role 更新与 token invalidate 原子递增 User.TokenVersion。
Dead Letter DTO 隐去 JWT、Flag/Submission 原文和 archive 内容；requeue 创建新的 durable
delivery attempt 并保留 Wolverine 原失败记录，不直接调用 Handler。比赛管理者不能操作
DLQ，只能从 Competition/Submission/Runtime 领域 API 重新触发。

删除用户前必须读取影响预览。没有任何业务引用时可物理删除；存在比赛、题目、队伍、
提交、计分、通知或生命周期审计引用时，只允许不可逆匿名化并停用。匿名化会清除个人
资料、认证信息和资源协作权限，使所有现有 Token 失效，但保留所有权、队伍成员关系和
历史事实。两种删除方式都要求管理员原因，并记录独立账号生命周期审计，不级联删除业务数据。

## Internal v1

```text
POST /api/internal/v1/awd/check-results
POST /api/internal/v1/awdp/fix-results
GET  /api/internal/v1/awdp/fix-archives/{submissionId}
```

全部使用独立 JWT Scheme、精确 audience/permission 与资源 Claims。Request body 不能包含可覆盖 Claims 的 Competition/Team/Submission Id。JWT 由调度该 durable Job 的可信进程签发，不提供公开“任意换 Token”接口。

- AWD callback permission=`awd:check-result:write`，绑定 RuntimeInstanceId、Generation、
  checker sequence、runtime processing version 与 deadline；请求只提交 typed checker
  status。sequence/version 与 Runtime 当前值精确匹配的同一次执行可以多次写，后一次覆盖
  前一次。
- AWDP callback permission=`awdp:fix-result:write`，绑定 SubmissionId、ProcessingVersion、deadline。
- Archive permission=`awdp:fix-archive:read`，只绑定一个 SubmissionId；Runner 使用它读取 archive，Checker callback Token 不含此权限。S3 可返回短时预签名地址，LocalFileSystem 可流式返回。

callback 成功且当前时返回 200。AWD 当前 sequence/version 下的重复或后续 typed status
更新均返回 200；过时 sequence/version 返回 202 superseded，任一未来 fence 返回 409。
AWDP 完全相同重放返回相同 200，同版本不同规范化 body 返回 409。Token/claim 不符返回
401/403；资源对该 Token 不存在返回 404。过时结果可写结构化日志，但不建审计业务表。

Runner 自身通过 Wolverine 读写，不需要 HTTP callback Endpoint。

## 通用成功契约

- 创建普通资源返回 201+资源 DTO+Location；异步 Submission/Runtime 返回 202+资源 Id+`statusUrl`；同步读取/更新返回 200；无 body 删除返回 204。无 Batch/Rejudge 实体的 queue-evaluation/集合 rejudge 例外返回 202+规范化筛选/cutoff，状态通过 Submission 列表查询。
- 202 不是判定成功。客户端轮询 statusUrl 或接收 SignalR invalidation，最终仍以 GET 资源为事实源。
- 所有写入必须在返回前完成接入事务与 Outbox commit；commit 失败不得返回资源 Id。
- 资源 revision 写统一使用 expectedRevision；状态机动作不复用 revision 作为幂等键，而是在事务内校验当前状态。
- 每个具体 Endpoint 的 `Results<T...>` 只能声明其真实分支；不能为了省事统一声明 200/400/401/403/404/409/500 全家桶。
