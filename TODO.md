# NoCTF 比赛能力补全 TODO

> 目标：在 EF Core + 原生 Channel + Redis-only leaderboard 基础上，补齐完整的比赛生命周期、报名、题目管理、五种 GameMode、Runner/runtime、排行榜和测试闭环。
>
> 架构约束：PostgreSQL + EF Core 是唯一业务事实来源；Submission 只保存输入事实；ScoringEvent 只保存无分数判断事实；Score/ScoreDelta/rank 只存在内存 projector 和 Redis；API 单副本；不恢复 Marten、Wolverine、Rebus、Submission Stream、Scoring Stream、Outbox 或 HPA。

## 0. 当前基线

- [x] EF Core Submission、ScoringEvent、ChallengeFlag、FixSubmissionRecord 基础模型。
- [x] `Submission.ScoringEventId` 当前事件绑定。
- [x] `ScoringEvent.SubmissionId` 可空反向关系。
- [x] ScoringEvent soft-delete 和 filtered SourceKey unique index。
- [x] 原生 bounded Channel submission processing 基础。
- [x] Redis leaderboard cache 和 miss → `202 Processing`。
- [x] Retry 基础流程：软删除旧 event、清空 binding、重新入队。
- [x] EF score-free evaluator contract。
- [x] 五种 GameMode projector seam 和基础稳定排序测试。
- [x] 用户 Access JWT Bearer-only 认证。
- [x] Refresh opaque token + HttpOnly Cookie rotation。
- [x] Runner scoring JWT issuer、独立 scheme 和 system event endpoint。
- [x] Runner/Worker/Marten/Wolverine 基础残留删除。
- [x] 基础 solution build、TUnit、frontend build、Compose config 验证。

## 1. Competition 生命周期和状态机

### 1.1 状态不变量

- [x] 定义并测试唯一状态图：`Draft -> Published -> Running -> Paused -> Running -> Finished`。
- [x] 禁止 Finished 重新进入其他状态。
- [x] 禁止 Draft、Published、Paused、Finished 接受 Submission。
- [x] 只有 Running 接受普通 Flag/Fix Submission。
- [x] 明确 `StartTime < EndTime` 校验。
- [x] 明确 StartTime/EndTime 边界为服务器 `ReceivedAt` 判断。
- [ ] 规定 Running 后哪些字段不可修改。
- [ ] 规定 Finished 后只允许查询和审计。
- [x] 为状态转换建立 Application policy，不让 endpoint 自行判断。

### 1.2 Lifecycle use case

- [x] 完善 `ICompetitionLifecyclePolicy`。
- [x] 实现 Publish、Pause、Resume、Finish use case。
- [x] 每个状态转换使用 expected-status 条件更新，保证并发幂等。
- [x] 状态转换记录 actor、时间和原因审计信息。
- [x] 状态变化统一 invalidate leaderboard 并 enqueue refresh。
- [x] Finished 状态触发 runtime cleanup work item。
- [ ] 状态变化发布脱敏 SignalR competition notification。

### 1.3 Lifecycle hosted service

- [x] 新增 `CompetitionLifecycleHostedService`。
- [x] 配置扫描间隔，默认 5 秒。
- [x] Published 到 StartTime 自动转 Running。
- [x] Published/Running/Paused 到 EndTime 自动转 Finished。
- [ ] 加入 shutdown cancellation 和有限 drain。
- [ ] 记录 structured log：CompetitionId、from、to、耗时、结果。
- [ ] 不记录 Flag、SourceKey、archive metadata。

### 1.4 Lifecycle API

- [x] `POST /admin/competitions/{id}/publish`。
- [x] `POST /admin/competitions/{id}/pause`。
- [x] `POST /admin/competitions/{id}/resume`。
- [x] `POST /admin/competitions/{id}/finish`。
- [x] 全部 endpoint 使用 typed FastEndpoints `ExecuteAsync`。
- [x] 全部 endpoint 使用 Bearer JWT + collaborator policy。
- [x] 状态冲突返回 typed `409 Problem`。
- [ ] 补 OpenAPI summary、authorization 和 response schema。

