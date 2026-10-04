# 开发规范

## 技术基线

- .NET 10 / C# current；
- FastEndpoints 8.2 StronglyTyped；
- EF Core 10 Relational 公共模型；运行时仅注册 PostgreSQL provider，SQLite 仅供隔离的模型测试使用；
- Wolverine NATS JetStream transport；PostgreSQL 仅保存业务事实。
- FluentStorage 8（Disk 与通用 S3 文件存储）；
- Redis/SignalR backplane；
- DynamicExpresso；
- TUnit + NSubstitute + Testcontainers。

实现前以仓库 Central Package Management 的实际版本为准。

## 功能纵切目录

```text
NoCTF.Domain/
  Competitions/
  Challenges/
  Teams/
  Gameplay/
  Runtime/

NoCTF.Application/
  Competitions/Lifecycle/
  Competitions/Scoring/
  GameplayFacts/Intake/
  GameplayFacts/Processing/
  GameplayFacts/Management/
  Flags/RuntimeScope/
  Flags/Matching/
  Runtime/Lifecycle/

NoCTF.Infrastructure/
  Competitions/
  GameplayFacts/
  Flags/
  Runtime/
  Storage/
  Messaging/
```

接口和用例就近，不建横向 Ports/UseCaseAdapters/Services/Helpers 垃圾目录。Infrastructure 类型按业务职责命名，如 GameplayFactIntakeStore，不统一加 Ef 前缀。

## FastEndpoints

一个 Endpoint 一个文件；Endpoint、Request、Response、Validator 同文件。复用 API model 放最接近概念所有权的 Endpoint 文件，禁止公共 Models/DTOs 文件。唯一例外是无业务字段的 signed-keyset `KeysetPage<T>`/cursor codec，集中在 API `Pagination` 功能目录，不能扩张为 DTO dumping ground。使用 ExecuteAsync、TypedResults、明确 Results union；禁止业务 Endpoint 手写响应。

每个有输入约束的 Request 配 FluentValidation。Validator 校验协议形状，Application/Domain 必须二次校验业务不变量。

## EF Core

Data Annotations 优先；TPH discriminator、Complex Types、关系集合和 converter 等注解无法表达的内容使用小型 `IEntityTypeConfiguration`。公共模型禁止 provider column type、JSON/数组列、filtered index、collation、方言 check constraint、原生锁与业务 Raw SQL；provider 注册和 migration 必须留在独立 provider project。

不使用 LINQ query syntax；全部 method syntax。只读查询 AsNoTracking/投影所需列；避免 N+1。异步 I/O 传 CancellationToken。

迁移只能：

```text
dotnet ef migrations add ...
dotnet ef migrations remove
dotnet ef database update
```

禁止编辑生成文件。迁移链只描述当前契约；旧 schema 不提供解析、双读或回退路径。

## Bounded Concepts

状态、Kind、Provider、FailureCode、Permission、Result 等必须 enum/value object；开放文本才使用 string。协议文本转换只在边界。配置、事件、通知、Runtime message/callback 使用带 `mode` 或 `type` discriminator 的强类型 DTO；不得引入 schema upgrader、自由 JSON 或 `*Json` 合约。

## 消息

Wolverine 只使用 NATS JetStream，不启用数据库 Message Store、Inbox 或 Outbox。业务提交成功后通过 `IPostCommitMessagePublisher` 发布；关键 Pending 状态必须可重新派发。Handler 假设至少一次投递，并依靠状态转换、唯一约束、自然业务键和稳定 `Nats-Msg-Id` 幂等。禁止 fire-and-forget、业务 Channel、同步阻塞 async 或在数据库事务中调用外部 Provider。

生产 Host 使用 Wolverine 静态 Handler 代码，不在启动时动态编译。修改 Handler 签名或依赖图后运行
`backend/scripts/Generate-WolverineHandlers.ps1`，提交 `NoCTF.Host/Internal/Generated/WolverineHandlers/`
中的生成文件；生成失败即阻止发布。该 PowerShell 文件只是本地调用包装，真正的生成器是
Wolverine/JasperFx 的 `dotnet run -- codegen write`，Docker 构建直接调用此命令。它与
`NoCTF.Modeling.Generators` 的 Roslyn 增量生成器（领域 TPH 类型与判别器）职责不同。
Handler 依赖必须可由 DI 显式解析，不能依赖 service-location 回退。
签名游标、Redis 消息、容量恢复和幂等指纹等明确的类型化 JSON 边界使用内置
`JsonSerializerContext` 源生成，并以线格式等价测试约束；Wolverine Handler 代码生成
不能替代 STJ 序列化源生成。

## 日志

结构化日志包含 Competition/Challenge/Team/GameplayFact/RuntimeInstance/Message Id。比赛 Owner、Manager、Judge 的受保护 Flag 访问必须有审计；平台 Administrator 读取不写审计。密码、任何 JWT/Token、InvitationToken、FlagDerivationSecret 不记录。异常不作为业务分支；Result/enum 表达预期失败。

## 人机验证

