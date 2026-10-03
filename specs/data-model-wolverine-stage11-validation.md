# 数据模型与 Wolverine 简化：阶段 11 契约与全量验证

## 范围与授权边界

阶段 11 收敛契约、生成产物、运行时恢复边界、全量测试和发布资料。父提交为 `4f3d3d99`，
工作分支为 `codex/data-model-wolverine-simplification`，版本递增为 `0.1.0-alpha.80`。

本阶段只在本机源码、Testcontainers 和可丢弃开发依赖中执行。没有推送、部署、连接或修改生产数据库、
生产 Wolverine 队列、生产 Redis、生产 Runner 或对象存储。全新测试环境部署、生产备份恢复、停机切换和
Go/No-Go 仍需发布负责人另行授权，不能用本阶段的本地验证替代。

## 完成的闭环

### 契约与生成产物

- 重新导出 `backend/artifacts/openapi/swagger.json` 与
  `backend/src/NoCTF.API/wwwroot/openapi/v1.json`。
- 使用仓库脚本重新生成 TypeScript SDK；没有手工编辑生成文件。
- 连续两次导出和生成后 SHA-256 保持一致：

| 产物 | SHA-256 |
|---|---|
| 两份 OpenAPI | `60B987C74023B0290D92FEC9A972404EDD3436EA122BB9D5CF0F1E5D6440960E` |
| `app/api/types.gen.ts` | `19990EAD2EFE8810B6EE8C9CE2DE003D43D3D21D35EEA123BFC13EF34F24B041` |

### 运行时与调度收尾

- 独立 Worker 的开发宿主不再依赖 Wolverine 内部 `NodeAgentController`，调度 readiness 只读取公开的
  Wolverine 运行状态和集群调度状态；API 开发宿主显式禁用集群调度。
- Docker 并发 provision 在网络名称竞争时复用已经创建的网络，删除不存在的资源按幂等成功收敛。
- Runner 对账只遍历当前部署实际配置的容器资源池，不再把未启用的 OVA provider 当成 Docker 工作。
- AWDP 排行榜缓存的有效期被限制在当前轮次边界，不能把未结算轮次结果带入下一轮。
- KoH observation 在同一业务事务和 EF transactional outbox 中记录
  `GameplayFactAdjudicated`，事件驱动排行榜因此能够及时失效和重建。

### 模式与隐私验证

- AWDP 完整边界测试按新 Runtime UUID、旧 Flag 失效、一次性 Fix target 与强类型结果运行。
- KoH 完整边界测试确认控制地址不向管理员、匿名用户或未报名用户泄漏；已报名队伍只能读取自己的
  私有控制 Flag。
- CTF、AWD、AWDP、KoH 四种模式的完整 E2E 与 API/PostgreSQL/Redis/Worker/Runner 恢复场景均通过。

## 数据库与模型结果

- EF migration 只有工具生成的 `20260824060413_InitialBaseline`。
- `dotnet ef migrations has-pending-model-changes` 返回无模型漂移。
- 初始基线严格创建 15 张业务表：`users`、`competitions`、`competition_events`、`teams`、
  `challenges`、`challenge_attachments`、`competition_challenges`、`challenge_flags`、
  `runtime_instances`、`patch_uploads`、`account_tokens`、`platform_settings`、`notifications`、
  `gameplay_facts`、`files`。
- `data_exports`、持久化 Revision 协议、`LeaderboardDirty` 与旧 Runtime 调度列不存在。
- 旧 schema 备份恢复和新空库应用的可丢弃开发演练见
  [阶段 10 证据](data-model-wolverine-stage10-ef-baseline.md)；它不是生产备份或生产转换报告。

## 自动化验证

### 后端

```powershell
dotnet build backend/NoCTF.slnx --configuration Release --no-restore
dotnet test backend/NoCTF.slnx --configuration Release --no-build
dotnet ef migrations has-pending-model-changes `
  --project backend/src/NoCTF.Infrastructure/NoCTF.Infrastructure.csproj `
  --startup-project backend/src/NoCTF.API/NoCTF.API.csproj `
  --configuration Release --no-build
```

结果：Release build 0 warning/0 error；TUnit 共 1091 项，1089 通过、0 失败、2 跳过。两个跳过项是
未启用 `NOCTF_KUBERNETES_INTEGRATION` 的真实 Kubernetes provider 测试，以及未提供
`NOCTF_LIBVIRT_DISK_PATH` 的真实 Libvirt provider 测试；均已明确记录，未当作通过。真实
PostgreSQL、Redis、Wolverine、Docker/Testcontainers 测试在完整测试集中通过。

定向证据包括：

- `Wolverine6292TopologySpikeTests` 与 `NoCtfWolverineTopologyTests`：competing consumers、显式
  fan-out、`IdAndDestination`、Sticky 缺失 fail-fast。
- `WolverineTransactionalOutboxTests`：事务回滚不发布、durable inbox/outbox 重投、双 Worker
  Singular Agent 和 failover。
- `RuntimeReplacementCleanupFailureTests`、`RuntimeQuotaPersistenceTests` 与四模式 E2E：Reset 新
  UUID、节点直投、停止与资源清理。
- `LeaderboardProjectionPersistenceTests`、`RedisLeaderboardPublicationFenceTests` 与
  `LeaderboardProjectionMergeQueueTests`：500ms 合并、cache miss/Redis loss 重建和乱序保护。
- 同步导出、通知线程、权限与隐私的 API/真实 PostgreSQL 测试随完整测试集通过。

### 前端

```powershell
Set-Location backend/src/NoCTF.API/ClientApp
bun install --frozen-lockfile
bun run test
bun run typecheck
bun run build
bun run generate
```

结果：275/275 测试通过，共 2029 assertions；typecheck、production build 和静态 generate 通过。
仅有 chunk 大于 500 KiB 与上游依赖弃用提示，没有构建错误。`package.json` 没有定义 lint 脚本，
因此没有把不存在的 lint 门禁报告为通过。

### 生成幂等与静态门禁

```powershell
dotnet run --project backend/src/NoCTF.API/NoCTF.API.csproj `
  --configuration Release --no-build --no-launch-profile -- --export-openapi
Set-Location backend/src/NoCTF.API/ClientApp
bun run api:gen
git diff --check
```

两次生成哈希一致。OpenAPI 导出宿主在本地 Redis 未启动时会记录 FusionCache backplane 回连告警，
但契约导出正常完成且产物稳定；这不是生产 readiness 结果。

## 未执行与发布门禁

以下项目因当前权限边界没有执行，因此阶段 11 的本地实现验证完成，但生产切换 Definition of Done
尚未满足：

1. 构建发布镜像并部署到全新的共享测试环境。
2. 使用部署后的浏览器、真实 TLS、真实 Runner 池进行发布冒烟。
3. 对生产快照执行离线数据转换、逐表计数和内容哈希核验。
4. 生产停机窗口、负责人签字、Go/No-Go 和失败回滚演练。

执行这些操作前必须使用
[生产切换与回滚 Runbook](data-model-wolverine-cutover.md)，并由主线程用户明确授权。

## 恢复入口

```powershell
git switch codex/data-model-wolverine-simplification
git log --oneline -12
dotnet build backend/NoCTF.slnx --configuration Release --no-restore
dotnet test backend/NoCTF.slnx --configuration Release --no-build
Set-Location backend/src/NoCTF.API/ClientApp
bun run test
bun run typecheck
bun run build
```