## 2. Competition CRUD 和配置

### 2.1 Competition CRUD

- [x] 实现 `CreateCompetition`。
- [x] 实现 `GetCompetition`。
- [x] 实现 `ListCompetitions`。
- [x] 实现 `UpdateCompetition`。
- [x] 实现 `DeleteCompetition` soft-delete。
- [x] 创建时强制 OwnerId 使用认证用户。
- [x] 创建时状态固定为 Draft。
- [x] GameMode 创建后 immutable。
- [x] MaxTeamMembers 必须大于 0。
- [x] 删除时禁止级联删除 Submission/ScoringEvent。
- [x] 补公开 competition DTO 与管理员 DTO。

### 2.2 Competition API

- [x] `POST /admin/competitions`。
- [x] `GET /competitions`。
- [x] `GET /competitions/{id}`。
- [x] `PUT /admin/competitions/{id}`。
- [x] `DELETE /admin/competitions/{id}`。
- [x] 公开 DTO 不包含私有配置、Flag、Runner secret、Fix metadata。
- [x] 使用 Mapperly 完成 command/response mapping。

### 2.3 CompetitionConfiguration

- [x] 配置 entity 与 Competition 建立唯一关系。
- [x] 实现 mode-specific configuration parser。
- [x] 实现 schemaVersion 校验和 upgrader。
- [x] 实现 `Revision` optimistic concurrency。
- [x] 更新配置必须传 expected revision。
- [x] revision 冲突返回 `409`。
- [ ] Running 状态只允许非破坏性配置修改。
- [x] Finished 状态拒绝配置修改。
- [x] 配置修改后 invalidate + rebuild。
- [x] 为 CTF/AWD/AWDP/KoH/Penetration 建立 validator catalog。

## 3. Team 报名和成员管理

### 3.1 Team use case

- [x] `CreateTeam`。
- [x] `GetTeam`。
- [x] `ListCompetitionTeams`。
- [x] `UpdateTeam`。
- [x] `DeleteTeam`。
- [x] `RegisterTeamToCompetition`。
- [x] `ApproveTeam`。
- [x] `RejectTeam`。
- [x] `InviteTeamMember`。
- [x] `AcceptTeamInvitation`。
- [x] `RejectTeamInvitation`。
- [x] `RemoveTeamMember`。
- [x] `LeaveTeam`。
- [x] `TransferTeamCaptain`。

### 3.2 Team 规则

- [x] 同一 Competition 内 Team name unique。
- [x] 创建者自动成为 Captain。
- [x] MaxTeamMembers 强制执行。
- [x] 自动批准和人工批准两条路径都可用。
- [x] Running 后默认禁止新报名。
- [x] Finished 后禁止成员变更。
- [x] 一个 User 在同一 Competition 只能属于一个 Team。
- [x] Team ban 后禁止 Submission。
- [x] Team soft-delete 后不进入 leaderboard，但保留历史事实。
- [x] ban/unban/delete 统一触发 leaderboard invalidate + rebuild。

### 3.3 Collaborator

- [x] `GET /admin/competitions/{id}/collaborators`。
- [x] `POST /admin/competitions/{id}/collaborators`。
- [x] `DELETE /admin/competitions/{id}/collaborators/{userId}`。
- [ ] 实现 Owner、Manager、Judge、Observer 权限矩阵。
- [ ] Judge 可 retry/rebuild，但不能修改 Competition owner/config secret。
- [ ] Observer 只能读取管理员视图。

## 4. Challenge、Flag 和题目配置

### 4.1 Challenge CRUD

- [x] 创建 Challenge。
- [x] 查询公开 Challenge 列表。
- [x] 查询管理员 Challenge 详情。
- [x] 更新 Challenge。
- [x] 删除 Challenge soft-delete。
- [x] 调整 Challenge Order。
- [x] Publish/Unpublish Challenge。
- [x] `(CompetitionId, Order)` 冲突返回 typed problem。
- [x] 未发布 Challenge 不进入公开 API 和 Submission admission。
- [x] Running 后禁止修改影响 evaluator 的字段。

### 4.2 ChallengeConfiguration

