# NoCTF 后端目标架构交接

> 创建于 2026-07-24，最后核验于 2026-08-06。文件名保留原日期，本文内容以最后核验日期为准。

## 1. 下一会话目标

2026-07-31 本轮 outcome、上传补偿、Runtime scope、Patch draft、Runtime cleanup/
replacement、团队并发额度与 CTF 排行榜前三血纵切已在本机闭合；Frontend 正继续按生成 OpenAPI 契约逐项
迁移。此前第 7 节列出的三个高置信 Backend 问题均已由 `a234ee6` 完成，最新状态以
6.33 至 6.63 和第 7 节为准：

- Docker Container/Compose 公开服务最终采用题目容器直接映射 Docker 随机宿主端口；
- 24 条 `LeaderboardRevision` 写路径均在同一事务发布 invalidation；
- Redis snapshot CAS、dirty/failure、订阅 TTL、首次订阅/GET 和 stale refresh 已闭环；
- Frontend 已使用生成的 OpenAPI 排行榜类型、正确 Hub 路径、重连 Join、heartbeat 和 polling
  fallback；
- AWDP Runner 已改为通过 API 最小权限下载 Fix archive，不再持有对象存储凭据；
- Frontend Authentication、Health、Competition、Challenge、Team 与 Flag submission
  公共链路已改用生成客户端；公开 Penetration 假模式、全局 Team 和同步 Flag 结果均已删除；
- Notification 已改为不可变事件历史，伪 read/unread 与不存在的 AWD/KoH dashboard、实时
  事件均已删除；
- Submission/Runtime 的真实 403/409/429/503 outcome 已进入 OpenAPI，认证用户限流分区
  不再退化为 anonymous+IP；
- PatchUpload 已按既定规范恢复为可替换 draft，替换与 Fix 消费共用事务锁，旧对象通过
  Wolverine durable outbox 清理；
- 当前环境可执行的后端测试（含真实 PostgreSQL/Redis/Wolverine/Docker 集成）均通过：
  497/497 non-Integration、118/118 可执行 Integration，0 failed；需要目标环境的
  Kubernetes/Libvirt 各 1 项保持显式 skip。EF 无 pending model，OpenAPI 两份 artifact
  字节一致，详见 6.48。

2026-07-30 已验证 GitOps 模板 `2571893` 可构建，`validate` 与 `self-test` 均通过。用户经
`$grill-me` 明确选择方案 A：使用普通 Organizer Bot、普通长生命周期 Access JWT、现有
细粒度管理 API 和稳定 UUID，不引入 Repository 专用 JWT 或原子 bundle API。后端现已
补齐 Bot identity/token、显式 ID、`includeDeleted`、模板与子资源恢复，并以真实
PostgreSQL 集成测试固定该契约。`docs/challenge-repository-gitops.md` 已改为方案 A。

### 当前完成度

- 6.31 排行榜 Backend 代码与本地验证范围已闭合；不存在遗漏的 revision invalidation 写点。
- 6.32 排行榜 Frontend OpenAPI/SignalR 适配已闭合；此前记录的 GitOps、权限与 challenge
  lifecycle Frontend 纵切也均已完成。
- 6.33 至 6.40 已完成 AWDP archive 权限收口，以及 Authentication/Health、
  Competition、Challenge、Team、异步 Flag submission、Notification 和真实 Hub surface
  的 generated-SDK/契约适配。
- 6.41 至 6.44 已完成 outcome OpenAPI、上传失败补偿清理、participant Runtime 可见性和
  PatchUpload replacement 的 Backend 修复。
- 6.45 至 6.47 已完成 provider cleanup/capacity 收敛、replacement/自动 provision
  状态机，以及 `MaxConcurrentRuntimeInstancesPerTeam` 事务额度、方案 A API 与 AWD
  start gate。
- 6.48 已完成 CTF 排行榜一血、二血、三血的统一强类型投影与 OpenAPI 契约；计分公式未改。
- 6.49 至 6.54 已完成独立个人资料、MailKit SMTP、排行榜冻结/黑灯、默认私密咨询、单比赛
  不可变事件日志，以及 CTF/AWDP 跨队 Flag 检测与人工裁决纵切。
- 6.55 已完成管理员平台运行日志固定保留/容量、强类型筛选、导出和事实投影审计；6.56 已完成
  安全忘记密码/邮箱重置、会话全失效、密码变更通知和前端恢复流程。
- 6.57 已完成完整比赛 ZIP 与平台审计导出；6.58 已完成 PostgreSQL、对象存储和 Wolverine
  持久状态的加密备份、隔离恢复与自动演练工具；6.59 已完成私密 Team 封禁申诉、Finished 后
  误判更正、排行榜重投影和最小公开更正通知。
- 6.49 至 6.60 的禁止 push/deploy 边界已由后续明确授权取代；6.61 和 6.63 已完成对应生产部署，
  最新生产状态以 6.63 为准。6.56 明确包含前端恢复流程；协作者拥有的其他 Frontend 未提交文件
  继续受保护。
- 真实 Kubernetes/Libvirt/生产运维验收等待用户提供目标环境细则。不要把已废弃或已否决
  的旧条目重新列为待办。
- “后端 100%”不表示已经生产部署，也不表示 Kubernetes/Libvirt 所有可选基础设施已在
  当前机器再次实测；第 6 节明确区分代码、测试、本地部署和生产部署状态。

## 2. Git 基线与工作树保护

- 仓库：`E:\SourceCode\NoCTF`
- 当前远端基线：`origin/main` = `1687acbd6c81944cbad2672698b3b3c1002c98da`。
- 6.31/6.32 开始前本地 HEAD：`cf8d6820ee39`；`origin/main` 是其祖先，当时
  ahead 20、behind 0。这是历史计数；6.31/6.32 随后由 `4b47ab5` 完成。
- 当前本地工作分支：`codex/backend-gitops-completion`
- 用户已明确要求 push。2026-07-31 通过 GitHub CLI 认证的 HTTPS fetch 确认
  `origin/main` 仍为当前分支祖先，behind 0，无冲突也无需空 merge。
- `5660dc3` 的首次推送已通过 GitHub API 核对远端 ref；6.42 至 6.44 的后续 bug fix 与
  handoff 已推送到同一工作分支。`a234ee6` 是 Runtime cleanup/quota 代码提交；
  `f9216b9` 是 CTF 排行榜前三血代码提交。
- 当前已推送功能 HEAD 与生产 checkout 均为
  `8b0bf555093368c540e3cd0a6a710811c5f72ce8 fix(deploy): install runtime GSS dependency`；
  `58f763f` 的邮箱修复也已包含在该生产版本，部署与清理结果见 6.63。本 HANDOFF 变更在功能提交
  之后单独提交并推送。
- 下方关于旧 `codex/backend-target-architecture-handoff` 分支的 ahead/behind 和提交
  序列是历史记录，不再代表当前 Git 状态。
- 本轮只更新并推送工作分支，按用户授权执行了 6.63 的生产部署；没有创建 PR 或直接推送 `main`。
- 本交接分支已包含远端 `main`，随后增加 Runtime、GitOps、并发栅栏与排行榜闭环纵切。
- 原目标架构远程分支：`origin/codex/backend-target-architecture`。
- 2026-07-27 推送前通过 GitHub CLI 认证的 HTTPS fetch 复核：
  `origin/codex/backend-target-architecture-handoff` 为 `0ca38b6`，本地包含该提交；
  文档提交前为 ahead 14、behind 0，没有协作者的 remote-only 分叉需要合并。
- 从旧交接 HEAD `003b75c` 到当前代码基线共有 38 个 backend 提交：

```powershell
git log --oneline 003b75c..2c6b4ce -- backend
```

以下工作树内容属于用户或仅为换行差异，未纳入后端提交；后续不得顺手清理或暂存：

- `TODO.md`
- `backend/src/NoCTF.Infrastructure/Messaging/WolverineTransactionalMessageOutbox.cs`：
  当前 clean；因前序用户所有权记录继续视为保护文件。
- `backend/src/NoCTF.Runner/Properties/`
- `deploy/docker-compose.local-ports.yml`
- `frontend/src/composables/useInstanceOperationState.ts`
- `frontend/src/lib/queryClient.ts`
- `frontend/tests/useInstanceOperationState.test.ts`
- `scripts/`
- 中断的 AWDP screen 清理保存在名为 `codex-wip-awdp-screen-removal` 的本地 stash；Frontend
  已交由协作者优化，未经用户明确要求不要 apply 或 drop。索引可能变化，应按名称查找。

提交前始终使用显式路径 `git add -- <files>`，不要使用 `git add .`。

## 3. 用户已经确认的架构决策

这些结论不要在下一会话重新发问或自行改写：

1. 一切按照新的目标架构推进，即远程 `codex/backend-target-architecture`。
2. 平台网络名为 `noctf-network`；每个题目/目标另有独立网络。
3. Checker 是可信、受管理员控制的容器，已受最小权限 JWT 约束。
4. Checker 可以同时加入题目网络与 `noctf-network`；平台组件也可按部署需要挂载两个网络。
5. Docker 公开访问的最终方案就是题目服务直接映射 Docker 随机宿主端口。不得再添加或规划
   HAProxy/ingress proxy、host firewall executor/sidecar、transparent gateway、
   TargetPort ACL 或 callback-only gateway；除非用户以后明确改变范围，否则这些不是待办，
   也不得再次用 `$grill-me` 追问。
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
23. 2026-08-06 用户明确决定 Administrator MFA 暂缓。不得创建 TOTP/Passkey/WebAuthn 模型、
    migration、Token claim、认证分流或前端页面，也不得再次为本轮追问其产品语义；只有用户以后
    明确恢复该范围时才重新审计。TODO 第 8 节应跳到数据导出。
24. 2026-08-06 用户明确决定分层限流暂缓。不得继续 Redis 分布式 limiter、可信代理/IP、额度、
    429/503 契约或限流前端适配，也不得继续本轮 `$grill-me`；只有用户以后明确恢复该范围时才重新
    审计。现有进程内 limiter 保持原样，本次决定不授权以相邻安全重构替代限流工作。

## 4. 2026-07-28 至 2026-07-31 早期门禁历史

> 本节全部是 2026-07-28 至 2026-07-31 早期的历史门禁记录，不代表当前工作树。
> 最新纵切与验证数字以 6.31 及其后续章节为准；不要把本节旧计数称为最终门禁。

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

以下是当时记录的能力边界，不等于当前第 7 节待办：

- Docker `InternetOnly` 没有宿主防火墙或 Egress Gateway 实现，且用户后续已明确不再推进。
- OVA/Libvirt EgressPolicy。
- IPv6 公网 egress、域名/FQDN allowlist、目的端口 allowlist。
- Runner 不做持续 dataplane/Cilium 健康探测；启动检查只证明指定配置和基线资源存在，
  `NetworkPolicyRequired=true` 仍保留运维声明语义。
- Pod/Service/node/management CIDR 自动发现；Pool 运维必须维护 `ProtectedCidrs`。
- Docker TargetPort ACL 与 callback-only gateway 后续已被用户明确废弃，不再列为加固项。

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
- provider orphan sweep 本身不释放 capacity；capacity 只在精确 assignment identity
  cleanup 后做 owner-checked release。离线 `Provisioning` 且没有 receipt 时缺少资源已
  清理的证据，因此保持原 Runner assignment 与 Redis claim，不释放、不 redispatch；
  后续只能由同 identity 的 typed cancel/termination，或迟到 receipt 持久化后 typed stop
  收敛。历史 `ReleaseRunnerCapacity` durable message 仅由无副作用 tombstone 吸收，不再
  产生新的 release token 或消息。详见 6.45。

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
- 该轮历史实现曾让 Runner 按 `Storage:Provider` 直连 MinIO。此权限边界已由 6.33
  取代：Runner 不再注册 `IObjectStorage` 或持有对象存储凭据，Fix archive 只通过绑定
  Submission 的最小权限内部 JWT 从 API 下载。
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
- `RunnerAssignmentReconciliationTests` 后续已扩充为 13 项，覆盖离线
  `Provisioning + no receipt` fail-closed、Redis 不可用时整批 defer、后续 heartbeat
  查询不可用时不做部分释放、replacement 等待精确 cleanup、在线节点资源审计和原节点
  receipt ownership。旧版“离线 assignment 直接 release/redispatch”语义已经废弃；
  当前契约详见 6.45。
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

本节仅保留 2026-07-31 当时的暂停过程；6.18 及其后续章节已经取代这里的状态与下一步。
用户当时要求先整合协作者更新后的 `main`，完成后端第一阶段并本地提交；随后在全套测试
运行中途明确要求暂停，写清 handoff 后交接。不得再用本节的中途状态否定后续已完成门禁，
也不得据此重复合并或恢复当时的旧待办。

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

### 6.21 Frontend CompetitionChallenge 稳定身份与生命周期（2026-07-31）

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

### 6.25 Frontend CompetitionChallenge typed conflict 与最新 revision 适配（2026-07-31）

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

### 6.26 Backend Challenge template Mode 活动引用不变量（2026-07-31）

本节完成 6.25 指定的 Backend 纵切。Challenge template update 不再把不存在、无权限和
revision 冲突合并成不可判定字符串，也不能在活动 CompetitionChallenge 仍引用时改变 Mode。
没有新增或修改 EF migration。

#### 方案 A、强类型状态与完整请求

- 按用户选择的方案 A，只有 `CompetitionChallenge.DeletedAt != null` 的历史引用不阻塞；
  任何未软删除引用都会阻止 Mode 改变，即使父 Competition 已软删除。这样 Competition
  restore 不会重新暴露 template/Competition mode mismatch。
- `IChallengeBankStore.UpdateAsync`、Application use case 与 Endpoint 统一使用
  `ChallengeTemplateWriteResult`。`NotFoundOrForbidden` 返回 404；
  `RevisionConflict` / `ActiveCompetitionModeConflict` 返回 typed
  `Conflict<ChallengeTemplateConflictResponse>`；无效请求或 definition 返回 400。
- `ChallengeTemplateConflictResponse.code/userIds` 均为 OpenAPI required。稳定 code 精确为
  `ResourceIdConflict`、`RevisionConflict`、`ActiveCompetitionModeConflict`、
  `OwnerIncludedInManagerSet`、`UserNotFound` 与 `RoleNotEligible`。
- update body 的 Mode、Visibility、Title、Direction、DefinitionJson、ExpectedRevision
  全部 required；Description 是唯一可选字段。三个值类型使用 nullable transport binding 与
  `NotNull` validator 区分“缺失”和合法的零值，revision 继续要求 minimum 0。

#### 锁序、失败原子性与真实 PostgreSQL

- update 在事务中只取得 Challenge advisory lock，先校验写权限和 revision，再检查未软删除
  CompetitionChallenge 的存在性；不追加 Competition lock，因此继续兼容 create/restore 的
  `Competition → Challenge` 固定锁序。
- 活动引用下改变 Mode 返回 typed conflict，Mode、Visibility、metadata、DefinitionJson、
  Revision 与 UpdatedAt 均不变；保持 Mode 的普通 metadata/definition 更新仍成功并只推进一次
  revision。软删除唯一引用后可改变 Mode，旧引用 restore 在 mismatch 时原子失败，改回 Mode
  后可以恢复。
- 真实 PostgreSQL 还覆盖父 Competition 已软删除但引用未删除时仍阻塞、stale revision
  优先于活动引用冲突，以及双向确定性并发：create 先持有 Competition/Challenge 锁并插入
  引用时 Mode update 等待后返回 typed conflict；Mode update 先持有 Challenge 锁时 create
  等待后返回 `TemplateModeMismatch`。两种锁序最终都不产生 mismatch。
- FastEndpoints 自动 FluentValidation 与 Application 的 invalid request/definition 分支共用
  `ApiValidationProblemFactory`；运行时媒体类型、OpenAPI metadata 与 typed union 均统一为
  `application/problem+json` / `ValidationProblemDetails`。最小 TestServer HTTP 回归分别发送
  空 body 与完整但非法 definition，验证两条真实请求路径的 400 契约，不依赖数据库。

#### 验证、OpenAPI/client 与下一步

- WSL 原生干净副本、.NET SDK 10.0.301、真实 Docker/Testcontainers：
  - Release `NoCTF.slnx` build：0 warning、0 error；
  - non-Integration：426/426 passed；
  - Integration：69 total，67 passed、0 failed、2 skipped；两个 skip 仍是未启用的真实
    Kubernetes dataplane 与未配置 fixture 的真实 Libvirt opt-in；
  - 全 solution analyzer、本轮 C# whitespace、EF pending model 与 `git diff --check`
    passed。
- OpenAPI 仍为 134 endpoints；两份 artifact 字节一致且第二次导出 byte-stable，SHA-256
  均为 `3429B4071BA6BF302C422EEAC36B5F4331D0234E33CB2494F32B67EDFEA1763C`。
  `@hey-api/openapi-ts 0.97.3` 已重建 generated SDK，update body 六项为必填、409 为 typed
  response；所有 validator 400 也从错误的 `FastEndpointsErrorResponse` 修正为
  `ValidationProblemDetails`，第二次生成 byte-stable。
- 本节作为独立 Backend 本地提交，提交说明为
  `fix(challenges): preserve template mode invariant`；不 push、不创建 PR。保护文件继续
  沿用 6.18。
- 下一纵切进入 Frontend：当前没有 Challenge template update UI，不复活已删除的 legacy
  editor；只让 generated conflict decoder 接受精确六值 enum（含
  `ActiveCompetitionModeConflict`）并补 fail-closed 测试，不得新增手写 URL 或解析错误文本。

### 6.27 Frontend Challenge template Mode conflict decoder（2026-07-31）

本节完成 6.26 指定的最小 Frontend 纵切，没有复活已删除的 template editor，也没有新增
手写 URL、手写 response type 或错误文本解析。

- `readChallengeTemplateConflict` 的 allow-list 接受 generated OpenAPI enum 中精确六个
  challenge template conflict code，新增 `ActiveCompetitionModeConflict`；仍只接受 HTTP
  409 的结构化对象 body，并继续对未知 code、字符串 body、非 409 与普通 `Error` fail closed。
- 测试以 generated enum 为键建立 exhaustive `Record`，OpenAPI 后续新增或删除 code 时会在
  TypeScript 编译期要求同步 decoder 测试；所有 code 均验证 `userIds` 规范化与去重。
- `bun test`：67/67 passed，364 assertions；`bun run build` passed；新改测试文件的定向
  ESLint passed。`src/api/noctf.ts` 全文件仍有 11 个本轮之前已存在的 lint baseline，本节只在
  typed allow-list 增加一个 generated enum 值，没有扩大该 baseline。
- 本节作为独立 Frontend 本地提交，提交说明为
  `feat(admin): decode template mode conflicts`；不 push、不创建 PR。保护文件继续沿用 6.18。

### 6.28 Backend Kubernetes Container 动态 NodePort 与重放安全（2026-07-31）

本节完成 runtime 网络审计中不依赖产品取舍的 Kubernetes Container 公网端口缺口。原实现只
创建 ClusterIP Service，并把请求中的 `containerPort -> 0` 原样写入 receipt，导致 persistent
Container 的 URL 无法展开；重放还会按名称信任 Pod/Service/NetworkPolicy。现在不保留该行为，
没有新增 API、OpenAPI contract、EF migration 或 Frontend 适配。

#### 双 Service 与实际端口回填

- Kubernetes Container 的 public `PortMappings` 只接受合法 container port 且值严格为 `0`，
  并只允许 `PersistentRuntime`；固定 host port、无效 identity/generation 或缺少 isolated
  Runtime network 都在任何 Pod/Service create 前 fail closed。
- 每个有端口的 Container 使用两个确定性 Service：
  - `{resourceId}` 是 ClusterIP，只包含 public + internal 的完整 container ports；
  - `{resourceId}-public` 是 NodePort，只包含 public `PortMappings.Keys`，创建请求不指定
    NodePort，由 apiserver 动态分配。
- receipt 不再回传零值，而是从 NodePort Service 精确构造
  `containerPort -> assignedNodePort`；internal host 继续使用已校验的 ClusterIP。
  AWDP disposable target 只创建 internal ClusterIP，不会意外创建 public Service。
- Service 的 platform labels 与 selector 固定包含 managed、job-kind、runtime instance、
  generation 与 runtime-id；internal/public role、type、精确 ports、TCP、name、targetPort、
  NodePort 唯一性、ClusterIP 及额外暴露字段全部在 create response 与 replay 时校验。

#### 幂等重放、stale exposure 与安全清理

- Pod、Service 与 callback NetworkPolicy 的 create 409/响应丢失都先 read-back，并只在完整
  identity/shape contract 匹配时继续；异主或 drift 资源不 patch、不覆盖、不删除。
- 所有 Pod 都写入同一套 platform identity labels；existing Pod 重放校验 runtime-id、
  job-kind、generation、sandbox，callback workload 还必须匹配 checker purpose。
