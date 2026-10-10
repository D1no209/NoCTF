# 测试与验收

## 框架

统一 TUnit；NSubstitute 可用于纯单元测试。关系约束、事务、乐观并发、JetStream、NATS KV 与 Redis 不能用 Substitute/EF InMemory 证明，使用 Testcontainers PostgreSQL/NATS/Redis 与真实 Wolverine transport。

## 单元测试

必须覆盖：

- Competition 状态机、EffectiveRunningTime、AWD/AWDP Round；
- Flag window、scope、SpecificationId、Round Guid 编码；
- Flag template、TEAMHASH、SafeLeetV1 固定表/强制变化/碰撞；
- CTF DynamicExpresso 默认/覆盖/边界/异常、assignment 禁用、Reflection 不可达、未知 identifier/额外类型拒绝、decimal rounding/overflow；
- CTF/AWD/AWDP/KoH 投影、血奖、罚分、所有 tie-break；LiveSolo 单独覆盖 Match/Round、受理顺序、资格、裁定与媒体隔离；
- GameplayFact 次数、预占释放、当前结果覆盖和重判平台失败保留结果；
- 权限矩阵、TeamMember/Captain/CompetitionCollaborator 关系不变量；
- 逻辑 URL 与 provider-neutral Runtime 配置验证、Runtime Queued/Reset 新 UUID、每次 Checker 独立事实及异常退出/超时；
- Problem code/result mapping。

## PostgreSQL 集成

验证真实：TPH discriminator、Complex Types、关系集合、普通唯一索引、UTC ticks、concurrency stamp、Serializable 重试、当前结果覆盖、Hint 并发扣分，以及没有 JSON/数组列、filtered index、方言 check constraint 或业务 Raw SQL。JetStream 集成验证至少一次重投、稳定业务幂等键、scheduled delivery、DLQ 和提交后重新派发；NATS KV 验证租约续期、接管与 fencing。

不得使用 EF InMemory 替代这些测试。

## API 测试

使用 FastEndpoints Testing/Factory：

- 每个 Endpoint ExecuteAsync/集成请求的 Typed Result 类型、body/status/content-type；
- Validator 400、401/403/404 隔离、409 revision/state、422 archive、429 Retry-After；
- OpenAPI 声明与实际 union；
- multipart Patch/Attachment 的用途级上限与稳定 413、Refresh Cookie 精确 `/api/v1/auth` Path/SameSite/Origin、cursor 签名；
- 玩家不能读取其他队/原始正确 Flag；管理权限矩阵。

## Runner Contract

- Docker/Kubernetes Container lifecycle/labels/security/resources/URL mapping；
- 1–64 个命名服务、服务引用、资源求和、单服务共享网桥/多服务独立网桥、Kubernetes 每服务 Pod 与多服务发现；
- Libvirt Provider OVA SHA-256、tar traversal、OVF 多 VM/资源总预算、routed subnet、
  Guest Agent、VmId URL expansion、幂等 cleanup、Pool inventory dispatch 与
  数据库 assignment 精确 orphan reconciliation；
- 网络隔离与 orphan reaper；
- AWDP 按队 Attack Runtime、Runtime UUID Flag 环境变量/文件注入、Reset 失效、公开 URL；
- AWDP disposable Fix target 与 Attack Runtime 隔离、Patch/一次 Checker callback、非零退出、无 callback、timeout、重放与清理；
- AWD raw command injection 与轮次截止重试。

Provider 测试可按环境标记，但 Release 流水线必须至少在受控 Runner 环境执行。

Docker Integration 默认在 Docker 不可用时明确 skipped；发布门禁必须设置
`NOCTF_REQUIRE_DOCKER_INTEGRATION=true`，使不可用直接失败。

Kubernetes 命名服务真实集成要求一个启用 CoreDNS 与 NetworkPolicy enforcement 的测试
集群，并设置：

```text
NOCTF_KUBERNETES_INTEGRATION=true
NOCTF_KUBERNETES_CONTEXT=<隔离测试集群 context>
NOCTF_KUBERNETES_PUBLIC_HOST=<可访问 NodePort 的节点地址>
NOCTF_KUBERNETES_POD_PIDS_LIMIT=<与节点一致的 PID 上限>
NOCTF_KUBERNETES_CLUSTER_DNS=<集群 DNS Service IP>
NOCTF_KUBERNETES_TEST_IMAGE=<测试服务镜像>
```

