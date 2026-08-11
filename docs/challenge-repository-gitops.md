# 比赛题目 GitHub 仓库与 GitOps 管理设计

> 当前平台契约：题目 Runtime、Checker 和 Compose service 镜像由受信任的出题人配置，tag 与
> digest 均可直接保存和运行；NoCTF 不解析 tag、不强制固定 digest，也不设置 Registry allowlist。
> 本文后续的 digest 解析是可选 GitOps 工作流自身采用的严格构建策略，不是 NoCTF API、发布或
> 开赛门禁。未启用该仓库策略时，管理员可以继续使用普通镜像 tag。

## 目标与边界

一场比赛对应一个独立 GitHub 仓库。仓库保存本场比赛的题目模板、比赛题目清单、题面、静态附件、明文静态 Flag、Runtime 与 Checker 源码以及构建说明。GitHub Pull Request 是题目审核入口，`main` 是仓库期望状态。

NoCTF 只提供普通用户认证和普通管理 API。仓库中的 GitHub Actions 负责：

- 从 Issue 创建题目分支、目录和 Draft PR；
- 校验仓库 Manifest；
- 发现本次修改影响的题目和镜像；
- 构建 Runtime、Compose service 和 Checker 镜像；
- 推送 GHCR，并按需推送自定义 Docker Registry；
- 把构建产物解析为不可变镜像 digest；
- 按依赖顺序调用 NoCTF 现有的普通 Challenge、Attachment、Flag、CompetitionChallenge 和 Hint API；
- 在失败后通过稳定 ID 和 Revision 安全重试。

NoCTF 不解析 Git 仓库、不执行 Docker build，也不承担仓库级差异计算、批量编排或跨资源事务。

本方案明确：

- 不使用 GitHub OIDC；
- 不引入仓库专用密钥体系或仓库专用身份；
- GitHub Repository Secret 中只保存管理员为 Bot 用户签发的普通 Access JWT；
- Bot 和自然人都是 `User`，使用相同的角色、比赛权限、题库权限和授权路径；
- Challenge 与且只与一种 `GameMode` 绑定；
- Challenge 管理 provider-neutral Runtime、Checker 和动态 Flag 注入定义；
- CompetitionChallenge 管理本场比赛的分值、顺序、发布状态、Hint 和模式规则；
- Competition 和 Challenge 都不关心平台部署使用 Docker 还是 Kubernetes；
- Manifest 中禁止出现 `provider`、`runnerPool`、Docker host port、Kubernetes namespace、Ingress 等部署字段；
- 静态题和静态附件中的 Flag 可以明文提交；
- CTF 动态容器 Flag、AWD 轮换 Flag 和 KoH Control Flag 由 NoCTF 生成，不进入 Git；
- Git 是配置期望状态，PostgreSQL 是运行时事实来源；
- 当前 Runtime Generation 不因 Challenge Definition 修改而热更新，Reset 后才使用新 Definition；
- 不记录题目发布版本，不进行自动版本升级，也不自动修改 Manifest 中的镜像 digest。

## 领域模型映射

NoCTF 题目模型分为两个层次：

- `Challenge` 是全局题库模板，绑定一种 GameMode，拥有标题、题面、方向、可见性、模板附件、模板静态 Flag、Runtime、Checker 和动态 Flag 注入定义；
- `CompetitionChallenge` 是某场比赛对 Challenge 的一次引用，拥有比赛内排序、发布状态、BaseScore、Rules 和 Hint。

仓库协议与领域模型直接对应：

```text
<direction>/<slug>/challenge.yml -> Challenge
competition.yml.challenges[]     -> CompetitionChallenge
```

每个受 Git 管理的实体都在 Manifest 中保存稳定 UUID：

- `challenge.yml.id` 是 `Challenge.Id`；
- `competition.yml.challenges[].id` 是 `CompetitionChallenge.Id`；
- Attachment、静态 Flag 和 Hint 也保存各自 ID。

目录路径是可读定位符，不是数据库身份。移动 `web/sql-notes` 到 `misc/sql-notes` 时，只要保留 Challenge ID 并修改 `competition.yml` 的路径引用，就仍然是同一个 Challenge。

同一 Challenge 在同一 Competition 中最多出现一次。仓库脚本必须同时校验：

- Challenge ID 全仓库唯一；
- CompetitionChallenge ID 唯一；
- 同一个 Challenge ID 在 `competition.yml` 中最多引用一次；
- Challenge Mode 与 Competition Mode 完全相同；
- 路径中的 direction 与 `challenge.yml.direction` 规范化后相符。

## 仓库结构

题目目录和题目分支都使用 `<direction>/<slug>`：

```text
.
├─ .github/
│  ├─ ISSUE_TEMPLATE/
│  │  └─ create-challenge.yml
│  ├─ workflows/
│  │  ├─ scaffold-challenge.yml
│  │  └─ challenge-ci.yml
│  ├─ scripts/
│  │  ├─ repository.cs
│  │  ├─ RepositoryApp.*.cs
│  │  └─ NoCtfClient.cs
│  └─ CODEOWNERS
├─ competition.yml
├─ web/
│  └─ sql-notes/
│     ├─ challenge.yml
│     ├─ statement.md
│     ├─ attachments/
│     │  └─ handout.zip
│     ├─ runtime/
│     │  ├─ Dockerfile
│     │  └─ compose.yml
│     ├─ checker/
│     │  └─ Dockerfile
│     ├─ tests/
│     └─ solution/
├─ pwn/
├─ crypto/
├─ reverse/
└─ misc/
```

目录含义：

- `challenge.yml`：Challenge 模板、仓库构建定义和静态资源清单；
- `statement.md`：选手题面；
- `attachments/`：最终交付给选手的静态附件；
- `runtime/`：Runtime 或 Compose service 的 Docker build context；
- `checker/`：AWD/AWDP one-shot Checker 的 Docker build context；
- `tests/`：仓库 CI 执行的题目自测；
- `solution/`：题解和内部材料，不上传 NoCTF，也不作为选手附件。

题目根目录不再增加 `challenges/` 层。分支名与题目目录相同，例如：

```text
web/sql-notes
pwn/heap-school
crypto/broken-rsa
```

## Bot 用户

### 用户类型

`User` 增加有界类型：

```csharp
public enum UserKind
{
    Human,
    Bot
}
```

Bot 是管理员创建的非交互用户，不是独立的认证主体。它与 Human 共用：

