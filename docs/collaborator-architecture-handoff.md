# NoCTF 协作者交接文档

本文面向接手继续开发 NoCTF 的协作者，目标是让你快速理解项目架构、模块边界、核心业务流和当前开发注意事项。请先读完本文，再动代码。

## 1. 当前仓库状态

截至本交接文档写入时：

- 当前分支：`main`
- 最新提交：`6c1fc7d style: redesign AWDP command screen`
- 工作区存在未提交改动，涉及后端安全、权限、管理端、Penetration 插件与测试等文件。
- 接手后第一件事请运行：

```powershell
git status --short
git log -5 --oneline
```

不要直接执行 `git reset --hard`、`git checkout -- .` 或批量删除未跟踪文件。当前未提交改动可能是上一位开发者的有效工作。

## 2. 项目定位

NoCTF 是一个插件化 CTF 竞赛平台，支持：

- CTF / Jeopardy
- AWD
- AWDP
- KoH
- Penetration Challenge

核心设计目标：

- 平台核心负责通用能力：认证、租户隔离、插件注册、容器调度、文件存储、统一计分、排行榜、审计和事件分发。
- 具体比赛模式由插件负责：提交处理、轮次逻辑、模式状态、模式专属检查与模式视图。
- 题库只保存可复用资产，比赛侧保存分值、衰减、轮次和策略。

## 3. 技术栈

后端：

- .NET 8
- FastEndpoints
- SignalR
- EF Core
- PostgreSQL
- Redis
- Docker Engine
- MinIO / S3 兼容存储

前端：

- Vue 3
- TypeScript
- Vite
- Bun
- Tailwind CSS
- reka-ui / shadcn-vue 风格组件
- Pinia
- TanStack Vue Query
- vue-i18n
- SignalR client

部署：

- `deploy/docker-compose.yml` 提供本地集成栈
- `deploy/k8s` 预留 Kubernetes 部署资源

## 4. 目录结构

```text
NoCTF/
├── backend/
│   ├── src/
│   │   ├── NoCTF.API/
│   │   ├── NoCTF.Application/
│   │   ├── NoCTF.Core/
│   │   ├── NoCTF.Infrastructure/
│   │   ├── NoCTF.PluginBase/
│   │   ├── NoCTF.Container.Docker/
│   │   ├── NoCTF.Container.K8s/
│   │   ├── NoCTF.Runner/
│   │   ├── NoCTF.Runner.Client/
│   │   ├── API-hosted Channel consumers/
│   │   ├── NoCTF.Plugins.CTF/
│   │   ├── NoCTF.Plugins.AWD/
│   │   ├── NoCTF.Plugins.AWDP/
│   │   ├── NoCTF.Plugins.KoH/
│   │   └── NoCTF.Plugins.Penetration/
│   └── tests/NoCTF.Tests/
├── frontend/
│   └── src/
│       ├── api/
│       ├── components/
│       ├── composables/
│       ├── i18n/
│       ├── lib/
│       ├── locales/
│       ├── mocks/
│       ├── router/
│       ├── stores/
│       ├── types/
│       └── views/
├── deploy/
├── docs/
└── README.md
```

## 5. 后端分层架构

### 5.1 NoCTF.API

位置：

```text
backend/src/NoCTF.API
```

职责：

- FastEndpoints HTTP API
- JWT 登录、注册、刷新
- SignalR Hub
- 租户解析中间件
- 权限服务
- 审计日志后处理器
- 健康检查
- 插件加载入口
- 静态文件和 SPA 托管

典型目录：

```text
Endpoints/
  Admin/
  Auth/
  Competitions/
  Teams/
SignalR/
Permissions/
Plugins/
Logging/
```

入口文件：

```text
backend/src/NoCTF.API/Program.cs
```

重要启动流程：

