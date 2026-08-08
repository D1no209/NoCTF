# NoCTF 数据模型重构交接

## 当前状态

- 已用 EF CLI 重建 `InitialBaseline`，业务 schema 为计划中的 17 张表。
- API、测试项目当前均可编译；EF model snapshot 与当前模型无 pending changes（2026-08-08 再次验证）。
- OpenAPI 已重新导出，Nuxt 生成客户端已更新。
- 根 `AGENTS.md`、`CONTEXT.md` 与数据库/API/消息/计分/存储文档已同步。
- Owner 明确本次提交不以完整测试通过为门槛；最后一次运行结果为 603 通过、101 跳过、6 失败，失败数与导入基线一致，包含旧 OpenAPI 路由计数/清单断言及本机 Docker 不可用。Build 与 EF drift 检查通过。

## 2026-08-08 续推记录

- `7f94be24`：合并并精确同步 `new-frontend@5631584f40b1cff9df3c1c16e8b0c76f05f9b972`，保留 17 表新基线与 Nuxt ClientApp。
- `5e41b485`：修复 Nuxt 与合并后强类型契约漂移；Team API 增加生成式 `avatarUrl`，重新导出 OpenAPI 并生成 TypeScript SDK。
- `6a33a6ac`：将邮件验证配置和密码重置持久化测试改写到 `platform_settings` 与统一 `account_tokens`，排除文件由 21 个降至 19 个。
- `ea8c004d`：将用户物理删除/匿名化的操作人、原因、时间和动作写为管理员受众的不可变 Notification 原始事实，管理审计直接投影该事实；修复管理员通知角色判断，并将用户关联 Notification 纳入删除影响预览。对应两项集成测试已恢复，排除文件由 19 个降至 17 个。
- `15d6fc50`：将 Competition Event、Lifecycle、Leaderboard Visibility 共 6 个持久化场景改写到不可变 `competition_events`，排除文件由 17 个降至 14 个；同时修复暂停生命周期 payload 使用默认大小写敏感 JSON 选项导致无法恢复暂停起点的问题。
- `0ee346b8`：恢复作弊事件、队伍封禁申诉和私密问答 3 个持久化场景，排除文件由 14 个降至 11 个；Event 记录器会把未被选为 Subject 的用户/队伍/比赛题目等首要引用写入 Related，并拒绝不成对的显式 Related 字段。问答测试按当前产品语义移除旧公开投影，公开答复继续由 Hint 或比赛公告承担。
- `87fb8e1c`：恢复 AWDP Fix admission 与 Hint unlock 两个持久化场景，排除文件由 11 个降至 9 个；Patch 测试使用统一不可变 File 引用，Hint unlock 测试验证异步 Queued Submission 与 `EvaluateSubmission` 投递，并确认评测完成前不产生 ScoringEvent 或排行榜失效。
- 新增协议枚举 `NotificationKind.UserAccountLifecycleChanged`；OpenAPI 与 Nuxt SDK 已同步。没有新增数据表或 migration。
- 本轮验证：`dotnet build backend/NoCTF.slnx --no-restore` 为 0 警告/0 错误；EF 无 pending model changes；OpenAPI 导出、Nuxt typecheck 与 production build 通过；全量测试 710 项，603 通过、101 跳过、6 个基线失败。恢复的 PostgreSQL 集成场景已进入测试集，但本机 Docker 不可用时按测试设施策略跳过。
- 未推送、未部署；原工作区 `TODO.md`、`PLAN.md` 及其他用户/协作者未提交内容保持不动。

## 已落地的主要能力

- `CompetitionEvent` 合并生命周期与排行榜可见性事实。
- `platform_settings` 合并邮件设置；`account_tokens` 合并验证与重置 Token。
- Notification 使用 Source/Target/Kind/Content/Related/ReplyTo，并以动态受众和线性线程承载 Question/Announcement。
- Hint 与 Runtime published ports 改为所属实体的 JSON；旧子表已删除。
- HintUnlock 与 ManualAdjust 均走 Submission + ScoringEvent；ManualAdjust delta 保存于 `SubmittedFlag`。
- `files` 成为唯一文件元数据表；User/Team Avatar、Competition Poster、Platform Logo、附件、Patch、DataExport 使用 FileId。
- 新增 Team Avatar、Competition Poster、比赛公告、Notification thread、ManualAdjust endpoint。
- Wolverine `SingularAgent` 投递维护 tick；删除 durable maintenance schedule 业务表。

## 验证入口

```powershell
dotnet build backend/NoCTF.sln --no-restore
dotnet test backend/tests/NoCTF.Tests/NoCTF.Tests.csproj --no-restore
dotnet ef migrations has-pending-model-changes `
  --project backend/src/NoCTF.Infrastructure/NoCTF.Infrastructure.csproj `
  --startup-project backend/src/NoCTF.API/NoCTF.API.csproj
dotnet run --project backend/src/NoCTF.API/NoCTF.API.csproj --no-build -- --export-openapi
```

## 后续必须继续处理

1. `NoCTF.Tests.csproj` 暂时排除了明确绑定旧表/旧协议的 9 个集成测试文件。已经恢复其余测试编译并新增 17 表 PostgreSQL Testcontainers 验收，但仍需把排除项逐项改写成新 Event/Notification/File/Token 模型测试后移除排除配置。
2. File 上传流程已有不可变 File 与 `CleanupFile(FileId)`，但 ChallengeAttachment、PatchUpload 等旧用例仍有“先上传对象、再创建 File 行”的路径；需统一到计划中的临时文件 → File 行 → 最终对象 → 建立引用补偿流程。
3. HintUnlock 与 ManualAdjust 已共享 Team/Competition advisory lock，但 HintUnlock 的 authoritative score 仍应进一步统一复用四种模式的正式排行榜投影，并补并发/rejudge Testcontainers 测试。
4. Maintenance `SingularAgent` 使用 Wolverine 6.21 的 `AddSingularAgent<T>()`（该版本没有计划文本中的 `EnableNodeAgentSupport()` 扩展）；仍需补多 Worker 故障转移与 500 条续页测试。
5. 需要继续检查 File 清理的失败重试、共享引用和 DataExport 过期场景，以及 Notification 动态受众/递归分页的完整并发测试。

## 工作区保护

- `.webbridge-tmp/`、`PLAN.md`、`backend/src/NoCTF.API/storage/` 是本地/用户内容，不应加入提交。
- 不恢复旧 migration、旧表或兼容层；migration 与 snapshot 只能通过 EF CLI 修改。
