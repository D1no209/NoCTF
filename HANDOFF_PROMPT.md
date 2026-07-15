# NoCTF 后端全量审计修复交接

更新时间：2026-07-15

## 任务目标

本轮 NoCTF 后端架构、性能、安全、并发和部署可靠性审计修复已完成复核、验证和本地逻辑分组提交。本文件保留边界、验证基线和后续接手要求。

必须保留以下边界：

- 游戏模式实现留在各自插件项目中；API 只依赖 `NoCTF.PluginBase` 和 `NoCTF.Application` 的通用抽象、registry 和 orchestration glue。
- CTF、Penetration、AWD、AWDP、KoH 的计分、恢复和后台能力继续通过插件注册，不在 API 中硬编码模式分支。
- 不改变现有公共路由或响应字段，除非修复明确的安全漏洞且已有兼容方案。
- 不通过删除测试、降低生产安全约束或吞掉生产警告来获得绿色检查。

## Git 与工作区状态

```text
workspace:   E:\SourceCode\NoCTF
branch:      codex/backend-architecture-performance-remediation
remote:      未推送，未创建 PR
```

实现已按持久化/存储、插件宿主/分布式协调、赛制运行时、容器/部署、API 安全/实时链路、测试基础设施、前端/OpenAPI 依次提交。接手时以 `git log --oneline -n 15` 和 `git status --short` 为准，不要依赖旧的固定 HEAD。

严禁执行 `git reset --hard`、`git checkout -- .`、清空未跟踪文件或用旧版本覆盖工作树。先用 `git status --short` 和局部 diff 确认修改归属；保留所有不相关的用户改动。默认不要推送远程、创建 PR 或直接更新 `main`。

## 协作者快速了解项目

优先按逻辑提交和下面的阅读顺序建立项目模型，不要只根据文件名推断赛制或插件归属。

### 1. 五分钟建立边界

依次阅读：

1. `README.md`：产品能力、启动方式和仓库入口。
2. `docs/architecture.md`：分层、依赖方向和运行时组件。
3. `docs/game-modes.md`：CTF、Penetration、AWD、AWDP、KoH 的职责差异。
4. 本文件的“已完成的主要修复”“当前验证结果”“未完成事项与执行顺序”。

先记住唯一允许的依赖方向：

```text
API / Worker / Runner
        |
        v
Application + PluginBase
        |
        v
Infrastructure / Container providers

Game-mode plugins -> Application + PluginBase
API 不得直接依赖具体游戏模式插件
```

### 2. 十分钟阅读核心骨架

按职责而不是目录数量阅读：

| 要理解的内容 | 首选入口 |
| --- | --- |
| 核心实体与生命周期 | `backend/src/NoCTF.Core/Entities.cs` |
| EF 模型、过滤器、索引、关系 | `backend/src/NoCTF.Infrastructure/ApplicationDbContext.cs` |
| 游戏模式与题型扩展点 | `backend/src/NoCTF.Application/CompetitionModes/CompetitionModeAbstractions.cs`、`ChallengeFeatureRegistry.cs` |
| 计分扩展点 | `backend/src/NoCTF.Application/Scoring/ScoringAbstractions.cs`、`ScoringServices.cs` |
| API 组合根 | `backend/src/NoCTF.API/Program.cs` |
| Worker 组合根 | `backend/src/NoCTF.Worker/WorkerHostComposition.cs`、`Program.cs` |
| 后台任务与租约 | `backend/src/NoCTF.Application/BackgroundTasks/BackgroundTaskQueue.cs`、`CompetitionExecutionLease.cs` |
| 插件加载与校验 | `backend/src/NoCTF.API/Plugins/PluginLoader.cs`、`PluginLoadContext.cs` |
| 容器抽象及实现 | `backend/src/NoCTF.PluginBase/Interfaces.cs`、`NoCTF.Container.Docker`、`NoCTF.Container.K8s` |
| 部署拓扑 | `deploy/docker-compose.yml`、`deploy/k8s` |

### 3. 十分钟跟踪四条关键链路

1. 提交与计分：`SubmitFlagEndpoint` → 通用 mode/feature registry → 对应插件 handler → `ScoringServices` → 持久化事件 → 榜单同步与 Redis 缓存。
2. 比赛与实例生命周期：管理/选手 endpoint → `CompetitionExecutionLease` → 插件 game mode → Docker/Kubernetes provider → 运行时元数据与恢复服务。
3. 后台任务：enqueue → PostgreSQL 原子 claim → 租约续期 → `CompetitionJobRegistry` → 插件 handler → 完成、重试或释放。
4. 对象存储：上传 → `StorageObjectKey` → 数据库引用 → 临时 URL → CAS 替换/删除 → `StorageCleanupOutbox` → Worker 清理。