- 预期无 public port 时，无论 `NetworkName` 是否为空，都会检查 `{resourceId}-public`：
  异主资源 fail closed，同 identity stale NodePort 使用 UID precondition 删除并等待实际消失，
  防止 Container 未声明 port 时仍被旧 Service 转发。
- callback NetworkPolicy 的 Pod selector、callback peer、DNS peer 均拒绝 MatchExpressions；
  policy types、Ingress、两条 Egress、protocol/port 与 `EndPort == null` 全部精确校验，避免
  “policy 存在但不选择 checker Pod”或端口范围扩大后退化为无限制 egress。
- create 失败只清理由本次调用明确创建的资源；删除必须携带 apiserver UID precondition，
  不再按确定性名称盲删并发赢家或异主资源。普通 destroy 先撤销 public Service，再清理
  internal Service、callback policy 与 Pod；terminal recreation 等待这四类资源全部消失。

#### 验证、远端边界与下一步

- WSL 原生干净副本、.NET SDK 10.0.301、真实 Docker/Testcontainers：
  - `KubernetesContainerLifecycleTests`：24/24 passed，覆盖动态 NodePort、AWDP internal-only、
    fixed port 前置拒绝、合法 replay、identity/selector/port-range drift、response-loss
    read-back、zero-port stale exposure、UID 清理与双 Service destroy；
  - Release `NoCTF.slnx` build：0 warning、0 error；
  - non-Integration：443/443 passed；
  - Integration：69 total，67 passed、0 failed、2 skipped；两个 skip 仍是未启用的真实
    Kubernetes dataplane 与未配置 fixture 的真实 Libvirt opt-in；
  - 全 solution analyzer、本轮 C# whitespace、EF pending model 与 `git diff --check`
    passed；独立只读复核最终无 P1/P2 blocker。
- OpenAPI 仍为 134 endpoints；两份 artifact 字节一致且两次导出 byte-stable，SHA-256 均为
  `3429B4071BA6BF302C422EEAC36B5F4331D0234E33CB2494F32B67EDFEA1763C`，因此无需重建
  Frontend generated SDK，也没有 Frontend URL 适配。
- 真实 Kubernetes `DNS + NetworkPolicy + NodePort + cleanup` dataplane 用例仍需设置
  `NOCTF_KUBERNETES_INTEGRATION` 并在目标集群执行；本节不把 opt-in skip 误报为实机通过。
- 提交前通过 GitHub compare 再次核验远端 `main` 仍为
  `1687acbd6c81944cbad2672698b3b3c1002c98da`，与本地已合并基线 `identical`；没有新冲突，
  也不制造空 merge commit。
- 本节作为独立 Backend 本地提交，提交说明为
  `fix(runtime): reconcile kubernetes node ports`；不 push、不创建 PR。保护文件继续沿用
  6.18。
- 本节记录的下一项 AWD checker callback sequence/version persistence fence 已在 6.29
  完成。此前记录的 Docker firewall/sidecar/gateway 选择已被用户后续决策废弃；以第 3 节
  第 5、13 项的直接随机宿主端口方案为准，不再推进该 dataplane。

### 6.29 Backend AWD checker callback 持久化栅栏（2026-07-31）

本节完成 6.28 指定的下一项安全纵切。原 token issuer 与 API auth policy 已写入并要求
`checker_sequence` / `processing_version`，但 Endpoint 丢弃这两个 claim，Application 结果
对象也没有对应字段；PostgreSQL Store 只校验 generation，随后会把旧 callback 的状态冒充为
当前 checker sequence、推进 `LastAppliedCheckerSequence` 并清除新 checker deadline。现在
不保留该行为。

#### Claim 贯通、FOR UPDATE fence 与既有多回调语义

- `RecordAwdCheckResultEndpoint` 解析 runtime identity、generation、checker sequence、
  runtime processing version 与 deadline；数字 claim 非法时在进入 Application/Store 前
  返回 401。Request body 仍只接受强类型 `AwdServiceState`。
- `AwdCheckResult` 显式携带 sequence/version；`InternalResultStore.RecordAwdAsync` 在
  `SELECT ... FOR UPDATE` 取得 Runtime 行锁后比较两个 fence：
  - token 任一值领先当前 Runtime 时返回 `Conflict`；
  - 否则任一值落后当前 Runtime 时返回 `Superseded`；
  - 两项都精确相等时才允许更新 checker 状态、时间、last-applied sequence 与 deadline。
- 精确 fence 内仍保留现行协议：同一次 checker 执行可以多次回调，后一次 typed status
  覆盖前一次；并发相同 callback 也仍可返回 `Applied`，但状态未变化时不会重复生成计分
  事实。sequence/version fence 只阻止旧任务冒充当前任务，不把 callback 改成一次性 token。
- callback 的 transport receive time 可能与取得行锁的顺序相反。Store 在 Runtime 行锁内先
  归一化到 PostgreSQL 微秒精度，再相对上一 applied timestamp 严格单调推进；Runtime 状态
  与 ScoringEvent 共用该时间，避免最终状态与按 OccurredAt 读取的计分时间线分叉。
- 所有拒绝分支都发生在 revision/scoring 查询和写入之前，不修改 CheckerStatus、
  CheckerStatusUpdatedAt、LastAppliedCheckerSequence、CheckerDeadlineAt、ScoringEvent、
  LeaderboardRevision 或 Outbox。
- 不同 Team/Runtime 可同时改变同一 Competition 的服务状态；LeaderboardRevision 不再对
  tracked Competition 做客户端 `R -> R+1`，改用数据库表达式原子递增，防止共享 revision
  丢更新及旧投影误标为最新。
- `LastAppliedCheckerSequence` 从已校验的 result sequence 写回。原实现还把 checker sequence
  塞入 `AwdServiceStatus.ProcessingVersion`；该字段按 `docs/database.md` 只属于 submission
  processing，此类事件现在保持 null。`docs/api.md`、`docs/runtime.md` 与认证文档已同步
  sequence/version fence 及同次多回调边界。

#### 验证、OpenAPI、Frontend 与远端基线

- 新增 TestServer HTTP 回归，证明两个 JWT fence claim 精确进入 Application、畸形 claim
  返回 401 且不调用 Store，并验证 `Superseded -> 202`、`Conflict -> 409`。
- 真实 PostgreSQL/Testcontainers 回归先在旧实现上得到 `Applied`、复现旧 sequence 覆盖，
  修复后覆盖：同 sequence 多次写、旧 sequence、旧 processing version、未来 sequence、
  未来 processing version、stale/future 混合输入、逆序 receive time、并发 callback 与
  拒绝后的完整无副作用；另用共享 SQL barrier 强制两个 Runtime 都先读到同一 Competition
  revision，再证明原子累加结果为 `R+2`。
- WSL 原生校验副本、.NET SDK 10.0.301、真实 Docker/Testcontainers：
  - HTTP + PostgreSQL 定向用例：6/6 passed；
  - non-Integration：447/447 passed；
  - Integration：70 total，68 passed、0 failed、2 skipped；两个 skip 仍是未启用的真实
    Kubernetes dataplane 与未配置 fixture 的真实 Libvirt opt-in；
  - Release `NoCTF.slnx` build：0 warning、0 error；
  - 全 solution analyzer、本轮 C# whitespace、EF pending model 与 `git diff --check`
    passed；独立 blocker-only 复核最终无 P1/P2 或剩余明确测试缺口。
- OpenAPI 仍为 134 endpoints；两份 artifact 在两次导出前后均字节一致且 byte-stable，
  SHA-256 都是
  `3429B4071BA6BF302C422EEAC36B5F4331D0234E33CB2494F32B67EDFEA1763C`。
  route、request body、response union 与 generated contract 均未改变，因此无需重建
  Frontend SDK，也没有 URL 或页面适配。
- 提交前通过 GitHub compare 核验远端 `main` 仍为
  `1687acbd6c81944cbad2672698b3b3c1002c98da`，与本地已合并基线 `identical`；没有新冲突，
  不制造空 merge commit。
- 本节作为独立 Backend 本地提交，提交说明为
  `fix(runtime): fence awd checker callbacks`；不 push、不创建 PR。保护文件继续沿用 6.18。
- 本节之后发现并完成的明确 Backend 纵切是 6.30/6.31。此前记录的 Docker firewall、
  sidecar、transparent gateway、TargetPort ACL 与 callback-only gateway 决策边界已经
  被用户明确废弃；不要再推进或询问。

### 6.30 Backend LeaderboardRevision 全写路径原子化（2026-07-31）

6.29 修复 AWD callback 后，对所有运行时 `LeaderboardRevision` 写点继续做了窄范围审计。
仓库共有 24 个实际写点：18 个已调用数据库原子 helper、Competition configuration 有 1 个
等价的数据库表达式，另有 5 个 tracked entity 的客户端 `R -> R+1`。后五条在同一
Competition 的不同 Submission、AWDP Runtime、Team hint unlock、KoH observation 或
lifecycle 与计分事务并发时，都可把两次已提交事实压成一个 revision。现在不保留该行为。

#### 五条写路径与统一数据库原子递增

- `SubmissionProcessor.CompleteAsync`、`InternalResultStore.RecordAwdpAsync`、
  `ChallengeHintStore.UnlockAsync`、`KohObservationHandler.Handle` 与
  `CompetitionLifecycleStore.TryTransitionWithAuditAsync` 均移除 tracked Competition 的
  常量 revision 写回，改为在原业务事务、原有效分支内调用
  `LeaderboardRevision.IncrementAsync`。
- Submission processing version、AWDP Runtime `FOR UPDATE`、Hint 的
  `(Competition, Team)` advisory lock 只能串行同一局部资源；不同资源仍可同时改变同一
  Competition。修复不扩大这些锁，也不把计分热路径塞进全 Competition advisory lock，
  而是让 PostgreSQL 的 `SET revision = revision + 1` 行更新按最新已提交值求值。
- Lifecycle 的 `CompetitionWriteLock` 是合作式 advisory lock，不是 Competition 行锁；
  其他计分写入并不统一获取它。Wolverine ambient transaction 下 lifecycle 也不会自建
  Serializable transaction，因此原 tracked 写回同样存在真实 ReadCommitted 丢更新。
  Lifecycle 现与其他四条路径使用同一原子 helper。
- Worker 需要调用该 Infrastructure-owned helper，因此 helper 类型由 assembly-internal
  调整为 public；方法职责仍只是在指定 Competition 存在时做一次数据库表达式递增，
  影响行数不是 1 时 fail closed。
- ScoringEvent、Submission/Runtime 状态、lifecycle audit、revision 与 transactional
  Outbox 仍处于原事务中；无效、superseded、重复、余额不足或无状态变化分支不会推进
  revision。没有新增锁、表、migration、消息字段或 API contract。

#### 确定性并发回归与投影语义

- 新增共享 `CompetitionLeaderboardUpdateBarrier`，只在测试启用后拦截真正包含
  `UPDATE competitions ... leaderboard_revision` 的 SQL statement，并在执行数据库写入前
  同步两个事务；它不会把 SELECT 中的 `updated_at` 误判为 UPDATE。
- 四个真实 PostgreSQL/Testcontainers 并发用例覆盖五条旧写路径：
  - 两个普通 Submission 同时完成；
  - 两个独立 AWDP Fix Submission/Runtime 同时回调；
  - 两个不同 Team 同时解锁 Hint；
  - 合法 KoH observation 与同一 Competition 的 `Running -> Paused` lifecycle 同时提交。
- 四个用例都先在旧实现上稳定得到“两条事实/转换均成功，但最终 revision 只有
  `R+1`”；原子化后均为 `R+2`，并断言对应业务事实、状态/audit 与两条
  `ProjectLeaderboard`。
- `ProjectLeaderboard` 只携带 CompetitionId 并全量重算，不会把重复消息计成重复分数。
  丢 revision 的可观测问题在于较旧投影可能最后覆盖 Redis，却与数据库 target 使用同一
  revision 而被误报 fresh。原子化后旧投影最多标为 `R+1`，数据库 target 为 `R+2`，
  `snapshotRevision < targetRevision` 会正确返回 stale。投影端若要完全禁止旧快照后写，
  仍属于独立 CAS/single-flight 范围，不阻塞本节。

#### 验证、OpenAPI、Frontend 与下一步

- WSL 原生干净副本、.NET SDK 10.0.301、真实 Docker/Testcontainers：
  - 四个新增并发用例：4/4 passed；
  - non-Integration：447/447 passed；
  - Integration：74 total，72 passed、0 failed、2 skipped；两个 skip 仍是未启用的真实
    Kubernetes dataplane 与未配置 fixture 的真实 Libvirt opt-in；
  - Release `NoCTF.slnx` build：0 warning、0 error；
  - 全 solution analyzer、本轮 C# scoped whitespace、EF pending model 与
    `git diff --check` passed。
- OpenAPI 仍为 134 endpoints；两份 artifact 两次导出 byte-stable，SHA-256 均为
  `3429B4071BA6BF302C422EEAC36B5F4331D0234E33CB2494F32B67EDFEA1763C`。
  本节没有 route、request/response、generated contract 或 URL 变化，因此无需重建
  Frontend SDK，也没有页面适配。
- GitHub compare 再次确认远端 `main` 仍为
  `1687acbd6c81944cbad2672698b3b3c1002c98da`，与本地 `origin/main` identical，且该
  commit 已是当前 HEAD 的祖先；没有远端冲突，也不制造空 merge commit。
- 本节作为独立 Backend 本地提交，提交说明为
  `fix(scoring): atomically advance leaderboard revisions`；不 push、不创建 PR。保护文件
  继续沿用 6.18。
- 6.30 后继续完成了 6.31/6.32。Docker firewall/gateway/ACL 旧待办已经废弃；正式
  Kubernetes/Libvirt 验收仍需对应环境，不自行声称完成。

### 6.31 Backend 排行榜按需刷新闭环（2026-07-31，阶段记录）

6.30 只完成了 `LeaderboardRevision` 原子递增，尚未闭合 revision 到 Redis snapshot 的
完整因果链。继续审计发现：普通 revision mutation 仍混用强制 `ProjectLeaderboard` 与
Application 层 post-commit 补偿；部分写点没有 durable 消息；cache invalidation 没有记录
dirty；stale GET 不会主动刷新；并发旧投影可以覆盖新快照；SignalR 订阅没有服务端 TTL。
本节不保留这些行为。

本节 Backend 与 6.32 Frontend 作为同一闭环由本地提交
`4b47ab5 fix(scoring): close leaderboard refresh loop` 完成。

#### 事务 invalidation 与强制 projection 分离

- 新增 durable `InvalidateLeaderboard(CompetitionId)`，API/Worker Wolverine 路由均归属
  `noctf-worker`。
- 穷尽排除 migration/snapshot 后共有 24 个实际 revision 写点：
  TeamRegistration 5、TeamModeration 1、TeamMembership 4、ChallengeManagement 3、
  ChallengeHint 4、ChallengeConfiguration 1、CompetitionConfiguration 1、
  CompetitionLifecycle 1、SubmissionProcessor 1、InternalResultStore 2、KoH 1。
- 24/24 都在对应业务事务 commit 前 1:1 写入 transactional outbox invalidation；没有
  missing，也没有 commit 后才 publish。事务拥有者在 commit 后 flush。
- 删除 Team、Challenge、Competition configuration/lifecycle 等 Application use case 的
  cache/message 补偿，避免“业务已提交但消息未持久化”的窗口和重复投影。
- lifecycle 每次都会递增 leaderboard revision，因此同事务无条件 invalidation；runtime
  provision/cleanup flags 保留，移除误导性的 `CompetitionLifecycleEffects.ProjectLeaderboard`。
- 普通 revision mutation 不再强制全量投影。`ProjectLeaderboard` 只保留无快照/stale GET、
  首个活跃订阅者和没有 revision mutation 的 AWD successor-round 时间边界。
- Worker 收到 invalidation 后先记录 dirty；仅在该 Competition 有活跃订阅者时刷新。
  `ProjectLeaderboard` 则始终执行强制按需刷新。

#### Redis snapshot、CAS、失败状态与单航班

- 每个 Competition 使用一个 `leaderboard:{competitionId}:state` Redis hash，保存
  snapshot、snapshotRevision、dirtyRevision、failureRevision 与 failureAt。
- 完整投影先获取 PostgreSQL Competition transaction-level advisory lock，再读取目标
  revision；多个 Worker 对同一 Competition 只会有一个实际 projector 执行。
- Lua CAS 拒绝旧 revision 和相同 revision 重放覆盖；只有实际接纳的新 snapshot 才广播
  refresh。revision 使用十进制字符串长度与字典序比较，在 `2^53` 以上仍保持完整 Int64
  精度，不依赖 Lua double。
- dirty/failure 只允许被相同或更高 revision 的成功 snapshot 清除；较旧 failure 不覆盖较新
  failure。调用方 cancellation 不会伪造 projection failure。
- projection 只接收 `Approved` Team；Pending/Rejected Team 不进入排行榜事实。
- cached snapshot 的 `stale` 与 target revision 仍以 PostgreSQL 为事实源。有旧 snapshot
  且 stale 时 GET 立即返回 200，并 durable queue 强制刷新；无 snapshot 时沿用
  202 + Retry-After/status URL，当前 target 的最后失败沿用 typed 503。

#### 订阅 TTL 与 Hub

- 新增 `ILeaderboardSubscriptionRegistry`；Redis 每 Competition 一个 ZSET，
  member 为 SignalR ConnectionId、score 为过期时间，默认 TTL 90 秒。
- Lua 使用 Redis `TIME`，避免多个 API 副本的主机时钟偏差；Touch/prune/first-active 判定
  原子执行，并发 32 个首订阅者只会有一个触发强制投影。
- `CompetitionHub.JoinCompetition` 在鉴权和 group join 后登记订阅；首个活跃订阅者发送
  `ProjectLeaderboard`。`HeartbeatCompetition` 只能续期当前连接已加入的比赛；断连立即
  remove，异常时仍有 TTL 兜底。

#### Backend 验证与契约

- 最终 WSL 原生隔离副本、.NET SDK 10.0.301、真实 Docker/Testcontainers：
  - Release `NoCTF.slnx` build：0 warning、0 error；
  - non-Integration：456/456 passed；
  - Integration：83 total，81 passed、0 failed、2 skipped；
  - 两个 skip 仅为未启用 `NOCTF_KUBERNETES_INTEGRATION` 的真实集群 dataplane 和未配置
    `NOCTF_LIBVIRT_DISK_PATH` 的真实 Libvirt fixture；
  - 全 solution analyzer、EF pending model 与 `git diff --check` passed。
- 新增/扩展的真实依赖覆盖包括 24 写点 outbox、事务 rollback/replay、Redis snapshot
  旧写拒绝、相同 revision 幂等、dirty/failure 单调、两个 Worker 单航班、`2^53` 以上
  revision、队伍审批过滤，以及订阅 first-active/并发/disconnect/TTL。
- 没有 EF model/migration 变化。OpenAPI 仍注册 134 endpoints；两份 artifact 与仓库基线
  byte-identical，SHA-256 都是
  `3429B4071BA6BF302C422EEAC36B5F4331D0234E33CB2494F32B67EDFEA1763C`。

### 6.32 Frontend 排行榜强类型与实时适配（2026-07-31，阶段记录）

- 本节与 6.31 同属本地提交 `4b47ab5 fix(scoring): close leaderboard refresh loop`。
- `competitionApi.leaderboard` 明确返回 OpenAPI 生成的 200/202 union；移除后端不存在的
  legacy `leaderboardTrend` 与 `leaderboardTeam` 请求，不再让它们通过 `Promise.all`
  拖垮真实排行榜。
- `leaderboardPresentation.ts` 只把带 `entries` 的 200 响应视为 snapshot，并把生成字段
  `score` / `solveCount` 映射到现有展示字段。202 Processing 不会清空最后一次成功快照。
- `ScoreboardView`、`GameLayout` 与 `CompetitionDetailWorkspace` 三个消费者都改用生成类型；
  删除 `as never`、手写 leaderboard DTO、`totalScore`/`solvedCount` 假契约与不存在的队伍
  详情/趋势 UI。
- Hub path 统一为相对路径 `/hubs/v1/competitions`，继续由现有 `apiUrl()` 展开部署 base URL；
  没有写死 scheme、host 或 port。
- 首次连接和自动重连都会重新 `JoinCompetition`；每 30 秒调用
  `HeartbeatCompetition`。进入 reconnecting、断连、Join/heartbeat 失败时立即启用 10 秒
  polling；Join/heartbeat 成功后停止 fallback。组件卸载和断连都会停止 heartbeat。
- 受版本控制 Frontend 测试共 67/67 passed，其中包含新增的 2 项排行榜 contract；
  `vue-tsc` 与
  Vite production build passed。Rollup 只有既有 dependency PURE comment 与大 chunk
  warning，无 TypeScript/build error。
