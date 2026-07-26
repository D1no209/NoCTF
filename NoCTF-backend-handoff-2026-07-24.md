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
`DenyAll | InternetOnly` enforcement 已接通。下一主线是当前 Kubernetes 策略的真实集群
复测、Docker/Kubernetes 持久网络与 ingress proxy orphan reconciliation，以及部署 smoke。

## 2. Git 基线与工作树保护

- 仓库：`E:\SourceCode\NoCTF`
- 代码基线分支：`main`
- 代码基线 HEAD：`2c6b4ce fix(backend): reject null runtime URL bindings`
- 本交接分支：`codex/backend-target-architecture-handoff`
- 当前代码实现 HEAD：`4dfbf90 feat(backend): enforce runtime egress policies`。
- 本轮实现只做本地提交，尚未获准推送。
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
11. 当前威胁模型信任办赛管理员、管理员维护的题目配置、Runner Pool 配置和平台托管镜像，
    不防管理员内鬼；选手、题目业务容器及其网络输入仍按不可信处理。
12. `EgressPolicy` 只属于 Container/Compose，OVA 首版不增加该字段。Docker 首版只支持
    `DenyAll`，`InternetOnly` 直接拒绝；Kubernetes 支持两者。
13. Docker 的 `internal` network 不提供可用的直接 published port。公开 Runtime 使用
    用户确认的 1A：平台托管可信 HAProxy ingress，同时连接题目内部 network 与
    `noctf-network`，只转发已声明的 TCP URL Binding；题目容器不连接平台网络。
14. Kubernetes `InternetOnly` 只放行公网 IPv4；IPv6 不放行。特殊/私有地址使用内建
    deny ranges，并要求 Runner Pool 额外声明非空 `ProtectedCidrs`。

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

- 334/334 passed。
- 0 failed，0 skipped。

### 真实依赖 Integration 测试

```powershell
wsl -d Ubuntu-22.04 -- bash -lc `
  "cd /mnt/e/SourceCode/NoCTF/backend && /home/fs/.dotnet/dotnet test tests/NoCTF.Tests/NoCTF.Tests.csproj --no-restore -- --treenode-filter '/*/*/*/*[Category=Integration]' --minimum-expected-tests 1"
