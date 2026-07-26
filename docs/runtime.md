# Runtime 规范

## 类型与 Provider

RuntimeKind：

- `Container`：单容器；Provider Docker/Kubernetes。
- `Compose`：原始 compose.yaml；Provider Docker/Kubernetes（Kompose）。
- `OvaVm`：Libvirt/QEMU/KVM，多 VM Appliance。

RuntimeKind 与 RuntimeProvider 是独立 enum。Runtime 配置包含 RunnerPool，任务进入对应 Wolverine durable queue。

模式兼容矩阵：

| 模式/用途 | Container | Compose | OvaVm |
|---|---:|---:|---:|
| CTF Static/PerTeamRuntime | 是 | 是 | 是，但 FlagSource 只能 Static |
| AWD 长期按队 Runtime | 是 | 是 | 否 |
| AWDP 一次性 Fix target | 是 | 否 | 否 |
| KoH shared Hill | 是 | 是 | 是 |

OVA 永远不接收平台生成的动态/PerTeam/AWD Flag，也不执行 Flag 注入命令；“固定 Flag”必须作为 ChallengeFlag 预先存在并由题目镜像/虚拟机自身配置使用。

## RuntimeInstance 生命周期

```text
不存在 -> Queued -> Provisioning -> Running -> Stopping -> Stopped
             \----------\----------\----------\-> Failed
                                                    \-> Stopping (Reset cleanup)
```

不存在不是持久化状态；真实实例从 Queued 开始。每次 Start/Reset 创建新 RuntimeInstance/Generation；Stopped/Failed 历史实例保留。容量不足只保持 Queued，Runner 接单并预留容量后才进入 Provisioning。Provider 报告资源 Running 即 Running，不做 ReadinessProbe/Health Check。

API Start/Stop/Reset/Extend 返回 202、RuntimeInstanceId 与状态 URL。相同实例同时只允许一个状态变更；通过 State+ProcessingVersion 防重复/迟到。Failed 不公开 URL，可再次 Start 新 Generation。

玩家动作按模式固定：CTF 允许 Start/Stop/Reset/Extend；AWD Runtime 由比赛生命周期自动 Start/Stop，玩家只允许 Reset；KoH shared Runtime 仅 Manager 管理；AWDP 一次性 target 没有玩家 Runtime API。管理动作也必须遵守相同状态机，只有系统 Competition Finish/Ban 清理可跳过玩家动作权限，但不能跳过版本栅栏。

状态动作固定如下：

- Start：没有 Queued/Provisioning/Running/Stopping 实例时，在题目级事务锁内创建 `generation=max+1` 的 Queued 实例；若最新 Failed 仍有 receipt，新实例以它为 replacement 并先执行幂等清理。已有活动实例返回 409 `RuntimeAlreadyActive` 和该实例 Id。
- Stop：仅 Queued/Provisioning/Running 可接受。Queued 可直接变 Stopped；其余变 Stopping 并投递销毁。Stopped/Failed 返回 409 `RuntimeNotActive`，Stopping 返回原实例的 202。
- Reset：仅 Provisioning/Running/Failed 可接受。在同一事务把旧实例置 Stopping，创建带 `replacesRuntimeInstanceId` 的新 Queued Generation；即使旧状态 Failed，清理仍幂等执行。新实例必须等旧资源完成清理后才能 Provisioning。这个替换对并发额度只占一个槽位。
- Extend：只修改 Running 实例的 ExpiresAt 和 ProcessingVersion，并投递带新版本的到期消息；旧到期消息到达时版本不符即 superseded。

GET 返回最新 Generation；活动实例优先于历史实例。Queued/Provisioning/Stopping 只返回状态和 Id，Running 才返回 URL，Stopped/Failed 返回终态；Failed 返回强类型 RuntimeFailureCode。Provider receipt 与原始错误只向管理者返回。

## CTF 按需与 TTL

CTF PerTeamRuntime 不预创建。首次 Start 时创建，每个 Team/CompetitionChallenge 同时最多一个。团队成员共享。

