# NoCTF 后端目标架构交接

> 创建于 2026-07-24，最后核验于 2026-07-26。文件名保留原日期，本文内容以最后核验日期为准。

## 1. 下一会话目标

继续完成 `E:\SourceCode\NoCTF\backend` 的目标架构迁移，架构基线以远程
`codex/backend-target-architecture` 及仓库 `AGENTS.md`、`docs/` 为准。

默认只修改 `backend` 与本交接文档。除非用户明确扩大范围，不修改 Frontend、
`backend/Dockerfile`、`deploy`、仓库外 CI 或用户的本地辅助文件。

Container 与 Docker Compose 的持久 Runtime 执行路径已经接通。下一主线是完成
Kubernetes Compose/Kompose，然后接入 OVA/Libvirt，并用真实依赖证明生命周期、
隔离、回写和清理。

## 2. Git 基线与工作树保护

- 仓库：`E:\SourceCode\NoCTF`
- 代码基线分支：`main`
- 代码基线 HEAD：`2c6b4ce fix(backend): reject null runtime URL bindings`
- 本交接分支：`codex/backend-target-architecture-handoff`
- 当前实现 HEAD：`fbd092c feat(backend): provision Docker Compose runtimes`
- 本交接分支从上述 `main` HEAD 创建，随后增加 RuntimeKind definition/dispatch 与
  Docker Compose 纵切。
- 创建交接分支前，`main` 相对 `origin/main`：ahead 186。
- 原目标架构远程分支：`origin/codex/backend-target-architecture`。
- 从旧交接 HEAD `003b75c` 到当前代码基线共有 38 个 backend 提交：

```powershell
git log --oneline 003b75c..2c6b4ce -- backend
```

以下工作树内容属于用户或仅为换行差异，未纳入后端提交；后续不得顺手清理或暂存：

- `backend/src/NoCTF.API/Program.cs`：仅换行差异。
- `backend/src/NoCTF.Runner/Program.cs`：仅换行差异。
- `backend/src/NoCTF.Runner/Properties/`
- `deploy/docker-compose.local-ports.yml`
- `frontend/src/composables/useInstanceOperationState.ts`
- `frontend/src/lib/queryClient.ts`
- `frontend/tests/useInstanceOperationState.test.ts`
- `scripts/`

提交前始终使用显式路径 `git add -- <files>`，不要使用 `git add .`。

## 3. 用户已经确认的架构决策

这些结论不要在下一会话重新发问或自行改写：

1. 一切按照新的目标架构推进，即远程 `codex/backend-target-architecture`。
2. 平台网络名为 `noctf-network`；每个题目/目标另有独立网络。
3. Checker 是可信、受管理员控制的容器，已受最小权限 JWT 约束。
4. Checker 可以同时加入题目网络与 `noctf-network`；平台组件也可按部署需要挂载两个网络。
5. Docker TargetPort ACL 与 callback-only gateway 属于后期网络加固，不是当前迁移阻塞项。
   不得把它们重新标成 P1，也不得声称已经完成底层网络级 ACL。
6. 每一次 AWDP Fix 都创建全新的即时 disposable target，不复用比赛长期 Runtime。
7. Fix 执行期间如果 Competition/Challenge 配置 revision 改变：
   - 当前 Fix 记为 PlatformFailed；
   - 停止并清理该次即时 target；
   - 不自动使用新配置重跑；
   - 管理员需要显式 rejudge。
8. Docker Desktop 已启动；WSL `Ubuntu-22.04` 有完整 .NET/Testcontainers 工具链。
9. 如果出现真正会改变产品语义的歧义，使用 `$grill-me` 向用户确认；不要静默选择。
10. Compose 使用强类型逐服务资源配置；每个 YAML service 必须有一条正资源上限，
    顶层 Runtime limits 是整个 Compose instance 的总预算，逐服务总和不得超过它。