- HTTP OpenAPI 未变化，因此无需重新生成 SDK；本节只让现有生成 SDK 成为唯一排行榜
  transport 类型来源。

### 6.33 AWDP Fix archive 最小权限下载（2026-07-31）

- 本地提交：`817b204 fix(runtime): download awdp archives through api`。
- Runner 已删除 AWS/S3/Local `IObjectStorage` 注册，不再接收 MinIO/S3 endpoint、bucket
  或访问凭据。API/Worker 仍按职责访问对象存储。
- `AwdpFixWorkReader` 使用既有 `IssueFixArchiveRead` 签发绑定
  `PatchUploadId`/`SubmissionId` 的 5 分钟内部 JWT，并通过既有
  `DownloadFixArchiveEndpoint` 取得归档；Work payload 不再携带对象存储 key。
- 新 downloader 使用 `ResponseHeadersRead` 流式落盘，再按 PostgreSQL 中的长度和
  SHA-256 做固定时序复核；404、长度或摘要不匹配均不会进入解包/执行。
- `RunnerScoring:CallbackBaseUrl` 现在必须显式配置为绝对 HTTP(S) URI；缺失、相对 URI
  或非 HTTP(S) scheme 在 Runner 注册时 fail-fast，不存在默认 host、scheme 或 port。
- 验证：Release solution build 0 warning/0 error；462/462 non-Integration passed；
  analyzer、whitespace、`git diff --check` passed；无 Runner 存储凭据的真实 AWDP
  API/Worker/Runner + PostgreSQL/Redis/MinIO/Docker E2E 1/1 passed，隔离资源已清理。
- 本节不猜测正式 Kubernetes service DNS 或 NetworkPolicy。Runner 到 API 的允许规则要在
  用户提供真实环境部署细则后按实际命名空间、Service 与 CIDR 落地。

### 6.34 Frontend Authentication/Health 生成契约适配（2026-07-31）

- 本地提交：`27ed39f fix(frontend): bind auth and health contracts`。
- login、register、显式 refresh 与 store 自动续期统一使用生成 SDK 的
  Authentication endpoints；不再调用旧 `/auth/*` 路径或自行拼接 base URL。
- Refresh 使用 HttpOnly cookie 的 `credentials: include`，并显式移除继承的旧
  Authorization header，避免 `timeoutFetch` 递归触发续期；refresh operation 通过
  type-only 强类型回调注入，不形成 generated client 的运行时循环依赖。
- Admin health 使用生成的 `/health` contract。页面只展示真实 `{ status }`，已删除后端
  不提供的 `checks[]`、组件数量和伪子系统状态。
- 新增精确 request path/body/credentials/header contract 测试，并覆盖自动续期。
  受版本控制测试加本节测试共 72/72 passed；`vue-tsc`、Vite production build、
  相关文件 scoped lint 与 `git diff --check` passed。生成文件无改动。

### 6.35 Frontend Competition 公共读取生成契约适配（2026-07-31）

- 本地提交：`080051c refactor(frontend): bind public competition reads`。
- `competitionApi.list/get` 统一使用生成的
  `/api/v1/competitions` 与 `/api/v1/competitions/{competitionId}` operation；list
  正确解包 `{ items }`。
- 新增单一 presentation boundary，将生成的数字 `GameMode` 0/1/2/3 映射到
  CTF/AWD/AWDP/KoH，将 0..5 lifecycle status 映射到
  Draft/Visible/Published/Running/Paused/Finished。缺少 id、标题、Owner、时间、人数或
  auto-approve 等必要事实时 fail-closed，不回填 mock 默认值。
- Competition list、Home、Registration、Detail 和 Teams 中的真实消费者已适配。
  删除后端不存在的 `registeredTeamCount`、tracks/`trackName` 与默认五人假设；保留真实
  `maxTeamMembers` 和 `teamRegistrationAutoApprove`。
- Penetration 是普通 CTF 内容，因此公开 Penetration API、路由、gateway、view/workspace
  已删除；本节未扩展到仍待后续迁移的管理页面。
- 开发 mock 已改为 v1 envelope、数字 enum 与真实字段。新增 contract test 后，
  受版本控制测试加本节测试共 76/76 passed；`vue-tsc`、Vite production build、
  scoped lint 与 `git diff --check` passed。生成文件无改动。

### 6.36 SubmitFlag alternatives OpenAPI 契约修正（2026-07-31）

- 本地提交：`4afe33f fix(api): document flag submission alternatives`。
- `SubmitFlagRequest` 的运行时规则仍要求 `flag` 与 `flags` 恰好提供一个，且批量元素不得
  为 null；没有放宽业务校验或改变 endpoint 行为。
- FastEndpoints Swagger 只忽略 `RuleForEach(...).NotNull()` 对 required/nullability 的
  错误推断。OpenAPI 现在将 `flag` 和 `flags` 都描述为 optional nullable，由运行时 XOR
  validator 决定组合合法性。
- 两份 OpenAPI artifact 已统一导出且完全一致；生成 TypeScript 请求成为
  `flag?: string | null` 与 `flags?: string[] | null`，不再强迫单 Flag 请求同时携带
  `flags`。
- 新增 5 项 validator 协议测试与 1 项 artifact 架构测试。最终 468/468
  non-Integration passed；Release solution build 0 warning/0 error；analyzer、Frontend
  Bun build、固定版本 OpenAPI/client 漂移检查和 scoped `diff-check` passed。

### 6.37 Frontend Challenge 公共读取与附件生成契约适配（2026-07-31）

- 本地提交：`2e42808 refactor(frontend): bind public challenge reads`。
- Challenge list/get 统一使用生成的 v1 operation；list 正确解包 `{ items }`。
  Presentation boundary 对 id、Competition/模板身份、标题、方向、`baseScore`、顺序、
  publication、revision、时间及 URL 等真实字段 fail-closed。
- Competition Detail、Challenge Modal 与 AWD 消费者已改用 `baseScore` 和稳定
  CompetitionChallenge id；删除后端不提供的 `typeId`、`points`、`solveCount`、
  `firstBloods`、`deploymentType`、`descriptionFormat`、hint 列表、`attachmentUrl` 与
  `patchTemplateUrl`。
- All-policy attachment list、指定附件下载与 RandomOnePerTeam 下载均使用生成 operation，
  不拼接 DTO URL。只有当前队伍已获批、比赛 Running、challenge detail 存在且 list 返回
  404 时才展示随机附件操作；401/403 不降级，随机分配只由用户点击触发，不预取。
- 三组开发 mock 已改为 v1 path、`{ items }` envelope 与真实 ChallengeResponse 字段。
  新增 7 项 contract test；最终 Frontend 83/83 passed，`vue-tsc` 与 Vite production
  build passed，新增/叶文件 lint 0，旧文件 lint 未恶化且总数下降，`git diff --check`
  passed。

### 6.38 Frontend Team competition-scope 生成契约适配（2026-07-31）

- 本地提交：`cd3f8b0 refactor(frontend): scope teams to competitions`。
- Team list/get-my/create/join/leave 统一使用生成的
  `/api/v1/competitions/{competitionId}/teams...` operation；list 解包 `{ items }`，
  get-my 将 generated 404 映射为无当前队伍，join/leave 正确处理 204 void。
- Presentation boundary 将数字 registration status 0/1/2 映射为
  Pending/Approved/Rejected，严格校验稳定身份、Captain、MemberIds、锁定状态和报名时间，
  `memberCount` 只由 `memberIds.length` 派生。
- Registration、Competition Detail、Game Layout 与 AWD 消费者已改为单个
  competition-scoped current team。join 不读取虚构返回值，leave 已迁入比赛报名页；
  客户端不再用后端未返回的 ban、持久 invitation token、track 或 `isCaptain` 字段作门禁。
- 后端没有跨比赛“我的全部队伍”聚合，因此公开 `/teams` route/nav/view/workspaces 与
  Home team aggregate/statistics/list 已删除；没有用 N+1 或新后端协议补造该页面。
  Admin team surface 不在本节范围。
- 开发 mock 已改为 v1 get-my 单对象和 list envelope；中英文文案同步删除旧 tracks/token/
  ban/global-team 语义。新增 5 项 contract test；最终 Frontend 88/88 passed，
  `vue-tsc` 与 Vite production build passed，新文件 lint 0，6 个 legacy touched 文件
  lint 与 HEAD 基线一致，generated drift 0，`git diff --check` passed。

### 6.39 Frontend Flag submission 异步判定适配（2026-07-31）

- 本地提交：`423fa65 refactor(frontend): poll flag submission outcomes`。
- 单 Flag 统一调用生成的
  `/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/flag-submissions`
  operation，请求体只有 `{ flag }`。不再传 `teamId`、模板 Challenge id 或 victim team；
  AWD/AWDP 的目标由服务端从 Flag 推导。
- 202 只表示 attempt 已受理。客户端保存 `submissionId`/`statusUrl` 元数据，但状态读取始终
  使用生成的 competition-scoped status operation；每秒轮询 Pending/Queued/Processing，
  只在 Completed 或 PlatformFailed 终止，关闭弹窗、切题、卸载或新提交时取消旧请求。
- 新增完整数字枚举 presentation boundary：SubmissionKind、EvaluationState、
  ScoringResult 与 29 个 ScoringFailureCode 均 fail-closed；只有
  `Completed + Correct + (Flag | Break)` 派生 solved，Fix、错误、排队与平台失败均不会
  误标已解。
- Submission history 使用生成的签名游标分页，opaque cursor 原样传递并防重复游标死循环；
  当前已批准队伍才加载自己的历史。正确终态才刷新排行榜/AWDP 状态，其他终态只展示真实
  判定结果。
- AWD 已删除 victim/team selector 和本地伪造 attack-failed 事件；KoH 不再展示 Flag
  提交。开发 mock 改为真实 v1 list/202/status 三段契约。
- 新增 9 项 contract tests；最终受版本控制 Frontend 测试加本节测试 97/97 passed，
  520 assertions。`vue-tsc`、Vite production build、JSON/whitespace 和
  `git diff --check` passed；新增文件及 AWD 文件 lint 0，`noctf.ts` 与 Competition
  Detail 旧基线不变，Challenge Modal lint error 从 83 降至 77。

### 6.40 Frontend 不可变事件历史与真实实时 surface（2026-07-31）

- 本地提交：`a28e761 refactor(frontend): align event surfaces with public contracts`。
- Notification 统一调用生成的 `/api/v1/notifications` operation，使用签名 opaque cursor
  分页并防重复 cursor/重复事件；后端没有 read/unread 状态，因此 badge、mark-read 和
  mark-all mutations 均已删除。
- NotificationKind 只接受 0..5，任意 payload 不进入翻译插值；只有后端当前真实产生的
  `awd_flag_injection_failed` 与 `awd_checker_callback_missing` 使用专门文案，其余合法
  kind 使用中性事件文案。
- 删除后端不存在的 AWD/KoH dashboard、GameHub path 与
  `ReceiveRoundStarted`/`ReceiveAttackLog`/`ReceiveKohUpdate`/`ReceiveScoreUpdate`。
  四种模式统一进入真实 Competition Detail；Scoreboard 只保留
  `/hubs/v1/competitions`、Join/Heartbeat、leaderboard/lifecycle 事件和 polling fallback。
- 最终 Frontend 103/103 tests、562 assertions passed；production build passed；新增/改动
  叶文件 lint 0，旧 `noctf.ts` 仍为既有 8 条、未恶化；JSON 与 `git diff --check` passed。

### 6.41 Submission/Runtime outcome 与认证限流契约（2026-07-31）

- 本地提交：`5660dc3 fix(api): publish submission and runtime outcomes`。
- SubmitFlag/SubmitFix 的业务 403/409 ProblemDetails 和空体 429 已进入 OpenAPI；四个
  Runtime mutation 的真实 503 ProblemDetails 也已公开。没有 producer 的
  `background_work_unavailable` 字符串映射已删除。
- ASP.NET RateLimiter rejection 现在明确为 429；Authentication 在 RateLimiter 前执行，
  partition 使用认证 subject+IP，不再把所有用户错误合并到 anonymous+IP。
- 新增 HTTP 回归证明不同 subject 配额独立且第 31 次请求返回空体 429；新增 outcome matrix
  架构测试固定 status/body media type/schema。
- Release solution build 0 warning/0 error，470/470 non-Integration passed；两份 OpenAPI
  artifact SHA-256 均为
  `5F1786A1AD1B13B7433B75FD39658C0A86B3DB1A2A0897CC3849680EBA171370`；
  生成客户端二次生成完全幂等，Frontend 103/103 tests 与 production build passed。

### 6.42 Challenge attachment 写入失败补偿清理（2026-07-31）

- 本地提交：`bb2881e fix(storage): clean up failed attachment uploads`。
- 对象存储 Put 成功后，只要 metadata Add 返回非 Added、抛数据库/其他异常或请求取消，
  都以 `CancellationToken.None` 对实际 `stored.ObjectKey` 做一次 best-effort 补偿删除。
- 补偿删除失败不会覆盖原本的业务拒绝、异常或取消；metadata Added 时绝不删除对象。
- 新增 6 项单元测试覆盖成功、两种拒绝、DbUpdateException、任意异常、请求取消及 cleanup
  自身失败；6/6 passed。

### 6.43 Participant Runtime 可见性边界（2026-07-31）

- 本地提交：`10d87a7 fix(runtime): enforce participant challenge visibility`。
- Participant runtime Find/Mutation 只接受 published CompetitionChallenge，并显式排除
  已软删除 Competition、CompetitionChallenge、Challenge template 和 Team。
- AWD target reader 使用同一边界，并通过 Challenge join 拒绝已删除模板；对手列表不再
  暴露软删除 Team。
- 新增真实 PostgreSQL 回归覆盖 unpublished/soft-deleted 五类 scope、Find、Reset 和
  targets 的拒绝，以及有效 AWD scope 正向行为；1/1 passed。

### 6.44 PatchUpload replacement 与 durable cleanup（2026-07-31）

- 本地提交：`44b90e4 fix(submissions): replace unconsumed patch drafts`。
- 恢复 `docs/storage-attachments.md`、`docs/submissions-rejudging.md` 和
  `docs/database.md` 已规定的行为：每队每题最多一个未消费 draft，新上传成功后原子替换
  旧行；已消费 archive 保持不可变并保留。
- Replacement 与 SubmitFix 使用同一个
  `(TeamId, CompetitionChallengeId, SubmissionKind.Fix)` PostgreSQL advisory lock；
  并发只能得到“旧 draft 被消费、新 draft 保留”或“旧 draft 被替换、旧 Id 不可消费”两种
  合法终态，不会产生唯一索引错误或清理已消费对象。
- 旧 ObjectKey 在替换事务内写入 Wolverine durable outbox，API 将 `CleanupObject` 路由到
  Worker；Worker 通过幂等 `IObjectStorage.DeleteAsync` 清理。新对象若数据库写入失败仍做
  best-effort 补偿删除，cleanup 失败不掩盖原拒绝。
- 新增 2 个真实 PostgreSQL+Wolverine+LocalObjectStorage 集成测试和 2 个单元测试；顺序
  replacement/consume、durable 删除和双方同时等待同一 advisory lock 均通过。
- 最终核验：Release solution build 0 warning/0 error；478/478 non-Integration passed；
  Integration 共发现 86 项，当前环境可执行的 84/84 passed、0 failed，真实 Kubernetes
  与 Libvirt 各 1 项因未提供目标环境而显式 skipped。原命令的
  `--minimum-expected-tests 86` 不把 skipped 计入最低执行数，因此返回的是 minimum-policy
  violation，而非测试或 teardown failure。EF 无 pending model changes；OpenAPI 导出
  134 个 endpoint 且两份 artifact 无漂移；`git diff --check` passed。

### 6.45 Runtime provider cleanup 与 capacity 收敛（2026-07-31）

- 本地提交：`a234ee6 fix(runtime): harden cleanup and team quotas`。
- Runner/Worker callback 现在使用带 RuntimeInstanceId、ProcessingVersion、Generation、
  RunnerPool 和 RunnerId 的强类型 cancel/termination/provision outcome；读取、provider
  清理、数据库回写和 replacement dispatch 均以完整 assignment identity fence 拒绝迟到
  或串错节点的消息。
- provider 已经开始创建资源后的失败会先清理精确 identity，再做 owner-checked Redis
  capacity release；正常 Stop 也只在 receipt cleanup 成功后释放。`OwnerMismatch` fail
  closed，不会把另一节点持有的预算归还。
- Stop/Reset 进入 `Stopping` 时清除旧 operation token。迟到的 provision success 会先
  持久化 receipt、推进 ProcessingVersion，再向原 Runner 发 typed Stop；不再丢失唯一可
  清理资源的 receipt。
- `Failed + receipt` 被视为仍持有不可信资源：Runner 保持 offline；独立 reconciliation
  逐条完成精确 provider cleanup 与 owner-checked capacity release 后，才清除 receipt 和
  assignment marker。单条失败不会回滚已经成功收敛的其他 assignment。
- 离线 `Provisioning + no receipt` 没有 cleanup 证据，必须保留原 assignment 和 Redis
  claim，不 release、不 redispatch。生产代码不再创建 `ReleaseRunnerCapacity`；
  handler 只作为 legacy durable-message tombstone，恒返回 `Superseded` 且无数据库、
  Redis、outbox 副作用。maintenance 只清除历史 stale token，不改变 ProcessingVersion。

### 6.46 Runtime replacement 与自动 provision 生命周期（2026-07-31）

- cleanup 失败会同时把直接排队等待的 replacement 标记为 `Failed/CleanupFailed`，避免新
  generation 永久停在 `Queued`；重放不会重复推进 terminal ProcessingVersion。
- Player/Admin Start 遇到历史 `Failed + receipt` ancestor 时，会先把该 ancestor 恢复到
  `Stopping` 并创建 linked replacement；cleanup 完成前不派发。任何历史 `Stopping`
  ancestor 都拒绝新的 Start；Reset 只接受 root `Queued`、`Provisioning`、`Running`，
  不接受正在等待 ancestor cleanup 的 queued replacement。
- AWD/KoH 自动 provision 遇到 `Stopping` 或 `Failed + receipt` 时返回
  `DeferredCleanup`，Worker 延迟 5 秒重试同一 durable provision message；只有 predecessor
  已 `Stopped` 的 replacement 才允许派发。cleanup failure 会留下可诊断 terminal 状态，
  不会提前启动新 generation。
- KoH 自动 provision 与 Admin mutation 共用完全相同的 shared-scope PostgreSQL advisory
  lock；Player/Admin/AWD 的 per-team 路径共用 team/competition lock。真实 PostgreSQL
  并发回归使用确定性 EF command barrier 固定 read/ack 与 lock race，不依赖
  `pg_stat_activity` 采样。

### 6.47 团队 Runtime 额度、方案 A API 与 AWD start gate（2026-07-31）

- 用户确认 `MaxConcurrentRuntimeInstancesPerTeam` 表示“同一队伍在同一比赛可同时占用的
  logical challenge Runtime slot 上限”：`Queued/Provisioning/Running/Stopping` 计入，
  同一 CompetitionChallenge 的 Reset 前后 generation 去重为一个 slot；`<= 0` 表示无限。
  AWDP disposable target 和 KoH shared Runtime 不占用该 per-team 额度。
- Create/Update Competition 按方案 A 把该字段作为必填、非 nullable `int32`；response
  同样返回非 nullable `int`。Player Start、team-scoped Admin Start 和 AWD 自动 provision
  都在同一事务 advisory lock 内检查并创建，跨入口竞争只能有一个成功，不会超额。
- AWD 草稿仍允许 challenge Runtime definition 为空；Competition Start 时才强制每个
  published AWD challenge 有可启动的合法 Runtime definition。正额度还必须至少容纳全部
  published AWD challenges，否则 start gate 返回强类型
  `RuntimeQuotaInsufficient`/`RuntimeDefinitionInvalid`。
- 最终门禁：Release solution build 0 warning/0 error；495/495 non-Integration passed；
  Integration 共发现 119 项，当前环境可执行的 117/117 passed、0 failed，真实 Kubernetes
  与 Libvirt 各 1 项因未提供目标环境而显式 skipped。若最低执行数错误设为 119，
  Microsoft Testing Platform 会因 skipped 不计为 executed 返回 minimum-policy 状态；
  测试报告本身仍为 passed，标准门禁应使用不高于 117 的最低执行数。
- EF `has-pending-model-changes` 明确无漂移，没有新增或手改 migration/snapshot。OpenAPI
  导出仍为 134 endpoints；两份 artifact 字节一致，SHA-256 均为
  `DF37E8152FBC3DBFE7C53F13DE2D9EE71BE4629C0243C3AD50054203F73D9749`。本轮未生成或
  修改 Frontend；协作者需从该 OpenAPI 重新生成强类型客户端。

### 6.48 CTF 排行榜一血、二血、三血（2026-07-31）

