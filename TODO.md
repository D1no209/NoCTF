# 综合渗透题功能开发 TODO

## 当前实现状态（2026-07-04）

已完成或具备 MVP 骨架：

- TODO-1 到 TODO-6：综合渗透实体、DbContext、迁移、`Submission.PenetrationFlagId` 和分阶段正确提交索引已实现。
- TODO-7 到 TODO-12：题型插件 registry、`NoCTF.Plugins.Penetration`、拓扑服务、实例服务、动态 Flag 服务、阶段计分策略、Worker 过期清理和实例状态同步已实现。
- TODO-13 到 TODO-17：管理端拓扑 API、管理端实例 API、选手端详情/实例 API、选手端阶段 Flag 提交 API 已实现；比赛级管理 API 使用 `CanManageCompetitionAsync`。
- TODO-19 到 TODO-21、TODO-23、TODO-24：Docker Compose 生成、安全校验、实例状态机、重置/销毁清理、动态 Flag 环境变量注入和基础提交限流已实现。
- TODO-25 到 TODO-29：题库 Penetration 表单、比赛题目拓扑 JSON 编辑、管理端实例监控页、选手端 Penetration 面板、挑战卡片阶段进度已接入。
- TODO-30、TODO-31：主要权限边界和敏感日志脱敏已接入；动态 Flag 明文不写入提交/比赛日志。
- TODO-32、TODO-34、TODO-35：已新增拓扑、Compose、实例重置、动态 Flag、阶段计分、Worker 维护清理测试；后端回归测试和前端构建通过。
- TODO-36、TODO-37、TODO-38、TODO-39、TODO-40：已新增/更新作者、运维、API、模式和交接文档，已同步 migration / ModelSnapshot / OpenAPI generated client；普通动态容器 API 与 Penetration 专用 API 分流。

仍需后续完善：

- TODO-18：Runner 能力只补了 Compose 状态查询，日志/批量状态等能力仍可继续增强。
- TODO-22：Kubernetes Provider 未实现，文档已明确 Penetration MVP 仅支持 Docker Compose。
- TODO-33：完整 API 集成测试尚未补齐。

## 0. 架构阅读结论

### 0.1 已确认的真实架构

- 技术栈：后端为 .NET 8、ASP.NET Core、FastEndpoints、SignalR、EF Core、PostgreSQL、Redis；前端为 Vue 3、TypeScript、Vite、Bun、Tailwind CSS、shadcn-vue 风格组件、Pinia、TanStack Vue Query。
- 后端分层：`backend/src/NoCTF.API` 负责 FastEndpoints、认证、SignalR、管理端和选手端 API；`NoCTF.Application` 负责计分、排行榜、后台任务和比赛模式抽象；`NoCTF.Core` 存放实体和枚举；`NoCTF.Infrastructure` 存放 EF Core DbContext、迁移、租户上下文、存储；`NoCTF.PluginBase` 存放插件和容器契约；`NoCTF.Container.Docker` 提供 Docker SDK 与 docker compose 实现；`NoCTF.Runner` 是容器操作边界；`NoCTF.Worker` 处理持久化后台任务和过期实例清理。
- 插件架构：已有 `NoCTF.Plugins.CTF`、`NoCTF.Plugins.AWD`、`NoCTF.Plugins.AWDP`、`NoCTF.Plugins.KoH`。插件通过 `IPluginModule` 注册服务，API 构建后把插件 DLL 复制到 `plugins` 目录并由 `PluginLoader` 冷加载。
- 鉴权方式：JWT Bearer；角色为 `Admin`、`Organizer`、`User`；`Program.cs` 明确设置 `NameClaimType = ClaimTypes.Name` 和 `RoleClaimType = ClaimTypes.Role`；SignalR 支持 query string 中的 `access_token`。
- 权限模型：存在 `CompetitionPermissionService` 和 `TeamPermissionService`，但很多 Admin Endpoint 仍只使用 `Roles("Admin", "Organizer")`，没有全部绑定比赛级权限。综合渗透功能必须使用比赛级权限校验。
- 多租户模型：实体实现 `ITenantEntity` 后由 `ApplicationDbContext` 全局 query filter 按 `CompetitionId` 隔离；比赛自身 `CompetitionId == Id`。大量跨比赛后台/管理查询使用 `.IgnoreQueryFilters()` 后手动加 `CompetitionId`。
- 比赛模型：`Competition` 使用 `GameModeType` enum 支持 `Ctf`、`Awd`、`Awdp`、`Koh`，同时有 `ModeKey` 和 `ScoringProfileJson`。当前综合渗透题应作为 CTF/Jeopardy 下的新题型实现，不建议新增互攻型赛制。
- 题目模型：存在全局题库 `ChallengeTemplate` 和比赛绑定题目 `Challenge`。二者都包含 `TypeId`、`DeploymentType`、`ContainerImage`、`ContainerMode`、`ComposeYaml`、`ExposedPort`、`FlagSecret`、`FlagEnvironmentVariable`、附件和 checker 配置。
- Flag / 提交 / 计分模型：`SubmitFlagEndpoint` 根据比赛 `GameModeType` 委托给对应 `IGameMode.ProcessSubmissionAsync`。CTF 使用 `CtfGameMode` 校验静态 flag 或 `[UUID]` 动态 flag，写入 `Submission`、`CompetitionLog`、`ScoreSignal`，再由 `ScoringStrategy` 写入 `ScoreEvent`。当前 `Submission` 有唯一索引限制同队同题只能有一个正确提交，不支持一道题多个阶段 Flag。
- 动态 Flag：已有 `CtfDynamicFlag`，按 `CompetitionId + TeamId + ChallengeId` 唯一，动态容器启动时生成 UUID 并通过 `NOCTF_FLAG_UUID` 或自定义环境变量注入。当前只支持一道题一个动态 flag。
- 容器/靶机部署：`IContainerManager` 支持 `CreateContainerAsync`、`DestroyContainerAsync`、`RunContainerAsync`、`ComposeUpAsync`、`ComposeDownAsync`。Docker SDK 可创建单容器，docker compose 可启动多容器，但当前动态实例选手端主要走单容器 `AwdGameBox`，ComposeUp 不返回每个服务的端口映射明细。
- 动态实例生命周期：`ChallengeInstanceEndpoints.cs` 提供选手端动态容器 `GET/POST/DELETE/extend`，使用 `AwdGameBox` 存储 `ContainerInstanceId`、端口映射、TTL、冷却时间；默认 TTL 2 小时、延长 30 分钟、操作冷却 5 秒；`ExpiredInstanceCleanupService` 清理过期实例。
- Runner/Worker：Worker 使用 `RunnerBackedContainerManager` 调 Runner；API 当前仍直接注册 `DockerManager`，Compose 部署中 backend、worker、runner 都挂载 Docker socket。综合渗透应优先沿用 Runner 边界，减少 API 直接操作 Docker 的新增面。
- 管理后台结构：Vue 路由 `/admin` 下有用户、队伍、赛事、题库、容器、插件、审计日志、健康检查和日志页面。赛事详情页 `AdminCompetitionDetailView.vue` 管理比赛设置、题目绑定、队伍审核、作弊信息和比赛日志。
- 选手端结构：`CompetitionGatewayView.vue` 按比赛模式路由到 CTF/AWDP 题目页、AWD Dashboard 或 KoH Dashboard；CTF/AWDP 题目页使用 `ChallengeCard.vue` 和 `ChallengeModal.vue`，动态容器启动、销毁、延长、flag 提交都在 modal 内完成。
- 审计日志：存在全局 `AuditLog` 和比赛内 `CompetitionLog`。敏感 Endpoint 可实现 `IAuditableEndpoint` 使用 `AuditLogPostProcessor`；部分代码也直接调用 `AuditLogWriter` 与 `CompetitionLogWriter`。
- 部署方式：`deploy/docker-compose.yml` 提供 Postgres、Redis、MinIO、backend、worker、runner；`deploy/k8s` 提供基础 K8s manifests 和 NetworkPolicy。`NoCTF.Container.K8s` 当前只有空类，Kubernetes Provider 未在当前项目中确认可用。
- 测试：`backend/tests/NoCTF.Tests` 已覆盖计分、动态 Flag、DockerComposeRunner、安全基线、排行榜、AWD/AWDP/KoH 等部分逻辑；前端没有在当前项目中确认到独立测试框架。

### 0.2 当前可复用模块

- 复用 `ChallengeTemplate` / `Challenge` 的题库与比赛绑定流程。
- 复用 `Competition`、`Team`、`TeamMember`、`CompetitionCollaborator` 的比赛和队伍权限边界。
- 复用 `IContainerManager`、`DockerManager`、`RunnerBackedContainerManager`、`DockerComposeRunner` 的容器编排入口。
- 复用 `BackgroundTaskItem`、`BackgroundTaskQueue`、`Worker` 的可恢复后台任务机制。
- 复用 `Submission`、`ScoreSignal`、`ScoreEvent`、`LeaderboardService` 的提交和排行榜主链路，但需要扩展支持阶段 Flag。
- 复用 `CtfDynamicFlag` 的生成思路，但不要直接复用该表承载多 Flag。
- 复用 `AuditLogWriter`、`CompetitionLogWriter`、`GetAuditLogsEndpoint`、比赛日志页面。
- 复用前端 `adminApi`、`competitionApi`、`queryKeys`、`ChallengeTemplateForm`、`AdminCompetitionDetailView`、`ChallengeModal`、i18n 文件。

### 0.3 当前必须扩展的模块

- `NoCTF.Core/Entities.cs`：新增综合渗透拓扑、节点、阶段 Flag、队伍实例、动态 Flag 实例、阶段提交关联字段。
- `NoCTF.PluginBase` / `NoCTF.Application.CompetitionModes`：新增“题型插件”扩展点和 registry，供 CTF 模式按 `Challenge.TypeId` 委托插件处理综合渗透题。
- `NoCTF.Application/Scoring`：只新增通用 scoring key / signal 常量；综合渗透阶段得分和首杀奖励策略由 `NoCTF.Plugins.Penetration` 注册实现。
- `NoCTF.Plugins.CTF/CtfGameMode.cs`：不要硬编码综合渗透业务；只改造成按题型扩展点查找 `IChallengeSubmissionHandler`，找不到 handler 时走现有普通 CTF 逻辑。
- `NoCTF.Plugins.Penetration`：新增官方插件，拥有拓扑、实例、编排、Flag、计分、后台任务等综合渗透业务实现。
- `NoCTF.API/Endpoints/Competitions`：新增选手端综合渗透实例、详情、阶段 Flag 状态 API，但 Endpoint 只能作为薄传输适配层，通过通用 registry 调插件，不能引用 `NoCTF.Plugins.Penetration` 具体类型。
- `NoCTF.API/Endpoints/Admin`：新增管理端拓扑、节点、阶段 Flag、队伍实例、环境日志 API，但业务逻辑必须在插件服务内，API 项目不得承载综合渗透领域逻辑。
- `NoCTF.Container.Docker` / `NoCTF.Runner`：补齐 Compose 服务发现、端口回读、网络/卷清理、状态同步、并发限流能力。
- `NoCTF.Infrastructure/ApplicationDbContext.cs` 和 migrations：新增 DbSet、索引、唯一约束、JSON 字段配置和提交唯一约束迁移。
- `frontend/src/api/noctf.ts`、`frontend/src/router/index.ts`、管理端/选手端 Vue 组件、`frontend/src/locales/*.json`：新增页面、组件、接口封装和文案。
- `backend/Dockerfile`、`backend/NoCTF.slnx`、API csproj 插件复制任务：如果新增 `NoCTF.Plugins.Penetration` 项目，需要加入构建和复制。

### 0.4 待确认信息

- Kubernetes Provider 未在当前项目中确认可用；当前仅有空项目 `NoCTF.Container.K8s` 和部署 manifests。
- 当前前端没有确认到单元测试/组件测试配置；实现 Agent 需要决定是否只做构建验证，还是引入最小测试。
- 当前 `RunnerBackedContainerManager` 只在 Worker 注册；API 仍直接注册 DockerManager。是否允许综合渗透选手请求同步调用 Runner，需要实现 Agent 结合运维目标确认。
- 当前是否有线上真实 Docker 网络隔离策略未在代码中确认；实现时不得假设宿主机已安全隔离。

## 1. 功能目标

新增一种题型：综合渗透题 / Penetration Challenge。它不是 AWD、AWDP、KOH 或攻防对抗，而是平台为每个队伍部署独立授权靶场环境，选手从入口服务进入，在本队独立环境内完成信息收集、漏洞利用、权限提升、内网探索和横向访问，提交多个阶段 Flag 得分。

MVP 目标：

