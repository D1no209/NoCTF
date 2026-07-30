# 比赛题目 GitHub 仓库管理

## 权威实现

首版以 GitHub Template Repository
`D1no209/NoCTF-Challenge-Template` 的 `main` 为仓库协议权威来源。2026-07-30 对齐基线为
`25718936d1a32a1a8c5f73a74143e350d726c7aa`。

该模板使用：

- 一场比赛一个 GitHub 仓库；
- 仓库内稳定 UUID；
- 普通 `UserKind.Bot` Organizer；
- 普通 NoCTF Access JWT；
- 现有细粒度管理 API；
- 可重入的 desired-state 收敛。

首版不使用 GitHub OIDC，不使用专用 Repository JWT/audience/token type，不上传 bundle，
不增加 RepositoryBinding/source-key 映射表，也不提供原子批量 Sync Endpoint。

## 身份与撤销

平台 Administrator 创建非交互 Bot：

```http
POST /api/v1/admin/platform/bots

{
  "userName": "summer-ctf-gitops",
  "role": "Organizer"
}
```

Bot 必须满足：

- `User.Kind=Bot`；
- Role 只能是 `Organizer`；
- Email 由服务端生成为唯一 `.invalid` 地址；
- PasswordHash 使用随机 dummy password 生成，但密码不返回；
- 密码登录、修改密码和 Refresh 均拒绝 Bot；
- Bot 不能自行签发 Token。

平台 Administrator 为 Bot 签发所需时长的普通 Access JWT：

```http
POST /api/v1/admin/platform/bots/{botUserId}/tokens

{
  "expiresInSeconds": 31536000
}
```

`ExpiresInSeconds` 范围为 60～31,536,000。Token 使用普通 Access issuer、audience 和
`token_type=access`，包含 Bot 的 `sub`、Role 与当前 `token_version`。仓库保存：

```text
Repository Variable: NOCTF_API_URL
Repository Secret:   NOCTF_BOT_TOKEN
```

管理员通过现有接口撤销该 Bot 的全部 Token：

```http
POST /api/v1/admin/platform/users/{botUserId}/tokens/invalidate
```

该操作递增 `User.TokenVersion`；Access JWT 的现有授权处理器会立即拒绝旧版本。删除 GitHub
Secret 不能替代平台侧撤销。

Bot 必须被加入目标 Competition 的 `ManagerIds`。Bot 创建的 Challenge 自动以 Bot 为
Owner；接管已有 Challenge 时，原 Owner 必须先把 Bot 加入 Challenge `ManagerIds`。
平台威胁模型信任 Administrator 与 Competition Manager，不防管理员内鬼。

## 仓库结构

模板仓库按 `<direction>/<slug>` 管理题目：

```text
.
├─ competition.yml
├─ web/
│  └─ sql-notes/
│     ├─ challenge.yml
│     ├─ statement.md
│     ├─ attachments/
│     ├─ runtime/
│     ├─ checker/
│     ├─ tests/
│     └─ solution/
├─ .github/
│  ├─ scripts/
│  │  └─ repository.cs
│  └─ workflows/
└─ docs/
```

`.github/scripts/repository.cs` 是 .NET 10 file-based app，提供：

```bash
dotnet build .github/scripts/repository.cs
dotnet run --file .github/scripts/repository.cs -- validate
dotnet run --file .github/scripts/repository.cs -- self-test
dotnet run --file .github/scripts/repository.cs -- readme
dotnet run --file .github/scripts/repository.cs -- discover --base origin/main --head HEAD
dotnet run --file .github/scripts/repository.cs -- plan --base origin/main --head HEAD
dotnet run --file .github/scripts/repository.cs -- apply --base <sha> --head <sha>
```

PR 校验不读取 NoCTF 或 Registry Secret。只有合并到 `main` 后的 Apply job 可以读取
`NOCTF_BOT_TOKEN`。

## Manifest 所有权

`challenge.yml` 管理全局 Challenge 模板：

- 稳定 Challenge UUID；
- Mode、Visibility、Title、Direction；
- statement；
- Attachment 稳定 UUID；
- 模板静态 Flag 稳定 UUID；
- provider-neutral Runtime、Checker、FlagTemplate、FlagInjection、Patch 和 ControlCheck；
- 镜像 build source 与最终 digest。

`competition.yml` 管理 CompetitionChallenge：

- Competition UUID 与 Mode；
- 稳定 CompetitionChallenge UUID；
- Challenge 路径引用；
- Order、BaseScore、Published；
- Hint 稳定 UUID；
- `rules`。

映射严格为：

```text
challenge.yml definition        -> Challenge.DefinitionJson
competition.yml challenges/rules -> CompetitionChallenge.RulesJson
```

Competition 配置与 RulesJson 不能声明 Runtime、Checker、Patch、Flag 生成或注入。
DefinitionJson 不能声明计分、提交限制或比赛调度规则。AWDP 的 Runtime/Checker/Patch
只属于 Challenge.DefinitionJson；比赛级配置只提供比赛规则默认值。