- 本地提交：`f9216b9 fix(leaderboard): expose first three bloods`。
- CTF 计分器原本已经按有效 solve 的 `ReceivedAt + SubmissionId` 给一血、二血、三血发放
  BloodReward；缺陷只在展示投影，旧 `BuildFirstBloods` 每题直接取 `.First()`，导致响应
  只有一血。本轮没有改计分公式、Submission/ScoringEvent 事实或数据库模型。
- Leaderboard 公共响应将语义错误的 `firstBloods[]` 改为 `bloods[]`；每项通过强类型
  `LeaderboardBloodRank.First/Second/Third`（OpenAPI 数值 1/2/3）携带 Team、slot 与时间。
  `subjects[].slots[]` 同步使用 nullable `bloodRank`/`bloodAt`，顶层与队伍题目槽位来自同一
  投影结果。
- 每题先按 TeamId 去重、取该队最早 Correct，再按 `ReceivedAt + SubmissionId` 排序取前三；
  同队重判/重复 Correct 不会占多个血位，第四名以后没有 blood rank。只有 CTF Flag solve
  eligible；AWD、AWDP、KoH 维持无 blood 语义。
- Redis leaderboard snapshot key 升为 `leaderboard:v2:{competitionId}:state`，新进程不会把
  旧 `firstBloods` JSON 当成当前契约；旧 key 按原 TTL 自然过期。新增真实 Redis 往返测试
  固定三个血位的序列化结果。
- 最终门禁：Release solution 与独立 E2E project 均 0 warning/0 error；497/497
  non-Integration passed；Integration 共发现 120 项，118/118 可执行项 passed、0 failed，
  Kubernetes/Libvirt 各 1 项因目标环境未提供而 skipped。EF 无 pending model changes。
  OpenAPI 仍为 134 endpoints，两份 artifact 字节一致，SHA-256 均为
  `AE632F758047EDEE3C37E59CE902BAEB5494BD77FF771747C5B280112DB0A1AB`。
- 本轮未改 Frontend，也未运行 Frontend SDK generator；协作者需要从新 OpenAPI 重新生成，
  将旧 `firstBloods` 消费改为 `bloods` 并在排行榜 UI 展示 First/Second/Third。

### 6.49 独立个人资料页与安全头像处理（2026-08-05）

- 功能提交：`809f825 feat(profile): add secure profile workspace`。本提交只在本地分支，
  未 push、未部署；本阶段没有必要递增运行版本。
- 首页已移除简介表单、文件输入和头像编辑器，只保留头像、用户名、简介、邮箱公开状态及
  命名路由编辑入口；完整资料编辑迁入需认证的 `/profile`。页面沿用 Pixel Industrial
  Card、边框、按钮、加载状态与中英文反馈，并把简介、头像、邮箱公开性和密码分区独立保存。
  简介与公开性写入互斥，避免并发请求用旧字段覆盖另一项。
- 用户回答 `$grill-me` 为 C/A/A，已固化为：本阶段桌面端只提供图片区域内滚轮缩放和拖动，
  不擅自增加移动端或纯键盘缩放控件；服务端上限为单边 `8192`、总像素
  `32,000,000`、只接受单帧；图像库采用 MIT 的 SkiaSharp `4.151.0`，没有引入
  ImageSharp 商业许可风险。移动端/纯键盘替代缩放仍是明确遗留的产品决策。
- 头像编辑器已完全删除缩放/X/Y 滑槽。滚轮以指针位置为锚点并限制 `1x..4x`；拖动使用
  Pointer Events、pointer capture、grab/grabbing 和边界钳制；90°/270° 旋转会交换宽高
  重算 cover scale。预览和最终 `512x512` 导出调用同一变换函数，优先导出 WebP、失败时
  回退 PNG。
- 服务端头像不信任扩展名、Content-Type 或 magic bytes：SkiaSharp 完整解码 JPEG/PNG/WebP，
  在像素分配前检查尺寸和总像素，并结合 codec 与 APNG/WebP 容器标记拒绝多帧、截断、
  畸形和不支持格式；随后重绘为无原始元数据的 `512x512` WebP，只保存标准化对象。
  服务端上传文件上限维持 3 MiB，multipart request 上限为文件上限加 64 KiB，并新增 subject/IP
  维度每分钟 10 次限流。替换仍先切数据库对象键，再 best-effort 删除旧对象或失败新对象。
- `User.IsEmailPublic` 默认 `false`；EF CLI 生成
  `20260804185915_AddUserEmailVisibility`，未手改 migration 或 snapshot。匿名化注销会强制
  清除此字段。本人和 Administrator 始终读取邮箱；匿名或其他用户只有在目标主动公开时
  才能从新增 `GET /api/v1/users/{userId}` 得到邮箱。
- 强类型契约变化：`CurrentUserResponse` 增加 `isEmailPublic`；
  `UpdateMyProfileRequest` 必填 `isEmailPublic`；头像 400 使用 `SizeInvalid`、
  `SourceMetadataMismatch`、`UnsupportedFormat`、`InvalidDimensions`、
  `PixelLimitExceeded`、`MultipleFrames`、`MalformedImage`；当前密码错误使用稳定 409
  `CurrentPasswordInvalid`。改密在同一 SaveChanges 中更新哈希并递增 TokenVersion，204 前
  删除当前 Refresh Cookie；前端成功后清空 query/auth/score 状态并回到命名登录路由。
- OpenAPI 已重新导出并生成 TypeScript SDK；公开路由清单精确计数为 152。两份 OpenAPI
  artifact SHA-256 均为
  `BD2D1DF5590761EF531C2A3852C58C176C295D160A93021D41666EB6D4ED0A04`。
- 最终验证：solution Debug build 0 warning/0 error；545/545 non-Integration passed；
  `UserProfilePersistenceTests` 2/2 passed，覆盖邮箱公开性持久化及旧 Access/Refresh version
  在改密后失效；Skia 真实图片测试 8/8 passed；EF 10.0.4
  `has-pending-model-changes` 返回无漂移。Frontend 目标测试 11/11、scoped ESLint、
  `vue-tsc` 与 Vite production build passed。全量 Frontend lint 仍有仓库既存的 9,109 项
  generated/vendor/旧文件基线错误，本阶段触及文件 scoped lint 为 0。
- 全量 Integration 发现 134 项：并发运行结果为 130 passed、2 skipped、2 failed；失败是
  KoH advisory-lock 等待者采样和 Docker 本机端口可达性在全套高并发下超时。两项原命令
  随后各自独立重跑均 1/1 passed，本阶段关键 Profile PostgreSQL 用例另行 2/2 passed。
- 使用内置浏览器连接隔离的本地 PostgreSQL/Redis/API/Vite 完成实际验收：简介和公开性
  分别保存并持久化、首页只剩摘要、头像滚轮从 100% 到 157%、Pointer 拖动无控制台错误、
  旋转后裁剪上传成功、三组密码明文切换可用。未执行最终改密提交，Token 失效由上述集成与
  HTTP 测试覆盖。

### 6.50 MailKit SMTP 投递与显式 TLS 模式（2026-08-05）

- 功能提交：`dbf534a feat(email): migrate SMTP delivery to MailKit`。本提交只在本地分支，
  未 push、未部署；平台信息当前取程序集元数据，仓库没有需要随本纵切递增的显式运行版本。
- 用户通过 `$grill-me` 回答 A/A，已固化为：TLS 使用强类型 `SmtpSecurityMode`，只允许
  `None`、`SslOnConnect`、`StartTls`；SMTP 用户名允许留空，留空时跳过认证且不要求密码。
  没有使用端口自动推断新配置，也没有把有界安全模式继续建模为布尔值或字符串。
- Domain、Application、Infrastructure 和协议契约现在共同使用显式 TLS 枚举。平台邮箱配置
  API 将 `smtpEnableSsl: bool` 替换为 `smtpSecurityMode` 字符串枚举，JSON 枚举转换器拒绝
  整数。旧数据库行仍由保留的 `SmtpEnableSsl` 列兼容解析：`true + 465` 推导
  `SslOnConnect`，其他 `true` 推导 `StartTls`，`false` 推导 `None`；新保存同时写入显式
  nullable 枚举列并同步 legacy bool，避免已有生产配置在迁移后改变投递语义。
- EF CLI 生成 `20260805022318_AddSmtpSecurityMode` 及 designer/snapshot，未手改 migration
  或 snapshot。MailKit `4.17.0` 在 `Directory.Packages.props` 集中锁定且只由
  Infrastructure 引用；投递已从 `System.Net.Mail` 改为 MimeKit `MimeMessage`、
  `BodyBuilder` 和 MailKit SMTP client，同时产生 UTF-8 text/plain 与 text/html 正文。
- Connect、Authenticate、Send、Disconnect 均为 async，透传调用方取消并受配置的总超时
  约束。连接、认证、拒收、超时和传输失败映射为强类型
  `EmailVerificationDeliveryFailure`；对外异常使用固定消息、不携带原始 inner exception，
  API 只返回固定 503 problem 与失败枚举，Worker 只按受控投递异常重试，避免密码、Token 或
  SMTP 凭据通过日志、ProblemDetails、死信或平台日志泄漏。
- 管理前端改为显式安全模式选择器；无认证模式会说明用户名可选，用户名为空时不要求密码，
  测试邮件按钮的可用性也使用同一规则。OpenAPI 已重新导出并生成 TypeScript SDK，前端没有
  手写 DTO、枚举、失败码或端点路径。两份 OpenAPI artifact 字节一致，共 153 endpoints，
  SHA-256 均为
  `449CBE007AE53F4A3301977AE65B337E8F7186BA71D92D775DEDD087505DE647`。
- 最终验证：solution build 0 warning/0 error；547/547 non-Integration passed；真实
  Mailpit `1.30.0` STARTTLS、真实 GreenMail `2.1.11` implicit TLS/认证/无认证/认证失败/MIME
  正文，以及 loopback SMTP 的超时、取消、拒绝连接和 550 拒收目标测试全部通过；SMTP
  Integration 3/3、配置持久化 1/1 passed。全量 Integration 共 137 项，135 passed、
  0 failed、2 skipped；Kubernetes 与 Libvirt 各因目标环境未配置而显式 skipped。
- EF `has-pending-model-changes` 返回无漂移；Frontend `bun test` 157/157、scoped ESLint、
  `vue-tsc` 和 Vite production build passed；全量 lint 仍有仓库既存的 9,437 项
  generated/vendor/旧文件基线问题，本纵切触及文件 scoped lint 为 0。`git diff --check`
  passed。没有进行浏览器生产验收，因为本阶段明确禁止 push/deploy，且 SMTP 协议行为已由
  上述真实服务集成测试覆盖。

### 6.51 排行榜冻结、黑灯与 BOT 常规播报边界（2026-08-05）

- 功能提交：`bf3d393 feat(scoring): add leaderboard freeze and blackout`。本提交只在本地
  分支，未 push、未部署；没有需要递增的显式平台运行版本。
- 用户通过 `$grill-me` 回答 AAAAA，并补充确认：管理员可立即覆盖或设置定时生效；比赛
  Finished 自动、幂等恢复 Normal 并公开最终实时榜单；Frozen 使用生效时刻的精确历史计分
  事实；Administrator、Owner、Manager、Judge、Observer 人类在 Frozen/Blackout 均读实时
  数据；Observer Bot 在 Frozen 读实时数据，在 Blackout 只能得到 Hidden。黑灯隐藏分数、
  解出数、一二三血与排行榜，但题目、附件、Runtime、提交和本人提交结果继续可用。
- Domain 使用单一 `CompetitionLeaderboardVisibility` 枚举（Normal/Frozen/Blackout），没有
  组合布尔值。Competition 仅增加配置、修订、生效时间和有界 Frozen snapshot JSON 列；
  生命周期审计沿用 Competition aggregate 的一个子表，没有新增独立业务根或大量数据表。
  EF CLI 生成 `20260805033207_AddCompetitionLeaderboardVisibility` 及 designer/snapshot，
  未手改 migration 或 snapshot。
- Application/Infrastructure 在 serializable transaction 与 competition advisory lock 内再次
  校验 schedule 和 revision。定时 Wolverine message 带 visibility revision fence，旧消息重放
  无副作用；Frozen 即使延迟执行也按原定 cutoff 重建历史 snapshot；切换状态会原子递增
  LeaderboardRevision、发布 invalidation 并清理旧缓存。Finish 会写自动揭榜审计、清除冻结
  snapshot/schedule 并使最终 live board 立即可重新投影。
- 新增强类型管理契约：
  `GET/PUT /api/v1/admin/competitions/{competitionId}/leaderboard-visibility`。PUT 的
  `visibility` 和 `expectedRevision` 在 OpenAPI 中为必填；非法定时返回 typed 400 Problem，
  revision/Finished 冲突返回 typed 409。排行榜响应增加 `visibility`、`dataScope`、
  `dataAsOf`；题目列表/详情增加 visibility/scope，Hidden 时 `baseScore=null`。所有接口仍使用
  FastEndpoints 8.2.0 强类型基类、`ExecuteAsync`、`Results<T...>` 与 `TypedResults`。
- 前端已从同一 OpenAPI 重新生成 TypeScript SDK，并通过现有薄封装调用新操作；没有写死 URL、
  `/api/v1`、DTO、枚举或失败码。管理员比赛设置增加 Pixel Industrial 风格的 Normal/Frozen/
  Blackout 即时/定时面板；比赛页和排行榜分别展示冻结 cutoff、可信角色实时视图或黑灯空态，
  Hidden 不会被解释为零分，题目弹窗也不会显示空分值。平台审计页可显示可见性变更。
- QQBOT 契约明确：`dataScope=Hidden` 时停止 `/rank`、定时排名、分数、解出数、血榜和其他
  常规战况播报，不能播报“全员零分”；通知 feed cursor、平台通知和比赛通告继续正常消费，
  不受黑灯影响。下一次读到 `dataScope=Live` 后恢复常规播报。
- OpenAPI 导出共 154 条文档化 v1 路由（另有 `GET /health`，总计 155 endpoints）；两份
  artifact 字节一致，SHA-256 均为
  `F246AD781D60246550A83FB206F0C833DDCCDA0006F9A48507E21A694BC0BAAC`。TypeScript SDK
  连续生成字节稳定。
- 最终验证：solution Debug build 0 warning/0 error；555/555 non-Integration passed；全量
  Integration 共 141 项，139 passed、0 failed、2 skipped，只有未配置
  `NOCTF_KUBERNETES_INTEGRATION` 与 `NOCTF_LIBVIRT_DISK_PATH` 的目标环境用例显式跳过。
  Frozen 历史 Redis/PostgreSQL、visibility persistence/revision/replay/Bot/finish reveal 和通知
  feed 持久化用例均通过。EF `has-pending-model-changes` 无漂移，`git diff --check` passed。
- Frontend production build passed，`bun test` 158/158，触及的非生成文件 scoped ESLint
  0 warning/0 error。全量 lint 仍被仓库既有 generated/mock/config 基线阻塞：9,589 项
  （9,316 errors、273 warnings），未运行全局 `--fix`，避免污染用户和协作者文件。
- 使用内置浏览器连接隔离 PostgreSQL/Redis、当前 API 源码和 Vite 完成真实验收：管理员可保存
  Blackout，配置与生效 badge 同步并出现成功 toast；普通参赛者得分显示 `-`，比赛页显示黑灯
  说明，排行榜显示专用 Hidden 空态；控制台 0 error。隔离容器与本地进程已清理。

### 6.52 默认私密的比赛咨询与可选公开回答（2026-08-05）

- 功能提交：`8dca6ef feat(questions): add private competition support`。本提交只在本地分支，
  未 push、未部署；本阶段没有需要递增的显式平台运行版本。
- 用户通过 `$grill-me` 完成七项产品决策并补充最终语义：咨询默认且通常保持私密，用于选手
  向出题人或比赛管理员询问题目或平台问题；需要面向全场公开的内容，产品主路径应使用 Hint
  或平台公告。后端仍保留显式公开回答接口，但前端不把公开对话当作常规工作流。
- Domain 只新增 `CompetitionQuestion` 与有序审计式 `CompetitionQuestionEntry` 两个模型，
  对应 `competition_questions`、`competition_question_entries` 两张表，没有建立通用消息、
  附件或会话 dumping ground。EF CLI 生成
  `20260805045027_AddCompetitionQuestions` 及 designer/snapshot，未手改 migration 或
  snapshot。
- 问题使用强类型 `Challenge`/`Platform` subject。题目咨询必须关联
  CompetitionChallenge；平台咨询只关联 Competition。每个问题固化提问 Team/User，可选关联
  Submission；v1 明确不支持附件，正文提示不得填写密码、Token、SMTP 凭据或 Flag。
- 新问题只允许在 Running/Paused 比赛创建；Finished 后只读。状态为
  Pending/Replied/Resolved/Closed；提问者在 Resolved 后继续追问会重新打开问题，Closed 为
  终态。创建与追加消息应用 subject/IP 维度每分钟 8 次限流，Bot 身份被服务端拒绝。
- 私密处理权限包含比赛 Administrator、Owner/Manager/Judge，以及题目模板 Owner/Manager；
  Observer 只读。处理者可见提问者和队伍身份；显式公开投影会匿名化提问者与队伍，不把私密
  上下文泄露给其他参赛者。
- 六个强类型 FastEndpoints 契约已加入：
  `POST/GET /api/v1/competitions/{competitionId}/questions`、
  `GET /api/v1/competitions/{competitionId}/questions/{questionId}`、
  `POST .../{questionId}/messages`、`PUT .../{questionId}/status` 和
  `PUT .../{questionId}/publication`。所有端点使用 `ExecuteAsync`、最小
  `Results<T...>` 与 `TypedResults`；业务规则在 Application use case，Endpoint 只负责
  认证、授权、限流与传输映射。
- 新问题、处理者回复和状态变化通过 Wolverine transactional outbox 向明确的人类收件人投递
  永久通知，使用稳定 NotificationKind 与幂等键；通知投影只使用受控字段。用户删除影响预览
  计入问题及消息历史，避免硬删除破坏咨询审计关系。
- OpenAPI 已重新导出并生成 TypeScript SDK；共有 161 个注册端点，其中 160 个为文档化 v1
  路由，另有 `GET /health`。两份 OpenAPI artifact 字节一致，SHA-256 均为
  `FE9BCDEF738A3E792DF6A84A89F7AFEFA81AD6C8BB3CE2575C2FCB4F131F6A21`。
  SDK 连续生成字节稳定：index
  `166B6BC2B9F25A8B2FDE0B885988E245CA032E307DBE6C027DBD3930F9251EAB`，
  sdk `4A2562E144F891472047CCE61B9531A9D515F7731BF4532836E048A25F0D9440`，
  types `40940703794B9B324FFD27BBDED45F78FE6E26D99273CC1C981D1EF3AA8AE0F6`。
- 前端只通过生成 SDK 和 `questionApi` 薄封装调用接口。比赛工作区增加“平台咨询”，题目弹窗
  增加“咨询出题人”；两处均显示“默认私密”、敏感信息警告及使用 Hint/平台公告公开回答的
  引导，保持 Pixel Industrial 风格与中英文文案。
- 浏览器验收同时发现旧 `awdp-screen` 命名路由已在历史提交中删除，但比赛工作区仍渲染
  RouterLink，导致任意响应式更新抛错并表现为标签页/按钮无响应。本提交删除该废弃入口，没有
  复活旧方案，并增加源契约回归测试防止重新引入。
- 最终验证：solution build 0 warning/0 error；569/569 non-Integration passed；HTTP 咨询限流
  1/1 passed；EF `has-pending-model-changes` 无漂移（本机全局 EF CLI 8.0.11 对 runtime
  10.0.4 仅给出版本提示）。真实 PostgreSQL 咨询持久化/权限/公开匿名化/outbox/幂等目标用例
  在全套后独立重跑 1/1 passed。
- 全量 Integration 共 142 项：138 passed、2 skipped、2 failed。Skipped 为未配置
  Kubernetes 与 Libvirt 目标环境；两项失败均是仓库既有高并发 advisory-lock waiter 采样
  不稳定：KoH 期望 2、观测 1，Patch/Fix 期望 2、观测 0，不涉及本阶段咨询模型。关键咨询
  PostgreSQL 用例随后独立重跑通过。
- Frontend production build passed，`bun test` 162/162，咨询、通知与回归文件 scoped
  ESLint 0 warning/0 error，`git diff --check` passed。全量 ESLint 仍有约 9,993 项
  generated/vendor/旧文件基线问题，未执行全局 `--fix`。
- 使用内置浏览器与显式启用的临时本地 Mock 完成实际验收：平台私密咨询可创建并进入详情；
  题目弹窗正常展示“咨询出题人”、已有私密问答与 Hint/公告引导；控制台 0 error。临时 Mock、
  浏览器标签和本地 Vite 监听均已清理，仓库没有残留验收数据。