测试使用 `KubernetesNamedServicesIntegrationTests`，通过 Kubernetes API 在独立临时 Namespace
验证单服务无 discovery Service、多服务短名 DNS、同 Runtime 互通、跨 Runtime/平台/元数据地址拒绝、
NodePort 和删除收敛。测试需要可查询的 `noctf/rustfs` Service，并在结束时删除测试 Namespace；
生产 Runtime 共用部署指定 Namespace，不逐 Runtime 创建 Namespace。若 API endpoint 是本机地址，还必须将它加入
`NO_PROXY`，避免 kubectl/.NET Kubernetes client 经 HTTP proxy 访问。

Libvirt/OVA 真实集成要求 Linux KVM/Libvirt 测试节点可访问 `qemu:///system`，并提供
`virsh`、`qemu-img`、`virt-install` 以及一个不会与节点现有委派冲突的
`10.253.240.0/24` 测试 CIDR。设置：

```text
NOCTF_LIBVIRT_DISK_PATH=/absolute/path/to/fixture.img
```

fixture 必须是可由 BIOS/KVM 启动的磁盘镜像，自带 QEMU Guest Agent，并为
`virtio_net` 接口启用 DHCP；测试不会下载、修改或向 Guest 注入这些依赖。测试运行时
自行把磁盘流式打包为临时单 VM OVA，验证 SHA-256、qcow2 转换、routed network、
Guest Agent IPv4、通过 Guest Agent 启动的 HTTP URL、旧 Runtime receipt stop、
Reset 新 Runtime UUID 与按完整 identity orphan cleanup。默认未设置变量时明确
skip；Release 的受控 Libvirt Runner 应设置该变量并使用专用 fixture。

## 端到端

### Development 宿主

`Development` 仍使用 PostgreSQL；Host 只注册 PostgreSQL provider，不支持共享内存 SQLite。
启动前配置 `ConnectionStrings:PostgreSql` 并准备数据库。SQLite 项目仅用于隔离模型测试。
Development 会 stub Wolverine 外部 transport，并关闭生产集群调度配置，因此开发宿主烟雾测试
不能证明 NATS 持久投递、集群恢复或生产调度。需要真实 Runtime 时还必须准备对应 Provider。
这些行为以 `NoCTF.Host/Program.cs` 和当前开发配置为准，生产验收使用真实依赖与 Production 配置。

FusionCache 按用途分为四个命名 profile：`leaderboards` 承载排行榜，`read-models` 缓存平台配置与
公开比赛查询，`webhook-test-statuses` 保存短期测试结果，`local-computation` 保存可重建计算结果。生产环境中
前三者使用 Redis L2 与 backplane 进行跨进程失效；纯计算结果只保留进程内 L1。
排行榜快照由事件失效后的完整投影替换，不使用短逻辑 TTL；其他 TTL
可通过 `Caching:ReadModelsTtlSeconds` 与 `Caching:LocalComputationTtlMinutes` 调整。

从 `backend` 目录启动：

```bash
dotnet run --project src/NoCTF.Host/NoCTF.Host.csproj --launch-profile Development
```

默认地址是 `http://localhost:5080`，开发管理员是 `dev-admin`，密码是
`dev-admin-change-me`。这些值仅来自 `appsettings.Development.json`，可用环境变量
覆盖。Development E2E 烟雾测试默认自托管 API；依赖与测试 fixture 配置见对应测试项目：

```bash
dotnet test tests/NoCTF.E2E/NoCTF.E2E.csproj \
  --treenode-filter "/*/*/*/*[Category=DevelopmentE2E]"
```

也可在 API 运行时测试外部进程：

```bash
NOCTF_E2E_BASE_URL=http://localhost:5080 \
NOCTF_E2E_ADMIN_USER=dev-admin \
NOCTF_E2E_ADMIN_PASSWORD=dev-admin-change-me \
dotnet test tests/NoCTF.E2E/NoCTF.E2E.csproj \
  --treenode-filter "/*/*/*/*[Category=DevelopmentE2E]"
```