人机验证 Provider 由平台管理员在“邮件与人机验证”中选择，支持 `None`、`Cap`、`Turnstile`。
Cap 使用独立的官方 Standalone 服务和站点密钥；Turnstile 仅在后端调用固定 Siteverify 地址。Provider token 通过
`X-NoCTF-Human-Verification` 交给登录、注册和玩家 Runtime／评测端点，不写日志或持久化，且每次
请求后立即丢弃。管理员操作与内部 Checker 回调不要求验证码。

全局启用验证后，平台管理员仍可独立关闭“容器操作需要人机验证”。关闭只影响玩家 Runtime 的启动、
重置、停止和续期；登录、注册与评测继续要求当前 Provider 的验证。公开平台配置通过
`humanVerification.runtimeRequired` 告知客户端是否应在 Runtime 请求前展示验证，服务端中间件始终
执行同一策略并作为权威边界。既有数据库迁移后默认保持 Runtime 验证开启。

平台设置保存 Provider、公开参数和加密后的 Provider secret；管理 API 只返回 secret 是否已配置。
`HumanVerification:*` 部署配置仅作为数据库尚未保存 Provider 时的首次启动回退，管理员首次保存后由
数据库配置作为事实源。`EmailVerification:EncryptionKey` 同时保护 SMTP 密码和人机验证 secret，
不同用途使用独立的认证附加数据。启用且配置就绪时，公开配置才会暴露 Provider，验证中间件才会要求
token；`Provider=None` 始终停用验证。

本地测试 Turnstile 时使用 Cloudflare 官方测试 site key/secret，并将 `localhost` 放入开发环境的
`AllowedHostnames`；生产配置会拒绝 loopback hostname。Cap 本地实例可以使用 HTTP，生产启动校验只
接受 HTTPS。测试适配器使用受控 HTTP handler，不让 CI 依赖公网 Provider。

## 文档同步

修改领域契约必须同时更新 specs、OpenAPI 和测试。不得以代码现状为理由恢复已废弃的 Penetration 模式、RuntimeOperation、Artifact、JSON/数组持久化、旧 schema upgrader 或独立 API/Worker/Runner 进程。TeamMember 与 CompetitionCollaborator 是当前关系实体，不得退回数组。

## OpenAPI 与前端构建门禁

OpenAPI 的源头是强类型端点、绑定属性、Validator、Summary 和 Description。FastEndpoints.OpenApi 注册 `v1` 文档，生产 `/openapi/v1.json` 实时提供该文档，Scalar 提供浏览界面。

在 `backend/src/NoCTF.API/ClientApp` 执行：

```powershell
bun install --frozen-lockfile
bun run api:gen
bun run api:export
bun run api:check
```

`api:gen` 通过 Host 的 `--generateclients true` 直接生成 Kiota TypeScript request builders 和模型；`api:export` 使用 `--exportopenapijson true` 独立写出唯一入库文档 `backend/artifacts/openapi/v1.json`，导出文件不是客户端生成输入。两种模式均只装配 API、使用随机本地端口和进程内临时签名密钥，不启动外部消息 transport、数据库迁移、Runner、业务后台任务或遥测。

`api:check` 构建 Host 一次，分别运行两个生成进程，再检查文档和 SDK 的内容漂移及未跟踪文件。`backend/scripts/Verify-Backend.ps1` 和 PR 的 `api-contracts` 门禁调用相同检查。Endpoint、文档、SDK 和调用方在同一变更中提交。普通构建、发布和 Docker 使用已提交 SDK，不隐式改写源码。

FastEndpoints 8.2 使用 Kiota Builder 1.29.1 安全补丁；TypeScript 运行库固定为 preview.102，abstractions 的传递版本也被锁定。preview.103 起更改了基础集合反序列化签名，不能直接升级。FE 8.2 文档规范化仅处理 nullable 引用及被移除的 IFormFile 组件引用；本地化 ProblemDetails 扩展由单独 schema transformer 描述，枚举、绑定和验证仍使用 FE 原生能力。生成代码不手工修补。Nuxt 设置 `verbatimModuleSyntax: false`，由 TypeScript 消除 Kiota 1.29 生成的纯类型枚举导入；配置变化后运行 `bun run postinstall` 刷新 Nuxt 类型配置。

CI 仅执行构建，不运行单元、集成、契约、架构或 SDK 漂移测试。测试、typecheck、
architecture audit 和 `bun run api:check` 在本地开发与提交前执行。
普通 PR 由 `repository-build.yml` 构建完整 Host Docker 镜像，不读取发布凭据、不推送镜像。
主分支和手动发布直接构建并推送镜像；后端专用发布同样不调用测试工作流。
翻译资源准备仍作为构建输入处理；文档由 `docs-pages.yml` 构建，PR 不部署 Pages。
Docker 的前端阶段执行 `nuxt prepare` 和 `bun run generate`，后端阶段执行 restore、build、
Wolverine codegen 和 publish。普通构建不会隐式重新生成 API 客户端。

Docker 使用多阶段构建：`frontend-build` 只是构建阶段，生成的 `.output/public`
会复制到 API/Host 镜像的 `wwwroot`。生产 Compose 不启动任何 Node/Bun/Nuxt 容器；
开发环境才由 ASP.NET Core SpaProxy 连接本机 Nuxt dev server。
