# 进程、消息与并发

## Wolverine 持久化

使用 Wolverine PostgreSQL persistence 保存：

- Durable Inbox/Outbox；
- API、Worker、Runner 的 durable queue；
- Scheduled Message；
- Dead Letter。

EF Core 业务事务与 Outbox 必须通过 Wolverine EF Core integration 绑定。参考官方资料：[Wolverine full LLM documentation](https://wolverinefx.net/llms-full.txt)。业务任务不得以 `System.Threading.Channels`、fire-and-forget Task 或仅内存定时器承载。

## 消息归属

| 消息 | 消费进程 |
|---|---|
| EvaluateSubmission / DrainManualEvaluation | Worker |
| Invalidate/ProjectLeaderboard | Worker |
| AdvanceCompetitionLifecycle / GenerateFlags / RotateAwdRound | Worker |
| SendNotification / CleanupObject | Worker |
| Start/Stop/ResetRuntime | Runner Pool |
| InjectAwdFlag / RunAwdChecker | Runner Pool |
| RunAwdpFixVerification | Runner Pool |

Runner 直接在完成事务中更新 RuntimeInstance，Provider/Archive 平台故障时可按 ProcessingVersion 更新对应 Submission，并通过 Outbox 发布后续消息。ChallengeFlag 是 Worker 生成的不可变答案事实，Runner 只读取并注入，不写注入状态。不存在 runtime_operations 表。

## 多 Worker 互斥

普通 Submission 可并行。以下操作按 CompetitionId 获取 PostgreSQL transaction-level advisory lock：

- 生命周期迁移；
- EffectiveRunningTime、AWD/AWDP 轮次与 Flag 轮换；
- Competition 结束；
- Leaderboard 完整投影/重建；
- 比赛级批量 Flag 预生成。

Submission 尝试次数使用更细粒度 `(TeamId, CompetitionChallengeId, SubmissionKind)` advisory lock，只串行同队同题同类型。HTTP 限流和请求验证在事务前完成；事务只进行额度检查、Insert、PatchUpload 消费和 Outbox，禁止在锁内上传文件或调用外部服务。

## 重试

- TransientInfrastructureFailure：1s、5s、15s、1m、5m，之后 Dead Letter。
- ConcurrencyConflict：带抖动快速重试，最多 5 次。
- BusinessRuleFailure、无效配置、团队输入错误：不重试，写业务结果。
- Admin 才能查看/重新投递 Dead Letter；比赛管理者通过领域 API 重新触发，不能直接操作队列。

`InjectAwdFlag` 是明确例外：使用同一 ChallengeFlag 以 1s 起步、最高 30s 的指数退避持续重新调度，下一次时间不得晚于 Flag.ValidUntil；到窗口结束后停止并记录最终管理失败，不提前因通用五次策略进入 DLQ，也绝不生成替代 Flag。Runtime 容量不足同样不是失败重试，而是保持实例 Queued 并由 capacity/heartbeat invalidation 再次唤醒派发。

## ProcessingVersion 栅栏

Submission、RuntimeInstance 以及需要异步写回的实体使用单调递增 ProcessingVersion。消息携带期望版本；写回时版本不符则结果为 superseded，不覆盖当前状态。相同版本与相同结果重放幂等成功，不同结果返回冲突。Runtime 的 Start/Stop/Reset/Extend 状态都落在 RuntimeInstance；Reset 预建下一 Generation 并用 `replaces_runtime_instance_id` 串联清理和创建，不引入 Operation 行。

AWD Checker 额外使用每个 RuntimeInstance 单调 CheckerSequence。序号在写 Runner Outbox 的同一事务分配，callback 通过 LastAppliedCheckerSequence 与 body hash 判定幂等、冲突或 superseded；只在 Up/Down 改变时写计分事件。完整字段与算法见 [Runtime Checker 调度](runtime.md#checker-调度字段)。

## Runner 容量

Runner heartbeat/capacity 只存在 Redis：

```text
runner:{runnerId}:heartbeat
runner:{runnerId}:capacity
runner-pool:{pool}:members
```

heartbeat 过期即离线。容量不足时 RuntimeInstance 保持 Queued，不标记 Failed，TTL 尚未开始。Redis 不可用时停止派发新 Runtime；PostgreSQL durable queue 保留任务。