- User ID；
- UserName；
- `UserRole`；
- `TokenVersion`；
- 普通 Access JWT issuer、audience、签名密钥和 `token_type=access`；
- Competition Owner、Manager、Judge、Observer 权限；
- Challenge Owner、Manager 权限；
- 默认 Access JWT authorization policy。

Bot 与 Human 的区别：

- Bot 只能由平台管理员创建；
- Bot 的 Email、NormalizedEmail 和 PasswordHash 仍为非空字段；服务端使用
  `bot-<user-id-N>@bot.invalid` 以及一次性随机 GUID 生成不可用的 dummy 值；
- dummy password 只在创建时用于生成 PasswordHash，随后立即丢弃，既不返回也不记录；
- Bot 不能通过 Login Endpoint 登录；
- Bot 不能修改密码；
- Bot 不签发 Refresh Token；
- Bot 不参与邮箱验证；
- 平台用户列表和详情必须返回 `UserKind`；
- Access JWT 可以包含 `user_kind=Bot` 供审计和界面展示，但业务权限不依赖该 claim。

Bot 登录尝试统一返回普通的无效凭据结果，不暴露额外账户信息。禁止密码登录是
`UserKind.Bot` 的认证规则，不依赖 dummy password 的未知性。

### Bot 创建与 Token 签发

平台管理员需要普通管理能力：

```text
创建 Bot 用户
为指定 Bot 签发给定有效时长的 Access JWT
递增 Bot.TokenVersion，使该 Bot 的全部现有 JWT 失效
```

签发请求接受明确的正数有效时长，例如：

```json
{
  "expiresInSeconds": 31536000
}
```

返回内容：

```json
{
  "accessToken": "...",
  "expiresAt": "2027-07-29T12:00:00Z"
}
```

JWT 使用普通 Access Token 格式：

```json
{
  "sub": "bot-user-id",
  "token_type": "access",
  "user_kind": "Bot",
  "token_version": 3,
  "jti": "...",
  "iat": 1785300000,
  "exp": 1816836000
}
```

JWT 只在签发响应中返回一次。NoCTF 不保存完整 JWT。管理员撤销时使用已有的 TokenVersion 机制，不引入单 Token 会话表。

平台管理员可在 `/admin/users` 查看 Human/Bot 类型、Role 和 TokenVersion，创建固定为 Organizer 的 Bot、按受控有效期签发一次性显示的 Access JWT，以及使某个身份的全部现有令牌失效。关闭令牌对话框后，前端必须同时清除显示状态和请求缓存中的完整 JWT。

题库管理者可在 `/admin/challenges` 以稳定 UUID、Revision、可见性和活跃比赛引用数核对 GitOps 清单，并切换查看软删除模板。Delete 和 Restore 必须作用于同一 UUID；仍被活跃比赛引用的模板不得从界面发起删除。

建议一场比赛创建一个专用 Bot，并只把该 Bot 加入该 Competition 的 `ManagerIds`。仓库保存：

比赛 GitOps Bot 使用 `UserRole.Organizer`，因为 Competition Owner/Manager 必须是 Organizer 或 Administrator。它不需要也不应拥有平台 Administrator 角色。

```text
NOCTF_BOT_TOKEN
```

作为 GitHub Repository Secret。

### Competition 与 Challenge 权限

Competition Manager 权限不会自动授予全局 Challenge 修改权。

仓库首次创建 Challenge 时，由调用 API 的 Bot 成为 Challenge Owner，因此它以后可以继续更新该模板。若仓库接管已有 Challenge，必须先由 Challenge Owner 或平台管理员把 Bot 加入该 Challenge 的 `ManagerIds`。

仓库脚本调用普通 API 时必须分别满足：

```text
修改 CompetitionChallenge:
    Bot 是 Competition Owner 或 Manager

修改 Challenge、Attachment、模板 Flag:
    Bot 是 Challenge Owner 或 Manager
```

其他比赛可以引用该 Challenge，但应明确接受原 Challenge Owner 的实时模板更新。比赛引用不会复制 Challenge 快照。

## Issue 驱动的题目创建

### Issue Form

`.github/ISSUE_TEMPLATE/create-challenge.yml` 使用 GitHub Issue Form，并自动添加：

```text
challenge:create
```

Issue Form 至少收集：

- 题目标题；
- Direction；
- slug；
- GameMode；
- Runtime 类型：None、Container 或 Compose；
- 初始 BaseScore；
- 初始 Order；
- 题目负责人；
- 简短说明。

Direction 和 slug 用于目录及分支名，必须规范化：

```text
Direction: Web
Slug: sql-notes

目录: web/sql-notes
分支: web/sql-notes
```

slug 只允许小写 ASCII 字母、数字和单个连字符，不能以连字符开头或结尾。Action 还要检查：

- 分支不存在；
- 目录不存在；
- slug 在该 direction 下没有重复；
- GameMode 是 CTF、AWD、AWDP、KoH 之一；
- BaseScore 与 Order 合法；
- Issue 创建者是允许创建题目的仓库成员。

Issue 的自由文本不得直接拼接为 shell 命令、分支命令或文件路径。结构化字段必须先解析和规范化。

### Scaffold Action

`.github/workflows/scaffold-challenge.yml`：

```yaml
name: Scaffold challenge

on:
  issues:
    types: [opened]

permissions:
  contents: write
  issues: write
  pull-requests: write

concurrency:
  group: challenge-scaffold-${{ github.event.issue.number }}
  cancel-in-progress: false
```

当 Issue 带有 `challenge:create` 标签时，Action：

1. 解析并校验 Issue Form；
2. 生成一个 Challenge UUID；
3. 生成一个 CompetitionChallenge UUID；
4. 从最新 `main` 创建 `<direction>/<slug>` 分支；
5. 创建 `<direction>/<slug>/`；
6. 根据 Mode 与 Runtime 类型生成 `challenge.yml` 和对应目录；
7. 创建 `statement.md`、`tests/`、`solution/` 等基础文件；
8. 在 `competition.yml` 中加入 `published: false` 的 CompetitionChallenge；
9. Commit 并 push 新分支；
10. 创建 Draft PR；
11. PR body 写入 `Closes #<issue-number>`；
12. 在 Issue 下回复 Challenge ID、CompetitionChallenge ID、分支和 PR 链接；
13. 添加 `challenge:scaffolded` 标签。

PR 示例：

```text
Title: [Web] SQL Notes
Branch: web/sql-notes

Closes #123

Challenge ID: 8c219fcc-b5cb-48d1-ab45-52333b984f87
Competition Challenge ID: 215e53e6-a17f-4052-9327-a89a602a26f1
```

