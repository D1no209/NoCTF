# 阶段 5：Runtime 最小模型与节点直投

## 实施基线

- 分支：`codex/data-model-wolverine-simplification`
- 父提交：`f3767f58`
- 权威规范：`specs/data-model-wolverine-simplification.md` 第 5.7、6.1、12.6 节
- 迁移状态：旧迁移暂不改动；阶段 10 通过 EF CLI 生成单一 InitialBaseline。

## 旧模型问题

Runtime 行同时保存了资源事实、定义快照派生值、调度游标、Checker 状态和 Runner
路由信息。创建请求先进入 pool queue，再由任意 Runner 竞争 Claim，造成 Worker 没有明确
选择节点，数据库中的 `runner_pool`、generation/sequence/revision 字段又成为第二套调度状态。
Reset 还依赖 generation/replaces 关系表达新旧实例，违背“每次动作读取最新定义、每次 Reset
创建独立 UUID”的目标语义。

## 本阶段字段闭环

从 `RuntimeInstance` 删除：

- `AwdpFixStage`
- `Generation`
- `RunnerPool`
- `RunnerAssignmentReleaseToken`
- `RunnerUnavailableAt`
- `ReplacesRuntimeInstanceId`
- `ParticipantUrlIndexes`
- `ControlCheckUrl`
- `AwdCheckerTargetHost`
- `CheckerStatus`、`CheckerStatusUpdatedAt`
- `CheckerSequence`、`LastAppliedCheckerSequence`
- `NextCheckerDueAt`、`CheckerDeadlineAt`
- `RuntimePublishedPort.AllocatedAt`

保留 `Id`、比赛/题目/队伍/Purpose/GameplayFact 关系、Runtime kind/provider、`RunnerId`、
状态与稳定失败码、Provider Receipt、URL、Published Ports、生命周期时间。

## 消息与路由闭环

- 删除 pool Claim 消息和 `IRunnerPoolMessage`。
- `DispatchRuntime` 在 Worker 读取最新题目定义与平台 placement，调用 Redis Registry/Capacity
  原子选择并 Claim 具体 RunnerId。
- Worker 在同一业务事务中写入 `RunnerId`/Provisioning 并通过 transactional outbox 直投
  `runner-node-{runnerId}`。
- 节点命令只携带 RuntimeId、RunnerId 和执行所需的最新定义；不携带 generation/pool。
- Stop/Cleanup 只以 RuntimeId、RunnerId、provider 和真实 `provider_receipt_json` 为准。
- 同一 Envelope 由 Wolverine durable inbox 保证 MessageId 幂等；不同 Envelope 按最后完成的
  Handler 结果覆盖，不再做 generation/sequence 防护。

## API 与读取闭环

- 参与者 URL 不再保存 index；读取时使用最新题目定义的参与者 URL 选择规则过滤 `Urls`。
- 管理端/API/生成 SDK/前端不再暴露 generation、runner pool、control/checker 内部字段或
  PublishedPort 分配时间。
- Reset 始终插入新 Runtime UUID，旧行进入 Stopping/Stopped 历史，不保存 replaces 关系。

## 定向门禁

- [x] Runtime EF 模型不包含已删除字段。
- [x] Reset 创建新 UUID 且旧行保留。
- [x] 多 Runner 集成测试证明 Worker 选择具体节点并只投递 node queue。
- [x] Runner 不注册 pool queue。
- [x] 晚到状态消息测试证明不同 Envelope 最后完成者获胜。
- [x] stop/cleanup 仅依据 receipt，容量与外部资源无泄漏。
- [x] URL 读取按最新定义过滤。
- [x] OpenAPI/SDK 重新生成且幂等。
- [x] 定向测试、Release build、前端测试/typecheck/build、`git diff --check` 通过。

## 行为验证证据

- 真实 PostgreSQL/Redis 多 Runner 测试由 Worker 为两个 Runtime 原子选择不同节点，持久化
  `RunnerId` 并仅向两个 `runner-node-{runnerId}` Sticky endpoint 发送命令；仓库不存在
  `runner-pool-*` listener、消息接口或 Claim Handler。
- Reset 的真实 PostgreSQL 测试证明新行使用新 UUID，旧 Runtime 仍保留并收敛停止；模型不再保存
  generation/replacement 关系。
- 不同 Wolverine Envelope 的完成顺序测试证明后完成结果覆盖先完成结果；同 Envelope 仍由 durable
  inbox 与稳定 MessageId 幂等。
- 资源对账覆盖 `Failed + RunnerId` 且没有 Provider Receipt 的失联分配：先向节点幂等确认资源不存在，
  再释放容量；有 Receipt 时停止/清理只使用 Receipt 中的真实外部资源身份。
- 参与者 URL 单元测试以最新题目定义过滤已保存 URL；定义删除公开索引后，旧 URL 不再向参赛者返回。
- OpenAPI 和生成 TypeScript SDK 连续生成两次 SHA-256 一致；生成文件未手工修改。

## 测试结果与阶段边界

- Release solution build：0 warning、0 error。
- 非 Integration TUnit：896/896 通过。
- ClientApp：275/275，通过 typecheck 与 production build。
- 完整 Integration：177 项中 171 通过、4 失败、2 跳过。跳过项分别缺少真实 Kubernetes 集群和
  Libvirt 磁盘。4 个失败均是阶段 8/9 明确废弃的旧语义断言：AWDP 周期全表扫描、封禁后立即走
  旧投影、`LeaderboardDirty` logical-round 扫描，以及 Wolverine scheduled successor。阶段 5
  没有篡改这些测试来伪造通过；它们将在阶段 8 Singular Agent 和阶段 9 事件驱动排行榜闭环中替换。
- 阶段 1—9 的真实 PostgreSQL Integration 暂用 `EnsureCreated` 建立当前模型。旧 migration 与
  snapshot 故意保留到阶段 10，再通过 EF CLI 重建单一 InitialBaseline；不得手改工具生成文件。

## 下一阶段前置条件

阶段 5 已满足 Runtime 最小模型与节点直投退出门禁。下一步只进入阶段 6：每次 AWD Checker
预创建独立 GameplayFact、按最新 Fact 派生服务状态，并完成 AWDP 六种强类型结果映射。不得提前
实现阶段 7 队列拓扑、阶段 8 Singular Agent 或阶段 9 排行榜投影。
