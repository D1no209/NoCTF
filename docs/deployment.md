# 部署边界

## 必需组件

```text
NoCTF.API       >=1
NoCTF.Worker    >=1，可水平扩展
NoCTF.Runner    >=1，可按 Pool/Provider 扩展
PostgreSQL
Redis
S3-compatible object storage（LocalFileSystem 仅非 HA）
Frontend/reverse proxy
```

不支持单进程部署。API 不运行 Worker/Runner 业务 HostedService。

## 进程权限

- API：业务 DB、Wolverine Outbox、Redis、ObjectStorage、公开/内部 HTTP；无 Docker/Kubernetes/Libvirt 权限。
- Worker：业务 DB、Wolverine queues、Redis、ObjectStorage；无宿主 Runtime socket。
- Runner：所需业务表和 Wolverine runner queues、Redis heartbeat、内部 archive 读取 API，以及特定 Provider 权限；数据库 role 不授予 User/认证配置写权限，也不需要对象存储通用凭据。

Runner 管理端口只在内部网络。Checker callback API 可达，但严格 JWT audience/permission。

## Provider 前置条件

- Docker Pool：Docker daemon，禁止把 socket 暴露给题目 Container。平台网络统一命名为
  `noctf-network`；Runner 必须配置 `Runtime__Docker__Network=noctf-network` 和可信
  `IngressProxyImage` 及其固定内存/CPU/PID 开销。Compose 部署保持 Runner 为非 root，
  并将 `DOCKER_SOCKET_GID` 设置为宿主 `/var/run/docker.sock` 的数字组 ID（Linux 可用
  `stat -c '%g' /var/run/docker.sock` 查询；Docker Desktop 默认通常为 `0`）。
- Kubernetes Pool：统一 Runtime Namespace、支持 NetworkPolicy 的 CNI、固定 Kompose
  `v1.38.0` 与最小 Kubernetes API 权限。Pool 必须显式声明
  `Runtime__Kubernetes__Namespace`、`ClusterDomain`、`PodPidsLimit` 和
  `NetworkPolicyRequired=true`，并以 `ProtectedCidrs` 数组声明不得由题目访问的
  Pod、Service、节点管理面及平台基础设施 IPv4 网段。
- Libvirt Pool：QEMU/KVM/Libvirt、`qemu-img`、`virt-install`，Runner 可访问
  `OvaSourceUrl`；需要 URL 的 VM 具备 QEMU Guest Agent。Pool 必须声明
  `Runtime__Libvirt__CacheDirectory`、`WorkDirectory`、`PoolRoutedNetworkCidr`、
  `NodeRoutedNetworkCidr` 与 `RuntimeSubnetPrefixLength`。

同 Pool 节点对 file:// OVA 路径必须有一致挂载。Provider/Pool 不可用会阻止新 Runtime 派发，不影响静态 API。

`PoolRoutedNetworkCidr` 是基础设施向玩家入口发布路由的 Pool 总 IPv4 地址池。部署层必须
从中为每个 Runner 节点分配互不重叠的 `NodeRoutedNetworkCidr`；节点只在自己的切片内
通过确定性起点加空闲探测分配 Runtime 子网。`RuntimeSubnetPrefixLength` 在同 Pool
保持一致且不得大于 `/28`。若多个节点复用同一个 node CIDR，节点本地 Libvirt 无法发现
另一节点的占用，平台不保证避免地址冲突，因此部署必须拒绝该配置。

所有 Provider 的 orphan audit 都依赖 `runner-pool:<pool>:members` 保留稳定 RunnerId
membership；heartbeat 过期的节点不会收到新审计，节点以相同 RunnerId 恢复并重新上线后
会收到下一次 durable 审计。一个 Runner Pool 必须只运行一种 Provider，节点
`Runner__Provider` 必须与该 Pool 的 Runtime 配置一致。不要通过复用 RunnerId 把同一
Docker/Kubernetes 节点或 Libvirt node CIDR 同时交给两个宿主。