```

- 本轮 WSL 回归为 32 passed、0 failed、1 skipped；唯一 skip 是未设置
  `NOCTF_KUBERNETES_INTEGRATION` 的真实 Kubernetes Compose 测试。
- 分组总计 367 total、366 passed、0 failed、1 skipped。
- 本轮修改前同一 HEAD 基线已用临时 k3d 完成 29/29 passed，0 failed，0 skipped；
  OVA 修改未触碰 Kubernetes Provider。
- Docker Desktop Engine `28.5.1`、Docker Compose `v2.40.3`。
- Kubernetes 使用临时 k3d `v5.9.0` / k3s `v1.35.5+k3s1`，其内建
  NetworkPolicy controller 已启用，kubelet 以 `--pod-max-pids=512` 启动并与测试
  Runner Pool 配置一致。
- 新增真实 Docker Compose lifecycle，覆盖 dynamic port、Compose DNS、exec、
  container/network/workdir cleanup。
- 新增真实 Kubernetes Compose lifecycle，覆盖固定 Kompose、短名 DNS、
  未 Ready Pod DNS、同 Runtime 互通、跨 Runtime 拒绝、NodePort 与精确 cleanup。
- 测试发现并修复 Docker Compose `ps --format json` 在当前 CLI 返回 JSON Lines、
  旧适配器只接受 JSON array 的兼容问题。
- 新增 OVA contract 测试，覆盖 SHA-256、tar traversal、OVF 多 VM/资源预算、
  routed subnet、Guest Agent address、stable identity、URL expansion 与幂等 cleanup。
- 新增真实 PostgreSQL CTF PerTeam Runtime Flag 生命周期测试，覆盖 Start 原子生成、
  Worker dispatch 环境覆盖、批量生成、Static 排除和 Reset 固定 Flag 复用。
- 新增真实 Docker Container/Compose ingress proxy 测试，证明题目容器不连接平台网络、
  proxy 双网络、公开端口可访问、同 Runtime DNS/互通、幂等 replay 与精确 cleanup。
- `deploy/docker-compose.yml` 已通过 `docker compose config --quiet` 静态解析。
- 当前 EgressPolicy 的 Kubernetes manifest/unit 测试已通过；本轮没有可用 k3d/k3s
  context，因此新的 `InternetOnly` ipBlock/except 尚未在真实 CNI 上复测。

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
- 公开 URL 由平台动态创建 NodePort；status 读取实际 NodePort 与完整内部 FQDN。
- Up 等待 Deployment 可用，Down 精确匹配 managed+RuntimeInstanceId+Generation；
  部分创建失败执行确定性清理，清理失败保留容量供 durable retry。
- 默认 Kubernetes 部署改为共享 `runtime` Namespace 的 namespace-scoped RBAC，
  Runner 镜像包含固定 Kompose。

真实 k3s+CNI 集成已证明：

- 固定 Kompose 与 Kubernetes API 可完整执行；
- Compose 短名与 FQDN 可解析，未 Ready endpoint 也会发布；
- 同 Runtime 互通，其他 Runtime 无法访问非公开服务；
- 平台动态 NodePort 可访问；
- managed+RuntimeInstanceId+Generation 资源删除收敛。

生产 Pool 仍须由运维核对 kubelet 实际 `PodPidsLimit` 与 Runner 配置相等；应用配置不能
替代 kubelet 配置。

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
- Kubernetes Pool 启动时强制非空、合法 IPv4 `ProtectedCidrs`；Docker/Libvirt Pool
  不被无关 Kubernetes 配置阻塞。
- `deploy/docker-compose.yml` 将默认平台网络命名为 `noctf-network`，并加入新的
  Runtime Docker/proxy/Runner 配置；`deploy/k8s/configmap.yaml` 加入
  `ProtectedCidrs` 示例。

当前实际部署状态：

- 本轮只修改、验证并本地提交了代码和部署清单，没有重启或替换用户正在运行的服务。
- 2026-07-27 检查时，本机仍运行已有 `deploy-*` 容器，实际网络仍是
  `deploy_default`；`noctf-network` 尚不存在。新的 `deploy/docker-compose.yml` 只完成
  `config --quiet` 验证，尚未 `up` 应用。
- 本轮没有连接真实 Kubernetes cluster；`deploy/k8s` 变更尚未 apply。旧基线曾在临时
  k3d/k3s+CNI 上通过 DNS/NetworkPolicy 测试，但本轮新增 `InternetOnly` 规则只经过
  manifest/unit 测试。
- 没有任何生产环境部署，也没有推送远程分支。

明确尚未实现：

- Docker `InternetOnly`；没有 `DOCKER-USER` 宿主防火墙方案和 Egress Gateway。
- OVA/Libvirt EgressPolicy。
- IPv6 公网 egress、域名/FQDN allowlist、目的端口 allowlist。
- CNI NetworkPolicy 能力自动探测；`NetworkPolicyRequired=true` 仍是运维声明。
- Pod/Service/node/management CIDR 自动发现；Pool 运维必须维护 `ProtectedCidrs`。
- 单 Container proxy 镜像的自动 registry 拉取/鉴权；节点须预拉取受信任镜像。Compose
  路径由 Compose pull policy 处理。
- 持久 Container sandbox、ingress proxy 与 Compose appliance 在 Runner 进程崩溃且
  receipt 尚未持久化时的完整 orphan reconciliation。普通 Stop/Reset/失败回滚已完成，
  但不能把它等同于 crash orphan audit。
- Docker TargetPort ACL 与 callback-only gateway；它们仍是后期加固，不阻塞当前迁移。

不得把可信管理员假设、JWT、label、DNS 名称或独立 network 单独描述成完整安全边界。

### 6.5 Runner capacity、reconciliation 与 orphan cleanup 需要最终闭环审计

基础实现和测试已经存在，但在 Compose/OVA 接入后必须重新证明：

- claim/release 幂等；
- replacement 只占一个槽；
- Runner 失联与 Redis TTL；
- receipt 已存在的 Failed 仍由原节点清理；
- Docker/Kubernetes reaper 只按 managed + RuntimeInstanceId + Generation 精确匹配；
- Compose appliance/OVA 多资源 cleanup 不使用宽泛 Competition/Team 标签。
- 新增的 Docker ingress proxy、持久 Container sandbox NetworkPolicy/network 与
  Compose proxy 必须纳入 crash orphan audit；持久资源不能使用从 Provider create 时刻
  开始的业务 TTL 提前回收。

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

1. 在临时 k3d/k3s+CNI 上复测当前 Kubernetes `DenyAll`、`InternetOnly`、
   `ProtectedCidrs`、IPv6 deny、跨 Runtime deny 与 cleanup。
2. 完成 Docker/Kubernetes 持久 sandbox、ingress proxy、Compose appliance 的 crash
   orphan reconciliation，并重新审计 capacity claim/release。
3. 使用新清单执行受控 Docker Compose/Kubernetes deployment smoke；不得自行重启用户
   当前运行的本地栈。
4. 重新跑四模式 E2E。
5. 最后做 capability/DI 机械重构和 `Verify-Backend.ps1`。

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
4. 从 OVA durable lifecycle 开始；不要重做已完成的 RuntimeKind dispatch、
   Docker/Kubernetes Compose handler 或 Compose Provider 集成。
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
