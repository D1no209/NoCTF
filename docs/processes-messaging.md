# 进程、消息与并发

## Wolverine 持久化

使用 Wolverine PostgreSQL persistence 保存：

- Durable Inbox/Outbox；
- API、Worker、Runner 的 durable queue；
- Host 只是上述角色的组合边界；同进程角色之间仍走相同 durable queue，不存在旁路内存总线；
- Scheduled Message；
- Dead Letter。

维护调度由 Wolverine SingularAgent 只投递 durable tick：Runner assignment/lifecycle 每 30 秒，AWD checker dispatch 每 1 秒。Handler 使用 transaction-level advisory lock 和当前状态幂等，满页最多 500 条并通过 Outbox 投递固定 Cutoff/Cursor 的续页；Agent 不直接执行业务，也不使用 durable maintenance schedule 表。

EF Core 业务事务与 Outbox 必须通过 Wolverine EF Core integration 绑定。参考官方资料：[Wolverine full LLM documentation](https://wolverinefx.net/llms-full.txt)。业务任务不得以 `System.Threading.Channels`、fire-and-forget Task 或仅内存定时器承载。

## 消息归属

| 消息 | 消费进程 |
|---|---|
| EvaluateGameplayFact / DrainGameplayFactEvaluation / DrainGameplayFactRejudge | Worker |
| RefreshDirtyLeaderboards / ProjectLeaderboard | Worker |
| AdvanceCompetitionLifecycle / GenerateFlags / RotateAwdRound | Worker |
| SendNotification / CleanupFile | Worker |
| Start/Stop/ResetRuntime | Runner Pool |
| InjectAwdFlag / RunAwdChecker | Runner Pool |
| RunAwdpFixVerification | Runner Pool |

Runner 直接在完成事务中更新 RuntimeInstance；AWDP callback 使用 claim 中的 GameplayFactId 和 Runtime 自身的 Generation/ProcessingVersion，覆盖同一 GameplayFact 当前结果。ChallengeFlag 是 Worker 生成的不可变答案事实，Runner 只读取并注入，不写注入状态。不存在 runtime_operations 表。

## 多 Worker 互斥

普通 GameplayFact 可并行。以下操作按 CompetitionId 获取 PostgreSQL transaction-level advisory lock：

- 生命周期迁移；
- EffectiveRunningTime、AWD/AWDP 轮次与 Flag 轮换；
- Competition 结束；
- Leaderboard 完整投影/重建；
- 比赛级批量 Flag 预生成。

GameplayFact 尝试次数使用更细粒度 `(TeamId, CompetitionChallengeId, GameplayFactKind)` advisory lock，只串行同队同题同类型。Fix 还锁定 PatchUpload 并由部分唯一索引防并发消费。HTTP 限流和请求验证在事务前完成；事务只进行额度检查、Insert、引用验证和 Outbox，禁止在锁内上传文件或调用外部服务。

## 重试

- TransientInfrastructureFailure：1s、5s、15s、1m、5m，之后 Dead Letter。
- ConcurrencyConflict：带抖动快速重试，最多 5 次。
- BusinessRuleFailure、无效配置、团队输入错误：不重试，写业务结果。
- Admin 才能查看/重新投递 Dead Letter；比赛管理者通过领域 API 重新触发，不能直接操作队列。

`InjectAwdFlag` 是明确例外：使用同一 ChallengeFlag 以 1s 起步、最高 30s 的指数退避持续重新调度，下一次时间不得晚于 Flag.ValidUntil；到窗口结束后停止并记录最终管理失败，不提前因通用五次策略进入 DLQ，也绝不生成替代 Flag。Runtime 容量不足同样不是失败重试，而是保持实例 Queued 并由 capacity/heartbeat invalidation 再次唤醒派发。

## 异步写回栅栏

GameplayFact 消息只携带 GameplayFactId，不保存或传递事实 ProcessingVersion/ClaimId/结果摘要。当前结果直接覆盖同一行。Runtime 的 Start/Stop/Reset/Extend 仍使用 RuntimeInstance 自己的 ProcessingVersion；Reset 预建下一 Generation 并用 `replaces_runtime_instance_id` 串联清理和创建，不引入 Operation 行。

AWD Checker 由 Worker 周期调度并附着到 Runtime 内部网络。Checker 通过 internal endpoint 主动更新状态，只在 Up/Down 改变时写 AwdServiceTransition GameplayFact。完整语义见 [Runtime Checker 调度](runtime.md#checker-调度与状态)。

## Runner 容量

Runner heartbeat/capacity 只存在 Redis：

```text
runner:{runnerId}:heartbeat
runner:{runnerId}:capacity
runner-pool:{pool}:members
```

heartbeat 过期即离线。容量不足时 RuntimeInstance 保持 Queued，不标记 Failed，TTL 尚未开始。Redis 不可用时停止派发新 Runtime；PostgreSQL durable queue 保留任务。