1. 配置 CORS。
2. 配置 JWT。
3. 配置 SignalR 和 Redis backplane。
4. 注册应用服务、排行榜、后台任务、文件校验。
5. 注册 EF Core `ApplicationDbContext`。
6. 注册 `IStorage:Provider`。
7. 注册 Docker 容器管理器。
8. 调用 `PluginLoader.LoadAndRegisterAll(...)` 加载插件。
9. 执行数据库迁移和数据种子。
10. 映射 FastEndpoints、Swagger、Health、SignalR Hubs、SPA。

### 5.2 NoCTF.Core

位置：

```text
backend/src/NoCTF.Core
```

职责：

- 领域实体
- 枚举
- 租户实体接口
- 比赛、队伍、题目、提交、分数、轮次、插件状态等核心模型

核心实体包括：

- `User`
- `Team`
- `TeamMember`
- `Competition`
- `ChallengeTemplate`
- `Challenge`
- `Submission`
- `ScoreEvent`
- `ScoreSignal`
- `AwdRound`
- `AwdGameBox`
- `AwdCheckResult`
- `AwdpRound`
- `AwdpTeamChallengeState`
- `AwdpRoundScore`
- `AwdpPatchSubmission`
- `KohControlRecord`

题库与比赛题目要区分：

- `ChallengeTemplate` 是题库模板，保存复用资产。
- `Challenge` 是投放到某场比赛后的题目，保存比赛上下文里的题目状态和策略引用。

不要把比赛分值策略随意塞回 `ChallengeTemplate`。

### 5.3 NoCTF.Infrastructure

位置：

```text
backend/src/NoCTF.Infrastructure
```

职责：

- EF Core `ApplicationDbContext`
- 数据库迁移
- 租户上下文
- 数据种子
- 存储提供者相关实现

多租户隔离在 `ApplicationDbContext` 中通过 EF Core 全局查询过滤器实现。所有实现 `ITenantEntity` 的实体都会按当前 `CompetitionId` 自动过滤。

当你确实需要跨比赛查询时，显式使用：

```csharp
.IgnoreQueryFilters()
```

但不要在普通玩家接口里随意使用它。管理员端、后台轮次引擎、排行榜聚合、容器清理等场景才常见。

### 5.4 NoCTF.Application

位置：

```text
backend/src/NoCTF.Application
```

职责：

- 跨插件共享的应用服务
- 比赛模式抽象
- 排行榜服务
- 计分服务
- 后台任务队列
- 安全校验工具
- 事件处理

重要文件：

```text
CompetitionModes/CompetitionModeAbstractions.cs
Scoring/
Leaderboard/
BackgroundTasks/
Security/
Events/
```

这里可以放跨模式通用逻辑，但不要把 AWDP、AWD、KoH 的专属业务写进 Application 的通用服务里。

### 5.5 NoCTF.PluginBase

位置：

```text
backend/src/NoCTF.PluginBase
```

职责：

- 插件与平台之间的稳定契约
- 容器管理接口
- 存储接口
- 游戏模式接口
- 挑战类型接口

重要接口：

```csharp
IGameMode
IChallengeType
IContainerManager
IContainerProvider<TClient, TMetadata>
IStorage:Provider
IPluginModule
```

容器配置模型：

```csharp
ContainerConfig
ContainerInstance
ContainerRunResult
ComposeConfig
ComposeDeployment
```

### 5.6 NoCTF.Container.Docker

位置：

```text
backend/src/NoCTF.Container.Docker
```

职责：

- Docker Engine API 封装
- 创建和销毁容器
- 运行一次性容器
- Docker Compose 校验和运行
- 端口映射读取
- 镜像拉取

插件不应该直接依赖 Docker.DotNet，而是通过 `IContainerManager` 调度。

### 5.7 API-hosted Channel consumers

位置：

```text
backend/src/API-hosted Channel consumers
```

职责：

- 后台任务执行
- 过期实例清理
- 与 API 共享基础设施和插件能力

如果你新增需要异步执行的模式任务，优先接入已有 `BackgroundTaskQueue` 和 `ICompetitionJobHandler` 模型。

## 6. 插件系统

插件通过 `IPluginModule` 注册服务。当前 API 启动时调用：

```csharp
PluginLoader.LoadAndRegisterAll(builder.Services, builder.Configuration);
```

