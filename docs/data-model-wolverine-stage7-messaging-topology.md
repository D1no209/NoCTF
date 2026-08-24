# 阶段 7：Wolverine competing consumers 与显式 fan-out

## 目标与旧模型问题

阶段 7 将单消费者工作固定到四个命名 PostgreSQL 队列，并把
`CompetitionEventCommitted` 从同一逻辑 Handler 中的串行副作用拆为两个独立 Sticky
订阅者。旧实现把实时发布和排行榜投影放在同一个 Handler 中，任一副作用失败都会阻塞另一个，
同时缺少 Sticky endpoint 启动期校验，Wolverine 6.29.2 会在 endpoint 缺失时静默退化到
`local://` 队列。

本阶段不改变 Singular Agent 周期调度和排行榜投影算法；它们分别属于阶段 8、9。

## 消息分类与稳定队列

| 队列 | 类型 | 代表消息 | Handler 语义 |
|---|---|---|---|
| `noctf-control` | 命令/单消费者事件 | 生命周期、AWD Round/Checker 派发、Runtime Dispatch/Stop/Reconcile、AWDP Fix 超时与恢复 | competing consumers |
| `noctf-gameplay` | 命令/单消费者事件 | GameplayFact 评测/重判、运行结果收敛、作弊事实、AWDP Fix 结果 | competing consumers |
| `noctf-projection` | 命令 | 排行榜投影与阶段 9 前的旧刷新消息 | competing consumers |
| `noctf-background` | 单消费者事件 | 邮件、文件清理、通知、血榜、公开事件副作用 | competing consumers |
| `noctf-competition-events-realtime` | fan-out 订阅 | `CompetitionEventCommitted` | Redis/SignalR 发布 |
| `noctf-competition-events-leaderboard` | fan-out 订阅 | `CompetitionEventCommitted` | 排行榜投影触发；阶段 9 改为失效和 500ms 合并 |
| `noctf-runner-node-{hash}` | Runner 节点命令 | 已选择节点的 Runtime Provision/Stop/Inspect | 单节点 durable inbox；阶段 5 已闭环 |

单消费者消息不使用 Sticky Handler。所有 Worker 实例监听同名 PostgreSQL queue，依靠 durable
inbox 和 competing consumers 保证一个 Envelope 只由一个实例执行。业务事务中的发布继续使用
既有 EF transactional outbox。

## fan-out 不变量

- `CompetitionEventRealtimeMessageHandler` 唯一绑定
  `noctf-competition-events-realtime`。
- `CompetitionEventLeaderboardMessageHandler` 唯一绑定
  `noctf-competition-events-leaderboard`。
- 两个 endpoint 都是同名 durable PostgreSQL listener，并使用 durable inbox。
- 全局 MessageIdentity 为 `IdAndDestination`，每个目的地具有独立 Envelope identity。
- 保持 Wolverine 6.29.2 默认 `ClassicCombineIntoOneLogicalHandler`；检测到全局
  `MultipleHandlerBehavior.Separated` 时立即拒绝配置。
- 启动生命周期的 `StartedAsync` 在 Wolverine 完成 PostgreSQL transport 初始化后、Host 宣告启动成功前
  验证 Sticky 名称、同名 endpoint、PostgreSQL transport、durable listener、唯一 Handler以及不存在
  `local://` fallback；任一不变量失败都会使 `Host.StartAsync` 失败，实例不能进入 Ready。

## 重试、并发和监控

基础设施瞬时故障只针对 `TimeoutException` 和 `NpgsqlException`，按目标 queue 独立配置：

- control：1s、3s、10s、30s；
- gameplay：1s、5s、15s；
- projection：2s、10s、30s；
- background：5s、30s、2min；
- Runner 节点命令：1s、5s、15s、1min。

确定性业务失败不纳入无限重试，也不再把 `DbUpdateConcurrencyException` 当成 Revision 冲突重试。
并发度继续由 `Worker:Concurrency:{queue}` 独立控制，fan-out realtime/leaderboard 分别继承
background/projection 并发限制。

NoCTF 每 5 秒从六个稳定 queue 表读取待处理数量与最旧消息年龄，输出固定低基数 `queue` 标签。
完成、失败、执行耗时和 dead-letter 使用 Wolverine 6.29.2 原生 OpenTelemetry meter；其
`message.destination`、`message.type`、`exception.type` 提供 queue、消息类型和失败种类，trace id
保留在关联 Activity/结构化日志中，业务 UUID 不进入 Prometheus 标签。

## 数据、契约与部署影响

- 没有新增或删除业务表、字段、EF migration 或 model snapshot。
- Wolverine 自有队列表会新增两个稳定 fan-out queue；它们属于消息基础设施，不计入业务表数量。
- 没有 HTTP、OpenAPI 或 TypeScript SDK 变化。
- 部署时所有 Worker 必须共享同一 PostgreSQL persistence/transport。若拆分 workload，background
  实例必须监听 realtime fan-out，projection 实例必须监听 leaderboard fan-out；启动校验会阻止错误
  拓扑进入 Ready。

## 验证证据

定向命令：

```powershell
dotnet build backend/NoCTF.slnx -c Release --no-restore
dotnet test backend/tests/NoCTF.Tests/NoCTF.Tests.csproj -c Release --no-build -- `
  --treenode-filter "/*/*/WorkerRoleTests/*"
$env:NOCTF_REQUIRE_DOCKER_INTEGRATION = 'true'
dotnet test backend/tests/NoCTF.Tests/NoCTF.Tests.csproj -c Release --no-build -- `
  --treenode-filter "/*/*/*/*[Category=WolverineTopology]"
```

覆盖证据：

- 24 个 durable `ProjectLeaderboard` Envelope 由两个 competing consumer Host 处理，逐个严格一次；
- PostgreSQL 容器停止并重启后继续消费，第三个 replay probe 不产生重复执行；
- 同一 `CompetitionEventCommitted` 的两个 Sticky destination 各收到一次，Envelope Id 不同；
- realtime 订阅者失败时 leaderboard 订阅者仍独立完成；
- 缺少同名 Sticky PostgreSQL listener 的真实 Host 在 `StartedAsync` 门禁中启动失败；完整有效拓扑则
  通过同一门禁正常启动；
- 队列选择、fan-out 监控集合、local fallback 拒绝和默认组合语义有定向单元测试。
- Release solution build 0 warning/0 error；非 Integration TUnit 899/899、事务 Outbox 10/10、
  Wolverine topology 3/3。

## 阶段 7 退出门禁

- [x] 单 Handler 使用 named PostgreSQL queue 与 durable inbox。
- [x] 两个 Sticky destination 独立收到事件。
- [x] `IdAndDestination` 生成独立 Envelope identity。
- [x] 单订阅者失败不阻塞另一个。
- [x] Sticky endpoint 缺失或退化为 local queue 时启动失败。
- [x] PostgreSQL 重启与 Worker 重投后无重复业务执行。
- [x] queue 独立并发、基础设施重试和 dead-letter/积压监控已配置。
- [x] 未全局启用 `Separated`。