Draft PR 合并后 GitHub 自动关闭关联 Issue。Scaffold Action 不访问 NoCTF，也不读取 `NOCTF_BOT_TOKEN`。

多个未合并的题目 PR 都会修改 `competition.yml`，因此合并前可能需要更新分支并解决排序冲突。CI 必须检查 Order 和 ID 唯一性。

## Manifest 身份与引用

### `challenge.yml`

通用字段示例：

```yaml
apiVersion: gitops.noctf.dev/v1
kind: ChallengeTemplate

id: 8c219fcc-b5cb-48d1-ab45-52333b984f87
mode: Ctf

title: SQL Notes
direction: Web
visibility: Private
statement: statement.md

metadata:
  author: Alice
  difficulty: Normal

attachments:
  - id: 413281b1-9507-4fce-a981-57fb650ddf79
    path: attachments/handout.zip
    fileName: handout.zip
    contentType: application/zip

flags:
  - id: 15aa869b-c796-40e1-9d5e-80809277913f
    value: flag{sql_notes_example}
    specification:
      kind: Attachment
      attachmentId: 413281b1-9507-4fce-a981-57fb650ddf79
```

`metadata` 是仓库展示信息，不发送 NoCTF。`statement` 文件内容转换为 `Challenge.Description`。

静态 Flag 直接写在 `challenge.yml`，不再增加另一种 Flag 文件协议。静态 Flag 是普通 Git 内容，可以通过 PR 审核和历史追踪。

Attachment 内容不可原位替换。修改附件文件内容时必须同时生成新的 Attachment ID；PR 校验要比较 base commit 中相同 ID 的内容 hash，拒绝“相同 ID、不同内容”。

### `competition.yml`

```yaml
apiVersion: gitops.noctf.dev/v1
kind: CompetitionChallengeSet

competitionId: 7cbe44d2-4b5f-4a7f-927e-d9d21ba8746d
mode: Ctf

challenges:
  - id: 215e53e6-a17f-4052-9327-a89a602a26f1
    challenge: web/sql-notes
    order: 20
    baseScore: 500
    published: false

    hints:
      - id: bb87d310-7fcb-4a69-8ecb-86dcfa037054
        content: 先检查查询参数。
        cost: 50
        publishedAt: null

    rules:
      schemaVersion: 1
```

`competition.yml` 是 CompetitionChallenge 的完整期望集合。它不能覆盖：

- Challenge Mode；
- Runtime；
- Checker；
- Flag 生成；
- Flag 注入；
- build 配置；
- Registry；
- provider；
- runner pool。

仓库脚本把 `challenge.yml` 中 Mode 对应的运行字段转换为 canonical `Challenge.DefinitionJson`，把 `competition.yml.rules` 转换为 canonical `CompetitionChallenge.RulesJson`。

## 四种 Mode 的 Challenge Definition

Manifest 必须按 `mode` 使用判别联合。不能创建一个包含所有可选字段的通用大对象。

### CTF

CTF Challenge 可以是纯静态题，也可以有 PerTeam Container/Compose Runtime。

静态题：

```yaml
mode: Ctf

flags:
  - id: 15aa869b-c796-40e1-9d5e-80809277913f
    value: flag{plain_text_flag}
```

动态 Container：

```yaml
mode: Ctf

build:
  images:
    - key: runtime
      context: runtime
      dockerfile: runtime/Dockerfile
      target: runtime
      platforms: [linux/amd64]

runtime:
  allocation: PerTeam
  flagSource: PerTeam
  definition:
    kind: Container
    image:
      build: runtime
    command: []
    environment: {}
    flagEnvironmentVariableName: FLAG
  limits:
    memoryBytes: 268435456
    nanoCpus: 500000000
    pidsLimit: 128
  endpoints:
    - name: web
      protocol: Http
      containerPort: 8080
      exposure: OwnerOnly
```

CTF 不允许 Checker。仓库脚本根据逻辑 endpoint 生成 NoCTF 当前 Runtime Definition 所需的动态公开端口配置，题目作者不填写 host port。

CTF 的计分曲线、血奖、最大尝试次数、错误提交惩罚等只放在 `competition.yml.rules`。

### AWD

AWD Challenge Definition 管理：

- PerTeam Container 或 Compose Runtime；
- AWD 轮换 Flag 模板；
- Flag injection；
- Checker one-shot Job。

Container 示例：

```yaml
mode: Awd

build:
  images:
    - key: runtime
      context: runtime
      dockerfile: runtime/Dockerfile
      platforms: [linux/amd64]
    - key: checker
      context: checker
      dockerfile: checker/Dockerfile
      platforms: [linux/amd64]

runtime:
  allocation: PerTeam
  flagSource: AwdRotation
  definition:
    kind: Container
    image:
      build: runtime
  limits:
    memoryBytes: 536870912
    nanoCpus: 1000000000
    pidsLimit: 256

flagTemplate:
  header: flag
  bodyTemplate: "[TEAMHASH:32]"
  leetLiteralText: false

flagInjection:
  command: /app/set-flag '${FLAG}'
  timeoutSeconds: 30

checker:
  job:
    image:
      build: checker
    command: []
    environment: {}
    timeoutSeconds: 30
```

AWD Container Checker 不配置目标 URL、目标 host 或目标 port。Runner 把 Checker 加入目标 Runtime 的内部网络并注入内部目标 host；Checker 自己知道服务协议和端口。

Compose 只额外配置内部目标 service：

```yaml
checker:
  targetServiceName: web
  job:
    image:
      build: checker
    timeoutSeconds: 30
```

Checker 通过平台指定的 Internal API 回报状态，不能用进程退出码表达 Up/Down：

- 正常回报 Up 或 Down：使用最后一次有效回报；
- 正常退出但未回报：`Unknown`；
- 非零异常退出：`CheckerAbnormalExit`；
- 超时：`CheckerTimedOut`；
- 后写的同一执行序列结果覆盖先写结果。

Checker interval、攻击计分、防守计分、轮次等放在 `competition.yml.rules`。

### AWDP

AWDP Challenge Definition 管理：

- disposable Container Runtime；
- Patch 入口和执行限制；
- Checker one-shot Job。

```yaml
mode: Awdp

build:
  images:
    - key: target
      context: runtime
      dockerfile: runtime/Dockerfile
      platforms: [linux/amd64]
    - key: checker
      context: checker
      dockerfile: checker/Dockerfile
      platforms: [linux/amd64]

runtime:
  allocation: PerTeam
  definition:
    kind: Container
    image:
      build: target
    internalPorts: [8080]
  limits:
    memoryBytes: 536870912
    nanoCpus: 1000000000
    pidsLimit: 256

patch:
  entrypoint: fix.sh
  command: []
  timeoutSeconds: 60
  readyTimeoutSeconds: 30

checker:
  image:
    build: checker
  command: []
  environment: {}
  timeoutSeconds: 30
```