插件加载器查找：

```text
plugins/NoCTF.Plugins.*.dll
```

然后找到实现 `IPluginModule` 的类型并调用：

```csharp
module.ConfigureServices(services);
```

### 6.1 比赛模式插件

当前模式插件：

```text
NoCTF.Plugins.CTF
NoCTF.Plugins.AWD
NoCTF.Plugins.AWDP
NoCTF.Plugins.KoH
NoCTF.Plugins.Penetration
```

模式插件通常会注册：

- `IGameMode`
- `ICompetitionModeProvider`
- `ICompetitionFileActionProvider`
- 模式专属 service
- 模式专属后台 engine
- 模式专属 job handler

### 6.2 CompetitionModeProvider

`ICompetitionModeProvider` 是前后端扩展视图和动作的重要桥：

```csharp
bool CanHandleAction(string actionKey);
Task<CompetitionActionResult> HandleActionAsync(...);
bool CanProvideView(string viewKey);
Task<CompetitionViewResult> GetViewAsync(...);
```

文件上传类动作使用：

```csharp
ICompetitionFileActionProvider
```

例如 AWDP 的 patch 上传就是通过 `submit-patch` file action 进入插件。

### 6.3 插件边界原则

必须遵守：

- 平台核心不写 `if mode == awdp` 的业务分支。
- AWDP 不写成 AWD 子模式。
- AWD、AWDP、KoH、Penetration 的模式状态和流程放在各自插件里。
- 平台核心只提供通用基础设施。

## 7. 游戏模式架构

### 7.1 CTF

位置：

```text
backend/src/NoCTF.Plugins.CTF
```

核心职责：

- 普通 flag 校验
- 一题一队一次正确提交
- 动态分值衰减
- 一血记录
- 动态 flag 相关校验

计分通过 `ScoreEvent` 和排行榜服务聚合。

### 7.2 AWD

位置：

```text
backend/src/NoCTF.Plugins.AWD
```

核心职责：

- 轮次引擎
- 每队每题 gamebox
- 每轮 flag 生成
- checker 容器检查服务状态
- 攻击记录
- 轮次结算

核心文件通常包括：

- `AwdGameMode`
- `AwdRoundEngine`
- `AwdCheckerService`
- `AwdScoreEngine`
- `AwdFlagService`

### 7.3 AWDP

位置：

```text
backend/src/NoCTF.Plugins.AWDP
```

AWDP 的正确模型：

```text
Break + Fix + 轮次得分 + 次数限制
```

核心点：

- 没有起始分。
- 总分来自每轮增量累计。
- Break 成功后，后续有效轮次获得攻击分。
- Fix 成功后，后续有效轮次获得防御分。
- FixFailed 默认不扣分。
- 服务异常是否扣分由 AWDP 配置决定。
- 违规是否扣分由 AWDP 配置决定。

核心类：

- `AwdpGameMode`
- `AwdpModeProvider`
- `AwdpPatchService`
- `AwdpPatchValidationJobHandler`
- `AwdpRoundEngine`
- `AwdpScoreEngine`
- `AwdpConfigResolver`
- `AwdpStateService`

核心状态：

- `AwdpInstanceStatus`
- `AwdpBreakStatus`
- `AwdpFixStatus`
- `AwdpServiceStatus`
- `AwdpRoundStatus`

当前重要待办：

- AWDP 应改为单一 `check 容器` 模型。
- 平台负责运行 check 脚本。
- 出题人提供 check 脚本和 check 容器。
- check 返回码统一映射：
  - `0`：修复成功
  - `1`：EXP 利用成功
  - `2`：邪修
  - `3`：交互异常
  - 超时：服务异常
- 选手只能看到：
  - 防御成功
  - 防御异常：EXP利用成功
  - 防御异常：服务异常
- 平台需要把选手提交的 patch 包 URL 同步传给 check 容器。
- AWDP 还需要 patch 包模板上传/下载功能。

这些具体待办另见根目录 `TODO.md`，如果该文件存在。