用 `rg` 从抽象或类型名追调用点，不要依靠文件名猜测：

```powershell
rg "ICompetitionMode|IChallengeFeature|IScoringStrategy" backend/src
rg "CompetitionExecutionLease|SubmissionMutationGuard" backend/src backend/tests
rg "StorageCleanupOutbox|StorageObjectKey" backend/src backend/tests
rg "RedisLeaderboardCache|LeaderboardSyncHandler" backend/src backend/tests
```

### 4. 修改前确认当前差异

```powershell
git status --short
git diff --stat
git diff -- <准备修改的文件>
```

协作者只处理明确分配的文件或职责域；不要运行全仓库自动格式化，不要提交、推送、清理工作树，也不要回滚其他协作者的修改。完成时必须报告修改文件、设计判断、运行的测试、未覆盖风险，并让主 Agent 统一进行跨模块复核和逻辑分组提交。

## 已完成的主要修复

### 插件架构与宿主

- 保留并强化游戏模式、计分策略、挑战能力和后台任务 handler 的 registry 机制。
- 内置插件缺失或加载失败时启动失败；外部插件要求匹配配置的 SHA-256。
- 增加宿主角色验证，避免 API、Worker 和 Runner 错误装载不属于自身的后台能力。
- 补齐插件拥有实体的外键、索引和迁移模型。

### 并发、计分与生命周期

- 后台任务改为 PostgreSQL 原子 claim、`SKIP LOCKED`、所有者/竞争令牌和续租；丢失租约后停止继续写入。
- 比赛执行租约、CTF 分数重建 advisory lock、Penetration 生命周期和多副本竞争路径得到加固。
- 计分事件/信号具备数据库幂等约束和冲突处理；批量计分、重建和缓存失效路径得到优化。
- Redis 榜单使用版本化原子更新；版本在数据库投影前预留，防止旧计算覆盖新榜单。
- 新增 `SubmissionMutationGuard`，CTF、Penetration、AWDP、AWD 提交在持有运行时准备租约后重新校验比赛、队伍和题目状态，阻止 teardown 与提交竞态。
- 队伍自动批准、队长退出、比赛开始前撤队以及删除队伍流程增加事务隔离和一致性约束。

### 存储生命周期

- 存储只接受稳定对象键，读取时再生成临时 URL；拒绝绝对路径、`..`、编码穿越和根目录逃逸。
- 挑战附件和补丁模板使用数据库 CAS 替换；同一事务将实际被替换对象写入持久化清理 outbox。
- 并发失败上传、模板删除竞态、AWDP 保存结果不确定等路径不再直接误删对象，而是写入 outbox 由 Worker 清理。
- 删除挑战、模板、比赛及运行时资源时补齐旧对象和派生资源清理。

### API、安全与性能

- 修复暂停比赛、隐藏题目、Penetration Flag 和实时 Hub 的可见性边界。
- 减少公开列表、管理列表、榜单趋势、提交记录和权限检查中的 N+1/重复查询；统一只读投影和取消令牌传播。
- 榜单、详情及昂贵匿名查询增加限流；请求体大小、Runner 响应体和长任务 HTTP 超时改为可配置。
- JWT 会话版本、刷新令牌、密码/角色变更失效和多副本缓存传播得到加固。
- 审计字段脱敏、日志缓冲、健康检查超时、生产 CORS/Secret 验证得到强化。

### 运行时、容器与部署

- Docker、Compose、Kubernetes 运行时身份、标签、资源 claim、清理回执和恢复元数据得到补齐。
- KoH/AWD/AWDP/Penetration 重启恢复、轮次恢复、运行时回收和重复执行保护得到增强。
- Runner 增加操作协调器、可配置长任务超时、超限响应保护及 Docker/Kubernetes 回执清理服务。
- 生产默认由独立迁移 Job 迁移数据库；API 多副本不自动迁移。
- K8s 非 root、只读容器、TLS、探针、NetworkPolicy、RBAC、Redis PVC 和空 Secret 模板已校验。

### 数据库迁移

