# 开发规范

## 技术基线

- .NET 10 / C# current；
- FastEndpoints 8.2 StronglyTyped；
- EF Core 10 + Npgsql/PostgreSQL；
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

Data Annotations 优先。只有 jsonb、uuid[]、GIN、部分索引、复杂 check/value conversion 等必要内容用小型 IEntityTypeConfiguration/ModelBuilder，并说明原因。

不使用 LINQ query syntax；全部 method syntax。只读查询 AsNoTracking/投影所需列；避免 N+1。异步 I/O 传 CancellationToken。

迁移只能：

```text
dotnet ef migrations add ...
dotnet ef migrations remove
dotnet ef database update
```

禁止编辑生成文件。本目标不兼容旧 schema，最终只有 EF 生成 InitialBaseline。

## Bounded Concepts

状态、Kind、Provider、FailureCode、Permission、Result 等必须 enum/value object；开放文本才使用 string。协议文本转换只在边界。Runtime Config/Message/Callback 使用强类型 DTO 与 schemaVersion。

## 消息

业务写+Outbox 同事务。Handler 假设至少一次投递；GameplayFact 与 Runtime 依靠状态、唯一约束、自然键和 durable inbox 幂等，不引入持久化 Revision/ProcessingVersion。禁止 fire-and-forget、业务 Channel、同步阻塞 async 或在数据库事务中调用外部 Provider。

## 日志

结构化日志包含 Competition/Challenge/Team/GameplayFact/RuntimeInstance/Message Id。比赛 Owner、Manager、Judge 的受保护 Flag 访问必须有审计；平台 Administrator 读取不写审计。密码、任何 JWT/Token、InvitationToken、FlagDerivationSecret 不记录。异常不作为业务分支；Result/enum 表达预期失败。

## 文档同步

修改领域契约必须同时更新 docs、OpenAPI 和测试。不得以代码现状为理由恢复已废弃的 Penetration 模式、RuntimeOperation、Artifact、TeamMember 或 Collaborator 表。

## OpenAPI 与前端构建门禁

OpenAPI 的源头是 API endpoint 元数据。导出命令会同时更新提交到仓库的
`backend/artifacts/openapi/swagger.json` 和运行时提供的
`backend/src/NoCTF.API/wwwroot/openapi/v1.json`：

```powershell
dotnet run --project backend/src/NoCTF.API/NoCTF.API.csproj -- --export-openapi
```

前端 SDK 由 `ClientApp/openapi-ts.config.ts` 从 `wwwroot/openapi/v1.json` 生成到
`ClientApp/app/api`：

```powershell
Push-Location backend/src/NoCTF.API/ClientApp
bun install --frozen-lockfile
bun run api:gen
Pop-Location
```

CI 和 `backend/scripts/Verify-Backend.ps1` 都会重新导出 OpenAPI、重新生成 SDK，
并以 `git diff --exit-code` 拒绝未提交的漂移。因此 endpoint、OpenAPI 文档和前端
调用代码必须在同一变更中更新。

前端提交门禁依次执行 `bun test`、`bun run typecheck` 和 `bun run generate`；API/Host
发布目标会执行同样的 typecheck/generate 钩子，Docker 构建则在专用阶段执行
`nuxt prepare` 和 `bun run generate`，确保 Nuxt 生产静态包能够编译。

Docker 使用多阶段构建：`frontend-build` 只是构建阶段，生成的 `.output/public`
会复制到 API/Host 镜像的 `wwwroot`。生产 Compose 不启动任何 Node/Bun/Nuxt 容器；
开发环境才由 ASP.NET Core SpaProxy 连接本机 Nuxt dev server。