### 6.53 单比赛不可变事件日志（2026-08-05）

- 功能提交：`dbccb72 feat(events): add immutable competition event log`。本提交只在本地分支，
  未 push、未部署；本阶段没有需要递增的显式平台运行版本。
- 用户通过 `$grill-me` 回答 `AAAAAA`，确认使用单一不可变 `competition_events` 表；事件随
  Competition 永久保留，不提供独立删除；查询按公开参赛者事件、本队事件和工作人员安全摘要
  分层；Administrator/Owner/Manager 可执行有界 JSONL 导出；用户匿名化注销保留历史关系；
  完整提交 Flag 只能由 Administrator/Owner/Manager/Judge 通过单独受审计端点读取，Observer
  明确禁止。
- Domain/Application 增加强类型 CompetitionEvent kind/level/visibility、append-only draft、查询
  与 Flag 访问用例。事件可关联操作用户、相关用户、队伍、比赛题目、Hint、Runtime、Submission
  和咨询，并以有界枚举承载生命周期、排行榜可见性、提交、计分、Runtime 与咨询状态；普通事件
  不持久化 Flag、Token、密码、邀请令牌或 SMTP 凭据。Hint 使用独立 `HintId`，同一题同一时刻
  发布多条 Hint 仍可逐条追溯。
- `NoCtfDbContext` 在普通 SaveChanges 路径拒绝更新或删除 CompetitionEvent；所有外键均为
  Restrict。比赛 hard delete 检查事件依赖，用户删除预览计入 Actor/RelatedUser 引用，因此有
  历史时只能匿名化并注销，不会破坏成绩、提交、咨询或事件关系。EF CLI 生成
  `20260805104614_AddCompetitionEvents` 及 designer/snapshot，未手改 migration 或 snapshot。
- 新增强类型 FastEndpoints 契约：
  `GET /api/v1/competitions/{competitionId}/events`、
  `GET /api/v1/admin/competitions/{competitionId}/events/export`、
  `POST /api/v1/admin/competitions/{competitionId}/submissions/{submissionId}/flag-access`。
  查询使用签名 keyset cursor，单次最多 200，时间跨度最多 31 天；导出最多 50,000 条 JSONL。
  Endpoint 均使用 `ExecuteAsync`、具体 HttpResults、`Results<T...>` 与 `TypedResults`，权限和业务
  规则留在 Application/Infrastructure use case。
- Competition/Challenge/Hint/Team/Submission/Scoring/Blood/Runtime/Port/Question 的关键写路径在
  原事务中追加事件。API、Worker、Runner 都将 `CompetitionEventCommitted` 通过 Wolverine
  PostgreSQL durable queue 路由到 Worker；Worker 只向 Redis 发布事件 ID/type/level/time 的
  刷新提示，API SignalR relay 再通知比赛组。前端永远用 generated SDK 重取服务器授权投影，
  不把实时 payload 当作事实或权限来源。
- OpenAPI 共注册 164 个端点，其中 163 个为文档化 v1 路由，另有 `GET /health`。两份 artifact
  字节一致，SHA-256 均为
  `7BABA1EF5D99305415913B9DAF898FAE743B801371160F8B720B2AFDCA4F4E92`。TypeScript SDK
  连续生成字节稳定：index
  `CD4E791C229282BD328944EBB7B9B542D97A4E432FD10A9C504870E72D80503B`，sdk
  `FF961C0B9FA14BC5445DD21F334F4994AFABBB84F6F0E3C01E8DD89FBCE36F34`，types
  `BDE416BF287FE2EC6FE5FB86EC39D0DB3221A5532860C739DFF815B722E3005B`。
- 前端比赛工作区新增第四个“比赛日志”标签，提供 24 小时默认范围、类型/最低级别/队伍/用户/
  比赛题目/Runtime 筛选、签名分页、15 秒降级轮询、SignalR 刷新、权限驱动 JSONL 导出和受审计
  Flag 查看。Flag 只存在于打开的对话框内，关闭即清空，不写 local/session storage；网络层只
  调用生成 SDK/现有薄封装，没有手拼 URL、路径、DTO、枚举或失败码。
- 最终验证：Release solution build 0 warning/0 error；569/569 non-Integration passed；真实
  PostgreSQL 的事件权限、分页、导出、脱敏、Flag 审计、HintId、outbox 和 append-only 目标用例
  独立通过。全量 Integration 共 143 项：140 passed、2 skipped、1 failed；skipped 仍是未配置
  Kubernetes/Libvirt 目标环境，failure 是既有 KoH advisory-lock waiter 在全套并行容器压力下
  期望 2、观测 1，随后低干扰独立重跑 1/1 passed。EF pending-model 检查无漂移，
  `git diff --check` passed。
- Frontend `bun test` 165/165、scoped ESLint 0 warning/0 error、`vue-tsc` 与 production build
  passed。仓库级 ESLint 仍被 generated/vendor/config/旧文件的大量既存基线阻塞，没有执行全局
  `--fix`，避免污染用户与协作者修改。
- 使用内置浏览器将本地未部署前端代理到现网已有数据完成实际验收：管理员登录、进入真实比赛、
  打开第四个“比赛日志”标签、展开高级筛选、检查实时 badge、筛选布局和错误态均正常，控制台
  只有既有 Three.js deprecation warning。现网尚无本提交的新 API，列表出现预期 404 错误态；
  数据行、导出、权限与 Flag 对话框由上述生成契约、前端测试及真实 PostgreSQL 用例覆盖。本地
  Vite 监听和浏览器验收标签已清理，没有修改或部署现网。

### 6.54 CTF 与 AWDP 跨队 Flag 反作弊（2026-08-05）

- 功能提交：`c11328e feat(submissions): add cross-team flag adjudication`。本提交只在本地分支，
  未 push、未部署；本阶段没有需要递增的显式平台运行版本。
- 用户通过 `$grill-me` 回答 `AAAAAA`，确认六项产品决策：不新增反作弊业务表，使用关联
  `ScoringEventId` 的 append-only `CompetitionEvent` 推导处理状态；Administrator、Owner、
  Manager 可最终确认封禁或更正，Judge 可查看证据和驳回，Observer 只看脱敏列表；封禁和更正
  只发布比赛范围的通用通知，工作人员理由保持私密；驳回静默且理由至少 8 字符；更正必须填写
  理由、解除封禁并发布通用更正；重判只 supersede 未处理事件，若仍命中则产生新的未处理事件，
  检测通知以 SubmissionId 幂等，已确认事件只有显式更正才会失效。
- CTF Flag 与 AWDP Break 现在会在同一比赛题目内精确匹配其他活跃队伍的动态 Flag。唯一命中
  时产生 `ForeignTeamFlagDetected` 和 victim team；多队同值时产生不暴露 victim 的 ambiguous
  事件。自队 Flag、公开模板 Flag 和正确 Flag 仍走原有评分；AWD 行为没有改变。普通选手的
  submission status/list 将两种内部判定统一投影为普通 Wrong，不返回 victim、内部失败码或
  反作弊状态，避免把接口变成 Flag 探测器。
- Processor 在评分事务内追加 `CheatIncidentDetected` 事件并通过 Wolverine 发送仅含 ID 的消息；
  日志不包含 Flag。Worker 只通知 Administrator、Owner/Manager/Judge，使用稳定幂等键。新增
  `Pending/Confirmed/Dismissed/Superseded/Corrected` 强类型状态，以及检测、封禁、更正对应的
  CompetitionEvent/Notification kinds；完整 Flag 只在单独、`no-store`、会写审计的详情读取中
  返回，Observer 明确禁止。
- 新增强类型管理契约：
  `GET /api/v1/admin/competitions/{competitionId}/cheat-incidents`、
  `GET /api/v1/admin/competitions/{competitionId}/cheat-incidents/{scoringEventId}`、
  `POST .../{scoringEventId}/dismiss`、`POST .../{scoringEventId}/confirm`、
  `POST .../{scoringEventId}/correct`。列表使用签名 cursor 和有界时间/队伍/用户/题目/状态筛选；
  Endpoint 全部使用 FastEndpoints 8.2.0 强类型基类、`ExecuteAsync`、具体 HttpResults、
  `Results<T...>` 与 `TypedResults`，裁决、权限和事务规则位于 Application/Infrastructure。
- 确认操作在同一 serializable transaction 和 competition advisory lock 内再次验证状态、封禁
  来源队伍并追加不可变事件；更正只允许处理已确认事件，解除由该事件导致的封禁并追加更正。
  rejudge 会锁定旧 ScoringEvent 并只 supersede Pending 事件。没有修改既有 ScoringEvent 事实，
  也没有新增反作弊表。EF CLI 生成
  `20260805122032_AddCheatIncidentAdjudication` 及 designer/snapshot，未手改 migration 或 snapshot。
- OpenAPI 已重新导出，共 169 个注册端点，其中 168 个为文档化 v1 路由，另有
  `GET /health`；Admin 路由为 109 条。两份 OpenAPI artifact 字节一致，SHA-256 均为
  `80522F48974B1EB39B5795C92B43AFBEFBE6E4C46FE478B6E5D485398CE34097`。TypeScript SDK
  连续生成字节稳定：index
  `1FADC204B3CD159AE59E6A93A8826862D6B9D1E2B38A3439992DE62E09508846`，sdk
  `0EA4CA102258164F107DBAE206BC20C2EDAF8B89C0CA3C76FB0FB20DF1A0F0E1`，types
  `C99D6CE16DB1BCBD0B5AAB19617069366AA0636791EB041F5456B0EBC148E615`。
- 前端仅通过生成 SDK 和 `cheatIncidentApi` 薄封装调用接口。比赛管理详情在“队伍”和“实例”
  之间增加“反作弊”标签，包含未处理计数、完整筛选、签名分页、15 秒轮询和 SignalR 刷新、
  脱敏列表、显式受审计证据读取/复制，以及按角色展示的驳回、确认封禁和更正操作。证据仅存在
  当前组件内存，关闭立即清空，不写 browser storage。最后修复 TabsContent/table 的 intrinsic
  width，使窄侧栏布局不再产生整页横向溢出，保持 Pixel Industrial 风格和完整中英文状态。
- 后端验证命令与结果：`dotnet build backend/NoCTF.slnx --no-restore` 和 Release build 均为
  0 warning/0 error；TUnit non-Integration 584/584 passed；真实 PostgreSQL 目标用例
  `CheatIncidentPersistenceTests` 1/1、
  `SubmissionProcessingLeaderboardRevisionPersistenceTests` 3/3、
  `CompetitionNotificationDeliveryPersistenceTests` 3/3 passed；
  `dotnet ef migrations has-pending-model-changes` 无漂移（全局 EF CLI 8.0.11 对 runtime 10.0.4
  仅提示版本较旧）；OpenAPI 导出和 SDK 二次生成字节稳定；`git diff --check` passed。
- 前端最终验证：`bun test` 169/169、触及文件 scoped ESLint 0 warning/0 error、`vue-tsc` 与
  production `bun run build` passed。构建只有依赖 PURE annotation 与既有大 chunk 警告。
- 使用内置浏览器代理到隔离本地 mock 完成实际验收：列表、筛选、未处理 badge、证据展开与复制、
  权限按钮、理由长度门禁、确认弹窗和窄宽布局均正常；文档宽度与 viewport 一致。控制台只有 mock
  未实现 Competition SignalR negotiate 的预期 404（15 秒 polling fallback 正常）和既有
  Three.js deprecation warning。浏览器标签、三个本地监听和临时 mock 文件均已清理。

### 6.55 管理员平台日志保留、审计投影与导出（2026-08-05）

- 功能提交：`567ed02e feat(observability): enforce platform log retention`。本提交只在本地
  分支，未 push、未部署；平台信息读取程序集元数据，仓库没有需要随本纵切递增的显式运行版本。
- 用户确认三类日志边界：单比赛历史继续写 PostgreSQL 不可变 `competition_events`，永久保留、
  禁止修改/自动清理，历史事件阻止比赛物理删除；管理审计只投影工作人员可见比赛事实与用户生命
  周期审计，不复制事实、不设置 TTL；API、Worker、Runner 诊断日志使用每日 UTC Redis Stream
  分片，固定保留 14 天、每日精确最多 50,000 条，到期删除且不归档。可信 Administrator 可查看
  Flag，密码、Token、Cookie、Authorization、SMTP 凭据与部署秘密继续强制脱敏。
- 用户通过 `$grill-me` 回答 `AAA`，确认运行日志导出为 JSONL，单次最多 50,000 条且只能覆盖
  14 天保留窗口；关联筛选分别使用 Competition、RuntimeInstance、Team、User、
  CompetitionChallenge、Submission 六类强类型 ID；全文检索对 category、event、message、
  exception 执行有界、大小写不敏感匹配。分类筛选为大小写不敏感精确匹配。
- `RedisPlatformLoggerProvider` 写入 `platform-logs:v2:yyyyMMdd`，`XADD MAXLEN` 精确限制每日容量，
  shard 过期时间固定为分片日加 14 天；实时 pub/sub channel 保持兼容。读取端跨保留期分片倒序
  查询，使用 filter-bound HMAC opaque cursor，增加读侧二次脱敏；导出沿用同一筛选和脱敏规则，
  生成 `application/x-ndjson` 流。没有新增 PostgreSQL 表或 EF migration。
- 管理审计不再读取 legacy Competition lifecycle/leaderboard audit child collection，而是直接投影
  append-only CompetitionEvent 与 UserAccountLifecycleAudit。比赛生命周期、排行榜可见性、普通
  比赛事件和账号生命周期为四类强类型视图；比赛事件同时保留相关用户、队伍、题目、Runtime、
  Submission、ScoringEvent、咨询和强类型状态字段。签名 keyset cursor 支持跨两类事实稳定翻页；
  普通比赛事件筛选不会混入已单列的生命周期或排行榜事件。
- 新增强类型 FastEndpoint：
  `GET /api/v1/admin/platform/logs/export`；既有 logs/audit-list 契约增加完整筛选、签名 cursor、
  关联字段和 `nextCursor`。Endpoint 使用 `ExecuteAsync`、具体 HttpResults、`Results<T...>` 与
  `TypedResults`，导出规则位于 Application use case。OpenAPI 共注册 170 个端点，其中 169 个
  为文档化 v1 路由，Admin 路由 110 条。两份 artifact 字节一致，SHA-256 均为
  `4473E43FAF6E052EFFF87DA827432D3649DB9FE76C8582FE1F462FEA0E3A2E5E`；SDK 二次生成
  字节稳定：index `F454659242EDC0633E533CA47D0AC0F6BF5DB153D2B80B0126B1EE17B6B39E65`，
  sdk `DF68CD03C2BC8E6E28143B93C0E6A85E2DA5E16272C8105EDE38426451805DC0`，types
  `3D2B076D96E1E3CA545D609676C4ECCC303E22658B9CE3FB41842FC674B3E4E1`。
- 前端仅通过 generated SDK 与 `platformLogsApi` 薄封装调用接口。平台日志工作台默认 Warning、
  最近 24 小时，补齐分类/全文和六类关联 ID 筛选、同语义实时过滤、14 天/每日 5 万条提示、
  JSONL 导出、管理审计四类筛选与签名 cursor“加载更早”。比赛事件名称映射抽为复用展示模块，
  保持 Pixel Industrial 风格、中英文文案和无整页横向溢出。
- 后端验证：Debug solution build 0 warning/0 error；Redis 真实依赖 3/3、PostgreSQL 审计投影
  1/1、signed opaque cursor 1/1、Admin OpenAPI rules 2/2、route drift 1/1 passed；EF pending
  model 无漂移（全局 EF CLI 8.0.11 对 runtime 10.0.4 仅提示版本较旧）；OpenAPI 导出、SDK
  二次生成和 `git diff --check` passed。前端 scoped `bun test` 6/6、scoped ESLint 0 warning/
  0 error、`vue-tsc` 与 production `bun run build` passed；构建只有既有依赖 annotation 与大
  chunk 警告。
- 使用内置浏览器连接一次性隔离 PostgreSQL/Redis/API/Vite 完成实际验收：管理员登录、真实
  SignalR 连接、默认 Warning、完整筛选字段、大小写不敏感全文检索（6 条缩至唯一匹配）、审计
  四类选项、导出动作及 1024/768 宽度均正常，document scrollWidth 等于 viewport，控制台零
  error。JSONL body、50,000 上限和脱敏由真实 Redis 集成用例验证；浏览器控制器未捕获由 Blob
  anchor 触发的 download 事件。验收标签、临时配置、监听进程和两个一次性容器均已清理，未触碰
  既有 `deploy-*` 数据或服务。

### 6.56 安全忘记密码与邮箱重置（2026-08-06）

- 功能提交：`03647e7f feat(auth): add secure password recovery`。本提交只在本地分支，未 push、
  未部署；平台信息读取程序集元数据，仓库没有需要随本纵切递增的显式运行版本。
- 用户通过 `$grill-me` 回答 `AAAAA`，确认五项产品与安全决策：只新增一个
  `password_reset_tokens` 表，业务表仅保存 SHA-256，Token 单次使用且可撤销；申请接口只接收
  email，所有合法 email 形状统一返回 generic 202；重置不依赖“注册邮箱验证”开关，只允许
  Active、已验证邮箱的 Human，Bot/Banned/Disabled/Anonymized 均不处理；默认 Token 30 分钟、
  同账号 60 秒冷却且每小时最多 3 次、同 IP 15 分钟最多 5 次，新 Token 使旧 Token 失效；完成
  后不自动登录，而是在同一事务修改密码、消费 Token、使其余 Token 失效、递增 TokenVersion，
  清除 refresh cookie，发送密码已变更通知并由前端返回登录页。
- `PasswordResetStore` 使用 PostgreSQL per-user advisory transaction lock 固定并发单赢家；Token
  由 CSPRNG 生成，比较使用 fixed-time equality，外键为 Restrict。请求不存在账号、账号不可用、
  账号限流或 SMTP 未配置时都不向匿名调用方暴露内部状态。Wolverine durable message 只在投递
  边界携带原始 Token，`ToString()` 强制脱敏，普通日志只记录 UserId。密码变更邮件即使注册邮箱
  验证开关关闭也会发送。
- EF CLI 生成 `20260805162117_AddPasswordResetTokens` 及 designer/snapshot，未手改 migration 或
  snapshot；账号物理删除/匿名化前显式清理 reset token，不改变历史业务数据的 Restrict 语义。
  `dotnet ef migrations has-pending-model-changes` 返回无模型漂移。
- 新增强类型 FastEndpoints 契约：
  `POST /api/v1/auth/password-reset/request` 返回 generic typed 202 body `{ accepted: true }`，IP 限流
  429 进入 OpenAPI；`POST /api/v1/auth/password-reset/complete` 返回 204 或稳定 typed 400
  `InvalidOrExpired`。Endpoint 使用 `Endpoint<TRequest,TResponse>`、`ExecuteAsync`、具体
  HttpResults、`Results<T...>` 与 `TypedResults`，业务规则均位于 Application/Infrastructure。
- 管理邮箱配置契约增加 password reset lifetime/cooldown/hourly limit 三个强类型整数；前端只通过
  generated SDK 和现有 `authApi` 薄封装调用。新增 `/forgot-password`、`/reset-password`，登录页
  提供恢复入口；申请成功始终显示相同隐私文案。重置页有两个独立、可访问的密码明文切换按钮，
  Token 初始化后立即从地址栏移除；成功后清空本地认证状态并返回登录页。邮箱 placeholder 使用
  Vue i18n literal `{'@'}`，避免 `name@example.com` 被消息编译器误解析。
- OpenAPI 路由漂移门禁为 171 条文档化 v1 路由，另有 `GET /health`。两份 artifact 字节一致，
  SHA-256 均为 `B844F6BF808FCD01F0E267E8343744F3EF0784F803075492CDAED3ECF775916F`；SDK
  hash：index `F121F460383F13276F48E7FE32724A7C8700BA2AE8CD2CC70B6094706B49BBF8`，sdk
  `9659981B41A9F2BBEA137735E33B6C270D14DAFE0F4FCAF879DBF31AEC067A32`，types
  `508DDC5F3A75DC62AA3A077368129A1636FD24029F3B013E4B72D40D43408195`。
- 后端最终验证：`dotnet build backend/NoCTF.slnx --no-restore` 为 0 warning/0 error；
  `PasswordResetEndpointTests` 8/8 passed；真实 PostgreSQL password reset 与 GreenMail SMTP 类
  6/6 passed，覆盖并发单赢家、hash-only、旧 Token 失效、账号限流、账号资格、TokenVersion、
  密码更新和关闭注册验证时的两类邮件。non-Integration 全套发现 593 项，589 passed、4 failed；
  四个失败均为既有 FastEndpoints Validator 进程内注册隔离问题，受影响的四个测试类随后分别在
  独立进程运行，共 20/20 passed。该基线问题没有混入本功能提交，后续应单独修复测试隔离。
