# 进程、消息与并发

本文描述简化迁移后的 Wolverine 目标拓扑。精确不变量以
[数据模型与 Wolverine 调度简化权威规范](data-model-wolverine-simplification.md) 为准。

## 角色与持久化

`Api`、`Worker`、`Runner` 可以独立运行或由 `NoCTF.Host` 组合，但所有 durable 业务工作都经过
同一 NATS JetStream。JetStream 保存持久 stream、durable consumer、ack/retry 和 DLQ；业务任务不得走进程内 Channel、fire-and-forget
Task 或临时 local queue。

业务写入与 NATS 发布不提供跨系统原子事务；消费端使用状态、唯一键和业务幂等键收敛。
所有消费 endpoint 使用 JetStream durable consumer；同一 MessageId 重投只产生一次业务效果。重试策略按 queue
和强类型失败类别配置，确定性业务失败不得无限重试。

## 消费语义

### Competing consumers

只需一个消费者执行的消息发布到一个命名 JetStream work-queue stream。多个 Worker 监听同一 durable consumer，
由 NATS 竞争消费；扩容不得复制业务效果。

### 显式 fan-out

需要多个独立订阅者的消息显式发布到各自命名的 JetStream consumer。每个订阅者拥有独立 ack、重试和 DLQ，消息身份使用 `IdAndDestination`，使同一 MessageId 在不同目的地各执行一次。
禁止全局启用 `MultipleHandlerBehavior.Separated`。

Wolverine 在 `[StickyHandler("name")]` 未配置同名 endpoint 时会静默生成 local queue；NoCTF
必须在启动门禁中解析实际 routing，任何必需 Sticky destination 缺失、不是 NATS JetStream 或退化为
`local://` 都使对应角色启动失败。

## 队列

单消费者工作使用四类命名持久队列：

| Queue | 工作负载 | 并发策略 |
|---|---|---|
| `noctf-control` | 生命周期、Round、超时恢复、资源对账 | 低并发、最高调度保障 |
| `noctf-gameplay` | Flag、Fix、Runtime 状态收敛、作弊事实 | 有上限的中高并发 |
| `noctf-projection` | 排行榜和比赛投影 | 每实例 1–2 并发 |
| `noctf-background` | 邮件、通知、同步导出以外的文件清理 | 低并发 |

Runner 任务由 Worker 先依据 Redis registry 选择具体 RunnerId，再直接投递该节点命名 durable queue；
Runner 不监听 pool queue。队列名、目的地和消息类型必须由领域/Application 有界类型构建，不能用任意
字符串穿透业务层。

## Singular Agent 调度

所有 Worker 共享 Wolverine/NATS 选主。集群同一时刻只有一个活动调度 Agent，负责从 PostgreSQL 业务事实
重建内存优先队列并派发：

- AWD Round；
- AWD Checker；
- KoH Poll；
- Competition lifecycle tick；
- 排行榜 500 ms debounce 集合。

Agent 只创建稳定业务键/FactId 并发送 durable 消息，不直接创建 Runtime、执行 Checker、计分或投影。
启动和 failover 从当前时间读取事实，计算第一个 `>= now` 的后续 tick；停机窗口不补跑。不得用业务
next-run 字段或 Wolverine Scheduled Message 保存周期执行时间。

无法选主、续租、恢复内存调度或验证必需 endpoint 时，Worker 不得报告调度就绪。就绪信息只暴露
leader、Agent 所属节点与最近接管时间，不暴露凭据或消息正文。

## 事务、锁与幂等

- PostgreSQL 主外键、业务唯一约束和 advisory lock 保护跨消息业务不变量；
- JetStream ack/retry/DLQ 保护消息边界；
- MessageId/业务键识别同一 FactId 或同一 Runtime 动作；
- 外部资源操作以 Runtime UUID、runner/provider 与 provider receipt 幂等；
- 不使用 Revision、ProcessingVersion、generation 或 scheduled-message 序号作为通用并发栅栏；
- 锁内只完成验证、状态写入与 Outbox，禁止调用外部服务。

## 失败、重试与死信

瞬时基础设施失败可按队列退避重试；业务规则失败写入强类型结果并停止重试。Dead Letter 监控至少包含
queue、message type、failure kind 和 trace id。CompetitionId、RuntimeId、FactId 可进入结构化日志/Trace，
不得成为 Prometheus 高基数标签。管理员通过受权管理能力检查和重投 DLQ，比赛管理者不得直接操作队列。

## 6.29.2 验证

固定依赖版本的编译与真实 PostgreSQL 行为记录见
[Wolverine 6.29.2 Spike](wolverine-6.29.2-spike.md)。升级 Wolverine 前必须重新运行同一组测试，不得
仅根据在线最新版文档假定 API 或语义不变。
