# 阶段 9：事件驱动排行榜投影

## 目标与旧模型问题

阶段 9 删除 `Competition.LeaderboardDirty` 及 15 秒全表扫描，把排行榜刷新改为比赛事件驱动。
旧模型把“是否需要投影”保存在业务行上，再由 Singular Agent 周期扫描；这会产生最多 15 秒延迟、
无事件时的重复数据库扫描，以及缓存、数据库状态和后台 tick 三套协调状态。

新模型以已提交的 `CompetitionEventCommitted` 为唯一失效信号。排行榜 Sticky PostgreSQL 订阅者
先立即删除 Fusion/Redis 已发布快照，再把比赛 ID 放入同进程 500ms 合并窗。Wolverine leader 上的
`MaintenanceTickAgent` 只派发 durable `ProjectLeaderboard`，实际全量 PostgreSQL 投影仍由 projection
competing consumer 执行。

## 事件、合并与 durable 投影

1. 影响排行榜的业务事务通过 EF transactional outbox 发布 `CompetitionEventCommitted`。
2. 显式 fan-out 将消息独立投递到 realtime 与 leaderboard 两个 Sticky PostgreSQL endpoint。
3. leaderboard endpoint 使用 `ListenOnlyAtLeader()`，和 Singular Agent 固定在同一 Wolverine leader。
4. Handler 立即调用 `ILeaderboardCache.InvalidateAsync`，写入 Redis tombstone fence 并删除已发布 payload。
5. `LeaderboardProjectionMergeQueue` 按比赛保留首个事件的 500ms fixed window；窗口内后续事件只合并，
   不滑动截止时间。
6. Singular Agent 每 50ms 取出到期比赛并 durable 发布一条 `ProjectLeaderboard`；发布失败按同一比赛
   延迟 1 秒重试。
7. projection Handler 从 PostgreSQL 全量读取比赛、队伍、题目、GameplayFact 和生命周期事实，在
   `RepeatableRead` 事务中生成完整快照。
8. Redis fencing Lua 只允许更新 fence 更大的快照；成功后替换 Fusion 缓存并发布 SignalR。

没有周期排行榜 tick、业务 next-run 字段、`LeaderboardDirty` 标志或局部增量投影。Agent 不执行查询、
计分或缓存发布，只派发 durable 消息。

## 缓存缺失、故障恢复与乱序保护

- API 读取遇到缓存缺失时，使用比赛级 keyed lock 合并同进程并发请求；锁内再次检查缓存后从
  PostgreSQL 全量重建并发布。
- Redis/Fusion 缓存整体丢失不会影响业务事实；下一次读取可从 PostgreSQL 重建。
- 失效操作会原子递增 Redis fence、保留 tombstone fence 并删除 payload。失效前已经开始的旧投影
  即使晚到，也不能覆盖 tombstone 或较新的快照。
- leader 在 500ms 内存窗口中退出时，窗口本身不补跑；缓存已经失效，后续 API 读取会重建，后续业务
  事件也会建立新窗口。停机期间不依赖内存状态保存业务事实。
- 投影或发布失败不会恢复旧缓存；durable message 按 projection queue 的 Wolverine retry/dead-letter
  规则处理。

## 拓扑与启动门禁

- `CompetitionEventLeaderboardMessageHandler` 必须是唯一 Sticky Handler。
- `noctf-events-leaderboard` 必须是 durable PostgreSQL listener，并且
  `ListenerScope.PinnedToLeader`。
- endpoint 缺失、退化为 `local://`、未 pinned leader、重复 Handler 或全局
  `MultipleHandlerBehavior.Separated` 时 Host 启动失败。
- 所有 Worker 继续共享同一 Wolverine PostgreSQL persistence/transport；`ProjectLeaderboard` 使用
  `noctf-projection` durable queue 和 durable inbox。

## 可观测性与契约影响

- 删除 dirty competition 数量、最老 dirty age 和 dirty scan 指标。
- 新增低基数排行榜 invalidation、merge started/coalesced、merge durable dispatch、cache miss、
  projection 和 publish failure 指标；比赛 ID 只进入 Trace/结构化日志，不作为 Prometheus 标签。
- 平台监控 Application 模型与前端监控卡片同步改为事件驱动指标。阶段 11 才统一导出 OpenAPI 并重新
  生成 TypeScript SDK；阶段 9 未手改生成 SDK。
- 没有新增业务表、字段或 migration；仅删除已经在阶段 2 从实体移除后遗留的 dirty 协调代码。

## 验证证据

```powershell
dotnet build backend/NoCTF.slnx -c Release --no-restore
dotnet test backend/tests/NoCTF.Tests/NoCTF.Tests.csproj -c Release --no-build -- `
  --treenode-filter "/*/*/LeaderboardProjectionMergeQueueTests/*"
$env:NOCTF_REQUIRE_DOCKER_INTEGRATION = 'true'
dotnet test backend/tests/NoCTF.Tests/NoCTF.Tests.csproj -c Release --no-build -- `
  --treenode-filter "/*/*/RedisLeaderboardPublicationFenceTests/*"
dotnet test backend/tests/NoCTF.Tests/NoCTF.Tests.csproj -c Release --no-build -- `
  --treenode-filter "/*/*/LeaderboardProjectionPersistenceTests/*"
dotnet test backend/tests/NoCTF.Tests/NoCTF.Tests.csproj -c Release --no-build -- `
  --treenode-filter "/*/*/NoCtfWolverineTopologyTests/*"
dotnet test backend/tests/NoCTF.Tests/NoCTF.Tests.csproj -c Release --no-build -- `
  --treenode-filter "/*/*/WolverineTransactionalOutboxTests/Maintenance_ticks_are_single_active_and_fail_over_between_workers"
dotnet test backend/tests/NoCTF.Tests/NoCTF.Tests.csproj -c Release --no-build -- `
  --treenode-filter "/*/*/WorkerRoleTests/*"
cd backend/src/NoCTF.API/ClientApp
bun test
bun run typecheck
bun run build
git diff --check
```

结果：Release build 0 warning/0 error；合并窗 4/4；真实 Redis fencing 4/4；真实 PostgreSQL/Redis
排行榜投影 9/9；真实 PostgreSQL/Wolverine fan-out 3/3；双 Worker Singular Agent durable 合并派发与
failover 1/1；拓扑门禁 8/8；监控模型 3/3；前端 275/275、typecheck 和 production build 通过。
`git diff --check` 无 whitespace error（PowerShell 工作区仅报告既有 LF/CRLF 提示）。

## 阶段 9 退出门禁

- [x] 删除 `LeaderboardDirty` 和 15 秒扫描/tick。
- [x] 相关已提交事件立即失效缓存。
- [x] 同比赛事件按 fixed 500ms 合并为一条 durable 投影消息。
- [x] 每次投影从 PostgreSQL 全量重建。
- [x] cache miss 使用 keyed lock 重建，Redis/Fusion 丢失可恢复。
- [x] tombstone fence 阻止失效前的旧投影晚到覆盖。
- [x] leaderboard Sticky endpoint 与 Singular Agent 固定在同一 leader。
- [x] Sticky 缺失、local fallback 或 listener scope 错误时启动失败。
- [x] Agent 只派发 durable 消息，不直接执行投影。
- [x] 阶段 9 定向后端、真实依赖与前端门禁全部通过。