AWDP 只允许 Container Runtime，禁止公开 endpoint，并且必须声明且只声明一个 `internalPort`。Checker 不配置目标 URL/host/port；Runner 根据 disposable target 的唯一 internal port 建立内部访问。

Break/Fix 分值、尝试次数、惩罚、轮次结算方式和调度方式放在 `competition.yml.rules`。

### KoH

KoH Challenge 使用 Shared Runtime：

```yaml
mode: Koh

build:
  images:
    - key: hill
      context: runtime
      dockerfile: runtime/Dockerfile
      platforms: [linux/amd64]

runtime:
  allocation: Shared
  definition:
    kind: Container
    image:
      build: hill
  limits:
    memoryBytes: 536870912
    nanoCpus: 1000000000
    pidsLimit: 256
  endpoints:
    - name: public
      protocol: Http
      containerPort: 8080
      exposure: Participants
  controlCheck:
    protocol: Http
    containerPort: 8080
    path: /flag
```

KoH 没有独立 Checker Job。NoCTF Worker 通过 Shared Runtime 的 Control Check binding 轮询。Control Flag 由平台生成，不进入仓库。

轮询间隔和每次控制得分放在 `competition.yml.rules`。

## Docker 构建模型

### Build 是仓库元数据

`build` 跟随 Challenge，但在调用 NoCTF API 前必须被移除。NoCTF 只接收最终镜像引用。

```yaml
build:
  images:
    - key: runtime
      context: runtime
      dockerfile: runtime/Dockerfile
      target: runtime
      platforms:
        - linux/amd64
```

约束：

- `key` 在单个 Challenge 内唯一；
- context 和 Dockerfile 必须位于该 Challenge 目录内；
- context 不允许逃逸到父目录；
- `target` 可选；
- 默认 platform 为 `linux/amd64`；
- Manifest 中所有 `image.build` 必须引用已声明 key；
- 未由仓库构建的外部镜像必须使用 digest；
- Runtime、Compose service 和 Checker 共用相同 build key 解析机制。

### Compose 源文件

仓库中的 Compose 是源模板，可以不写最终 `image`，但禁止直接把 `build:` 交给 NoCTF：

```yaml
build:
  images:
    - key: web
      context: runtime/web
      dockerfile: runtime/web/Dockerfile
    - key: proxy
      context: runtime/proxy
      dockerfile: runtime/proxy/Dockerfile

runtime:
  allocation: PerTeam
  definition:
    kind: Compose
    file: runtime/compose.yml
    serviceImages:
      web:
        build: web
      proxy:
        build: proxy
      redis:
        external: redis@sha256:...
```

Action：

1. 读取 `runtime/compose.yml`；
2. 校验 `serviceImages` 与 Compose services 一一匹配；
3. 将 build key 替换为本次构建得到的 digest；
4. 将 external digest 注入对应 service；
5. 删除任何仓库构建字段；
6. 生成 NoCTF `ComposeRuntimeDefinition.ComposeYaml`；
7. 再执行 NoCTF 当前 Compose policy 等价校验。

最终发送给 NoCTF 的每个 Compose service 都有明确、不可变的 `image: registry/...@sha256:...`。

## 变化发现

仓库使用一个 .NET 10 file-based app 入口：

```text
.github/scripts/repository.cs
```

入口通过 `#:include` 组合同目录下按 Validation、Manifest、Build、Scaffold、Apply、
Definition 和 API Client 职责拆分的多个小型 `.cs` 文件；不增加 `.csproj`。它是仓库实现
细节，不发布成独立 CLI，也不要求开发者全局安装工具：

```bash
dotnet run --file .github/scripts/repository.cs -- validate
dotnet run --file .github/scripts/repository.cs -- discover --base <sha> --head <sha>
dotnet run --file .github/scripts/repository.cs -- plan --base <sha> --head <sha>
dotnet run --file .github/scripts/repository.cs -- apply
```

脚本通过 `CallerFilePath` 定位仓库，并使用参数列表启动 git、Docker 和 dotnet，不拼接 shell 命令。

变化范围：

- Pull Request：merge base 到 PR head；
- main push：GitHub event 的 `before` 到当前 SHA；
- Workflow rerun：仍使用原事件的 `before` 与 SHA；
- workflow_dispatch：允许指定 `<direction>/<slug>` 或 `all`。

文件变化使用：

```text
git diff --name-status -z
```

处理新增、修改、删除和 rename。脚本从变化路径向上找到最近的 `<direction>/<slug>/challenge.yml`。

构建判断：

- 新 Challenge：构建其全部 images；
- `challenge.yml` 修改：构建该 Challenge 的全部 images；
- Dockerfile 修改：构建对应 image；
- image context 内文件修改：构建对应 image；
- Compose 文件修改：重新物化 Definition；只有 context 同时变化时才重建镜像；
- Checker context 修改：只重建 Checker image；
- statement、solution 和普通附件修改：不重建镜像；
- `competition.yml` 修改：只应用 CompetitionChallenge；
- 公共构建脚本或 Workflow 修改：构建全部 images；
- Challenge 删除：不构建，进入删除计划。

输出一个按镜像拆分的动态 matrix：

```json
{
  "include": [
    {
      "challengeId": "8c219fcc-b5cb-48d1-ab45-52333b984f87",
      "challengePath": "web/sql-notes",
      "imageKey": "runtime",
      "context": "web/sql-notes/runtime",
      "dockerfile": "web/sql-notes/runtime/Dockerfile",
      "target": "runtime",
      "platforms": "linux/amd64"
    }
  ]
}
```

Matrix 以 image 为粒度，因此一个 Compose Challenge 的多个 service 和 Checker 可以并行构建。

## Registry 配置

### GitHub Repository Variables

```text
NOCTF_API_URL
NOCTF_IMAGE_REGISTRY
CUSTOM_REGISTRY_HOST
CUSTOM_REGISTRY_NAMESPACE
```

含义：

- `NOCTF_API_URL`：NoCTF API origin，例如 `https://ctf.example.com`，不要附加 `/api/v1`；
- `NOCTF_IMAGE_REGISTRY`：`ghcr` 或 `custom`，决定最终写入 NoCTF Definition 的镜像来源，默认 `ghcr`；
- `CUSTOM_REGISTRY_HOST`：可选，例如 `registry.example.com`；
- `CUSTOM_REGISTRY_NAMESPACE`：可选的自定义 Registry namespace。

