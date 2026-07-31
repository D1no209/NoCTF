# NoCTF 后端目标架构交接

> 创建于 2026-07-24，最后核验于 2026-07-31。文件名保留原日期，本文内容以最后核验日期为准。

## 1. 下一会话目标

2026-07-30 已切换到协作者更新后的 `main`（基线 `a186d5f`），当前工作不是单纯交接
复核，而是完成协作者引入的后端收尾：

- Docker Container/Compose 公开服务直接请求随机宿主端口，删除旧 HAProxy 假设；
- 完成 Challenge.DefinitionJson 与 CompetitionChallenge.RulesJson 的严格所有权边界；
- 对齐 `D1no209/NoCTF-Challenge-Template` 的 GitOps Manifest 与后端 API；
- 修正 GitOps、部署和本交接文档；
- 重新执行真实依赖、OpenAPI、EF 与四模式边界测试；
- 后端稳定并本地提交后，再评估 Frontend 整合。Frontend 分支当前不得提前合并。

2026-07-30 已验证 GitOps 模板 `2571893` 可构建，`validate` 与 `self-test` 均通过。用户经
`$grill-me` 明确选择方案 A：使用普通 Organizer Bot、普通长生命周期 Access JWT、现有
细粒度管理 API 和稳定 UUID，不引入 Repository 专用 JWT 或原子 bundle API。后端现已
补齐 Bot identity/token、显式 ID、`includeDeleted`、模板与子资源恢复，并以真实
PostgreSQL 集成测试固定该契约。`docs/challenge-repository-gitops.md` 已改为方案 A。

### 当前完成度

- 6.21 CompetitionChallenge 稳定 ID create/update、`includeDeleted` inventory、软删除与精确
  restore 的 Frontend 管理纵切：代码和本地验证范围 100%。
- 6.19 Owner/Manager 角色资格、降级阻塞、restore 重验及 restore/hard-delete 串行化的
  代码和本地验证范围：100%；当前明确的 Frontend 前置后端阻断项已闭合。
- NoCTF 整体交付估算：约 92%；主要余项是剩余 Frontend 对新 OpenAPI/GitOps 管理能力的整合、
  更新后的本地 deploy 镜像重建验收，以及正式环境部署/运维验收。
- “后端 100%”不表示已经生产部署，也不表示 Kubernetes/Libvirt 所有可选基础设施已在
  当前机器再次实测；第 6 节明确区分代码、测试、本地部署和生产部署状态。

## 2. Git 基线与工作树保护

- 仓库：`E:\SourceCode\NoCTF`
- 2026-07-30 代码基线分支：`main`
- 2026-07-30 代码基线 HEAD：`a186d5f`，与 `origin/main` 一致。
- 当前本地工作分支：`codex/backend-gitops-completion`
- 本轮仍遵循“先本地提交，只有用户明确要求后才推送”；当前不得推送远端。
- 下方关于旧 `codex/backend-target-architecture-handoff` 分支的 ahead/behind 和提交
  序列是历史记录，不再代表当前 Git 状态。
- 当前分支未设置 upstream；本轮只创建本地提交，不推送、不创建 PR。
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
13. 2026-07-30 用户确认协作者的新实现为准：Docker Container/Compose 公开服务由题目
    容器直接发布声明的 TCP 端口，宿主端口请求 `0` 并由 Docker 随机分配；Runner 根据
    实际映射展开 URL。不得恢复 HAProxy/ingress proxy。Runtime 仍使用独立普通
    user-defined bridge network，不把题目容器接入 `noctf-network`。
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
22. GitOps 采用方案 A：平台 Bot 是普通 `UserKind.Bot` Organizer，令牌是普通 Access
    JWT；同步逐个调用既有细粒度管理 API。Manifest UUID 是稳定资源身份，删除后可通过
    `includeDeleted` 找回并恢复。不增加 Repository 专用身份、专用 JWT 或原子 bundle API。

## 4. 当前门禁状态与 2026-07-28 历史记录

> 本节后续带旧提交号的明细是历史记录。以下列表是
> `codex/backend-gitops-completion` 当前工作树在本地提交前的最新结果。

- solution build：0 warning、0 error。
- 非 Integration：371 passed、0 failed、0 skipped。
- Integration：51 passed、0 failed、2 skipped；Kubernetes dataplane 与 Libvirt fixture
  均为显式 opt-in，未把 skip 伪装为 passed。
- EF pending model check：通过；两项新增 migration 均由 `dotnet ef` 生成：
  `AddPlatformBotUserKind`、`AddAwdpTargetRevisionFence`。
- OpenAPI artifact/export/route drift：通过；共 131 条 route（含 health），130 条公开
  文档 route，85 条 Admin route。
- GitOps 模板 `2571893`：build、validate、self-test 通过。
- GitOps 真实 PostgreSQL persistence contract：通过。
- Docker 全边界 smoke：CTF、AWD、AWDP、KoH 各 1/1 passed。AWD/AWDP checker fixture
  的 shell CRLF 问题已用根级 `.gitattributes` 固定为 LF，并经两个模式实际验证。
- Docker Desktop Engine `28.5.1`、Compose `v2.40.3`。E2E 会动态读取 Docker socket
  GID；清理按精确 Compose project label 覆盖独立 Runtime 网络，不再遗留题目容器。
- `deploy-*` 六服务目前均运行在 `noctf-network`，API 与 Runner healthy；它们是此前
  本地部署，尚未用本轮分支镜像重建，因此不能作为本轮 GitOps/Bot API 的部署验收。

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

- 当前统一入口 `dotnet run --file backend/tests/e2e.cs -- --mode ctf --suite smoke` 使用隔离 Compose project、动态 API 端口和唯一镜像/
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

- 当前统一入口 `dotnet run --file backend/tests/e2e.cs -- --mode awd --suite smoke` 使用独立 Compose project、动态 API 端口、唯一网络与
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
- 本段旧 HAProxy ingress 方案已被 2026-07-30 用户确认的新实现取代。Docker
  Container/Compose 直接把声明的 TCP 端口发布为 Docker 随机宿主端口，Runner 从实际
 映射展开 URL；不得恢复平台 HAProxy。题目 Runtime 仍使用独立 user-defined bridge，
  不连接 `noctf-network`。
- Kubernetes Container/Compose 每 Runtime 创建 NetworkPolicy。`DenyAll` 只允许同
  Runtime、DNS 和平台声明入站；`InternetOnly` 额外允许 `0.0.0.0/0`，通过 `except`
  排除内建特殊/私有 IPv4 与 Pool `ProtectedCidrs`，不生成 IPv6 allow。
- Kubernetes Pool 启动时强制非空、合法 IPv4 `ProtectedCidrs` 与精确
  `ClusterDnsServiceAddress`；Runner 校验 Cilium `enable-policy=always`、
  deployment-owned Namespace baseline default-deny 与实际 kube-dns ClusterIP。
  Docker/Libvirt Pool 不被无关 Kubernetes 配置阻塞。
