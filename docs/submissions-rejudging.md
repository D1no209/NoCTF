# Submission、判定与重判

## 类型与接入

SubmissionKind：`Flag`（CTF/AWD）、`Break`（AWDP）、`Fix`（AWDP）。KoH 不接收 Submission。

创建前同步校验：

1. Access JWT、User/Team；
2. Team Approved、未 Ban/删除；
3. Competition=Running；
4. CompetitionChallenge 属于比赛、已发布、未删除；
5. Mode 支持该 Kind；
6. 请求/Flag 格式；
7. Redis 限流；
8. 非 AWD 的尝试次数快速预检。

上述失败返回 4xx/429 且零 Submission。通过后每次请求都是新尝试，不支持 Idempotency-Key。

Flag 统一规则：1..4096 UTF-8 bytes、禁止 NUL、不 Trim、不 Unicode normalize、ordinal 区分大小写。客户端原文保存到 Submission，并保存普通 SHA-256 做精确索引。

## 结果与失败原因

SubmissionEvaluation 只使用以下 Result：

| Result | 含义 |
|---|---|
| Correct | 本次判定正确；是否形成一次计分成就由模式投影决定 |
| Wrong | 已完整判定但答案/服务结果错误 |
| Duplicate | 答案正确但命中该模式定义的重复维度 |
| AttemptsExhausted | 接入后 Worker 二次验证发现它不在允许次数内 |
| Rejected | 输入已接入，但命中 SelfAttack/规则违规等业务拒绝 |

Submission 的平台故障不创建 `ScoringResult.PlatformFailed` 事件：EvaluationState 置 PlatformFailed，原因存 Submission.EvaluationFailureCode；重判时旧事件继续有效。各模式的稳定 FailureCode 包括 `FlagNotMatched`、`FlagExpired`、`AmbiguousFlagMatch`、`DuplicateSolve`、`DuplicateAttack`、`SelfAttackRejected`、`BreakRequired`、`FixArchiveInvalid`、`FixArchiveLimitExceeded`、`AwdpFixFailed`、`AwdpPatchFailed`、`AwdpPatchTimeout`、`AwdpViolation`、`AwdpServiceDown`、`StorageUnavailable`、`RuntimeUnavailable`、`ProducerTimeout`、`ProducerUnavailable` 与 `CheckerInvalidResponse`。没有更具体原因时 Wrong 使用 FlagNotMatched；FailureCode 不能存自由文本。

## AWD 批量

只有 AWD 可提交 `flags` 数组；`flag` 与 `flags` 必须且只能有一个。数组至少一项，不限制项数，也不设应用请求体上限。任一项无效则整个请求 400/零写入；相同字符串重复出现也创建多个独立 Submission。一次 HTTP 请求按一个限流配额，数据库不保存 batch id/index。

## 异步状态

```text
Pending -> Queued -> Processing -> Completed
                              \-> PlatformFailed
PlatformFailed -> Queued       (首次失败重试或重判)
Completed -> Queued            (重判)
```

ManualBatch 接入后为 Pending；管理员筛选后 Queued。Automatic 接入即在事务中 Queued+Outbox。Worker claim 时设置 Processing；使用 ProcessingVersion 防止旧消息写回。

单项接入返回 202，包含 SubmissionId、EvaluationState、status URL。AWD `flags` 返回同一结构的 `submissions` 数组，严格保持输入顺序；没有顶层 BatchId/status。SignalR 只提示状态变化，REST 是事实源。

## 尝试次数

配置：

- CTF `MaxFlagSubmissions`；
- AWDP `MaxBreakSubmissions`、`MaxFixSubmissions`；
- 值 <=0 表示无限；AWD 完全没有最大次数。

API 在 `(TeamId, CompetitionChallengeId, Kind)` advisory lock/事务中预检。Pending/Queued/Processing 立即预占一次，防并发绕过。Worker 在判定前按 ReceivedAt/Id 二次验证；并发或配置下降导致超限时为 AttemptsExhausted，不执行 Checker。