- [x] `GET /admin/competitions/{competitionId}/challenges/{challengeId}/configuration`。
- [x] `PUT /admin/competitions/{competitionId}/challenges/{challengeId}/configuration`。
- [x] 按 Competition.Mode 选择 configuration parser。
- [x] schemaVersion 校验。
- [x] revision concurrency。
- [x] 配置修改后 enqueue rebuild。
- [x] 配置修改后只刷新受影响 Competition 的 leaderboard。

### 4.3 ChallengeFlag

- [x] 创建 Flag。
- [x] 查询管理员 Flag。
- [x] 更新 Flag。
- [x] 删除/失效 Flag。
- [x] 支持 global Flag 和 Team-specific Flag。
- [x] 校验 ValidStart/ValidEnd。
- [x] 阻止同一范围的有效时间窗口冲突。
- [x] Flag 更新使用 RowVersion。
- [x] Flag 更新触发 rebuild。
- [x] Flag 绝不进入日志、Redis、SignalR、Channel payload、公开 DTO。

## 5. Submission admission、Idempotency 和 MaxAttempt

- [x] `SubmissionAdmissionSnapshot` 增加 Competition.Mode。
- [x] 增加 Competition/Challenge configuration revision。
- [x] 增加 SubmissionKind 支持矩阵。
- [x] 增加 MaxFlagAttempts/MaxFixAttempts。
- [x] 增加当前 accepted attempts 快速计数。
- [x] 修复 Draft/Published/Finished 状态误接受问题。
- [x] Flag/Fix command 增加 IdempotencyKey。
- [x] 建立 `(CompetitionId, IdempotencyKey)` unique 行为。
- [x] 相同 key 重试返回原 Submission。
- [x] 相同 key 不允许绑定不同 User/Team/Challenge。
- [x] Intake transaction 内执行快速 MaxAttempt 检查。
- [x] Processor Serializable transaction 内再次执行最终 MaxAttempt 检查。
- [x] Flag/Fix attempt 是否消耗由 GameMode policy 决定。
- [x] 正确结果普通 retry 拒绝。
- [x] `AttemptsExhausted` 生成 score-free ScoringEvent。
- [ ] 并发提交测试保证不超过 MaxAttempt。

## 6. Fix submission 完整状态机

- [x] `Created -> Claimed -> Verifying -> Valid/TeamFailure/PlatformFailed` 状态机。
- [x] Upload expiry 检查。
- [x] Upload claim transaction。
- [x] archive path 安全校验。
- [x] archive metadata 读取和 hash 校验。
- [x] verifier timeout。
- [x] verifier failure category 映射。
- [ ] runner failure 不写入 Flag 或 archive 内容日志。
- [x] Fix retry 的旧 event soft-delete 和 record 状态重置规则。
- [ ] 原始 runtime 恢复逻辑。
- [x] CleanupPending 扫描和清理。

## 7. 五种 GameMode evaluator

### 7.1 CTF

- [x] CTF Flag evaluator。
- [x] Duplicate solve。
- [x] First Blood。
- [x] Dynamic score 参数解析。
- [x] minimum/initial/decay 规则。
- [x] solve count 和 solve time 排序。
- [x] Penetration stage challenge 支持。
- [x] stage completion 不等同于整个 Challenge completion。

### 7.2 AWD

- [x] victim/subject/service/stage 维度。
- [x] self-attack rejection。
- [x] duplicate attack rejection。
- [ ] round flag expiry。
- [ ] current round 校验。
- [x] attack points。
- [x] service online/down 规则。
- [x] been-attacked penalty。

### 7.3 AWDP

- [x] Break evaluator。
- [x] Fix evaluator。
- [x] BreakRequired。
- [x] Break/Fix attempt limits。
- [x] BreakSuccess 后禁止普通 Break。
- [x] FixSuccess 后禁止普通 Fix。
- [x] Fix archive verification mapping。
- [ ] Check exit code mapping。
- [x] round settlement。
- [ ] attack/defense/violation/service penalty 配置。
- [x] Break 只来自 Correct Flag event。
- [x] Fix 只来自 Correct Fix event。
- [x] Checkdown 只使用独立 system event。