## 4. 2026-07-26 当前 HEAD 的实测门禁

所有命令均在 `E:\SourceCode\NoCTF` 发起，通过 WSL 在
`/mnt/e/SourceCode/NoCTF/backend` 执行。

### Build

```powershell
wsl bash -lc `
  "cd /mnt/e/SourceCode/NoCTF/backend && /home/fs/.dotnet/dotnet build NoCTF.slnx --no-restore"
```

- 成功。
- 0 warnings，0 errors。

### 非 Integration 测试

```powershell
wsl bash -lc `
  "cd /mnt/e/SourceCode/NoCTF/backend && /home/fs/.dotnet/dotnet tests/NoCTF.Tests/bin/Debug/net10.0/NoCTF.Tests.dll --treenode-filter '/*/*/*/*[Category!=Integration]' --minimum-expected-tests 1"
```

- 292/292 passed。
- 0 failed，0 skipped。

### 真实依赖 Integration 测试

```powershell
wsl -d Ubuntu-22.04 -- bash -lc `
  "cd /mnt/e/SourceCode/NoCTF/backend && /home/fs/.dotnet/dotnet test tests/NoCTF.Tests/NoCTF.Tests.csproj --no-restore -- --treenode-filter '/*/*/*/*[Category=Integration]' --minimum-expected-tests 1"
```

- 2026-07-25 的上一个完整门禁为 27/27 passed，0 failed，0 skipped。
- 2026-07-26 完成 Docker Compose 纵切后尚未重跑：当前 Windows Docker Desktop
  Linux Engine pipe 不存在，WSL 也未暴露 `docker` CLI。
- 因此不得声称 `fbd092c` 已通过真实 Docker Compose lifecycle；Docker 恢复后必须
  增加/运行对应 Integration test。

### EF Core 模型

WSL 的 `dotnet-ef` 安装在 `/home/fs/.dotnet/tools/dotnet-ef`，该目录默认不一定在 PATH：

```powershell
wsl -d Ubuntu-22.04 -- bash -lc `
  "cd /mnt/e/SourceCode/NoCTF/backend && env DOTNET_ROOT=/home/fs/.dotnet PATH=/home/fs/.dotnet:/home/fs/.dotnet/tools:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin /home/fs/.dotnet/tools/dotnet-ef migrations has-pending-model-changes --project src/NoCTF.Infrastructure/NoCTF.Infrastructure.csproj --startup-project src/NoCTF.API/NoCTF.API.csproj --no-build"
```

- 成功：`No changes have been made to the model since the last migration.`

### OpenAPI / FastEndpoints

```powershell
wsl -d Ubuntu-22.04 -- bash -lc `
  "cd /mnt/e/SourceCode/NoCTF/backend && /home/fs/.dotnet/dotnet run --project src/NoCTF.API/NoCTF.API.csproj --no-build -- --export-openapi"
