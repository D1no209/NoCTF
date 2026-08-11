# GameplayFact、判定与重判

## 单一当前事实

`GameplayFact` 同时表示客观比赛行为、当前处理状态、当前唯一结果和稳定失败原因。不存在 Submission、ScoringEvent、CurrentScoringEventId、ProcessingVersion、ClaimId、callback body hash、判定配置 revision 或判定历史表。

Kind：FlagAttempt、BreakAttempt、FixAttempt、HintUnlock、ManualAdjustment、AwdServiceTransition、KohControlObservation。状态流为：

```text
Pending/Queued -> Processing -> Completed
                         \-> PlatformFailed
```

首次平台失败保持 `Result=null`。重判开始只把 State 设为 Queued，旧 Result 继续供排行榜使用；成功时覆盖 Result/FailureCode，平台失败时保留已有 Result 并把 FailureCode 更新为最新平台原因。不复制事实，也不保留旧判定。

Flag 与 Break 保存原文 `Value` 和 32-byte SHA-256。Fix、Hint、AWD Round 通过同时为空或同时存在的 `(ReferenceKind, ReferenceId)` 关联；多态目标由 Application/Infrastructure 在事务内验证。

ManualAdjustment 由管理员创建即为 Completed/Applied，Value 是非零 canonical signed Int32。AWD 服务只在 Up/Down 变化时创建事实；KoH 每次完成的轮询都创建事实，Producer/Timeout/Ambiguous 记为 PlatformFailed。

## 接入与尝试

Flag、Break、Fix、Hint 先创建事实并通过 Wolverine Outbox 发布 `EvaluateGameplayFact(GameplayFactId)`。消息和内部 JWT 不携带事实处理版本或 callback hash。批量领取按 `(TeamId, CompetitionChallengeId, Kind)` 和 `(OccurredAt, Id)`，使用短事务及 `FOR UPDATE SKIP LOCKED`；PlatformFailed 是否消耗尝试沿用各模式规则。

Fix 事务先 `FOR UPDATE` 锁定 PatchUpload，验证 Competition、CompetitionChallenge、Team 与上传者，确认不存在 `ReferenceKind=PatchUpload/ReferenceId=upload.Id` 的事实，再创建 FixAttempt 和 Outbox。部分唯一索引保证并发时一个上传只能被一个事实引用；PatchUpload 不保存 ConsumedAt 或 GameplayFactId。

## 重判与作弊事件

管理端可批量 queue evaluation/rejudge 或精确重判一个 GameplayFact。Drain 消息分为 `DrainGameplayFactEvaluation` 与 `DrainGameplayFactRejudge`，每批最多 500 条。旧持久化消息不兼容。

ForeignTeamFlagDetected 作弊事件直接以 GameplayFactId 为身份；Confirm、Dismiss、Correct、Supersede CompetitionEvent 都引用该 ID。重判覆盖一个仍有未解决作弊事件的事实时写 CheatIncidentSuperseded，但不创建判定版本。

## API 可见性

玩家查询 `/competitions/{competitionId}/gameplay-facts` 及单项状态，只能读取自己 Team 的 FlagAttempt、BreakAttempt、FixAttempt、HintUnlock，且不返回 Value。系统事实、管理员事实及其他队事实不可见。

管理路由统一位于 `/admin/competitions/{competitionId}/gameplay-facts`，支持 Challenge、Team、VictimTeam、Actor、Kind、State、Result、FailureCode、时间、Value 精确匹配和 Reference 筛选；排序固定为 `(OccurredAt desc, Id desc)` 的 signed keyset pagination。

## 历史差异预览

比赛工作人员可读取有界、游标分页的历史裁决差异预览。该预览不会排队评测、修改 `GameplayFact`、追加 `competition_event` 或发布消息。

预览把当前 `GameplayFact` 与不可变事件流作为证据，而不是擅自补全已经不存在的历史：

- 重复成就与 CTF 血榜名次按权威 `(OccurredAt, Id)` 顺序比较；
- 缺失、名次错误、意外出现及重复的血榜事件只报告、不修改；
- 在成就唯一的模式中，当前 `Correct` 之前仍存在当前 `Correct` 属于确定性差异；
- 不可变事件中存在互相冲突的裁决结果，或当前 `Duplicate` 的前序事实已不再为 `Correct`，标记为 `NeedsReview`，因为旧配置、旧 Flag 归属和完整旧结果均未保存；
- 尤其不得静默推断或修复 `Correct → Wrong → Correct`。

Observer、Judge、Manager、Owner 与平台管理员均可读取；参赛者不可访问。此功能刻意不提供“应用纠正”操作。