### 7.4 KoH

- [x] 不接受普通 Flag Submission。
- [ ] Agent observation parser。
- [ ] Team identifier 映射。
- [ ] unknown/invalid identifier 处理。
- [ ] polling timeout。
- [ ] control transition。
- [x] control interval score。
- [x] banned/deleted Team 过滤。

### 7.5 Penetration

- [x] Stage dependency evaluator。
- [ ] Stage flag 生成和窗口。
- [x] Stage submission evaluator。
- [x] stage completion。
- [ ] runtime stage lifecycle。
- [ ] stage failure/platform failure。
- [ ] 全部 stage 完成判定。
- [x] stage-specific leaderboard projection。

## 8. Round engine 和 system producer

- [ ] `AwdRoundHostedService`。
- [ ] `AwdFlagRotationHostedService`。
- [ ] `AwdCheckerHostedService`。
- [ ] `AwdRoundSettlementHostedService`。
- [ ] `AwdpRoundHostedService`。
- [ ] `AwdpSettlementHostedService`。
- [ ] `KohPollingHostedService`。
- [ ] Penetration stage monitor。
- [ ] producer 使用 Runner scoring JWT。
- [ ] producer 使用 stable SourceKey。
- [ ] producer timeout/retry。
- [ ] producer 不记录 Flag、archive URL 或 secret。
- [ ] Competition Paused 时暂停 producer。
- [ ] Competition Finished 时停止 producer。
- [ ] 失败 producer 生成 PlatformFailed 或明确延迟策略。

## 9. Runner 和 runtime lifecycle

- [ ] `CompetitionRuntimeProvisioner`。
- [x] `ChallengeRuntimeProvisioner`。
- [x] `CompetitionRuntimeCleaner`。
- [ ] `ChallengeRuntimeHealthChecker`。
- [x] runtime operation idempotency key。
- [x] runtime operation timeout。
- [ ] Docker runtime 接线。
- [ ] Kubernetes runtime 接线。
- [ ] AWD game box provision。
- [ ] AWDP patch/recreate/check 流程。
- [ ] KoH agent provision/poll。
- [ ] Penetration stage instance provision。
- [ ] orphan runtime cleanup。
- [ ] competition finish cleanup。
- [x] runtime failure → ScoringEvent/PlatformFailed。

## 10. Maintenance、rebuild 和 lifecycle Channel

- [x] 完成 Maintenance hosted service。
- [x] CompetitionId rebuild 去重。
- [x] stable Submission order：`ReceivedAt + SubmissionId`。
- [x] stable system fact order：`OccurredAt + ScoringEventId`。
- [x] old current event soft-delete。
- [x] replacement event rebind。
- [x] rebuild success/failure/cancel 释放去重标记。
- [x] rebuild 最后只触发一次 leaderboard refresh。
- [ ] lifecycle work item 接入 Maintenance/专用 channel。
- [x] runtime cleanup work item 接入 Maintenance channel。
- [x] Processing/Projection/Maintenance 统一 1/5/15 秒 retry。
- [x] consumer scope、耗时和结果 structured logging。
- [ ] shutdown drain。
- [ ] 记录已知限制：Channel 不跨进程、不持久化、不恢复崩溃窗口。

## 11. Leaderboard 完整投影

- [ ] 删除 Correct count 临时 projector。
- [x] CTF projector。
- [x] AWD projector。
- [x] AWDP projector。
- [x] KoH projector。
- [x] Penetration projector。
- [x] Competition/Challenge configuration 进入 projector input。
- [x] Team ban/soft-delete 过滤。
- [x] Challenge soft-delete 过滤。
- [x] system event `OccurredAt + Id` 排序。
- [x] Submission `ReceivedAt + Id` 排序。
- [ ] First Blood summary。
- [ ] Subject/slot summary。
- [ ] Slot kind。
- [x] score/rank 只在内存 DTO 和 Redis。
- [x] Redis TTL 配置化。
- [ ] Redis miss → 202 → refresh → hit。
- [ ] Redis 未配置的正式环境启动失败。
- [x] leaderboard visibility 按 Draft/Published/Running/Paused/Finished 处理。
- [x] refresh 成功广播 CompetitionId 和 GeneratedAt。

