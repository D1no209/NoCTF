# 比赛题目 GitHub 仓库管理设计

## 目标

一场比赛对应一个独立 GitHub 仓库。仓库中的题目目录管理可复用 `Challenge` 模板，根目录 `competition.yml` 决定本场比赛引用哪些模板，并管理这些 `CompetitionChallenge` 的分值、顺序、发布状态、Hint 和模式配置。代码合并到 `main` 后，GitHub Actions 使用 NoCTF 签发的比赛管理 JWT，把仓库内容同步到指定比赛。

本方案明确：

- 不使用 GitHub OIDC；
- 不引入公钥、私钥或密封文件；
- NoCTF 管理 JWT 保存在 GitHub Repository Secret；
- 静态题和静态附件 Flag 以明文形式保存在题目仓库；
- 动态容器、AWD 和 KoH Flag 由 NoCTF 生成，不进入仓库；
- GitHub 是题目配置的期望状态来源，NoCTF PostgreSQL 仍是运行时事实来源。

## 总体流程

```text
题目作者提交 PR
    -> GitHub Actions 本地校验
    -> Reviewer 审核
    -> 合并 main
    -> Action 读取 NOCTF_MANAGEMENT_TOKEN
    -> noctfctl 构建题目 Bundle
    -> 调用 NoCTF 比赛题目同步 API
    -> NoCTF 校验并原子更新
```

同步 API 只管理题目，不管理：

- 比赛 Owner、Manager、Judge、Observer；
- 队伍与报名；
- Start、Pause、Resume、Finish；
- Submission 与重判；
- 平台用户和基础设施。

## 仓库结构

```text
.
├─ .github/
│  ├─ workflows/
│  │  ├─ validate.yml
│  │  └─ sync.yml
│  └─ CODEOWNERS
├─ .noctf/
│  └─ repository.yaml
├─ competition.yml
├─ challenges/
│  ├─ web/
│  │  └─ sql-notes/
│  │     ├─ challenge.yml
│  │     ├─ statement.md
│  │     ├─ attachments/
│  │     │  └─ handout.zip
│  │     ├─ runtime/
│  │     │  ├─ Dockerfile
│  │     │  └─ compose.yaml
│  │     ├─ checker/
│  │     └─ tests/
│  └─ crypto/
└─ README.md
```

`.noctf/repository.yaml`：

```yaml
apiVersion: gitops.noctf.dev/v1
kind: CompetitionRepository

challengeRoot: challenges
competitionManifest: competition.yml
```

每道题的 `challenge.yml` 定义完整 Challenge 模板。Challenge 与且只与一个 GameMode 绑定；Runtime、Checker 和动态 Flag 注入跟随题目，但全部使用平台无关的逻辑定义：

```yaml
apiVersion: gitops.noctf.dev/v1
kind: ChallengeTemplate

key: web/sql-notes

title: SQL Notes
direction: Web
mode: Ctf
visibility: Private
statement: statement.md

attachments:
  - key: handout
    path: attachments/handout.zip
    fileName: handout.zip
    contentType: application/zip

flags:
  - key: primary
    value: flag{sql_notes_example}
    specification:
      kind: Attachment
      attachmentKey: handout

runtime:
  allocation: PerTeam
  definition:
    kind: Container
    image: registry.example.com/ctf/sql-notes:main
    command: []
    environment: {}
  limits:
    memoryBytes: 268435456
    nanoCpus: 500000000
    pids: 128
  publicEndpoints:
    - name: web
      protocol: Http
      containerPort: 8080

checker:
  image: registry.example.com/ctf/sql-notes-checker:main
  command: []
  environment: {}
  timeoutSeconds: 30
```

这里不允许出现 `provider`、`runnerPool`、Docker host port、Kubernetes namespace/Ingress 等部署字段。NoCTF 部署配置只能选择一种活动 provider（Docker 或 Kubernetes）及对应 runner pool；同步同一份题目定义时，由平台把逻辑 Runtime 落到该 provider。

根目录的 `competition.yml` 定义本场比赛中的题目实例：

```yaml
apiVersion: gitops.noctf.dev/v1
kind: CompetitionChallengeSet

competitionId: 7cbe44d2-4b5f-4a7f-927e-d9d21ba8746d
mode: Ctf

challenges:
  - template: web/sql-notes
    order: 20
    baseScore: 500
    published: false

    hints:
      - key: first-step
        content: 先检查查询参数。
        cost: 50
        publishedAt: null
```