### GitHub Repository Secrets

```text
NOCTF_BOT_TOKEN
CUSTOM_REGISTRY_USERNAME
CUSTOM_REGISTRY_PASSWORD
```

GHCR 使用当前仓库的 `GITHUB_TOKEN`，不需要额外 PAT。

自定义 Registry 规则：

- `CUSTOM_REGISTRY` 未配置：跳过全部自定义 Registry 步骤；
- 配置 Registry 后 username/password 必须同时存在；
- 配置不完整时立即失败；
- 自定义 Registry 登录或推送失败时，main apply 失败；
- Registry pull credential 属于 NoCTF Runner 的平台部署配置，不进入比赛仓库 Manifest；
- `NOCTF_IMAGE_REGISTRY=custom` 时必须已成功推送并验证 custom digest。

## 镜像命名、标签与 digest

GHCR：

```text
ghcr.io/<owner>/<repo>/<direction>-<slug>-<image-key>
```

自定义 Registry：

```text
<custom-registry>/<namespace>/<direction>-<slug>-<image-key>
```

示例：

```text
ghcr.io/noctf/noctf-2027/web-sql-notes-runtime
ghcr.io/noctf/noctf-2027/web-sql-notes-checker
registry.example.com/noctf-2027/web-sql-notes-runtime
```

主标签：

```text
sha-<full-git-commit-sha>
```

该可选 GitOps 工作流可以额外推送 `latest` 方便人工拉取，但写入 NoCTF 时选择构建所得 digest；
这不代表 NoCTF 平台拒绝或改写由管理员直接配置的可变 tag。该工作流写入示例：

```text
ghcr.io/noctf/noctf-2027/web-sql-notes-runtime@sha256:...
```

OCI metadata：

```text
org.opencontainers.image.source=<repository-url>
org.opencontainers.image.revision=<commit-sha>
dev.noctf.challenge.id=<challenge-id>
dev.noctf.challenge.path=<direction>/<slug>
dev.noctf.image.key=<image-key>
```

同一个 Buildx build 同时附加 GHCR 和自定义 Registry tags，避免重复构建。推送后分别 inspect 两个 Registry 中的 tag，记录实际 digest，不假定 Registry 一定保留完全相同的 manifest 表示。

每个 matrix job 输出：

```json
{
  "challengeId": "8c219fcc-b5cb-48d1-ab45-52333b984f87",
  "challengePath": "web/sql-notes",
  "imageKey": "runtime",
  "ghcr": "ghcr.io/...@sha256:...",
  "custom": "registry.example.com/...@sha256:...",
  "selected": "ghcr.io/...@sha256:..."
}
```

结果以唯一 artifact 名上传：

```text
image-map-web-sql-notes-runtime
```

后续 apply job 下载并合并所有 image-map。Digest 不写回 Git，不生成 lock 文件。

### 未变化镜像的解析

最终 Challenge Definition 必须完整，但一次提交可能只重建一个 image。例如只修改 AWD Checker 时，不应该同时重建 Runtime；只修改 Compose 配置时，也可能没有任何新镜像。

仓库脚本按以下优先级解析每个 `image.build` 或 Compose `serviceImages.build`：

1. 本次 workflow 的 image-map 中有该 build key：使用本次新 digest；
2. 该 Challenge 已存在且对应 Definition 字段仍由同一个 build key 表示：从 NoCTF 当前 Challenge Definition 复用现有 digest；
3. 当前 Challenge 不存在、字段无法对应、旧值不是 digest，或 build key 已改变：把该 image 补入本次构建计划；
4. 仍无法得到 digest：停止 apply。

对应关系由当前 Manifest 结构决定：

- Container `runtime.definition.image.build` 对应当前 Container image；
- Checker `image.build` 对应当前 Checker image；
- Compose `serviceImages.<service>.build` 对应当前 ComposeYaml 中该 service 的 image。

PR 不访问 NoCTF。PR 校验使用明确的本地占位 digest 物化完整 Definition，仅验证结构；main apply 才读取平台当前 Definition 并解析真实未变化 digest。

## Build Cache

使用 GitHub Actions cache backend，并为每个 image 分配独立 scope：

```text
noctf-web-sql-notes-runtime
noctf-web-sql-notes-checker
```

对应 Buildx：

```yaml
cache-from: type=gha,scope=noctf-web-sql-notes-runtime
cache-to: type=gha,mode=max,scope=noctf-web-sql-notes-runtime
```

不能让所有镜像共用默认 scope，否则后构建镜像会覆盖前一个镜像的 cache。

首版不自动清理 GHCR 或自定义 Registry。NoCTF Definition 长期引用 digest，删除旧 manifest 会导致 Runtime Reset 或后续重建无法拉取镜像。只有在能够确认没有任何 Challenge Definition 引用某个 digest 后，才能设计安全的保留策略。

## Pull Request 校验与构建

`.github/workflows/challenge-ci.yml` 同时处理 PR 和 main：

```yaml
name: Challenge CI

on:
  pull_request:
    paths:
      - "*/*/**"
      - "competition.yml"
      - ".github/**"
  push:
    branches: [main]
    paths:
      - "*/*/**"
      - "competition.yml"
      - ".github/**"
  workflow_dispatch:
    inputs:
      challenge:
        description: "<direction>/<slug> or all"
        default: all

permissions:
  contents: read
```

PR concurrency：

```yaml
concurrency:
  group: challenge-pr-${{ github.event.pull_request.number || github.ref }}
  cancel-in-progress: ${{ github.event_name == 'pull_request' }}
```

Job 图：

```text
discover
   └─ validate
         ├─ build-images(matrix)                 (PR)
         └─ resolve-main-image-plan              (main)
                └─ build-images(matrix)           (main)
                       └─ collect-image-maps
                              └─ apply-to-noctf
```

`resolve-main-image-plan` 使用 Bot JWT 读取受影响 Challenge 的当前 Definition，生成：

- 本次必须构建的 image matrix；
- 可以直接复用的 digest map；
- 不涉及 image 的普通 API 变化。

该 job 只做普通 GET，不修改 NoCTF。PR 不运行它。

动态 matrix：

```yaml
strategy:
  fail-fast: false
  max-parallel: 4
  matrix: ${{ fromJSON(needs.discover.outputs.images) }}
```

PR 只构建，不推送：

```yaml
push: false
```

PR job 不读取：