- RuntimeLifetimeSeconds >0；进入 Running 时 `ExpiresAt=now+lifetime`。
- 只有 `0 < remaining < 10 minutes` 可 Extend；更新为 `now+lifetime`，不累加旧时间。
- 已到期/开始回收返回 409 RuntimeExpirationStarted。
- TTL 使用真实 UTC，Paused 不冻结。
- Stop 销毁资源和运行数据；Reset=销毁后立即新 Generation；固定 PerTeam Flag 不变。
- MaxConcurrentRuntimeInstancesPerTeam <=0 无限；Container/Compose/OvaVm 各算一个实例。额度统计 Queued/Provisioning/Running/Stopping，Reset 的前后 Generation 合并算一个替换槽。AWD Start Gate 要求额度覆盖全部题；AWDP disposable/KoH shared 不计每队额度。

## URL Binding

公开连接最终只保存/返回完整字符串数组：

```json
{ "urls": ["http://host:31001", "ssh://user:password@host:31002"] }
```

配置按顺序声明多个 Binding：

- Container：UrlTemplate + ContainerPort + Exposure；
- Compose：UrlTemplate + ServiceName + ContainerPort + Exposure；
- OVA：UrlTemplate + VmId + GuestPort? + Exposure。

`Exposure` 只有 `OwnerOnly | Participants`。本队 Runtime GET 返回两类；AWD targets 对其他队只返回 Participants 且必须等加固期结束；KoH challenge detail 只返回 Participants；OwnerOnly 永不跨队。CTF 只允许 OwnerOnly，KoH 至少一个 Participants。Runner 展开时把全部 URL 与 Participants 的 0-based indexes 一起固化到 RuntimeInstance，因此之后配置变化不改变当前 Generation。响应仍只是按配置顺序筛选后的 `urls: string[]`，不暴露 Binding 元数据。

占位符大小写敏感：`{HOST}`、`{PORT}`。直接原文替换，不 URL encode；未知占位符拒绝保存。Container/Compose 必须声明目标端口并使用动态映射；模板应使用 HOST/PORT。OVA 的 GuestPort 可空：空时不得用 PORT，Runner 直接以 VM 可访问地址展开 HOST，不做端口转发；模板也可使用固定绝对 URL。展开后必须是绝对 URI。

只有 Running 向本队/管理者返回 URL。URL 可含凭据，不建 RuntimeCredential；停止后隐藏历史 URL。

## Container 配置

```text
Provider: Docker | Kubernetes
RunnerPool
Image
Command: string[]?
Environment: map<string,string>
FlagEnvironmentVariableName: string?     // PerTeam 时必填
UrlBindings[]
Resources: CpuCores, MemoryBytes, PidsLimit, EphemeralStorageBytes
EgressPolicy: DenyAll | InternetOnly
RuntimeLifetimeSeconds
OperationTimeoutSeconds
FlagSource: Static | PerTeam
```

Command null 使用镜像默认。`FlagSource=PerTeam` 时
`FlagEnvironmentVariableName` 必填；Static 时必须省略。环境变量名合法且不能使用
`NOCTF_`。Image 可为 tag/digest，不强制 pin；配置变化只影响新 Generation。

## EgressPolicy 与 Runtime 网络

`ContainerRuntimeDefinition` 与 `ComposeRuntimeDefinition` 使用强类型
`EgressPolicy: DenyAll | InternetOnly`，省略时为 `DenyAll`。OVA/Libvirt 首版不接受该字段。

- Docker Container/Compose：首版只支持 `DenyAll`。每个长期 Runtime 使用独立
  `internal` bridge network；Compose 中所有题目 network（包括平台补出的 default
  network）都会被强制设为 `internal: true`。题目容器不连接平台网络。同一 Runtime 内
  仍可互通；若存在公开 URL Binding，平台额外创建一个可信 HAProxy ingress，代理同时
  连接 Runtime 内部网络与 `noctf-network`，只把声明的 TCP 端口转发回目标
  service/container。代理的固定资源开销计入 Runner capacity，并随 Runtime receipt
  幂等创建、回滚和删除。`InternetOnly` 在保存校验和 Runner 执行边界均直接拒绝；当前
  没有通过 `DOCKER-USER` 修改宿主防火墙，也没有部署 Egress Gateway。