- 前端最终验证：`bun test` 178/178 passed；`vue-tsc` 与 production `bun run build` passed；触及
  文件 scoped ESLint 为 0 error，`AuthLayout.vue` 保留 12 条本阶段未引入的单行内容换行 warning。
  仓库级 lint 仍被 generated/vendor/config/旧文件的大量既有基线阻塞，没有全局 `--fix`。
  `git diff --check` passed。
- 使用内置浏览器连接本地 Vite 完成实际验收：登录恢复入口、申请页、无 Token 失效态、有效 Token
  表单、两个密码眼睛、短密码/不一致的内联错误，以及 Token 从 URL 移除均正常。移动端验收发现
  Auth grid item 的 intrinsic width 导致 375px 页面横向溢出，已给 AuthLayout section/content
  增加 `min-w-0`；复验失效页和有效表单 document scrollWidth 均等于 viewport，桌面 1265px
  同样无溢出。本地 Vite 监听和浏览器标签已清理，未访问或修改现网。

### 6.57 完整比赛归档与平台审计导出（2026-08-06）

- 功能提交：`7b479f4a feat(exports): add durable competition and audit archives`；前端提交：
  `5923e577 feat(frontend): add data export workspaces`。两项提交均只在本地分支，未 push、未部署；
  平台信息继续读取程序集元数据，仓库没有需要随本纵切递增的独立运行版本。
- 用户通过 `$grill-me` 的八组 `A` 确认完整契约：每个比赛导出一个 ZIP，包含 manifest 及
  competition、challenges、teams、submissions、scoring-events、cheat-incidents、
  competition-events 七个 NDJSON 数据集；平台审计是独立 NDJSON。比赛归档允许 Administrator、
  Owner、Manager，平台审计仅 Administrator。默认不含明文 Flag；仅 Human Administrator 可在
  提供 8 至 512 字符理由后显式包含，成功生成会写永久比赛审计。导出包含删除、取代、匿名化和
  全部历史行，排除附件二进制、运行诊断日志、密码、Token 与 SMTP 凭据。
- 只新增一个 `data_exports` 表，保存异步任务与对象元数据；没有复制 competition events 或平台
  审计事实，也没有为比赛/请求人增加阻塞删除的外键。EF migration
  `20260805174833_AddDataExports` 完全由 `dotnet ef` 生成，未手改 migration 或 snapshot。
  Wolverine 负责 Generate/Expire/Purge durable message；对象文件 24 小时失效并删除，任务元数据
  30 天后删除。每个请求人/导出 scope 全局最多一个 Queued/Processing 任务，PostgreSQL advisory
  transaction lock 固定并发单赢家；默认最大产物 2 GiB，超限整任务失败且不保留截断文件。
- 生成器在 PostgreSQL Repeatable Read transaction 中读取一致快照。比赛 ZIP manifest 使用
  `noctf.competition-export/1` schema version，记录 ExportId、scope、competition、导出时间、
  protected-Flag 边界和每个数据集行数；Flag 默认只输出 SHA-256，附件只输出名称、类型、大小和
  SHA-256，队伍邀请令牌、Competition derivation secret 与对象 key 永不导出。平台审计直接遍历
  既有 CompetitionEvent/UserAccountLifecycleAudit 投影视图，不复制数据。文件上传到既有
  `IObjectStorage`，完成/失败均产生幂等站内通知；下载时重新校验当前角色，protected artifact
  永远只允许 Administrator。
- 新增五个强类型 FastEndpoints：比赛任务 GET/POST、平台审计任务 GET/POST、统一下载 GET。
  Endpoint 全部使用 `ExecuteAsync`、具体 HttpResults、`Results<T...>` 和 `TypedResults`，权限、
  业务状态和生成规则位于 Application/Infrastructure。OpenAPI 为 176 条文档化 v1 路由，另有
  `GET /health`；Admin 路由 115 条。两份 OpenAPI artifact 字节一致，SHA-256 均为
  `145FBCC6AA269FE0B1FE09207F1AD69AE18CF6A0B13EA66B7E83B3B436AEE05F`；generated SDK hash：
  index `AD368712EC2B48CEE388369DD0CAAE65ECAB7B8CD85AEB8DAD50BDD6803F3AD0`，sdk
  `DF31A50E1FA506E5EDD09401FBC8BDCCE299CF57F65C75C6E9BF7BD3F8FBE557`，types
  `DC5ED9F7F962FFC7082AF024704B549C47208569A1AB95A730634C85786BD7CE`。
- 前端只通过 regenerated SDK 与 `dataExportApi` 薄封装调用接口，没有新增手写 URL。比赛管理详情
  增加“数据导出”标签；平台日志的审计页增加“平台审计归档”。共用 Pixel Industrial 面板显示
  Queued/Processing/Available/Failed/Expired、轮询、失败码、文件大小、保留期限和下载入口；仅平台
  Administrator 看见 protected-Flag 开关与理由。DataExportReady/DataExportFailed 站内通知会
  导航到对应比赛导出页或平台审计页。浏览器验收发现并修复 Tabs grid/table intrinsic width 导致
  的整页横向溢出；本地 mock 补齐本纵切真实交互所需的强类型响应。
- 最终验证：Debug solution build 0 warning/0 error；真实 PostgreSQL DataExports 2/2 passed，覆盖
  默认脱敏、protected Flag 与永久审计、全部历史行、平台审计投影、24h/30d 生命周期、不同比赛
  同 scope 并发单赢家和 2 GiB 原子失败；DataExport Application 2/2、Admin OpenAPI/route drift
  2/2 passed；`dotnet ef migrations has-pending-model-changes` 无漂移。non-Integration 全套发现
  595 项，591 passed、4 failed，仍是 6.56 已记录的既有 FastEndpoints Validator 进程内注册隔离
  问题，失败文件不在本阶段改动范围。前端 `bun test` 181/181、`vue-tsc`、production build、触及
  文件和 mock JSON scoped ESLint 0 warning/0 error、`git diff --check` 均 passed；全仓 lint 仍被
  generated/vendor/config/旧文件约 1.1 万条既有格式基线阻塞。
- 关键验证命令：`dotnet build backend/NoCTF.slnx --no-restore`；
  `dotnet backend/tests/NoCTF.Tests/bin/Debug/net10.0/NoCTF.Tests.dll --treenode-filter
  '/*/*/*/*[Category=DataExports]' --minimum-expected-tests 2`；以 `--filter-uid` 独立执行两项
  Admin OpenAPI/route drift 测试；`dotnet ef migrations has-pending-model-changes --project
  backend/src/NoCTF.Infrastructure/NoCTF.Infrastructure.csproj --startup-project
  backend/src/NoCTF.API/NoCTF.API.csproj --no-build`；前端执行 `bun test`、`bun run build` 和触及文件
  `bunx eslint ... --max-warnings 0`；最后执行 `git diff --check`。
- 使用内置浏览器连接本地 Vite mock 完成比赛归档和平台审计实际验收：Human Administrator 的
  protected-Flag 开关、理由长度门禁、创建成功 toast、可下载/空状态、导航和中英文布局均正常；
  1265px 视口下 document scrollWidth 等于 clientWidth，右侧按钮不再裁切。平台日志 SignalR 因
  本地 mock 没有 hub 而显示预期的降级提示，历史/审计操作不受影响。浏览器标签和本地 Vite 监听
  已清理，未访问或修改现网。

### 6.58 PostgreSQL、对象存储与 Wolverine 灾难恢复（2026-08-06）

- 功能提交：`92a6205 feat(operations): add verified disaster recovery tooling`。本阶段只增加
  外部运维工具、CI 恢复演练和权威文档；没有应用 API、管理页面、业务表、EF migration、
  OpenAPI 或 generated SDK 变化，未 push、未部署，也没有修改目标环境配置。
- 用户通过 `$grill-me` 回答八项 `A`，确认：采用外部 CLI 而非应用内备份；生成停写期间的离散
  一致性恢复点而非伪装成在线 PITR；完整保留 `public`、`wolverine_api`、`wolverine_worker`、
  `wolverine_runner`；备份当前 bucket 全部对象和必要元数据；产物使用 age recipient 加密并由
  独立 Minisign key 签名；部署 Secret 独立保存并通过非秘密 `secretSetId` 绑定；只允许恢复到
  隔离的空 database/空 bucket；参考目标为 RPO 不超过 24 小时、RTO 不超过 4 小时、恢复点保留
  30 天，生产调度、异地复制、删除和告警继续由目标环境负责。
- 新增 `deploy/recovery` 工具镜像，固定 PostgreSQL 16、MinIO Client image digest，并提供
  `backup`、`restore`、`verify` 三个入口。备份前后均拒绝存在其他 PostgreSQL client connection，
  要求运维先停止 API、Worker、Runner 和 migration job；PostgreSQL custom dump 包含业务事实、
  Wolverine Inbox/Outbox/durable queue/scheduled/dead letter，对象逐项保存内容、`Content-Type` 和
  `x-amz-meta-sha256`。Redis 明确不备份，恢复后从 PostgreSQL、在线 Runner 和流量重建。
- 加密包使用 `noctf.disaster-recovery/1` manifest，记录恢复点时间、schema、逐表行数、对象数/
  字节数、文件 SHA-256、Wolverine 恢复语义和外部 Secret 集标识，不记录数据库/S3 地址或凭据。
  恢复先验证 Minisign 来源、age 完整性、安全 tar 路径、manifest 和全文件 checksum，再拒绝任何
  非空目标；写入后重新比较所有受保护 schema 的逐表行数，并下载每个对象核验 key、内容 SHA-256、
  Content-Type 与 SHA 元数据。目标 database/bucket 名必须与 manifest 一致，工具不提供覆盖、合并
  或原地恢复。
- Secret 不进入产物：JWT signing key、Runner scoring key、数据库/S3 凭据、age identity、
  Minisign signing key 与 `EmailVerification__EncryptionKey` 必须由外部 Secret 系统恢复；否则
  既有 session/内部签名或数据库内 SMTP 密文不能按原环境工作。签名 secret key 在自动化挂载时
  不使用交互式口令，依赖 Secret 系统加密、最小权限只读挂载；恢复端只分发 public key。
- `deploy/recovery/rehearse.sh` 只创建带唯一名称的两套 PostgreSQL、两套 MinIO、临时 network 与
  工具 image，退出后精确清理。最终真实 Docker 演练 passed，覆盖比赛永久事件、三个 Wolverine
  schema、多个对象及其元数据、快照后源数据变化不会进入恢复点、恢复后逐表/逐对象一致性、
  Minisign 篡改拒绝和非空目标拒绝。ShellCheck（`-x -P SCRIPTDIR`）与全部脚本 `bash -n`
  passed；CI 新增同一恢复演练门禁。
- 后端 `dotnet build backend/NoCTF.slnx --no-restore` 为 0 warning/0 error；EF
  `has-pending-model-changes` 返回无模型漂移（全局 EF CLI 8.0.11 对 runtime 10.0.4 仅提示版本
  较旧）；`git diff --check` 与 Markdown 相对链接检查 passed。主机缺少仓库锁定的 SDK 10.0.300，
  构建/EF 检查期间仅在工作树临时使用已安装的 10.0.201，结束后已将 `global.json` 恢复为
  10.0.300，未纳入提交。
- non-Integration 全套发现 595 项，594 passed、1 failed：
  `ChallengeTemplateProtocolTests.Update_request_requires_complete_revision_fenced_payload` 仍是 6.56/
  6.57 已记录的 FastEndpoints Validator 进程内注册隔离基线，单独进程复验 1/1 passed，本阶段
  未触及该文件。Actionlint 在既有 OpenAPI workflow 的 `for i in {1..60}` 报唯一 SC2034；忽略该
  既有 warning 后 workflow lint passed，本阶段新增 recovery step 无新增诊断。

### 6.59 私密队伍封禁申诉、误判更正与公开更正通知（2026-08-06）

- 功能提交：`f20193c feat(teams): add private ban appeals and corrections`。本阶段未 push、未部署；
  未修改用户的 `TODO.md` 或其他 Agent 的 Runner、本地端口、实例状态与脚本工作树。平台信息仍
  来自程序集元数据，本纵切没有需要递增的独立运行版本。
- 用户通过 `$grill-me` 确认：仅处理比赛 Team 封禁；队长可对每个不可变 TeamBanned 事实提交一次
  私密申诉，全队成员可读；Administrator、Owner、Manager 可作最终裁决，Judge、Observer 只读；
  维持封禁始终私密；接受申诉或工作人员直接纠错可在 Finished 后解封并重新投影历史排行榜；公开
  更正只包含比赛、队伍和时间，不公开工作人员、理由、证据或 Flag。v1 不提供附件、公开对话或重复
  申诉，公开解释继续使用 Hint/公告。
- 没有新增申诉业务表。`competition_events` 新增 nullable `ParentEventId` 自关联，将申诉连接原始
  TeamBanned、将裁决连接申诉、将 TeamUnbanned/TeamBanCorrectionPublished 连接原始封禁；原始
  事实永久保留，公开事件不含 Actor/Reason。EF migration `20260805210255_AddTeamBanAppeals` 完全由
  `dotnet ef` 生成，只有 `parent_event_id`、索引和 Restrict self-FK；模型漂移检查通过。
- 新增六个强类型 FastEndpoints：参赛端 GET ban case、POST appeal；管理端 GET appeals、POST
  uphold、POST accept、POST direct correction。Endpoint 全部使用 `Endpoint<TRequest, Results<...>>`、
  `ExecuteAsync`、具体 HttpResults 和 `TypedResults`；权限、一次性约束、当前封禁判断、Finished 后
  更正与 leaderboard revision/outbox 失效均位于 Application/Infrastructure。Team API 增加
  `IsBanned`；CompetitionEvent API 增加 `ParentEventId`。
- 一般人工封禁现在保存经过秘密脱敏的 staff-only 原因，参赛端仅投影 `ManualModeration` 或
  `CheatIncident` 来源类别。接受/直接纠错会清空 Team 当前封禁字段、递增 LeaderboardRevision、
  发布 `InvalidateLeaderboard` 与只含资源标识的 `TeamBanCorrected`；不写补偿 ScoringEvent、不删除
  Submission/成绩/审计，也不重启 Runtime。现有反作弊更正路径同样支持 Finished 后纠错并复用上述
  私密/公开事件边界。
- OpenAPI 现在有 182 条文档化 v1 路由，另有 `GET /health`；Admin 路由 119 条。两份 OpenAPI
  artifact SHA-256 均为 `09E79962150AFC4A39426F5551ECD9E6050EB967B199103E7F419F3B798A5769`，
  generated TypeScript SDK 已重新生成，前端只通过 generated operation 和
  `teamBanAppealApi` 薄封装调用，没有手写 API URL、版本前缀、路径或 DTO。
- 管理比赛的“队伍审核”页显示封禁状态、显式封禁理由、直接纠错和私密申诉队列；只有可裁决角色
  看见接受/维持按钮。参赛 `/teams` 显示封禁来源、当前私密申诉/裁决，只有队长且尚未申诉时能提交
  16–512 字符陈述。中英文、加载/错误/空状态和本地 mock fixture 已补齐。
- 后端最终验证：solution build 0 warning/0 error；新增 Application/validator 定向 3/3 passed；
  真实 PostgreSQL 申诉/Finished 更正和既有反作弊事件流 2/2 passed；Admin metadata、runtime quota
  contract、OpenAPI route drift 3/3 passed；EF pending model 无漂移；两份 OpenAPI artifact 字节
  一致；`git diff --check` passed。non-Integration 全套发现 597 项，594 passed、3 failed，仍为
  既有 FastEndpoints Validator 进程内注册隔离：Null collection、BanTeam reason、challenge revision；
  本阶段触及的 `BanTeam_RequiresAReason` 随后独立进程 1/1 passed，未修改另外两个失败文件。
- 前端最终验证：`bun test` 183/183 passed；`vue-tsc` 与 production `bun run build` passed；触及
  文件和 mock JSON scoped ESLint 0 warning/0 error；OpenAPI SDK 重生成功。使用内置浏览器连接
  本地 Vite mock 实际验收管理端封禁队伍、私密申诉、来源、只读内容、维持/接受按钮，以及接受后
  解封、历史重投影和无理由公开通知说明；浏览器标签与 5173 本地监听已清理，未访问或修改现网。

### 6.60 Validator 测试隔离与参赛者端浏览器验收（2026-08-06）

- 功能提交：`948b02f test: stabilize validators and participant acceptance`。本阶段没有业务实现、
  API、OpenAPI、generated SDK、Domain、EF 模型或 migration 变化；未 push、未部署，也没有修改
  用户的 `TODO.md` 或其他 Agent 的 Runner、本地端口、实例状态和脚本工作树。
- 已稳定复现 6.56 至 6.59 记录的三个偶发失败：FastEndpoints 测试宿主执行
  `AddFastEndpoints` 时会全局切换 FluentValidation 的 JSON 属性名解析；直接实例化 Validator 的
  单元测试与宿主测试并行时，错误属性名可能是 `Reason`/`Flags[1]`/`Mode`，也可能是
  `reason`/`flags[1]`/`mode`。Validator 规则和产品契约没有丢失，失败只来自进程全局命名格式与
  大小写敏感断言的竞态。
- 修复保持最小：三个协议测试改为使用 `StringComparer.OrdinalIgnoreCase` 或
  `StringComparison.OrdinalIgnoreCase` 比较属性名；没有用 `[NotInParallel]` 串行化全部测试，也
  没有改变 FastEndpoints/FluentValidation 生产注册或业务规则。修复前五轮压力命令第 1 轮稳定得到
  594/597 和上述三个失败；修复后同一完整 non-Integration 命令连续五轮均为 597/597，阶段结束前
  再次复验 597/597 passed。
- 本地 Vite mock 原先缺少 generated `GET /api/v1/auth/me` 契约。登录后 `/teams` 的当前用户查询
  会落到真实后端并返回 401，导致内存会话清空和登录页回跳。fixture 已补齐完整管理员当前用户响应；
  浏览器验收时只临时把 login/refresh/current-user 设为普通参赛者并将现有申诉设为空，验收完成后
  除新增 `/auth/me` route 外全部还原，临时身份和表单状态未进入提交。
- 使用内置浏览器连接 `http://127.0.0.1:5173` 完成参赛者实际验收：登录后 SPA 正常进入
  `/teams`；普通用户导航不显示管理后台；ByteGuild 显示已封禁、`跨队 Flag 事件` 来源和“工作人员
  证据及内部讨论不会公开”的私密边界；申诉为空时按钮禁用，少于 16 字仍禁用，合法陈述后启用。
  未提交 mock 申诉写操作；页面无 console error，仅有既有 THREE.Clock deprecation warning。
  浏览器标签、临时 fixture 和 5173 监听均已清理，未访问或修改现网。
- 验证结果：Debug solution build 0 warning/0 error；non-Integration 597/597 连续五轮加最终一轮
  passed；真实 PostgreSQL `Captain_can_appeal_and_staff_can_correct_finished_competition` 1/1 passed；
  PostgreSQL/Redis Testcontainers 可达性 1/1 passed；前端 `bun test` 183/183 passed；`bun run build`
  的 `vue-tsc` 与 production Vite build passed；mock JSON 可解析且目标 `git diff --check` passed。
  全量 Integration 当前发现 154 项，包含 Kubernetes、libvirt 和真实 Docker 环境型用例；本次整套
  运行在 10 分钟内无失败输出但未产生汇总，超时后子进程和 Testcontainers 均自行退出/清理，因此
  明确不记为通过，以上两个与当前范围相关的真实依赖用例为可判定依据。
- 主机仍缺少仓库锁定的 SDK 10.0.300；build/test 期间仅临时把 `global.json` 指向已安装的
  10.0.201，提交前已恢复 10.0.300，SDK 临时改动未进入提交。本阶段没有契约变化，因此没有重生
  OpenAPI/TypeScript SDK，也不需要 EF pending-model 检查或平台运行版本递增。

### 6.61 生产部署、Worker 生命周期恢复与浏览器验收（2026-08-06）

- 用户明确授权 push 和生产部署。工作分支 `codex/backend-gitops-completion` 已推送到
  `origin`；生产 Compose checkout `/root/NoCTF` 已快进到功能提交
  `499f5cb fix(worker): keep lifecycle handler codegen-safe`。未创建 PR、未合入 `main`，也没有
  修改生产 `.env`、HTTPS 证书、数据库卷、Redis 卷或既有直连 Docker 随机宿主端口方案；按用户
  指令没有为本次覆盖部署另做备份。
