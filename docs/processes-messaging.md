# 进程、消息与并发

本文描述当前 NATS-only Wolverine 拓扑；历史阶段文档不定义运行时兼容契约。

## 角色与持久化

`Api`、`Worker`、`Runner` 可以独立运行或由 `NoCTF.Host` 组合，但所有 durable 业务工作都经过
同一 NATS JetStream。JetStream 保存持久 stream、durable consumer、ack/retry 和 DLQ；业务任务不得走进程内 Channel、fire-and-forget
Task 或临时 local queue。

业务写入与 NATS 发布不提供跨系统原子事务；只允许数据库提交成功后由
`IPostCommitMessagePublisher` 发布。关键流程必须保留可扫描的 Pending 状态并能重新派发。
消费端使用状态、唯一键和业务幂等键收敛；稳定业务键同时写入 `Nats-Msg-Id`，但不得只依赖
JetStream duplicate window。所有消费 endpoint 使用 JetStream durable consumer；同一业务键重投只产生一次业务效果。重试策略按 queue
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

使用 NATS 2.12 原生定时投递的 Control、Gameplay、Background stream 必须同时覆盖正式
subject 与 `<subject>.scheduled`；consumer 只监听正式 subject。Runner stream 的
`noctf.v2.runner.>` 通配符覆盖两者。调度控制消息与目标 subject 相同会被 NATS 拒绝，
漏配 `.scheduled` 则发布不会收到 JetStream 确认。

Runner 任务由 Worker 依据 schema 3 presence 与关系化 capacity ledger 选择具体 RunnerId，再直接投递该节点命名 JetStream subject；
Runner 不监听 pool queue。队列名、目的地和消息类型必须由领域/Application 有界类型构建，不能用任意
字符串穿透业务层。

Leader 每 5 秒从关系事实重新派发滞留的 Runtime 和纯 GameplayFact 评测；GameplayFact
扫描只覆盖旧的 Queued，以及超过 60 秒仍为 Processing 的 Flag/Break/Hint/手工调整，
不重放外部 Fix/Checker 工作。扫描分页游标只随消息传递，不持久化。

## Singular Agent 调度

所有 Worker 使用 NATS KV CAS 租约选主。租约 30 秒、每 10 秒续租，KV revision 是 fencing token；
连续两次续租失败必须停止派发。集群同一时刻只有一个活动调度 Agent，负责从关系数据库业务事实
重建内存优先队列并派发：

- AWD Round；
- AWD Checker；
- KoH Poll；
- Competition lifecycle tick；
- 排行榜 500 ms debounce 集合。

Agent 只创建稳定业务键/FactId 并发送 durable 消息，不直接创建 Runtime、执行 Checker、计分或投影。
启动和 failover 从当前时间读取事实，计算第一个 `>= now` 的后续 tick；停机窗口不补跑。不得用业务
next-run 字段或 Wolverine Scheduled Message 保存周期执行时间。

Runner 的 resource-domain 所有权使用同一租约模型；失去租约后进入 draining 并终止，接管者从
Runtime、ledger 与 allocation 事实重建容量。无法选主、续租、恢复内存调度或验证必需 endpoint 时，Worker 不得报告调度就绪。就绪信息只暴露
leader、Agent 所属节点与最近接管时间，不暴露凭据或消息正文。

## 事务、锁与幂等

- 关系数据库主外键、普通唯一约束、Guid concurrency stamp 与 Serializable 事务保护业务不变量；
- JetStream ack/retry/DLQ 保护消息边界；
- MessageId/业务键识别同一 FactId 或同一 Runtime 动作；
- 外部资源操作以 Runtime UUID、runner/provider 与 typed receipt 幂等；
- 不使用数据库原生锁、Revision、ProcessingVersion、generation 或 scheduled-message 序号作为通用并发栅栏；
- 数据库事务只完成验证与状态写入，禁止调用非幂等外部服务。幂等事务冲突最多重试三次并短随机退避。

## 失败、重试与死信

瞬时基础设施失败可按队列退避重试；业务规则失败写入强类型结果并停止重试。Dead Letter 监控至少包含
queue、message type、failure kind 和 trace id。CompetitionId、RuntimeId、FactId 可进入结构化日志/Trace，
不得成为 Prometheus 高基数标签。管理员通过受权管理能力检查和重投 DLQ，比赛管理者不得直接操作队列。

## NATS JetStream 验证

升级 Wolverine 或 NATS 前必须重新验证 durable consumer、竞争消费、ack 超时重投、最大投递、
fan-out、Runner 定向 subject 与 scheduled delivery，不得仅根据在线最新版文档假定语义不变。