```

- 成功。
- FastEndpoints 注册 122 endpoints。

### 其他

- `git diff --check` 在每个已提交纵切前均成功。
- `backend/Verify-Backend.ps1` 仍不存在，是明确交付项。

## 5. 已完成且不要重做

### 5.1 已有基础

- API、Worker、Runner 三个独立进程。
- Wolverine PostgreSQL durable messaging、Runner Pool/Node 路由。
- Competition lifecycle、Start Gate、Pause/Resume、EndAt 与 lifecycle audit。
- RuntimeInstance Generation、ProcessingVersion、replacement、TTL、原节点清理基础状态机。
- Docker/Kubernetes Container lifecycle、容量 gate、receipt 与回写基础闭环。
- Access/Refresh/Internal JWT 基础边界。
- FastEndpoints strongly typed endpoint 基线与 122 endpoint 注册。
- 唯一 EF CLI 生成的 `InitialBaseline`；不可手工编辑 migration/designer/snapshot。

### 5.2 AWDP

从 `9ee49f2` 到 `a3add16` 以及后续相关提交已完成：

- `RequireBreakBeforeFix` intake gate、继承与投影 gate。
- Break/Fix 分离；Fix 不是 Flag 动作。
- 每次 Fix 创建新的 submission-owned disposable target。
- Competition/Challenge configuration revision 固化与 dispatch fence。
- 配置变更时使当前 Fix PlatformFailed，不自动重跑。
- PatchUpload 消费、archive 准备、Patch/Checker 执行与 callback 基础闭环。
- Docker/Kubernetes target/checker 隔离身份与原节点 cleanup ownership。
- Fix penalties、Milestone/PerRound、EffectiveRunningTime、ranking/tie-break 对齐。
- AWDP target 只允许 Docker/Kubernetes Container。
- AWDP target 禁止公开 `PortMappings`/`UrlBindings`。
- Checker 保留 `TARGET_HOST`、`TARGET_PORT`、`TARGET_READY_TIMEOUT_SECONDS` 与 `NOCTF_*`。

不要重新引入共享长期 target、Compose/OVA target，或把 Fix 建模为 Flag。

### 5.3 计分、生命周期与 durable 行为

- AWDP penalties、round grouping、Fix receipt-time ranking。
- AWD round replay precision、service round scores、ranking。
- CTF ranking。
- KoH durable poll chain、Runtime URL、leaderboard projection。
- lifecycle audit 以新事实持久化。
- Wolverine transactional durability 已恢复。
- JSONB 集成断言改为语义比较。

相关提交：

- `a3add16`、`4e97aa2`
- `8280892`、`d95f73c`、`873b854`
- `c1434bc`
- `c29d810`、`e5c5930`
- `0c0f6d8`

### 5.4 Runtime 配置契约

以下保存校验已完成：

- RuntimeKind/Provider/mode compatibility。
- CTF/AWD allocation 约束。
- AWD 禁止 OVA；AWDP 仅 Container；OVA 仅 Static Flag。
- 环境变量 ASCII 名称与 `NOCTF_` 保留前缀。
- URL Binding 按 Container/Compose/OVA 校验 locator。
- URL template 仅 `{HOST}`/`{PORT}` 且展开为绝对 URI。
- CTF URL 仅 `OwnerOnly`。
- `ControlCheckUrlBinding` 仅 KoH。
- Container ControlCheck/public URL 均要求动态端口映射。
- Runtime 必须声明正 resource limits。
- 显式 Security 必须 no-new-privileges、non-root、drop ALL。
- null Checker image、null URL template、null URL Binding 元素返回结构化错误，不抛 500。

相关提交：`b4e99fd` 到 `2c6b4ce` 中所有 Runtime/Checker validation 提交。

### 5.5 Runtime/Checker 平台标签

以下已统一为 `noctf.io/*`：

- 持久 Runtime 的 managed、runtime-instance、competition、competition-challenge、
  team（仅 per-team）、generation 标签。
- AWD Checker 的 managed、runtime-instance、purpose 标签。
- AWDP target/checker 原有 `noctf.io/*` 标签保持。

后续不得重新使用旧的 `noctf.*` 键。

### 5.6 RuntimeKind dispatch 与 Docker Compose

以下已完成：

- `ChallengeRuntimeDefinition` 按 `container | compose | ova` JSON discriminator
  拆为强类型 definition，不再借用 Container `Image`。
- Worker 分别发布 `ClaimContainerRuntime`、`ClaimComposeRuntime`、
  `ClaimOvaRuntime`；Compose/OVA 不再进入 Container claim。
- stop dispatch 按 RuntimeKind 使用独立消息；receipt 仍从持久
  `RuntimeInstance` 读取，不塞入 stop 消息。
- Compose 配置明确包含原始 `ComposeYaml` 与逐服务 `ServiceResources`；
  顶层 `RuntimeResourceLimits` 作为容量 claim 和总预算。
- 保存与 Docker 执行前均解析 YAML，服务集合必须与资源映射完全一致，逐服务总和
  不得超过总预算。
- 永久拒绝文档列出的 privileged/host namespace/volume/device/socket/
  capability/external network 等危险面；平台覆盖 service/network labels、
  environment、动态端口、CPU/内存/PID 上限、drop ALL 与
  no-new-privileges。
- Docker Compose 已接入 durable claim、node ownership、provision、
  `up --wait`、status、receipt、URL expansion、stop 与失败清理。
- 部分创建后清理失败不会释放容量；保留本地 Compose definition 并由 durable
  消息重试，避免仍存活资源造成过量调度。

相关提交：

- `b8b58e9 refactor(backend): split runtime definitions by kind`
- `9c43083 feat(backend): dispatch runtime claims by kind`
- `fbd092c feat(backend): provision Docker Compose runtimes`

## 6. 当前最重要的剩余缺口

### 6.1 Kubernetes Compose/Kompose 尚未执行

这是下一主线的首要缺口。

当前 `KubernetesComposeRuntime.UpAsync` 仍只构造 receipt，未执行固定版本 Kompose
转换与 Kubernetes apply；`GetStatusAsync` 固定返回 Failed，`DownAsync` 是空实现。

需要完成：

- 复用同一 Compose YAML 执行前安全策略。
- 以固定版本 Kompose 转换，不接受题目指定 Namespace。
- 转换后解析全部 manifests，拒绝 Namespace、Ingress、NodePort、
  LoadBalancer、hostPath/volume/config/secret/device 与越权 security context。
- 平台覆盖统一 Namespace、不可覆盖 labels、资源限制、NetworkPolicy 与清理选择器。
- durable provision/status/receipt/URL/stop/reset/expire 与幂等 cleanup。
- 使用真实 Kubernetes/Testcontainers 或项目既定真实测试环境证明，而不是只测 stub。

### 6.2 OVA/Libvirt 尚未接入业务生命周期

当前已有：

- `LibvirtProcessAdapter`
- `LibvirtApplianceLifecycle`
- OVA source URL、Provider、Static Flag 的保存校验

仍缺：

- Worker/Runner durable claim 与 node ownership。
- import/cache/hash、OVF 多 VM、稳定 VmId。
- 独立网络、Guest Agent address、GuestPort URL expansion。
- start/stop/reset/expire 的 appliance 原子生命周期。
- receipt 持久化、迟到回写 fence、幂等 cleanup/reaper。

### 6.3 CTF PerTeam Runtime Flag 注入未闭环

当前 `RuntimeFlagSource` 有 `Static | PerTeam | AwdRotation`，MissingFlagGenerator 能识别
`flagSource=PerTeam`，但 Runtime 配置尚无完整的 `FlagEnvironmentVariableName` 协议，
Worker 也未在新 Container/Compose Generation 创建时注入对应团队固定 Flag。

需要完成：

- CTF Static/PerTeam 与 RuntimeKind 的模式校验。
- Container/Compose 的显式 Flag 环境变量配置。
- Start 前保证 team-scoped ChallengeFlag 存在。
- Worker 在创建该 Generation 时注入；Reset 复用同一固定 Flag。
- OVA 永远不注入动态 Flag。

### 6.4 Egress 与网络模型仍不完整

`docs/runtime.md` 定义 `EgressPolicy: DenyAll | InternetOnly`，但
`ChallengeRuntimeTemplate` 当前没有对应强类型字段，也没有完整的 Docker/Kubernetes
enforcement。

需要区分：

- 题目 Runtime 的长期隔离与 EgressPolicy：仍是目标架构交付项。
- 可信 Checker 的双网络部署：用户已明确允许。
- TargetPort ACL/callback-only gateway：后期加固，不阻塞当前 Runtime 迁移。

不得把 JWT 约束、label 或独立 network 等同于底层网络 ACL 已完成。

### 6.5 Runner capacity、reconciliation 与 orphan cleanup 需要最终闭环审计

基础实现和测试已经存在，但在 Compose/OVA 接入后必须重新证明：

- claim/release 幂等；
- replacement 只占一个槽；
- Runner 失联与 Redis TTL；
- receipt 已存在的 Failed 仍由原节点清理；
- reaper 只按 managed + RuntimeInstanceId + Generation 精确匹配；
- Compose appliance/OVA 多资源 cleanup 不使用宽泛 Competition/Team 标签。

### 6.6 API、DI 与交付项

功能稳定后再做，不与 Provider 业务变化混在一个提交：

- 按 capability 拆 Application/Infrastructure dumping ground。
- API 不引用 Provider；Domain 不依赖 EF/HTTP/Redis/Wolverine/Provider SDK。
- 审计 122 endpoints 的 TypedResults union、OperationId、auth、Produces。
- 创建 `backend/Verify-Backend.ps1`：
  - 分开报告非 Integration 与 Integration；
  - Docker 不可用必须明确 skipped/failure，不能伪装 passed；
  - 包含 build、test、EF pending model、OpenAPI、`git diff --check`。
- 四条真实 E2E：CTF、AWD、AWDP、KoH。

## 7. 建议的下一实施顺序

每一项必须独立 commit：

1. 接入 Kubernetes Kompose/deploy/manifest security validation/cleanup。
2. 接入 Libvirt OVA durable lifecycle。
3. 完成 CTF PerTeam Flag 环境注入。
4. 增加 EgressPolicy 模型及 Docker/Kubernetes enforcement。
5. Docker 恢复后补跑 Docker Compose lifecycle Integration。
6. 重新跑 Provider contract、全部 Integration 与四模式 E2E。
7. 最后做 capability/DI 机械重构和 `Verify-Backend.ps1`。

如果某一步出现产品语义歧义，停止该步并用 `$grill-me`；可以继续不依赖该决策的只读审计，
但不能自行发明新协议。

## 8. 仓库硬约束

- 每个 HTTP endpoint 使用 strongly typed FastEndpoints、`ExecuteAsync`、TypedResults、
  最小 `Results<...>` union。
- route binding、auth、rate limit、transport mapping 留在 Endpoint；业务规则放 Application。
- 有界业务概念使用 enum/value object，不用 string。
- AWDP Break 来自正确 Flag；Fix 是 archive submission，不是 Flag。
- 不保留旧 API、旧 schema、旧 migration 兼容层。
- EF migration/snapshot 只能通过 `dotnet ef` 生成，禁止手改。
- Domain 不依赖 EF/HTTP/Redis/Wolverine/Provider SDK；API 不引用 Provider。
- 禁止 Channel、fire-and-forget、业务 Timer/HostedService、单进程生产模式。
- LINQ 只用 method syntax。
- 测试使用 TUnit；PostgreSQL/Redis/Wolverine 行为使用真实依赖/Testcontainers。
- 不添加通用 Repository、CommandHandler、ResourceManager dumping ground。

## 9. 下一会话开场清单

1. 读取仓库 `AGENTS.md`、本文以及相关 `docs/`。
2. 查看 `git status --short --branch`，确认上述用户文件仍被保护。
3. 读取适用 Skill 的完整 `SKILL.md`；编码任务使用 `$karpathy-guidelines`。
4. 从 Kubernetes Compose/Kompose 的失败测试开始；不要重做已完成的 RuntimeKind dispatch
   或 Docker Compose durable handler。
5. 每个纵切固定执行：
   - 失败测试；
   - 最小实现；
   - 相关测试；
   - 全部非 Integration 测试；
   - 涉及真实依赖时跑对应 Integration；
   - build；
   - `git diff --check`；
   - 只暂存预期文件；
   - 独立 commit。
6. 阶段结束时重跑 EF pending model 与 OpenAPI。