- `deploy/docker-compose.yml` 将默认平台网络命名为 `noctf-network`，并加入新的
  Runtime Docker/Runner 配置；`deploy/k8s/configmap.yaml` 加入
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

- 标准入口是 `dotnet run --file backend/tests/e2e.cs -- --mode ctf --suite smoke`；需要保留
  失败环境时追加 `--keep-environment`，排查完必须按输出的唯一 project 精确清理。
- 测试必须继续跨 HTTP、独立 API/Worker/Runner、真实 PostgreSQL/Redis/MinIO 与 Docker；
  Runtime fixture 必须保持非 root、只读 rootfs 和平台 ingress proxy 路径。
- CTF 已验证附件、PerTeam Runtime Flag、first blood、Hint 扣分、leaderboard projection、
  rejudge 与 Runtime cleanup。后续模式可以复用脚本的隔离/清理方式，但不要把四模式压成
  同一个巨型测试或共享可变数据库。

### 6.8 AWD 完整边界

AWD 完整边界已由 `c46917d` 独立完成。不要退回固定 Flag、预建 Runtime 或进程内 Checker：

- 标准入口是 `dotnet run --file backend/tests/e2e.cs -- --mode awd --suite smoke`；需要保留失败环境时追加 `--keep-environment`，
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

- 标准入口是 `dotnet run --file backend/tests/e2e.cs -- --mode awdp --suite smoke`；需要保留
  失败环境时追加 `--keep-environment`，排查后必须按输出的唯一 project 精确清理。
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

- 标准入口是 `dotnet run --file backend/tests/e2e.cs -- --mode koh --suite smoke`；需要保留
  失败环境时追加 `--keep-environment`，排查后必须按输出的唯一 project 精确清理。
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
- 最新最终门禁见 6.18：Release solution build 0 warning/0 error；380/380
  non-Integration passed；真实依赖 Integration 56 passed、0 failed、2 个显式 opt-in
  skipped；EF 无 pending model；全 solution analyzer、限定文件 whitespace、OpenAPI
  artifact、生成 TypeScript client 与 `git diff --check` 均无漂移。
- 一次性 Libvirt 容器、定制工具镜像、fixture 目录均已精确删除；WSL system Libvirt
  最终无 domain、只有原有 inactive `default` network。当前本地 `deploy` 六服务继续
  运行，API 与 Runner healthy，未被本轮测试重启或替换。

### 6.17 `origin/main` 后端整合（2026-07-31，按用户要求暂停）

本节是下一位协作者必须优先阅读的最新状态。用户要求先整合协作者更新后的 `main`，
完成后端第一阶段并本地提交；随后在全套测试运行中途明确要求暂停，写清 handoff 后交接。
因此不得把本节记录为“完整门禁已通过”，也不得自行继续部署、GitOps 实机演练或前端功能
整合。

#### 已整合的来源和取舍

- 协作者的 `origin/main` 为 `1687acbd6c81944cbad2672698b3b3c1002c98da`（
  `feat: add bot identities and restore APIs`）；本地 GitOps 后端基线为 `0a664a8`，后续
  修正提交为 `94fbcfb`。两边从 `a186d5f` 分叉，自动合并预演有 39 个冲突，因此在隔离
  worktree 的 `codex/backend-main-integration` 分支执行显式合并和逐项复核，没有用覆盖式
  reset。
- 采用协作者 `main` 的稳定资源 ID、`includeDeleted`、精确 restore、Bot identity 和统一
  `ManagePlatform` / `PlatformAdministrationStore` 实现；删除本地分支上功能重复的
  `Administration/Bots` 应用与基础设施实现，避免形成两套 Bot 业务入口。
- Bot 被收紧为 GitOps 专用 Organizer 身份：用户名只允许 3～64 个 ASCII 字母、数字、
  `_`、`-`；只允许 `Organizer`；Access JWT lifetime 为 60～31,536,000 秒；普通角色更新
  也不能把 Bot 改成其他角色。Bot 仍无密码登录和 refresh token。
- Challenge template create/update 的 `Mode`、`Visibility`，以及 Bot create 的 `Role`
  在 API 边界接受文档所用的 enum 名称；为兼容已生成或已有调用方仍接受正确的旧数字值。
  OpenAPI 明确发布字符串 enum。对应 protocol tests 已加入，GitOps 模板无需把
  `Ctf`、`Private`、`Organizer` 改成数字。
- 保留用户已确认的 AWDP revision fence：每次 Fix 创建全新的即时目标，并固化
  Competition configuration、CompetitionChallenge、Challenge definition 三个 revision；
  任一 revision 变化都把在途结果标记为 `Superseded` / `PlatformFailed`，调度目标清理，
  不产生 scoring event，也不自动重跑。
- 修复合并后真实 PostgreSQL 测试暴露的 EF 翻译问题：
  `ChallengeBankStore` 在 projection 前过滤，`ChallengeManagementStore` 在 projection
  前排序。GitOps persistence contract 已适配协作者的新 command/result 结构。
- migration 全程由 EF CLI 管理。最终基线为
  `20260729165305_InitialBaseline`，AWDP 增量为
  `20260730162036_AddAwdpTargetRevisionFence`；重复的 Bot migration 和旧 AWDP migration
  已通过 EF CLI 移除后重新生成，没有手改 migration 或 snapshot。
- CI 的 SDK 与 `global.json` 对齐为 .NET SDK `10.0.300`；OpenAPI 启动探针从不存在的
  `/api/health` 修正为 `/health`，循环结束后增加一次 fail-fast health 请求，避免 API
  未启动时继续生成客户端。

#### 已完成验证

- 最终源码的 Release solution build：0 warning、0 error。
- 合并后的 non-Integration 在最后一次协议小修前为 377/377 passed；新增 Bot protocol
  test 已在随后的 Release build 中成功编译。下一位必须重跑完整 non-Integration，不能
  沿用 377 作为最终数量。
- 定向真实依赖测试已通过：Bot authentication 1/1、GitOps persistence contract 1/1、
  AWDP Fix result persistence 2/2。
- EF `has-pending-model-changes` 在最终 migration 生成后通过；最后一次 Bot JSON converter
  修改不涉及数据模型。
- 最终 OpenAPI 两份后端 artifact 已重新导出且 SHA-256 相同；生成的 TypeScript client
  已刷新。`bun run build` 通过。
- 一次把测试放在普通 SDK 容器内的 Integration 尝试得到 48 passed、4 failed、2 skipped；
  4 个失败全部是测试宿主不带 Docker CLI或容器内 `localhost` 无法访问宿主随机端口，
  不能作为代码失败或最终门禁结果。随后已改在 WSL 原生文件系统和其完整 Docker/.NET
  工具链运行。

#### 用户要求暂停时的状态