## 12. 管理 API 和公开 API

### 12.1 公开 API

- [ ] `GET /competitions`。
- [ ] `GET /competitions/{id}`。
- [x] `GET /competitions/{id}/challenges`。
- [ ] `GET /competitions/{id}/teams/me`。
- [ ] `GET /competitions/{id}/leaderboard`。
- [ ] `GET /competitions/{id}/submissions/{submissionId}`。

### 12.2 管理 API

- [ ] Competition CRUD。
- [ ] lifecycle publish/pause/resume/finish。
- [x] Challenge CRUD/publish。
- [x] Flag CRUD/window update。
- [x] Competition/Challenge configuration update。
- [ ] Team approval/member management。
- [ ] Collaborator management。
- [x] Admin detailed submission status。
- [x] Admin retry。
- [x] Admin rebuild。
- [ ] system event internal endpoint。
- [x] 全部 endpoint 使用 typed `ExecuteAsync`。
- [x] 全部 endpoint 使用 `Results<T...>`/`TypedResults`。
- [x] 全部 request/response 使用 DTO。
- [x] 全部 structural mapping 使用 Mapperly。
- [ ] endpoint 不直接访问 EF、Redis 或 Channel。

## 13. JWT、Cookie、SignalR 和权限

- [x] 用户 Access JWT Bearer-only。
- [x] Refresh opaque token HttpOnly Cookie。
- [x] Refresh rotation/replay family。
- [x] Logout。
- [x] Runner scoring JWT issuer。
- [x] Runner scoring scheme、issuer、audience、scope 校验。
- [x] 用户 token_version 即时吊销 policy。
- [ ] Refresh Origin/Referer 与部署域名配置统一。
- [ ] SignalR Bearer token provider。
- [ ] 仅 Hub path 允许 access_token query。
- [ ] query token 不进入日志和代理访问日志。
- [ ] User JWT 不能访问 system endpoint。
- [ ] Runner JWT 不能访问普通用户/管理员 endpoint。
- [ ] Owner/Manager/Judge/Observer policy matrix。

## 14. EF、migration 和索引

- [ ] Competition status/time index。
- [ ] Competition soft-delete filter/index。
- [ ] Team/TeamMember unique indexes。
- [ ] Challenge order index。
- [ ] ChallengeFlag time-window index。
- [ ] Submission idempotency index。
- [ ] Submission stable ordering index。
- [ ] ScoringEvent current/source filtered unique indexes。
- [ ] RefreshSession token hash unique index。
- [x] runtime operation idempotency index。
- [ ] 所有 schema 变更只通过 `dotnet ef migrations add`。
- [ ] 空 PostgreSQL 执行唯一 baseline。
- [ ] `dotnet ef migrations has-pending-model-changes` 通过。
- [ ] 禁止手改 migration/snapshot。

## 15. 测试

### 15.1 Unit

- [ ] lifecycle state machine。
- [ ] admission status boundary。
- [ ] MaxAttempt。
- [ ] Submission idempotency。
- [ ] Team membership。
- [ ] collaborator authorization。
- [ ] Challenge publish/Flag window。
- [ ] configuration revision。
- [ ] 五种 evaluator。
- [ ] 五种 projector。
- [ ] system SourceKey dedupe。
- [ ] retry/rebuild ordering。
- [x] runtime failure mapping。
- [ ] Channel capacity/cancel/retry/dedup。
- [ ] JWT/Runner JWT policy。
- [ ] Mapperly mapping。
- [ ] Flag/archive sensitive data redaction。

### 15.2 Testcontainers PostgreSQL

- [ ] 空库 baseline migration。
- [ ] Competition CRUD。
- [ ] lifecycle concurrent transition。
- [ ] Team registration race。
- [ ] Challenge/Flag constraints。
- [ ] MaxAttempt race。
- [ ] Submission → current ScoringEvent。
- [ ] Retry/rebuild replacement。
- [ ] configuration revision conflict。
- [ ] refresh rotation concurrency。
- [ ] soft-delete query filter。

