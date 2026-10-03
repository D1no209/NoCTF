# 数据模型与 Wolverine 简化：阶段 0 基线

## 源码基线

- 日期：2026-08-23
- 分支：`codex/data-model-wolverine-simplification`
- main/origin/main：`8e366fbda160e24d5142d54f1151bf0024950dc1`
- 开始前未提交内容：用户提供的 `specs/data-model-wolverine-simplification.md` 和配套 `specs/README.md` 修改；均作为本任务输入保留。
- .NET SDK：`10.0.300`
- Bun：`1.3.14`
- Docker：`28.5.1`

## 当前数据库与迁移基线

迁移前模型有 16 张业务表，含即将删除的 `data_exports`。当前 migration 链：

1. `InitialBaseline`
2. `AddCompetitionTracks`
3. `PracticeMode`
4. `ChallengeFlagMatchKind`
5. `CustomTitle`
6. `AwdpContinuousRuntime`
7. `BindAwdpFixTarget`

目标新基线严格为 15 张业务表，删除 `data_exports`，Wolverine 自有表和 EF history 不计入业务表数量。阶段 10 前不得手改或提前压平 migration。

## 契约基线

- OpenAPI：
  - `backend/artifacts/openapi/swagger.json`
  - `backend/src/NoCTF.API/wwwroot/openapi/v1.json`
  - SHA-256：`2A34CE9D1F95B208A52F93FD36CF56BF1294A0A2F9E23613D1A6E12880A08FC6`
- 生成 TypeScript SDK：
  - `backend/src/NoCTF.API/ClientApp/app/api/sdk.gen.ts`
  - SHA-256：`CF04870E40E567353D2205AA96978446127E8CD0FE6E5138C1DC5E61FDC8938F`
- TUnit 发现测试：`1117`

阶段 0 不改业务 HTTP 契约，不导出 OpenAPI、不生成 SDK。

## 当前 Wolverine 拓扑

- PostgreSQL persistence/transport 与 EF transactional outbox 已启用。
- Worker 已按 `control`、`gameplay`、`projection`、`background` 四类持久队列监听并使用 durable inbox。
- Runner 当前同时监听 pool queue 与 node queue。
- 当前维护 Agent 仍派发旧 Dirty 排行榜扫描及部分旧周期工作；后续阶段按事实重建。
- 当前消息路由仍含 `GenerateDataExport`、`ExpireDataExport`、`PurgeDataExport`、`RefreshDirtyLeaderboards` 等待删除消息。
- 当前尚未全局启用 `IdAndDestination`，fan-out 和 Sticky endpoint fail-fast 尚未进入业务拓扑。

## 分阶段迁移清单

| 阶段 | 字段/类型 | 接口/消息 | 关键测试 |
|---|---|---|---|
| 1 | 所有持久化 Revision/ExpectedRevision/ConcurrencyToken | 所有写 DTO、command、Store、409 conflict | LWW、业务唯一冲突、OpenAPI/SDK 无 revision |
| 2 | User/Competition/Team/Challenge 隐私与核心字段 | 对应管理/公共查询 | PostgreSQL 约束、隐私投影 |
| 3 | Notification.ThreadRootId/ReplyToId | 咨询创建、回复、关闭、查询 | 线程原子性、权限、并发限额 |
| 4 | 删除 DataExport | 同步流式导出 endpoint；删除三类导出消息 | 鉴权、中断、大文件、无残留对象 |
| 5 | Runtime 最小字段；Reset 新 UUID | Dispatch/Claim/Stop/Reset；节点 Sticky 直投 | claim 幂等、节点路由、清理与重投 |
| 6 | AWD 每次 Checker 独立事实；AWDP 强类型结果 | Checker/Fix result payload | 模式语义、版本化 payload、幂等 |
| 7 | 无业务表新增 | competing/fan-out/Sticky/IdAndDestination | 每组一次、每订阅者一次、fallback fail-fast |
| 8 | 删除 next-run/scheduled 状态 | Singular Agent durable dispatch | 唯一 Agent、failover、不补跑 |
| 9 | 删除 LeaderboardDirty | 事件驱动 invalidation/coalescing/project | 500ms、缓存重建、乱序保护 |
| 10 | 单一 EF InitialBaseline | 无 HTTP 变化 | 15 表、model drift、恢复演练 |
| 11 | 最终协议 | OpenAPI/SDK/前端 | 全量矩阵、生成幂等、E2E |

## 文档冲突处理

- 根 AGENTS、ClientApp AGENTS、database、messaging、scoring、runtime、API、realtime 与本计划已同步目标语义。
- 尚保留旧字段细节的 Runtime/测试/ADR/历史实施提示仅作为迁移删除清单或归档证据，不再具有目标规范效力，并须在对应业务阶段重写或删除。
- [`specs/README.md`](README.md) 将新规范列为最高阅读优先级；专题文档不得用优先级说明长期维持矛盾，阶段 11 必须归零所有迁移标记。

## 阶段 0 验证

已通过：

- locked restore；
- Release build（0 warnings，0 errors）；
- Wolverine 6.29.2 真实 PostgreSQL Spike（3/3）；
- 既有 EF outbox/rollback/dead-letter 和 Singular failover 定向集成测试；
- Release 完整 TUnit/Architecture/Integration：1117 total，1115 passed，0 failed，2 skipped；
- 前端 Bun 测试：274/274；
- Nuxt typecheck、production build、static generate；
- EF `has-pending-model-changes`：无漂移；
- analyzer verify 与 `git diff --check`。

两个跳过项均依赖当前机器没有配置的真实外部目标：Kubernetes 和 Libvirt。它们没有被报告为通过，
且不阻塞只验证 Wolverine/PostgreSQL 机制的阶段 0。前端仓库没有 `lint` script，因此本阶段明确记为
“不可执行”，未用其他命令冒充 lint。

阶段 0 最终文档扫描未发现仍具有规范效力的冲突；搜索命中的旧术语只存在于权威规范的删除清单、
禁止性描述、迁移前基线和历史 HANDOFF 记录中。

## 切换与回滚评审

[`data-model-wolverine-cutover.md`](data-model-wolverine-cutover.md) 已明确停机、备份、转换、验证、
失败回滚和角色责任。阶段 0 的方案评审门禁已关闭；生产执行门禁仍保持关闭，直到项目负责人指定
真实责任人并签署备份/恢复演练结果。本任务没有操作生产数据库。

## 回滚点

阶段 0 只修改规范、计划、HANDOFF 和 Spike 测试；不修改业务模型、数据库或 HTTP 契约。回滚方式是 revert 阶段 0 提交，不产生数据恢复动作。