### 7.4 KoH

位置：

```text
backend/src/NoCTF.Plugins.KoH
```

核心职责：

- 启动公共 hill 容器
- 轮询 agent 状态
- 记录当前控制者
- 按控制时长或轮询间隔加分

KoH 不走传统 flag 提交流程。

### 7.5 Penetration Challenge

位置：

```text
backend/src/NoCTF.Plugins.Penetration
```

定位：

- 这是 CTF/Jeopardy 下的一种题型扩展。
- 通常表现为每队一套 Docker Compose 靶场。
- 支持拓扑、阶段 flag、动态 flag、实例生命周期和分阶段计分。

注意：

- Penetration 不是 AWD/AWDP。
- 不要把 Penetration 的实例拓扑逻辑写进 CTF 插件核心。

## 8. 数据模型和租户

### 8.1 租户模型

`CompetitionId` 是租户边界。

实现 `ITenantEntity` 的实体会被 EF Core query filter 自动限制到当前比赛。

典型租户实体：

- `Team`
- `TeamMember`
- `Challenge`
- `Submission`
- `ScoreEvent`
- `ScoreSignal`
- `AwdRound`
- `AwdGameBox`
- `AwdpRound`
- `AwdpTeamChallengeState`
- `AwdpPatchSubmission`
- `KohControlRecord`

非租户或全局实体：

- `User`
- `ChallengeTemplate`
- 部分系统级配置

### 8.2 题库与比赛题目

题库模板：

```text
ChallengeTemplate
```

负责：

- 标题
- 描述
- 类型
- 附件 URL
- 容器镜像
- 容器模式
- Compose YAML
- 暴露端口
- checker / check 容器配置
- Flag 环境变量
- patch 模板 URL 等可复用资产

比赛题目：

```text
Challenge
```

负责：

- 所属比赛
- 对应题库模板
- 比赛内描述覆盖
- 分值配置
- 难度系数
- 一血奖励开关
- hints
- 模式特定比赛配置

原则：

- 题库只放资产。
- 比赛侧放分值、轮次、衰减、次数限制和策略。

### 8.3 计分模型

统一计分落库通常走：

```text
ScoreEvent
```

排行榜通过 `LeaderboardService` 和 Redis cache 聚合。

CTF：

- 以正确提交为主要事件。
- 根据 solve count 和 decay 配置动态计算分值。

AWD：

- 每轮按服务状态、攻击记录等结算。

AWDP：

- 每轮按 BreakSuccess 和 FixSuccess 是否有效结算。
- 没有初始分。
- `totalScore = sum(roundScore)`。

## 9. 容器与文件存储

### 9.1 容器调度

插件只调用：

```csharp
IContainerManager
```

不要在插件里直接调用 Docker SDK。

主要方法：

```csharp
CreateContainerAsync
DestroyContainerAsync
RunContainerAsync
ComposeUpAsync
ComposeDownAsync
```

动态容器实例由：

```text
AwdGameBox
```

跟踪，包含：

- `ContainerInstanceId`
- `PortMappingsJson`
- `ExpiresAt`
- `LastInstanceActionAt`

### 9.2 文件存储

统一走：

```csharp
IStorage:Provider
```

用途：

- 普通题目附件
- AWDP FixScript patch 包
- AWDP patch 模板
- 其他上传资产

本地存储会通过：

```text
/api/files
```

暴露下载。

## 10. API 架构

后端使用 FastEndpoints。

路径约定：

```text
/api/auth/*
/api/competitions/*
/api/teams/*
/api/admin/*
/api/health
```

管理员接口：

```text
backend/src/NoCTF.API/Endpoints/Admin
```

玩家/比赛接口：

```text
backend/src/NoCTF.API/Endpoints/Competitions
```

团队接口：

```text
backend/src/NoCTF.API/Endpoints/Teams
```

安全要求：

- 玩家接口必须从 JWT 获取 `UserId`。
- 需要队伍身份的接口必须验证队伍属于当前比赛。
- 需要报名状态的接口必须验证 approved。
- banned team 不应参与提交、计分、solve count。
- 管理端敏感操作要实现 `IAuditableEndpoint` 或写入 competition log。

