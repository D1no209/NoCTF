# 计分与排行榜投影

## 事实源

排行榜只读取 PostgreSQL 的当前 `GameplayFact`、Team、CompetitionChallenge、Hint、AWD Round、生命周期事件和当前配置。`GameplayFact` 不保存分数；Projector 按 `Kind` 分发并用 `(OccurredAt, Id)` 确定稳定顺序。

- CTF：Correct FlagAttempt、Wrong 罚分、Unlocked Hint、Applied ManualAdjustment。
- AWD：带 VictimTeam/AwdRound 的 Correct FlagAttempt，以及状态变化产生的 AwdServiceTransition。
- AWDP：Correct BreakAttempt/FixAttempt、模式罚分、Hint 和 ManualAdjustment。
- KoH：每次完成轮询产生的 Controlled/Uncontrolled KohControlObservation；连续同队控制仍分别计分。

配置修改立即影响下一次全量投影，不保存历史配置，也不支持按过去时点重建。冻结排行榜在冻结命令内同步使用当前事实和配置完整生成，并把完整 JSON 保存到 Competition；冻结失败不改变可见性。

## Dirty 与 15 秒全量刷新

影响投影的事务在提交前设置 `Competition.LeaderboardDirty=true`。唯一维护 Tick 每秒调度 AWD checker、每 15 秒发布 `RefreshDirtyLeaderboards`、每 30 秒执行 Runner reconciliation 和生命周期推进；是否存在订阅者不影响刷新。

`RefreshDirtyLeaderboards` 使用 `FOR UPDATE SKIP LOCKED` 每批领取最多 500 个脏比赛，在同一短事务中清除 Dirty 并通过 Wolverine Outbox 写入 `ProjectLeaderboard`。投影期间产生的新变更会再次置 Dirty，由下一轮处理，不会丢失。

`ProjectLeaderboard` 获取 CompetitionId 对应的 PostgreSQL transaction advisory lock，从 PostgreSQL 全量加载并计算完整不可变 `LeaderboardResponse`，然后原子替换缓存并发布 `leaderboardRefreshed`。同一比赛的投影串行，旧任务不能在新任务之后覆盖快照。

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

## 数值与 CTF 表达式

Points、Penalty、Hint Cost、ManualAdjustment 和最终分数使用 checked signed Int64；溢出使整场投影失败并保留旧快照。CTF DynamicExpresso 只注入 `initialPoints`、`minimumPoints`、`solveCount`、`eligibleTeamCount`、`decayParameter`，禁止 Reflection、assignment、额外程序集和复杂对象。Correct solve 及血奖顺序统一按 `(OccurredAt, GameplayFactId)`。

公共响应仍是 `challenges[]` 与排序后的 `entries[]` 稀疏矩阵，保留 GeneratedAt、DataScope、DataAsOf、Visibility、Entries、Challenges。