- Kubernetes Container/Compose：每 Runtime 创建 NetworkPolicy。`DenyAll` 只放行同一
  不可变 Runtime selector 的 Pod 互通、集群 DNS，以及平台声明的公开/内部检查端口入站。
  `InternetOnly` 在此基础上只增加 `0.0.0.0/0` IPv4 egress，并通过 `except` 排除平台
  内建特殊/私有地址和 Runner Pool 的 `ProtectedCidrs`；不生成 IPv6 放行规则。

`Runtime__Kubernetes__ProtectedCidrs` 是 Runner Pool 必填数组，必须覆盖 Pod、Service、
节点管理面、平台基础设施及其他不可由题目访问的非公网 IPv4 网段。平台会拒绝空数组和
非法/IPv6 CIDR，但不会自动发现集群网络。`NetworkPolicyRequired=true` 只是运维声明，
不是 CNI 能力探测；CNI 必须实际执行 NetworkPolicy。

该策略只表达地址级的 `DenyAll`/公网 IPv4 二选一，不提供域名、FQDN、目的端口白名单，
也不把 DNS 命名隔离视为安全边界。

本阶段威胁模型信任平台管理员、管理员配置与平台托管 ingress 镜像，不防管理员内鬼；
题目业务容器和选手输入仍按不可信处理。可信入口代理不改变 Checker 可由管理员配置并
同时连接题目网络和 `noctf-network` 的既定模型。

## Compose

题目保存原始 compose.yaml，允许自定义内部 network。Docker Runner 直接运行 Compose；Kubernetes Runner 使用固定版本 Kompose 转换后部署到平台统一配置的 Namespace。

保存与执行前解析 YAML，转换后解析 manifests。永久拒绝：privileged、host network/PID/IPC、hostPath/bind/named/external volume、tmpfs/emptyDir/PVC、configs/secrets mount、device、Docker socket、高危 capability、题目 Namespace/Ingress/NodePort/LoadBalancer。平台覆盖隔离标签、NetworkPolicy、资源限制与清理标签。

不支持任何目录/卷挂载；只使用镜像自身可写层。Kompose 不支持字段、转换失败或危险资源使实例 Failed。

Compose URL Binding 通过 ServiceName+ContainerPort 定位；配置顺序决定返回顺序。

Compose 的 `FlagEnvironmentVariables` 是 `serviceName -> environmentVariableName` 映射。
`FlagSource=PerTeam` 时至少配置一个目标 service，且只能引用 Compose 中真实存在的
service；Static 时必须省略。未列出的 service 不接收 Flag。

Kubernetes Compose 固定使用 Kompose `v1.38.0`，题目不得创建 Kubernetes Service。
平台为公开 URL Binding 创建动态 NodePort Service；题目转换出的 Service 只用于检查端口语义，
不会直接部署。首版每个 Compose service 只允许一个副本。

Kubernetes Compose service 名必须满足
`^[a-z]([-a-z0-9]*[a-z0-9])?$` 且长度为 1～63。平台不做 `_`、`.`、
大写字母等归一化，非法名称直接拒绝。

每个 Runtime 使用不可变 RuntimeInstance Id 生成唯一 `rt-<runtime-id>` 名称，并创建一个
`clusterIP: None`、`publishNotReadyAddresses: true` 的 headless Service。每个业务 Pod 的
`hostname` 是 Compose service 名，`subdomain` 是该 Runtime 名，DNS search domain 是
`<runtime>.<namespace>.svc.<clusterDomain>`。`ClusterDomain` 由 Runner Pool 显式配置，
不得在代码中假定为 `cluster.local`。Compose project name 只作为展示 annotation，
不参与 Kubernetes 资源唯一性或 selector。

在 `dnsPolicy: ClusterFirst` 下，自定义 search domain 会追加在 kubelet 的基础搜索路径之后。
因此 Runtime Namespace 内的平台资源只能使用保留的 `platform-*` 前缀，Runtime 资源统一使用
`rt-*`，不得创建 `web`、`db`、`redis` 等可能遮蔽 Compose 短名称的公共 Service。

本方案解决共享 Namespace 中不同 Runtime 的 Compose 服务名称冲突；Runtime 之间的网络安全
隔离由 NetworkPolicy 提供，DNS 命名本身不作为安全边界。