## 11. SignalR 和实时通信

Hub：

```text
/hubs/leaderboard
/hubs/game
/hubs/monitor
```

用途：

- 排行榜实时刷新
- 比赛事件
- 管理端监控日志

SignalR 通过 Redis backplane 支持多实例扩展。

WebSocket JWT 可以通过 query string：

```text
access_token
```

传入。

## 12. 前端架构

位置：

```text
frontend/src
```

### 12.1 启动入口

```text
frontend/src/main.ts
```

注册：

- Pinia
- Vue Router
- vue-i18n
- TanStack Vue Query

### 12.2 路由

```text
frontend/src/router/index.ts
```

主要路由：

```text
/
/login
/register
/competitions
/competitions/:id
/competitions/:id/register
/competitions/:id/awd
/competitions/:id/koh
/awdp/screen/:gameId
/admin/*
```

路由守卫会检查：

- `requiresAuth`
- `requiresAdminOrOrganizer`
- `requiresAdmin`

### 12.3 API 层

```text
frontend/src/api/noctf.ts
frontend/src/api/generated
```

说明：

- 一部分 API 通过 OpenAPI 生成。
- 一部分动态接口手写 wrapper。
- `ApiError` 封装了 status 和 details。
- 修改后端接口 DTO 后，可能需要运行：

```powershell
cd frontend
bun run fetch-openapi
bun run generate-api
```

### 12.4 状态管理

```text
frontend/src/stores
```

常见 store：

- auth
- score

服务端数据主要使用 TanStack Vue Query。

### 12.5 组件分层

```text
components/ui
```

基础 UI 组件。

```text
components/layout
```

页面框架、布局、表格外壳、标题等。

```text
components/game
```

比赛和题目交互组件。

```text
components/admin
```

管理端复用组件，例如题目表单。

### 12.6 i18n

所有可见文案都应同步维护：

```text
frontend/src/locales/zh-CN.json
frontend/src/locales/en.json
```

不要只改中文或只改英文。

## 13. 管理端核心流程

### 13.1 题库

入口：

```text
/admin/challenges
/admin/challenges/new
```

相关文件：

```text
frontend/src/views/admin/AdminChallengesView.vue
frontend/src/views/admin/AdminChallengeCreateView.vue
frontend/src/components/admin/ChallengeTemplateForm.vue
```

后端：

```text
backend/src/NoCTF.API/Endpoints/Admin/CreateChallengeEndpoint.cs
backend/src/NoCTF.API/Endpoints/Admin/UpdateChallengeEndpoint.cs
backend/src/NoCTF.API/Endpoints/Admin/GetChallengesAdminEndpoint.cs
backend/src/NoCTF.API/Endpoints/Admin/ChallengeAttachmentEndpoint.cs
```

规则：

- 创建题目是完整页面，不是弹窗。
- 页面不要做成卡片堆叠，应是铺满工作区的表单。
- 选择题目类型后显示对应配置。
- CTF 资产形态包括静态附件、动态容器、静态容器。
- 只有容器题显示容器配置。
- 容器题题目镜像必填。
- 无附件时就是无附件，不需要伪造附件 URL。

### 13.2 比赛管理

入口：

```text
/admin/competitions
/admin/competitions/:id
```

相关文件：

```text
frontend/src/views/admin/AdminCompetitionsView.vue
frontend/src/views/admin/AdminCompetitionDetailView.vue
```

后端：

```text
backend/src/NoCTF.API/Endpoints/Admin/CreateCompetitionAdminEndpoint.cs
backend/src/NoCTF.API/Endpoints/Admin/UpdateCompetitionEndpoint.cs
backend/src/NoCTF.API/Endpoints/Admin/GetCompetitionsAdminEndpoint.cs
backend/src/NoCTF.API/Endpoints/Admin/CompetitionChallengeEndpoints.cs
```

比赛侧配置：