- 管理员能在题库创建 `TypeId = "Penetration"` 的题目模板。
- 管理员能配置多节点拓扑、入口节点、内部节点、网络、资源限制、阶段 Flag、动态 Flag 注入规则。
- 管理员能把综合渗透题绑定到 CTF 比赛，并配置阶段分值、提示、重置策略。
- 选手只能启动、查看、重置、销毁自己队伍的实例。
- 每个队伍每题最多一个 active 实例，实例内可以有多个容器、网络和阶段 Flag。
- 支持固定 Flag 和动态 Flag；动态 Flag 按队伍/实例/阶段生成，不同队伍不同值。
- 同队同阶段 Flag 只能得分一次；同题多个阶段可以分别得分。
- 每个阶段 Flag 支持独立分值和首杀奖励。
- 实例操作、提交、失败、异常、管理员干预均进入审计日志和比赛日志。
- 普通 CTF、普通动态容器题、AWD、AWDP、KoH 不发生行为回归。

## 2. 非目标

- 不做 AWD。
- 不做 AWDP。
- 不做 KOH。
- 不做 Attack-Defense。
- 不做队伍互相攻击。
- 不做服务可用性 check 计分。
- 不做补丁上传。
- 不做防守得分。
- 不做占领机器持续得分。
- 不做对真实公网目标的攻击能力。
- 不绕过平台现有权限系统。
- 不把综合渗透题设计成共享靶场互相争夺控制权。
- 不为了该题型大规模重构全平台计分、比赛或插件体系。

## 3. 当前项目可复用模块

- 题库与比赛绑定：`ChallengeTemplate`、`Challenge`、`AdminChallengesView.vue`、`ChallengeTemplateForm.vue`、`AdminCompetitionDetailView.vue`。
- 比赛和队伍权限：`CompetitionPermissionService`、`TeamPermissionService`、`TeamMember`、`TeamRegistrationStatus`、队伍审批/封禁逻辑。
- 提交入口：`SubmitFlagEndpoint`、`SubmissionContext`、`CtfGameMode` 的普通提交前置校验模式。
- 计分事件：`ScoreSignal`、`ScoreEvent`、`IScoreSignalEmitter`、`IScoreEventWriter`、排行榜 projection。
- 动态容器基础：`IContainerManager`、`DockerManager`、`DockerComposeRunner`、`RunnerBackedContainerManager`、`AwdGameBox` 生命周期经验。
- 后台任务：`BackgroundTaskItem`、`BackgroundTaskQueue`、`Worker`、失败重试和锁机制。
- 日志审计：`AuditLog`、`CompetitionLog`、`AuditLogWriter`、`CompetitionLogWriter`、管理端审计日志和比赛日志页面。
- 前端数据层：`competitionApi`、`adminApi`、TanStack Query、`queryKeys`、`ChallengeModal` 动态容器交互模式。

## 4. 当前项目需要扩展的模块

- 数据层新增综合渗透专属表，避免继续扩宽 `Challenge` 到不可维护。
- 提交表新增阶段 Flag 关联字段，并调整唯一约束支持同题多次正确提交。
- 新增综合渗透实例服务，不能继续用只存一个 `ContainerInstanceId` 的 `AwdGameBox` 表表达多容器拓扑。
- 新增 Compose 编排服务，负责根据拓扑生成 docker compose YAML、启动、回读端口、状态同步和清理。
- 新增阶段 Flag 服务，负责动态值生成、注入、校验、跨队动态 Flag 风险识别。
- 新增综合渗透 API 分组，避免把多节点靶场逻辑塞进现有单容器 `/instance` API。
- 前端新增拓扑编辑器、阶段 Flag 编辑器、队伍实例监控页和选手端综合渗透面板。
- 文档新增综合渗透题配置、YAML/JSON 示例、选手授权范围说明和运维排障说明。

## 5. 推荐总体方案

### 5.1 最小侵入原则

- 综合渗透题作为题型实现：使用 `Challenge.TypeId = "Penetration"`，不新增互攻赛制，不新增队伍对抗逻辑。
- MVP 运行在 CTF 比赛内：沿用比赛时间、队伍审批、封禁、排行榜、提交入口和日志体系。
- 多节点环境使用 Docker Compose 作为 MVP 编排后端，因为当前 `IContainerManager` 已有 `ComposeUpAsync/ComposeDownAsync`，且 K8s Provider 未实现。
- 新增 `Penetration*` 实体表达拓扑、节点、阶段 Flag、队伍实例和动态 Flag，不复用 `AwdGameBox` 表承载多容器状态。
- 普通 CTF 保持原路径：只有当 `Challenge.TypeId` 规范化后等于 `penetration` 时才进入新服务。

### 5.1.1 插件架构红线

- 必须保持当前插件式总体架构：综合渗透实现为官方插件 `NoCTF.Plugins.Penetration`，不能把综合渗透核心业务写进 `NoCTF.API`。
- `NoCTF.API` 只能保存 HTTP 路由、认证、权限、参数绑定和响应映射；具体拓扑校验、实例状态机、Flag 校验、计分、编排、后台任务必须由插件注册的服务完成。
- `NoCTF.API` 不允许添加对 `NoCTF.Plugins.Penetration` 项目的编译期引用。API 调用综合渗透能力时，只能依赖 `NoCTF.PluginBase` 或 `NoCTF.Application` 中的通用接口和 registry。
- `NoCTF.Core` 只放共享领域实体、枚举和 value object；不得把综合渗透业务流程方法塞入实体。
- `NoCTF.Infrastructure` 只负责 DbContext、迁移和存储配置；不得实现综合渗透领域服务。
- `NoCTF.Application` 可以新增通用扩展点、registry、后台任务抽象和 scoring 抽象，但不得写死 `Penetration` 分支。
- `NoCTF.Plugins.CTF` 只能做一次通用化改造：让 CTF 模式按题型 handler 委托未知题型；不能在 CTF 插件内硬编码综合渗透状态机。
- `NoCTF.Plugins.Penetration` 通过 `IPluginModule.ConfigureServices` 注册所有综合渗透服务、scoring strategy、challenge feature provider 和 background job handler。

### 5.2 领域关系

```text
ChallengeTemplate
  └─ PenetrationTopologyTemplate
       ├─ PenetrationNodeTemplate
       └─ PenetrationFlagTemplate

Challenge
  └─ PenetrationTopology
       ├─ PenetrationNode
       └─ PenetrationFlag
            └─ DynamicFlagInstance per team/instance/flag

Team + Challenge
  └─ TeamChallengeInstance
       ├─ container ids / compose project / network ids
       └─ entry host/port/url

Submission
  └─ optional PenetrationFlagId for staged solves

ScoreSignal / ScoreEvent
  └─ scoring key: penetration-stage, penetration-blood-bonus
```

### 5.3 推荐新增后端服务

以下服务均由 `NoCTF.Plugins.Penetration` 拥有和注册；接口如需被 API 调用，必须抽象为通用题型 feature/action/submission 接口放在 `NoCTF.PluginBase` 或 `NoCTF.Application`，不得让 API 直接依赖插件内接口。

- `IPenetrationTopologyService`：校验和克隆题库拓扑到比赛题目。
- `IPenetrationInstanceService`：启动、停止、重启、重置、销毁、状态同步、入口解析。
- `IPenetrationComposeBuilder`：根据拓扑、队伍、动态 Flag 生成安全 Compose YAML。
- `IPenetrationFlagService`：生成动态 Flag、注入映射、校验提交、识别跨队动态 Flag。
- `IPenetrationScoringService` 或 scoring strategies：写阶段得分和阶段首杀奖励。
- `IPenetrationAuditService`：统一写 `AuditLog` 和 `CompetitionLog`。

### 5.4 API 设计原则

- 管理端 API 均放在 `/api/admin/competitions/{competitionId}/challenges/{challengeId}/penetration/...`，必须校验 `CanManageCompetitionAsync`。
- 选手端 API 均放在 `/api/competitions/{id}/challenges/{challengeId}/penetration/...`，必须校验登录、队伍存在、队伍审批通过、未封禁、比赛时间和题目类型。
- 普通 `/submit` 可以继续作为提交入口，但内部必须按 `PenetrationFlagId` 写提交和分数；也可以新增等价的 `/penetration/flags/submit`，前端优先使用新路径，旧路径保持兼容。
- 所有上述 API Endpoint 都是薄适配层：先完成认证、比赛级权限、队伍归属校验，再通过 `IChallengeFeatureRegistry` / `IChallengeAdminFeatureRegistry` / `IChallengeSubmissionHandlerRegistry` 调用对应插件。
- 如果 registry 中没有 `TypeId = penetration` 的 provider，API 返回 503 `challenge_type_plugin_unavailable`，不能 fallback 到普通 CTF 逻辑。

### 5.5 题型插件扩展点设计

- 在 `NoCTF.PluginBase` 或 `NoCTF.Application.CompetitionModes` 新增通用题型插件接口，而不是让 API 直接依赖具体插件类：
  - `IChallengeSubmissionHandler`：按 `TypeId` 处理题型级提交，输入 `SubmissionContext` 和题目上下文，输出 `SubmissionResult` 与可选 DTO。
  - `IChallengeFeatureProvider`：处理选手端题型动作和视图，例如 `penetration.instance.start`、`penetration.instance.reset`、`penetration.detail`。
  - `IChallengeAdminFeatureProvider`：处理管理端题型动作和视图，例如拓扑保存、测试部署、实例管理、日志读取。
  - `IChallengeFeatureRegistry` / `IChallengeAdminFeatureRegistry` / `IChallengeSubmissionHandlerRegistry`：按 `Challenge.TypeId` 找到 provider。
- `NoCTF.Plugins.Penetration` 实现上述接口并注册到 DI。
- `NoCTF.Plugins.CTF.CtfGameMode` 在普通静态/动态 Flag 逻辑之前查询 `IChallengeSubmissionHandlerRegistry`；若存在 handler 且 handler 声明接管提交，则委托插件处理。
- 现有 `IChallengeType` 可继续保留给简单 flag/container 配置；综合渗透需要实例、阶段、计分和审计，不应强行塞进只负责 `ValidateFlagAsync` 的旧接口。

## 6. 数据库与模型 TODO

### TODO-1: 新增综合渗透实体与状态枚举

- 优先级：P0
- 负责方向：Database / Backend
- 涉及文件：
  - `backend/src/NoCTF.Core/Entities.cs`
  - `backend/src/NoCTF.Core/Enums.cs`
  - `backend/src/NoCTF.Infrastructure/ApplicationDbContext.cs`
  - `backend/src/NoCTF.Infrastructure/Migrations/*`
- 前置依赖：
  - 无
- 任务说明：
  - 新增综合渗透题所需领域实体和状态枚举。
  - 不把所有字段继续塞进 `Challenge` 宽表；只在 `Challenge` / `ChallengeTemplate` 上保留必要开关和摘要字段。
- 实现要点：
  - 新增枚举：`PenetrationInstanceStatus`，值至少包含 `None`、`Starting`、`Running`、`Stopping`、`Stopped`、`Resetting`、`Failed`、`Destroying`、`Destroyed`、`Expired`。
  - 新增枚举：`PenetrationFlagInjectionType`，值至少包含 `EnvironmentVariable`、`File`；MVP 可只实现环境变量，但模型预留文件注入。
  - 新增实体 `PenetrationTopologyTemplate`、`PenetrationNodeTemplate`、`PenetrationFlagTemplate`，用于题库模板。
  - 新增实体 `PenetrationTopology : ITenantEntity`、`PenetrationNode : ITenantEntity`、`PenetrationFlag : ITenantEntity`，用于比赛绑定后的实际题目。
  - 新增实体 `TeamChallengeInstance : ITenantEntity`，用于队伍级独立环境实例。若担心名称泛化过度，可命名为 `PenetrationTeamInstance`，但 API DTO 使用 `TeamChallengeInstance` 语义。
  - 新增实体 `DynamicFlagInstance : ITenantEntity`，用于队伍/实例/阶段 Flag 的动态值。
  - `AuditLog` 已存在，不新增重复表；如需字段扩展，新增可空 `CompetitionId`、`TargetType`、`TargetId` 前需评估现有审计页面兼容性。MVP 可用 `NewValues` JSON 承载细节。
- 验收标准：
  - 新实体可被 EF Core 识别并生成 migration。
  - 所有比赛绑定实体实现 `ITenantEntity` 并有 `CompetitionId`。
  - 题库模板实体不受租户过滤影响。
  - 不破坏现有实体编译和现有迁移快照。
- 风险点：
  - 若把模板实体也实现 `ITenantEntity`，全局过滤会导致题库在非比赛请求中不可见。

### TODO-2: 设计综合渗透拓扑表结构

- 优先级：P0
- 负责方向：Database / Backend
- 涉及文件：
  - `backend/src/NoCTF.Core/Entities.cs`
  - `backend/src/NoCTF.Infrastructure/ApplicationDbContext.cs`
- 前置依赖：
  - TODO-1
- 任务说明：
  - 实现 `PenetrationTopology` 和模板拓扑表，描述一道综合渗透题的整体靶场拓扑。