会消耗：

- CTF Flag 与 AWDP Break：所有已接收业务结果，包括 Wrong/Duplicate；PlatformFailed 不消耗。
- AWDP Fix：Correct、StillVulnerable、RuleViolation、ServiceUnavailable、Patch nonzero/timeout；平台/Runner/Storage 故障不消耗。
- 管理重判不创建新 Submission、不消耗新尝试。

PlatformFailed 释放预占。修改上限不自动重判历史结果；管理员可手动重判。

## ManualBatch

题目配置 `EvaluationDispatchMode.Automatic | ManualBatch`。ManualBatch 不是人工填写 Correct/Wrong，只是管理员批量触发同一个自动 evaluator。

管理员先用统一 Submission 列表筛选。`queue-evaluation` 请求复用同一强类型筛选 DTO，必须指定 CompetitionChallengeId，可选 Team/Kind/ReceivedFrom/To；服务端接受时固定 `cutoff=(ReceivedAt,Id)` 上界并把规范化筛选+cutoff 放进 durable drain message。它只选择 ManualBatch 的 Pending，或任意 DispatchMode 下 PlatformFailed 且 CurrentScoringEventId 为空的首次失败 Submission；有旧事件的失败重判必须走 rejudge。

Drain Handler 每次按 `(ReceivedAt,Id)` 取最多 500 条，在短事务中使用 `FOR UPDATE SKIP LOCKED`，把仍符合且非 Processing 的记录置 Queued、ProcessingVersion++ 并写每项 EvaluateSubmission Outbox；提交后继续下一页。多 Worker 可并行 claim，不持有跨批事务，不返回/保存预计总数。消息重放会跳过已 Queued/Processing/Completed 记录。

无 Batch 实体、BatchId 或进度行；进度只通过同一筛选查询各 EvaluationState。202 响应回显规范化筛选与 cutoff，不伪造 status resource。

## ScoringEvent 替换

每个 Submission 只有一个当前未删除事件。重判流程：

1. 对原 Submission 设置 Queued、ProcessingVersion++；
2. 新判定完成前，旧 ScoringEvent 继续参与排行榜；
3. 成功判定时单事务插入新事件、设置旧事件 DeletedAt、更新 CurrentScoringEventId/Completed；
4. PlatformFailed 不替换旧事件；首次判定则状态为 PlatformFailed、当前事件为空；
5. LeaderboardRevision++ 并 Outbox invalidation。

不存在 Rejudge 表、RejudgeId 或复制 Submission。

## 权限与范围

Admin、Owner、Manager、Judge 可触发；Observer 只读；玩家绝不能触发。

- CTF/AWD Flag 与 AWDP Break 可按 CompetitionChallenge 批量重判。
- AWDP Fix/Patch 可精确按单 Submission 重判；使用原不可变 archive 与当前 Runtime/Checker 配置。
- 管理者可对任意单 Submission 重判。

集合 rejudge 同样固定筛选 cutoff，并以 500 条 `FOR UPDATE SKIP LOCKED` 短事务处理；只选择有 CurrentScoringEventId 的 Flag/Break，逐项设置 Queued/ProcessingVersion++。它不软删除旧事件；旧事件直到各项新判定成功时才分别原子替换。并发重复触发通过 ProcessingVersion 与状态跳过，不建立 Rejudge/Batch 记录。

## 管理查询

统一入口见 API 清单。SubmittedFlag 只支持 SHA-256+原文精确匹配，不支持子串/模糊搜索。列表可返回原始 Flag 给 Admin/Owner/所有 Collaborator；玩家状态 API不得返回。详情返回当前事件和所有 DeletedAt 历史事件。

## Fix Upload

上传 API 只创建 PatchUpload，不创建 Submission/尝试。每队每题最多一个未消费；新上传成功后替换并清理旧未消费对象。触发 API 先次数预检，再事务消费 Upload、创建 Fix Submission 和 Outbox。已消费 archive 不可修改，保留到 Competition 最终硬删除。
