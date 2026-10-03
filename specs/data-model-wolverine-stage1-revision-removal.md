# 数据模型与 Wolverine 简化：阶段 1 Revision 协议删除

## 实施前基线

- 分支：`codex/data-model-wolverine-simplification`
- 阶段 0 提交：`1a195f6c`
- 业务源码扫描（排除 migration、生成 SDK、OpenAPI、bin/obj）命中：
  - Domain：5 个文件；
  - Application：21 个文件；
  - Infrastructure：38 个文件；
  - API：27 个文件；
  - Worker/Runner/GameModes：12 个文件。
- 阶段 0 的 Release build、1117 项后端测试、274 项前端测试、EF model drift、
  OpenAPI/SDK 基线均已通过或按外部环境准确标记跳过。

阶段 1 会产生预期中的 EF 模型漂移；现有 migration 与 snapshot 在阶段 10 由 EF 工具删除并
重建单一 `InitialBaseline`。在此之前不得手工修改生成文件，也不得把旧 migration 当作当前领域
模型仍允许 Revision 的依据。

## 删除范围

### 持久化字段

- `User.ConcurrencyVersion`；
- `Team.ConcurrencyVersion`、`Team.CriticalSectionVersion`；
- `Challenge.Revision`；
- `CompetitionChallenge.Revision`、`CriticalSectionVersion`、AWD 调度 source revision；
- `Competition.PermissionRevision`、`TrackConfigurationRevision`、`ConfigurationRevision`、
  `LeaderboardVisibilityRevision`；
- `PlatformSettings.Revision`；
- `RuntimeInstance.ProcessingVersion` 与三个 source revision。

### Application 与消息

- command/query/store 的 `ExpectedRevision`、`ExpectedPermissionRevision`、
  `ExpectedTeamVersion`、`ExpectedProcessingVersion`；
- Runtime、AWD、AWDP、KoH、Notification 消息中的 processing/source revision；
- revision mismatch/retry/result/failure 分支；
- Runtime callback token 中只为伪并发控制存在的 processing-version claim。

### HTTP 与前端

- 写请求、读响应、Validator、409 业务码中的 Revision 并发协议；
- OpenAPI 与生成 TypeScript SDK 中对应字段；
- 前端 revision state、隐藏字段、RevisionConflict 专用刷新提示。

业务唯一冲突继续使用强类型 409，例如题目已经加入比赛、顺序冲突、资源标识冲突；不得把它们
重新映射成 RevisionConflict。

## 保留范围

- JWT `TokenVersion`；
- JSON `schemaVersion`；
- 版本化事件 Payload；
- 榜单 Schema/Catalog/Snapshot 的外部协议版本；
- 容器、Checker 与其他外部协议明确拥有的版本字段。

公开资源 cache-busting 不再依赖平台实体 Revision；如仍需缓存失效，应使用文件不可变标识或
更新时间等自然事实。

## 并发替代

- 普通管理更新采用 PostgreSQL last-write-wins；后提交事务覆盖先提交事务。
- 业务临界区使用 PostgreSQL 行锁或 transaction-scoped advisory lock，不新增持久化版本列。
- Runtime/消息幂等使用 Runtime UUID、GameplayFact UUID、状态机和 Wolverine durable inbox，
  不把 `ProcessingVersion` 换名为 Epoch/Sequence 后重新引入。
- append-only 实体继续只允许创建；删除并发协议不开放 update/delete 路径。

## 定向测试与退出门禁

1. 两个独立 DbContext 对同一可变记录提交不同值，证明后提交值最终生效。
2. PostgreSQL 行锁/事务临界区仍阻止业务不变量被并发突破。
3. CompetitionEvent、Notification reply 等 append-only/追加模型没有获得更新入口。
4. API、OpenAPI 与 SDK 不包含 revision 并发字段。
5. 业务唯一冲突仍返回准确强类型失败码。
6. JWT token、定义 schema、事件 payload、榜单协议版本测试继续通过。
7. `rg` 门禁在业务源码、API、前端和测试中归零；旧 migration 仅作为阶段 10 前的工具所有
   历史制品保留并明确排除。

## 完成证据

- Release solution build：0 warnings，0 errors。
- Unit 与 Architecture：`899/899` 通过；其中持久化模型架构门禁验证业务实体没有 EF
  concurrency token，参数化行锁 SQL 均有逐项白名单和用途说明。
- 真实 PostgreSQL 定向验证：
  - Ownership transfer、competition configuration/permission、resource-manager role、template mode
    invariant：`16/16`；
  - competition event/question/management/practice 与 Application 管理规则：`15/15`；
  - Challenge attachment 的 last-write-wins 更新时间：`1/1`。
- OpenAPI/协议定向测试：`18/18`；重新导出 OpenAPI 并生成 TypeScript SDK 后，6 个契约制品
  SHA-256 全部不变。
- Frontend：`bun test` 为 `273/273`，typecheck 与 production build 通过；构建只保留既有的
  chunk size/plugin timing/第三方依赖提示。
- 移除协议扫描排除 tooling-owned migrations/snapshot、生成目录和历史说明后无生产实现命中；
  callback token 不再签发或要求 `processing_version` / `runtime_processing_version`。
- `git diff --check` 无 whitespace error。

完整 Integration 套件在本阶段不可宣称通过：仍调用 `MigrateAsync` 的旧模型测试会因阶段 1
产生的预期 EF model drift 失败。权威阶段顺序要求 migration/snapshot 只能在阶段 10 通过 EF
工具重建；本阶段没有批量改写无关 fixture，也没有伪造 migration 已同步。阶段 1 自身涉及的
关系型不变量均使用 `EnsureCreated` 的可丢弃 Testcontainers PostgreSQL 完成定向验证。

## 退出结论

阶段 1 最小闭环与本阶段门禁已完成。下一阶段是阶段 2“核心实体和隐私模型”；在阶段 10 前，
EF model drift 是已记录的阶段性状态而非可部署 migration。

## 数据迁移影响与回滚

本阶段只形成新的目标模型和契约，不执行生产迁移。生产列删除、数据转换和单一基线在阶段 10/11
按切换方案执行。回滚必须整体 revert 阶段 1 提交，不能只恢复 HTTP 字段而保留无 Revision 的
数据库语义。