`ChallengeTemplate.key` 是模板的稳定身份。`competition.yml` 使用该 key 引用模板，不复制题面。标题、方向和目录展示名可以修改，但 key 不应随意改变。

`competition.yml` 只管理比赛实例规则。普通静态 CTF 题可以不写 `rules`；计分、提交限制、轮次调度等比赛相关覆盖直接内联在对应条目中：

```yaml
challenges:
  - template: pwn/example-service
    order: 30
    baseScore: 500
    published: false
    rules:
      schemaVersion: 1
      # 仅允许对应 GameMode 的计分、接入和调度规则
```

同步器把 `challenge.yml` 中的运行定义转为 canonical `Challenge.DefinitionJson`，把 `competition.yml` 的 `rules` 转为 canonical `CompetitionChallenge.RulesJson`。CompetitionChallenge 不得覆盖 Runtime、Checker、Flag 生成或注入定义；复用模板意味着复用这些题目行为，只允许比赛规则不同。

## Challenge 与 CompetitionChallenge 映射

NoCTF 当前模型包含两个层次：

- `Challenge`：绑定一个 GameMode 的全局题库模板，管理标题、题面、方向、附件、明文模板 Flag 及 provider-neutral Runtime/Checker/Flag 定义；
- `CompetitionChallenge`：比赛题目实例，管理顺序、分值、发布状态、Hint 和 Rules。

现在仓库协议与领域模型一一对应：

```text
challenges/**/challenge.yml   -> Challenge
competition.yml.challenges[] -> CompetitionChallenge
```

同一个模板在一场比赛中最多出现一次，这也符合数据库当前 `(CompetitionId, ChallengeId)` 唯一约束。

同步分为两步：

1. 同步仓库中所有 Challenge 模板、附件和模板静态 Flag；
2. 读取 `competition.yml`，校验模板 Mode 与 Competition Mode 相同，把模板链接为 CompetitionChallenge，再同步分值、顺序、发布状态、Rules 和 Hint。

模板可以存在于仓库但不被 `competition.yml` 引用，此时它只进入题库，不出现在本场比赛。以后可以再加入 `competition.yml`；其他比赛也可以在 NoCTF 中通过对应 ChallengeId 引用它，但模板内容仍由原仓库管理。

根目录 Manifest 是本场比赛题目实例的完整列表。从 `competition.yml` 移除一项表示软删除该 CompetitionChallenge，但保留 Challenge 模板；删除模板本身仍需显式的模板 tombstone，避免影响其他比赛引用。

平台需要保存简单的同步映射：

```text
(RepositoryBindingId, TemplateKey) -> ChallengeId
(CompetitionId, TemplateKey) -> CompetitionChallengeId
(CompetitionChallengeId, ResourceKind, SourceKey) -> ResourceId
```

它用于稳定映射模板、实例、附件、Flag 和 Hint，并保证同步器只修改这个仓库创建或显式接管的资源。

静态 Flag 可以直接写在 `challenge.yml`，也可以拆到同目录的 `flags.yml` 后通过 `flagFile` 引用。无论采用哪种形式，它都是普通 Git 内容，可以在 PR 中审核和追踪历史。

`specification` 是可选的。普通静态题 Flag 不写该字段；当一个 Flag 对应某个静态附件、Hint 或其他规范对象时，通过相应 source key 建立关联，同步器再解析为 NoCTF 的 `SpecificationKind` 与 `SpecificationId`。

## GitHub Repository 配置

仓库只需要配置：

```text
NOCTF_BASE_URL
NOCTF_MANAGEMENT_TOKEN
```

其中：

- `NOCTF_BASE_URL`：NoCTF API 地址，也可以改用 Repository Variable；
- `NOCTF_MANAGEMENT_TOKEN`：平台签发的比赛管理 JWT，是本方案唯一需要的 Secret。

CTF 动态容器的 per-team Flag、AWD 轮换 Flag 和 KoH Control Flag 不写入 Manifest。仓库只保存 Runtime、生成和注入配置，真实 Flag 继续由 NoCTF 生成。

## 比赛管理 JWT

不要把普通用户登录得到的 15 分钟 Access JWT 放进仓库。平台应提供“为本比赛签发 GitHub 管理 Token”的操作。

JWT 示例：

