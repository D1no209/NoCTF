# NoCTF 后端目标架构交接

> 创建于 2026-07-24，最后核验于 2026-07-28。文件名保留原日期，本文内容以最后核验日期为准。

## 1. 下一会话目标

后端目标架构迁移的既定交付项已经完成。下一会话以交接复核为主：审阅本分支提交，
确认受保护工作树未被纳入，按用户指示推送或创建 PR；不要再把 capability/DI 或
`Verify-Backend.ps1` 当作待实现项。

默认只修改 `backend`、Runtime 部署清单与本交接文档。除非用户明确扩大范围，不修改
Frontend、仓库外 CI 或用户的本地辅助文件。

Container、Docker Compose 与 Kubernetes Compose 的持久 Runtime 执行路径已经接通，
OVA/Libvirt lifecycle、orphan reconciliation 与 CTF PerTeam Runtime Flag 注入也已闭环。
EgressPolicy 强类型模型、Docker `DenyAll`、可信双网络 ingress proxy 与 Kubernetes
`DenyAll | InternetOnly` enforcement 已接通，并已在 Cilium `always` 模式的临时集群
完成真实复测。Docker/Kubernetes 持久 Container sandbox、可信 ingress proxy 与
Compose appliance 的 crash orphan reconciliation 也已闭环。受控 Docker Compose 与
Kubernetes 三进程 deployment smoke、真实双栈 Cilium IPv6 deny 验证均已完成并清理；
Admin API 传输层、协议测试、OpenAPI 与文档重构也已完成。CTF、AWD、AWDP 与 KoH
四模式完整边界 E2E 均已通过。Application/Infrastructure capability 归档、DI composition
root 收口和最终 `Verify-Backend.ps1` 也分别由 `39ff3d3`、`984d633` 完成并通过全量门禁。

### 当前完成度

后端目标架构迁移的既定交付项完成度为 **100%**。这是本次目标迁移清单的完成度，
不是测试覆盖率、生产部署状态或生产可用性承诺。

已完成项包含目标数据模型、强类型 API/消息边界、四模式主要业务闭环、
三类 Runtime Provider lifecycle、容量状态机、CTF PerTeam Flag、AWDP 即时 target、
DNS 命名隔离、Docker/Kubernetes 网络策略、Cilium fail-closed 前提和三 Provider
crash orphan reconciliation、三进程 Docker/Kubernetes deployment smoke、真实双栈
IPv6 deny 验证、Admin API 传输契约重构、四模式完整边界 E2E、capability/DI 整理与
可区分测试门禁。第 6 节列出的真实环境验证和后续网络加固仍是独立非阻塞工作，
不得据此把已完成的迁移交付项重新描述为未完成。

## 2. Git 基线与工作树保护

- 仓库：`E:\SourceCode\NoCTF`
- 代码基线分支：`main`
- 代码基线 HEAD：`2c6b4ce fix(backend): reject null runtime URL bindings`
- 本交接分支：`codex/backend-target-architecture-handoff`
- 当前实现与验证 HEAD：`1f0f3e3 fix(deploy): wait for API and Runner readiness`。
- 本交接文档提交前，当前分支相对
  `origin/codex/backend-target-architecture-handoff` ahead 9、behind 0；本文独立提交后
  应为 ahead 10、behind 0。
- 前序实现均先做本地提交；当前用户规则是只有收到明确推送指令后才能推送远端。
- 本交接分支从上述 `main` HEAD 创建，随后增加 RuntimeKind definition/dispatch 与
  Docker Compose 纵切。
- 创建交接分支前，`main` 相对 `origin/main`：ahead 186。
- 原目标架构远程分支：`origin/codex/backend-target-architecture`。
- 2026-07-27 推送前通过 GitHub CLI 认证的 HTTPS fetch 复核：
  `origin/codex/backend-target-architecture-handoff` 为 `0ca38b6`，本地包含该提交；
  文档提交前为 ahead 14、behind 0，没有协作者的 remote-only 分叉需要合并。
- 从旧交接 HEAD `003b75c` 到当前代码基线共有 38 个 backend 提交：

```powershell
git log --oneline 003b75c..2c6b4ce -- backend
```

以下工作树内容属于用户或仅为换行差异，未纳入后端提交；后续不得顺手清理或暂存：

- `backend/src/NoCTF.Infrastructure/Messaging/WolverineTransactionalMessageOutbox.cs`：仅换行状态。
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
11. 当前威胁模型信任办赛管理员、管理员维护的题目配置、Runner Pool 配置和平台托管镜像，
    不防管理员内鬼；选手、题目业务容器及其网络输入仍按不可信处理。
12. `EgressPolicy` 只属于 Container/Compose，OVA 首版不增加该字段。Docker 首版只支持
    `DenyAll`，`InternetOnly` 直接拒绝；Kubernetes 支持两者。
13. Docker 的 `internal` network 不提供可用的直接 published port。公开 Runtime 使用
    用户确认的 1A：平台托管可信 HAProxy ingress，同时连接题目内部 network 与
    `noctf-network`，只转发已声明的 TCP URL Binding；题目容器不连接平台网络。
14. Kubernetes `InternetOnly` 只放行公网 IPv4；IPv6 不放行。特殊/私有地址使用内建
    deny ranges，并要求 Runner Pool 额外声明非空 `ProtectedCidrs`。
15. 生产 Kubernetes Runner Pool 必须使用 Cilium
    `policyEnforcementMode=always`。k3s 内置 NetworkPolicy controller 只允许开发使用，
    不作为生产 fail-closed 安全边界。
16. Runtime Namespace 的全选 Pod default-deny NetworkPolicy 由部署负责创建；Runner
    只在启动时验证 Cilium 配置、基线策略和 `kube-system/kube-dns`，不得自行修改。
17. Kubernetes Runner Pool 必填 `ClusterDnsServiceAddress`。Runner 启动时校验它等于
    `kube-dns` ClusterIP，并在每 Runtime policy 中仅向该地址的 TCP/UDP 53 放行；
    题目 Compose 不承担集群 DNS 地址。
18. 四模式 E2E 使用完整边界：HTTP + 独立 API/Worker/Runner + 真实 PostgreSQL、Redis、
    MinIO 与 Docker；不得用进程内捷径替代。Admin 重构期间暂缓执行，Admin 完成后再继续。
19. Admin 重构采用方案 2：只完整收口 API transport、protocol tests、OpenAPI 与 docs；
    本纵切不重组 Application/Infrastructure capability 或 DI 架构。
20. Competition 普通更新继续使用 lifecycle status concurrency fence，不增加通用
    `Competition.Revision`；模式配置使用 `ConfigurationRevision`。
21. CompetitionChallenge 普通更新与模式配置更新继续使用各自现有 revision。

## 4. 2026-07-28 当前 HEAD 的实测门禁

最终门禁在 Windows 直接执行 `backend/scripts/Verify-Backend.ps1`；下列 WSL 命令保留为
前序专项回归记录。最终脚本摘要为：

```text
[SUMMARY] Non-Integration=PASSED; Integration=PASSED; EF=PASSED; OpenAPI=PASSED; Diff=PASSED
```

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

- 最终 Windows 门禁 359/359 passed。
- 0 failed，0 skipped。

### 真实依赖 Integration 测试

```powershell
wsl bash -lc `
  "cd /mnt/e/SourceCode/NoCTF/backend && /home/fs/.dotnet/dotnet tests/NoCTF.Tests/bin/Debug/net10.0/NoCTF.Tests.dll --treenode-filter '/*/*/*/*[Category=Integration]' --minimum-expected-tests 38"