- `NOCTF_BOT_TOKEN`；
- 自定义 Registry username/password。

PR 必须检查：

- YAML 语法和 apiVersion/kind；
- 所有实体 UUID；
- Challenge、CompetitionChallenge、Attachment、Flag、Hint ID 唯一性；
- direction/slug 与目录；
- Competition Mode 与 Challenge Mode；
- Mode 判别联合；
- Challenge Definition 与 CompetitionChallenge Rules 边界；
- 题面和附件路径；
- Attachment ID 与内容不可变规则；
- 静态 Flag 及 specification 引用；
- build key、context、Dockerfile 和 target；
- 外部镜像必须使用 digest；
- Container/Compose Runtime 约束；
- Compose service image 物化结果；
- AWD Checker 不声明目标 URL/port；
- AWDP 唯一 internal port；
- KoH Shared Runtime 和 Control Check；
- Docker build；
- 题目 `tests/`；
- `competition.yml` 的 Order、BaseScore、发布状态和 Hint。

无镜像的纯静态题应让 build job 正常跳过，最终 required check 仍然成功。最终提供一个固定名称的汇总 job，供 branch protection 使用。

## main 构建、推送与应用

main workflow 使用：

```yaml
permissions:
  contents: read
  packages: write
```

执行顺序：

1. discover；
2. validate；
3. 读取受影响 Challenge 的当前 Definition，解析未变化 digest 并完成 build plan；
4. 登录 GHCR；
5. 如已配置则登录自定义 Registry；
6. Buildx 构建受影响 image；
7. 同一次 build 推送所有 Registry tags；
8. inspect 并记录每个 Registry digest；
9. 上传 image-map artifacts；
10. 合并本次 image-map 与复用的 digest map；
11. 将 build 引用和 Compose serviceImages 物化为 selected digest；
12. 计算普通 API 调用计划；
13. 调用 NoCTF。

只要任一受影响镜像构建、Registry 推送、digest inspect 或 Manifest 校验失败，就不得执行 NoCTF apply。

代表性的 image matrix job：

```yaml
build-images:
  needs: [discover, validate, resolve-main-image-plan]
  if: ${{ needs.resolve-main-image-plan.outputs.hasImages == 'true' }}
  runs-on: ubuntu-latest
  permissions:
    contents: read
    packages: write
  strategy:
    fail-fast: false
    max-parallel: 4
    matrix: ${{ fromJSON(needs.resolve-main-image-plan.outputs.images) }}
  steps:
    - uses: actions/checkout@v6

    - uses: actions/setup-dotnet@v5
      with:
        dotnet-version: 10.0.x

    - uses: docker/setup-buildx-action@v4

    - name: Login to GHCR
      uses: docker/login-action@v4
      with:
        registry: ghcr.io
        username: ${{ github.actor }}
        password: ${{ secrets.GITHUB_TOKEN }}

    - name: Login to custom registry
      if: ${{ vars.CUSTOM_REGISTRY != '' }}
      uses: docker/login-action@v4
      with:
        registry: ${{ vars.CUSTOM_REGISTRY }}
        username: ${{ secrets.CUSTOM_REGISTRY_USERNAME }}
        password: ${{ secrets.CUSTOM_REGISTRY_PASSWORD }}

    - name: Prepare tags and labels
      id: image
      run: >
        dotnet run --file .github/scripts/repository.cs --
        image-metadata
        --challenge "${{ matrix.challengePath }}"
        --image "${{ matrix.imageKey }}"
        --commit "${{ github.sha }}"

    - name: Build and push
      id: build
      uses: docker/build-push-action@v7
      with:
        context: ${{ matrix.context }}
        file: ${{ matrix.dockerfile }}
        target: ${{ matrix.target }}
        platforms: ${{ matrix.platforms }}
        push: true
        tags: ${{ steps.image.outputs.tags }}
        labels: ${{ steps.image.outputs.labels }}
        cache-from: type=gha,scope=${{ matrix.cacheScope }}
        cache-to: type=gha,mode=max,scope=${{ matrix.cacheScope }}

    - name: Inspect pushed images
      run: >
        dotnet run --file .github/scripts/repository.cs --
        inspect-image
        --challenge "${{ matrix.challengePath }}"
        --image "${{ matrix.imageKey }}"
        --output image-map.json

    - uses: actions/upload-artifact@v4
      with:
        name: image-map-${{ matrix.artifactKey }}
        path: image-map.json
        if-no-files-found: error
```

PR 使用等价 job，但不登录 Registry、设置 `push: false`，也不上传最终 image-map。

apply job：

```yaml
apply-to-noctf:
  needs: [validate, resolve-main-image-plan, build-images]
  if: >-
    ${{
      always()
      && needs.validate.result == 'success'
      && needs.resolve-main-image-plan.result == 'success'
      && (needs.build-images.result == 'success'
          || needs.build-images.result == 'skipped')
    }}
  runs-on: ubuntu-latest
  permissions:
    contents: read
  steps:
    - uses: actions/checkout@v6

    - uses: actions/setup-dotnet@v5
      with:
        dotnet-version: 10.0.x

    - uses: actions/download-artifact@v5
      if: ${{ needs.resolve-main-image-plan.outputs.hasImages == 'true' }}
      with:
        pattern: image-map-*
        path: .artifacts/image-maps
        merge-multiple: true

    - name: Apply repository state
      env:
        NOCTF_API_URL: ${{ vars.NOCTF_API_URL }}
        NOCTF_BOT_TOKEN: ${{ secrets.NOCTF_BOT_TOKEN }}
        NOCTF_IMAGE_REGISTRY: ${{ vars.NOCTF_IMAGE_REGISTRY }}
      run: >
        dotnet run --file .github/scripts/repository.cs --
        apply
        --commit "${{ github.sha }}"
        --image-maps .artifacts/image-maps
```

示例为了可读性使用版本号。正式仓库模板按前述审核规则把第三方 Action 固定到具体 commit SHA。

main apply 使用独立 concurrency：

```yaml
concurrency:
  group: noctf-main-apply
  cancel-in-progress: false
```

当前执行不被中断，新的 main 状态在后续运行中应用。GitHub 可以淘汰中间 pending run，但最终最新 main 必须重新完成一次完整 apply。

## 仓库侧普通 API 编排

### 不增加平台同步协议

仓库脚本只调用 NoCTF 普通管理 API：

- Challenge template list/get/create/update/restore/delete；
- Challenge configuration get/update；
- Attachment list/create/delete/download metadata；
- 模板 Flag list/create/update/delete；
- CompetitionChallenge list/get/create/update/restore/delete；
- CompetitionChallenge configuration get/update；
- Hint list/create/update/delete；
- Competition get；
- Competition permission检查。

