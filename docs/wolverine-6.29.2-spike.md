# Wolverine 6.29.2 技术 Spike

## 目的与边界

本 Spike 只验证仓库固定版本 Wolverine `6.29.2` 的实际 API 和运行行为，不修改 NoCTF 业务语义。验证日期为 2026-08-23，真实依赖由 Testcontainers PostgreSQL 提供。

## 研究来源

- [Wolverine 官方文档](https://wolverinefx.net/)
- [Wolverine LLM 文档索引](https://wolverinefx.net/llms.txt)
- PostgreSQL transport/durability、EF Core transactional inbox/outbox、durable inbox/outbox、Sticky handler、leader election、dead letter、handler retry 和 modular-monolith fan-out 专题。
- 本机 NuGet `6.29.2` XML metadata，用于确认在线文档中的 API 在固定版本确实存在。

## 6.29.2 API 证据

已通过编译确认：

- `PersistMessagesWithPostgresql(...)`
- `ListenToPostgresqlQueue(...)`
- `UseDurableInbox()`
- `UseEntityFrameworkCoreTransactions()`
- `StickyHandlerAttribute(string)`
- `MessageIdentity.IdAndDestination`
- `IWolverineRuntime.ExplainRoutingFor(Type)`
- `AddSingularAgent<TAgent>()`

## 真实 PostgreSQL 验证

测试位于：

- `backend/tests/NoCTF.Tests/Integration/Messaging/Wolverine6292TopologySpikeTests.cs`
- `backend/tests/NoCTF.Tests/Integration/Messaging/WolverineTransactionalOutboxTests.cs`

### Sticky fan-out 与 `IdAndDestination`

同一消息显式发送到两个命名 PostgreSQL endpoint；两个 handler 分别通过 `StickyHandler` 绑定自己的 endpoint。启用 `MessageIdentity.IdAndDestination` 后，每个目的地各执行一次，互不吞并。

### Competing consumers

两个独立 Wolverine host 监听同一 PostgreSQL queue。发送 24 条唯一消息后，每条消息在整个消费组中只执行一次，而且两个 host 都取得过工作。

### Sticky 缺失的实际行为

当 handler 声明的 Sticky endpoint 未注册时，Wolverine 6.29.2 不会自动启动失败，而是把 handler 退化到 `local://`。`ExplainRoutingFor(...)` 已固定该行为。

因此生产实现必须在 host 启动期间校验所有预期 Sticky endpoint：

1. endpoint 必须存在；
2. URI 必须为 PostgreSQL transport；
3. 不能是 `local://`；
4. 校验失败必须阻止 ready 状态。

### EF outbox、durable inbox 与事务回滚

现有真实 PostgreSQL 集成测试确认：

- 业务事实与 outbox 消息原子提交；
- 业务事务回滚时消息不发布；
- durable inbox 能消费持久消息；
- dead-letter 消息可重放。

### Singular Agent

两个 Worker host 注册同一 `MaintenanceTickAgent` 时只有一个活动 Agent；活动 host 停止后另一个 host 接管。Spike 同时暴露并修正了测试 host 漏注入 `TimeProvider` 的装配缺陷，未改变业务代码。

## 执行命令

```powershell
$env:NOCTF_REQUIRE_DOCKER_INTEGRATION='true'
dotnet tests/NoCTF.Tests/bin/Debug/net10.0/NoCTF.Tests.dll `
  --treenode-filter '/*/*/*/*[Category=Wolverine6292Spike]' `
  --minimum-expected-tests 3
```

结果：`3/3` 通过。

以下既有测试按 UID 单独执行并通过：

- `Maintenance_ticks_are_single_active_and_fail_over_between_workers`
- `Consumer_observes_business_fact_committed_with_outbox`
- `Failed_business_transaction_rolls_back_and_dead_letter_can_be_replayed`

## 阶段 0 结论

Wolverine 6.29.2 支持目标设计所需的 PostgreSQL durability、EF transactional outbox、durable inbox、competing consumers、Sticky、`IdAndDestination` 和 Singular Agent，无需升级依赖。缺失 Sticky endpoint 的静默 local fallback 是必须在阶段 7 通过启动校验消除的已验证风险。
