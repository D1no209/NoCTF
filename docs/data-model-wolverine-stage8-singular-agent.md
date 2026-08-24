# 阶段 8：Singular Agent 周期调度

## 目标与旧模型问题

阶段 8 将 AWD Round、AWD Checker、KoH Poll 与比赛生命周期 tick 迁移到 Wolverine
6.29.2 集群 `SingularAgent`。旧实现依靠 Handler 递归发布 Wolverine Scheduled Message 保存
下一次执行时间；Worker 停机恢复后会继续消费已经过时的 tick，形成补跑洪峰，并让周期调度状态散落
在消息存储而不是 PostgreSQL 业务事实中。

新调度器只在内存中维护优先队列，只派发 durable 消息，不直接执行 Runtime、Checker、计分或投影重活。
Wolverine 集群选主保证同一时刻只有一个活动调度器；接管节点从 PostgreSQL 当前业务事实和当前时间重建，
跳过停机窗口中的历史 tick。

阶段 9 前仍临时保留 15 秒排行榜刷新 tick；它不再由 Scheduled Message 递归保存，但将在阶段 9 随
`LeaderboardDirty` 一并删除并改为事件驱动 500ms 合并投影。

## 调度来源与恢复语义

| 周期工作 | 重建依据 | 恢复行为 | durable 目的地 |
|---|---|---|---|
| AWD Round | Running 比赛、有效运行时长、已发布题目、现有轮次 Flag 窗口、合格队伍 | 计算当前轮次；只派发当前/下一轮，不补历史轮次 | `noctf-control` |
| AWD Checker | 固定 1 秒 tick；Handler 从当前 PostgreSQL Runtime/配置派生工作 | 首次接管立即派发，之后从当前时间继续 | `noctf-control` |
| KoH Poll | Running 比赛、已发布题目、最后观察事实、当前配置 | 过期计划收敛到当前时刻一次，随后从当前时间继续 | `noctf-control` |
| 生命周期 | 固定 30 秒 tick；Handler读取当前比赛事实 | 首次接管立即派发，不重放停机窗口 | `noctf-control` |
| 排行榜刷新（临时） | 固定 15 秒 tick | 阶段 9 删除 | `noctf-projection` |

AWD 没有已审核、未封禁、未删除队伍时不创建空轮次，也不保留无意义计划。后续出现合格队伍时，
调度源按比赛当前有效运行时长直接计算当前轮次。KoH Poll 使用
`(CompetitionChallengeId, DueAt)` 派生稳定 `GameplayFactId`，重投不会创建第二条业务事实。

## 唯一 Agent、诊断租约与就绪门禁

- `MaintenanceTickAgent` 继承 `SingularAgent`，名称固定为 `noctf-maintenance-ticks`。
- Wolverine leader/agent assignment 是唯一性的权威；Redis 只保存低成本、短 TTL 的诊断租约，不能参与
  业务选主。
- Agent 接管后必须先完成 PostgreSQL 计划重建，再写入 scheduler owner、接管时间和本地恢复状态。
- 当前 Wolverine leader 与 scheduler owner 每 5 秒续租，15 秒过期；续租丢失会把本地调度状态标记为
  失败。
- `/health/ready` 返回 `leaderNode`、`schedulerOwnerNode`、`schedulerTakenOverAt`、本地节点及是否本地
  owner。Wolverine 未完成 balanced agent assignment、无 leader、无 scheduler owner、owner 本地状态
  未恢复或重建超过 20 秒时均 fail closed。
- 诊断不包含数据库凭据、消息正文、CompetitionId、TeamId 或 RuntimeId。

## 幂等、停机与可观测性

- 动态计划每 5 秒从 PostgreSQL 重建；相同 key 已派发的 due time 不会再次派发。
- 固定周期和动态周期成功派发后都以“当前时刻 + interval”计算下一次执行，禁止追赶积压 tick。
- durable publish 失败时同一消息在 1 秒后重试；Agent 不直接执行业务操作。
- 指标使用固定 `schedule`/`outcome` 标签，覆盖接管次数、重建耗时、计划数量、派发延迟、失败与跳过
  tick 数；业务 UUID 只允许进入结构化日志/Trace。
- Leader/Agent 故障转移由 Wolverine PostgreSQL durability 协调；新 owner 重建内存状态后继续。

## 数据、契约与部署影响

- 没有新增或删除业务表、字段、EF migration 或 model snapshot。
- 没有 HTTP、OpenAPI 或 TypeScript SDK 变化；仅扩展现有 readiness 响应中的诊断 data。
- 删除 AWD Round、KoH Poll 与比赛生命周期 Handler 的递归 Scheduled Message next-run。
- 所有 Worker 必须共享同一 Wolverine PostgreSQL persistence/transport；Redis 诊断租约不可替代
  Wolverine leader election。
- 部署时 readiness 必须在 Agent 完成接管重建后才成功；若无法选主、续租或恢复，实例不得报告
  Worker 调度能力 Ready。

## 验证证据

定向命令：

```powershell
dotnet build backend/NoCTF.slnx -c Release --no-restore
dotnet test backend/tests/NoCTF.Tests/NoCTF.Tests.csproj -c Release --no-build -- `
  --treenode-filter "/*/*/ClusterScheduleTests/*"
$env:NOCTF_REQUIRE_DOCKER_INTEGRATION = 'true'
dotnet test backend/tests/NoCTF.Tests/NoCTF.Tests.csproj -c Release --no-build -- `
  --treenode-filter "/*/*/AwdRoundCoordinationTests/*"
dotnet test backend/tests/NoCTF.Tests/NoCTF.Tests.csproj -c Release --no-build -- `
  --treenode-filter "/*/*/WolverineTransactionalOutboxTests/Maintenance_ticks_are_single_active_and_fail_over_between_workers"
```

覆盖证据：

- 两个真实 PostgreSQL/Wolverine Worker Host 同时运行时只有一个活动
  `noctf-maintenance-ticks` Agent；停止 owner 后另一实例接管。
- 当前轮次恢复测试在停机跨过三个历史窗口后只创建当前第 4 轮，不创建第 1–3 轮补跑消息。
- 当前轮次只创建一次 Flag/事实；递归 Scheduled Message 集合为空。
- 所有参赛队伍被封禁后不再创建空轮次，协调器幂等返回。
- 时间钳制、跳过 tick 计数与 KoH 确定性 FactId 有定向单元测试。
- Release solution build 0 warning/0 error；阶段 8 定向测试 6/6 通过。

## 阶段 8 退出门禁

- [x] 多 Worker 同一时刻只有一个活动 Singular Agent。
- [x] owner 停止后另一 Worker 接管。
- [x] 接管从 PostgreSQL 当前事实重建。
- [x] 停机窗口没有历史 tick 补跑洪峰。
- [x] AWD 恢复后从当前轮次继续。
- [x] AWD Round、AWD Checker、KoH Poll 与 lifecycle tick 均由 Agent 派发 durable 消息。
- [x] Agent 不直接执行 Runtime、Checker、计分或投影重活。
- [x] readiness 暴露 leader、owner 和接管时间并在无法恢复时 fail closed。
- [x] 没有业务 next-run 字段或周期性递归 Scheduled Message。