- 新增并完善：
  - `20260712054600_BackendArchitecturePerformance`
  - `20260714062708_DurableStorageCleanupOutbox`
  - `20260714103208_FinalizeArchitecturePerformanceIndexes`
- 用户 Email/UserName 缩短和归一化唯一索引迁移在执行任何 DDL 前进行无损预检；异常只返回聚合数量，不泄露 PII。
- 最新迁移清理安全外键孤儿数据并对历史重复 `AwdRound` 做确定性去重。

## 当前验证结果

- 最新 Release 解决方案构建：0 warnings，0 errors。
- 测试 helper 复用按 EF 单例选项和 `InMemoryDatabaseRoot` 隔离的共享 InMemory service provider；不再触发 `ManyServiceProvidersCreatedWarning`，生产警告策略未被放宽。
- 最新完整 Release 测试使用真实 PostgreSQL 16 和 Redis 7：466/466 通过。测试集已从交接时预估的 455 项扩展到 466 项。
- `SubmissionMutationGuard` 在 CTF、Penetration、AWDP、AWD 的租约顺序、事务边界、软删除/比赛/队伍二次校验、取消和释放路径已人工复核；AWD 显式回滚改用不可取消令牌，避免已取消的变更令牌遮蔽事务清理和租约释放。
- EF 模型与迁移快照一致；全新 PostgreSQL 16 的完整升级、最后一条迁移回退/重放、完整降级到 `0`、再完整升级均通过。
- `docker compose config -q` 通过。
- kubeconform strict：47 个资源有效，0 invalid/error/skipped。`kubectl apply --dry-run=client` 仅因本机没有 Kubernetes API discovery（`localhost:8080`）无法完成，不能将其表述为清单失败。
- 前端 `bun install --frozen-lockfile`、`bun run lint` 与 `bun run build` 通过；构建仅报告第三方 PURE annotation 提示。
- NuGet 漏洞/弃用扫描为空；`bun audit` 在升级 Vite/OpenAPI 生成器并固定安全的 `esbuild`、`js-yaml` 版本后为 0 vulnerabilities。
- analyzer、whitespace format、迁移漂移检查均通过。
- API 已从真实运行实例重新生成 OpenAPI；契约差异仅包含补丁提交列表的 `before`/`limit` 分页参数。客户端生成器安全升级产生的模板差异已构建验证，连续重生成的 18 个 artifact 哈希保持一致。
- API/Worker Release publish 均包含 CTF、AWD、AWDP、KoH、Penetration 五个插件程序集。
- 临时 PostgreSQL/Redis 验证容器已删除。

## 后续交接检查

1. 开始新工作前先运行 `git status` 和 `git log --oneline -n 15`，确认当前分支和逻辑分组提交。
2. 若运行环境提供 Kubernetes API，再补充 server/client dry-run；本轮 kubeconform strict 已覆盖静态 schema 验证。
3. 后续修改继续按现有插件边界、公开 API、状态/退出码和数据结构执行，不要重新合并 AWD 与 AWDP。
4. 默认不推送、不创建 PR、不合并 `main`，除非用户再次明确授权。

## 建议首先检查的文件

- `backend/src/NoCTF.Application/CompetitionModes/SubmissionMutationGuard.cs`
- `backend/src/NoCTF.Plugins.CTF/CtfGameMode.cs`
- `backend/src/NoCTF.Plugins.Penetration/PenetrationSubmissionHandler.cs`
- `backend/src/NoCTF.Plugins.AWDP/AwdpPatchService.cs`
- `backend/src/NoCTF.Plugins.AWD/AwdScoreEngine.cs`
- `backend/tests/NoCTF.Tests/CtfGameModeTests.cs`
- `backend/tests/NoCTF.Tests/PenetrationSubmissionRaceTests.cs`
- `backend/src/NoCTF.Infrastructure/Storage/StorageCleanupOutbox.cs`
- `backend/src/NoCTF.Application/BackgroundTasks/BackgroundTaskQueue.cs`
- `backend/src/NoCTF.Infrastructure/Migrations/20260712054600_BackendArchitecturePerformance.cs`
- `.github/workflows/ci.yml`

## 接手要求

先阅读 `README.md`、`docs/architecture.md`、`docs/development.md`、`docs/game-modes.md` 和本文件，再检查工作树与最近提交。不要重复已经完成的广泛重构；新改动仍须逐项记录实际命令、结果和外部环境限制，不得用定向测试替代全套验收。