- WSL 原生目录中的完整 `dotnet test backend/NoCTF.slnx` 已运行约三分钟，尚未产生失败
  输出或最终摘要时，用户要求立即暂停。测试进程已终止，Testcontainers/Ryuk 临时容器已
  退出；不得宣称完整测试通过。下一位应从干净的 WSL 原生副本重新跑完门禁。
- 没有使用本轮整合源码重建 `deploy-*`，没有删除或修改现有部署数据，没有执行 GitOps
  apply/reapply/restore 实机演练。现有六服务仍是整合前镜像。
- Frontend 只为 OpenAPI drift 做了客户端再生成与编译验证；Bot、稳定 ID、restore 和
  GitOps 管理 UI 尚未做功能整合，协作者 Frontend 分支也尚未合并。
- 没有推送、没有 PR、没有生产部署。本节对应工作只允许形成一个本地提交。
- 主工作区原有的以下用户文件必须继续保护，不能混入后端整合提交：
  `WolverineTransactionalMessageOutbox.cs`、`backend/src/NoCTF.Runner/Properties/`、
  `deploy/docker-compose.local-ports.yml`、
  `frontend/src/composables/useInstanceOperationState.ts`、
  `frontend/src/lib/queryClient.ts`、
  `frontend/tests/useInstanceOperationState.test.ts` 和 `scripts/`。

#### 下一位协作者的起点

1. 先确认当前本地整合提交和 `git status`，保护上列用户文件；不要重复合并 `main`。
2. 在 WSL 原生文件系统重跑 Release build、完整 non-Integration、完整 Integration、
   analyzer、EF pending model、OpenAPI/client drift 和 `git diff --check`。
3. 只有上述门禁完整通过后，才可在用户再次授权时重建本地 deploy 并进行真实 Bot +
   GitOps apply/reapply/restore；当前 handoff 不授权自动部署。
4. 前端功能整合仍是独立后续阶段。推送继续等待用户明确指令。

### 6.18 所有权转移与并发收口（2026-07-31）

本节取代 6.17 的“暂停中”状态；6.17 仅保留为历史过程，不得据此重复旧工作或否定本节
已经完成的验证。

#### 远端基线与本地提交

- 通过 GitHub API 再次核验远端 `main` 仍为
  `1687acbd6c81944cbad2672698b3b3c1002c98da`；本地 `origin/main` 同一 SHA，且是当前
  `codex/backend-gitops-completion` 的祖先。`ada6a7f` 已完成该来源的显式合并，因此本轮
  没有新的冲突，也没有制造空 merge commit。
- 6.17 之后已形成以下本地提交：
  - `06fb745 fix(ci): stabilize backend release gates`
  - `c0857fe fix(auth): enforce access token revocation`
  - `49040fe feat(admin): manage platform bots`
  - `0a98a02 feat(admin): reconcile challenge lifecycle`
- 外部模板仓库 `D1no209/NoCTF-Challenge-Template` 的本地分支
  `codex/gitops-apply-idempotency` 已提交
  `cd0086e fix(gitops): make challenge apply idempotent`。两个仓库均未 push。

#### 最后一个后端契约缺口及修复

- Competition 与 Challenge 的 Owner transfer endpoint/document 均承诺把旧 Owner 保留为
  Manager；原 store 只替换 `OwnerId` 并移除新 Owner，导致 GitOps Bot 转交所有权后失去
  后续写权限。本轮在同一次写入中把旧 Owner 加入去重、有序的 `ManagerIds`，移除新
  Owner；Competition 还会从 `JudgeIds`、`ObserverIds` 移除新 Owner，满足 PostgreSQL
  互斥约束。
- Competition transfer 复用既有 `CompetitionWriteLock` 和事务，避免并发 transfer 或
  permission update 覆盖上一任 Owner。Challenge 新增按模板 UUID 的 PostgreSQL advisory
  transaction lock，并由 template update、permissions、owner transfer、delete/restore、
  attachment add/update/delete/restore 共同使用，保证 `Revision` 不丢增量。
- CompetitionChallenge create 按固定顺序先取得 Competition lock、再取得 Challenge
  lock；restore 也在恢复活动引用前取得 Challenge lock 并重查活动模板。Challenge
  soft-delete 在同一锁和事务内检查活动引用，从而不能与引用创建同时成功并留下
  “活动比赛题目引用已删除模板”的状态。当前锁序未形成反向环。
- Challenge transfer 在写入前验证新 Owner 存在，避免外键异常；三个 Challenge 写接口都
  在持锁事务内完成返回 View 投影，再提交，避免已成功写入后因紧随其后的修改/删除返回
  错误状态。
- 没有修改 endpoint、OpenAPI 或 migration；无效目标 Owner 仍使用现有
  `not found or revision conflict` 协议语义，未擅自扩展外部契约。

#### 最终验证快照

- 在 WSL 原生干净副本、.NET SDK 10.0.301、Docker 28.5.1 下完成：
  - Release `NoCTF.slnx` build：0 warning、0 error；
  - non-Integration：380/380 passed；
  - Integration：58 total，56 passed、0 failed、2 skipped；两个 skip 分别是未启用的真实
    Kubernetes dataplane 和未配置 fixture 的真实 Libvirt opt-in；
  - 全 solution analyzer `--severity warn`、本轮文件 whitespace
    `--verify-no-changes`：passed；
  - EF `has-pending-model-changes`：明确无模型漂移。
- 新的真实 PostgreSQL/TUnit 回归覆盖：
  - Competition 双并发 Owner transfer 不丢任何上一任 Owner；
  - Challenge 同 revision 双并发 transfer 只有一个成功；
  - template soft-delete 与 CompetitionChallenge create 只能得到合法串行结果；
  - attachment revision 更新与 Owner transfer 不会丢 revision 增量；
  - 新 Owner 不存在时不修改持久化状态。
- OpenAPI 导出仍注册 132 endpoints；两份 artifact 及仓库当前 artifact 的 SHA-256 均为
  `8dba9f2ee6081b5408733dd039abbf55b49418f5967851a939090e3d6f13f2cd`。
  `@hey-api/openapi-ts 0.97.3` 从该 artifact 重新生成并执行项目 patch 后，生成目录零 diff。
- Windows 直接运行 backend verification 会选中本机 .NET SDK 10.0.201，与仓库
  `global.json` 的 10.0.300 feature band 不兼容；这是工具链问题。上述最终结论来自
  工具链匹配的 WSL 干净副本，不使用失败的 Windows 结果。

#### 本地部署、GitOps 与前端状态

- 本轮已使用整合后的源码重建本地六服务并确认健康；已用真实 Platform Bot token 完成
  GitOps template apply、重复 apply、软删除和按稳定 UUID restore。该演练仅是本地部署，
  不代表生产环境验收。
- Frontend 已完成 Platform Bot 创建、一次性 token、TokenVersion invalidate，以及
  Challenge Bank 的 `includeDeleted`、稳定 UUID、活动引用数、受限删除和精确恢复；全部
  调用生成的强类型 SDK，没有手写 URL。
