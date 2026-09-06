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

ManualAdjustment 由管理员创建即为 Completed/Applied，Value 是非零 canonical signed Int32。AWD 每次 Checker 执行先创建独立事实，健康和不健康分别收敛为 ServiceUp/ServiceDown；当前状态取 `(OccurredAt, Id)` 最大的已完成事实。KoH 每次完成的轮询都创建事实，Producer/Timeout/Ambiguous 记为 PlatformFailed。

## 接入与尝试

Flag、Break、Hint 先创建事实并通过 Wolverine Outbox 发布 `EvaluateGameplayFact(GameplayFactId)`。AWDP Fix 可在一次性 Target 为 Queued、Provisioning 或 Running 时，由唯一一次 Patch 上传创建 Pending FixAttempt；Target Running 后 Worker 将其推进为 Processing 并发布 `RunAwdpFixVerification`。消息和内部 JWT 不携带事实处理版本或 callback hash。批量领取按 `(TeamId, CompetitionChallengeId, Kind)` 和 `(OccurredAt, Id)`，使用短事务及 `FOR UPDATE SKIP LOCKED`；PlatformFailed 是否消耗尝试沿用各模式规则。

Fix 事务在同一个按 Team/题/Kind 串行的临界区内验证 Competition、CompetitionChallenge、Team、一次性 AwdpTarget 与上传者，锁定 Target/File，并创建相互绑定的 PatchUpload、FixAttempt 和 Outbox。RuntimeInstance 与 PatchUpload 的部分唯一索引、Target 上的 GameplayFactId 共同保证一个 Target 只能消费一次；PatchUpload 不保存 ConsumedAt，消费事实由 GameplayFact Reference 与 Target 绑定共同确定。

## 重判与作弊事件

管理端可批量 queue evaluation/rejudge 或精确重判一个 GameplayFact。Drain 消息分为 `DrainGameplayFactEvaluation` 与 `DrainGameplayFactRejudge`，每批最多 500 条。旧持久化消息不兼容。

ForeignTeamFlagDetected 作弊事件直接以 GameplayFactId 为身份；Confirm、Dismiss、Correct、Supersede CompetitionEvent 都引用该 ID。重判覆盖一个仍有未解决作弊事件的事实时写 CheatIncidentSuperseded，但不创建判定版本。

## API 可见性

玩家查询 `/competitions/{competitionId}/gameplay-facts` 及单项状态，只能读取自己 Team 的 FlagAttempt、BreakAttempt、FixAttempt、HintUnlock，且不返回 Value。系统事实、管理员事实及其他队事实不可见。

管理路由统一位于 `/admin/competitions/{competitionId}/gameplay-facts`，支持 Challenge、Team、VictimTeam、Actor、Kind、State、Result、FailureCode、时间、Value 精确匹配和 Reference 筛选；排序固定为 `(OccurredAt desc, Id desc)` 的 signed keyset pagination。

## 历史差异预览

比赛工作人员可读取有界、游标分页的历史裁决差异预览。该预览分析 CTF 的
`FlagAttempt`，并额外识别 AWDP `BreakAttempt` 中当前结果恰为
`Duplicate / DuplicateAchievement` 的旧版本异常；AWD、KoH 及其他事实类型返回空结果。
AWDP Break 的 Milestone/PerRound 唯一性仍由排行榜投影负责，当前合法的 Correct
Break 不会被预览标为 Duplicate；同轮、跨轮旧异常都只读报告
`CurrentDuplicateShouldBeCorrect`，确定性预期结果为 Correct，且不分析 CTF 血榜。
该预览不会排队评测、修改 `GameplayFact`、追加 `competition_event` 或发布消息。

预览把当前 `GameplayFact` 与不可变事件流作为证据，而不是擅自补全已经不存在的历史：

- CTF 重复 Flag 与血榜名次按权威 `(OccurredAt, Id)` 顺序比较；
- AWDP 仅按当前 `Result` 与稳定 `FailureCode` 识别上述旧异常，不尝试从当前配置重建历史 Milestone/PerRound 首次成就；
- 缺失、名次错误、意外出现及重复的血榜事件只报告、不修改；
- 当前 `Correct` Flag 之前仍存在同队当前 `Correct` 属于确定性差异；
- 当前结果存在但缺少对应 `GameplayFactAdjudicated` 事件，标记为 `NeedsReview`；该历史完整性检查同样适用于纳入预览的 AWDP Break；
- 若当前队伍或会影响血榜名次的前序队伍已不是 Approved、已封禁或已删除，现有事件不足以无歧义重建发生时资格，因此不报告确定性血榜名次并标记为 `NeedsReview`；同一事实的重复血榜事件仍是确定性差异；
- 不可变事件中存在互相冲突的裁决结果，或当前 `Duplicate` 的前序事实已不再为 `Correct`，标记为 `NeedsReview`，因为旧配置、旧 Flag 归属和完整旧结果均未保存；
- 尤其不得静默推断或修复 `Correct → Wrong → Correct`。

Observer、Judge、Manager、Owner 与平台管理员均可读取，比赛软删除归档后仍可继续只读审计；
参赛者不可访问，不存在的比赛返回 404。此功能刻意不提供“应用纠正”操作。
