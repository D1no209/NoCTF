# 阶段 6：GameplayFact 与 AWDP 强类型结果

## 实施基线

- 分支：`codex/data-model-wolverine-simplification`
- 父提交：`851fc689`
- 权威规范：`specs/data-model-wolverine-simplification.md` 第 5.8、12.7 节
- 迁移状态：旧 migration/snapshot 暂不改动；阶段 10 仅通过 EF CLI 重建单一 InitialBaseline。

## 旧模型问题

- AWDP 的 `ServiceAbnormal`、`PatchFailed`、`PatchTimeout` 被写成 `Wrong`，而不是
  `Rejected`；`PlatformFailed` 又把整个 GameplayFact 写成 `PlatformFailed` 状态，和六类
  outcome 全部收敛为 `Completed` 的权威语义冲突。
- `AwdpFixResolved` 只写通用事件字段，没有保存可验证的强类型、版本化 outcome 与稳定失败码，
  无法在不解析自由文本的情况下重建一次 Fix 的最终诊断。
- AWD Checker 虽然在执行前创建独立 GameplayFact，但回调丢失只发送工作人员通知，事实会永久
  停在 `Processing`；旧 `AwdServiceStateTransition` 还表达“只有状态变化才产生事实”的废弃语义。
- AWDP 内部回调仍接受 `Fixed`、`StillVulnerable`、`RuleViolation` 等旧协议别名，掩盖当前契约错误。

## 本阶段闭环范围

### GameplayFact

- 每次 AWD Checker 执行预创建一条独立 `AwdServiceTransition` GameplayFact。
- 回调按同一个 FactId 完成该事实；回调超时同样将该事实幂等收敛为
  `Completed / ServiceDown / CheckerPlatformError`。
- 当前服务状态由最新 `(OccurredAt, Id)` 的已完成 Checker Fact 派生；计分只读取
  GameplayFact，不新增 Runtime Checker 状态或调度字段。
- 删除“只有 Up/Down 变化才产生日志”的 `AwdServiceStateTransition` 兼容逻辑。

### AWDP

- 六类 outcome 映射固定为：`DefenseSucceeded => Correct`、`ExploitSucceeded => Wrong`，
  其余四类均为 `Rejected`；所有六类事实状态均为 `Completed`。
- `AwdpFixResolved` 使用 schemaVersion 1 的强类型 Payload，包含 fact、patch、runtime、team、
  challenge、outcome、稳定失败码和 resolvedAt。
- Payload 禁止写入明文 Flag、Patch 内容、Token、密码或 Runner stderr。
- 内部 Checker 回调只接受当前三个业务枚举值，不保留旧字符串别名。

## 变更清单

- 领域/应用：`AwdpFixOutcome` 六类映射、版本化事件 Payload、删除旧状态转换器。
- Infrastructure：AWD 超时事实收敛、AWDP 结果和事件写入、排行榜 Checker Fact 过滤。
- API：移除 AWDP 回调旧协议别名；若 OpenAPI 发生变化则重新生成 TypeScript SDK。
- 测试：六类映射、事件 Payload 安全性、每次 Checker 一条事实、回调重投幂等、最新事实排序。

## 退出门禁

- [x] 每次健康/不健康/超时 Checker 执行都恰好对应一条已完成 GameplayFact。
- [x] AWDP 六类 outcome 均按权威表映射。
- [x] `AwdpFixResolved` Payload 版本化、强类型且不含敏感值。
- [x] AWD 与 AWDP 计分仅使用 GameplayFact。
- [x] 定向测试、Release build、相关真实 PostgreSQL/Wolverine 集成测试通过。
- [x] OpenAPI/SDK（如变化）生成幂等，前端测试/typecheck/build 和 `git diff --check` 通过。

## 验证证据

- Release solution build：0 warning / 0 error。
- 非 Integration TUnit：896/896；阶段 6 单元门禁：9/9。
- 真实 PostgreSQL/Wolverine 定向门禁：AWDP 非零 Checker 退出与 AWD 每次执行独立事实 2/2；
  `WolverineTransactionalOutboxTests` 10/10。
- ClientApp：275/275，2029 assertions；typecheck 与 production build 通过。
- OpenAPI 与生成 TypeScript SDK 连续生成两次 SHA-256 一致；生成文件未手工修改。
- `git diff --check` 通过，仅有仓库既有 Windows 行尾转换提示。
- 全量 TUnit 共 1074 项：1068 通过、4 失败、2 跳过。4 项仍断言阶段 8/9 明确删除的
  scheduled successor、AWDP 全表轮询与 `LeaderboardDirty` 扫描/即时投影旧语义；Kubernetes、
  Libvirt 两项因未配置外部环境跳过。它们未被误报为阶段 6 通过项，也不阻塞本阶段退出门禁。
- `dotnet ef migrations has-pending-model-changes` 当前返回 model drift；这是阶段 2—6 已删除字段而
  阶段 10 尚未按权威顺序重建单一 InitialBaseline 的预期状态，不允许在本阶段手改 migration 或 snapshot。

## 当前阶段边界

本阶段不改 Wolverine 队列拓扑、Singular Agent 或排行榜事件驱动失效；这些分别属于阶段 7、8、9。
阶段 5 已知的四项旧调度/投影断言仍必须留到其权威阶段替换，不能在本阶段伪造通过。
