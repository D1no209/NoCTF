# 计分与排行榜投影

## 事实源

排行榜只读取 PostgreSQL 的 GameplayFact、Team、CompetitionChallenge、生命周期/可见性事件和当前
配置。GameplayFact 不保存分值、分差或累计分；Projector 按强类型 Kind 分发并以 `(OccurredAt, Id)`
确定稳定顺序。缓存与 SignalR 都不是事实源，Redis 全量丢失后必须能从 PostgreSQL 重建。

- CTF：正确 Flag、罚分、Hint、ManualAdjustment；
- AWD：攻击事实和每次独立 Checker GameplayFact；
- AWDP：每题每轮独立 Break/Fix 成功与强类型验证结果；
- KoH：每次完成轮询产生的控制观察事实。

配置变更影响下一次全量投影，不保存历史配置 Revision。需要冻结的公开结果由不可变比赛事件和事实
时间表达，不恢复 Competition 上的 Dirty/Snapshot/Revision 列。

## 事件驱动失效与 500 ms 合并

任何影响计分、资格或可见性的事务都通过 EF transactional outbox 发布强类型失效事件。每个订阅者
使用独立 Sticky PostgreSQL endpoint；排行榜订阅者收到事件后：

1. 立即删除该比赛的 FusionCache/Redis 榜单键；
2. 把 CompetitionId 加入 Singular Agent 的进程内合并集合；
3. 固定等待 500 ms，同一窗口只派发一次 durable 全量投影消息；
4. Handler 从 PostgreSQL 计算完整不可变响应；
5. 成功后原子替换缓存，再发布强类型 SignalR 刷新通知；
6. 失败时保持 cache miss 并按 durable 消息策略重试。

不保存 `Competition.LeaderboardDirty`，不扫描脏比赛，不使用 15 秒刷新。Agent 在失效后崩溃不会使旧
缓存重新可见；下一次 API cache miss 必须触发 PostgreSQL 重建。投影和发布使用单调发布 fence/协议
版本阻止晚到旧结果覆盖新结果，但该值不是业务表并发 Revision。

## Cache miss

同一 API 实例使用进程内 keyed lock 合并相同 CompetitionId 的并发 miss。多个 API 实例允许同时做
等价全量投影；只有完整结果可替换缓存。缓存仍使用命名 `leaderboards` FusionCache，稳定键为：

```text
leaderboard:{competitionId:N}
```

读取不得返回已知失效的旧快照。构建失败时返回明确的强类型投影失败响应，并保留 PostgreSQL 事实供
后续重试；订阅者数量不参与是否投影的决定。

## 共享分值衰减曲线

CTF 题值以及 AWDP Break/Fix 分值使用 `ScoreCurveConfiguration`：

```text
InitialPoints: 1..1,000,000
MinimumPoints: 0..InitialPoints
DecayTeamCount: int > 1
DecayMode: Fixed | Linear | Quadratic | Exponential | Logarithmic | Custom
CustomExpression?: string
```

令 `x=clamp((solveCount-1)/(decayTeamCount-1),0,1)`。Linear 为
`initial+(minimum-initial)*x`；Quadratic 使用 `x²`；Exponential 使用归一化 `e^(-4x)`；
Logarithmic 使用 `log10(1+9x)`；Fixed 恒为 InitialPoints。结果钳制到
`[MinimumPoints, InitialPoints]` 后以 AwayFromZero 取整为 signed Int64。

Custom 只允许 `initialPoints`、`minimumPoints`、`solveCount`、`eligibleTeamCount`、
`decayTeamCount`，禁止 Reflection、assignment、额外程序集和复杂对象。保存前验证所有整数点，投影
运行时同样钳制与取整。公式或 checked 聚合错误使本次投影失败，不得发布部分结果。

Points、Penalty、Hint Cost、ManualAdjustment 与总分使用 checked signed Int64。正确事实及血奖稳定
顺序为 `(OccurredAt, GameplayFactId)`。

## 赛道和可见性

响应包含可见 `tracks[]`、`challenges[]` 与排序后的 `entries[]` 稀疏矩阵。Entry 带 TrackKey，名次
按赛道独立计算。普通访问者只读取公开且非内部赛道以及本队允许看到的自身数据；工作人员可按权限查看
全部赛道。`EarnsScore=false` 不产生排行榜条目；内部测试事实保留在 PostgreSQL，但不改变公开投影、
事件或通知。

## 验证要求

必须用真实 PostgreSQL/Redis/Wolverine 覆盖：500 ms 合并、多个失效只投影一次、缓存全失重建、晚到
发布不能覆盖新结果、投影失败保持 miss、Worker 重投幂等，以及封禁/解封/重判后的全量重盘。