- 实现要点：
  - `PenetrationTopologyTemplate` 字段：`Id`、`ChallengeTemplateId`、`Name`、`Description`、`NetworkConfigJson`、`EntryConfigJson`、`HealthcheckConfigJson`、`CreatedAt`、`UpdatedAt`。
  - `PenetrationTopology` 字段：`Id`、`CompetitionId`、`ChallengeId`、`Name`、`Description`、`NetworkConfigJson`、`EntryConfigJson`、`HealthcheckConfigJson`、`CreatedAt`、`UpdatedAt`。
  - `NetworkConfigJson` 建议结构包含 networks、subnets、egressPolicy、dns、internalOnly 默认值。
  - `EntryConfigJson` 建议结构包含 entryNodeId、scheme、containerPort、path、displayMode、visibleAfterStart。
  - `HealthcheckConfigJson` MVP 仅用于状态展示，不参与计分。
  - 建唯一索引：`PenetrationTopologies(CompetitionId, ChallengeId)`。
  - 建唯一索引：`PenetrationTopologyTemplates(ChallengeTemplateId)`。
- 验收标准：
  - 一个比赛题目最多一个综合渗透拓扑。
  - 删除比赛题目时能清理拓扑、节点、Flag 或实现明确的级联策略。
  - `HealthcheckConfigJson` 不被用于服务可用性得分。
- 风险点：
  - JSON 结构若无服务层校验，容易保存非法拓扑，后续 Compose 构建失败。

### TODO-3: 设计综合渗透节点表结构

- 优先级：P0
- 负责方向：Database / Backend
- 涉及文件：
  - `backend/src/NoCTF.Core/Entities.cs`
  - `backend/src/NoCTF.Infrastructure/ApplicationDbContext.cs`
- 前置依赖：
  - TODO-2
- 任务说明：
  - 实现 `PenetrationNode` 和模板节点表，描述拓扑内每个容器节点。
- 实现要点：
  - 字段：`Id`、`CompetitionId`、`TopologyId`、`Name`、`Role`、`Image`、`Command`、`EntrypointJson`、`EnvironmentJson`、`PortsJson`、`VolumesJson`、`NetworksJson`、`DependsOnJson`、`IsEntry`、`IsInternal`、`ResourceLimitJson`、`HealthcheckJson`、`CreatedAt`、`UpdatedAt`。
  - 模板表同字段但使用 `TopologyTemplateId`，不含 `CompetitionId`。
  - `PortsJson` 区分 internal ports 与 published entry ports；默认只有 `IsEntry = true` 的节点可发布端口。
  - `EnvironmentJson` 不得保存真实动态 Flag 值，只保存变量名、注入占位符和非敏感配置。
  - `VolumesJson` 默认只允许命名卷，不允许宿主机绝对路径挂载；如必须支持，只允许管理员配置白名单路径。
  - 索引：`(CompetitionId, TopologyId)`、`(CompetitionId, TopologyId, Name)`。
- 验收标准：
  - 可表达至少一个入口 Web 节点、一个内部数据库节点、一个内部二级服务节点。
  - 非入口节点默认不会暴露 host port。
  - 节点资源限制可被后续 Compose builder 读取。
- 风险点：
  - `VolumesJson`、`Command`、`EntrypointJson` 是容器逃逸高风险入口，必须在服务层做 allowlist/denylist。

### TODO-4: 设计阶段 Flag 与动态 Flag 实例表结构

- 优先级：P0
- 负责方向：Database / Backend / Security
- 涉及文件：
  - `backend/src/NoCTF.Core/Entities.cs`
  - `backend/src/NoCTF.Infrastructure/ApplicationDbContext.cs`
- 前置依赖：
  - TODO-2
  - TODO-3
- 任务说明：
  - 实现一道综合渗透题多个阶段 Flag 的定义和队伍级动态 Flag 实例。
- 实现要点：
  - `PenetrationFlag` 字段：`Id`、`CompetitionId`、`ChallengeId`、`NodeId`、`Name`、`Stage`、`ValueSecret`、`ValueHash`、`Score`、`IsDynamic`、`Visible`、`InjectionType`、`InjectionKey`、`HintAfterSolved`、`SolvedCount`、`CreatedAt`、`UpdatedAt`。
  - 模板 Flag 表同字段但不含 `CompetitionId`，并使用 `NodeTemplateId`。
  - `ValueSecret` 与现有 `FlagSecret` 风格保持兼容，但所有列表 DTO 必须隐藏；如实现成本允许，新增 HMAC/hash 校验并只在 reveal API 返回原值或不支持 reveal。
  - `Stage` 从 1 开始，允许同一节点多个 Flag，但 `(CompetitionId, ChallengeId, Stage)` 唯一。
  - `DynamicFlagInstance` 字段：`Id`、`CompetitionId`、`ChallengeId`、`TeamId`、`FlagId`、`InstanceId`、`ValueSecret`、`ValueHash`、`GeneratedAt`、`SolvedAt`。
  - 唯一索引：`DynamicFlagInstances(CompetitionId, TeamId, FlagId, InstanceId)`。
  - 查询索引：`DynamicFlagInstances(CompetitionId, ChallengeId, FlagId)`，用于识别提交了其他队伍动态 Flag 的异常。
- 验收标准：
  - 同一题可配置至少 5 个阶段 Flag，每个阶段独立分值。
  - 动态 Flag 对同一阶段的不同队伍值不同。
  - DTO 不泄露 `ValueSecret`、`ValueHash`。
- 风险点：
  - 如果 `ValueSecret` 明文落库，需沿用现有 reveal 审计模式，不能在普通 Admin 列表中返回。

### TODO-5: 扩展提交模型支持同题多阶段正确提交

- 优先级：P0
- 负责方向：Database / Backend
- 涉及文件：
  - `backend/src/NoCTF.Core/Entities.cs`
  - `backend/src/NoCTF.Infrastructure/ApplicationDbContext.cs`
  - `backend/src/NoCTF.Infrastructure/Migrations/*`
  - `backend/src/NoCTF.API/Endpoints/Competitions/GetSubmissionsEndpoint.cs`
  - `backend/src/NoCTF.API/Endpoints/Competitions/GetChallengesEndpoint.cs`
- 前置依赖：
  - TODO-4
- 任务说明：
  - 调整 `Submission` 以支持一道综合渗透题多个阶段 Flag 分别提交、分别得分，同时保持普通题型同题只算一次。
- 实现要点：
  - 在 `Submission` 新增可空字段：`PenetrationFlagId`、`SubmissionKind` 或 `Stage`。推荐 `PenetrationFlagId` 可空。
  - 替换当前 `ux_submissions_correct_once` 唯一索引为两个 partial unique index：
    - 普通题：`CompetitionId + TeamId + ChallengeId` where `IsCorrect = true AND PenetrationFlagId IS NULL`。
    - 综合渗透阶段：`CompetitionId + TeamId + ChallengeId + PenetrationFlagId` where `IsCorrect = true AND PenetrationFlagId IS NOT NULL`。
  - `GetSubmissionsEndpoint` 返回新增字段 `SolvedFlags`，包括 `ChallengeId`、`FlagId`、`Stage`、`SolvedAt`、`Score`。
  - 对普通前端兼容：`SolvedChallenges` 对普通题保持原行为；对综合渗透题只有全部 visible flags 解出后才标记为整题 solved，或新增 `IsFullySolved` 字段供前端使用。
  - `ChallengeSolveCounts.GetAsync` 对普通题保持不变；综合渗透题的 `SolveCount` 建议表示全清队伍数，阶段 solve count 由综合渗透详情 API 返回。
- 验收标准：
  - 同队同综合渗透题提交不同阶段正确 Flag 可产生多条正确 `Submission`。
  - 同队重复提交同一阶段正确 Flag 返回 already solved，不重复计分。
  - 普通 CTF 题仍只能有一条正确提交。
- 风险点：
  - 迁移 partial index 时必须先删除旧唯一索引，否则综合渗透第二个正确 Flag 会被数据库拒绝。

### TODO-6: 扩展 Challenge / ChallengeTemplate 的综合渗透配置

- 优先级：P0
- 负责方向：Database / Backend
- 涉及文件：
  - `backend/src/NoCTF.Core/Entities.cs`
  - `backend/src/NoCTF.API/Endpoints/Admin/CreateChallengeEndpoint.cs`
  - `backend/src/NoCTF.API/Endpoints/Admin/UpdateChallengeEndpoint.cs`
  - `backend/src/NoCTF.API/Endpoints/Admin/GetChallengesAdminEndpoint.cs`
  - `backend/src/NoCTF.API/Endpoints/Admin/CompetitionChallengeEndpoints.cs`
- 前置依赖：
  - TODO-1
- 任务说明：
  - 在现有题库/比赛题目模型上补充综合渗透运行策略字段。
- 实现要点：
  - 保留 `TypeId = "Penetration"` 作为题型标识，不新增 `ChallengeDeploymentType`。
  - `DeploymentType` 对综合渗透题固定为 `DynamicContainer`，`ContainerMode` 推荐固定为 `DockerCompose`。
  - 在 `ChallengeTemplate` 与 `Challenge` 增加字段或 JSON 配置：
    - `PenetrationAllowReset`
    - `PenetrationInstanceMode`，MVP 固定 `team`
    - `PenetrationMaxResetCount`
    - `PenetrationResourceLimitJson`
    - `PenetrationVisibleEntryAfterStart`
    - `PenetrationInstanceTtlSeconds`
    - `PenetrationActionCooldownSeconds`
  - 如果不想继续扩宽表，可使用 `PenetrationConfigJson`，但必须提供强类型 DTO 和校验。
  - `BindCompetitionChallengeEndpoint` 绑定综合渗透题时复制模板拓扑、节点、Flag 和策略到比赛题目。
- 验收标准：
  - 管理端创建模板后，绑定到比赛能得到一份独立可编辑的比赛题目拓扑。
  - 修改比赛题目的阶段分值和提示不会反向修改题库模板。
  - 普通题目 DTO 不多返回敏感字段。
- 风险点：
  - 模板与比赛题目共用同一拓扑会导致不同比赛互相影响，必须避免。

## 7. 后端服务 TODO

### TODO-7: 新增题型插件扩展点与综合渗透插件

- 优先级：P0
- 负责方向：Backend / Plugin
- 涉及文件：
  - `backend/src/NoCTF.PluginBase/Interfaces.cs`
  - `backend/src/NoCTF.PluginBase/Models.cs`
  - `backend/src/NoCTF.Application/CompetitionModes/*`
  - `backend/src/NoCTF.Plugins.Penetration/*`
  - `backend/src/NoCTF.Plugins.CTF/CtfGameMode.cs`
  - `backend/NoCTF.slnx`
  - `backend/src/NoCTF.API/NoCTF.API.csproj`
  - `backend/Dockerfile`
  - `backend/src/NoCTF.Worker/Program.cs`
- 前置依赖：
  - TODO-1
- 任务说明：
  - 按现有插件体系新增通用“题型插件”扩展点，并新增官方插件 `NoCTF.Plugins.Penetration`。
  - 不允许在 `NoCTF.API` 或 `NoCTF.Plugins.CTF` 中实现综合渗透领域业务。
- 实现要点：
  - 在 `NoCTF.PluginBase` 或 `NoCTF.Application` 新增通用接口：`IChallengeSubmissionHandler`、`IChallengeFeatureProvider`、`IChallengeAdminFeatureProvider` 及 registry。
  - `CtfGameMode` 只做通用委托：按 `Challenge.TypeId` 查找 submission handler；命中后交给 handler，未命中则走现有 CTF 逻辑。
  - 新增 `PenetrationModule : IPluginModule`，注册综合渗透 provider、submission handler、topology service、instance service、compose builder、flag service。
  - 综合渗透 scoring strategies 和 background job handler 也由 `PenetrationModule` 注册。
  - API 项目 `CopyPlugins` 加入 `NoCTF.Plugins.Penetration.dll`。
  - Dockerfile restore/publish 阶段加入新项目。
  - Worker 通过同一插件模块或 worker 专用插件注册方法加载必要 background job handler；不要在 Worker 中散落手写具体服务注册。
  - 不新增 `IGameMode`，避免把综合渗透误建成独立互攻赛制。
- 验收标准：
  - `dotnet build backend/NoCTF.slnx` 能发现并构建新插件。
  - API 启动日志显示插件成功加载。
  - `NoCTF.API` 项目文件没有 `ProjectReference Include="../NoCTF.Plugins.Penetration/...`。
  - `CtfGameMode` 不出现 `Penetration` 字符串分支，只依赖通用题型 handler registry。
  - 普通 CTF/AWD/AWDP/KoH 插件加载不受影响。
- 风险点：
  - 插件动态加载只扫描 publish 输出的 `plugins` 目录，本地 build 和 Docker publish 都要覆盖。
  - 如果 API 直接引用 `IPenetrationInstanceService` 这类插件内接口，会形成编译期耦合，必须改为通用 registry。

### TODO-8: 实现拓扑校验与克隆服务

- 优先级：P0
- 负责方向：Backend / Security
- 涉及文件：
  - `backend/src/NoCTF.Plugins.Penetration/PenetrationTopologyService.cs`
  - `backend/src/NoCTF.API/Endpoints/Admin/CompetitionChallengeEndpoints.cs`
  - `backend/tests/NoCTF.Tests/PenetrationTopologyServiceTests.cs`