```json
{
  "iss": "https://ctf.example.com",
  "aud": "noctf-competition-repository-v1",
  "sub": "competition-repository:7cbe44d2-...",
  "token_type": "competition_repository",
  "competition_id": "7cbe44d2-4b5f-4a7f-927e-d9d21ba8746d",
  "permissions": [
    "challenge:read",
    "challenge:write",
    "challenge:publish"
  ],
  "token_version": 1,
  "jti": "90ff3c...",
  "iat": 1785300000,
  "exp": 1816836000
}
```

设计要求：

- 使用独立 audience 和 `token_type`；
- 只被比赛题目同步 Endpoint 接受；
- JWT 中固定 `competition_id`，请求路径中的比赛 ID 必须相同；
- permission 只覆盖题目读取、写入、发布；
- 可签发 180 天或 1 年，根据比赛周期决定；
- Competition 保存 `RepositoryTokenVersion`；
- 重新签发或撤销时递增版本，旧 Token 立即失效；
- 删除 GitHub Secret 后还应在 NoCTF 点击撤销，不能只依赖仓库侧删除；
- 不记录完整 JWT。

NoCTF 现有 Access、Refresh、Internal JWT 的验证配置不能直接复用为该 Token 的 Authentication Scheme。可以继续使用部署现有的 HMAC 签名密钥，但 audience、token type、policy 和可访问路由必须分开。

## 同步 API

首版不需要复杂的多阶段发布协议。提供一个批量同步 Endpoint 即可：

```text
POST /api/v1/repository/competitions/{competitionId}/challenges/sync
```

请求使用 multipart：

```text
Bundle: noctf-bundle.tar.gz
CommitSha: Git commit SHA
Repository: owner/name
DryRun: true | false
```

响应：

```json
{
  "commitSha": "...",
  "bundleSha256": "...",
  "dryRun": false,
  "created": 3,
  "updated": 5,
  "deleted": 0,
  "warnings": [],
  "changes": []
}
```

同步过程：

1. 验证比赛管理 JWT；
2. 验证路径 CompetitionId 与 Token claim；
3. 解包并校验所有 ChallengeTemplate、附件与明文静态 Flag；
4. 校验 `competition.yml` 中的模板引用、重复项、Order、分值、Hint 和模式配置；
5. 先根据 TemplateKey 创建或更新 Challenge、Attachment 和模板 Flag；
6. 再根据 `competition.yml` 创建、更新或软删除 CompetitionChallenge 和 Hint；
7. 检查数据库 Revision；
8. 将数据库变更和 Outbox 放入同一事务；
9. 返回区分模板变化与比赛实例变化的完整摘要。

`DryRun=true` 只返回将发生的变化。`DryRun=false` 真正应用。

同步以 `(CompetitionId, CommitSha, BundleSha256)` 幂等。相同提交和 Bundle 重试时返回已有结果，不重复创建资源。

附件上传时先写临时对象，事务提交后转为有效引用；事务失败则清理临时对象。附件内容改变时创建新 Attachment ID，遵守 NoCTF 现有“附件内容不可原位替换”规则。

## 删除规则

`competition.yml` 是本场比赛的完整题目实例列表，因此：

- 从列表移除模板引用：软删除对应 CompetitionChallenge；
- 再次加入相同模板引用：恢复或重新链接 CompetitionChallenge；
- 该操作不删除 Challenge 模板、附件或模板 Flag。

Challenge 模板不采用“目录不存在就自动删除”。删除模板必须显式提交：

```yaml
apiVersion: gitops.noctf.dev/v1
kind: DeletedChallengeTemplate

key: web/old-question
```

如果模板仍被任意未删除 CompetitionChallenge 引用，NoCTF 按现有领域规则拒绝删除。同步器只允许软删除由本仓库管理的模板；比赛硬删除、模板硬删除和历史事实清理仍只能在 NoCTF 管理界面执行。

## Pull Request Workflow

`.github/workflows/validate.yml`：

```yaml
name: Validate challenges

on:
  pull_request:

permissions:
  contents: read

jobs:
  validate:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: noctf/setup-noctfctl@v1
      - run: noctfctl validate .
      - run: noctfctl test .
```

正式仓库可以把 Action 固定到具体 commit SHA。示例使用版本号是为了便于阅读。

PR 校验不读取任何 NoCTF Secret，只检查：

- YAML/JSON 格式；
- 模板 key 唯一性；
- `competition.yml` 引用的模板是否存在且没有重复；
- Order 是否唯一，分值是否合法；
- 文件和附件引用；
- 模式配置；
- Docker/Compose 配置；
- checker 与题目自测；
- 静态 Flag 定义及其 Attachment/Hint 引用是否合法；
- Bundle 是否能生成。