- 进入 Frontend 前仍有一个明确的后端阻断项：Owner/Manager 必须是 Organizer 或
  Administrator；Organizer 降级为 User 时必须在事务内扫描所有未删除 Competition 与
  Challenge 的 Owner/Manager 关系，409 返回稳定的阻塞资源 ID 且不能递增
  `TokenVersion`；已删除资源允许降级，但 restore 必须重新验证角色并在不合格时返回
  409。Create、permissions、owner transfer 也必须在事务内重查目标用户角色。该切片需要
  具体的 result enum/response schema、真实 PostgreSQL 并发测试、OpenAPI 与生成客户端
  更新，不能由 UI 禁用选项代替。
- 完成上述后端阻断项后，下一个前端纵切是“GitOps 访问权限”：管理员从 Platform Bot
  中选择 Organizer Bot，把它加入已有 Challenge 的 `ManagerIds`。使用
  `adminPlatformListUsers`、`adminChallengeBankGetTemplate` 和
  `adminChallengeBankUpdatePermissions`；更新是全量替换，必须保留现有 Manager、排除
  Owner，并显式发送当前 `expectedRevision`。该管理员专用流程没有未决产品语义。
- 不复用旧 `AdminCollaboratorsWorkspace`；它依赖已废弃且后端不存在的手写
  `/collaborators` URL。Competition permission UI 也暂不做，因为当前读取响应没有返回
  manager/judge/observer 全量集合，不能安全执行全量替换。
- 没有 push、PR 或生产部署。主工作区原有
  `WolverineTransactionalMessageOutbox.cs`、Runner `Properties/`、本地端口 compose、
  三个 instance-operation/query 文件和 `scripts/` 继续视为用户工作，不得混入本轮提交。

### 6.19 Owner/Manager 角色资格与生命周期串行化（2026-07-31）

本节完成 6.18 明确记录的最后一个 Frontend 前置后端阻断项，并取代第 7 节原先要求再次
实现该切片的旧顺序。6.18 仍保留为所有权并发修复的历史记录。

#### 角色不变量与事务协议

- Domain 新增 `UserRolePolicy.CanManageResources()`；Competition 与 Challenge 的 Owner 和
  Manager 只允许 `Organizer` 或 `Administrator`。Judge/Observer 未被擅自扩展为同一
  规则，已验证普通 `User` 仍可作为 Competition Judge。
- 新增共享 `ResourceManagerRoleGuard`：使用 user-scoped PostgreSQL advisory transaction
  lock，批量 UUID 先去重、排序再加锁，并在锁后重查用户存在性与角色。赋权路径固定为
  Resource lock → 排序后的 User locks；平台降级只取得目标 User lock 后做无锁 MVCC
  扫描，不存在 User → Resource 的反向锁环。
- Platform 角色更新改为单事务 typed result。`Organizer`/`Administrator` 降级为 `User`
  时扫描所有未删除 Competition/Challenge 的 Owner/Manager 关系；阻塞时返回排序后的
  `CompetitionIds` 与 `ChallengeIds`，并保持 Role、TokenVersion、UpdatedAt 不变。只有
  已删除资源时允许降级。
- Competition 与 Challenge 的 create、完整 Manager set replacement、Owner transfer 和
  restore 都在事务内锁定并重验最终 Owner + Manager 集合。缺失用户与角色不合格使用
  不同 enum 状态和稳定 User ID 列表；失败时不写入、不递增 Revision，也不更新时间。
- Competition restore 只允许 Owner 或平台 Administrator；hard-delete 同样收紧为该权限，
  并与 restore 共用 Competition advisory lock。真实并发回归证明 restore 已成功后，
  hard-delete 不会再把刚恢复的资源永久删除。Challenge restore 继续允许 Owner、Manager
  或平台 Administrator，OpenAPI 文案已与实际权限一致。

#### 强类型 HTTP/OpenAPI 契约

- Platform role downgrade 的 409 body 为
  `UpdatePlatformUserRoleConflictResponse`，code 为
  `ActiveOwnerOrManagerAssignments`。
- Competition create/permissions/transfer/restore 的 409 body 统一为
  `CompetitionResourceManagerConflictResponse`，code 为
  `RolesOverlap | OwnerIncluded | UserNotFound | RoleNotEligible`。
- Challenge create/permissions/transfer/restore 的 409 body 统一为
  `ChallengeTemplateConflictResponse`，code 为
  `ResourceIdConflict | RevisionConflict | OwnerIncludedInManagerSet | UserNotFound |
  RoleNotEligible`。
- Application 与 Store 路径使用 typed state/result；新路径没有用字符串承载有界业务状态。
  Endpoint 的未知 enum 分支 fail-fast，不会静默误映射。OpenAPI architecture test 允许
  既有 `application/problem+json` 冲突和新的具体 `application/json` typed conflict。
- OpenAPI 导出仍注册 132 endpoints；两份 artifact 完全一致，SHA-256 均为
  `AE8990A76B5A7810761FAD8A399274739C18956D71ECA7B90F34537A1E365E76`。
  `@hey-api/openapi-ts 0.97.3` 已从该 artifact 重新生成并执行项目 patch，未手改 SDK。
- 没有 EF 模型或 migration 变化。

#### 最终验证快照

- WSL 原生干净副本、.NET SDK 10.0.301、真实 Docker/Testcontainers：
  - Release `NoCTF.slnx` build：0 warning、0 error；
  - non-Integration：383/383 passed；
  - Integration：62 total，60 passed、0 failed、2 skipped；两个 skip 仍是未启用的真实
    Kubernetes dataplane 与未配置 fixture 的真实 Libvirt opt-in；
  - 新增 restore/hard-delete 并发项定向执行 1/1 passed；
  - 全 solution analyzer `--severity warn` 与全部本轮 C# 文件 whitespace
    `--verify-no-changes`：passed；
  - EF `has-pending-model-changes`：明确无模型漂移；
  - `git diff --check`：passed。
- 新增真实 PostgreSQL 覆盖包括：
  - 活跃 Owner/Manager 阻止降级，阻塞 ID 稳定排序且 TokenVersion 不变；
  - 仅软删除资源时允许降级，restore 在角色不合格时保持删除状态，升级后可恢复；
  - create/permissions/transfer 拒绝缺失或普通 User 的 Owner/Manager，失败状态原子不变；
  - 降级先持 User lock 与赋权先持 User lock 的两种执行顺序都只能得到合法串行结果；
  - Competition restore 与 hard-delete 不能同时成功并留下“恢复成功后资源消失”的状态。
- 独立只读审查确认 typed mapping、授权可见性、锁序、事务边界和测试设计无剩余阻塞；
  Challenge 共享同一 guard 的并发闭环由代表性的 Competition 双向竞态测试覆盖。

#### Git 与下一阶段

- 提交前通过 GitHub compare 再次核验远端 `main` 仍为
  `1687acbd6c81944cbad2672698b3b3c1002c98da`，与 6.18 已合并基线 `identical`；当前
  HEAD 已包含该提交，因此没有新冲突或需要制造空 merge commit。