- 前置依赖：
  - TODO-2
  - TODO-3
  - TODO-4
- 任务说明：
  - 统一校验拓扑安全性，并在题库模板绑定到比赛题目时克隆拓扑。
- 实现要点：
  - 校验至少一个 `IsEntry = true` 节点。
  - 校验所有节点 image 非空，且不包含明显危险配置。
  - 校验 `depends_on` 只引用同拓扑内节点。
  - 校验 internal 节点没有 published host port。
  - 校验 volumes 不包含 `/var/run/docker.sock`、宿主机绝对路径、`..`。
  - 校验 Compose service name 只能由字母、数字、短横线、下划线组成。
  - 校验阶段 Flag 的 `Stage` 连续或至少唯一，`Score > 0`。
  - 克隆时保留模板中的非敏感配置，动态 Flag 不生成实例值。
- 验收标准：
  - 非法拓扑保存/绑定时返回明确错误码。
  - 合法模板绑定后生成独立的 `PenetrationTopology`、`PenetrationNode`、`PenetrationFlag`。
  - 单元测试覆盖 entry 缺失、内部节点暴露端口、非法 volume、重复 stage。
- 风险点：
  - 只在前端校验不可靠，必须后端校验。

### TODO-9: 实现综合渗透实例服务

- 优先级：P0
- 负责方向：Backend / Infra
- 涉及文件：
  - `backend/src/NoCTF.Plugins.Penetration/PenetrationInstanceService.cs`
  - `backend/src/NoCTF.Plugins.Penetration/PenetrationComposeBuilder.cs`
  - `backend/src/NoCTF.PluginBase/Interfaces.cs`
  - `backend/src/NoCTF.Runner/Program.cs`
  - `backend/src/NoCTF.Runner.Client/RunnerContracts.cs`
- 前置依赖：
  - TODO-2
  - TODO-3
  - TODO-4
- 任务说明：
  - 实现队伍级靶场实例启动、停止、重启、重置、销毁、状态同步。
- 实现要点：
  - 启动前校验比赛处于可操作时间窗口，队伍已审批且未封禁，题型为 `Penetration`。
  - 一个 `CompetitionId + TeamId + ChallengeId` 同时最多一个 active 实例。
  - 使用 `PenetrationComposeBuilder` 生成 Compose YAML，project name 格式：`noctf-pen-{competition8}-{challenge8}-{team8}-{shortNonce}`。
  - 启动前生成所有动态 Flag 值并写 `DynamicFlagInstance`，再注入 Compose environment。
  - 调用 Runner/ContainerManager 的 ComposeUp；失败时立即 ComposeDown 回滚，并将实例标记 `Failed`。
  - ComposeUp 后需要回读 entry 节点 host/port。当前 `ComposeDeployment` 不包含端口，需要新增 Runner API 或 DockerManager 方法，通过 compose project label/service label inspect 容器端口。
  - 停止：保留实例记录和动态 Flag，但停止/删除容器，状态 `Stopped`。
  - 重启：对 stopped/running 实例重新 up，不重新生成 dynamic flag。
  - 重置：down + 删除卷 + 递增 reset_count + 重新生成 dynamic flag + up。
  - 销毁：down + 删除卷 + 标记 `Destroyed`，保留历史提交、动态 flag solved 记录和审计。
  - 所有状态变更写 `CompetitionLog` 和 `AuditLog`。
- 验收标准：
  - 启动成功后 `TeamChallengeInstance` 有 entry URL、compose project、container ids、status running。
  - 失败时无残留容器、网络、卷，实例状态 failed 并记录错误。
  - 重置次数超过限制返回 429 或 409，不执行编排。
  - API/Worker 重启后可通过 DB 记录同步真实容器状态。
- 风险点：
  - 当前 ComposeUp 临时写 compose 文件，后续 ComposeDown 依赖 DB 中保存原 compose yaml；必须保存实际渲染后的 YAML。

### TODO-10: 实现综合渗透 Flag 校验服务

- 优先级：P0
- 负责方向：Backend / Security
- 涉及文件：
  - `backend/src/NoCTF.Plugins.Penetration/PenetrationFlagService.cs`
  - `backend/src/NoCTF.Plugins.Penetration/PenetrationSubmissionHandler.cs`
  - `backend/src/NoCTF.Plugins.CTF/CtfGameMode.cs`
  - `backend/src/NoCTF.PluginBase/Interfaces.cs`
  - `backend/tests/NoCTF.Tests/PenetrationFlagServiceTests.cs`
- 前置依赖：
  - TODO-4
  - TODO-5
  - TODO-9
- 任务说明：
  - 实现多阶段 Flag 的生成、校验、重复提交处理、跨队动态 Flag 检测和提交审计。
- 实现要点：
  - 在 `NoCTF.Plugins.Penetration` 内实现 `PenetrationSubmissionHandler : IChallengeSubmissionHandler`，由插件注册为 `TypeId = "penetration"`。
  - `CtfGameMode.ProcessSubmissionAsync` 只查询通用 `IChallengeSubmissionHandlerRegistry`；若当前题型有 handler 则委托 handler，不出现 penetration 专属代码。
  - `SubmitFlagEndpoint` 不直接调用 `IPenetrationFlagService`，继续委托比赛 `IGameMode` 或通用 challenge action registry。
  - 比赛未开始、已结束、暂停、队伍无实例、实例未 running 时拒绝提交。
  - 对动态 Flag：只接受当前队伍 active instance 对应 `DynamicFlagInstance` 的值。
  - 对固定 Flag：按 `PenetrationFlag.ValueSecret` 或 `ValueHash` 校验。
  - 比较使用 timing-safe comparison，不用普通字符串比较。
  - 正确提交写 `Submission`，带 `PenetrationFlagId`。
  - 重复正确提交同一阶段返回 `AlreadySolved`，不写新的正确分数。
  - 错误提交写 `Submission` 和 `CompetitionLog`，但不记录完整 flag 内容到审计 JSON；如必须保留 `Submission.FlagContent`，至少审计日志只写长度和 hash。
  - 提交了其他队伍动态 Flag 时创建 `CheatIncident`，事件类型 `penetration.flag.suspected_cross_team`。
- 验收标准：
  - 一个队伍可以按阶段分别得分。
  - 其他队伍动态 Flag 不给当前队伍得分并记录作弊风险。
  - 普通 CTF `[UUID]` 动态 Flag 流程不受影响。
- 风险点：
  - 如果旧 `CtfGameMode` 在题型 handler 前先判断 already solved，会导致第一阶段后整题锁死；必须先让题型 handler 接管提交，再由 handler 自行判断阶段重复提交。

### TODO-11: 实现综合渗透计分策略

- 优先级：P0
- 负责方向：Backend
- 涉及文件：
  - `backend/src/NoCTF.Application/Scoring/ScoringAbstractions.cs`
  - `backend/src/NoCTF.Application/Scoring/ScoringServices.cs`
  - `backend/src/NoCTF.Plugins.Penetration/PenetrationScoringStrategy.cs`
  - `backend/src/NoCTF.Plugins.Penetration/PenetrationBloodBonusStrategy.cs`
  - `backend/tests/NoCTF.Tests/PenetrationScoringTests.cs`
- 前置依赖：
  - TODO-4
  - TODO-5
  - TODO-10
- 任务说明：
  - 新增阶段 Flag 得分和按阶段计算的首杀奖励。
- 实现要点：
  - 新增 `ScoreSignalTypes.PenetrationFlagAccepted = "penetration.flag.accepted"`。
  - 新增 `ScoringKeys.PenetrationStage = "penetration-stage"`。
  - 新增可选 `ScoringKeys.PenetrationBloodBonus = "penetration-blood-bonus"`。
  - Application 层只增加通用 signal/scoring key 常量和 profile 扩展点；实际 `IScoringStrategy` 实现在 `NoCTF.Plugins.Penetration` 内注册。
  - CTF scoring profile 可通过通用 profile provider 合并综合渗透 scoring key，但不能在 `CompetitionScoringProfileResolver` 中硬编码具体插件分支；优先让插件注册 profile contributor。
  - strategy 只处理 penetration signal，不影响普通 CTF。
  - 阶段得分：`PenetrationFlag.Score` 直接写入 `ScoreEvent`，idempotency key 包含 `teamId + challengeId + flagId`。
  - 阶段首杀：对每个 `FlagId` 单独排名，复用 Competition 的 `FirstBloodBonusPercent`、`SecondBloodBonusPercent`、`ThirdBloodBonusPercent` 或新增 penetration 专属配置；MVP 复用现有百分比。
  - `MetadataJson` 写入 `flagId`、`stage`、`flagName`、`baseScore`、`solveRank`。
  - `PenetrationFlag.SolvedCount` 可以由查询动态计算；若持久化，需要在事务内递增并提供重建任务。
- 验收标准：
  - 第一阶段首杀只影响第一阶段，不影响第二阶段首杀。
  - 同队同阶段重复提交不产生新 `ScoreEvent`。
  - 排行榜合计包含综合渗透阶段分数。
- 风险点：
  - 现有动态分数修正按 challenge 维度工作，不能直接套到阶段 Flag，否则会互相覆盖。

### TODO-12: 新增后台任务处理实例清理和状态同步

- 优先级：P1
- 当前状态：已完成。已新增通用 `IInstanceMaintenanceService`，Penetration 插件注册维护服务，Worker 定期清理过期实例、下线 Compose 项目、停用动态 Flag，并同步运行中实例的容器 ID、入口端口和丢失/停止状态。
- 负责方向：Backend / Infra
- 涉及文件：
  - `backend/src/NoCTF.Worker/*`
  - `backend/src/NoCTF.Plugins.Penetration/PenetrationInstanceCleanupJobHandler.cs`
  - `backend/src/NoCTF.Plugins.Penetration/PenetrationInstanceSyncService.cs`
  - `backend/src/NoCTF.Application/BackgroundTasks/*`
- 前置依赖：
  - TODO-9
- 任务说明：
  - 清理过期综合渗透实例，定期同步容器状态，处理启动失败和残留资源。
- 实现要点：
  - 优先新增 `ICompetitionJobHandler` / 插件注册的 background job handler；如需 hosted service，也应由 `PenetrationModule` 注册，避免在 Worker 主程序写死 penetration 服务。
  - 扫描 `TeamChallengeInstance` 中 `ExpiresAt <= now` 且 active 的实例。
  - 每轮限制处理数量，避免一次清理大量容器阻塞。
  - 调用 ComposeDown 清理容器、网络、卷；失败时记录 `LastError` 并下次重试。
  - 同步状态时通过 Runner/Docker 根据 compose project label 查询容器状态；如果 entry 容器退出，实例状态更新为 `Failed` 或 `Stopped`。
  - 对启动中卡住超过超时时间的实例标记 failed 并回滚。
- 验收标准：
  - 实例过期后 1 分钟内被清理。
  - Worker 重启不会丢失待清理任务。
  - 清理失败不会删除 DB 历史记录，但会持续可见并可由管理员手动销毁。
- 风险点：
  - 误删其他比赛或其他题目的 compose project 是严重事故，project name 和 label 必须双重匹配。

## 8. API TODO

### TODO-13: 管理端题库拓扑 API

- 优先级：P0
- 负责方向：Backend
- 涉及文件：
  - `backend/src/NoCTF.API/Endpoints/Admin/PenetrationTemplateTopologyEndpoints.cs`
  - `backend/src/NoCTF.Application/CompetitionModes/ChallengeFeatureRegistry.cs`
  - `backend/src/NoCTF.Plugins.Penetration/PenetrationTopologyService.cs`
- 前置依赖：
  - TODO-2
  - TODO-3
  - TODO-4
- 任务说明：
  - 为题库模板提供综合渗透拓扑、节点、Flag 的创建和编辑 API。
- 实现要点：
  - Endpoint 只能依赖 `IChallengeAdminFeatureRegistry`，按 `TypeId = "penetration"` 和 action/view key 调用插件；不得引用插件内具体 service。
  - `GET /api/admin/challenges/{templateId}/penetration-topology`
    - 权限：`Admin` 或 `Organizer`，后续可细化。
    - 请求参数：`templateId`。
    - 响应：topology、nodes、flags，不包含 Flag secret。
    - 复用：`ChallengeTemplates` 题库。
    - 新增：拓扑聚合 DTO。
    - 验收：不存在拓扑时返回空结构和 `code = "not_configured"`。
  - `PUT /api/admin/challenges/{templateId}/penetration-topology`
    - 权限：`Admin` 或 `Organizer`。
    - 请求参数：topology、nodes、flags、config。
    - 响应：保存后的拓扑聚合 DTO。
    - 复用：`ChallengeTemplateRequestRules` 的清洗风格。
    - 新增：后端拓扑安全校验。
    - 验收：非法节点/端口/volume 返回 400 和明确 code。
  - `POST /api/admin/challenges/{templateId}/penetration-topology/flags/{flagId}/reveal`
    - 权限：`Admin` 或有题目管理权的 Organizer。
    - 请求参数：`templateId`、`flagId`。
    - 响应：只返回该 Flag secret 或动态占位说明。
    - 复用：`RevealChallengeSecretEndpoint` 的审计思路。
    - 新增：`penetration.flag.revealed` 审计动作。
    - 验收：调用后 `AuditLog` 有记录。
