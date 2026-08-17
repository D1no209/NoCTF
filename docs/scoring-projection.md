# 计分与排行榜投影

## 事实源

排行榜只读取 PostgreSQL 的当前 `GameplayFact`、Team、CompetitionChallenge、Hint、AWD Round、生命周期事件和当前配置。`GameplayFact` 不保存分数；Projector 按 `Kind` 分发并用 `(OccurredAt, Id)` 确定稳定顺序。

- CTF：Correct FlagAttempt、Wrong 罚分、Unlocked Hint、Applied ManualAdjustment。
- AWD：带 VictimTeam/AwdRound 的 Correct FlagAttempt，以及状态变化产生的 AwdServiceTransition。
- AWDP schema v3：按 Team/CompetitionChallenge/Kind/round 选择最早 Correct，按该轮不同成功队伍数分别计算 Break/Fix 动态曲线；失败事实只应用一次罚分。
- KoH：每次完成轮询产生的 Controlled/Uncontrolled KohControlObservation；连续同队控制仍分别计分。

配置修改立即影响下一次全量投影，不保存历史配置，也不支持按过去时点重建。冻结排行榜在冻结命令内同步使用当前事实和配置完整生成，并把完整 JSON 保存到 Competition；冻结失败不改变可见性。

## Dirty 与 15 秒全量刷新

影响投影的事务在提交前设置 `Competition.LeaderboardDirty=true`。唯一维护 Tick 每秒调度 AWD checker、每 15 秒发布 `RefreshDirtyLeaderboards`、每 30 秒执行 Runner reconciliation 和生命周期推进；是否存在订阅者不影响刷新。

`RefreshDirtyLeaderboards` 使用 `FOR UPDATE SKIP LOCKED` 每批领取最多 500 个脏比赛，在同一短事务中清除 Dirty 并通过 Wolverine Outbox 写入 `ProjectLeaderboard`。投影期间产生的新变更会再次置 Dirty，由下一轮处理，不会丢失。

`ProjectLeaderboard` 获取 CompetitionId 对应的 PostgreSQL transaction advisory lock，从 PostgreSQL 全量加载并计算完整不可变 `LeaderboardResponse`，然后原子替换缓存并发布 `leaderboardRefreshed`。同一比赛的投影串行，旧任务不能在新任务之后覆盖快照。

AWDP v3 的分数会在没有新 GameplayFact 的逻辑轮边界变化。singular maintenance 的 15 秒刷新消息每批最多检查 500 场 Running AWDP v3 比赛；它用生命周期事件分别计算缓存 `DataAsOf` 与当前时刻的 EffectiveRunningTime/轮次，只在跨轮或缓存缺失时设置 Dirty。Pause 不跨轮，Finished 不再进入扫描，最终生命周期投影冻结分数。该机制不新增计划表，也不保存派生攻击/防御状态。

失败时保留旧快照、记录 `leaderboard:{competitionId:N}:last-failure`、重新置 Dirty 并让 Wolverine 重试。没有旧快照时 GET 返回 503 `LeaderboardProjectionFailed`；有旧快照时继续返回 200。

## FusionCache

排行榜使用命名缓存 `leaderboards`，稳定 key 为：

```text
leaderboard:{competitionId:N}
leaderboard:{competitionId:N}:last-failure
```

FusionCache 提供 L1，生产环境使用 Redis L2 与 backplane。排行榜业务代码不直接用 Redis `IDatabase` 读写快照；Redis Pub/Sub 仅负责跨 API 节点的 SignalR 通知。快照不使用 60 秒逻辑过期，正常替换由 Dirty 刷新驱动，Redis eviction 按 cache miss 恢复。

- Dirty 且缓存存在：返回旧的完整快照及 `GeneratedAt`。
- 缓存不存在：原子置 Dirty 并返回 202 Processing。
- 响应不包含 SnapshotRevision、TargetRevision、Stale 或 LastFailureAt。

## 共享分值衰减曲线

CTF 题值以及 AWDP Break/Fix 分值都使用 `ScoreCurveConfiguration`：

```text
InitialPoints: 1..1,000,000
MinimumPoints: 0..InitialPoints
DecayTeamCount: int > 1
DecayMode: Fixed | Linear | Quadratic | Exponential | Logarithmic | Custom
CustomExpression?: string
```

令 `x=clamp((solveCount-1)/(decayTeamCount-1),0,1)`。Linear 为 `initial+(minimum-initial)*x`；Quadratic 使用 `x²`；Exponential 使用归一化 `e^(-4x)`；Logarithmic 使用 `log10(1+9x)`；Fixed 恒为 InitialPoints。结果先钳制到 `[MinimumPoints, InitialPoints]`，再使用 AwayFromZero 取整为 signed Int64。`solveCount` 在 CTF 表示当前有效解题队伍数，在 AWDP 表示当前题目、当前轮、当前 Break 或 Fix 轨道的不同成功队伍数。

Custom 使用受限 DynamicExpresso，仅注入 `initialPoints`、`minimumPoints`、`solveCount`、`eligibleTeamCount`、`decayTeamCount`；禁止 Reflection、assignment、额外程序集和复杂对象。保存前对 0..eligibleTeamCount 的所有整数点验证，运行时同样钳制与取整。任何公式或 checked 聚合错误使整场投影失败并保留旧快照。

Points、Penalty、Hint Cost、ManualAdjustment 和最终分数使用 checked signed Int64。可配置单项分值、Penalty 与 Hint Cost 位于 0–1,000,000；百分比血奖仍为 0–100。该边界不改变 ManualAdjustment 的规范 Int32 文本边界，也不截断最终聚合分数。Correct solve 及血奖顺序统一按 `(OccurredAt, GameplayFactId)`。

公共响应仍是 `challenges[]` 与排序后的 `entries[]` 稀疏矩阵，并返回可见的 `tracks[]`。每条 Entry 带 TrackKey，名次在各赛道内独立计算。普通访问者只读取 VisibleOnLeaderboard 的非内部赛道（以及本队赛道的自身条目）；工作人员可查看全部赛道。EarnsScore=false 不生成排行榜条目。

CTF 动态分值的 eligibleTeamCount 与 solveCount 只读取 AffectsDynamicChallengeScore=true 的赛道；不影响动态分值但允许计分的队伍使用正式池的当前题目分值。全比赛一二三血只读取 EarnsBlood=true 的赛道。AWD/AWDP 的攻击或受害结算、KoH 的公开 King 与控制积分只读取 AffectsCompetitiveResults=true 的队伍；内部测试事实仍留在 PostgreSQL，但不能改变公开投影、事件或通知。

响应保留 GeneratedAt、DataScope、DataAsOf、Visibility、Entries、Challenges、Tracks。