- 本节对应一个独立本地提交，提交说明为
  `fix(auth): enforce resource manager roles`；不 push、不创建 PR。
- 下一阶段进入 Frontend：
  1. `/admin/users` 对 typed downgrade 409 显示阻塞 Competition/Challenge ID，并在失败后
     刷新用户数据；
  2. Challenge Bank 的管理员 GitOps access UI 只从 Platform Organizer Bot 选择 Manager，
     保留现有 ManagerIds、排除 Owner、显式发送当前 `expectedRevision`，409 后重新拉取；
  3. 所有请求只使用生成 SDK，不写死 URL，不恢复旧 `/collaborators`。
- Competition permission UI 仍等待读取契约返回完整 manager/judge/observer 数组；在此之前
  不得用空数组做全量替换。
- 保护文件列表与 6.18 相同；本地提交不得包含用户的 Wolverine、Runner Properties、
  本地端口 compose、instance-operation/query 文件或 `scripts/`。

### 6.20 Frontend typed 409 与 Challenge GitOps Manager 授权（2026-07-31）

本节完成 6.19 和第 7 节前两项 Frontend 纵切；后续不得重复恢复旧 `/collaborators`
协议，也不得把 Challenge permission 的全量替换误用于 Competition permission。

#### Platform 角色降级冲突

- `/admin/users` 直接读取生成契约的
  `UpdatePlatformUserRoleConflictResponse`。只有 HTTP 409 且 code 为
  `ActiveOwnerOrManagerAssignments` 时，才显示排序、去重后的 Competition/Challenge
  阻塞 ID；未知状态或 code 继续走通用错误。
- 角色更新失败后保留对话框并刷新 `adminUsers` 查询；打开对话框、重试和成功后都会清除
  旧 blocker，避免把上一次冲突误显示给其他用户。
- 没有根据资源 ID 拼接 URL，也没有解析 ProblemDetails 文本。

#### Challenge GitOps access

- Challenge Bank 活动模板只对平台 Administrator 显示“Grant GitOps access”入口。
  对话框使用生成的 `adminChallengeBankGetTemplate`、`adminPlatformListUsers` 和
  `adminChallengeBankUpdatePermissions`。
- 候选人严格限制为未分配的 Organizer Bot，并排除 Owner；提交 payload 保留、去重并排序
  全部现有 `ManagerIds`，加入所选 Bot，显式发送最新 `expectedRevision`。
- 生成响应缺失 `OwnerId`、`ManagerIds` 或 `Revision` 时 fail-closed，不会以空数组覆盖已有
  权限。typed 409 后同时重取模板和平台用户，所选 Bot 必须在最新候选集合中仍然有效才可
  再次提交。
- 没有手写 URL，没有复用已废弃的 `AdminCollaboratorsWorkspace` 或 `/collaborators`。

#### Frontend 验证与 Git 状态

- 提交前通过 GitHub compare 再次核验远端 `main` 仍为
  `1687acbd6c81944cbad2672698b3b3c1002c98da`，与本地已合并基线 `identical`；该提交是
  当前 HEAD 的祖先，因此没有新冲突或需要创建空 merge commit。
- `bun test`：30/30 passed。
- `bun run build`：`vue-tsc --noEmit` 与 Vite production build passed；仅保留依赖 PURE
  annotation 和既有大 chunk 警告。
- 除 `src/api/noctf.ts` 外，本轮目标文件 scoped ESLint passed；`noctf.ts` 的 11 项均位于
  HEAD 已有代理/type/正则/旧样式代码，本轮新增 typed helper 没有新增 lint 告警。全仓
  `bun run lint` 仍被既有数千项 baseline 阻塞，本轮没有借机格式化或重写无关文件。
- `git diff --check` passed；独立只读审查确认 Organizer Bot 筛选、Manager 保留、Owner
  排除、fail-closed、revision 与 409 refetch 无剩余阻塞。
- 本节作为独立 Frontend 本地提交；不 push、不创建 PR。保护文件列表继续沿用 6.18。
- 下一项进入 CompetitionChallenge 稳定 ID 的 create/update/delete/restore 管理流程；
  Competition permission UI 仍等待后端读取契约返回完整权限集合。

### 6.21 Frontend CompetitionChallenge 稳定身份与生命周期（2026-07-31，当前最新）

本节完成 6.20 和第 7 节原先排在首位的 CompetitionChallenge 管理纵切。旧
`templateId`/description/pointsConfig/hints/AWDP payload 与不存在的
`/container/restart` 均未恢复；这些概念分别属于 Challenge template、configuration、
hint 子资源或 Runtime lifecycle。

#### 强类型 API 与稳定身份

- list/get/create/update/delete/restore 全部直接使用生成 SDK 的
  `adminListCompetitionChallenges`、`adminGetCompetitionChallenge`、
  `adminCreateCompetitionChallenge`、`adminUpdateCompetitionChallenge`、
  `adminDeleteCompetitionChallenge` 与 `adminRestoreCompetitionChallenge`；本纵切没有
  手写 URL。
- create 可显式输入 8-4-4-4-12 十六进制稳定 GUID，拒绝 Empty Guid；留空时省略 `id`，
  由后端生成 UUID v7。只允许选择与 Competition mode 精确匹配的活动 Challenge template，
  当前候选失效时 fail-closed、重新选择有效候选。
- create 只发送 `id?`、`challengeId`、`baseScore`、`order`，新记录明确为 draft；发布只在
  后续 update 中发送，避免展示或提交后端不会接受的 create 字段。
- update 只发送 `baseScore`、`order`、`isPublished` 与响应中的最新
  `expectedRevision`；响应缺失 revision 或记录已软删除时禁用提交。

#### Inventory、删除与恢复

- inventory 始终显式发送 `includeDeleted`，active/deleted 查询使用不同 cache key；详情
  query 也按 Competition、稳定 CompetitionChallenge ID 和 `includeDeleted` 隔离，避免与
  旧 Operations DTO 缓存串线。
- 列表显示稳定 ID、模板 ID、基础分、顺序、发布状态、revision 和删除状态，可复制稳定
  ID；删除与恢复均要求确认。`204` 后 invalidate/refetch，以后端新 revision 和状态为准。
- create/update 的 409、edit 的 404，以及 delete/restore 的 404/409 都不会自动覆盖或猜测
  服务端状态；页面会保留当前操作上下文并重新拉取相应详情/清单。create 的 404 还会同时
  重取 Competition 与模板集合，处理页面打开后模板被删除或权限变化的竞态。
- 当前后端 OpenAPI 对本组 409 仍只暴露通用 error schema，delete/restore 也没有
  `expectedRevision`。前端因此只按 HTTP 状态刷新，不解析错误文本或发明业务 code；若要
  增加 typed conflict 或陈旧 delete/restore fencing，应作为独立后端协议纵切处理。

#### 验证、远端基线与 Git