```

- 最终 Windows 门禁为 46 passed、0 failed、1 skipped；总计发现 47 项。
- 唯一 skip 是未设置 `NOCTF_KUBERNETES_INTEGRATION` 的 Provider dataplane 用例；此前
  2026-07-27 已在一次性 Cilium 集群完成该路径的真实复测，不把本轮 skip 伪装为 passed。
- 非 Integration 与最终 Integration 分组总计 405 passed、0 failed、1 skipped。
- 2026-07-27 启用临时 Kubernetes 集群的 WSL 回归为 33/33 passed、0 failed、0 skipped。
- `3227370` 后再次执行 Integration：32 passed、0 failed、1 skipped；唯一 skip 是未设置
  `NOCTF_KUBERNETES_INTEGRATION` 的 Provider dataplane 用例。本阶段另以完整部署清单完成
  独立 Kubernetes smoke，不把该 skip 伪装为 passed。
- Docker Desktop Engine `28.5.1`、Docker Compose `v2.40.3`。
- `a1f158f` 后重新执行 Docker Container 专项和完整 Integration：单 Container
  7/7 passed；当时完整 Integration 43 passed、0 failed、1 Kubernetes dataplane skipped。
  专项测试发现无显式 `RuntimeInstanceId` 的一次性 checker receipt 会导致 callback
  network 被遗漏。修复后销毁使用与创建一致的
  `RuntimeInstanceId ?? OperationId` ownership identity，回归测试明确断言两个 callback
  network 均已删除。
- Kubernetes 使用一次性 k3d `v5.9.0` / k3s `v1.35.5+k3s1` /
  Cilium `v1.19.6`。k3s 以 `--flannel-backend=none --disable-network-policy`
  启动；Cilium `enable-policy=always`，内建 NetworkPolicy controller 未参与。本轮
  lifecycle/reconciliation 集群没有部署 Runner Pod，也没有修改 kubelet；统一
  `PodPidsLimit=512` 的 Pool/kubelet 契约已由前一轮启动检查与清单验证覆盖。
- 临时集群实测部署了 Cilium 非 Runtime 兼容策略；Runtime Integration 在独立测试
  Namespace 内创建并验证 default-deny 与逐 Runtime policy。验证结束后集群、Docker
  network/volume、kubeconfig context 与
  `/tmp/noctf-runtime-reconcile-it-20260727` 工具目录均已删除。
- 新增真实 Docker Compose lifecycle，覆盖 dynamic port、Compose DNS、exec、
  container/network/workdir cleanup。
- 新增真实 Kubernetes Compose lifecycle，覆盖固定 Kompose、短名 DNS、
  未 Ready Pod DNS、同 Runtime 互通、`DenyAll` 外网拒绝、`InternetOnly` 公网放行、
  `ProtectedCidrs` 拒绝、跨 Runtime 拒绝、NodePort 与两个 Runtime 的精确 cleanup。
- 同一真实 Kubernetes 用例新增持久单 Container，证明 Pod、Service、NetworkPolicy
  与 Compose Deployment/Pod/Service/NetworkPolicy 都能按
  RuntimeInstanceId+Generation 枚举和删除。
- Docker 单 Container 与 Compose 实测新增 crash orphan adapter 路径，覆盖题目
  container、可信 ingress proxy、内部 sandbox network、Compose project/network 和
  本地 operation workdir；平台 `noctf-network` 不在删除 selector 内。
- 真实 Cilium 测试发现并修复 Kubernetes exec error stream 不响应取消、导致策略丢包
  时调用永久等待的问题；Compose 与 Container exec 现在都有调用侧 timeout 边界。
- Cilium 数据路径对 kube-dns ClusterIP 的策略判定不能只依赖 CoreDNS Pod selector；
  当前每 Runtime policy 同时使用 selector 和 Pool 配置的精确 DNS Service `/32`。
- 测试发现并修复 Docker Compose `ps --format json` 在当前 CLI 返回 JSON Lines、
  旧适配器只接受 JSON array 的兼容问题。
- 新增 OVA contract 测试，覆盖 SHA-256、tar traversal、OVF 多 VM/资源预算、
  routed subnet、Guest Agent address、stable identity、URL expansion 与幂等 cleanup。
- 新增真实 PostgreSQL CTF PerTeam Runtime Flag 生命周期测试，覆盖 Start 原子生成、
  Worker dispatch 环境覆盖、批量生成、Static 排除和 Reset 固定 Flag 复用。
- 新增真实 Docker Container/Compose ingress proxy 测试，证明题目容器不连接平台网络、
  proxy 双网络、公开端口可访问、同 Runtime DNS/互通、幂等 replay 与精确 cleanup。
- `deploy/docker-compose.yml` 已通过 `docker compose config --quiet` 静态解析。
- Kubernetes EgressPolicy 已在真实 Cilium dataplane 上复测，不再只是 manifest/unit
  证明。

### CTF 完整边界 E2E（`9c18118`）

- `backend/scripts/Run-CtfE2E.ps1` 使用隔离 Compose project、动态 API 端口和唯一镜像/
  network，启动独立 API、Worker、Runner、PostgreSQL、Redis、MinIO 与真实 Docker
  Runtime；成功和失败路径都只清理本轮资源，不修改现有 `deploy-*` 容器。
- HTTP 流程覆盖种子管理员登录、CTF 比赛/题库模板/附件/CompetitionChallenge/Runtime
  配置/Hint、比赛发布和启动、玩家注册/自动批准团队、MinIO 附件下载、PerTeam Flag
  Runtime、正确 Flag 提交、525 分与 first blood、Hint 扣分至 475、单项 rejudge 后仍为
  475，以及 Runtime stop/容器网络清理。
- 最终独立复跑 1/1 passed，43.293 秒；脚本随后删除全部隔离容器、网络、卷和本地镜像。
- E2E 暴露并修复：迁移阶段首管理员初始化缺失；附件/Hint 新增实体 EF state 错误；
  Worker 缺少跨进程 leaderboard refresh transport；Competition 单项读取在投影后过滤导致
  EF 无法翻译；HintUnlock 被通用 evaluator 当作历史正确提交、使 rejudge 错判 Duplicate；
  fixture 镜像缺少 `httpd`；Docker socket group 权限未传给非 root Runner。
- 首管理员仅在 `SeedAdmin:Password` 配置时创建；已有管理员则幂等跳过、不重置密码，
  不提升普通用户，配置身份被普通用户占用时启动失败。
- leaderboard refresh 沿用现有 Redis Pub/Sub + API SignalR relay 架构；没有把 API-only
  publisher 注入 Worker，也没有新增 fire-and-forget。生产 Compose Runner 继续非 root，
  通过可配置 `DOCKER_SOCKET_GID` 加入宿主 socket group。
- 同轮门禁：解决方案与 E2E 项目均 0 warning/0 error；347 non-Integration passed；
  38 Integration passed、1 Kubernetes dataplane skipped；EF 无 pending model；OpenAPI
  两份 artifact SHA-256 均为
  `7E8B803FB6F368B4E02894488AF5EEF54093F4A889E3130F3A771417D761232A`；两份 Compose、
  PowerShell parser 与 `git diff --check` 均通过。

### AWD 完整边界 E2E（`c46917d`）

- `backend/scripts/Run-AwdE2E.ps1` 使用独立 Compose project、动态 API 端口、唯一网络与
  fixture 镜像，启动独立 API、Worker、Runner、PostgreSQL、Redis、MinIO 和真实 Docker
  Runtime/Checker；最终 1/1 passed，耗时 1 分 42.6 秒，并精确清理本轮资源。
- HTTP 流程覆盖 AWD 比赛/题库模板/CompetitionChallenge/Runtime/Checker 配置、两支自动
  审批队伍、hardening 提交拒绝、每队 Runtime、轮换 Flag、批量攻击提交、重复/过期 Flag、
  Up/Down checker 结果、跨轮计分、leaderboard rank，以及比赛结束后的 Runtime 和 Docker
  资源清理。
- AWD Start 现在在既有 Application port + Infrastructure adapter 边界内，为每个已发布题目
  和已批准未封禁队伍原子创建缺失的 PerTeam Runtime；沿用 `RuntimeInstance` 状态机、
  `DispatchRuntime` 与 Wolverine transactional outbox，没有新增业务表、timer 或进程内捷径。
- AWD PerTeam Flag derivation 纳入 round specification id：同轮重放稳定、跨轮轮换；既有 CTF
  derivation 保持 v1 输入不变。真实 PostgreSQL integration 与 unit regression 均已覆盖。
- E2E 暴露并修复：三进程共享 Wolverine persistence schema 会让其他进程接管 scheduled
  message；现按 API/Worker/Runner 分离 schema，并由 Admin 聚合三者 dead letters。对应真实
  PostgreSQL 测试证明 Worker 停机后 Runner 不会消费其 schedule，Worker 恢复后正常执行。
- Checker callback compose key 修正为 `Runtime__Docker__CallbackContainer`；测试 checker
  deadline 为 15 秒，round 为 30 秒，给真实 Docker network/container 启动、探测、callback
  和 Worker-to-Runner durable hops 留出完整边界时间，不改变生产默认协议。
- quiet match 的 round boundary 现在在创建 successor round 时通过同一 transactional outbox
  发布 `ProjectLeaderboard`；首轮不误发，后继轮精确发布一次，真实 PostgreSQL regression
  已覆盖。最终计分实测 Red/Blue 为 `6/-6`，下一轮为 `17/5`。
- CTF 完整边界独立回归 1/1 passed，45.7 秒；本轮完整门禁为 build 0 warning/0 error、
  354 non-Integration passed、41 Integration passed、1 Kubernetes dataplane skipped、EF 无
  pending model、OpenAPI 导出成功且无内容差异、`git diff --check` 通过。

### 真实双栈 IPv6 deny 验证（`adce710`）

- 一次性 k3d `v5.9.0` / k3s `v1.35.5+k3s1` 集群使用
  `10.42.0.0/16,fd00:42::/56` Pod CIDR 与
  `10.43.0.0/16,fd00:43::/112` Service CIDR；节点实测同时报告
  `10.250.0.4`、`fd00:29::3` InternalIP 和
  `10.42.0.0/24`、`fd00:42::/64` PodCIDR。
- `kubernetes` Service 以 `PreferDualStack` 实测获得
  `10.43.0.1` 与 `fd00:43::aafe`；Cilium `v1.19.6` 使用
  `enable-policy=always`、IPv4/IPv6 enabled 和 IPv6 `/64` node mask，Flannel 与
  k3s 内建 NetworkPolicy controller 均未参与。
- 外部探针是同一 Docker dual-stack network 上的 `fd00:29::10` nginx。无 Runtime
  限制的控制 Pod 能读取该地址；同一个 URL 从 `InternetOnly` Runtime Pod 访问失败，
  证明拒绝来自 Cilium policy，而不是宿主缺少 IPv6 连通性。
- `KubernetesComposeRuntimeIntegrationTests` 新增可选
  `NOCTF_KUBERNETES_IPV6_PROBE_URL`。设置为 IPv6 literal URL 时，同一真实用例会创建
  显式全放行控制 Pod，并同时断言 control allow 与 `InternetOnly` deny；未设置时保留
  原有 single-stack Integration 行为。
- 更新后的 Kubernetes 真实用例 1/1 passed；非 Integration 341/341 passed；build
  0 warnings、0 errors；EF 无 pending model；OpenAPI 仍注册 122 endpoints。
- 宿主 Integration 全量执行为 32 passed、1 failed；唯一失败是 Docker 现存 18 个空
  `noctf-callback-*` 网络耗尽默认地址池，发生在无关的 checker callback network 创建。
  未删除这些既有网络；该唯一用例在临时独立 Docker daemon/独立地址池重跑为 1/1
  passed。因此代码路径共 33 项均通过，但本轮不把受宿主地址池影响的单次 32/33
  报告伪装成一次 33/33。
- 集群、探针、Docker network/volume、临时 kubeconfig、k3d/Helm/Kompose、独立 Docker
  daemon 与本轮镜像标签均已删除。用户原有 `deploy-*` 容器 ID/状态未变化，既有 18 个
  callback network 未被修改。

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
- FastEndpoints 注册 126 endpoints，其中 80 个位于 `/api/v1/admin/*`。
- 80 个 Admin operations 全部具有唯一 `Admin*` OperationId、Summary、Description、
  Bearer security 与 tags；无 Admin 422 response，全部 Admin 400/409 使用
  `application/problem+json`。
- `backend/artifacts/openapi/swagger.json` 与
  `backend/src/NoCTF.API/wwwroot/openapi/v1.json` SHA-256 均为
  `6B249693F0D3F5BAD075E4F51AF32DBEDDC51788ECD984E97062320F157C8098`。

### 其他

- `git diff --check` 在每个已提交纵切前及最终门禁中均成功。
- `backend/scripts/Verify-Backend.ps1` 已由 `984d633` 完成：Build、non-Integration、
  Integration、EF pending model、OpenAPI export/artifact drift 与 `git diff --check` 分步输出
  `[RUN]` / `[PASSED]` / `[SKIPPED]` 和最终摘要。
- Docker 可用时，脚本强制设置 `NOCTF_REQUIRE_DOCKER_INTEGRATION=true` 并运行真实依赖测试；
  Docker 不可用时默认明确报告 `Integration=SKIPPED`，使用 `-RequireDockerIntegration` 或
  预设 require 环境变量时则明确失败。默认 skip 与 required failure 两条分支均已实测。
- OpenAPI exporter 使用稳定 LF；导出后两份 artifact 的 SHA-256 保持一致，artifact drift
  gate 成功。PowerShell parser 检查成功。

### 三进程 deployment smoke（`3227370`）

- 新增独立 Worker image target、Compose service 与 Kubernetes Deployment；API、Worker、
  Runner 均在 migration 成功后启动。部署层旧 Runner HTTP/API-key、QQBot 与 plugin-agent
  残留已删除，Domain/Application/Wolverine/Provider 架构未改变。
- 首次隔离 Compose 启动复现 Worker 收到三条维护消息后报告 `No known handler`。根因是
  static `BackendMessageHandlers` 不属于 Wolverine 的 concrete-type conventional discovery；
  Worker 入口使用 `Discovery.IncludeType(typeof(BackendMessageHandlers))` 显式注册现有类型，
  没有重写 handler 或消息拓扑。
- Docker smoke 使用隔离 project `noctf-smoke-20260727` 和独立 host ports。migration exit 0，
  API `/health`、Runner `/health/ready` 正常；Worker 实际执行并重新排程 Runner assignment、
  Competition lifecycle、AWD checker 三条 durable maintenance chain，0 restart，日志无
  `No known handler`。
- Kubernetes smoke 使用一次性 k3d `v5.9.0` / k3s `v1.35.5+k3s1` / Cilium `v1.19.6`；
  Flannel 与内建 NetworkPolicy controller 关闭，Cilium `enable-policy=always`。严格按
  Runtime baseline、配置/Secret、存储、migration、三进程、network policy 的顺序 apply。
- 最终 API、Worker、Runner 与三项依赖均 Ready、0 restart；Runner 通过 Cilium、baseline、
  kube-dns 启动校验，Worker 在最终 default-deny 后继续更新三条 schedule。数据库最终观察到
  processing version `2/4/3`，证明不是只检查 Pod Running。
- 一次性 k3d 集群、隔离 Compose project/network/volumes、临时镜像标签与工具文件均已删除。
  用户原有 `deploy-*` 六个容器全程未重启，最终仍保持运行；没有应用到生产环境。

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
- `af73a27 feat(backend): provision Kubernetes Compose runtimes`
- `7277323 fix(backend): parse Docker Compose JSON lines status`

## 6. 已完成能力与非阻塞后续限制

### 6.1 Kubernetes Compose/Kompose 生命周期与真实集群验证已完成

当前已完成：

- 复用 Compose YAML 安全策略；Kubernetes Pool 统一 PID，不要求题目逐 service 重复。
- 固定 `/usr/local/bin/kompose` `v1.38.0`，隔离临时目录并受 operation timeout 约束。
- 转换后严格解析 manifests；只接受安全的 Deployment/ClusterIP Service 中间结果，
  拒绝题目 Namespace、Ingress、NodePort、LoadBalancer、volume 与越权 workload 字段。
- 平台重写统一 Namespace、不可变 ownership labels、CPU/内存、安全上下文与单副本。
- 每 Runtime 创建唯一 `rt-<runtime-id>` headless Service，注入 Pod hostname/subdomain、
  Pool `ClusterDomain` search domain 与 `publishNotReadyAddresses=true`。
- 每 Runtime 创建 NetworkPolicy；DNS 仅承担命名隔离，不作为安全边界。
- Pool 必填 `ClusterDnsServiceAddress`；每 Runtime DNS egress 同时匹配 CoreDNS Pod
  与精确 Service `/32`，Runner 启动时核对实际 `kube-dns` ClusterIP。
- 公开 URL 由平台动态创建 NodePort；status 读取实际 NodePort 与完整内部 FQDN。
- Up 等待 Deployment 可用，Down 精确匹配 managed+RuntimeInstanceId+Generation；
  部分创建失败执行确定性清理，清理失败保留容量供 durable retry。
- 默认 Kubernetes 部署改为共享 `runtime` Namespace 的 namespace-scoped RBAC，
  Runner 镜像包含固定 Kompose。

真实 k3s+CNI 集成已证明：

- 固定 Kompose 与 Kubernetes API 可完整执行；
- Compose 短名与 FQDN 可解析，未 Ready endpoint 也会发布；
- 同 Runtime 互通，其他 Runtime 无法访问非公开服务；
- `DenyAll` 无法访问公网；`InternetOnly` 可访问公网但不能访问 `ProtectedCidrs`；
- 平台动态 NodePort 可访问；
- managed+RuntimeInstanceId+Generation 资源删除收敛；
- deployment-owned baseline default-deny 在 workload 创建前生效，Cilium `always`
  对初始化 endpoint fail-closed。

生产 Pool 仍须由运维核对 kubelet 实际 `PodPidsLimit` 与 Runner 配置相等；应用配置不能
替代 kubelet 配置。Runner 只验证 Cilium ConfigMap、kube-dns Service 与 Runtime
baseline policy 的静态状态，不执行启动时 dataplane 探测。

### 6.2 OVA/Libvirt 已接入首版 durable lifecycle 与 orphan reconciliation

当前已有：

- `LibvirtProcessAdapter`
- `ClaimOvaRuntime -> ProvisionOvaRuntime -> RuntimeProvisioned/Failed` 的 Pool claim、
  node ownership 与 ProcessingVersion fence
- `StopOvaRuntime` 从持久 receipt 清理，不在消息中复制 receipt
- OVA source URL、必填 SHA-256、Provider、Static Flag 的保存校验
- 安全 tar 解包、单 descriptor、OVF 单/多 VM、稳定 `ovf:id` VmId
- OVF CPU/内存合计预算校验；PidsLimit 仅作为 Runner 容量预留
- SHA-256 内容寻址缓存、逐磁盘 qcow2 转换、逐 VM Libvirt domain
- Pool/Node routed CIDR、每 Runtime 子网、Guest Agent IPv4 discovery
- OVA public/KoH control URL expansion、receipt 持久化与幂等 stop cleanup
- 任一 VM 导入/地址发现失败时的 appliance 整组回滚
- Worker 复用 durable Runner assignment reconciliation，从 Redis Pool inventory 向在线
  Libvirt 节点投递审计；节点结合 PostgreSQL 当前 assignment，只删除精确
  RuntimeInstanceId+Generation orphan domain/network/workdir

后续第 6.16 节已在真实 KVM/QEMU/Libvirt 隔离测试主机补齐 import、Guest Agent、
routed network、URL、stop、replacement generation 与 identity cleanup 验证。仍缺：

- 在最终生产式 systemd Libvirt 节点执行带上游路由和正式防火墙配置的运维演练；
- 为 Pool/Node CIDR 的非重叠委派增加部署期审计。

### 6.3 CTF PerTeam Runtime Flag 注入已闭环

已完成：

- CTF Runtime 只允许 `Static | PerTeam`；OVA 仍只允许 Static。
- Container 以 `FlagEnvironmentVariableName` 声明唯一变量；Compose 以
  `FlagEnvironmentVariables[serviceName]` 声明一个或多个目标 service。
- PerTeam 必须声明注入目标，Static 必须省略；变量名和 service 引用在保存时校验，
  `NOCTF_` 保留前缀仍禁止使用。
- 玩家或管理员 Start/Reset 在 RuntimeInstance 同一 PostgreSQL 事务中保证团队固定
  ChallengeFlag 存在；使用 `SpecificationKind.RuntimeDefinition` 和
  `SpecificationId=CompetitionChallengeId` 精确标识，Reset 不重新生成。
- MissingFlagGenerator 改走强类型 Runtime catalog，不再错误读取根级字符串
  `flagSource`，因此兼容真实的嵌套配置和 enum JSON 表示。
- Worker dispatch 只读取相同 CompetitionChallenge、Team 和 RuntimeDefinition scope 的
  Flag。Container 直接覆盖镜像环境；Compose 只覆盖映射中列出的 service，未列出的
  service 不接收 Flag。
- OVA 不存在注入字段，仍永远不注入动态 Flag。

### 6.4 Egress 与网络模型已接通首版，部署状态必须区分

代码与部署清单已完成：

- `ContainerRuntimeDefinition`、`ComposeRuntimeDefinition` 使用强类型
  `RuntimeEgressPolicy.DenyAll | InternetOnly`，默认 `DenyAll`；OVA 没有该字段。
- Worker 把策略传入 Container/Compose request；普通 Container 不再使用 Runner shared
  network，而是创建不可变 RuntimeInstanceId+Generation 的独立 sandbox。
- Docker Container/Compose 只接受 `DenyAll`。所有题目 network 强制
  `internal: true`；`InternetOnly` 在保存校验和 provider 执行边界双重拒绝。
- Docker 公开 URL 使用一个平台托管 HAProxy ingress。proxy 同时连接 Runtime 内部
  network 与 `noctf-network`，题目容器不连接平台网络；proxy 只监听声明的 TCP Binding。
  Container receipt 保存 proxy resource id；Compose 保存本地 ingress 映射 metadata，
  status 把 proxy host port 映射回原 service/target port。
- proxy 使用 `read_only`、drop ALL capabilities、no-new-privileges、固定内存/CPU/PID
  limits；其资源开销计入 Runner capacity。创建重放、失败回滚、Stop/Reset cleanup 已覆盖。
- Kubernetes Container/Compose 每 Runtime 创建 NetworkPolicy。`DenyAll` 只允许同
  Runtime、DNS 和平台声明入站；`InternetOnly` 额外允许 `0.0.0.0/0`，通过 `except`
  排除内建特殊/私有 IPv4 与 Pool `ProtectedCidrs`，不生成 IPv6 allow。
- Kubernetes Pool 启动时强制非空、合法 IPv4 `ProtectedCidrs` 与精确
  `ClusterDnsServiceAddress`；Runner 校验 Cilium `enable-policy=always`、
  deployment-owned Namespace baseline default-deny 与实际 kube-dns ClusterIP。
  Docker/Libvirt Pool 不被无关 Kubernetes 配置阻塞。
- `deploy/docker-compose.yml` 将默认平台网络命名为 `noctf-network`，并加入新的
  Runtime Docker/proxy/Runner 配置；`deploy/k8s/configmap.yaml` 加入
  `ProtectedCidrs` 与 k3s 默认 `ClusterDnsServiceAddress` 示例。

当前实际部署状态：

- 新清单只应用到隔离 Compose project `noctf-smoke-20260727` 和一次性本地
  k3d/k3s+Cilium 集群完成 deployment smoke；另一个一次性双栈 k3d/k3s+Cilium 集群只做
  IPv6 deny Integration。三套测试环境及其 network、volume、临时镜像和工具均已删除。
- 用户原有 `deploy-*` 栈没有执行 `up`、`down`、`restart` 或替换；最终六个容器仍运行在
  原有 `deploy_default`。受控 smoke 创建的 `noctf-network` 已随隔离 project 删除。
- 没有任何生产环境部署。本轮 `3227370`、`adce710` 与后续 handoff docs 提交仅保留在
  本地，尚未推送，未创建 PR；Git 提交和临时集群 smoke 都不代表生产部署。

明确尚未实现：

- Docker `InternetOnly`；没有 `DOCKER-USER` 宿主防火墙方案和 Egress Gateway。
- OVA/Libvirt EgressPolicy。
- IPv6 公网 egress、域名/FQDN allowlist、目的端口 allowlist。
- Runner 不做持续 dataplane/Cilium 健康探测；启动检查只证明指定配置和基线资源存在，
  `NetworkPolicyRequired=true` 仍保留运维声明语义。
- Pod/Service/node/management CIDR 自动发现；Pool 运维必须维护 `ProtectedCidrs`。
- 单 Container proxy 镜像的自动 registry 拉取/鉴权；节点须预拉取受信任镜像。Compose
  路径由 Compose pull policy 处理。
- Docker TargetPort ACL 与 callback-only gateway；它们仍是后期加固，不阻塞当前迁移。

不得把可信管理员假设、JWT、label、DNS 名称或独立 network 单独描述成完整安全边界。

### 6.5 Runner capacity、reconciliation 与 orphan cleanup 已闭环

- Worker 的 durable assignment reconciliation 现在向所有有历史 Runtime 的 Pool 在线
  成员投递统一 `ReconcileRuntimeResources`，不再只审计 Libvirt。
- Runner 只接受自己的 Pool/RunnerId，并按节点配置的强类型 Provider 选择 reconciler。
  只有数据库中相同 RuntimeInstanceId+Generation、Provider、Pool、RunnerId 且状态仍为
  Provisioning/Running/Stopping 的资源会保留。
- Docker 只枚举完整匹配 `managed=true + job-kind=persistent-runtime +
  RuntimeInstanceId + Generation` 的 container/network；Compose 额外持久化原子 runtime
  metadata，审计会执行 project down 并删除 operation workdir。题目 container、可信
  ingress proxy、内部 sandbox/Compose network 都被覆盖。
- Kubernetes 使用同一完整 selector 枚举并删除 Pod、Deployment、Service 和
  NetworkPolicy，等待这些资源实际消失后才完成审计。
- Libvirt 通过薄 adapter 接入同一个节点审计协议，保留原有 appliance 精确枚举/销毁。
- 已注册但此前没有调度入口的 Docker/Kubernetes disposable TTL reaper 现在由同一
  durable 节点审计消息驱动；持久资源不带 `expires-at`，不会按 Provider 创建时刻提前
  回收。
- orphan cleanup 不释放 capacity。claim/release、replacement 单槽、Runner 失联、
  receipt cleanup 与 release token 仍由 RuntimeInstance assignment durable 状态机负责，
  避免双重释放；相关 PostgreSQL/Redis/Wolverine Integration 全量通过。

### 6.6 API、capability/DI 与交付门禁

Admin API 传输层已由 `db3cd5e` 独立完成：

- 80 个 Admin endpoint 均为 strongly typed FastEndpoints、`ExecuteAsync` 与最小可达
  TypedResults union；空体 Conflict 已改为结构化 Problem Details。
- 4 条 Competition/CompetitionChallenge configuration GET/PUT 路由恢复为独立强类型端点。
- Competition 与 CompetitionChallenge 的 Admin-only Create/Update DTO 和 Validator 已回到
  最接近的 owning endpoint 文件；没有新建共享 DTO dumping ground。
- Competition 创建限制为 Organizer/Administrator；Admin 读取统一允许 Observer/Judge，
  rejudge/evaluation 写入允许 Judge，管理写入要求 Manager/Owner/Administrator；Owner/Admin-only
  所有权和权限变更仍由 Application/Store 条件保证。
- 错误码与 HTTP 状态已核对：输入/字段错误为 400，状态/revision 冲突为 409，Admin 不再
  暴露 422；400/409 均声明 `application/problem+json`。
- 所有 Admin endpoint 具有唯一稳定 OperationId、Summary、Description、Bearer security；
  `AdminOpenApiRulesTests` 固定 80 个 operation 和上述协议规则。
- `BanTeamRequest.Reason` 增加所属端点 Validator，并由单元测试覆盖。
- `docs/api.md`、`docs/api-conventions.md`、路由漂移测试和两份 OpenAPI artifact 已同步。

Capability/DI 整理由 `39ff3d3` 独立完成，没有改变协议、业务规则、消息拓扑、Provider
行为或服务 lifetime：

- Application 删除横向 `Ports`，Runtime contracts 按 `Provisioning`、`Capacity`、
  `Configuration`、`Callbacks` 归档；Submissions contracts 归入 owning feature。
- Infrastructure 删除 `Persistence/UseCaseAdapters` 与横向 `Caching`，adapter 按
  Administration、Authentication、Challenges、Competitions、Messaging、Notifications、
  Persistence、Runtime、Scoring、Storage、Submissions、Teams capability 归档。
- 通用 `Ef*` 文件和类型按业务责任重命名；仅在区分 provider 有意义时保留 `Postgres` 前缀。
- 根 `ServiceRegistration.cs` 只组合 12 个 capability registration。重构前后均为 137 条
  注册，service/implementation/lifetime 一致。
- 新增 4 条架构规则，禁止 `Ports`、`UseCaseAdapters`、`Services`、`Helpers` dumping ground，
  旧横向 namespace、泛化 `Ef*` 命名和根 DI 直接注册具体服务。
- 测试只做 namespace/type 的必要机械更新，没有改变逻辑或断言；两个受保护 Runner 文件
  只改了编译所需的 `using`，未改行为。

交付脚本由 `984d633` 独立完成，行为与第 4 节最终实测一致。OpenAPI 输出换行稳定化仅位于
显式 export 边界，不改变在线 API 响应或协议 artifact 内容。

### 6.7 CTF 完整边界

CTF 完整边界已由 `9c18118` 独立完成。不要退回进程内测试或只验证 API：

- 标准入口是 `backend/scripts/Run-CtfE2E.ps1`；需要保留失败环境时使用
  `-KeepEnvironment`，排查完必须按输出的唯一 project 精确清理。
- 测试必须继续跨 HTTP、独立 API/Worker/Runner、真实 PostgreSQL/Redis/MinIO 与 Docker；
  Runtime fixture 必须保持非 root、只读 rootfs 和平台 ingress proxy 路径。
- CTF 已验证附件、PerTeam Runtime Flag、first blood、Hint 扣分、leaderboard projection、
  rejudge 与 Runtime cleanup。后续模式可以复用脚本的隔离/清理方式，但不要把四模式压成
  同一个巨型测试或共享可变数据库。

### 6.8 AWD 完整边界

AWD 完整边界已由 `c46917d` 独立完成。不要退回固定 Flag、预建 Runtime 或进程内 Checker：

- 标准入口是 `backend/scripts/Run-AwdE2E.ps1`；需要保留失败环境时使用 `-KeepEnvironment`，
  排查后必须按输出的唯一 project 精确清理。
- 每个已批准未封禁队伍由 Worker 通过 `IAwdRuntimeProvisioner` 创建题目 PerTeam Runtime；
  重放幂等，已有 active generation 不重复创建，仍使用既有 Runtime durable 状态机。
- Flag derivation 必须包含 AWD round specification，Checker callback 必须绑定同一 Runtime
  generation/deadline；跨轮计分变化必须触发 leaderboard projection。
- 三个生产进程各自拥有 Wolverine persistence schema；Admin dead-letter API 聚合 API、Worker、
  Runner 三个 store。不要重新合并 schema，也不要让一个进程执行另一个进程的 schedule。
- AWD 已验证 hardening、targets、轮换 Flag、攻击、duplicate/expired、checker Up/Down、跨轮计分、
  leaderboard 与 cleanup。后续 KoH 继续使用独立 E2E 类、Compose 和可清理资源。

### 6.9 AWDP 完整边界

AWDP 完整边界已由 `e24bfac` 独立完成。不要退回共享 target、进程内 Checker 或把 Fix
建模为 Flag：

- 标准入口是 `backend/scripts/Run-AwdpE2E.ps1`；需要保留失败环境时使用
  `-KeepEnvironment`，排查后必须按输出的唯一 project 精确清理。
- 流程跨 HTTP、独立 API/Worker/Runner、真实 PostgreSQL/Redis/MinIO 与 Docker，验证静态
  Break Flag、无效 archive 422、Break gate 不消费 upload、错误/正确 Break、成功 Fix、
  Patch 失败、精确 Fix rejudge、三个独立 disposable target 及其 checker/network cleanup。
- 实测分数依次为错误 Break `-7`、正确 Break `+40`、首次 Fix `+60`、Patch 失败 `-11`；
  首次 Fix 后 leaderboard 为 93，最终和 rejudge 后为 82。
- Runner 按既有 `Storage:Provider` 选择 `S3ObjectStorage`，因此独立 Runner 可从 MinIO 读取
  PatchUpload；没有新增 storage port 或 adapter。
- 显式 EF 事务内的业务事实先 commit，再 flush Wolverine outgoing messages；修正范围为
  AWD/AWDP result 与 Competition/Challenge configuration 三个既有 store。真实 PostgreSQL
  回归用第二连接证明 flush 时评分事实和 Runtime 状态已提交。
- AWDP achievement 去重只作用于 Break；每次 Fix 即使已有正确 Fix 仍创建新 target。精确
  rejudge 在重新排队时清除上一处理版本的 callback body hash，新处理版本内部仍保持幂等。
- 最终 AWDP E2E 1/1 passed，1 分 41.5 秒；CTF 1/1 passed，45.4 秒；AWD 1/1 passed，
  1 分 39.1 秒。所有隔离 project、fixture 容器、network、volume 和本地镜像均已清理；
  用户 `deploy` 六容器未修改。
- 同轮门禁：solution 与 E2E project 均 0 warning/0 error；355 non-Integration passed；
  41 Integration passed、1 Kubernetes dataplane skipped；EF 无 pending model；OpenAPI 两份
  artifact SHA-256 均为
  `7E8B803FB6F368B4E02894488AF5EEF54093F4A889E3130F3A771417D761232A`；CTF/AWD/AWDP 与
  deploy Compose、三份 PowerShell parser、`git diff --check` 均通过。

### 6.10 KoH 完整边界

KoH 完整边界已由 `a953ae1` 独立完成。不要退回预建 Runtime、进程内 poll、向所有队伍
暴露 Control Flag，或让 Worker 直接访问题目容器的隔离网络：

- 标准入口是 `backend/scripts/Run-KohE2E.ps1`；需要保留失败环境时使用
  `-KeepEnvironment`，排查后必须按输出的唯一 project 精确清理。
- 流程跨 HTTP、独立 API/Worker/Runner、真实 PostgreSQL/Redis/MinIO 与 Docker；验证每题
  一个 shared Runtime、玩家本队 Control Flag、Participants URL、Wrong、Correct、
  ProducerUnavailable、ProducerTimeout、AmbiguousFlagMatch、Pause/Resume、最终 leaderboard、
  finish 与 Runtime/Docker cleanup。
- KoH Running 时由 `IKohRuntimeProvisioner` 原子、幂等创建每个已发布题目的 team-null
  shared Runtime；重放不重复创建，停止后重建递增 generation，继续使用既有
  `RuntimeInstance` 状态机、`DispatchRuntime` 与 transactional outbox。
- Control Flag 使用 `SpecificationKind.RuntimeDefinition` 与
  `SpecificationId=CompetitionChallengeId`；poll 只匹配该合法 scope。玩家 detail 只在
  Running 且队伍已批准、未封禁时返回调用者队伍的 `controlFlag` 与 Participants `urls`，
  不返回 `ControlCheckUrl`。
- Docker receipt 对有 ingress 的 Container 使用可信 ingress proxy 名称作为 `InternalHost`；
  Worker 因此可经平台网络访问 Control URL，题目容器仍只连接自己的 internal network。
- API 将首个 `PollKohChallenge` 路由到 Worker；Worker 将 `ProjectLeaderboard` 路由回自己的
  PostgreSQL durable queue。KoH observation 使用显式 EF 事务写入事实、revision、下一轮 poll
  和 projection outbox，commit 后再 flush；禁止恢复会让 outgoing envelope 滞留的隐式
  `[Transactional]` 写法。
- E2E 曾直接复现 Worker outgoing 表累积 20 条未发送 `ProjectLeaderboard`、dead letter 为 0、
  snapshot 停在旧 revision。修复后 resume 的 Blue Team Correct 已进入 snapshot；最终
  `stale=false` 在 finish 停止持续 observation 后断言，避免把正常持续写入窗口误判为最终投影。
- 最终 KoH E2E 1/1 passed，1 分 29.8 秒；同轮 CTF 1/1 passed，50.6 秒；AWD 1/1 passed，
  1 分 42.6 秒；AWDP 1/1 passed，1 分 25.9 秒。所有隔离 project、fixture 容器、network、
  volume 和本地镜像均已清理；用户 `deploy` 栈未修改。
- 同轮门禁：solution 与 E2E project 均 0 warning/0 error；355 non-Integration passed；
  43 Integration passed、1 Kubernetes dataplane skipped；EF 无 pending model；OpenAPI 两份
  artifact SHA-256 均为
  `6B249693F0D3F5BAD075E4F51AF32DBEDDC51788ECD984E97062320F157C8098`；四模式与 deploy
  Compose、四份 PowerShell parser、`git diff --check` 均通过。

### 6.11 Docker Container Provider 专项复测与 callback cleanup

用户于 2026-07-28 要求先把 Docker Container 模式测试好。`a1f158f` 完成了本轮唯一代码
修复并补充真实 Docker 回归：

- `DockerContainerLifecycleTests` 7/7 passed，覆盖隔离网络幂等、持久 Runtime 双网络
  HAProxy ingress、stdin exec、超时后终止容器进程树、AWDP checker 独立 callback
  network、AWD checker 同时连接 Runtime 与 callback network，以及创建失败回滚。
- 回归测试先证明原实现会残留 `noctf-callback-*`：checker 未显式传
  `RuntimeInstanceId` 时，创建标签使用 `OperationId`，但销毁只接受非空
  `RuntimeInstanceId`。修复后销毁使用相同的 effective identity，并断言两个 callback
  network 均不可再查询。
- 完整测试重新执行：solution build 0 warning/0 error；E2E project 在 WSL build
  0 warning/0 error；359 non-Integration passed；当时 43 Integration passed、0 failed、
  1 Kubernetes dataplane skipped。`eb36632` 增加 Compose 回归后最新值为
  46 Integration passed、0 failed、1 Kubernetes dataplane skipped。
- 四模式 Docker 完整边界重新执行并通过：CTF 1/1（54.2 秒）、AWD 1/1（1 分 37.1 秒）、
  AWDP 1/1（1 分 26.4 秒）、KoH 1/1（1 分 09.8 秒）。AWD 首次构建遇到 Docker Hub
  匿名 token 网络超时，预拉取 `python:3.13-alpine` 与 `curlimages/curl:8.14.1` 后业务
  流程通过；不得把该外部拉取失败记为平台测试失败。
- 四个本轮隔离 Compose project 的 container、network、volume 和项目镜像均已清理；
  另清除了 12 个无容器引用、标签明确属于两个旧 AWD E2E project 的可重建残留镜像。
  最终没有 E2E network、E2E project image 或 managed callback network。
- 用户原有 `deploy-backend-1`、`deploy-worker-1`、`deploy-runner-1`、PostgreSQL、Redis、
  MinIO 六个容器保持运行在 `deploy_default`；本轮没有对该栈执行 up、down、restart 或
  替换，也没有生产部署。

### 6.12 Docker Compose Provider 专项复测与真实 CTF 边界

用户于 2026-07-28 确认在单 Container 后继续完成 Docker Compose 专项。`eb36632`
补齐了生命周期、失败清理和完整边界证据，并完成本轮唯一生产代码修复：

- `DockerComposeRuntimeIntegrationTests` 从 1 项增加到 4 项并全部通过。真实 Docker
  覆盖多 service DNS、可信 HAProxy 双网络入口、公开端口、selective PerTeam Flag
  environment、实际 memory/CPU/PID/capability 限制、AWD checker Runtime+callback
  双网络、相同 operation replay、正常 `Down`、crash orphan reconciliation 和
  `docker compose up` 失败后的 partial resource cleanup。
- 测试先证明非法 Compose 在纯内存策略阶段被拒绝后仍会留下 operation workdir。
  `DockerComposeRuntime.UpAsync` 现在先执行 definition 与 ingress policy，再创建
  workdir；无 Docker 资源产生的验证失败不会污染 Runner 工作目录。
- CTF 完整边界新增第二个真实题目：API 保存一个 `web`+`db` Compose definition，
  PerTeam Flag 只映射到 `web`；玩家经 HTTP 启动后，消息跨 API、Worker、Runner，
  Runner 使用 Docker Compose 创建 appliance 与 ingress，玩家从公开 URL 读回不同于
  单 Container 题目的生成 Flag，最后两个 Runtime 均经平台停止。
- 新边界首次运行发现 `backend/tests/NoCTF.E2E/Dockerfile.runner` 未像生产 Runner 镜像
  一样包含 Docker CLI/Compose plugin；单 Container 走 Docker API，所以此前未暴露。
  E2E Runner 现从 `docker:28-cli` 复制 CLI 与 plugin，测试拓扑能够真实执行 Compose。
- 最新门禁：solution 与 E2E project 均 0 warning/0 error；359 non-Integration passed；
  46 Integration passed、0 failed、1 Kubernetes dataplane skipped；合计 405 passed、
  0 failed、1 skipped。
- 四模式最终回归：CTF（包含 Container+Compose）1/1 passed，1 分 17.9 秒；AWD 1/1，
  1 分 36.1 秒；AWDP 1/1，1 分 24.7 秒；KoH 1/1，1 分 10.9 秒。期间 Docker Hub
  `docker:28-cli` 匿名 token 曾网络超时；预拉取后同一测试通过，该外部失败不计为平台
  功能失败。
- 最终 E2E container、network、project image、managed persistent Runtime network 与
  callback network 数量均为 0。用户 `deploy-*` 六容器继续运行在 `deploy_default`，
  未执行 up、down、restart 或替换；没有生产部署。

### 6.13 Runner 容量并发与异常恢复专项

用户于 2026-07-28 要求在 Docker Container/Compose 专项后继续验证 Runner 容量与异常
恢复。`4634244` 没有改动生产协议或实现，而是在真实 Redis 上补齐此前缺少的并发压力证据：

- `RedisRunnerCapacityGateTests` 从 6 项增加到 9 项并全部通过。64 个不同 Runtime 同时向
  `1024 memory / 100 CPU / 10 PID` 的同一 Runner 申请 `128 / 10 / 1`，精确产生 8 个
  claim 和 56 个 capacity insufficient；剩余容量为 `0 / 20 / 2`，没有超卖或负数。
- 对 8 个成功 claim 各并发发送两次 release，精确产生 8 个 `Released` 和 8 个
  `AlreadyReleased`；容量恢复为配置总量，全部 claim key 删除，没有重复归还。
- 64 次同一 Runtime claim 在两个在线 Runner 间交错重放，只有一个 `Acquired`，同一
  owner 的其余 31 次为 `AlreadyOwned`，另一 Runner 的 32 次全部拒绝；两个节点合计只
  扣减一次，错误 owner 不能释放，正确 owner 释放后精确恢复。
- Runner heartbeat 丢失后，新 claim 被拒绝；已经存在的 claim 仍可释放并恢复容量。
  这避免离线节点继续接单，也不会让失联恢复流程因 heartbeat 已过期而无法归还预算。
- 既有 `RunnerAssignmentReconciliationTests` 6/6 重新通过，继续覆盖离线 assignment
  redispatch、Redis 不可用时整批 defer、后续 heartbeat 查询不可用时不做部分释放、
  replacement 等待容量释放、在线节点资源审计和原节点 receipt ownership。消息重放、
  assignment/orphan 状态收敛没有发现新的生产代码缺陷。
- 最新完整后端门禁：solution build 0 warning/0 error；359 non-Integration passed；
  49 Integration passed、0 failed、1 Kubernetes dataplane skipped；EF 无 pending model；
  OpenAPI artifact 无漂移；`git diff --check` 通过。合计 408 passed、0 failed、1 skipped。
- 四模式完整边界再次通过：CTF（Container+Compose）1/1，1 分 14.6 秒；AWD 1/1，
  1 分 43.0 秒；AWDP 1/1，1 分 21.0 秒；KoH 1/1，1 分 06.4 秒。
- 最终本轮 E2E container、network、volume 和 project image 数量均为 0。用户现有
  `deploy-backend-1`、`deploy-worker-1`、`deploy-runner-1`、PostgreSQL、Redis、MinIO
  六个容器保持运行；本轮没有部署、重启或替换任何环境。

### 6.14 本地 deploy 栈更新与内置 SPA 修复

用户于 2026-07-28 明确确认原 `deploy-*` 栈和数据均已过时，授权不做备份直接删除并以当前
架构重建。本轮已完成实际本地部署；这不是生产部署：

- 旧 PostgreSQL 卷记录 31 条历史 migration，history table 使用旧 `MigrationId` 列；
  当前基线读取 `migration_id`，与既定“不保留旧 persistence compatibility”一致，不能
  编写兼容迁移。经用户确认后，旧 `deploy` 容器、遗留 `deploy-frontend-1`、旧
  `deploy_default` network 和 `deploy_postgres_data`、`deploy_redis_data`、
  `deploy_minio_data`、`deploy_backend_uploads` 四个卷均已无备份删除，不可恢复。
- 以当前分支重新创建 `noctf-network`、四个空数据卷以及 PostgreSQL、Redis、MinIO、
  migration、API、Worker、Runner。migration exit 0，数据库只包含
  `20260728013508_InitialBaseline`；PostgreSQL、Redis、MinIO 健康。
- Runner 镜像包含 Docker CLI `28.5.2`、Docker Compose `2.40.3` 与 Kompose `1.38.0`。
  初次 BuildKit 通过本机代理下载固定 Kompose release 时连续收到 GitHub 502；普通容器
  直连下载正常，最终以无代理/传统构建路径成功完成镜像，没有为外部代理故障修改生产协议。
- 部署实测发现 API 镜像虽然包含 Vite `wwwroot`，pipeline 没有启用静态文件，导致根路径
  404。`a27dfd7` 启用 default/static files，并只对 GET、`Accept: text/html`、无文件扩展名、
  非 `/api`、`/hubs`、`/health`、`/openapi`、`/swagger` 的 404 导航返回
  `index.html`；API 404 不会被 SPA fallback 吞掉。
- CTF 完整边界新增 SPA 回归：`/` 与 `/login` 均返回 `text/html` 和 Vite app root，
  `/api/v1/does-not-exist` 仍为 404。实际本地 deploy 同样为 health 200、root 200、
  login 200、unknown API 404。
- 最终本地 deploy 的 API、Worker、Runner 均 0 restart；Runner heartbeat TTL 正常，
  capacity 为配置的 `4294967296 / 2000000000 / 2048`，三条 durable maintenance
  processing version 从 `4/4/20` 持续推进至 `40/40/237`，API/Worker/Runner dead letter
  均为 0。
- 完整后端门禁：solution build 0 warning/0 error；359 non-Integration passed；
  49 Integration passed、0 failed、1 Kubernetes dataplane skipped；EF 无 pending model；
  OpenAPI artifact 和 `git diff --check` 无漂移。合计 408 passed、0 failed、1 skipped。
- 修复后的四模式完整边界：CTF（含 SPA、Container+Compose）1/1，1 分 26.1 秒；
  AWD 1/1，1 分 42.6 秒；AWDP 1/1，1 分 16.3 秒；KoH 1/1，1 分 05.7 秒。
  最终 E2E container、network、volume、project image 数量均为 0；新建的本地
  `deploy-*` 栈继续运行。

### 6.15 本地 deploy 重启、替换与 readiness

用户于 2026-07-28 要求继续推进后，本轮在第 6.14 节的新数据基线上验证进程重启和容器替换：

- `docker compose restart backend worker runner` 后，API 约 5.1 秒恢复。PostgreSQL、
  Redis、MinIO 和三个应用容器 ID 均未改变；maintenance processing version 从
  `173/173/1033` 推进到 `175/175/1041`，证明 durable 状态在进程重启后继续执行。
- `docker compose up --no-deps --force-recreate backend worker runner` 后，API 约
  5.7 秒恢复。三个应用容器 ID 全部改变，三个依赖容器 ID 保持不变；maintenance version
  继续推进到 `176/176/1051`，唯一 `InitialBaseline`、Runner heartbeat/capacity 和
  SPA/API 路由均保持正确。
- 两轮恢复后 API、Worker、Runner dead letter 仍为 `0/0/0`，新容器日志没有
  `fail`、`crit`、unhandled exception 或 `No known handler`。
- 实测发现原 Compose 没有 API/Runner healthcheck，`docker compose up --wait` 只能确认
  进程已启动。`1f0f3e3` 使用基础镜像已有 Bash `/dev/tcp`，分别向 API `/health` 和
  Runner `/health/ready` 发出真实 HTTP 请求并要求状态行包含 200；没有向运行镜像增加
  curl/wget 或新的平台协议。
- 更新后的真实 Compose 重建 API/Runner 后，`up --wait` 明确等待两者进入 `healthy`；
  `docker inspect` 的 health command 已正确把 Compose `$$status` 转换为 shell
  `$status`，两项探针 exit 0。Worker 没有伪造仅检查 PID 的 healthcheck。
- 新增 `DeploymentTopologyTests` 断言两个 HTTP 探针及 200 检查不能被静默移除。
  定向测试 1/1、全部 359 non-Integration、solution build 0 warning/0 error、
  Compose config 和 `git diff --check` 均通过。第 6.14 节的完整 Integration 与四模式
  E2E 已在同一代码基线上通过；本提交只修改 deployment Compose 与拓扑断言。
- GitHub CLI 只读复核远端交接分支仍为 `96e8b1d`，与本地 remote-tracking ref 一致；
  没有协作者 remote-only 提交。普通 `git fetch` 因 `origin` 使用 SSH 且当前 SSH key
  未授权而失败，没有修改 Git 认证配置。

### 6.16 真实 Libvirt/OVA lifecycle 验证

用户于 2026-07-28 要求继续推进后，本轮选择不改变安全语义的真实 Libvirt/OVA 验证纵切：

- 新增显式 opt-in 的 `LibvirtOvaRuntimeIntegrationTests`。默认无
  `NOCTF_LIBVIRT_DISK_PATH` 时明确 skip；受控 Runner 提供一个已启用
  `virtio_net` DHCP 与 QEMU Guest Agent 的启动盘。测试自行流式生成临时 OVA，不把
  大型镜像、OVA 或 Guest 凭据提交到仓库。
- 真实测试使用 `/dev/kvm`、Libvirt 8.0、QEMU 6.2、`qemu:///system` 和真实
  `virsh`/`qemu-img`/`virt-install`。fixture 基于官方 Ubuntu 24.04 minimal cloud
  qcow2 的临时副本，校验发布 SHA-256 后离线加入 `qemu-guest-agent` 与明确的
  `virtio_net` DHCP netplan；fixture 只用于本轮测试，已删除且不可恢复。
- generation 0 真实完成 OVA SHA-256、解包、qcow2 转换、独立 routed network、KVM
  domain、Guest Agent 唯一 IPv4；测试通过 Guest Agent 在 VM 内启动 Python HTTP，
  并从 Libvirt 主机直连 `http://<guest>:8080/` 验证 URL 可达。随后使用持久 receipt
  执行 stop cleanup。
- 同一 operation 的 generation 1 创建全新即时目标，再次取得 Guest Agent 地址，最后
  使用完整 operation+generation identity 执行 orphan cleanup。两轮结束后 domain、
  `noctf-*` network 与 workdir 均为空；扩展测试 1/1 passed，用时 3 分 04.6 秒。
  Reset/Expire 在 Provider 边界复用 replacement generation 与 stop cleanup，因此该测试
  覆盖其实际资源生命周期，而不伪造新的 Provider 动作。
- 首次直连 WSL system Libvirt 暴露宿主 `iptables-nft` 原生表与 Libvirt 8.0
  `iptables` backend 不兼容；未切换 WSL 全局 alternatives。最终验证改用一次性特权
  Ubuntu 22.04 Libvirt 容器，透传 `/dev/kvm`，并把 routed bridge/firewall 留在容器
  namespace。该容器因无 systemd PID 1，QEMU 回收约需 40 秒；这不是生产节点性能基线。
- 真实失败过程发现 `qemu-img`、`virt-install`、`virsh net-define/net-start` 的 stderr
  被泛化异常丢弃。生产代码现保留原错误分类，同时把工具 stderr 加入内部异常，后续节点
  配置错误可直接定位；纯单元回归强制 `net-start` 失败并断言 stderr 保留。没有改变
  OVA、网络、Guest Agent、超时或清理语义。
- 最终门禁：solution build 0 warning/0 error；Libvirt 单元 5/5；真实 Libvirt opt-in
  1/1；360 non-Integration passed；常规真实依赖 Integration 49 passed、0 failed，
  Kubernetes 与默认未配置 fixture 的 Libvirt 各 1 skipped；EF 无 pending model；
  OpenAPI artifact、`git diff --check` 均无漂移。本轮 4 个 C# 文件的 scoped
  `dotnet format --verify-no-changes` 通过。全仓 format 仍会报告 AWD validator、Flag
  generator、旧 leaderboard tests 等既有 whitespace 基线，本轮未扩大范围修改它们。
- 一次性 Libvirt 容器、定制工具镜像、fixture 目录均已精确删除；WSL system Libvirt
  最终无 domain、只有原有 inactive `default` network。当前本地 `deploy` 六服务继续
  运行，API 与 Runner healthy，未被本轮测试重启或替换。

本次目标迁移没有剩余实现项。真实 Provider lifecycle 已验证；仍未完成的是生产式
Libvirt 节点上游路由/防火墙运维演练、Pool/Node CIDR 部署期审计、Docker
`InternetOnly` 和后期 TargetPort/callback gateway 加固。它们均已明确边界，不是
capability/DI 或交付脚本遗漏，也不得在没有新的用户批准和设计评审时顺带实现。

## 7. 建议的下一交接顺序

1. 审阅 `2c6b4ce..HEAD` 的提交序列与本文，确认目标迁移边界、架构约束和实测证据一致。
2. 再次执行 `git status --short --branch`，确认本文提交未包含第 2 节的用户文件。
3. 按用户指示推送 `codex/backend-target-architecture-handoff` 或创建 PR，进入代码评审。
4. 只有用户另行批准时，才从第 6 节非阻塞限制中选择新的纵切；不得把它们混入本次迁移交付。

如果后续工作出现产品语义或重大架构歧义，停止该步并用 `$grill-me`；可以继续不依赖该
决策的只读审计，但不能自行发明新协议。

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
4. 当前迁移已完成，从提交审阅和交接开始；不要重做 capability/DI、`Verify-Backend.ps1`、
   四模式 E2E、deployment smoke、双栈 IPv6 deny、RuntimeKind dispatch、Docker/Kubernetes
   Compose handler、三 Provider crash orphan reconciliation、IPv4 Cilium egress 验证或
   Admin API 传输层重构。
5. 若用户批准新的实现纵切，固定执行：
   - 失败测试；
   - 最小实现；
   - 相关测试；
   - 全部非 Integration 测试；
   - 涉及真实依赖时跑对应 Integration；
   - build；
   - `git diff --check`；
   - 只暂存预期文件；
   - 独立 commit。
6. 新纵切阶段结束时重跑 EF pending model、OpenAPI 与统一 `Verify-Backend.ps1`。