## Main 同步 Workflow

`.github/workflows/sync.yml`：

```yaml
name: Sync to NoCTF

on:
  push:
    branches: [main]
  workflow_dispatch:
    inputs:
      dryRun:
        description: Only show changes
        type: boolean
        default: false

permissions:
  contents: read

concurrency:
  group: noctf-competition-sync
  cancel-in-progress: false

jobs:
  sync:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: noctf/setup-noctfctl@v1

      - name: Validate
        run: noctfctl validate .

      - name: Sync
        env:
          NOCTF_BASE_URL: ${{ vars.NOCTF_BASE_URL }}
          NOCTF_MANAGEMENT_TOKEN: ${{ secrets.NOCTF_MANAGEMENT_TOKEN }}
          NOCTF_DRY_RUN: ${{ inputs.dryRun || 'false' }}
        run: noctfctl sync . --commit "${{ github.sha }}" --dry-run "$NOCTF_DRY_RUN"
```

如果不希望每次合并自动同步，可以删除 `push` trigger，只保留 `workflow_dispatch`。

如果希望发布前再人工确认，可以给 `sync` job 配置普通 GitHub Environment；这只是可选审核功能，不改变 JWT 认证方式。

## GitHub 审核规则

建议：

- `main` 禁止直接 push；
- 所有修改通过 PR；
- 至少一名或两名 Reviewer；
- 必须通过 `Validate challenges`；
- 修改后撤销旧批准；
- 必须解决 Review conversation；
- 禁止 force push；
- `.github/**` 和 `.noctf/**` 由比赛管理员审核；
- runtime/checker 由对应出题或运维人员审核。

示例 CODEOWNERS：

```text
/.github/             @competition-admins
/.noctf/              @competition-admins
/challenges/web/      @web-reviewers
/challenges/crypto/   @crypto-reviewers
**/runtime/           @runtime-reviewers
**/checker/           @judge-reviewers
```

## 容器镜像

Runtime 镜像可以在 `sync.yml` 中构建并推送到 GHCR 或其他 Registry。

推荐配置最终引用镜像 digest，而不是可变 tag：

```text
registry.example/noctf/sql-notes@sha256:...
```

Registry pull credential 属于 NoCTF Runner 的部署配置，不放进题目 Manifest，也不使用比赛管理 JWT 拉取镜像。

Docker Container 与 Compose Runtime 继续使用挑战服务自身的 Docker port mapping 和 host port `0`，不增加 HAProxy 或 ingress。

## NoCTF 代码落点

按 `RepositorySync` 能力组织：

```text
NoCTF.API/Endpoints/RepositorySync/
NoCTF.Application/Challenges/RepositorySync/
NoCTF.Infrastructure/Challenges/RepositorySync/
```

Endpoint：

- 继承强类型 FastEndpoints base；
- 使用 `ExecuteAsync`；
- Bundle 通过 request DTO 的 `IFormFile` 绑定；
- 调用 `AllowFileUploads()`；
- 返回 `Results<Ok<...>, UnauthorizedHttpResult, NotFound, Conflict<...>, ProblemHttpResult>`；
- 不直接读取 `HttpContext.Request.Body`；
- 不逐项在 Endpoint 中编排业务规则。

`ResourceKind`、`RepositoryPermission`、`RepositorySyncStatus` 和 `RepositoryChangeKind` 使用 enum，不在 Application 和 Infrastructure 之间传递字符串。

PostgreSQL、对象存储和 Outbox 行为使用真实依赖集成测试；Migration 只通过 `dotnet ef migrations ...` 生成。

## 建议实施顺序

1. 增加比赛 `RepositoryTokenVersion` 与 Token 签发/撤销操作；
2. 增加独立 JWT Authentication Scheme；
3. 实现 SourceKey 映射和批量 Sync Application use case；
4. 实现 multipart Sync Endpoint；
5. 实现 `noctfctl validate`、`bundle`、`sync`；
6. 建立比赛仓库模板、两个 Workflow 和 CODEOWNERS；
7. 首轮只允许 Draft/Visible/Published 比赛同步；
8. 稳定后再决定是否允许 Running 状态更新。

这套首版只有一个真正的远程写操作和一个比赛 JWT Secret；静态 Flag、附件、题面与配置都走普通 Git 版本管理，容易理解、部署和排错。