- `bun test`：37/37 passed；CompetitionChallenge lifecycle 定向覆盖稳定/空 ID、数值
  边界、候选模板失效、显式最新 revision、删除态 fail-closed、活动顺序与中英文 key。
- 本轮目标文件 scoped ESLint passed；`src/api/noctf.ts` 继续保留 6.20 已记录的既有
  baseline，不借机格式化无关代理代码。
- `bun run build`：`vue-tsc --noEmit` 与 Vite production build passed；仅有依赖 PURE
  annotation 和既有大 chunk 警告。`git diff --check` passed。
- 独立只读审查确认最终实现无剩余提交阻断。
- 提交前再次通过 GitHub compare 核验远端 `main` 仍为
  `1687acbd6c81944cbad2672698b3b3c1002c98da`，与本地已合并基线 `identical`；没有新冲突，
  也不制造空 merge commit。
- 本节作为独立 Frontend 本地提交，提交说明为
  `feat(admin): manage competition challenge lifecycle`；不 push、不创建 PR，保护文件
  列表继续沿用 6.18。
- 下一安全纵切回到 Backend：补齐强类型 Admin Competition 读取响应中的完整
  Owner/Manager/Judge/Observer 权限集合，完成 OpenAPI/client 与后端验证后，再用生成 SDK
  实现 Frontend 全量替换权限 UI。不得恢复旧 `/collaborators`。

### 6.22 Competition 权限快照、候选目录与独立并发栅栏（2026-07-31）

本节按 `$grill-me` 明确选择的方案 A 完成 6.21 的 Backend 前置纵切。没有扩张匿名
Competition 响应，也没有恢复逐人 `/collaborators` 协议；Frontend 必须在本节完整契约和
生成 SDK 上继续。

#### 资源作用域读取与资格约束

- 新增 `GET /admin/competitions/{competitionId}/permissions`，只允许 Competition Owner 或
  平台 Administrator 读取完整 Owner、Manager、Judge、Observer 集合与
  `PermissionRevision`。Manager/Judge/Observer 即使能读取其他 Admin Competition 元数据，
  也不能通过该端点枚举完整权限名单。
- 新增 `GET /admin/competitions/{competitionId}/permission-candidates`，使用相同授权边界，
  仅返回 `Id`、`UserName`、`Kind`、`Role`、`EmailVerified`。Owner 从候选集中排除；
  Manager 候选为 Organizer/Administrator Human 或 Bot，Judge/Observer 候选必须完成邮箱
  验证。平台用户 Email、TokenVersion 等管理字段没有泄漏。
- `PUT .../permissions` 继续执行完整集合替换，但现在强制携带非负
  `expectedPermissionRevision`。只有 Owner 或数据库中当前仍为 Administrator 的用户可写；
  Manager 必须是 Organizer/Administrator，Judge/Observer 必须完成邮箱验证。缺失用户、
  角色不合格、邮箱未验证、Owner 混入、集合重叠与 revision 冲突均为 typed state/result，
  新增稳定 code `EmailNotVerified` 与 `RevisionConflict`。
- 权限写与 Owner transfer 共用 Competition advisory transaction lock。成功全量替换递增
  `PermissionRevision`；Owner transfer 同样递增，因此转让前取得的权限快照不能覆盖新
  Owner。该 revision 不复用 ConfigurationRevision，也没有扩张成通用 Competition
  revision。

#### EF、OpenAPI 与生成客户端

- EF Core 迁移 `20260731001648_AddCompetitionPermissionRevision` 及 ModelSnapshot 完全由
  `dotnet ef migrations add` 生成，没有手改 migration 或 snapshot。
- OpenAPI 当前注册 134 endpoints；两份 artifact 完全一致，SHA-256 均为
  `B38067B0A605E611751B940997C9C299954E300CA3CA77AC570462A5AFE4BD16`。
- `@hey-api/openapi-ts 0.97.3` 已从 artifact 重建客户端并执行项目 patch；生成结果二次运行
  无漂移。新增 generated 方法为 `adminGetCompetitionPermissions` 与
  `adminListCompetitionPermissionCandidates`，现有 `adminUpdateCompetitionPermissions`
  的 body 已强制包含 `expectedPermissionRevision`。没有手写 URL 或手改 SDK。
- Public Competition schema 的 architecture test 明确禁止出现 Manager/Judge/Observer
  数组或 PermissionRevision，避免匿名读取面被 Admin 权限功能污染。

#### 验证、远端基线与 Git

- WSL 原生干净副本、.NET SDK 10.0.301、真实 Docker/Testcontainers：
  - Release `NoCTF.slnx` build：0 warning、0 error；
  - non-Integration：391/391 passed；
  - Integration：67 total，65 passed、0 failed、2 skipped；两个 skip 仍是未启用的真实
    Kubernetes dataplane 与未配置 fixture 的真实 Libvirt opt-in；
  - 新增真实 PostgreSQL 覆盖 Owner/Admin 读取边界、最小候选投影、相同 revision 并发只
    允许一个写成功、未验证 Judge/Observer 原子拒绝，以及 Owner transfer 使旧写冲突；
  - 全 solution analyzer `--severity warn`、全部本轮 C# 文件 whitespace、EF pending
    model、OpenAPI artifact/client drift 与 `git diff --check` 均 passed；
  - 生成 SDK 后 Frontend `vue-tsc --noEmit` 与 Vite production build passed，仅保留既有
    依赖 PURE annotation 和大 chunk 警告。
- 通过 GitHub compare 再次核验远端 `main` 仍为
  `1687acbd6c81944cbad2672698b3b3c1002c98da`，与本地已合并基线 `identical`；当前 HEAD
  已包含该提交，因此没有新冲突或需要制造空 merge commit。
- 本节作为独立 Backend 本地提交；不 push、不创建 PR。保护文件继续沿用 6.18，尤其不得
  混入 Wolverine outbox、Runner Properties、本地端口 compose、三个
  instance-operation/query 文件或 `scripts/`。
- 下一纵切进入 Frontend：只使用本节生成 SDK 实现 Competition 权限完整集合 UI，409 后
  重取权限快照和候选目录并从最新 revision 重建 payload；随后删除旧
  `AdminCollaborators` route/view/workspace/API/query key 与 copy，不保留兼容层。

### 6.23 Frontend Competition 权限完整集合管理（2026-07-31）

本节完成 6.22 的 Frontend 纵切。权限 UI 只消费生成 SDK 的资源作用域快照和候选目录，
旧 `/collaborators` 协议、页面、路由、query key 与 copy 已全部删除，不保留兼容跳转。

#### 完整快照、候选资格与并发处理

- Competition detail 新增 `permissions` section；Competition inventory 的权限入口直接进入
  该 section。Owner 只读展示，Manager/Judge/Observer 使用完整集合增、移、删。