- 首次把 `a3831c3` 的 API、Migration、Worker、Runner 镜像切换上线后，迁移容器以 0 退出，
  API/Runner 健康且公网 `/health` 返回 `{"status":"ok"}`。验收同时发现 Worker 对
  `AdvanceCompetitionLifecycle` 的 Wolverine 6 动态代码生成失败：
  `ICompetitionEventRecorder` 使用 scoped opaque lambda alias，违反默认
  `ServiceLocationPolicy.NotAllowed`；其他维护链仍工作，但比赛生命周期链停在 processing version
  15555，因此不能把初次切换视为完整成功。
- 修复保持最小：`ICompetitionEventStore` 与 `ICompetitionEventRecorder` 改为直接绑定
  `CompetitionEventStore`，不再通过 `GetRequiredService` 工厂别名；新增 descriptor 回归测试保证两项
  注册有明确 `ImplementationType` 且没有 `ImplementationFactory`。没有新增表、迁移、接口、DTO、
  OpenAPI、generated SDK、业务状态或配置项。
- 本地验证使用仓库锁定的独立 SDK 10.0.300：目标回归 1/1 passed，完整 non-Integration
  598/598 passed，`dotnet build backend/NoCTF.slnx --no-restore` 0 warning/0 error，
  `git diff --check` passed。修复提交已独立推送。
- 生产主机无法稳定访问 GitHub/Docker Hub；部署使用有 prerequisite 的 Git bundle，以及本地已存在
  基础镜像/kompose 的 SHA-256 校验后离线传输。kompose v1.38.0 二进制与官方
  `SHA256_SUM` 一致；没有修改仓库 Dockerfile，离线替换只存在于已清理的临时构建上下文。最终四个
  应用镜像均从 `499f5cb8aac9a1902940f5269f63bcadd64fdf6e` 重建：Backend
  `a4533dd0ba74`、Migration `c6ef977a6676`、Worker `8cd6aa3a90e5`、Runner
  `cc5911729e23`。
- 最终生产验收：Migration exit 0；Backend/Runner healthy，Worker running；四个应用容器
  restart count 均为 0；PostgreSQL `pg_isready` accepting connections，Redis `PONG`；15 分钟窗口内
  API/Worker/Runner 的 fail/fatal/unhandled/critical/exception 匹配均为 0。比赛生命周期 schedule
  从 15555 恢复并持续推进至 15584，跨多个 30 秒轮询周期更新；Runner 内 Docker client/server
  28.5.2/29.3.1、kompose 1.38.0 可用。TLS 证书 CN/SAN 为 `noctf.fa1lsnow.com`，有效期至
  2026-10-15，公网 HTTPS health 正常。
- 使用内置浏览器实际访问生产 HTTPS：首页完整渲染并显示已登录 Administrator、`/teams`、
  `/profile` 与管理入口；`/competitions` 完整加载比赛卡片且没有失败提示；
  `/admin/platform-logs` 显示实时连接正常、死信 0。页面 console 没有应用错误，仅有既有
  `THREE.Clock` deprecation warning；浏览器临时标签已清理。
- 部署没有触碰用户/协作者的 `TODO.md`、Runner `Properties/`、本地端口 overlay、前端实例状态/
  Query 文件、测试和 `scripts/` 未提交工作。Administrator MFA 与分层限流仍按用户要求暂缓；
  生产备份调度、异地保留、监控告警和正式恢复演练仍是独立运维事项。

### 6.62 邮箱验证投递与全局提示样式修复（2026-08-06）

- 功能提交：`58f763f fix(email): restore verification delivery and toast styling`。本阶段没有 HTTP
  endpoint、DTO、OpenAPI、generated SDK、Domain、EF 模型或 migration 变化；尚未 push、尚未
  部署，6.61 的 push/deploy 授权不自动续用。
- 生产只读诊断确认邮箱验证配置已启用，公开地址、SMTP host、465 端口、认证账号、加密密码和
  From 地址均已配置；`FFS` 注册生成了一个未消费且未过期的验证 Token。该消息进入 Worker 后于
  2026-08-06 09:02:22 UTC 触发 Wolverine `InvalidServiceLocationException`：
  `IEmailVerificationDelivery` 使用 scoped opaque lambda factory，违反
  `ServiceLocationPolicy.NotAllowed`。因此 SMTP 发送根本没有执行，不是收件箱拒收；失败消息没有
  留在当前队列或死信，原始 Token 仅保留 hash，部署后必须重新发送才能生成可投递链接。
- 修复把邮箱配置读取、验证邮件投递和密码重置投递接口全部改为带明确
  `ImplementationType` 的直接 scoped 注册，移除 `GetRequiredService` 工厂别名；descriptor 回归
  测试同时断言四项注册没有 `ImplementationFactory`，固定 Wolverine 静态代码生成边界。
- MailKit 消息现在使用 From 域显式生成 `Message-Id` 并通过初始 Header 构造 `MimeMessage`，不再
  依赖本机 hostname。该修复消除了 Windows 主机名导致的 SMTP 集成测试提前失败，也使容器/主机名
  与 RFC Message-ID 域解耦；凭据、验证 Token 和邮件正文没有进入日志或测试输出。
- Frontend `Sonner.vue` 重新显式引入 `vue-sonner/style.css`，并以 Pixel Industrial 的方角、两像素
  边框、固体偏移阴影、主题字体和方形关闭按钮覆盖基础样式。验证邮件重新发送使用稳定 toast id，
  相同失败只更新一条提示，不再把无样式通知 DOM 堆叠在页面底部。未写死 URL、域名、端口、API
  前缀、端点路径、DTO、枚举或失败码。
- 验证结果：`dotnet build backend/NoCTF.slnx --no-restore` 0 warning/0 error；邮件 DI 与 MailKit
  定向 TUnit 5/5 passed；`bun run build` 的 `vue-tsc` 与 production Vite build passed；触及的两个
  Vue 文件 scoped ESLint 0 warning/0 error；`git diff --check` passed。完整后端套件在 Message-ID
  修复前执行 752 项，741 passed、9 failed、2 skipped；其中 4 个 SMTP 失败已由随后 5/5 定向回归
  证明修复，剩余 5 个是既有/环境型 KoH advisory-lock 时序、两个 Docker Container 高端口连通、
  Docker Compose lifecycle 和 Patch advisory-lock 时序失败，本阶段未修改这些业务文件，也没有把
  它们记为通过。
- 使用内置浏览器访问本地 Vite `/login` 并触发本地失败请求，实际 DOM 只存在一个通知项；截图确认
  提示固定在右上区域、具有完整错误图标/关闭语义和 Pixel Industrial 边界，页面不再被通知内容撑开。
  未向真实邮箱发送测试邮件，未操作生产账号；本地浏览器标签和 4173 监听在交接提交前清理。
- 下一步需要用户重新明确授权 push/deploy。部署 Worker/Backend/Frontend 后，应由待验证账号重新发送
  一封验证邮件，确认 Worker 不再出现 `InvalidServiceLocationException`、日志出现脱敏的成功投递且
  收件箱收到验证链接；不要尝试恢复已经丢失明文的旧验证 Token。

### 6.63 生产 ERROR 修复、邮箱修复部署与远程空间清理（2026-08-06）

- 用户明确要求检查并修复生产 ERROR，同时以远程主机为主要清理目标。本轮已将
  `58f763f fix(email): restore verification delivery and toast styling`、其 HANDOFF 提交
  `384325b` 以及新增的 `8b0bf55 fix(deploy): install runtime GSS dependency` 推送到
  `origin/codex/backend-gitops-completion`；生产 checkout `/root/NoCTF` 已快进到完整提交
  `8b0bf555093368c540e3cd0a6a710811c5f72ce8`。未创建 PR、未合入 `main`，没有修改 `.env`、
  HTTPS 证书、PostgreSQL/Redis 卷、上传目录或 Docker Runtime 的直连随机宿主端口方案。
- 只读日志确认两类生产 ERROR：验证邮件消息在旧 Worker 中因 opaque scoped factory 触发
  Wolverine `InvalidServiceLocationException`；API、Worker、Runner 每次启动还会各记录一次
  `libgssapi_krb5.so.2` 无法加载。前者由 6.62 的明确 `ImplementationType` 注册修复，后者根因是
  Ubuntu 24.04 的 ASP.NET 10.0 基础镜像不含 GSS 运行库。本轮新增一个共享 `runtime` 阶段安装
  发行版包 `libgssapi-krb5-2`，API/Worker/Runner 全部从该阶段派生；部署拓扑测试固定三进程共享
  运行时和依赖包，避免单个最终阶段回退。
- 本地临时容器证明安装后 `/usr/lib/x86_64-linux-gnu/libgssapi_krb5.so.2` 存在；
  `DeploymentTopologyTests` 2/2 passed。宿主机缺少锁定的 SDK 10.0.300，WSL Debug 测试项目构建
  0 warning/0 error；额外的 WSL Release solution build 被既有 `obj/Release` Windows/WSL 写权限
  阻塞，未把它误记为代码失败。最终发布从 `8b0bf55` 的独立干净 worktree 构建，完整执行前端
  `vue-tsc`/Vite production build，以及 API、Worker、Runner 三项 Release `dotnet publish`，全部
  成功；发布镜像分别为 API `7331f6e316de`、Worker `6c128494403b`、Runner `1976b4a0d55f`。
- 生产使用 SHA-256 校验的 Git bundle 与共享层镜像包离线传输。Migration 以 0 退出后才重建三个
  长期进程；最终 Backend/Runner `healthy`、Worker `running`，三者 restart count 均为 0，容器内
  GSS 库均存在，PostgreSQL `pg_isready` accepting connections、Redis `PONG`、公网 HTTPS
  `/health` 返回 `{"status":"ok"}`。从本轮三个容器各自 `StartedAt` 起，精确匹配 .NET/Serilog
  ERROR/FTL/fail/critical/unhandled、`InvalidServiceLocationException` 和 libgssapi 签名均为 0；宽
  匹配曾命中 SQL 列名 `failure_code`，已作为检索噪声排除。
- 远程清理只删除经身份和引用检查确认可重建/无引用的目标：一个 5 天前失败的孤立容器及其
  432 MB 镜像、四个被新容器替换的旧 NoCTF 应用镜像、SDK/Bun/Docker CLI/Alpine 四个仅构建
  基础镜像、两个部署传输临时文件，以及生产 checkout 中 277,391,743 bytes 的
  `frontend/node_modules`。根盘由初始 92% 使用、3.6 GB 可用（镜像装载峰值 97%、1.6 GB 可用）
  收敛到 86% 使用、5.8 GB 可用。没有删除当前五个生产镜像、任何数据卷、数据库/Redis 数据、
  `.git`、其他应用目录、Lightclaw 模型或系统缓存；后续发布继续使用本地干净构建与离线镜像包。
- 内置浏览器实际加载新生产首页和 `/admin/users`，Administrator 会话、导航和三条用户记录正常；
  浏览器控制在展开 FFS 操作菜单时超时，因此没有替用户修改账号或发送外部邮件。旧 FFS 验证消息
  的明文 Token 在修复前失败后不可恢复；本次部署不会伪造或重放它，FFS 必须在验证页主动点击
  “重新发送”生成新 Token 和新邮件。SMTP/MailKit 与 Wolverine 注册的本地回归结果仍以 6.62 为准。

### 6.64 个人资料入口、设置布局与头像裁剪视觉收敛（2026-08-06）

- 功能提交为 `dca96ab fix(profile): streamline account workspace`。首页已完全移除重复的个人资料
  摘要卡和对应 `current-user` 请求；顶部导航使用生成 SDK 获取的当前用户头像作为唯一资料入口，
  无头像时显示用户名首字母，头像链接具备可访问名称并进入 `/profile`。资料更新仍通过共享
  `queryKeys.currentUser` 缓存即时反映到导航，没有新增前端状态或手写接口。
- `/profile` 从四块同权卡片重排为左侧身份摘要与右侧连续设置面板；简介、头像、邮箱公开性和改密
  仍保持各自独立保存与原有安全语义，但信息层级、操作对齐和桌面/移动响应式结构已收敛。旧的
  单边强调提示改为完整边框的 Pixel Industrial 面板，没有改变后端契约。
- 头像编辑器的圆形裁剪指示层由 `inset-4` 改为覆盖完整 320×320 方形预览，因此圆形直径与矩形
  宽度相等；Pointer Events、pointer capture、指针锚点滚轮缩放、旋转后的边界钳制和最终正方形
  导出算法均未改动。
- 验证结果：`bun test ./tests/userProfile.test.ts ./tests/passwordVisibility.test.ts` 12/12 passed；
  scoped ESLint passed；`bun run build` 完成 `vue-tsc --noEmit` 与 Vite production build。Rollup
  仍只有既有 PURE annotation 与大 chunk 警告，没有新增错误。尚未执行本地浏览器验收；应与后续
  全局队伍前端一起验收。
- 用户通过 `$grill-me` 回答 `AAA`，确认下一阶段采用全局可复用 Team、每场比赛报名保存成员快照、
  全局邀请 Token 只用于加入 Team，比赛报名由队长单独操作。该决策会新增唯一必要的比赛报名状态
  持久化结构并改变 Team/OpenAPI/前端流程；6.64 没有提前修改该数据模型。当前任务未授权 push 或
  deploy，本提交和本 HANDOFF 只保留在本地。

### 6.65 全局可复用队伍、比赛报名快照与前端验收（2026-08-06）

- 用户通过 `$grill-me` 的三项 `AAA` 明确确认：Team 是不依赖比赛的全局可复用身份；比赛报名复制
  当时成员形成快照，后续成员变更不回写历史；邀请 Token 只加入全局 Team，报名由队长在比赛页
  单独执行。后端功能提交为 `e50c4c0 feat(teams): add reusable global team profiles`，前端适配提交为
  `169ee05 feat(frontend): adapt reusable team workflow`；两项均仅为本地提交，未 push、未部署生产。
- 数据模型只新增必要的 `team_profiles` 表，并在既有 `teams` 增加 nullable `team_profile_id`：前者保存
  稳定名称、大小写不敏感规范名、当前 Captain/MemberIds、头像和全局 32 字符邀请 Token；后者继续
  作为全部计分、提交、Runtime、封禁、申诉和审计关系使用的比赛快照。既有历史记录保持 null；新
  报名保留外键追溯。没有新增 TeamMember、Invitation 或 CompetitionConfiguration 表。migration
  `20260806103231_AddGlobalTeamProfiles` 由 `dotnet ef` 10.0.4 生成，未手改 migration/snapshot。
- 强类型 FastEndpoints 新增 `GET/POST /api/v1/teams` 和
  `POST /api/v1/competitions/{competitionId}/team-registrations`；修改、删除、加入、退出、移除成员、
  轮换 Token 和转移队长全部迁到全局 `/api/v1/teams/...`。比赛队伍列表、本人队伍、详情和报名重提
  仍是 competition-scoped。报名 use case 只允许当前全局队长，执行比赛状态、成员上限、同比赛成员
  互斥和重复报名检查，并复制名称、头像、CaptainId、MemberIds；OpenAPI、路由清单、E2E 流程及
  TypeScript SDK 已重新生成/同步，前端没有手写 URL、DTO、枚举或失败码。
- `/teams` 现在始终显示右上角创建/邀请加入入口，不再查询或要求可报名比赛；空状态仅保留说明，
  不再出现第二个无响应创建按钮。页面只管理全局名称、头像、当前成员、Token、编辑与退出；比赛页
  从队长拥有且不超过人数上限的全局队伍中选择报名。私密封禁申诉完整迁回对应比赛报名工作区，
  仍保留队长提交、成员查看状态和工作人员裁决结果。中英文补齐全部新流程文案及漏译的复制按钮，
  Router 在新页面导航时回到顶部、浏览器后退时保留 saved position。
- 后端验证：锁定 SDK 10.0.300 的 solution build 0 warning/0 error；non-Integration 601/601 passed；
  主机 Docker/Testcontainers 真实 PostgreSQL 定向用例 1/1 passed，覆盖跨比赛复用、报名成员快照不
  回写、投影追溯与名称大小写冲突；EF pending model check passed；`NoCTF.E2E.csproj` 独立编译
  0 warning/0 error。嵌套 SDK 容器内的 Testcontainers Resource Reaper 因 Docker Desktop 容器网络
  初始化超时，随后在主机 Docker 环境通过同一真实依赖用例；本阶段没有把完整 Integration 套件记为
  通过。
- 前端验证：scoped ESLint passed；全量 Bun 186/186 passed；`bun run build` 的 vue-tsc 与 Vite
  production build passed，只有既有 PURE annotation/大 chunk 警告。内置浏览器连接本地迁移后的
  API，确认首页资料摘要已移除、导航头像进入资料页、重排后的 `/profile` 正常；在数据库没有任何
  比赛时从 `/teams` 成功创建 `Browser Acceptance Team`，创建/加入面板均无比赛选择器、空态无重复
  按钮，Token、复制、轮换与编辑区正常显示。该记录只存在本地验收库，不涉及生产数据。
- 本地验收为当前工作树临时重建 API/Migration 并应用 migration，公网 HTTPS/远程主机未变更；临时
  Vite 监听和浏览器验收标签已清理。本轮继续保护用户/协作者的 `TODO.md`、Runner `Properties/`、
  本地端口 overlay、实例状态 composable、Query client、对应测试和 `scripts/`。下一步需用户重新
  明确授权才可 push 或生产部署；部署前必须包含 `e50c4c0`、`169ee05` 及本 HANDOFF 提交。

### 6.66 全局队伍生产部署与验收（2026-08-06）

- 用户明确授权“推送部署”。工作分支 `codex/backend-gitops-completion` 已从 `a8110cd` 推送到
  `3bdff5738762436d120560a46ef1888affe181fd`，包含 6.64/6.65 的五个本地提交；GitHub
  `refs/heads/codex/backend-gitops-completion` 已通过 `ls-remote` 核对为同一提交。未创建 PR、未合入
  `main`，也未把 `TODO.md` 或协作者的 Runner `Properties/`、本地端口 overlay、实例状态 composable、
  Query client、对应测试和 `scripts/` 未提交工作带入发布。
- 生产主机继续使用 `/root/NoCTF` Compose checkout、既有 `/root/NoCTF/.env` 和未跟踪的
  `deploy/docker-compose.prod.yml` HTTPS overlay；没有修改证书、环境密钥、PostgreSQL/Redis 数据卷、
  上传目录或 Docker Runtime 直连随机宿主端口方案。因生产外网不稳定，本次从固定到 `3bdff57` 的本地
  干净 worktree 构建 API/Worker/Runner，增量 Git bundle 与共享镜像包分别以 SHA-256
  `A0030F359947C75297EED80D96FDD551EA92D12E8C92E61CD400ECE2714AA50F`、
  `EA48B10DFABFB9F31FB9ED197B7518476259C08DB18FF93B94E9CD3A5F35D6BE` 校验后离线传输。
- 生产 checkout 通过 bundle prerequisite 验证从 `8b0bf55` 无冲突 fast-forward 到 `3bdff57`。Compose
  静态解析通过；独立 migration 容器以 0 退出并应用
  `20260806103231_AddGlobalTeamProfiles`，数据库确认 migration history 和 `team_profiles` 均存在。
  迁移成功后才重建 Backend、Worker、Runner；最终镜像分别为
  `5f4d83ae1451c44c7767b22afb621a6cd426d4e8fe2c1d07f9f5d66f9323f0ed`、
  `4746c10c1ef30b5ddb38cd8b54c721b27178aca47fe9d7692fca840497a9c0ed`、
  `432cd4e7664ea0fdca1674463ac56912bcd2f802964ffc66933f373855e2f632`。
- Backend/Runner 最终 healthy，Worker running，三者 restart count 均为 0；公网 HTTPS `/health` 返回
  `{"status":"ok"}`，PostgreSQL `pg_isready` accepting connections、Redis `PONG`。从新容器各自
  `StartedAt` 起，精确匹配 .NET/Serilog `fail:`、`crit:`、`ERR`、`FTL`、unhandled exception、
  `InvalidServiceLocationException` 和 libgssapi 签名均为 0。先前宽匹配的 Worker/Runner 命中全部是
  EF SQL 列名 `failure_code`，没有误记为生产错误。公开 OpenAPI 已包含 `/api/v1/teams`，带浏览器
  `Accept: text/html` 的 `/teams` SPA fallback 正常。
- 内置浏览器刷新生产 SPA 后确认首页重复资料卡已经移除，导航使用 FFS 头像并链接 `/profile`；首页
  文案明确“选择已有队伍报名比赛”和“在队伍管理中创建可跨比赛复用的队伍”。`/teams` 在当前 0 支
  全局队伍时正常加载，右上角同时显示创建与邀请 Token 加入入口，空态没有重复按钮，也没有比赛
  选择器。尝试展开创建面板时浏览器控制会话超时，因此不把该点击记为通过；没有提交表单、创建
  测试队伍或修改任何生产业务数据。