如果现有普通 Create API 由服务端生成 ID，需要扩展为允许传入客户端生成的 UUID。该能力属于普通 CRUD，不包含 GitHub、仓库、commit 或 branch 概念。

普通列表和详情响应需要返回：

- 实体 ID；
- 当前 Revision；
- DeletedAt 或可恢复状态；
- Attachment 内容 hash；
- 调用方执行差异计算所需的字段。

### 期望状态与删除

对于仓库管理的 Challenge：

- `challenge.yml.attachments` 是模板附件完整集合；
- `challenge.yml.flags` 是模板静态 Flag 完整集合；
- `competition.yml.challenges[].hints` 是 Hint 完整集合；
- `competition.yml.challenges` 是本场 CompetitionChallenge 完整集合。

Action 先调用 list API，再按 Manifest ID 比较：

- Manifest 中存在、平台不存在：create；
- 双方存在、字段不同：update；
- 平台存在、Manifest 不存在：soft delete；
- Manifest 指向已删除的相同 ID：restore 后 update；
- 相同字段：skip。

仓库管理的资源不应同时在 UI 中编辑。UI 修改会在下次 main apply 时被 Git 期望状态覆盖。

从 `competition.yml` 移除一项只软删除 CompetitionChallenge，不删除 Challenge template。

删除 Challenge 目录时，main push 的 base/head diff 记录被删除的 Challenge ID。Action 只有在：

- `competition.yml` 已经不引用该 Challenge；
- NoCTF 返回它没有其他有效 CompetitionChallenge 引用；
- Bot 拥有删除权限；

时才调用普通 Challenge soft delete。Workflow rerun继续使用原始 base/head，因此删除操作可以重试。手工执行删除时必须显式提供 base SHA，不能仅凭“当前目录不存在”猜测要删除的 Challenge。

### 调用顺序

因为平台不提供跨资源原子事务，仓库必须采用安全顺序：

1. 获取 Competition、Challenge 和子资源当前状态与 Revision；
2. 校验 Bot 的 Competition 与 Challenge 权限；
3. 创建或恢复 Challenge，保持 `Private`；
4. 更新 Challenge metadata、statement 和 Definition；
5. 创建新 Attachment，再删除不需要的旧 Attachment；
6. 创建或更新模板静态 Flag；
7. 创建或恢复 CompetitionChallenge，保持 `published: false`；
8. 更新 BaseScore、Order 和 Rules；
9. 创建或更新 Hint；
10. 删除 Manifest 中已移除的 Hint、Flag 和 Attachment；
11. 软删除已移除的 CompetitionChallenge；
12. 最后应用目标 Challenge visibility 和 CompetitionChallenge published；
13. 最后处理符合条件的 Challenge soft delete。

新题在所有依赖完成前不会发布。

### Revision 与重试

Challenge 与 CompetitionChallenge 聚合更新携带 API 当前返回的 ExpectedRevision；
CompetitionChallenge soft delete/restore 也必须携带当前聚合 revision，不能把生命周期操作
当作无条件覆盖。
Challenge template 的 Mode 只有在不存在未软删除的 CompetitionChallenge 引用时才能改变；
即使父 Competition 已软删除，仍活动的引用也会返回 typed
`ActiveCompetitionModeConflict`。仓库必须先显式收敛这些引用的生命周期，不能解析错误文本、
强制覆盖或把冲突当作可自动重试的普通 revision race。
Attachment、Flag 和 Hint 不拥有独立 Revision；它们按稳定 UUID、删除状态和内容收敛。
Hint 变更会推进父 CompetitionChallenge revision，因此后续聚合 update/delete/restore 必须
重新读取父资源，不得复用 Hint 写之前的 revision。
发生聚合 Revision conflict 时：

1. 重新读取该资源；
2. 重新计算单资源差异；
3. 如果已经达到目标状态则视为成功；
4. 否则停止 apply 并报告冲突，不强制覆盖未知修改。

稳定 UUID 使 create 可重复：

- ID 不存在时创建；
- ID 已存在且内容一致时跳过；
- ID 已存在但类型、父资源或所有权不匹配时返回冲突；
- 不根据标题、路径或数组位置猜测实体身份。

一次 apply 可能在中途失败。重新运行同一 commit 必须从当前平台状态继续收敛，不能假定前一次完全回滚。

apply job 在 GitHub Job Summary 中输出：

- created、updated、restored、deleted、skipped 数量；
- 每个 Challenge 的变化；
- 构建镜像和 selected digest；
- 失败的 API 操作和响应 problem code；
- 可直接重跑的 workflow run 信息。

不得输出 Bot JWT、Registry password 或完整受保护 Flag。

## 生命周期规则

仓库 Action 和 NoCTF 普通 API 都必须遵守比赛生命周期。

### Draft、Visible、Published

允许完整更新：

- Challenge 题面、附件、静态 Flag和 Definition；
- CompetitionChallenge BaseScore、Order、Rules、Hint 和发布状态；
- 新增、恢复和软删除。

### Running、Paused

允许保存新的 Challenge Definition，但：

- 当前 Runtime Generation 不热更新；
- 已运行实例继续使用创建该 Generation 时的 Definition；
- Runtime Reset 后才使用新的 Definition；
- 已生成的 per-team ChallengeFlag 不被模板 Flag 修改追溯重写；
- Rules 修改只影响后续计分、后续尝试或后续调度；
- 不自动重判既有 GameplayFact，也不改写已经完成的轮次事实；
- 删除或取消发布必须遵守当前领域规则。

Pause 停止相应调度；Resume 后继续，不补跑暂停期间错过的 Checker 或 KoH 观测。

### Finished

比赛结束后拒绝仓库写入 CompetitionChallenge。Challenge 是全局模板，是否允许继续修改由其自身权限和其他引用方状态决定，但不能改变已结束比赛的历史事实。

## GitHub 审核规则

建议保护 `main`：

- 禁止直接 push；
- 所有修改通过 PR；
- 至少一名或两名 Reviewer；
- 必须通过固定名称的 Challenge CI required check；
- 新 commit 撤销旧批准；
- 必须解决 Review conversation；
- 禁止 force push；
- 合并后自动删除题目分支。
- 正式模板中的第三方 Action 固定到审核过的 commit SHA。

示例 CODEOWNERS：