- 所有读写分别使用生成 SDK 的 `adminGetCompetitionPermissions`、
  `adminListCompetitionPermissionCandidates` 与 `adminUpdateCompetitionPermissions`，没有
  手写 URL。快照缺失 Competition/Owner/任一角色数组/非负整数 revision、角色集合重叠、
  Owner 混入或 UUID 无效时均 fail-closed；候选响应缺失完整 `items` 或必要元数据时同样
  fail-closed，不会以空数组替换服务端事实。
- Manager 候选只接受 Organizer/Administrator 的 Human 或 Bot；Judge/Observer 候选必须
  EmailVerified，Owner 始终排除。候选目录未返回某个已分配成员时仍按 ID 保留其当前角色，
  可以安全移除但不能臆测资格后移动。
- mutation 执行时从 QueryClient 重新取得最新完整快照，发送全部三组角色 ID 和该快照的
  `expectedPermissionRevision`；不做 optimistic overwrite，也不从过期组件状态构造 payload。
- 任意 409（包括未知 typed code）都会重新拉取权限快照和候选目录，不自动重试，也不解析
  ProblemDetails 文本；选择项仅在最新候选事实中失效时清除。403/404 mutation 同样刷新并
  撤销写能力。成功后先取得 canonical server state，再清除选择。
- 查询进行中（包括已有缓存的 background reauthorization）隐藏完整名单并关闭写操作，
  防止跨登录会话短暂显示旧的 Owner/Manager/Judge/Observer 缓存。

#### 验证、远端基线与 Git

- `bun test`：59/59 passed，333 assertions；新增覆盖完整快照 fail-closed、UUID/重叠/
  Owner 约束、全量角色编辑、资格筛选、缺失候选元数据保留和 typed 409 解码。
- `bun run build`：`vue-tsc --noEmit` 与 Vite production build passed；仅保留依赖 PURE
  annotation 和既有大 chunk 警告。
- 本轮新增/干净目标文件 scoped ESLint passed。`src/api/noctf.ts` 的 11 项、
  `AdminCompetitionsWorkspace.vue` 的 1 项及 `AdminLayout.vue` 的既有告警已逐一与 HEAD
  baseline 比对，本轮没有新增 lint debt，也没有格式化无关旧代码。
- locale JSON parse、旧 collaborator/手写 permission URL 静态扫描与
  `git diff --check` passed；两项独立只读审查确认 fail-closed、资格、完整集合、revision、
  409/403/404 refetch 和缓存隐藏无提交阻断。
- 提交前通过 GitHub compare 再次核验远端 `main` 仍为
  `1687acbd6c81944cbad2672698b3b3c1002c98da`，与本地已合并基线 `identical`；当前 HEAD
  已包含该提交，因此没有新冲突或需要制造空 merge commit。
- 本节对应独立 Frontend 本地提交，提交说明为
  `feat(admin): manage competition permissions`；不 push、不创建 PR。保护文件继续沿用
  6.18，尤其不得混入 Wolverine outbox、Runner Properties、本地端口 compose、
  instance-operation/query 文件或 `scripts/`。
- 下一安全纵切回到 Backend：为 CompetitionChallenge 的 typed 409 和 delete/restore
  增加 revision fence，再重建 OpenAPI/client 并用生成 SDK 适配 Frontend；前端不得解析
  错误字符串代替强类型冲突。

### 6.24 Backend CompetitionChallenge 强类型冲突与生命周期并发栅栏（2026-07-31）

本节完成 6.23 与第 7 节首项的 Backend 协议和持久化部分；旧通用 ProblemDetails 字符串、
无条件 delete/restore 和错误唯一约束归类均不保留。没有新增或修改 EF migration。

#### 强类型协议与稳定冲突

- create/update/delete/restore 的 409 统一返回
  `CompetitionChallengeConflictResponse`，`code` 在 OpenAPI 中是 required string enum。
  稳定 code 为 `ResourceIdConflict`、`ChallengeOrderConflict`、
  `ChallengeTemplateConflict`、`RevisionConflict`、`LifecycleStateConflict`、
  `ChallengeTemplateNotFound` 与 `ChallengeTemplateModeMismatch`。
- PostgreSQL `pk_competition_challenges`、活动 template 唯一索引和活动 order 唯一索引按
  constraint name 精确映射，不再把 template link 冲突或稳定 ID 冲突伪装成 order 冲突。
- update body 的 BaseScore、Order、IsPublished、expectedRevision 四项全部 required；
  delete/restore 使用 required、minimum 0、non-nullable 的 query `expectedRevision`。
  FastEndpoints 运行时以 DTO 默认 `-1` 配合 validator fail-closed；定向 NSwag operation
  processor 只修正两个 lifecycle operation 的 schema，并在参数缺失时导出失败。

#### 聚合 revision、锁序与失败原子性

- delete/restore 在 Competition transaction lock 后按父 Competition 和稳定
  CompetitionChallenge ID（包含软删除）读取；先校验 revision，再校验生命周期方向。
  成功只推进一次 CompetitionChallenge.Revision 与 Competition.LeaderboardRevision；
  stale 或方向错误均无 DB 与 Application side effect。
- restore 继续采用 Competition → Challenge 固定锁序，并在持锁后重验活动 template、
  template/Competition mode 与两个活动唯一约束；template 删除、mode mismatch、order 或
  template link conflict 都保持删除状态、revision 与 leaderboard revision 不变。
- Hint 虽没有独立 revision，其 save/delete/restore 仍是父 CompetitionChallenge 聚合写。
  三条路径现在也先取得 Competition transaction lock，并在同一事务内保存 Hint、递增父
  revision 和 leaderboard revision；不再允许与 management 写并发丢 revision，也不会在
  Hint 保存失败时留下 phantom leaderboard revision。

#### 验证、OpenAPI、远端基线与 Git

- WSL 原生干净副本、.NET SDK 10.0.301、真实 Docker/Testcontainers：
  - Release `NoCTF.slnx` build：0 warning、0 error；
  - non-Integration：420/420 passed；
  - Integration：68 total，66 passed、0 failed、2 skipped；两个 skip 仍是未启用的真实
    Kubernetes dataplane 与未配置 fixture 的真实 Libvirt opt-in；
  - 新增真实 PostgreSQL 覆盖 strict lifecycle revision/state、template 删除与 mode
    mismatch、order/template 唯一冲突、并发 delete/restore，以及 Hint 三条写路径等待同一
    Competition lock、连续推进 revision 并使旧 management revision 冲突；
  - 全 solution analyzer、本轮 C# whitespace、EF pending model 与 `git diff --check`
    passed。
- OpenAPI 仍为 134 endpoints；两份 artifact 字节一致，SHA-256 均为
  `DBBAE6276375CA95AEC2056EFF4D8EEDE868B538B3AAC1624E1D3FCB8588F223`。
  `@hey-api/openapi-ts 0.97.3` 已从 artifact 重建 generated SDK，第二次生成 byte-stable。
- 提交前通过 GitHub compare 核验远端 `main` 仍为
  `1687acbd6c81944cbad2672698b3b3c1002c98da`，与本地已合并基线 `identical`；该提交是
  当前 HEAD 的祖先，因此没有冲突或需要制造空 merge commit。