每个 Runtime 的 NetworkPolicy 只允许同一不可变 runtime-id 的 Pod 互通、访问集群 DNS，
以及对公开 URL/平台内部检查端口的入站访问；其他 Runtime 的 ingress/egress 均不放行。
`publishNotReadyAddresses` 只保证初始化阶段可以解析名称，Runtime 进入 Running 仍以
Deployment 可用状态（包含 Pod readiness）为准。

Kubernetes PID 预算由 Runner Pool 的 kubelet `PodPidsLimit` 统一执行。题目未声明 PID
limit 时直接接受；历史 Compose 中 `pids_limit` 或
`deploy.resources.limits.pids` 与 Pool 值相等时接受并在转换前移除，不同则拒绝。题目不必
重复 Pool 数值，Kubernetes manifests 也不携带题目级 PID limit。容量按
`service 数 × PodPidsLimit` 预留。

## OVA/Libvirt

平台不上传、存储或管理 OVA。配置保存 `OvaSourceUrl` 与必填的 64 位十六进制
`Sha256`，允许 Provider 支持的绝对 `https`/`file` URI。Runner 下载或读取后必须先校验
SHA-256，再以摘要作为内容寻址缓存键；摘要不匹配时不得解包或启动 VM。`file://`
路径必须在同 Pool 候选节点保持一致挂载。

首版接受包含且仅包含一个 OVF descriptor 的未压缩 OVA tar。descriptor 可以直接包含
一个 `VirtualSystem`，也可以包含一个由多个直接子 `VirtualSystem` 组成的
`VirtualSystemCollection`；每台 VM 必须具有唯一、非空的 `ovf:id`，该值原样作为
`VmId`。嵌套 collection、ScaleOut、DeploymentOption、archive link/path traversal、
缺失资源和 `qemu-img` 不支持的磁盘格式均拒绝，不进行名称归一化或工具猜测。

每台 VM 使用 OVF 声明的 CPU 与内存；两者的 appliance 合计不得超过 Runtime 总预算。
OVA 的 `PidsLimit` 只参与 Runner 容量预留，不表示或尝试限制 Guest 内部进程。每台 VM
的磁盘转换为节点工作目录下独立的 qcow2。

一个 OVA 可含多 VM，整体作为 Appliance：Start/Stop/Reset/Expire 原子作用于全部 VM，
不允许选手单独操作。每个 Runtime 创建独立 routed Libvirt network，各 VM 可互通。
需要 URL 的 VM 使用 QEMU Guest Agent 获取该 Runtime 子网内的唯一 IPv4 地址；任一 VM
停止、地址重复或发现超时都会使整个实例失败并触发整组清理。OVA 只允许 Static Flag，
不注入 PerTeam Flag。

Worker 的 durable Runner assignment reconciliation 同时读取 Redis Pool membership，并向
所有在线节点投递资源审计。节点只接受自己的 Pool/RunnerId，并按本节点配置的强类型
Provider 枚举 `noctf.io/managed=true`、`noctf.io/job-kind=persistent-runtime`、
RuntimeInstanceId 与 Generation 完整匹配的资源。只有数据库中相同
RuntimeInstanceId+Generation 仍处于 Provisioning/Running/Stopping、Provider 相同且仍
归属该节点时才保留；Redis/数据库不可用时不清理。旧 Generation、已改派节点、终态或
无业务事实的 Docker Container/ingress/network、Docker Compose project/workdir、
Kubernetes Pod/Deployment/Service/NetworkPolicy 和 Libvirt appliance 均按完整 identity
精确删除。持久资源不使用 Provider 创建时刻推导的业务 TTL；TTL reaper 只处理带
`expires-at` 的 disposable 资源，并由同一节点审计消息驱动。

资源审计不释放 Runner capacity。容量 claim/release 仍只由 RuntimeInstance assignment、
ProcessingVersion 与 release token 的 durable 状态机处理，避免资源清理与容量账本发生
双重释放。

## Flag 注入

### CTF Container/Compose

