# 数据模型与 Wolverine 简化：阶段 10 单一 EF 基线

## 范围与授权边界

阶段 10 只重建 EF Core 源码迁移基线并验证空库、约束、索引与 Wolverine 自有 schema。
全部操作均在本机可丢弃 PostgreSQL 容器 `noctf-stage10-pg-20260824` 中完成；没有连接、修改或清理
生产数据库、生产 Wolverine 队列、对象存储，也没有推送或部署。

父提交为 `681138cc`。生产切换仍受
[生产切换与回滚 Runbook](data-model-wolverine-cutover.md) 的停机、负责人、备份、转换、验证和回滚
门禁约束；本阶段的开发库演练不能替代生产审批。

## 旧 schema 与恢复演练

在移除源码迁移前，使用 EF CLI 从旧迁移链生成 SQL，并在一次性数据库 `noctf_legacy` 应用；随后
使用 PostgreSQL custom-format backup 恢复到独立数据库 `noctf_legacy_restore`。恢复库包含旧的 16 张
业务表和 `__EFMigrationsHistory`，证明备份文件可读取、可恢复。开发期证据保存在被 `.gitignore`
排除的 `backend/artifacts/stage10/`，不会进入代码提交：

| 文件 | SHA-256 |
|---|---|
| `legacy-migration-chain.sql` | `6344DF8FC484B7EC155BA16604254D8CDA1EB7BDB469772BF2C557CDE0931366` |
| `noctf-legacy-schema.sql` | `C36EC4C202BF11298FEB61EA38851FF925CA1B3D742FEAA3F7FE0A634EC7C292` |
| `noctf-legacy.dump` | `C39AA295E56C00B15BC8C79D388B8609935250FC044A798252D628A9BB4BE19F` |

生产备份不能复用这些开发文件。生产切换必须重新冻结旧库、生成并校验整库备份、逐表计数、对象
存储清单和 Wolverine schema 快照；无法无损转换时必须暂停，不能以清库代替迁移。

## EF 工具操作

旧七个 migration 依次通过 `dotnet ef migrations remove` 逆序移除；没有用文件删除代替 EF 工具，
也没有手工编辑 migration 或 model snapshot。随后执行：

```powershell
dotnet ef migrations add InitialBaseline `
  --project backend/src/NoCTF.Infrastructure/NoCTF.Infrastructure.csproj `
  --startup-project backend/src/NoCTF.API/NoCTF.API.csproj `
  --context NoCtfDbContext `
  --output-dir Migrations
```

EF 生成 `20260824060413_InitialBaseline` 及对应 Designer/Snapshot。Release 配置下
`dotnet ef migrations list` 只返回该 migration，`dotnet ef migrations has-pending-model-changes`
返回无模型漂移。

## 空库和约束结果

新基线在空数据库 `noctf_baseline_work` 一次应用成功。public schema 除 EF 历史表外恰好包含：

1. `users`
2. `competitions`
3. `competition_events`
4. `teams`
5. `challenges`
6. `challenge_attachments`
7. `competition_challenges`
8. `challenge_flags`
9. `runtime_instances`
10. `patch_uploads`
11. `account_tokens`
12. `platform_settings`
13. `notifications`
14. `gameplay_facts`
15. `files`

数据库检查得到 15 个业务主键、36 个外键、36 个 check constraint、81 个 public index；外键删除
行为中没有 Cascade。`data_exports`、持久化 Revision/ExpectedRevision/ConcurrencyToken、
`leaderboard_dirty`、Runtime `generation`、`runner_pool`、`checker_deadline` 均不存在。

真实 PostgreSQL 集成测试使用生产 `ConfigureNoCtfPersistence` 分别启动 Api、Worker、Runner Wolverine
6.29.2 Host；`wolverine_api`、`wolverine_worker`、`wolverine_runner` 均正常建立 durable incoming、
outgoing、dead letter、node、assignment、control queue 等自有表。它们不计入 15 张业务表，但必须按
Runbook 纳入备份、恢复和监控。

## 自动化门禁

`DataModelSchemaTests` 已从 `EnsureCreated` 改为真实 `MigrateAsync`，并固定检查：

- migration 只有一个且名称以 `_InitialBaseline` 结尾；
- 15 张业务表精确匹配；
- 每张业务表都有主键，外键无级联删除；
- 已删除表和字段不存在；
- 同一 PostgreSQL 数据库可同时承载业务基线与三套 Wolverine role schema。

验证结果：Release 测试项目构建 0 warning/0 error；两项真实 PostgreSQL/Testcontainers schema 测试
与一项 migration 架构测试均通过；EF migration list、空库应用、model drift 和 `git diff --check`
门禁均通过。阶段 10 使用的精确开发容器 `noctf-stage10-pg-20260824` 已在验证后删除；完整全量矩阵
留在阶段 11 执行。

## 回滚点与下一阶段

阶段 10 不生成面向旧 schema 的向后兼容 migration。若生产切换失败，只能停止新应用并恢复旧应用、
切换前整库备份和匹配的旧 Wolverine schema，不能让旧应用连接已经写入新模型的数据库。

下一阶段只能进入阶段 11：统一刷新 OpenAPI/生成 SDK，清理残余旧契约和文档，递增 Alpha 版本，
运行完整 backend/frontend/integration/E2E、备份恢复、Redis loss、Worker failover 和 Runner cleanup
验证。构建全新测试环境或生产切换仍需单独授权。