```text
/.github/           @competition-admins
/competition.yml   @competition-admins
/web/               @web-reviewers
/pwn/               @pwn-reviewers
/crypto/            @crypto-reviewers
/reverse/           @reverse-reviewers
/misc/              @misc-reviewers
**/runtime/         @runtime-reviewers
**/checker/         @judge-reviewers
```

修改以下内容必须由比赛管理员审核：

- `.github/**`；
- `competition.yml`；
- Challenge ID 或 CompetitionChallenge ID；
- GameMode；
- build/Registry 解析逻辑；
- published；
- Rules。

Runtime 和 Checker 还应由对应运维或判题人员审核。

## 本地使用

仓库脚本应支持与 CI 相同的本地操作：

```bash
dotnet run --file .github/scripts/repository.cs -- validate
dotnet run --file .github/scripts/repository.cs -- discover \
  --base origin/main \
  --head HEAD
dotnet run --file .github/scripts/repository.cs -- build \
  --challenge web/sql-notes
```

本地 build 使用测试 tag，不推送 Registry，不调用 NoCTF。静态 Flag 是明文内容，不需要额外解密步骤。

## 参考仓库与取舍

本设计参考：

- [Lil-House/CTF-Repo-Base](https://github.com/Lil-House/CTF-Repo-Base) 的题目独立分支、Docker 自动构建、GHCR/自定义 Registry 和附件构建实践；
- [GZCTF/game-template](https://github.com/GZCTF/game-template) 的 direction/challenge 目录、路径过滤、构建前检查、题目目录和统计生成实践。

NoCTF 比赛仓库采用以下取舍：

- 保留“一题一分支”，但分支由 Issue Action 创建，不要求作者手工创建或 fork 仓库；
- 使用 `<direction>/<slug>` 同时作为分支和目录；
- 使用一个仓库级动态 matrix Workflow，不生成每题一份 Workflow；
- PR 仍然经过 Review，不采用长期悬空且不发 PR 的题目分支；
- main 构建使用 commit SHA tag，NoCTF 使用 digest，不依赖 `latest`；
- 不在 PR Workflow 中自动 commit 生成结果；生成文件不一致时直接让检查失败；
- 不自动清理旧镜像，避免删除仍被 Challenge Definition 引用的 digest。

## NoCTF 代码改动

NoCTF 不新增仓库同步模块。平台只需要补齐普通能力：

1. `UserKind.Human/Bot`；
2. Bot 的非空 dummy Email、NormalizedEmail 和 PasswordHash 生成规则；
3. 管理员创建 Bot；
4. 管理员为 Bot 签发指定有效期的普通 Access JWT；
5. Login、Refresh、ChangePassword 对 Bot 的限制；
6. 平台用户列表和详情返回 UserKind；
7. 普通 Create API 接受客户端生成的 UUID；
8. 普通列表/详情 API 暴露 Revision、删除状态和 Attachment hash；
9. 现有 Challenge/CompetitionChallenge/Attachment/Flag/Hint API 保持普通用户授权；
10. 对上述普通 API 补充 Bot 与 Revision 集成测试。

数据库 Migration 必须通过 `dotnet ef migrations` 生成，不手工编辑 migration 或 snapshot。

## 比赛仓库模板需要提供

首版仓库模板至少包含：

- Issue Form；
- Scaffold Action；
- Challenge CI/Main Apply Action；
- 仓库本地 file-based app；
- CODEOWNERS 示例；
- `competition.yml` 示例；
- CTF 静态题示例；
- CTF Container 示例；
- AWD Container 示例；
- AWD Compose 示例；
- AWDP 示例；
- KoH 示例；
- GHCR 配置说明；
- 自定义 Registry 可选配置说明；
- Bot 创建、授权、签发和撤销说明；
- 本地校验与构建说明；
- PR 审核清单。

## 验收标准

### Scaffold

- 从 Issue Form 自动创建 `<direction>/<slug>` 分支；
- 自动创建同路径目录；
- 自动生成 Challenge 和 CompetitionChallenge UUID；
- 自动写入 `competition.yml` 且默认不发布；
- 自动创建 Draft PR；
- PR 通过 `Closes #n` 关联 Issue；
- 重复方向/slug 不会覆盖已有内容。

### PR

- 纯静态题无需 Docker 也能通过；
- 修改一个 Runtime context 只构建对应镜像；
- Compose 与 Checker 多镜像形成独立 matrix；
- PR 不读取 NoCTF 或 Registry Secret；
- Mode、Definition/Rules 和内部网络边界得到校验。

### main

- 修改题目镜像后自动构建；
- 必须推送 GHCR；
- 配置自定义 Registry 后同时推送；
- 任一 Registry 失败时不调用 NoCTF；
- NoCTF Definition 使用选定 Registry 的 digest；
- 不把 digest commit 回仓库；
- Action 只调用普通管理 API；
- 中途失败后重跑可以继续收敛；
- 新题在完整创建前保持 Private/unpublished；
- 当前 Runtime Generation 不被 Definition 更新热替换。

### 边界

- Manifest 中没有 provider、runnerPool、Docker host port、Kubernetes namespace 或 Ingress；
- AWD/AWDP Checker 没有目标 URL/port；
- Checker 结果不依赖进程返回码；
- 静态 Flag 可以明文提交；
- 动态 Flag 不进入 Git；
- Registry pull credential 不进入题目 Manifest；
- NoCTF 不执行仓库解析、Docker build 或仓库级差异编排。

## 建议实施顺序

1. 完成 Bot User、管理员创建和指定时长 Token 签发；
2. 让普通 Create API 支持 Manifest 提供的稳定 UUID；
3. 补齐普通 API 的 Revision、删除状态和 Attachment hash；
4. 建立比赛仓库模板和四种 Mode 的 Manifest schema；
5. 实现 Issue Form 与 Scaffold Action；
6. 实现仓库本地 validate/discover/plan/build；
7. 实现动态 matrix Docker build；
8. 实现 GHCR 和可选自定义 Registry 推送；
9. 实现 image-map 收集与 Compose digest 物化；
10. 实现普通 API apply、Revision 重试和删除顺序；
11. 补齐 PR/Main GitHub Actions 测试；
12. 用四种 Mode 的示例比赛完成端到端验收。

最终边界：

```text
GitHub Issue
    -> <direction>/<slug> 分支和目录
    -> Draft PR 与审核
    -> main
    -> 仓库 Action 构建并推送镜像
    -> 仓库 Action 使用 Bot JWT 调用普通 NoCTF API

NoCTF
    -> 普通 User/Bot 认证
    -> 普通领域权限
    -> 普通资源 CRUD
    -> Runtime 和比赛事实执行
```