Manifest 禁止部署字段：

```text
provider
runnerPool
hostPort
namespace
ingress
targetUrl
targetPort
```

部署只选择一个活动 Docker 或 Kubernetes provider/runner pool。Docker 公开 Container
或 Compose service 直接请求 host port `0`，由 Docker 分配随机宿主端口；不创建 HAProxy。

## 稳定身份

Challenge、CompetitionChallenge、Attachment、Flag 与 Hint 的 UUID 直接提交到 Git。
这些 UUID 是 GitOps 身份来源，因此后端创建 API接受调用方提供的非空 UUID。

不再维护以下重复映射：

```text
(Repository, SourceKey) -> ResourceId
```

目录、标题和文件名可以变化，资源 UUID 不随之变化。两个资源不得复用同一 UUID。
Attachment 内容不可原位替换；内容变化必须生成新的 Attachment UUID。

## Apply 收敛顺序

Apply 先读取 Competition 并拒绝修改 Finished 比赛，然后按以下顺序执行：

1. 读取所有 Challenge Manifest 并生成 canonical DefinitionJson；
2. 创建或恢复 Challenge；
3. 暂时保持 Challenge `Private`；
4. 同步 Attachment 和模板静态 Flag；
5. 更新 Challenge 到期望 Visibility；
6. 创建或恢复 CompetitionChallenge；
7. 暂时取消发布需要变更的 CompetitionChallenge；
8. 更新 BaseScore、Order、RulesJson 和 Hint；
9. 恢复期望 Published 状态；
10. 根据显式 base/head Git diff 处理已删除 Challenge 目录。

每次更新继续使用现有 Revision/ExpectedRevision。列表和单项管理读取使用
`includeDeleted=true` 查看软删除资源；恢复沿用原 UUID。

模板依赖的管理能力包括：

```text
Challenge:
  explicit create Id
  GET includeDeleted
  DeletedAt
  ActiveCompetitionReferenceCount
  restore

Attachment:
  explicit multipart Id
  list includeDeleted
  DeletedAt
  restore

Template Flag:
  explicit create Id
  list includeDeleted
  DeletedAt
  restore

CompetitionChallenge:
  explicit create Id
  get/list includeDeleted
  DeletedAt
  restore

Hint:
  explicit create Id
  list includeDeleted
  DeletedAt
  restore
```

## 删除语义

`competition.yml` 是本场比赛 CompetitionChallenge 的完整期望列表：

- 移除条目：软删除 CompetitionChallenge；
- 重新加入同一 UUID：恢复；
- 不因此删除 Challenge 模板。

Challenge 目录删除只根据 workflow 提供的显式 base/head diff 处理。工具读取
`ActiveCompetitionReferenceCount`；仍被任何未删除 CompetitionChallenge 引用时跳过删除。
工具不会因为当前 checkout 中暂时缺少目录就盲删共享 Challenge。

Attachment、Flag 与 Hint 从 Manifest 移除时软删除；重新加入原 UUID 时恢复。Attachment
对象必须保留到元数据不可恢复或所属 Challenge 最终硬删除的清理边界。

## 幂等与非原子性

本方案的幂等来源是：

- Git 中的稳定 UUID；
- GET/compare/update；
- Revision；
- 软删除恢复；
- GitHub Actions concurrency；
- 对 429、5xx 和网络故障的有限重试。

Apply 是多次 HTTP 调用，不是跨全部资源的数据库原子事务。中途失败可能留下已经完成的
前缀，但资源保持合法状态；下一次 Apply 会从平台当前状态继续收敛。模板在修改子资源时
先把 Challenge 保持 Private，并在修改比赛题目时先取消发布，以降低部分应用窗口的影响。

如果未来出现“整场题目必须一次提交或一次回滚”的硬需求，应另行设计批量事务协议；不能
把当前多 API Apply 描述成已经具备原子性。

## 镜像与部署

仓库 workflow 构建镜像并解析 registry digest。最终 DefinitionJson 使用
`registry/name@sha256:...`。Registry pull credential 属于 NoCTF Runner Pool 部署配置，
不进入 Manifest，也不使用 Bot JWT 拉取镜像。

Runtime、Checker、题目网络、宿主端口、Kubernetes Namespace、NetworkPolicy 与 PID
预算仍由平台运行时策略负责。题目仓库只描述 provider-neutral 业务容器。

## 验证基线

主仓库必须验证：

- Bot 不能 Login、ChangePassword 或 Refresh；
- Bot Token 使用请求的有限 lifetime 和当前 TokenVersion；
- 显式 UUID 原样持久化；
- `includeDeleted` 能读取软删除资源；
- Restore 恢复原 UUID；
- ActiveCompetitionReferenceCount 只统计未删除引用；
- PostgreSQL migration 与真实关系约束；
- OpenAPI 与 `docs/api.md` 路由完全一致；
- 模板 `build`、`validate`、`self-test`；
- 使用真实 API、PostgreSQL 和对象存储完成一次模板 Apply 与重复 Apply。