- 验收标准：
  - 管理端能完整保存并读取一个多节点综合渗透题模板。
  - API 不返回真实 Flag secret，除 reveal API。
- 风险点：
  - 当前 Admin 题库是全局资源，Organizer 权限边界不细；若短期沿用角色权限，需要在 TODO-30 中补权限测试。

### TODO-14: 管理端比赛题目拓扑 API

- 优先级：P0
- 负责方向：Backend
- 涉及文件：
  - `backend/src/NoCTF.API/Endpoints/Admin/PenetrationCompetitionTopologyEndpoints.cs`
  - `backend/src/NoCTF.API/Permissions/CompetitionPermissionService.cs`
  - `backend/src/NoCTF.Application/CompetitionModes/ChallengeFeatureRegistry.cs`
- 前置依赖：
  - TODO-8
  - TODO-13
- 任务说明：
  - 管理员/赛事管理者编辑比赛内某道综合渗透题的实际拓扑和阶段分值。
- 实现要点：
  - Endpoint 完成 `CanManageCompetitionAsync` 后，通过 `IChallengeAdminFeatureRegistry` 调用 `penetration.topology.get/update/test-deploy`；拓扑校验和测试部署逻辑由插件实现。
  - `GET /api/admin/competitions/{competitionId}/challenges/{challengeId}/penetration/topology`
    - 权限：`CanManageCompetitionAsync`。
    - 响应：比赛题目拓扑、节点、阶段 Flag、运行策略，不含 secret。
    - 验收：非综合渗透题返回 400 `not_penetration_challenge`。
  - `PUT /api/admin/competitions/{competitionId}/challenges/{challengeId}/penetration/topology`
    - 权限：`CanManageCompetitionAsync`。
    - 请求：完整拓扑聚合。
    - 新增逻辑：禁止在已有 running 实例时修改会影响编排的字段，或要求 `force` 并先销毁实例。
    - 验收：running 实例存在时返回 409 `active_instances_exist`。
  - `POST /api/admin/competitions/{competitionId}/challenges/{challengeId}/penetration/test-deploy`
    - 权限：`CanManageCompetitionAsync`。
    - 请求：可选 `ttlSeconds`。
    - 响应：临时 entry URL、container ids、logs 摘要。
    - 新增逻辑：使用特殊 admin test team id 或独立 project label，结束后自动销毁。
    - 验收：测试部署失败会返回错误日志并清理残留。
- 验收标准：
  - 比赛题目拓扑可独立于题库模板编辑。
  - 比赛级权限校验覆盖所有接口。
- 风险点：
  - test deploy 若不隔离命名，可能覆盖真实队伍实例。

### TODO-15: 管理端队伍实例 API

- 优先级：P0
- 负责方向：Backend
- 涉及文件：
  - `backend/src/NoCTF.API/Endpoints/Admin/PenetrationInstanceAdminEndpoints.cs`
  - `backend/src/NoCTF.Application/CompetitionModes/ChallengeFeatureRegistry.cs`
  - `backend/src/NoCTF.Plugins.Penetration/PenetrationInstanceService.cs`
- 前置依赖：
  - TODO-9
- 任务说明：
  - 管理员查看和管理所有队伍的综合渗透实例。
- 实现要点：
  - Endpoint 使用 `IChallengeAdminFeatureRegistry` 转发 `penetration.instances.*` 管理动作；实例查询、重置、销毁、日志脱敏由插件实现。
  - `GET /api/admin/competitions/{competitionId}/penetration/instances`
    - 权限：`CanManageCompetitionAsync`。
    - 参数：`challengeId?`、`teamId?`、`status?`、`page`、`pageSize`。
    - 响应：实例列表、队伍名、题目名、status、entryUrl、startedAt、resetCount、lastError。
    - 验收：只能返回该比赛实例。
  - `GET /api/admin/competitions/{competitionId}/penetration/instances/{instanceId}`
    - 权限：`CanManageCompetitionAsync`。
    - 响应：实例详情、节点状态、容器 id、端口、动态 Flag solved 状态，不返回未解动态 Flag 明文。
    - 验收：实例不存在返回 404。
  - `POST /api/admin/competitions/{competitionId}/penetration/instances/{instanceId}/reset`
    - 权限：`CanManageCompetitionAsync`。
    - 请求：`reason`、`force`。
    - 响应：新实例状态。
    - 验收：写 `penetration.instance.admin_reset` 审计。
  - `DELETE /api/admin/competitions/{competitionId}/penetration/instances/{instanceId}`
    - 权限：`CanManageCompetitionAsync`。
    - 请求：可选 `reason`。
    - 响应：204。
    - 验收：清理 compose project、网络、卷并写日志。
  - `GET /api/admin/competitions/{competitionId}/penetration/instances/{instanceId}/logs`
    - 权限：`CanManageCompetitionAsync`。
    - 参数：`nodeId?`、`tail?`。
    - 响应：按节点分组的最近日志。
    - 验收：日志中不输出动态 Flag 明文；如容器日志包含 Flag，需要过滤常见注入变量值。
- 验收标准：
  - 管理员能按比赛、题目、队伍筛选实例。
  - 管理员操作实例均有审计和比赛日志。
- 风险点：
  - 容器日志可能包含 Flag，日志接口需要脱敏。

### TODO-16: 选手端综合渗透题详情与实例 API

- 优先级：P0
- 负责方向：Backend
- 涉及文件：
  - `backend/src/NoCTF.API/Endpoints/Competitions/PenetrationChallengeEndpoints.cs`
  - `backend/src/NoCTF.Application/CompetitionModes/ChallengeFeatureRegistry.cs`
  - `backend/src/NoCTF.Plugins.Penetration/PenetrationInstanceService.cs`
- 前置依赖：
  - TODO-9
  - TODO-10
- 任务说明：
  - 为选手提供题目详情、授权范围、阶段状态和实例生命周期 API。
- 实现要点：
  - Endpoint 完成登录、队伍归属、审批/封禁检查后，通过 `IChallengeFeatureRegistry` 调用插件 action/view；实例状态机和入口生成不得写在 API 层。
  - `GET /api/competitions/{id}/challenges/{challengeId}/penetration`
    - 权限：登录；需校验队伍审批通过才能看到入口相关状态。
    - 响应：题目说明、授权范围、visible flags 列表、已解阶段、实例状态、entryUrl、resetCount、resetLimit、serverTime。
    - 复用：`TeamMember`、`Teams`、`ChallengeHints`。
    - 新增：阶段 Flag solved 状态。
    - 验收：未参赛或未审批只返回受限信息，不返回入口。
  - `POST /api/competitions/{id}/challenges/{challengeId}/penetration/instance/start`
    - 权限：登录且本队已审批、未封禁。
    - 请求：空或 `{}`。
    - 响应：实例状态和 entryUrl。
    - 验收：重复启动返回已有 running 实例，不重复创建。
  - `GET /api/competitions/{id}/challenges/{challengeId}/penetration/instance`
    - 权限：登录且本队成员。
    - 响应：自己的实例状态。
    - 验收：不能通过任何参数查询其他队伍实例。
  - `POST /api/competitions/{id}/challenges/{challengeId}/penetration/instance/stop`
    - 权限：登录且本队已审批。
    - 响应：stopped 状态。
    - 验收：比赛结束后可允许销毁/停止，但不允许重新启动。
  - `POST /api/competitions/{id}/challenges/{challengeId}/penetration/instance/reset`
    - 权限：登录且本队已审批。
    - 响应：重置后的 entryUrl 和 resetCount。
    - 验收：超过 max reset 返回 429/409。
  - `DELETE /api/competitions/{id}/challenges/{challengeId}/penetration/instance`
    - 权限：登录且本队已审批。
    - 响应：204 或实例 none 状态。
    - 验收：只销毁本队实例。
- 验收标准：
  - 选手完整生命周期操作可用。
  - 所有接口都检查比赛时间、队伍状态、题型和权限。
- 风险点：
  - 现有动态容器 API 没有严格比赛时间校验，综合渗透不能继承该缺口。

### TODO-17: 选手端阶段 Flag 提交 API

- 优先级：P0
- 负责方向：Backend
- 涉及文件：
  - `backend/src/NoCTF.API/Endpoints/Competitions/PenetrationFlagSubmitEndpoint.cs`
  - `backend/src/NoCTF.API/Endpoints/Competitions/SubmitFlagEndpoint.cs`
  - `backend/src/NoCTF.Application/CompetitionModes/ChallengeFeatureRegistry.cs`
  - `backend/src/NoCTF.Plugins.Penetration/PenetrationFlagService.cs`
- 前置依赖：
  - TODO-10
  - TODO-11
- 任务说明：
  - 新增综合渗透专用提交 API，并保持普通 `/submit` 兼容。
- 实现要点：
  - `POST /api/competitions/{id}/challenges/{challengeId}/penetration/flags/submit`
    - 权限：登录且本队已审批、未封禁。
    - 请求参数：`{ "flag": "flag{...}" }`。不要求选手传 stage，后端通过值匹配阶段。
    - 响应：`correct`、`alreadySolved`、`result`、`flagId`、`stage`、`scoreAwarded`、`solvedFlags`。
    - 复用：`SubmitFlagEndpoint` 前置 team 校验逻辑可抽公共 helper。
    - 新增：阶段 Flag 匹配、动态 Flag 实例校验、计分 signal，均由插件 handler 完成。
    - 验收：正确阶段返回 stage，错误不泄露接近哪个阶段。
  - 修改通用 `SubmitFlagEndpoint`：不要写 `if penetration` 业务分支；通过 `IGameMode` -> `CtfGameMode` -> `IChallengeSubmissionHandlerRegistry` 委托插件，返回兼容 `SubmitFlagResponse`。
  - 增加错误提交频率限制：同队同题 5 秒内最多 1 次，或 1 分钟最多 10 次；触发后返回 429 `flag_rate_limited` 并写风控日志。
- 验收标准：
  - 新旧提交路径对 penetration 都能工作。
  - 普通 CTF 提交流程无变化。
  - 频率限制只影响当前队伍/题目，不影响全队其他题目。
- 风险点：
  - 通过 flag 值匹配阶段时，固定 Flag 重复值会造成歧义；后端保存时必须禁止同题重复 Flag 值。

### TODO-18: 内部服务 API / Runner 能力扩展

- 优先级：P1
- 负责方向：Infra / Backend
- 涉及文件：
  - `backend/src/NoCTF.PluginBase/Interfaces.cs`
  - `backend/src/NoCTF.PluginBase/Models.cs`
  - `backend/src/NoCTF.Container.Docker/DockerManager.cs`
  - `backend/src/NoCTF.Runner/Program.cs`
  - `backend/src/NoCTF.Runner.Client/RunnerContracts.cs`
- 前置依赖：
  - TODO-9
- 任务说明：
  - 补齐多容器实例所需的 Runner 内部能力。
- 实现要点：
  - 新增 `ComposeStatus` / `ComposeServiceInstance` DTO：projectName、services、containerIds、publishedPorts、status。
  - 新增 `IContainerManager.GetComposeStatusAsync(projectName, labels)` 或 Runner 专用接口。
  - Runner 新增：
    - `GET /runner/compose/{projectName}/status`
    - `GET /runner/compose/{projectName}/logs?service=&tail=`
    - `POST /runner/compose/down`
  - Docker 实现通过 Docker labels 和 compose project name 查询，不仅依赖临时 compose 文件。
  - 禁止 Runner 接受未通过 allowlist 的 image registry；Compose YAML 中每个 image 也要校验。
- 验收标准：
  - ComposeUp 后可准确返回 entry 节点 host port。
  - Admin 日志 API 可读取指定节点 tail logs。
  - Runner 拒绝 forbidden compose directive。
- 风险点：
  - docker compose CLI 和 Docker SDK label 行为可能不完全一致，需要集成测试。

## 9. 靶场编排 TODO

### TODO-19: 实现安全 Compose 生成器

- 优先级：P0
- 负责方向：Infra / Backend / Security
- 涉及文件：
  - `backend/src/NoCTF.Plugins.Penetration/PenetrationComposeBuilder.cs`
  - `backend/src/NoCTF.Container.Docker/DockerComposeRunner.cs`
  - `backend/tests/NoCTF.Tests/PenetrationComposeBuilderTests.cs`
- 前置依赖：
  - TODO-3
  - TODO-4
- 任务说明：
  - 根据拓扑、队伍、实例和动态 Flag 生成可部署的 docker compose YAML。
