# 阶段 3：Notifications / Questions 线程化

## 实施基线

- 分支：`codex/data-model-wolverine-simplification`
- 前置提交：`371fd93d refactor(model): simplify core identity and competition state`
- 权威规范：`docs/data-model-wolverine-simplification.md` 第 5.8、9、12 节
- 数据库迁移：本阶段不生成或修改 migration / snapshot；统一留到阶段 10 通过 EF CLI 重建初始基线。

## 旧模型问题

- `ReplyToId` 存在唯一约束，回复被强制串成严格线性链。
- 线程成员、回复上下文和写入顺序共用同一字段，查询必须递归遍历。
- 当前状态依赖链尾节点，无法明确区分状态语义与普通线程节点。
- Question API 以通用 `Id` 暴露根通知标识，没有表达它是线程根。

## 本阶段修改范围

### 字段与约束

- `Notification.ThreadRootId : Guid?`，数据库列 `thread_root_id`。
- Question 根通知：`ThreadRootId = null`。
- 回复与状态节点：`ThreadRootId = root.Id`。
- `ReplyToId` 保留为可空回复上下文，不再唯一，也不参与线程成员判定。
- 根通知通过两个 Restrict 自引用外键保持不可被线程节点级联删除。

### 类型与接口

- `NotificationView` / `NotificationResponse` 增加 `ThreadRootId`。
- `CompetitionQuestionView` / `CompetitionQuestionResponse` 使用 `ThreadRootId` 表示咨询线程。
- OpenAPI 与 TypeScript SDK 由工具重新生成，禁止手工编辑生成文件。

### 查询与业务规则

- 线程查询只使用 `ThreadRootId`，按 `(SentAt, Id)` 稳定排序。
- 当前状态取最后一个具有状态语义的 `Message` 或 `QuestionStatusChanged` 节点；没有节点时使用根状态。
- 并发回复均 append；不恢复 Revision 冲突。
- 保持来源队伍、题目所有者、裁判、管理员和观察者权限边界。

### 测试

- 真实 PostgreSQL 验证并发回复均保留。
- 相同时间戳下按 Id 稳定排序。
- 当前状态确定且不受非状态节点影响。
- 私有咨询正文不向其他队伍泄露。
- 根通知受到外键 Restrict 保护。
- OpenAPI / SDK 生成幂等，前端测试、typecheck、production build 通过。

## 验证证据

- Release solution build：0 warning / 0 error。
- 定向 TUnit：咨询规则 15/15、通知 feed 2/2、Development 通知读取 1/1、
  咨询限流 HTTP 1/1、咨询分页 HTTP 1/1。
- 真实 PostgreSQL Testcontainers：`CompetitionQuestionPersistenceTests` 2/2；其中新增用例并发写入
  Owner 与 Manager 的同时间戳回复，验证 `(SentAt, Id)` 顺序、来源队伍隐私和根记录不可删除。
- ClientApp：`bun test` 275/275、`bun run typecheck`、`bun run build` 通过；build 仅有既有
  chunk 大小、插件耗时和第三方 trailing-slash deprecation warning。
- OpenAPI 导出与 `bun run api:gen` 二次执行前后四个生成产物 SHA-256 完全一致。
- `git diff --check` 通过；本阶段没有 migration 或 model snapshot 变化。

## 已知阶段性阻塞

- `NotificationReaderPersistenceTests` 等仍调用 `Database.MigrateAsync()` 的旧基线测试会触发 EF
  `PendingModelChangesWarning`。这是阶段 1—9 模型已按权威规范演进、而 migration 必须到阶段 10
  才能由 EF CLI 统一重建导致的预期门禁阻塞；不得以手改 migration、snapshot 或改用
  `EnsureCreated` 冒充迁移验证。阶段 10 重建基线后必须恢复并通过这些测试。

## 退出门禁

- 不新增 Question、Conversation、Participant 或 ReadState 表。
- 不存在依赖 `ReplyToId` 的递归线程成员查询。
- 并发 append、稳定顺序、确定状态与隐私测试通过。
- Release build、定向 TUnit / Testcontainers、前端门禁、`git diff --check` 通过。

## 回滚点

本阶段尚未生成数据库迁移，也不会应用到共享环境。代码可整体回退至前置提交；阶段 10 以前不得将中间 EF 模型应用到任何共享或生产数据库。
