# 测试与验收

## 框架

统一 TUnit；NSubstitute 可用于纯单元测试。关系约束、事务、锁、Wolverine、Redis 不能用 Substitute/EF InMemory 证明，使用 Testcontainers PostgreSQL/Redis 与真实 Wolverine persistence。

## 单元测试

必须覆盖：

- Competition 状态机、EffectiveRunningTime、AWD/AWDP Round；
- Flag window、scope、SpecificationId、Round Guid 编码；
- Flag template、TEAMHASH、SafeLeetV1 固定表/强制变化/碰撞；
- CTF DynamicExpresso 默认/覆盖/边界/异常、assignment 禁用、Reflection 不可达、未知 identifier/额外类型拒绝、decimal rounding/overflow；
- 四模式投影、血奖、罚分、所有 tie-break；
- GameplayFact 次数、预占释放、当前结果覆盖和重判平台失败保留结果；
- 权限矩阵、Team/Competition UUID 数组不变量；
- 逻辑 URL 与 provider-neutral Runtime 配置验证、Runtime Queued/Reset 新 UUID、每次 Checker 独立事实及异常退出/超时；
- Problem code/result mapping。

## PostgreSQL 集成

验证真实：GameplayFact 字段矩阵 check、PatchUpload Reference 部分唯一索引、uuid[]/GIN、jsonb、advisory lock 并发、尝试次数竞争、Dirty competition 领取、500 条 SKIP LOCKED drain、当前结果覆盖、Hint 并发扣分、Wolverine Inbox/Outbox/DLQ。禁止 EF InMemory 证明关系行为。

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
- Compose YAML 拒绝项、Docker Compose 与 Kompose manifests 后置验证；
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

Kubernetes Compose 真实集成要求一个启用 CoreDNS 与 NetworkPolicy enforcement 的测试
集群，并设置：

```text
NOCTF_KUBERNETES_INTEGRATION=true
NOCTF_KOMPOSE_PATH=/usr/local/bin/kompose
```

测试会创建唯一临时 Namespace，真实执行 Kompose 与 Kubernetes API，验证 Compose
短名 DNS、`publishNotReadyAddresses`、同 Runtime 互通、跨 Runtime 拒绝、动态 NodePort
和删除收敛，然后删除该 Namespace。若 API endpoint 是本机地址，还必须将它加入
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

### 无容器开发宿主

`Development` 环境由 `NoCTF.Host` 单进程承载全部角色的 Wolverine handlers，使用
EF Core InMemory、Wolverine 本地内存队列、FusionCache L1 和 Runner 容量门。生命周期、
Runner assignment、AWD checker 的维护消息也由该进程启动；排行榜投影和 SignalR 通知
仍执行真实应用逻辑，只省略 Redis L2、backplane 和通知中继。生产使用相同的排行榜与
订阅实现，仅通过配置为 FusionCache 附加 Redis。它不会连接 PostgreSQL 或 Redis，也不要求任何
依赖容器；若本机 Docker Engine 可用，内置 Runner 会通过 Docker 的 `bridge` 网络直接
创建题目 Runtime，否则只有 Runtime 操作不可用，API 仍可启动。数据只在进程生命周期内
存在，且该模式不证明 PostgreSQL 约束、事务、锁或 Wolverine durable inbox/outbox 行为。

FusionCache 按用途分为三个命名 profile：`leaderboards` 承载排行榜，`read-models` 缓存平台配置与
公开比赛查询，`local-computation` 缓存 Runtime/AWD/KoH 配置 JSON 的解析结果。生产环境中
前两者使用 Redis L2 与 backplane 进行跨进程失效；纯计算结果只保留进程内 L1，避免把可由
输入稳定重建的数据写入 Redis。排行榜快照由 Dirty 刷新替换，不使用短逻辑 TTL；其他 TTL
可通过 `Caching:ReadModelsTtlSeconds` 与 `Caching:LocalComputationTtlMinutes` 调整。

从 `backend` 目录启动：

```bash
dotnet run --project src/NoCTF.Host/NoCTF.Host.csproj --launch-profile Development
```

默认地址是 `http://localhost:5080`，开发管理员是 `dev-admin`，密码是
`dev-admin-change-me`。这些值仅来自 `appsettings.Development.json`，可用环境变量
覆盖。无容器 E2E 烟雾测试默认自托管 Development API：

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

Full 在模式业务流程之后还会验证 API 重启后原 JWT 有效、Redis 停止时认证回退
PostgreSQL、Redis 恢复后 Runner heartbeat 重建，以及 PostgreSQL 重启后三进程重新
连接。服务停止和重启由编排器负责，成功条件只通过 HTTP 与 heartbeat 判断。

E2E 只通过 HTTP、排行榜、GameplayFact、Runtime 管理 API 和 Runtime 对外行为判断
业务结果。不查询 PostgreSQL，不使用 Docker inspect/资源列表证明业务成功。
Runner/数据库/Wolverine 的精确持久化事实和 KoH failure code 映射继续由真实依赖
集成测试负责。

静态验收命令：

```bash
dotnet build backend/tests/e2e.cs
dotnet build backend/tests/NoCTF.E2E/NoCTF.E2E.csproj --no-restore
```

## CI 门槛

Release 必过：build、analyzer、format、TUnit、Testcontainers integration、OpenAPI drift、migration drift/空库 apply、无 Penetration/RuntimeOperation/旧 API contract 架构测试。