- 实现要点：
  - 每个队伍实例命名：project name 唯一且带比赛/题目/队伍短 id。
  - 每个实例创建独立默认网络，内部节点只连接该 project 网络。
  - entry 节点可发布配置的端口到随机 host port；内部节点不得发布 host port。
  - 所有服务加 labels：`noctf.kind=penetration`、`competitionId`、`challengeId`、`teamId`、`instanceId`、`nodeId`。
  - 动态 Flag 通过节点 `EnvironmentJson` 中配置的变量名注入。
  - 资源限制映射到 compose deploy/resources 或 Docker host config 可识别字段；Compose 本地模式不完全支持时需在文档中标明，并尽量用 Docker SDK 后处理或 runner 校验。
  - 默认安全策略：禁止 privileged、host network、host pid、devices、cap_add、docker.sock、extra_hosts；默认 `cap_drop: [ALL]`，仅按 allowlist 增加必要能力。
  - volumes 使用 project scoped named volumes，重置时删除。
  - 生成结果保存到 `TeamChallengeInstance.InstanceConfigJson` 或 `RenderedComposeYaml`，用于 down 和排障。
- 验收标准：
  - 三节点拓扑生成的 Compose YAML 可通过 `DockerComposeRunner.ValidateComposeYaml`。
  - 只有入口节点有 host port。
  - 每个服务带完整 labels。
- 风险点：
  - Compose YAML 字符串拼接容易出错；优先用结构化 YAML 序列化库，如项目未引入，需要评估新增依赖。

### TODO-20: 实现实例状态机和并发锁

- 优先级：P0
- 负责方向：Backend / Infra
- 涉及文件：
  - `backend/src/NoCTF.Plugins.Penetration/PenetrationInstanceService.cs`
  - `backend/src/NoCTF.Infrastructure/ApplicationDbContext.cs`
- 前置依赖：
  - TODO-9
- 任务说明：
  - 避免同一队伍同题并发启动/重置造成重复容器或状态错乱。
- 实现要点：
  - 数据库唯一索引：`TeamChallengeInstances(CompetitionId, TeamId, ChallengeId)`。
  - 实例操作在事务内将状态从 stable 状态切换到 transient 状态，如 `None -> Starting`、`Running -> Resetting`。
  - 对 `Starting/Resetting/Destroying` 状态的再次请求返回 409 `instance_busy`。
  - `LastActionAt` 用于冷却限制。
  - 编排失败时状态变为 `Failed`，记录 `LastError`，管理员可 destroy/reset。
  - 可选：使用 Postgres advisory lock 或行级并发 token。
- 验收标准：
  - 并发 5 个 start 请求最终只有一个 compose project。
  - 并发 reset/destroy 不会互相覆盖。
  - 失败状态对选手和管理员可见。
- 风险点：
  - 容器编排耗时较长，事务不能覆盖整个 Docker 操作；需要短事务抢占状态，外部操作后再落结果。

### TODO-21: 实现环境重置和销毁的完整清理

- 优先级：P0
- 负责方向：Infra / Backend
- 涉及文件：
  - `backend/src/NoCTF.Plugins.Penetration/PenetrationInstanceService.cs`
  - `backend/src/NoCTF.Container.Docker/DockerManager.cs`
- 前置依赖：
  - TODO-19
  - TODO-20
- 任务说明：
  - 确保 reset/destroy 清理容器、网络、卷和动态 Flag 状态。
- 实现要点：
  - reset 流程：mark resetting -> compose down with volumes -> generate new dynamic flags -> compose up -> update entry/status/resetCount。
  - destroy 流程：mark destroying -> compose down with volumes -> clear entry/status -> mark destroyed。
  - stop 流程：compose down without deleting dynamic flag solved history，可保留或删除卷按策略配置。
  - 清理时同时根据 labels 查找残留容器/网络/卷，处理 compose down 未覆盖的资源。
  - 所有 cleanup 操作幂等，找不到资源视为成功但写 warning log。
- 验收标准：
  - destroy 后 `docker ps`、`docker network ls`、`docker volume ls` 中无该 instance labels 的资源。
  - reset 后动态 Flag 值变更，旧动态 Flag 不再可提交。
  - 已得分阶段不会因 reset 丢失。
- 风险点：
  - reset 后旧动态 Flag 若仍有效，会被队伍复用；必须按 instanceId 校验最新 active instance。

### TODO-22: 规划 Kubernetes 后续支持边界

- 优先级：P2
- 负责方向：Infra / Docs
- 涉及文件：
  - `backend/src/NoCTF.Container.K8s/*`
  - `deploy/k8s/*`
  - `docs/deployment.md`
- 前置依赖：
  - TODO-19
- 任务说明：
  - 明确 Kubernetes Provider 当前未实现，综合渗透 MVP 只支持 Docker Compose；为后续 K8s 支持留下接口边界。
- 实现要点：
  - 文档写明：当前 `NoCTF.Container.K8s` 未实现多节点题目编排。
  - 后续 K8s 设计围绕 Namespace、Pod/Deployment、Service、NetworkPolicy、Ingress、ResourceQuota、LimitRange。
  - 不在 MVP 前端暴露 K8s 选项。
- 验收标准：
  - 管理端不会让用户误以为可选 K8s 编排。
  - docs 中有明确后续任务。
- 风险点：
  - 部署文档现有 K8s 内容可能让实现 Agent 误选 K8s，必须在综合渗透文档中特别说明。

## 10. 动态 Flag 与计分 TODO

### TODO-23: 支持动态 Flag 注入策略

- 优先级：P0
- 负责方向：Backend / Security
- 涉及文件：
  - `backend/src/NoCTF.Plugins.Penetration/PenetrationFlagService.cs`
  - `backend/src/NoCTF.Plugins.Penetration/PenetrationComposeBuilder.cs`
- 前置依赖：
  - TODO-4
  - TODO-19
- 任务说明：
  - 让每个阶段 Flag 可以配置固定值或队伍级动态值，并注入到指定节点。
- 实现要点：
  - 动态 Flag 格式复用比赛/题目 flag prefix 规则，默认 `flag{uuid}`。
  - 每个 dynamic flag 生成时关联 `TeamChallengeInstance.Id`。
  - `InjectionType = EnvironmentVariable` 时，将 `InjectionKey=ENV_NAME` 注入目标节点环境变量。
  - `InjectionType = File` MVP 可返回 400 `file_injection_not_supported` 或通过环境变量 + entrypoint 模板实现；不得假装支持。
  - `visible=false` 的 Flag 不在选手 UI 显示名称/阶段提示，但可提交得分。
  - 生成动态值时使用密码学随机或 UUID v4，不使用可预测序列。
- 验收标准：
  - 两个队伍同阶段动态 Flag 不相同。
  - 同队 reset 后新 instance 的动态 Flag 变更。
  - 旧 instance 的动态 Flag 不再有效。
- 风险点：
  - 如果 flag 仅作为环境变量存在，题目作者需要在镜像内把变量写入合适位置；文档必须说明。

### TODO-24: 提交频率限制和风控

- 优先级：P1
- 负责方向：Backend / Security
- 涉及文件：
  - `backend/src/NoCTF.Plugins.Penetration/PenetrationFlagService.cs`
  - `backend/src/NoCTF.Infrastructure/ApplicationDbContext.cs`
  - `backend/tests/NoCTF.Tests/PenetrationRateLimitTests.cs`
- 前置依赖：
  - TODO-10
- 任务说明：
  - 防止错误 Flag 高频提交、撞库和跨队动态 Flag 滥用。
- 实现要点：
  - MVP 可用数据库查询最近错误提交数量：`CompetitionId + TeamId + ChallengeId + SubmittedAt`。
  - 默认规则：5 秒内只能提交 1 次；1 分钟内最多 10 次；管理员可在配置中调整。
  - 超限返回 429 `flag_rate_limited`。
  - 高频错误写 `CompetitionLog` event `penetration.flag.rate_limited`。
  - 多次提交其他队伍动态 Flag 创建/更新 `CheatIncident`。
- 验收标准：
  - 错误提交超过阈值被拒绝。
  - 正确提交不因前一次错误提交过快而被永久阻塞，只受短时限制。
  - 风控日志中不包含明文 Flag。
- 风险点：
  - 纯 DB 查询在高并发下有成本，后续可接 Redis；MVP 先保证正确。

## 11. 前端管理端 TODO

### TODO-25: 扩展题库创建/编辑表单支持 Penetration

- 优先级：P0
- 负责方向：Frontend
- 涉及文件：
  - `frontend/src/components/admin/ChallengeTemplateForm.vue`
  - `frontend/src/views/admin/AdminChallengesView.vue`
  - `frontend/src/views/admin/AdminChallengeCreateView.vue`
  - `frontend/src/locales/zh-CN.json`
  - `frontend/src/locales/en.json`
- 前置依赖：
  - TODO-13
- 任务说明：
  - 在管理端题库中新增综合渗透题型入口。
- 实现要点：
  - `challengeTypeOptions` 增加 `{ value: 'Penetration', label: 'Penetration' }`。
  - 选择 Penetration 时固定或默认：
    - `deploymentType = DynamicContainer`
    - `containerMode = DockerCompose`
    - 隐藏普通单 Flag secret 输入，改为阶段 Flag 配置入口。
  - 增加“拓扑配置”区域，MVP 可以用 JSON/YAML 编辑器式 textarea，后续再做图形化拓扑。
  - 增加“节点配置”列表：name、image、role、isEntry、internal、ports、env、resource limit。
  - 增加“阶段 Flag”列表：stage、name、node、score、isDynamic、injection key、visible、hint after solved。
  - 保存题库模板后调用 TODO-13 的 topology API。
  - 加载态：保存拓扑时显示按钮 loading。
  - 错误态：展示后端 code，不吞掉非法拓扑错误。
  - 权限限制：沿用 admin route，后端兜底。
- 验收标准：
  - 管理员能创建带三节点、三个阶段 Flag 的综合渗透题模板。
  - 编辑后刷新页面配置不丢失。
  - 中英文文案完整。
- 风险点：
  - 复杂拓扑用纯表单很容易难用；MVP 先保证可配置和可验证。

### TODO-26: 扩展比赛题目绑定和编辑页

- 优先级：P0
- 负责方向：Frontend
- 涉及文件：
  - `frontend/src/views/admin/AdminCompetitionDetailView.vue`
  - `frontend/src/api/noctf.ts`
  - `frontend/src/api/queryKeys.ts`
  - `frontend/src/locales/zh-CN.json`
  - `frontend/src/locales/en.json`
- 前置依赖：
  - TODO-14
- 任务说明：
  - 在赛事详情中编辑综合渗透题的比赛级分值、提示、重置策略和拓扑。
- 实现要点：
  - 绑定 Penetration 模板时显示阶段分值摘要，不使用普通 `initial/minimum/decay` 作为唯一分值来源。
  - 选中综合渗透题后显示：
    - 基础说明
    - 授权范围说明
    - 阶段 Flag 表格
    - reset 策略
    - 资源限制
    - 拓扑 JSON/YAML
    - 测试部署按钮
  - 测试部署调用 `POST .../test-deploy`，展示 entry URL 和失败日志。
  - running 实例存在时，编辑危险字段前提示并禁用保存。
  - 加载态：拓扑单独 skeleton，不阻塞比赛基础信息。
  - 错误态：拓扑加载失败可重试。
- 验收标准：
  - 比赛内综合渗透题可调整每阶段分值。
  - 测试部署成功/失败信息可见。
  - 普通 CTF/AWDP 编辑区域不受影响。
- 风险点：
  - `AdminCompetitionDetailView.vue` 已较大，建议拆分 `PenetrationChallengeEditor.vue`，避免继续堆单文件。

### TODO-27: 新增管理端综合渗透实例监控页

- 优先级：P1
- 当前状态：已完成。比赛管理顶栏新增“靶场实例”入口，支持按综合渗透题目和队伍筛选实例、查看状态/入口/过期时间/错误，并执行重置或销毁。节点级日志抽屉仍属于后续增强。
- 负责方向：Frontend
- 涉及文件：
  - `frontend/src/views/admin/AdminPenetrationInstancesView.vue`
  - `frontend/src/views/admin/AdminCompetitionDetailView.vue`
  - `frontend/src/api/noctf.ts`
  - `frontend/src/router/index.ts`
  - `frontend/src/locales/zh-CN.json`
  - `frontend/src/locales/en.json`
- 前置依赖：
  - TODO-15
- 任务说明：
  - 管理员查看所有队伍综合渗透实例并执行 reset/destroy/logs。
- 实现要点：
  - 可作为 `AdminCompetitionDetailView` 新增 nav section `penetration`，避免全局路由膨胀。
  - 表格列：队伍、题目、状态、入口、启动时间、重置次数、最后错误、操作。
  - 筛选：题目、队伍、状态。
  - 详情抽屉：节点容器、端口、日志、动态 Flag solved 状态。
  - 操作：重置、销毁、查看日志，危险操作需要确认。
  - 加载态：表格 skeleton。
  - 错误态：保留筛选条件并允许重试。
- 验收标准：
  - 管理员可以销毁某队某题环境。
  - 查看日志不会显示已知动态 Flag 明文。
  - 操作后表格状态刷新。
- 风险点：
  - 全局 `/admin/containers` 仍显示 `AwdGameBox`，不能作为综合渗透唯一监控面。

## 12. 前端选手端 TODO