### 15.3 Redis/SignalR/API

- [ ] leaderboard miss → 202 → refresh → hit。
- [ ] invalidate/rebuild。
- [ ] banned Team removal。
- [ ] config/Flag change refresh。
- [ ] Submission status notification。
- [ ] lifecycle notification。
- [ ] leaderboard notification。
- [ ] Login/Refresh/Logout Cookie behavior。
- [ ] User JWT/Runner JWT scheme isolation。
- [ ] system event duplicate request。
- [ ] 不泄露 Flag、SourceKey、archive metadata。

### 15.4 Architecture

- [ ] 禁止 Marten/Wolverine/Rebus。
- [ ] 禁止 stream/checkpoint/snapshot store。
- [ ] 禁止 Score/ScoreDelta 持久化。
- [ ] 禁止 Worker/HPA/multi-replica backend。
- [ ] Application/GameModes 不直接引用 EF/Redis/Channel。
- [ ] endpoint 不使用 HandleAsync/IResult/object/Request.Body。
- [ ] endpoint 不手写 structural mapping。
- [ ] 禁止手工 migration/snapshot。
- [ ] Flag 不进入日志、Redis、SignalR、公开 DTO、Channel payload。

## 16. 部署、OpenAPI 和文档

- [ ] 统一配置键：`ConnectionStrings__PostgreSql`。
- [ ] 统一配置键：`Authentication__*`。
- [ ] 统一配置键：`RunnerScoring__*`。
- [ ] 统一配置键：`Storage__*`。
- [ ] 删除 `DefaultConnection`、`JwtSettings`、`StorageProvider` 残留。
- [x] 删除 Kubernetes worker/HPA/network policy 残留。
- [x] backend Deployment 固定 `replicas: 1`。
- [x] Compose 只运行单 backend。
- [ ] Runner 镜像保留 Docker CLI/Compose plugin。
- [x] OpenAPI 删除 LeaderboardSnapshot/旧 outcome schema。
- [x] OpenAPI 增加 Login/Refresh/Logout/Runner scoring security scheme。
- [ ] 重新生成 frontend API client。
- [ ] 更新 architecture、deployment、game-modes、handoff 文档。
- [ ] 文档明确 Channel 丢失窗口和单副本限制。

## 17. 最终验收

- [ ] `dotnet restore backend/NoCTF.slnx`。
- [ ] `dotnet build backend/NoCTF.slnx --configuration Release --no-restore`。
- [ ] 完整 TUnit suite 通过。
- [ ] Testcontainers PostgreSQL/Redis suite 通过。
- [ ] `dotnet ef database update` 空库通过。
- [ ] `dotnet ef migrations has-pending-model-changes` 通过。
- [x] OpenAPI export 通过。
- [ ] frontend `bun run generate-api` 通过。
- [ ] frontend `bun run build` 通过。
- [ ] `docker compose config` 通过。
- [ ] API/Runner Docker image build 通过。
- [ ] Kubernetes kubeconform 通过。
- [ ] 旧架构残留扫描通过。
- [ ] 完成一次 API 端到端比赛流程：
  - [ ] 创建 Draft Competition。
  - [ ] 配置五种模式之一。
  - [ ] 发布并自动进入 Running。
  - [ ] 创建/审核 Team。
  - [ ] 发布 Challenge 和 Flag。
  - [ ] 提交 Flag/Fix。
  - [ ] 产生 current ScoringEvent。
  - [ ] Redis leaderboard 异步刷新。
  - [ ] ban/unban 后 rebuild。
  - [ ] Competition 自动 Finished。
  - [ ] runtime cleanup 完成。

## 18. 暂不实现的能力

- [ ] 本轮不引入 EF Outbox。
- [ ] 本轮不实现跨进程 Channel dispatch。
- [ ] 本轮不实现崩溃恢复扫描。
- [ ] 本轮不启用 backend HPA。
- [ ] 本轮不实现 durable dead-letter queue。
- [ ] 本轮不为 system ScoringEvent 另建 raw input 表；system event 是最终事实。
- [ ] 后续如需多副本或可靠恢复，单独设计 durable dispatch migration。