PerTeam Flag 仅以环境变量在创建时注入。Container 使用单个
`FlagEnvironmentVariableName`；Compose 使用 `FlagEnvironmentVariables` 为每个目标
service 配置一个合法变量名，可指定多个 service。Runner 覆盖 YAML/镜像同名值。
需要文件的镜像由自身 ENTRYPOINT 写入。平台不挂载 Flag 文件/Secret。

Start 在创建 RuntimeInstance 的同一事务内保证团队固定 Flag 存在。该事实使用
`SpecificationKind.RuntimeDefinition`，且 `SpecificationId=CompetitionChallengeId`；
Worker 只读取这个精确 scope。Reset 创建新 Generation，但继续复用同一 Flag。

### AWD 热轮换

题目配置 Shell 模板，例如：

```sh
echo ${FLAG} > /flag
```

Runner 在目标 Container/Compose service 执行 `/bin/sh -c`。`${FLAG}` 直接原文替换，不引用、不转义、不编码；模板作者是受信任管理者并承担 Shell 注入语义。模板至少出现一次 FLAG，未知占位符拒绝。AWD 不允许 OVA。

非零/超时/Provider 失败由 Wolverine 用同一当轮 Flag 重试，只重试到 ValidUntil，之后最终失败。Flag 窗口不根据实际注入成功时间改变。

## 网络安全

每个 Team/题/Generation 独立网络。永久禁止平台 API/Worker/Runner 管理地址、PostgreSQL、Redis、对象存储内网、云元数据、其他 Team Runtime。Compose 内同实例服务可互通。

EgressPolicy 默认 DenyAll；InternetOnly 仅公网+DNS，仍阻断私网与平台。入站只通过 URL Binding。

永久禁止 privileged、host namespace、Docker socket、host mount、device 与 SYS_ADMIN/SYS_MODULE 等高危 capability。默认 no-new-privileges/drop all；特殊高权限环境使用隔离 VM。

Kubernetes 使用统一 Namespace，平台注入不可覆盖标签：`noctf.io/runtime-instance-id`、`noctf.io/competition-id`、`noctf.io/competition-challenge-id`、`noctf.io/team-id`（shared 空缺）、`noctf.io/generation`、`noctf.io/managed=true`。Docker/Compose 使用完全相同的 labels。清理器必须同时匹配 managed、RuntimeInstanceId 与 Generation，不能按宽泛 Competition/Team 标签批量误删。

## 资源与容量

部署配置各 Provider/Pool 最大 CPU、内存、PID、临时磁盘。题目必须声明正 CPU/内存请求；
Docker Compose 声明逐服务 PID，Kubernetes Compose 的 PID 由 Pool 统一提供。Compose
声明服务与总额度；Runner 创建前验证并强制应用。无法落实配额则失败；容量不足保持 Queued
且 TTL 未开始。

Runner heartbeat/capacity 存 Redis TTL；实际 RunnerId/Pool 与 receipt 存 RuntimeInstance。Redis 故障不派发新实例。

## Checker 调度字段

AWD Checker 不建立 Operation 表。每个 AWD RuntimeInstance 保存 `CheckerSequence`（已分配的最大序号）、`LastAppliedCheckerSequence`、`LastAppliedCheckerBodySha256` 与 `NextCheckerDueAt`：

1. Worker 在短事务中锁 RuntimeInstance，若已到期且没有更新序号的任务，就递增 CheckerSequence、推进 NextCheckerDueAt，并写 Runner Outbox；
2. durable message 带 RuntimeInstanceId、Generation、CheckerSequence、Deadline 和最小权限 JWT；
3. callback 先把 typed state 规范化为精确 ASCII `Up`/`Down`，body hash=`SHA256(UTF8(normalizedState))`；序号大于 LastApplied 时比较当前服务状态，只在 Up/Down 发生变化时插入 AwdServiceStatus，无变化也更新 LastApplied 序号与 body hash；
4. callback 序号等于 LastApplied 且 body hash 相同是幂等成功，不同返回 409；更小返回 202 superseded；
5. Paused 清空 NextCheckerDueAt 且不补跑，Resume 将它设为当前时间。Finished 后不再分配序号。

这些字段只负责调度和 HTTP 重试幂等，不是计分状态副本；轮次末状态仍从默认 Up 加 AwdServiceStatus 变化事件推导。