本地 E2E 使用 .NET 10 file-based app 统一编排，不依赖 PowerShell、Bash、WSL、
固定端口或当前工作目录。入口位于 `backend/tests/e2e.cs`，默认运行四种模式的
Full 套件：

```bash
dotnet run --file backend/tests/e2e.cs
dotnet run --file backend/tests/e2e.cs -- --mode ctf --suite smoke
dotnet run --file backend/tests/e2e.cs -- --mode all --suite full
dotnet run --file backend/tests/e2e.cs -- --mode awd --keep-environment
```

已有同一源码构建的 Host 镜像时，可设置 `NOCTF_E2E_HOST_IMAGE` 跳过 Host 镜像重建；
题目与 Checker fixture 仍由编排器构建。例如：

```bash
NOCTF_E2E_HOST_IMAGE=noctf-host:matching-source-revision \
  dotnet run --file backend/tests/e2e.cs -- --mode all --suite full
```

`--mode` 可取 `ctf`、`awd`、`awdp`、`koh`、`all`；`--suite` 可取 `smoke`、
`full`。编排器从自身源文件位置定位仓库，为每个模式生成唯一 Compose project、
network、volume、镜像、Runner pool 和随机凭据，以 Docker 分配的 backend host
port 启动三个只运行 `NoCTF.Host.dll` 的角色容器，等待健康检查与 Runner heartbeat 后在宿主机运行
TUnit。失败时输出 Compose 状态以及各角色 Host 日志。默认总会清理；
统一宿主测试还覆盖全部七种非空角色组合与单进程 durable routing。
`--keep-environment` 会保留现场并打印精确清理命令。

API、Worker 与 Runner 的部署配置共同指定 Docker placement。题目 Definition 和
比赛题目 Rules 均不包含 `provider` 或 `runnerPool`；Checker 只通过隔离网络的内部
DNS 访问目标，题目也不提供 Checker target URL/port。

Smoke 覆盖每种模式至少一条真实依赖流程：

- CTF：注册/队伍/附件或 Runtime/Flag/血奖/Hint/重判；
- AWD：加固/轮换/批量攻击/重复/服务 Up-Down/轮末投影；
- AWDP：两队独立 Attack Runtime/端口、真实动态 Flag、错误/外队/旧 Runtime Break、无需 Break 的 Fix、一次 Checker、Break/Fix 持续按轮叠加、Pause/Resume/Finish 冻结、页面状态恢复；
- KoH：共享 Runtime/Control Flag、正确/错误/不可用/超时/歧义行为、暂停恢复。

Full 在模式业务流程之后还会验证 API 重启后原 JWT 有效、Redis 停止时认证和 EF Core
共享准入继续工作、Runner 的 NATS KV 心跳与资源观测不受影响，以及 Redis/PostgreSQL
重启后三进程恢复。服务停止和重启由编排器负责，成功条件只通过 HTTP readiness 判断。

E2E 只通过 HTTP、排行榜、GameplayFact、Runtime 管理 API 和 Runtime 对外行为判断
业务结果。不查询 PostgreSQL，不使用 Docker inspect/资源列表证明业务成功。
Runner/数据库/Wolverine 的精确持久化事实和 KoH failure code 映射继续由真实依赖
集成测试负责。

静态验收命令：

```bash
dotnet build backend/tests/e2e.cs
dotnet build backend/tests/NoCTF.E2E/NoCTF.E2E.csproj --no-restore
```

## 发布验收与 CI 范围

`repository-build.yml` 验证完整 Host 镜像可构建；`ci.yml` 发布镜像及配置包，
`backend-only.yml` 手动发布后端镜像，`docs-pages.yml` 构建文档。不要把镜像构建成功当作完整测试通过。
LiveSolo 不在上面的四模式 `e2e.cs --mode all` 范围内，其证据与缺口见[验收状态](live-solo-status.md)。

发布前应单独完成相关门禁（可用 `backend/scripts/Verify-Backend.ps1`）：build、analyzer、format、TUnit、Testcontainers integration、OpenAPI drift、migration drift/空库 apply、无 Penetration/RuntimeOperation/旧 API contract 架构测试。
