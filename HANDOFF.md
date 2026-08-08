# NoCTF 数据模型重构交接

## 当前状态

- 已用 EF CLI 重建 `InitialBaseline`，业务 schema 为计划中的 17 张表。
- API、测试项目当前均可编译；EF model snapshot 与当前模型无 pending changes（提交前再次验证）。
- OpenAPI 已重新导出，Nuxt 生成客户端已更新。
- 根 `AGENTS.md`、`CONTEXT.md` 与数据库/API/消息/计分/存储文档已同步。
- Owner 明确本次提交不以完整测试通过为门槛；最后一次运行结果为 602 通过、85 跳过、6 失败，失败包含旧 OpenAPI 路由计数/清单断言及本机 Docker 不可用。Build 与 EF drift 检查通过。

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

1. `NoCTF.Tests.csproj` 暂时排除了明确绑定旧表/旧协议的 21 个集成测试文件。已经恢复其余测试编译并新增 17 表 PostgreSQL Testcontainers 验收，但仍需把排除项逐项改写成新 Event/Notification/File/Token 模型测试后移除排除配置。
2. File 上传流程已有不可变 File 与 `CleanupFile(FileId)`，但 ChallengeAttachment、PatchUpload 等旧用例仍有“先上传对象、再创建 File 行”的路径；需统一到计划中的临时文件 → File 行 → 最终对象 → 建立引用补偿流程。
3. HintUnlock 与 ManualAdjust 已共享 Team/Competition advisory lock，但 HintUnlock 的 authoritative score 仍应进一步统一复用四种模式的正式排行榜投影，并补并发/rejudge Testcontainers 测试。
4. Maintenance `SingularAgent` 使用 Wolverine 6.21 的 `AddSingularAgent<T>()`（该版本没有计划文本中的 `EnableNodeAgentSupport()` 扩展）；仍需补多 Worker 故障转移与 500 条续页测试。
5. 需要继续检查 File 清理的失败重试、共享引用和 DataExport 过期场景，以及 Notification 动态受众/递归分页的完整并发测试。

## 工作区保护

- `.webbridge-tmp/`、`PLAN.md`、`backend/src/NoCTF.API/storage/` 是本地/用户内容，不应加入提交。
- 不恢复旧 migration、旧表或兼容层；migration 与 snapshot 只能通过 EF CLI 修改。