- 分值
- 分数衰减
- 轮次
- 攻击/防御每轮得分
- 尝试次数
- hints
- 题目投放
- 队伍审核

### 13.3 容器管理

入口：

```text
/admin/containers
```

后端：

```text
backend/src/NoCTF.API/Endpoints/Admin/GetContainersEndpoint.cs
backend/src/NoCTF.API/Endpoints/Admin/DestroyContainerEndpoint.cs
backend/src/NoCTF.API/Endpoints/Admin/ChallengeContainerAdminEndpoints.cs
```

用于查看、重启或销毁运行实例。

## 14. 玩家端核心流程

入口：

```text
/competitions
/competitions/:id
/competitions/:id/register
```

核心文件：

```text
frontend/src/views/CompetitionsView.vue
frontend/src/views/CompetitionGatewayView.vue
frontend/src/views/CompetitionDetailView.vue
frontend/src/views/CompetitionRegistrationView.vue
frontend/src/components/game/ChallengeCard.vue
frontend/src/components/game/ChallengeModal.vue
```

典型流程：

1. 登录。
2. 创建或加入队伍。
3. 报名比赛。
4. 等待审核通过。
5. 进入比赛。
6. 打开题目。
7. 下载附件或创建动态实例。
8. 提交 flag。
9. AWD/AWDP 模式下执行攻击、防御和 patch 上传。

## 15. AWDP 当前重点交接

AWDP 是当前最容易误改的部分。请特别注意：

### 15.1 正确模型

```text
Break + Fix + 轮次得分 + 次数限制
```

不要实现：

- 起始分
- 初始分扣减
- 防御失败默认扣分
- AWDP 复用 AWD 实时互打页面
- AWDP 写入平台核心

### 15.2 已有实现入口

后端：

```text
backend/src/NoCTF.Plugins.AWDP/AwdpGameMode.cs
backend/src/NoCTF.Plugins.AWDP/AwdpModeProvider.cs
backend/src/NoCTF.Plugins.AWDP/AwdpPatchService.cs
backend/src/NoCTF.Plugins.AWDP/AwdpRoundEngine.cs
backend/src/NoCTF.Plugins.AWDP/AwdpScoreEngine.cs
backend/src/NoCTF.Plugins.AWDP/AwdpConfig.cs
backend/src/NoCTF.Plugins.AWDP/AwdpPatchContracts.cs
```

前端：

```text
frontend/src/components/game/ChallengeModal.vue
frontend/src/views/AwdpScreenView.vue
```

测试：

```text
backend/tests/NoCTF.Tests/AwdpGameModeTests.cs
backend/tests/NoCTF.Tests/AwdpPatchServiceTests.cs
backend/tests/NoCTF.Tests/AwdpScoreEngineTests.cs
```

### 15.3 待完成方向

如果还未完成，请继续：

1. 将 AWDP 修复校验改成单 check 容器。
2. 平台负责运行 check 脚本。
3. 出题人负责提供 check 容器和 check 命令。
4. check 容器拿到目标服务信息和 patch 包 URL。
5. 返回码按平台约定映射。
6. 选手只看到三类防御结果。
7. 增加 patch 包模板上传/下载。
8. 更新 AWDP 出题模板。

## 16. Penetration 当前重点交接

Penetration 插件已经存在，并且当前工作区有相关未提交改动。

位置：

```text
backend/src/NoCTF.Plugins.Penetration
```

关键类：

- `PenetrationComposeBuilder`
- `PenetrationFlagService`
- `PenetrationInstanceService`
- `PenetrationTopologyService`

测试：

```text
backend/tests/NoCTF.Tests/PenetrationComposeBuilderTests.cs
backend/tests/NoCTF.Tests/PenetrationFlagServiceTests.cs
backend/tests/NoCTF.Tests/PenetrationInstanceServiceTests.cs
backend/tests/NoCTF.Tests/PenetrationTopologyServiceTests.cs
```

接手前请先查看未提交 diff，确认这些改动是否已经验证。

## 17. 常用开发命令

### 17.1 前端

