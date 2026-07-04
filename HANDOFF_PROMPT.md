# 综合渗透题功能交接提示词

你是接手 NoCTF 综合渗透题功能的实现 Agent。请先阅读 `E:\SourceCode\NoCTF\TODO.md`、`README.md`、`docs/architecture.md`、`docs/handoff.md`、`docs/game-modes.md`、`docs/development.md`。

重点查看：

- `backend/src/NoCTF.Core/Entities.cs`
- `backend/src/NoCTF.Infrastructure/ApplicationDbContext.cs`
- `backend/src/NoCTF.API/Endpoints/Competitions/SubmitFlagEndpoint.cs`
- `backend/src/NoCTF.API/Endpoints/Competitions/ChallengeInstanceEndpoints.cs`
- `backend/src/NoCTF.API/Endpoints/Admin/CreateChallengeEndpoint.cs`
- `backend/src/NoCTF.API/Endpoints/Admin/CompetitionChallengeEndpoints.cs`
- `backend/src/NoCTF.Plugins.CTF/CtfGameMode.cs`
- `backend/src/NoCTF.Container.Docker/DockerManager.cs`
- `backend/src/NoCTF.Container.Docker/DockerComposeRunner.cs`
- `backend/src/NoCTF.Runner.Client/RunnerContracts.cs`
- `frontend/src/components/game/ChallengeModal.vue`
- `frontend/src/components/admin/ChallengeTemplateForm.vue`
- `frontend/src/views/admin/AdminCompetitionDetailView.vue`
- `frontend/src/api/noctf.ts`

目标：按 `TODO.md` 实现“综合渗透题 / Penetration Challenge”。它是 CTF/Jeopardy 下的新题型，不是 AWD、AWDP、KOH 或队伍互攻模式。每个队伍只攻击自己的授权靶场实例；一道题支持多容器、多网络、多阶段 Flag、动态 Flag、队伍级实例启动/停止/重置/销毁、阶段计分和审计。

关键约束：

1. 不新增队伍互相攻击逻辑。
2. 不实现服务可用性计分、补丁上传、防守得分或占领持续得分。
3. 不破坏普通 CTF、普通动态容器、AWD、AWDP、KoH。
4. 不把多容器实例塞进 `AwdGameBox`；新增综合渗透实例表。
5. 必须调整 `Submission` 唯一索引以支持同题多阶段正确提交。
6. 必须保持插件架构：综合渗透业务在 `NoCTF.Plugins.Penetration`，`NoCTF.API` 不得直接引用该插件项目。
7. API 只能依赖 `NoCTF.PluginBase` / `NoCTF.Application` 的通用题型 registry，不能硬编码 penetration 业务分支。
8. 管理端 API 必须校验比赛级 `CanManageCompetitionAsync`。
9. 选手端 API 必须只允许访问本队实例和本队动态 Flag。
10. 动态 Flag、审计日志、容器日志不得泄露明文 Flag。
11. MVP 使用 Docker Compose；Kubernetes Provider 当前未实现，不要宣称支持。
12. 每个实现步骤都要补测试或说明无法测试的原因。

建议从 `TODO-1` 到 `TODO-6` 开始，先完成数据模型、迁移和提交唯一索引调整；然后先做 `TODO-7` 的通用题型插件扩展点，再实现 `TODO-8` 到 `TODO-11` 的综合渗透插件服务；API、前端、文档和测试按 `TODO.md` 的优先级推进。

完成每个阶段后运行：

```powershell
dotnet build backend/NoCTF.slnx
dotnet test backend/tests/NoCTF.Tests
cd frontend
bun run build
```

如果修改了后端 DTO 或新增 API，请启动后端后运行：

```powershell
cd frontend
bun run fetch-openapi
bun run generate-api
```

最终交付请说明已完成的 TODO 编号、修改文件、测试结果、剩余风险。