- 发布后精确删除本次两份离线传输文件、一个两小时前成功退出的旧 `migration` 一次性容器和三张已
  无任何容器引用的旧应用镜像；均为可由 Git/镜像重建的部署产物。没有删除数据卷、数据库、Redis、
  上传文件、证书或当前镜像。根盘从镜像装载峰值 94% 使用、2.6 GB 可用恢复到 87% 使用、5.4 GB
  可用。生产 Git remote-tracking ref 已在 GitHub 提交核对后同步，工作树只保留原有 HTTPS overlay。
- 本节 HANDOFF 作为独立文档提交推送后，只需将生产 checkout 再 fast-forward 到该文档提交；运行
  镜像仍精确对应功能提交 `3bdff57`，无需再次迁移或重建服务。

### 6.67 已结束比赛归档与永久删除分流（2026-08-06）

- 用户最新明确纠正：管理端“删除”是物理删除，和可恢复的“归档”不是同一操作；已结束比赛应在
  二次确认后允许物理删除。该决策覆盖此前把管理列表“删除”误接到软删除的交互，也构成
  `competition_events` 永久保留规则的显式管理员删除例外。功能提交为
  `d837227 fix(competitions): separate archive and permanent deletion`。
- 原 `DELETE /api/v1/admin/competitions/{competitionId}` 仍保留为强类型归档端点，现允许归档
  `Finished` 比赛并在 OpenAPI 中明确可恢复语义；原
  `DELETE /api/v1/admin/competitions/{competitionId}/hard-delete` 现在可直接永久删除调用者拥有或
  Administrator 管理的 `Finished` 比赛。若仍存在 Queued、Provisioning、Running 或 Stopping
  Runtime 则 fail closed 返回冲突，避免容器资源失去追踪。
- 永久删除在同一个 PostgreSQL 事务中按外键依赖顺序移除比赛事件及父子事件链、通知、导出元数据、
  私密咨询及条目、Runtime 与历史端口、提交、计分事实、补丁上传、比赛 Flag、Hint、比赛题目实例、
  比赛报名快照、生命周期/榜单可见性审计和 Competition 本身；任一步失败会整体回滚。用户账号、
  全局 `team_profiles` 和可复用 Challenge 模板不删除。补丁上传及数据导出的对象 Key 会先写入同事务
  Wolverine `CleanupObject`，提交后刷新；即时刷新失败只记录告警，由 durable outbox 后续投递。
- 前端操作菜单现在明确分为“归档比赛”和“永久删除”：归档说明数据保留且可恢复；永久删除仅在
  `Finished` 行显示，继续使用 Pixel Industrial Dialog 做二次确认，并逐项说明比赛作用域数据会被
  物理删除、用户和全局题库模板保留。薄封装分别调用生成 SDK 的 `adminDeleteCompetition` 与
  `adminHardDeleteCompetition`，没有拼接 URL、路径、DTO 或失败码。OpenAPI 两份制品和 TypeScript
  SDK 已重新导出生成；路由和响应 schema 未改变，仅 operation 描述与生成注释更新，无 migration。
- 本地门禁：兼容 `global.json` 的 .NET SDK 10.0.302 Docker 环境中，完整 solution/test project build 均为 0 warning/
  0 error；non-Integration 601/601 passed；真实 PostgreSQL 定向
  `CompetitionManagementPersistenceTests` 2/2 passed，新增用例覆盖 Finished 比赛、队伍报名、比赛
  Challenge、Stopped Runtime、历史端口、CompetitionEvent 和 lifecycle audit 全部物理删除，同时
  User 与全局 Challenge 保留；EF pending model check 显示无漂移。前端删除交互测试 3/3 passed，
  scoped ESLint passed，`bun run build` 的 vue-tsc/Vite production build passed，只有既有 Rollup
  PURE annotation 与大 chunk 警告；`git diff --check` passed。
- 完整 Integration 在“SDK Linux 容器挂宿主 Windows Docker socket”的非标准环境运行 156 项，
  146 passed、2 个真实 Kubernetes/Libvirt 环境项按配置 skipped、8 failed。失败均与本次代码无关：
  3 个 GreenMail/密码通知用例因宿主临时目录不能作为嵌套容器 bind mount 或 SMTP 容器连接失败，
  3 个真实 Docker Runtime 用例因测试进程访问容器返回的 `localhost` 宿主端口被拒绝，2 个既有
  advisory-lock 并发观测在全量高并发资源争抢下超时；新增永久删除用例在定向和全量运行中均通过。
  不把该受限环境结果误记为完整 Integration 通过。
- 当前仍保护协作者的 `TODO.md`、Runner `Properties/`、本地端口 overlay、实例状态 composable、
  Query client、对应测试和 `scripts/`。本节完成时尚未 push、部署或删除生产数据；下一步按本轮用户
  明确授权推送功能与 HANDOFF，重新部署 API/前端，再只对生产中精确核对的 `deploy-smoke-*`
  Finished 测试比赛执行永久删除并做数据库、HTTPS 和浏览器验收。

### 6.68 已结束比赛永久删除生产部署与测试数据清理（2026-08-06）

- `d837227 fix(competitions): separate archive and permanent deletion` 与首轮文档提交
  `84406cc docs(handoff): record competition deletion split` 已推送到
  `origin/codex/backend-gitops-completion`。从干净 worktree 构建的生产 API/前端镜像为
  `sha256:8bdc9bf2050f8203cc2e6655dcbd70e0dfc0ba231987fe62b37d254a9ee7ac3f`；离线镜像和增量
  Git bundle 均经 SHA-256 校验后导入生产，生产 checkout 从 `750a1543` fast-forward 到
  `84406cc8`。Compose 静态配置通过，migration 容器报告数据库已是最新、没有应用 migration。
- 生产仅重建 `deploy-backend-1`，Worker、Runner、PostgreSQL、Redis、数据卷、上传卷和 HTTPS
  证书均未替换。新 API 容器 `running/healthy`、RestartCount 0，公开
  `https://noctf.fa1lsnow.com/health` 为 200，PostgreSQL accepting connections，Redis PONG；
  从新容器 StartedAt 起以及永久删除完成后的 API 日志中，Error/Fatal/Critical/Unhandled/Failure
  关键字计数均为 0。
- 破坏性操作前按精确白名单重新核对了 8 个生产测试比赛：
  `deploy-smoke-20260731T182509Z-{1,2}`、`deploy-smoke-20260731T190254Z-{1,2}`、
  `deploy-smoke-20260731T190449Z-{1,2}`、`deploy-smoke-20260731T190631Z-{1,2}`。8 个记录均为
  `Finished`、均未归档；其 Runtime 全部为 `Stopped` 或 `Failed`，没有 Queued、Provisioning、
  Running 或 Stopping 实例。随后只对这 8 个已核对 UUID 逐个调用新强类型 hard-delete API，
  8/8 均返回 204，未对任何非白名单 Competition 执行删除。
- 删除后 PostgreSQL 只读验收确认：目标 Competition 0、`deploy-smoke-*` 0、当前全部 Competition 0；
  对 schema 中 13 张含 `competition_id` 的表动态逐表查询，CompetitionChallenge、Event、榜单可见性/
  生命周期审计、咨询、导出、通知、Patch、Runtime、历史端口、计分、Submission 和报名 Team 的目标
  引用全部为 0。全局 `users` 仍有 3 条、可复用 `challenges` 仍有 4 条、全局 `team_profiles` 仍有
  1 条，符合“只删除比赛作用域、保留账号/全局题库/全局队伍资料”的契约。
- 内置浏览器实际登录生产管理后台验收 `/admin/competitions`：页面正常加载并显示“没有找到比赛”，
  左侧完整管理导航仍可见，浏览器 Console error 为 0。由于生产比赛已经按明确白名单全部清空，
  本轮没有再创建一次性比赛来重复点击确认框；确认框的 Archive/Permanent 分流由本地前端 3/3 测试、
  production build 与部署前源契约覆盖。
- 部署验收后精确删除本次 `/root/noctf-deploy-84406cc8` 两份离线传输文件及无任何容器引用的旧 API
  镜像 `sha256:5f4d83ae...`；这些均可由 Git/镜像重建。没有执行全局 image/container/volume prune，
  没有删除数据库、Redis、上传文件、证书、当前镜像或任何数据卷。根盘从镜像装载峰值 89% 使用、
  4.5 GB 可用恢复到 87% 使用、5.4 GB 可用。
- 本节作为独立 `docs(handoff)` 提交推送后，只需将生产 checkout 再 fast-forward 到该文档提交；
  运行镜像仍精确对应功能提交 `d837227`，无需再次迁移、重建服务或重复删除生产数据。本轮
  push/deploy/物理删除授权至此消费完毕。

### 6.69 管理题库“包含已删除”卡死与过滤语义修复（2026-08-06）

- 用户报告管理后台题库页点击“包含已删除”后页面卡死。实际浏览器逐项排查确认，根因是
  TanStack Table 在 Vue Query 返回新数组时自动重置页码，同时页面 watcher 再次写入页码，形成
  响应式更新循环；`vAutoAnimate` 不是根因，已保留原有表格动画。功能提交为
  `4d771cb fix(challenges): prevent deleted filter freeze`。
- 前端表格现关闭 `autoResetPageIndex`，仅由搜索 watcher 和显式生命周期切换函数控制页码；切换
  已删除题目时只在当前页不是第一页时写入一次页码，然后更新筛选状态。实际浏览器通过生产 API
  代理验收“包含已删除”→“隐藏已删除”→“包含已删除”双向连续切换，页面保持响应、4 行题目稳定、
  Console error 为 0。
- 排查同时发现后端默认列表和 `includeDeleted=false` 都会返回软删除题目。原因是投影中的关联比赛
  题目计数使用 `IgnoreQueryFilters()`，EF Core 将其作用扩散到组合查询的外层 Challenge。题库 Store
  现在统一从无全局过滤源开始，并在 `includeDeleted=false` 时显式追加 `DeletedAt == null`；列表和
  详情行为一致，显式 `includeDeleted=true` 仍可取回软删除记录。
- 新增真实 PostgreSQL 集成断言，覆盖软删除模板后默认 List/Find 不可见、显式包含时可见；兼容
  `global.json` 的 .NET SDK 10.0.302 容器中完整 solution build 为 0 warning/0 error，non-Integration
  601/601 passed，定向 `GitOpsPersistenceContractTests` 1/1 passed。前端题库定向测试 6/6 passed、
  scoped ESLint passed、`vue-tsc`/Vite production build passed，仅有既有 Rollup PURE annotation 与
  大 chunk 警告。更早同一轮的全量前端测试为 187 passed/1 failed；唯一失败仍是既有
  `competitionContract.test.ts` 对已拆分的 `competitionAdminApi.delete` 旧断言，与本修复无关。
- 本次没有 API 契约、OpenAPI、SDK、数据库模型或 migration 变化，也不需要递增平台运行版本。
  仍保护协作者的 `TODO.md`、Runner `Properties/`、本地端口 overlay、实例状态 composable、Query
  client、对应测试和 `scripts/`。本节完成时仅创建本地功能提交；6.68 已明确消费上一轮 push/deploy
  授权，因此本修复尚未推送或部署，生产仍运行旧版本，必须等待新的明确授权。

上述旧目标迁移和 2026-07-30 GitOps 后端收尾均已完成代码与本地验证。本轮新增重点：

- 方案 A 的 Platform Bot 创建/Access JWT 签发，稳定 UUID、`includeDeleted` 与精确恢复；
- Challenge `DefinitionJson` / CompetitionChallenge `RulesJson` 所有权边界；
- AWDP disposable target 同时固化 Competition configuration、CompetitionChallenge 和
  Challenge definition 三个 revision，任一变化均 PlatformFailed、清理且不自动重跑；
- Docker 直接随机宿主端口、E2E socket GID/清理修正和 shell fixture LF 约束；
- Kubernetes Container 动态 NodePort、强 identity replay 与 UID-precondition 清理；
- AWD checker callback sequence/version 的 PostgreSQL 行锁 fence 与旧 token 无副作用拒绝；
- 全部 LeaderboardRevision 运行时写路径的 PostgreSQL 原子递增与确定性并发回归；
- transactional invalidation、订阅感知刷新、Redis 单航班/CAS/失败恢复与 approved-team 投影；
- Frontend 生成类型、正确 Hub path、rejoin/heartbeat/polling fallback 与 202 snapshot 保留；
- 四模式全边界、真实 PostgreSQL、EF、OpenAPI 和 solution 门禁。
- Runtime provider cleanup/capacity 的完整 identity fence、replacement cleanup 状态机与
  离线 `Provisioning + no receipt` fail-closed；
- 团队 logical Runtime slot 事务额度、方案 A 必填 API 和 AWD start-time completeness。
- CTF 排行榜按题公开一血、二血、三血，顶层 blood summary 与队伍 slot 强类型一致。
- 独立 `/profile`、后端邮箱投影、TokenVersion 改密失效和受控头像解码/重编码。
- MailKit 双正文 SMTP 投递、显式 TLS 安全模式、可选认证和稳定脱敏失败契约。
- 排行榜 Normal/Frozen/Blackout、精确 cutoff、可信角色/BOT 服务端投影、自动揭榜及前端空态。
- 默认私密的题目/平台咨询、强类型状态与权限、Wolverine 通知和可选匿名公开回答。
- 单比赛永久不可变事件、三层可见性、签名分页/JSONL 导出、受审计 Flag 读取及可靠实时刷新。
- CTF/AWDP 跨队 Flag 隐蔽检测、事件派生裁决、受审计证据、封禁/更正和管理前端。
- 管理员平台运行日志每日 Redis 分片、固定保留/容量、强类型筛选、签名分页、JSONL 导出与事实
  投影审计。
- 安全忘记密码/邮箱重置、hash-only 单次 Token、账号/IP 双层限流、全会话失效、变更通知与
  generated-SDK 前端恢复流程。
- 完整比赛 ZIP 归档、平台审计 NDJSON、默认 Flag 脱敏/受审计明文例外、Wolverine 异步任务、
  24 小时对象/30 天元数据生命周期及 generated-SDK 管理前端。
- PostgreSQL 业务与 Wolverine durable state、S3 对象/元数据的一体化停写恢复点、age 加密、
  Minisign 来源认证、空目标 fail-closed 恢复及真实 Docker 自动演练。
- 每个不可变 TeamBanned 事实一次私密队长申诉、工作人员裁决/直接纠错、Finished 后排行榜重投影、
  无敏感内容的公开更正事实和 generated-SDK 前后端工作区。
- FastEndpoints/FluentValidation 全局 JSON 属性命名下稳定的 Validator 协议测试，以及普通参赛者
  `/teams` 私密封禁申诉的实际浏览器验收和完整 current-user mock 契约。
- 全局可复用 Team、当前成员/邀请 Token、比赛报名成员快照及不依赖比赛的创建/加入前端。

当前尚未执行的交付边界：

- 本地 `deploy-*` 六服务重建及真实 Bot GitOps apply/reapply/delete/restore 已完成；灾备工具的
  隔离 Docker 演练已完成，生产 Compose 已按 6.68 部署并验收；监控、备份调度/异地保留和生产
  恢复验收尚未执行。
- 生产 Kubernetes 安装、Runner Pool 运维参数落地与生产式 Libvirt 演练仍需目标环境。
- 工作分支已按用户指令推送并完成生产 Compose 部署；最新生产部署与测试比赛物理删除结果以
  6.68 为准。未创建 PR、未合入 `main`。
- `809f825`、`dbf534a`、`bf3d393`、`8dca6ef`、`dbccb72`、`c11328e`、`567ed02e`、
  `03647e7f`、`7b479f4`、`5923e57`、`92a6205`、`f20193c`、`948b02f`、`499f5cb` 及各自
  HANDOFF 提交已按对应明确授权推送；最新生产部署范围和结果以 6.68 为准。该授权不自动覆盖后续任务。
- Docker 公开访问只采用直接随机宿主端口映射；旧 firewall/gateway/ACL 条目已废弃，
  不属于剩余工作。

## 7. 建议的下一交接顺序

最新版 `TODO.md` 是后续优先级来源。P0 `/profile`、全局 Team/比赛报名快照、P0 MailKit SMTP、P1 排行榜冻结/黑灯、
P1 选手与出题人交流、P1 单比赛独立日志和 P1 CTF/AWDP 跨队 Flag 反作弊已分别由 6.49 至
6.54 完成；P2 管理员平台日志由 6.55 完成，安全忘记密码/邮箱重置由 6.56 完成，完整比赛与审计
数据导出由 6.57 完成，PostgreSQL/对象存储/Wolverine 灾难恢复工具与演练由 6.58 完成。
Administrator MFA 和分层限流均已由用户明确暂缓，封禁申诉/误判更正/公开更正通知由 6.59 完成。
当前 TODO 已没有获授权且不依赖真实环境细则的下一代码纵切：

1. 跳过 Administrator MFA 和分层限流，不继续此前八项限流 grilling，也不自行选择额度、代理信任
   或 Redis 故障策略。等待用户明确恢复其中一个范围，或提供新的产品纵切。
2. 不重做灾备工具、比赛/审计导出、平台日志导航、实时通道、死信队列、每日 Redis shard、固定保留/容量、
   管理审计投影或忘记密码流程；单比赛永久事实仍以 `competition_events` 为唯一来源。未经用户
   后续明确恢复范围，不实现或重新规划 Administrator MFA/分层限流。
3. 接口继续使用强类型 FastEndpoints，前端继续只使用 OpenAPI generated SDK/薄封装；完成后
   运行后端、真实依赖、EF、OpenAPI、Frontend 和浏览器门禁，做独立功能提交与 HANDOFF 提交。
4. 本轮 push/deploy/生产测试比赛物理删除授权已在 6.68 完成并消费；未经新任务明确授权，后续
   继续不 push、不部署。
   生产备份调度、异地保留和真实恢复仍等待单独运维授权。

真实 Compose/HTTPS 环境已按 6.68 部署；对尚未提供的 Kubernetes、Libvirt 与灾备环境细则：

1. 不猜测 Kubernetes namespace、Service DNS、NetworkPolicy/CIDR、Ingress/TLS、镜像
   registry/tag、replica/resource、对象存储、备份、监控或密钥来源。
2. 有正式 Kubernetes 环境后执行 `NOCTF_KUBERNETES_INTEGRATION` dataplane，并按真实
   Runner→API 路径落地最小网络允许规则与 Runner Pool 运维验收。
3. 有真实 Libvirt 环境与 fixture disk 后设置 `NOCTF_LIBVIRT_DISK_PATH`，执行 OVA
   import/public URL/exact cleanup 与生产式生命周期演练。
4. Compose 生产部署不等于监控、备份恢复或 Kubernetes/Libvirt 生产验收；这些仍需分别执行。
5. 本轮 push/deploy 授权不自动续用。仍不得自行创建 PR、合入 `main` 或再次执行生产部署。

不要重新引入 HAProxy/ingress、firewall executor/sidecar、transparent gateway、
TargetPort ACL 或 callback-only gateway。Docker Runtime 公开访问只使用题目服务自己的
Docker port mapping，host port 固定请求 `0`。

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
4. 旧目标迁移、后端收口、本地部署与 GitOps 实机演练无需重做。最新 Frontend 迁移状态
   使用 6.34 至 6.40；最新 outcome、上传补偿、Runtime scope 和 Patch draft Backend 状态
   使用 6.41 至 6.44；Runtime cleanup/replacement/quota 使用 6.45 至 6.47；CTF 排行榜
   三血契约使用 6.48；个人资料、邮箱可见性、头像安全和改密失效使用 6.49；MailKit、显式
   TLS 模式、可选 SMTP 认证和脱敏失败使用 6.50；排行榜冻结、黑灯、BOT/通知边界使用
   6.51；默认私密咨询、可选匿名公开回答和通知使用 6.52；单比赛不可变事件、三层可见性、
   导出、Flag 审计和实时刷新使用 6.53；跨队 Flag 检测、事件派生裁决、受审计证据、封禁和
   更正使用 6.54；管理员平台日志保留、筛选、签名分页、导出和投影审计使用 6.55；忘记密码
   流程以 6.56 的 hash-only Token、枚举防护、双层限流、全会话失效、密码变更通知和恢复前端为准；
   完整比赛/审计导出以 6.57 的单表任务、Repeatable Read、对象生命周期、Flag 边界和 generated-SDK
   前端为准；灾难恢复以 6.58 的停写一致性、完整 Wolverine schema、age+Minisign、外部 Secret、
   空目标和逐表/逐对象校验为准；封禁申诉与更正以 6.59 的 competition event 自关联、一次私密申诉、
   Finished 后重投影和最小公开事件为准；Validator 测试隔离和参赛者 `/teams` 浏览器门禁以 6.60
   为准。Administrator MFA 与分层限流均已由用户明确暂缓，不得
   继续实现或追问。等待用户提供新的产品纵切或真实环境部署细则；不要回滚或重做 `/profile`、SMTP、
   排行榜可见性、咨询、比赛日志、反作弊、平台日志、忘记密码、数据导出、灾备或封禁申诉流程。
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