```powershell
cd frontend
bun install
bun run dev
bun run build
bun run lint
```

生成 API：

```powershell
cd frontend
bun run fetch-openapi
bun run generate-api
```

### 17.2 后端

```powershell
dotnet build backend/NoCTF.slnx
dotnet test backend/tests/NoCTF.Tests/NoCTF.Tests.csproj --no-restore
dotnet run --project backend/src/NoCTF.API --urls "http://127.0.0.1:5000"
```

### 17.3 Docker Compose

```powershell
docker compose -f deploy/docker-compose.yml up -d --build
Invoke-RestMethod http://127.0.0.1/api/health
```

## 18. 验证建议

后端改动：

```powershell
dotnet build backend/NoCTF.slnx --no-restore
dotnet test backend/tests/NoCTF.Tests/NoCTF.Tests.csproj --no-restore
```

前端改动：

```powershell
cd frontend
bun run build
```

接口变更：

```powershell
cd frontend
bun run fetch-openapi
bun run generate-api
bun run build
```

UI 改动：

- 在浏览器打开实际页面。
- 检查控制台错误。
- 检查网络请求。
- 检查 390px 左右移动端宽度。
- 管理端页面不要出现横向溢出。
- AWDP 大屏要检查 16:9 宽屏和窄屏。

## 19. 常见坑

- 不要把 AWDP 写成 AWD 子模式。
- 不要在平台核心到处写 `if plugin == awdp`。
- 不要把题库当比赛配置存储。
- 不要让 banned team 影响 solve count 和分数衰减。
- 不要只做 API 测试却不打开浏览器验证 UI。
- 不要忘记中英文 i18n。
- 不要暴露 FlagSecret。
- 不要在玩家接口里信任前端传入的 teamId。
- 不要随意 `.IgnoreQueryFilters()`。
- 不要在容器插件里直接调用 Docker SDK。
- 不要默认把 FixFailed 当扣分。
- 不要把 AWDP check 的内部细节暴露给选手。

## 20. 推荐阅读顺序

1. `README.md`
2. `docs/collaborator-architecture-handoff.md`
3. `docs/architecture.md`
4. `docs/game-modes.md`
5. `docs/development.md`
6. `docs/api.md`
7. `TODO.md`，如果存在
8. 当前 `git diff`

## 21. 接手检查清单

接手后请做：

```powershell
git status --short
git log -5 --oneline
```

然后确认：

- 未提交改动是谁的。
- 当前 API 是否能 build。
- 前端是否能 build。
- 当前 Docker Compose 是否能启动。
- 当前浏览器中登录态是否过期。
- 当前任务是继续 AWDP、Penetration，还是修复已有变更。

如果要继续当前 AWDP 工作，优先看：

```text
backend/src/NoCTF.Plugins.AWDP/AwdpPatchService.cs
backend/src/NoCTF.Plugins.AWDP/AwdpModeProvider.cs
backend/src/NoCTF.API/Endpoints/Competitions/GetPatchSubmissionsEndpoint.cs
frontend/src/components/game/ChallengeModal.vue
frontend/src/components/admin/ChallengeTemplateForm.vue
backend/src/NoCTF.Plugins.AWDP/Templates/basic-web
```

如果要继续当前 Penetration 工作，优先看：

```text
backend/src/NoCTF.Plugins.Penetration
backend/tests/NoCTF.Tests/Penetration*Tests.cs
frontend/src/views/admin/AdminCompetitionDetailView.vue
frontend/src/components/game/ChallengeModal.vue
```

## 22. 维护原则

请保持这几个边界：

- Core 只放领域模型和通用枚举。
- Application 放跨模式通用服务。
- API 负责 HTTP、鉴权、租户、审计和平台编排。
- Infrastructure 负责数据访问和迁移。
- PluginBase 放稳定契约。
- Plugins 放模式专属业务。
- Frontend API wrapper 只封装调用，不承担业务判定。
- Vue Query 负责服务端状态，Pinia 负责会话和少量客户端全局状态。

这套边界是 NoCTF 可继续扩展新模式的关键。