### TODO-28: 新增选手端综合渗透题面板

- 优先级：P0
- 负责方向：Frontend
- 涉及文件：
  - `frontend/src/components/game/ChallengeModal.vue`
  - `frontend/src/components/game/PenetrationChallengePanel.vue`
  - `frontend/src/views/CompetitionDetailView.vue`
  - `frontend/src/api/noctf.ts`
  - `frontend/src/api/queryKeys.ts`
  - `frontend/src/locales/zh-CN.json`
  - `frontend/src/locales/en.json`
- 前置依赖：
  - TODO-16
  - TODO-17
- 任务说明：
  - 让选手在题目弹窗中启动环境、查看入口、查看阶段进度、提交多个阶段 Flag、重置/销毁环境。
- 实现要点：
  - `ChallengeModal` 检测 `challenge.typeId` 为 `Penetration` 时渲染独立 `PenetrationChallengePanel`。
  - 面板区域：
    - 授权范围和注意事项。
    - 环境状态：not started / starting / running / failed / stopped / expired。
    - 入口地址：只在 running 且 `visibleEntryAfterStart` 时显示，支持复制。
    - 操作按钮：启动、停止、重置、销毁；reset 显示剩余次数和冷却。
    - 阶段列表：stage、name、score、solved、solvedAt、hint after solved。
    - Flag 提交框：提交后更新阶段列表和排行榜。
  - 状态流转：
    - start 后进入 starting，轮询 instance API。
    - running 显示入口。
    - reset 后清空 entry 并轮询直到 running/failed。
    - failed 显示错误摘要和重试/销毁。
  - 加载态：首次详情加载 skeleton；操作按钮 loading。
  - 错误态：API code 显示本地化文案，如 `instance_busy`、`reset_limit_exceeded`、`competition_ended`。
  - 权限限制：未审批队伍显示报名/等待审批提示，不显示启动入口。
- 验收标准：
  - 选手可以完成 start -> copy entry -> submit stage1 -> submit stage2 -> reset -> destroy。
  - 第一阶段解出不会把整题标记为完全 solved，除非所有 visible flags 已解。
  - 移动端按钮和入口地址不溢出。
- 风险点：
  - 现有 `ChallengeModal.vue` 已包含 CTF/AWDP/AWD 分支，建议拆组件降低复杂度。

### TODO-29: 调整挑战卡片和排行榜展示

- 优先级：P1
- 负责方向：Frontend
- 涉及文件：
  - `frontend/src/components/game/ChallengeCard.vue`
  - `frontend/src/views/CompetitionDetailView.vue`
  - `frontend/src/components/game/ScoreboardView.vue`
  - `frontend/src/stores/score.ts`
- 前置依赖：
  - TODO-11
  - TODO-28
- 任务说明：
  - 让综合渗透题的多阶段进度在卡片和题目列表中合理展示。
- 实现要点：
  - `ChallengeDto` 扩展：`totalStageCount`、`solvedStageCount`、`totalScore`、`fullSolveCount`。
  - 卡片显示 `solvedStageCount / totalStageCount`，points 显示总分或剩余分值。
  - solved badge 仅在全部 visible stages solved 后显示。
  - 排行榜已有 `ScoreEvent` 聚合，不需单独改总分；可在团队详情中显示 stage score breakdown。
  - 普通题卡片保持原 UI。
- 验收标准：
  - 综合渗透题不会在一个阶段完成后被隐藏于“隐藏已解出”。
  - 总分与阶段得分之和一致。
- 风险点：
  - 如果后端 `GetSubmissionsEndpoint` 不扩展，前端无法准确判断 full solved。

## 13. 权限、安全与审计 TODO

### TODO-30: 综合渗透权限校验基线

- 优先级：P0
- 负责方向：Security / Backend
- 涉及文件：
  - `backend/src/NoCTF.API/Permissions/*`
  - `backend/src/NoCTF.API/Endpoints/Admin/Penetration*`
  - `backend/src/NoCTF.API/Endpoints/Competitions/Penetration*`
  - `backend/tests/NoCTF.Tests/PenetrationPermissionTests.cs`
- 前置依赖：
  - TODO-15
  - TODO-16
- 任务说明：
  - 为综合渗透所有 API 加明确权限边界。
- 实现要点：
  - 普通选手只能查看自己的实例。
  - 普通选手不能查看其他队伍入口。
  - 普通选手不能查看真实 Flag 或动态 Flag 明文。
  - 普通选手不能访问管理端 API。
  - 管理员可以查看和管理所有实例。
  - 出题人/Organizer 只能管理自己有权限的比赛题目，必须使用 `CanManageCompetitionAsync`。
  - 比赛结束后选手不能启动、重启、重置或提交 Flag；可允许销毁自己的实例。
  - 环境重置和 Flag 提交必须有频率限制。
  - 所有关键操作写入审计日志。
- 验收标准：
  - 安全测试覆盖跨队、跨比赛、未登录、未审批、封禁队伍、普通用户访问 admin。
  - `.IgnoreQueryFilters()` 查询均手动带 `CompetitionId`。
- 风险点：
  - 只依赖前端隐藏按钮会导致越权。

### TODO-31: 审计日志和比赛日志规范

- 优先级：P0
- 负责方向：Backend / Security
- 涉及文件：
  - `backend/src/NoCTF.API/AuditLogWriter.cs`
  - `backend/src/NoCTF.API/CompetitionLogWriter.cs`
  - `backend/src/NoCTF.API/Endpoints/Admin/GetAuditLogsEndpoint.cs`
  - `backend/src/NoCTF.API/Endpoints/Admin/CompetitionSecurityEndpoints.cs`
  - `frontend/src/views/admin/AdminAuditLogsView.vue`
  - `frontend/src/views/admin/AdminCompetitionDetailView.vue`
- 前置依赖：
  - TODO-15
  - TODO-17
- 任务说明：
  - 记录综合渗透关键操作和异常，用于运维排障和赛后追溯。
- 实现要点：
  - 记录事件：
    - `penetration.instance.started`
    - `penetration.instance.stopped`
    - `penetration.instance.reset`
    - `penetration.instance.destroyed`
    - `penetration.instance.start_failed`
    - `penetration.instance.container_exited`
    - `penetration.instance.resource_limit`
    - `penetration.flag.accepted`
    - `penetration.flag.rejected`
    - `penetration.flag.rate_limited`
    - `penetration.flag.suspected_cross_team`
    - `penetration.admin.instance_reset`
    - `penetration.admin.instance_destroy`
    - `penetration.unauthorized_access`
  - `CompetitionLog` 用于比赛内可观察事件，带 teamId、challengeId、metadata。
  - `AuditLog` 用于敏感操作，带 actor、IP、endpoint、target、diff/newValues。
  - 日志 metadata 只保存 flagLength、flagHashPrefix、flagId、stage，不保存明文 Flag。
  - 管理端比赛日志增加 event type 筛选；全局审计日志增加 action 筛选即可复用。
  - 如需要导出，P2 实现 CSV 导出。
- 验收标准：
  - 每个实例操作和提交都有至少一条比赛日志。
  - 管理员 reset/destroy 有全局审计。
  - 错误 Flag 明文不出现在 AuditLog JSON。
- 风险点：
  - 容器日志可能自带 Flag，日志展示层需要二次脱敏。

## 14. 测试 TODO

### TODO-32: 后端单元测试

- 优先级：P0
- 负责方向：Test / Backend
- 涉及文件：
  - `backend/tests/NoCTF.Tests/PenetrationTopologyServiceTests.cs`
  - `backend/tests/NoCTF.Tests/PenetrationComposeBuilderTests.cs`
  - `backend/tests/NoCTF.Tests/PenetrationFlagServiceTests.cs`
  - `backend/tests/NoCTF.Tests/PenetrationScoringTests.cs`
  - `backend/tests/NoCTF.Tests/PenetrationPermissionTests.cs`
- 前置依赖：
  - TODO-8
  - TODO-10
  - TODO-11
- 任务说明：
  - 覆盖综合渗透核心纯逻辑和安全边界。
- 实现要点：
  - 数据模型测试：唯一索引、租户字段、模板/比赛克隆。
  - 动态 Flag 生成测试：不同队伍不同值，reset 后旧值失效。
  - Flag 校验测试：固定、动态、重复、错误、跨队动态 Flag。
  - 计分测试：多阶段独立得分、单阶段首杀、重复不计分。
  - 权限校验测试：未登录、未审批、封禁、跨队、跨比赛。
  - 状态机测试：start/reset/destroy 并发和非法状态转换。
- 验收标准：
  - `dotnet test backend/tests/NoCTF.Tests` 通过。
  - 新增测试不依赖真实 Docker，Compose builder 测试只校验 YAML 和 labels。
- 风险点：
  - EF InMemory 不支持 Postgres partial index，唯一约束相关建议使用 sqlite 或 Testcontainers；若项目暂未使用 Testcontainers，至少补服务层幂等测试。

### TODO-33: 后端集成测试

- 优先级：P1
- 负责方向：Test / Backend / Infra
- 涉及文件：
  - `backend/tests/NoCTF.Tests/PenetrationApiIntegrationTests.cs`
  - `backend/tests/NoCTF.Tests/TestDoubles/*`
- 前置依赖：
  - TODO-15
  - TODO-16
  - TODO-17
- 任务说明：
  - 覆盖从 API 到服务的主要链路，容器管理使用 fake `IContainerManager`。
- 实现要点：
  - 创建综合渗透题模板。
  - 绑定到 CTF 比赛。
  - 创建队伍并审批。
  - 启动队伍实例。
  - 获取入口。
  - 提交正确 stage1 Flag。
  - 提交错误 Flag。
  - 重复提交 stage1。
  - 提交 stage2。
  - 重置实例。
  - 销毁实例。
  - 管理员查看实例。
  - 比赛结束后禁止提交和启动。
- 验收标准：
  - API 状态码和响应 code 与 TODO-13 到 TODO-17 一致。
  - fake container manager 记录 ComposeUp/Down 调用次数，验证无重复创建。
- 风险点：
  - FastEndpoints 测试启动插件目录可能缺 DLL，需要测试宿主显式注册 services。

### TODO-34: 回归测试

- 优先级：P0
- 负责方向：Test
- 涉及文件：
  - `backend/tests/NoCTF.Tests/CtfScoreCalculatorTests.cs`
  - `backend/tests/NoCTF.Tests/CtfFlagFormattingTests.cs`
  - `backend/tests/NoCTF.Tests/DynamicScoringCalculatorTests.cs`
  - `backend/tests/NoCTF.Tests/ChallengeSolveCountsTests.cs`
  - `backend/tests/NoCTF.Tests/AwdpGameModeTests.cs`
- 前置依赖：
  - TODO-5
  - TODO-10
  - TODO-11
- 任务说明：
  - 确保新增综合渗透题不破坏原有题型。
- 实现要点：
  - 普通 Web 静态 Flag。
  - 普通 Pwn 动态容器题。
  - 普通 CTF `[UUID]` 动态 Flag。
  - 原有动态计分和排行榜。
  - 原有 first blood。
  - AWDP Break/Fix 状态。
  - AWD/KoH 基础测试保持通过。
- 验收标准：
  - 现有测试全部通过。
  - 普通 CTF `Submission` 唯一约束仍有效。
- 风险点：
  - 修改 `Submission` 索引和 `GetSubmissionsEndpoint` 最容易影响隐藏已解出、solve count 和排行榜。

### TODO-35: 前端验证与构建

- 优先级：P1
- 负责方向：Test / Frontend
- 涉及文件：
  - `frontend/src/components/game/PenetrationChallengePanel.vue`
  - `frontend/src/components/admin/*Penetration*.vue`
  - `frontend/src/locales/*.json`
- 前置依赖：
  - TODO-25
  - TODO-28
- 任务说明：
  - 验证管理端和选手端综合渗透 UI 不破坏现有页面。
- 实现要点：
  - 运行 `cd frontend && bun run build`。
  - 如修改 ESLint 相关文件，运行相关文件 lint。
  - 手动浏览：
    - 管理端创建模板。
    - 比赛内绑定题目。
    - 选手启动实例。
    - 提交多个阶段 Flag。
    - 移动端宽度下入口地址和按钮不溢出。
  - 如果引入组件测试，保持依赖最小，不为该功能引入大型 UI 测试框架。
- 验收标准：
  - 前端构建通过。
  - 浏览器控制台无新增错误。
  - 中英文切换无缺失 key。
- 风险点：
  - 现有 full lint 可能有历史问题；最终报告要区分新增与既有问题。

## 15. 文档 TODO

### TODO-36: 新增综合渗透题作者文档

- 优先级：P0
- 负责方向：Docs
- 涉及文件：
  - `docs/penetration-challenges.md`
  - `docs/game-modes.md`
  - `README.md`
- 前置依赖：
  - TODO-19
  - TODO-23
- 任务说明：
  - 编写管理员和出题人配置综合渗透题的文档。
