# NoCTF 后端目标架构交接

> 创建于 2026-07-24，最后核验于 2026-07-27。文件名保留原日期，本文内容以最后核验日期为准。

## 1. 下一会话目标

继续完成 `E:\SourceCode\NoCTF\backend` 的目标架构迁移，架构基线以远程
`codex/backend-target-architecture` 及仓库 `AGENTS.md`、`docs/` 为准。

默认只修改 `backend`、Runtime 部署清单与本交接文档。除非用户明确扩大范围，不修改
Frontend、仓库外 CI 或用户的本地辅助文件。

Container、Docker Compose 与 Kubernetes Compose 的持久 Runtime 执行路径已经接通，
OVA/Libvirt lifecycle、orphan reconciliation 与 CTF PerTeam Runtime Flag 注入也已闭环。
EgressPolicy 强类型模型、Docker `DenyAll`、可信双网络 ingress proxy 与 Kubernetes
`DenyAll | InternetOnly` enforcement 已接通，并已在 Cilium `always` 模式的临时集群
完成真实复测。Docker/Kubernetes 持久 Container sandbox、可信 ingress proxy 与
Compose appliance 的 crash orphan reconciliation 也已闭环。受控 Docker Compose 与
Kubernetes 三进程 deployment smoke、真实双栈 Cilium IPv6 deny 验证均已完成并清理；
下一主线是 CTF、AWD、AWDP、KoH 四模式真实 E2E。

### 当前完成度

后端目标架构迁移当前完成度估算为 **92%**。这是按剩余交付里程碑计算的工程进度，
不是测试覆盖率或生产可用性承诺。剩余 8 个百分点固定分配为：

- CTF、AWD、AWDP、KoH 四模式真实 E2E：5%；
- capability/DI 机械整理与最终 `Verify-Backend.ps1`：3%。

已完成的 92% 包含目标数据模型、强类型 API/消息边界、四模式主要业务闭环、
三类 Runtime Provider lifecycle、容量状态机、CTF PerTeam Flag、AWDP 即时 target、
DNS 命名隔离、Docker/Kubernetes 网络策略、Cilium fail-closed 前提和三 Provider
crash orphan reconciliation、三进程 Docker/Kubernetes deployment smoke，以及真实双栈
IPv6 deny 验证。未完成项仍可能在真实验证中暴露返工，因此百分比只用于交接排期。

## 2. Git 基线与工作树保护

- 仓库：`E:\SourceCode\NoCTF`
- 代码基线分支：`main`
- 代码基线 HEAD：`2c6b4ce fix(backend): reject null runtime URL bindings`
- 本交接分支：`codex/backend-target-architecture-handoff`
- 当前代码实现 HEAD：`adce710 test(backend): verify Kubernetes IPv6 egress deny`。
- `3227370`、`9011c2e` 与 `adce710` 尚未推送；本交接文档更新完成后仍需保留为
  独立本地 docs 提交。
- 前序实现均先做本地提交；用户已于 2026-07-27 明确授权完善本文后推送当前分支。
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

## 4. 2026-07-27 当前 HEAD 的实测门禁

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

- 341/341 passed。
- 0 failed，0 skipped。

### 真实依赖 Integration 测试

```powershell
wsl bash -lc `
  "cd /mnt/e/SourceCode/NoCTF/backend && NOCTF_KUBERNETES_INTEGRATION=true NOCTF_KOMPOSE_PATH=/tmp/noctf-runtime-reconcile-it-20260727/kompose-linux-amd64 /home/fs/.dotnet/dotnet tests/NoCTF.Tests/bin/Debug/net10.0/NoCTF.Tests.dll --treenode-filter '/*/*/*/*[Category=Integration]' --minimum-expected-tests 33"
```

- 本轮 WSL 回归为 33/33 passed、0 failed、0 skipped。
- 非 Integration 与 Integration 分组总计 373 passed、0 failed、0 skipped。
- `3227370` 后再次执行 Integration：32 passed、0 failed、1 skipped；唯一 skip 是未设置
  `NOCTF_KUBERNETES_INTEGRATION` 的 Provider dataplane 用例。本阶段另以完整部署清单完成
  独立 Kubernetes smoke，不把该 skip 伪装为 passed。
- Docker Desktop Engine `28.5.1`、Docker Compose `v2.40.3`。
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
- FastEndpoints 注册 122 endpoints。

### 其他

- `git diff --check` 在每个已提交纵切前均成功。
- `backend/Verify-Backend.ps1` 仍不存在，是明确交付项。

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

## 6. 当前最重要的剩余缺口

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

仍缺：

- 在 Linux KVM/Libvirt 节点用真实 OVA 验证 import、Guest Agent、routed network、
  URL 可达性和 stop/reset/expire；当前开发机只有 `/dev/kvm`，未安装
  `virsh`/`qemu-img`/`virt-install`。
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

1. 重新跑四模式 E2E。
2. 最后做 capability/DI 机械重构和 `Verify-Backend.ps1`。

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
4. 从 CTF、AWD、AWDP、KoH 四模式真实 E2E 开始；不要重做已完成的 deployment smoke、
   双栈 IPv6 deny、RuntimeKind dispatch、Docker/Kubernetes Compose handler、三 Provider
   crash orphan reconciliation 或 IPv4 Cilium egress 验证。
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