- 本节作为独立 Backend 本地提交，提交说明为
  `feat(admin): fence competition challenge lifecycle`；不 push、不创建 PR。保护文件
  继续沿用 6.18，尤其不得混入 Wolverine outbox、Runner Properties、本地端口 compose、
  instance-operation/query 文件或 `scripts/`。
- 下一纵切进入 Frontend：只使用本节 generated SDK 发送 lifecycle expectedRevision，
  从最新 QueryClient fact fail-closed 构造 mutation，并按 typed conflict 处理 404/409；
  不得手写 URL 或解析错误文本。

### 6.25 Frontend CompetitionChallenge typed conflict 与最新 revision 适配（2026-07-31，当前最新）

本节完成 6.24 的 Frontend 纵切。delete/restore 不再使用无条件旧调用；没有手写 URL、
兼容层或 ProblemDetails 文本解析。

#### 最新事实、fail-closed 与冲突处理

- API wrapper 的 delete/restore query 直接使用 generated
  `AdminDeleteCompetitionChallengeData['query']` 与
  `AdminRestoreCompetitionChallengeData['query']`，必传 `expectedRevision`；请求只经
  `adminDeleteCompetitionChallenge` / `adminRestoreCompetitionChallenge` generated SDK。
- lifecycle builder 只接受 non-negative safe integer revision，并严格区分 active
  `deletedAt === null` 与非空 deleted timestamp。确认操作时不复用弹窗打开时的对象，而是按
  stable ID 从当前 inventory QueryClient key 重读；必须恰好有一个匹配且生命周期方向仍有效
  才构造 mutation。缓存缺失、重复 ID、revision 缺失或状态翻转均关闭弹窗、刷新清单且不发
  mutation。
- 409 decoder 只接受 `ApiError` status 409、对象 response 与 generated 七值 enum。
  `RevisionConflict` 使用既有 revision 提示；其他已知或未知 409、404 使用通用 lifecycle
  stale 提示，不解析 error message。delete/restore 的 404/409 都在等待 invalidate/refetch
  前清空旧弹窗，避免 mutation pending 时锁住失效对象。
- editor 的 update 404/409 会 refetch detail/inventory；只有 typed
  `RevisionConflict` 使用 revision copy，其余冲突统一提示重新核对服务端状态。

#### 验证、远端基线与 Git

- `bun test`：67/67 passed，357 assertions，13 个测试文件；新增覆盖七值 typed decoder、
  lifecycle safe-integer/state direction、从唯一最新 cache fact 取 revision，以及缓存缺失、
  状态翻转和重复 ID fail-closed。
- `bun run build`：`vue-tsc --noEmit` 与 Vite production build passed；仅保留依赖 PURE
  annotation 和既有大 chunk 警告。
- lifecycle helper、两个 Vue 组件和两个测试文件 scoped ESLint 0 error/0 warning；
  `src/api/noctf.ts` 的 11 项规则/消息与 HEAD baseline 完全一致，只因新增代码后移行号，
  本轮没有新增 lint debt。
- 目标文件 `git diff --check`、无手写 lifecycle URL/错误文本解析静态扫描 passed；独立
  只读审查发现的 404 dialog 残留与最新 cache 协调测试盲区均已修正，最终复审无阻断。
- 提交前通过 GitHub compare 再次核验远端 `main` 仍为
  `1687acbd6c81944cbad2672698b3b3c1002c98da`，与本地已合并基线 `identical`；没有新冲突，
  也不制造空 merge commit。
- 本节作为独立 Frontend 本地提交，提交说明为
  `feat(admin): adapt challenge lifecycle conflicts`；不 push、不创建 PR。保护文件继续
  沿用 6.18。
- 下一安全纵切回到 Backend：关闭 Challenge template mode 更新可绕过活动
  CompetitionChallenge mode invariant 的缺口；应给出 typed state/409、真实 PostgreSQL
  覆盖与 OpenAPI/client，再适配 Frontend，不得只依赖 start gate 晚拒绝。

上述旧目标迁移和 2026-07-30 GitOps 后端收尾均已完成代码与本地验证。本轮新增重点：

- 方案 A 的 Platform Bot 创建/Access JWT 签发，稳定 UUID、`includeDeleted` 与精确恢复；
- Challenge `DefinitionJson` / CompetitionChallenge `RulesJson` 所有权边界；
- AWDP disposable target 同时固化 Competition configuration、CompetitionChallenge 和
  Challenge definition 三个 revision，任一变化均 PlatformFailed、清理且不自动重跑；
- Docker 直接随机宿主端口、E2E socket GID/清理修正和 shell fixture LF 约束；
- 四模式全边界、真实 PostgreSQL、EF、OpenAPI 和 solution 门禁。

仍未完成/不属于本轮已部署：

- Frontend 的 Bot、Challenge lifecycle inventory、typed role 409、Challenge Bot
  Manager 授权、CompetitionChallenge 稳定 ID/lifecycle 与 Competition 权限完整集合 UI
  均已完成；CompetitionChallenge typed 409、delete/restore revision fence 与其 Frontend
  generated-SDK 并发适配也已完成。下一项是 Challenge template mode 更新的活动引用 invariant。
- 本地 `deploy-*` 六服务重建及真实 Bot GitOps apply/reapply/delete/restore 已完成；正式
  环境部署与运维验收尚未执行。
- 未推送远端、未创建 PR、未生产部署。
- 生产 Kubernetes 安装、Runner Pool 运维参数落地与生产式 Libvirt 演练仍需目标环境；
  Docker `InternetOnly`、TargetPort ACL 和 callback-only gateway 是已记录的后续加固，
  不阻塞本轮架构迁移。

## 7. 建议的下一交接顺序

1. 在 Backend 阻止 Challenge template 在存在活动 CompetitionChallenge 引用时改变
   GameMode；补稳定 typed conflict、真实 PostgreSQL 失败原子性与 OpenAPI/client，然后
   适配 Frontend。不得只依赖比赛启动时再发现 mode mismatch。
2. 有正式 Kubernetes/Libvirt 环境后执行相应 opt-in dataplane/lifecycle 与运维验收。
3. 推送必须等待用户明确指令；当前本地 commits 不得自行 push 或创建 PR。

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
4. 旧目标迁移、后端收口、本地部署与 GitOps 实机演练无需重做；Frontend typed 409、
   Bot Manager 授权、CompetitionChallenge lifecycle 与 Competition 权限完整集合 UI
   均已完成。CompetitionChallenge typed 409、delete/restore revision fence 与 Frontend
   generated-SDK 适配已按 6.24/6.25 完成；下一阶段处理 Challenge template mode 更新的活动
   引用 invariant。引用权限 Backend 测试数量时使用 6.22，引用最新 Backend 状态时使用
   6.24，引用最新 Frontend 状态时使用 6.25。
5. 继续实现时固定执行：
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