- 实现要点：
  - 明确综合渗透题不是 AWD/AWDP/KOH。
  - 管理员使用说明：创建题库模板、配置拓扑、绑定比赛、测试部署、监控实例。
  - 出题人配置说明：镜像要求、入口节点、内部节点、环境变量注入、资源限制。
  - YAML/JSON 配置示例：
    - 单入口 Web + DB。
    - Web + 内网服务 + 提权节点。
    - 三阶段动态 Flag。
  - 动态 Flag 配置示例：env 注入、flag prefix、reset 后变更。
  - 选手规则说明：只允许攻击本队入口和环境内授权目标。
  - 禁止行为说明：禁止攻击其他队伍、平台、真实公网目标。
- 验收标准：
  - 新 Agent 不读代码也能配置一个 MVP 综合渗透题。
  - 文档明确 Docker Compose 是 MVP 支持路径。
- 风险点：
  - 文档若不强调授权范围，选手可能误解为可互攻。

### TODO-37: 新增运维和故障排查文档

- 优先级：P1
- 负责方向：Docs / Infra
- 涉及文件：
  - `docs/deployment.md`
  - `docs/penetration-operations.md`
  - `.env.example`
- 前置依赖：
  - TODO-12
  - TODO-18
- 任务说明：
  - 说明综合渗透环境部署、资源、安全和排障。
- 实现要点：
  - 环境变量：public host、instance ttl、max concurrent starts、allowed registries。
  - Docker/Runner 部署要求：Runner 隔离、Docker socket 风险、镜像来源白名单。
  - 常见故障：
    - image pull 失败。
    - entry port 不显示。
    - compose down 残留网络/卷。
    - reset 后旧 Flag 仍可用。
    - 容器日志脱敏。
  - K8s 未实现说明和后续路线。
- 验收标准：
  - 运维可根据文档定位实例启动失败。
  - 文档不宣称当前没有实现的 K8s 能力。
- 风险点：
  - 当前 docker-compose 中 backend 仍挂 Docker socket，文档需如实写明风险和推荐演进。

### TODO-38: 更新 API 文档和 OpenAPI 客户端

- 优先级：P1
- 当前状态：已完成。`docs/api.md` 已补 Penetration API，已运行 `bun run fetch-openapi` 和 `bun run generate-api` 更新 `frontend/src/api/generated/*`。
- 负责方向：Docs / Backend / Frontend
- 涉及文件：
  - `docs/api.md`
  - `frontend/swagger.json`
  - `frontend/src/api/generated/*`
- 前置依赖：
  - TODO-13
  - TODO-17
- 任务说明：
  - 更新 API 文档，并同步前端 OpenAPI 生成客户端。
- 实现要点：
  - `docs/api.md` 增加 Admin Penetration API 和 Player Penetration API。
  - 后端启动后运行 `cd frontend && bun run fetch-openapi && bun run generate-api`。
  - 如生成代码格式被 patch 脚本调整，确认 diff 只来自 OpenAPI 变化。
- 验收标准：
  - `frontend/src/api/generated` 与后端 DTO 一致。
  - 手写 `adminApi` / `competitionApi` 封装和 generated 类型不冲突。
- 风险点：
  - 若本地后端未运行，生成客户端会失败；实现 Agent 应按 `docs/development.md` 启动后端。

## 16. 迁移与兼容性 TODO

### TODO-39: 数据库迁移和回滚策略

- 优先级：P0
- 当前状态：已完成。已添加 `20260704020000_AddPenetrationChallengeSupport` migration，并同步更新 `ApplicationDbContextModelSnapshot.cs`。
- 负责方向：Database
- 涉及文件：
  - `backend/src/NoCTF.Infrastructure/Migrations/*`
  - `backend/src/NoCTF.Infrastructure/ApplicationDbContextModelSnapshot.cs`
- 前置依赖：
  - TODO-1
  - TODO-5
- 任务说明：
  - 生成 EF Core migration，确保旧数据可迁移，普通题型兼容。
- 实现要点：
  - 添加新表时设置必要外键或应用层级联删除策略。
  - `Submission.PenetrationFlagId` 可空，旧数据保持 null。
  - 删除旧 `ux_submissions_correct_once` 后创建两个新 partial unique index。
  - 给新 JSON 字段设置默认 `{}` 或 `[]`，避免 null 判断散落。
  - 对 `Challenge.TypeId` 规范化不做数据批量改写，避免影响现有 `Ctf/Awd/Awdp/Koh` 大小写。
- 验收标准：
  - 空库迁移成功。
  - 带旧 CTF submissions 的库迁移成功。
  - 迁移后普通 CTF 重复正确提交仍被拒绝。
- 风险点：
  - Postgres partial index 语法大小写需要与 EF 生成列名一致。

### TODO-40: 保持现有动态容器 API 兼容

- 优先级：P0
- 负责方向：Backend / Frontend
- 涉及文件：
  - `backend/src/NoCTF.API/Endpoints/Competitions/ChallengeInstanceEndpoints.cs`
  - `frontend/src/components/game/ChallengeModal.vue`
- 前置依赖：
  - TODO-16
- 任务说明：
  - 不破坏当前普通动态容器题使用 `/instance` 的行为。
- 实现要点：
  - 普通动态容器继续使用 `AwdGameBox` 和 `CtfDynamicFlag`。
  - 综合渗透题使用 `/penetration/instance` 专用 API 和 `TeamChallengeInstance`。
  - 如果用户误调用普通 `/instance` 启动 Penetration，返回 400 `use_penetration_instance_api`。
  - 前端按 `typeId` 分流，不让 Penetration 走普通动态容器按钮。
- 验收标准：
  - 普通 dynamic container 题启动/延长/销毁照常工作。
  - Penetration 题不会创建 `AwdGameBox`。
- 风险点：
  - 两套 API 状态字段相似，前端类型要区分。

## 17. 风险清单

- 数据模型风险：旧 `Submission` 唯一索引按 challenge 维度限制正确提交，必须迁移，否则多阶段得分不可用。
- 安全风险：综合渗透环境多容器、多网络，若 Compose 安全校验不足，可能暴露宿主机、平台内网或其他队伍环境。
- 权限风险：当前部分 Admin Endpoint 只校验角色，综合渗透管理 API 必须使用比赛级权限。
- 运维风险：API 当前仍注册直接 DockerManager，新增同步编排会扩大 API Docker 权限面；应优先走 Runner 或明确后续拆分。
- 日志泄露风险：容器日志、审计 JSON、错误提交可能包含 Flag，必须脱敏。
- 状态一致性风险：Docker 操作不在 DB 事务内，启动/重置失败必须有回滚和状态修复。
- 前端复杂度风险：`ChallengeModal.vue` 和 `AdminCompetitionDetailView.vue` 已较大，新增功能应拆组件。
- K8s 误用风险：项目有 K8s manifests 但 Provider 为空，MVP 文档必须明确不支持 K8s 编排。
- 兼容风险：普通 CTF solve count、隐藏已解出、排行榜可能受多阶段提交影响，需要回归测试。

## 18. 最小可用版本 MVP

MVP 必须完成：

- TODO-1 到 TODO-6：数据模型和迁移。
- TODO-7 到 TODO-11：服务、实例、Flag、计分。
- TODO-13 到 TODO-17：核心管理端和选手端 API。
- TODO-19 到 TODO-21：Docker Compose 安全编排、状态机、清理。
- TODO-23：动态 Flag 环境变量注入。
- TODO-25、TODO-26、TODO-28、TODO-29：管理端和选手端基础 UI。
- TODO-30 到 TODO-34：权限、审计和核心测试。
- TODO-36、TODO-39、TODO-40：作者文档、迁移、兼容。

MVP 可暂缓：

- 图形化拓扑编辑器，先使用结构化 JSON/YAML + 表格。
- K8s Provider。
- CSV 导出。
- 文件注入型动态 Flag。
- 复杂 healthcheck 展示和日志聚合优化。

## 19. 后续增强版本

- P1：管理端实例监控页完善节点级日志、筛选、批量销毁。
- P1：Runner 支持 compose status/logs 标准接口，API 不再直接操作 Docker。
- P1：Redis 级提交限流和实例启动限流。
- P1：更细的资源配额：每队/每题/每比赛最大 CPU、内存、实例数。
- P2：可视化拓扑编辑器，支持拖拽节点和网络。
- P2：动态 Flag 文件注入，配套只读 secret volume。
- P2：Kubernetes Provider：Namespace、NetworkPolicy、Service、Ingress、ResourceQuota。
- P2：实例快照/恢复策略。
- P2：更细的阶段提示解锁规则。

## 20. Agent 分工建议

- Agent A - Database/Domain：负责 TODO-1 到 TODO-6、TODO-39。
- Agent B - Plugin Services：负责 TODO-7 到 TODO-12、TODO-23、TODO-24，确保综合渗透业务留在 `NoCTF.Plugins.Penetration`。
- Agent C - API/Security：负责 TODO-13 到 TODO-18、TODO-30、TODO-31、TODO-40，API 只做薄适配和权限校验，不直接引用插件项目。
- Agent D - Infra/Runner：负责 TODO-18 到 TODO-22、TODO-37。
- Agent E - Frontend Admin：负责 TODO-25 到 TODO-27。
- Agent F - Frontend Player：负责 TODO-28、TODO-29。
- Agent G - Tests/Docs：负责 TODO-32 到 TODO-38，并在每个 Agent 完成后跑回归。

推荐实施顺序：

1. 先完成数据模型、迁移和测试骨架。
2. 再完成后端服务和 fake container manager 集成测试。
3. 然后接管理端 API 和选手端 API。
4. 再接前端 UI。
5. 最后做真实 Docker Compose 冒烟、文档和回归。

## 21. 交接提示词

```text
你是接手 NoCTF 综合渗透题功能的实现 Agent。请先阅读 E:\SourceCode\NoCTF\TODO.md、README.md、docs/architecture.md、docs/handoff.md、docs/game-modes.md、docs/development.md，并重点查看以下现有代码：

- backend/src/NoCTF.Core/Entities.cs
- backend/src/NoCTF.Infrastructure/ApplicationDbContext.cs
- backend/src/NoCTF.API/Endpoints/Competitions/SubmitFlagEndpoint.cs
- backend/src/NoCTF.API/Endpoints/Competitions/ChallengeInstanceEndpoints.cs
- backend/src/NoCTF.API/Endpoints/Admin/CreateChallengeEndpoint.cs
- backend/src/NoCTF.API/Endpoints/Admin/CompetitionChallengeEndpoints.cs
- backend/src/NoCTF.Plugins.CTF/CtfGameMode.cs
- backend/src/NoCTF.Container.Docker/DockerManager.cs
- backend/src/NoCTF.Container.Docker/DockerComposeRunner.cs
- backend/src/NoCTF.Runner.Client/RunnerContracts.cs
- frontend/src/components/game/ChallengeModal.vue
- frontend/src/components/admin/ChallengeTemplateForm.vue
- frontend/src/views/admin/AdminCompetitionDetailView.vue
- frontend/src/api/noctf.ts

目标：按 TODO.md 实现“综合渗透题 / Penetration Challenge”。它是 CTF/Jeopardy 下的新题型，不是 AWD、AWDP、KOH 或队伍互攻模式。每个队伍只攻击自己的授权靶场实例；一道题支持多容器、多网络、多阶段 Flag、动态 Flag、队伍级实例启动/停止/重置/销毁、阶段计分和审计。

关键约束：

1. 不新增队伍互相攻击逻辑。
2. 不实现服务可用性计分、补丁上传、防守得分或占领持续得分。
3. 不破坏普通 CTF、普通动态容器、AWD、AWDP、KoH。
4. 不把多容器实例塞进 AwdGameBox；新增综合渗透实例表。
5. 必须调整 Submission 唯一索引以支持同题多阶段正确提交。
6. 必须保持插件架构：综合渗透业务在 `NoCTF.Plugins.Penetration`，API 不得直接引用该插件项目。
7. API 只能依赖 `NoCTF.PluginBase` / `NoCTF.Application` 的通用题型 registry，不能硬编码 penetration 业务分支。
8. 管理端 API 必须校验比赛级 CanManageCompetitionAsync。
9. 选手端 API 必须只允许访问本队实例和本队动态 Flag。
10. 动态 Flag、审计日志、容器日志不得泄露明文 Flag。
11. MVP 使用 Docker Compose；Kubernetes Provider 当前未实现，不要宣称支持。
12. 每个实现步骤都要补测试或说明无法测试的原因。

建议从 TODO-1 到 TODO-6 开始，先完成数据模型、迁移和提交唯一索引调整；然后先做 TODO-7 的通用题型插件扩展点，再实现 TODO-8 到 TODO-11 的综合渗透插件服务；API、前端、文档和测试按 TODO.md 的优先级推进。

完成每个阶段后请运行：

- dotnet build backend/NoCTF.slnx
- dotnet test backend/tests/NoCTF.Tests
- cd frontend && bun run build

如果修改了后端 DTO 或新增 API，请启动后端后运行：

- cd frontend && bun run fetch-openapi && bun run generate-api

最终交付请说明已完成的 TODO 编号、修改文件、测试结果、剩余风险。
```
