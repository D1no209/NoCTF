# NoCTF PostgreSQL 数据模型（目标 InitialBaseline）

本文描述迁移完成后的数据库目标；字段级不变量以
[数据模型与 Wolverine 调度简化权威规范](data-model-wolverine-simplification.md) 为准。
当前迁移前结构、哈希和差异只记录在阶段 0 基线文档，不得作为新增兼容层的依据。

## 数据边界

- PostgreSQL 是业务事实源；Redis 只保存可丢失缓存、限流、SignalR backplane 与 Runner 心跳/容量索引。
- 主键由应用生成 UUIDv7；时间使用 UTC `timestamptz`；领域枚举存 `smallint`。
- 所有业务外键使用 `ON DELETE RESTRICT`，硬删除按应用定义的引用顺序执行。
- `jsonb` 必须符合带 `schemaVersion` 的强类型契约；未知版本拒绝写入。
- 文件元数据创建后不可变。替换文件时创建新 File，提交业务引用后再通过 durable `CleanupFile` 清理无引用旧对象。
- EF migration 与 model snapshot 只能由 `dotnet ef migrations add/remove` 生成，不得手工编辑。
- Wolverine 自有 Inbox、Outbox、transport、node assignment 与 dead-letter 表不计入业务表数量，但必须纳入备份、恢复与监控。

## 唯一业务表清单

最终基线正好包含以下 15 张业务表：

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

`data_exports` 不存在；导出通过有权限、有限大小和超时的同步流式端点完成。不得为调度、
队列、咨询、导出或 Runtime 操作新增业务表。

## 并发与版本

可变记录采用 last-write-wins。业务表、Application、API、OpenAPI 与 SDK 均不得出现持久化
`Revision`、`*Revision`、`ExpectedRevision`、`ConcurrencyVersion`、
`CriticalSectionVersion` 或 `ProcessingVersion` 协议，也不得换名后重新引入。

仍保留的版本概念只有：

- `User.TokenVersion`：JWT 全局失效；
- JSON `schemaVersion`：题目定义、配置、规则与事件 Payload 结构；
- 排行榜协议版本：客户端识别传输/缓存结构。

数据库继续使用主外键、业务唯一索引、Check Constraint、软删除规则、不可变记录更新禁令、
幂等业务键以及 Wolverine Inbox/Outbox 保护真正的不变量。

## 核心实体目标

### Competition 与 CompetitionEvent

Competition 保存当前配置、协作者 UUID 数组和生命周期入口；不保存 Revision、运行累计、
排行榜 Dirty/Snapshot 或周期 next-run 字段。生命周期、榜单可见性与需要追溯的管理事实写入
append-only `competition_events`，Payload 使用强类型版本化 JSON。

### Team、Challenge 与 CompetitionChallenge

队伍成员保存在无重复 `member_ids` 数组，Captain 必须属于数组；不建立成员或邀请关系表。
Challenge 是跨比赛模板，CompetitionChallenge 是比赛实例。二者不保存 Revision；比赛题目
不保存 AWD 调度游标或其他 next-run 状态。

### RuntimeInstance

Runtime 只保存 Competition/CompetitionChallenge/Team/Purpose/GameplayFact 关系、provider 与
runner 事实、状态/失败码、provider receipt、公开 URL/端口以及生命周期时间。不得保存
generation、replacement 链、pool、定义 snapshot/revision、checker deadline、next checker、
last checker sequence 或其他调度栅栏。每次创建和 Reset 都插入全新 UUID；旧实例保留历史。

### Notification 咨询线程

Notification 是 append-only 动态受众消息。Question 根的 `thread_root_id` 为空；回复和状态事件
把根通知 Id 写入 `thread_root_id`。`reply_to_id` 只是可空、非唯一的回复上下文，不决定线程成员。
线程按 `(sent_at, id)` 排序；并发回复全部 append，不产生 Revision conflict。

### GameplayFact

GameplayFact 是比赛行为事实。每次 AWD Checker 执行建立独立 Fact；AWDP Fix 结果使用强类型
结果/失败枚举，并由版本化事件 Payload 承载跨进程结果。Fact 不保存累计分、调度 next-run、
通用并发版本或派生排行榜快照。

### File 与同步导出

File 保存不可变对象元数据。同步导出直接从 PostgreSQL 流式生成响应，不创建 File 或后台任务；
客户端中断必须取消查询并释放连接，超过大小/时限明确失败且不退化为异步导出。

## 基线重建与生产切换

阶段 10 才允许在可丢弃开发数据库上移除旧 migration 并由 EF CLI 生成一个新的
`InitialBaseline`。生产切换必须使用单独的切换计划，包含停机窗口、旧 schema 与对象存储备份、
数据转换、表/约束/计数验证、失败回滚和明确负责人；禁止用清库代替生产数据迁移。