每个 Runner 节点必须配置 `Runner__Capacity__MemoryBytes`、
`Runner__Capacity__NanoCpus`、`Runner__Capacity__PidsLimit`、
`Runner__Heartbeat__IntervalSeconds` 和 `Runner__Heartbeat__TtlSeconds`。所有容量值和心跳周期
必须为正，TTL 必须大于刷新周期。Runner 只刷新已初始化的可用容量，不会覆盖已扣减值；
Redis 容量状态丢失且 PostgreSQL 仍有该节点活跃 assignment 时，节点保持离线，直到
assignment 收敛后才从部署配置重建容量。

`Runtime__Kubernetes__PodPidsLimit` 是 Runner 的容量与兼容校验值，必须与该 Pool
kubelet 实际统一配置的 `PodPidsLimit` 完全一致；应用配置本身不会修改 kubelet。
`NetworkPolicyRequired=true` 是部署契约，运维仍必须确认 CNI 实际执行 NetworkPolicy。
`Runtime__Kubernetes__ProtectedCidrs` 至少包含一项；平台会额外内建拒绝 RFC1918、
link-local、loopback、共享地址和其他特殊用途 IPv4 空间。Pool 必须补充所有不属于这些
内建范围的集群/管理网段，应用不会自动发现 CNI、Service 或节点 CIDR。
Runtime Namespace 只放置 `rt-*` Runtime 资源；如确需平台资源，名称必须使用
`platform-*` 保留前缀。

## 网络

外部 TLS 在可信代理终止或进程端到端 TLS。ForwardedHeaders 只信任明确代理。API CORS 精确 Origin+credentials；Refresh Cookie Secure/SameSite Strict。

当前威胁模型信任办赛管理员、管理员维护的题目配置、Runner Pool 配置及平台托管镜像，
不防御管理员内鬼。选手、题目业务容器及其网络输入仍是不可信边界。

Runtime 网段与平台数据网隔离；默认拒绝横向访问和云元数据。Runner 仅允许必要 PostgreSQL/Redis/Provider/内部 API 流量；Patch archive 通过绑定单 Submission 的内部 API 读取，不开放对象存储通用网络/凭据。

Docker 公开 Runtime 不把题目容器直接接入 `noctf-network`。每个 Runtime 的题目容器只在
独立 `internal` network；平台托管的 HAProxy ingress 容器同时连接该 network 与
`noctf-network`，并只转发配置中声明的 TCP URL Binding。代理开销计入 Runner capacity，
receipt 保存其资源 ID，Stop/Reset/失败回滚会一并删除。生产节点必须预先准备受信任的
`Runtime__Docker__IngressProxyImage`，或确保 Docker Compose/daemon 可从受信任 registry
拉取该镜像。

## 请求大小

NoCTF 应用层不设置 Payload Too Large，不主动返回 413：Kestrel `MaxRequestBodySize=null`，multipart `MultipartBodyLengthLimit=long.MaxValue`，Endpoint 不附加 RequestSizeLimit。部署代理若设置外部上限属于运维策略，必须与产品方明确，不能被应用文档误称为业务规则。Flag 单项/解包安全限制仍在业务/Runner 层执行；无效 JSON/Flag 是 400，无法处理的 archive 是 422，均不是 413。

## 配置/Secret

JWT signing key 可供 Access/Refresh/Internal 使用，但 audience/Scheme 隔离。PostgreSQL、Redis、S3 credentials、SMTP、FlagDerivationSecret 不写日志。FlagDerivationSecret 是每 Competition 数据，不是部署 Secret。

Runner Pool/Provider/resource max、Redis、Wolverine PostgreSQL transport、S3、CORS/Origin、Cookie Secure、SMTP 是强类型 IOptions 并在进程启动时 ValidateOnStart。

## 健康与关闭

Liveness 只表示进程事件循环；Readiness 检查进程所需 PostgreSQL/Wolverine，API 另检查 Redis 的降级状态，Runner 检查 Provider/queue。Runtime 题目本身不使用平台 Health Probe。

优雅关闭先停止接收/claim 新消息，等待当前短事务/Provider 操作到部署超时；未完成消息依 Wolverine lease 恢复。不得依赖内存 drain 状态。

## 运维边界

平台不实现数据库/对象备份、PITR 或恢复编排；由外部运维负责。Redis 可丢失并重建。QQBot 不部署。
