# 部署边界

## 必需角色与组件

```text
Api role        >=1，可水平扩展
Worker role     >=1，可水平扩展
Runner role     >=1，可按 Pool/Provider 扩展
PostgreSQL
Redis
S3-compatible object storage（LocalFileSystem 仅非 HA）
Frontend/reverse proxy
```

三个角色可以分别运行在兼容入口中，也可以由 `NoCTF.Host` 以任意非空组合承载。
`NoCTF.Host` 缺省启用全部角色；使用 `Hosting__Roles__0=Api`、
`Hosting__Roles__1=Worker`、`Hosting__Roles__2=Runner` 明确配置。角色集合在进程启动后
不可热切换。

仓库提供同等受支持的本地样例：`deploy/docker-compose.yml` 为独立进程，
`deploy/docker-compose.single.yml` 为全合一。生产环境可按负载运行单 API、N Worker、N Runner，
或组合 API+Worker/Worker+Runner 等；N 表示多个进程或容器，不是在一个进程中重复注册角色。

## CI 容器镜像

推送到 `main` 时，GitHub Actions 先运行唯一的 `test` Job。测试通过后，
`publish-images` Job 使用仓库根目录作为构建上下文和 `backend/Dockerfile` 的 `host`
target 发布一个镜像：`ghcr.io/<owner>/<repo>`。同一镜像通过 `Hosting__Roles__*`
选择 Api、Worker、Runner 的任意非空组合；迁移任务也复用该镜像并以
`--migrate-only` 启动。

镜像同时发布 `latest`、程序集预发布版本（例如 `0.1.0-alpha.97`）和
`sha-<完整提交哈希>` 标签。Host target 会先执行 Nuxt 静态生成，再把
`.output/public` 放入发布目录的 `wwwroot`。
部署清单应固定版本或提交标签，不能以 `latest` 作为供应链身份。

GHCR 使用工作流内置的 `GITHUB_TOKEN`。若仓库配置了以下 Actions Variables 与 Secrets，
同一构建还会推送到自定义 Registry；未配置 `CUSTOM_REGISTRY` 时相关校验、登录和推送均跳过：

| 类型 | 名称 | 用途 |
| --- | --- | --- |
| Variable | `CUSTOM_REGISTRY` | Registry 主机名和可选端口，不含协议或路径 |
| Variable | `CUSTOM_REGISTRY_NAMESPACE` | 可选命名空间；未配置时使用 GitHub 仓库所有者 |
| Secret | `CUSTOM_REGISTRY_USERNAME` | 自定义 Registry 用户名 |
| Secret | `CUSTOM_REGISTRY_PASSWORD` | 自定义 Registry 密码或访问令牌 |

自定义 Registry 镜像名为 `<registry>/<namespace>/<repo>`，标签与 GHCR 完全一致。

仓库中的第三方构建基础镜像、PostgreSQL、Redis、MinIO 与 MinIO Client 均同时固定可读版本标签和
多架构 manifest digest；Kompose 固定版本下载后必须以官方发布的 SHA-256 校验通过才能执行。依赖更新
必须在一次审阅中同时替换版本与 digest，并通过部署架构门禁，禁止 `latest` 或只有可变 tag 的外部镜像。
`noctf-*` 是本地构建示例名；生产发布必须以平台预发布版本和构建产物 digest 标记，再由环境清单引用，
不能把本地 `latest` 当作生产供应链身份。

## 进程权限

- API：业务 DB、Wolverine Outbox、Redis、ObjectStorage、公开/内部 HTTP；无 Docker/Kubernetes/Libvirt 权限。
- Worker：业务 DB、Wolverine queues、Redis、ObjectStorage；无宿主 Runtime socket。
- Runner：所需业务表和 Wolverine runner queues、Redis heartbeat、内部 archive 读取 API，以及特定 Provider 权限；数据库 role 不授予 User/认证配置写权限，也不需要对象存储通用凭据。
- Host：权限是其全部启用角色权限的并集。包含 Runner 的组合进程必须获得 Provider 权限，
  因此全合一适合本地或受控小型部署；需要最小权限隔离时应拆分角色。

Runner 管理端口只在内部网络。Checker callback API 可达，但严格 JWT audience/permission。

官方清单不部署额外 callback 服务。Docker 拆分进程中的 `backend` 和单进程中的 `noctf` 容器使用
`scoring-callback-gateway` 角色标签作为稳定 callback 身份；Runner 把所有当前运行且精确匹配该标签
的 API role 容器临时接入每次 Checker 独立的 internal network。Kubernetes 使用
`backend-service.noctf.svc.<clusterDomain>`，并由 Runtime 侧 egress 与平台侧 ingress 的 Namespace +
Pod 双 selector 将路径限制到可信 AWD/AWDP Checker 和 API Pod 的 callback 端口。修改 Compose 服务名、
Kubernetes Namespace、Service、cluster domain 或 callback 标签时，必须同步修改
`RunnerScoring__CallbackBaseUrl` 与对应 Runtime callback identity 配置。

## Provider 前置条件

- Docker Pool：Docker daemon，禁止把 socket 暴露给题目 Container。平台网络统一命名为
  `noctf-network`；Runner 必须配置 `Runtime__Docker__Network=noctf-network`。
  Compose 部署保持 Runner 为非 root，并将 `DOCKER_SOCKET_GID` 设置为宿主
  `/var/run/docker.sock` 的数字组 ID（Linux 可用
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

每个 Runner 节点必须配置稳定唯一的 `Runner__Id`、`Runner__Capacity__MemoryBytes`、
`Runner__Capacity__NanoCpus`、`Runner__Capacity__PidsLimit`、
`Runner__Heartbeat__IntervalSeconds`、`Runner__Heartbeat__TtlSeconds` 和
`Runner__ProviderFailureHoldSeconds`。所有容量值和周期必须为正，心跳 TTL 必须大于刷新周期。
Provider 创建资源被拒绝、超时或清理失败时，Runner 会在最后一次失败后的 hold 窗口内让
readiness 失败、停止发布可接单 heartbeat，并记录不含题目配置或凭据的结构化 Warning；成功的
Provider 创建会立即恢复，hold 到期后也会重新开放一次探测机会，默认窗口为 120 秒。Runner
只刷新已初始化的可用容量，不会覆盖已扣减值；
Redis 容量状态丢失且 PostgreSQL 仍有该节点活跃 assignment 时，节点保持离线，直到
assignment 收敛后才从部署配置重建容量。

Kubernetes 清单使用 StatefulSet Pod 名作为 RunnerId，使副本扩缩容和重启保持稳定身份。
其他编排环境也必须为每个 Runner 副本提供唯一且可恢复的 RunnerId；不得让多个活动副本共享身份。

`Runtime__Kubernetes__PodPidsLimit` 是 Runner 的容量与兼容校验值，必须与该 Pool
kubelet 实际统一配置的 `PodPidsLimit` 完全一致；应用配置本身不会修改 kubelet。运维核验节点配置后，
必须给允许承载题目工作负载的节点设置精确的 `noctf.io/pod-pids-limit=<数值>` 标签。Runner 启动时以
只读 Node 权限确认至少一个同值、Ready 且可调度的节点；所有单容器与 Compose Runtime Pod 同时强制
使用该标签作为 `nodeSelector`。未核验或数值不同的新节点不会承载 Runtime。
`NetworkPolicyRequired=true` 是部署契约，运维仍必须确认 CNI 实际执行 NetworkPolicy。
`Runtime__Kubernetes__ProtectedCidrs` 至少包含一项；平台会额外内建拒绝 RFC1918、
link-local、loopback、共享地址和其他特殊用途 IPv4 空间。Pool 必须补充所有不属于这些
内建范围的集群/管理网段，应用不会自动发现 CNI、Service 或节点 CIDR。
Runtime Namespace 只放置 `rt-*` Runtime 资源；如确需平台资源，名称必须使用
`platform-*` 保留前缀。

## 网络

ASP.NET Core 只监听 `http://+:8080`，不执行 HTTPS 重定向，也不加载公网证书。Docker
Compose 将该端口仅发布到宿主回环地址 `127.0.0.1:${NOCTF_BACKEND_PORT:-8080}`；公网
HTTP→HTTPS、TLS 终止、HSTS 和 WebSocket upgrade 由宿主 Nginx 完成。仓库样例
`deploy/nginx/noctf.conf` 中的域名和证书路径必须替换后再启用，Nginx 上游保持
`http://127.0.0.1:8080`。不得把 API 8080 改回 `0.0.0.0` 公网发布，也不得让内部
Runner/Checker 绕行公网入口。

Nginx 必须传递 `Host`、`X-Forwarded-Host`、`X-Forwarded-For` 和
`X-Forwarded-Proto`。API 只接受 `ForwardedHeaders:KnownNetworks`/`KnownProxies` 中明确
配置的一跳代理；Docker 默认私网 CIDR 仅是样例，生产必须按实际 bridge CIDR 收窄，
`NOCTF_PUBLIC_HOST` 必须为公网 Host。Kubernetes 的 ConfigMap 同样必须把示例
`10.0.0.0/8` 换成 ingress-controller 的精确 Pod CIDR，禁止使用 `TrustAll`。内部
`RunnerScoring__CallbackBaseUrl` 与归档地址继续使用受限网络中的 HTTP Service 地址，
并由 Runner JWT、audience、resource、Runtime UUID 和执行身份验证。
Refresh Cookie 默认由 API 设置 `Secure=true`、`HttpOnly=true`、`SameSite=Strict`。
仅在明确隔离的纯 HTTP 测试部署中可设置 `AUTHENTICATION_REFRESH_COOKIE_SECURE=false`；
此时 Cookie 会移除 `__Secure-` 前缀。公网生产部署仍应终止 TLS 并保持默认值。
API CORS 继续使用精确 Origin+credentials。

当前威胁模型信任办赛管理员、管理员维护的题目配置、Runner Pool 配置及平台托管镜像，
不防御管理员内鬼。选手、题目业务容器及其网络输入仍是不可信边界。

Runtime 网段与平台数据网隔离；默认拒绝横向访问和云元数据。Runner 仅允许必要 PostgreSQL/Redis/Provider/内部 API 流量；Patch archive 通过绑定单 GameplayFact/PatchUpload 的内部 API 读取，不开放对象存储通用网络/凭据。

Docker 公开 Runtime 不把题目容器接入 `noctf-network`。每个 Runtime 使用独立的普通
user-defined bridge network；题目 Container/Compose service 直接发布配置中声明的 TCP
端口，宿主端口固定请求 `0`，由 Docker 分配随机端口。Runner 从实际端口映射展开公开
URL，Stop/Reset/失败回滚按 Runtime identity 清理容器、Compose project 与独立 network。
当前架构不创建 HAProxy/ingress proxy，也不要求部署 ingress proxy 镜像。

## 请求大小

NoCTF 继续让 Kestrel 的全局 `MaxRequestBodySize=null`、multipart `MultipartBodyLengthLimit=long.MaxValue`，但每个上传 Endpoint 都设置“用途文件上限 + 64 KiB multipart 开销”的请求硬上限，防止在模型绑定前无限写临时盘。应用随后执行精确文件上限，超过上限返回带稳定失败码的 413，并在对象存储写入前终止。反向代理的外部上限不得低于 Endpoint 硬上限，否则会破坏应用错误契约。

## 配置/Secret

JWT signing key 可供 Access/Refresh/Internal 使用，但 audience/Scheme 隔离。PostgreSQL、Redis、S3 credentials、SMTP、FlagDerivationSecret 不写日志。FlagDerivationSecret 是每 Competition 数据，不是部署 Secret。`EmailVerification:EncryptionKey` 必须是独立的 Base64 32-byte 部署 Secret，仅用于加密数据库中的 SMTP 密码；API 永不返回该密码。

Runner Pool/Provider/resource max、Redis、Wolverine PostgreSQL transport、S3、CORS/Origin、Cookie Secure 是强类型 IOptions 并在进程启动时 ValidateOnStart。邮箱验证开关、密码找回有效期/冷却/账号限额、公开 URL 与 SMTP 投递参数由管理员页面写入数据库，API/Worker 动态读取；SMTP 密码只能整体替换，前端不回填也不持久化。密码找回不依赖注册邮箱验证开关，但没有完整 SMTP 投递配置时不会签发重置令牌。

Kubernetes 的平台 Namespace 缺省拒绝全部外联。Runner 通过 Cilium 的 `kube-apiserver` 实体仅访问
真实 Kubernetes API，不得以 `0.0.0.0/0` 放行 443/6443。基础 DNS policy 只允许通过集群 DNS 查询
`*.svc.cluster.local`；外部名字必须由单独的精确 FQDN policy 放行。SMTP 属于部署与管理员配置共同决定的可选
外部依赖：启用邮件投递前，运维必须从 `deploy/k8s/smtp-egress.example.yaml` 生成只匹配当前 SMTP
FQDN 与 TCP 端口的 Cilium policy，并以同一精确 FQDN 限制集群 DNS 观察规则；修改 SMTP 主机或
端口时必须同步更新 policy。策略不得包含凭据、
通配域名或任意地址放行。

## 健康与关闭

Liveness 只表示进程事件循环；Readiness 检查进程所需 PostgreSQL/Wolverine。API 将 Redis
不可用报告为降级；Worker 和 Runner 将 Redis 视为必要依赖，因为排行榜投影、通知、平台日志及
Runner 容量事实均依赖 Redis。Runtime 题目本身不使用平台 Health Probe。

优雅关闭先停止接收/claim 新消息，等待当前短事务/Provider 操作到部署超时；未完成消息依 Wolverine lease 恢复。不得依赖内存 drain 状态。

## 运维边界

平台进程和管理后台不实现数据库/对象备份或恢复 API；由外部运维负责。仓库提供强制停写、age
加密、完整保留 Wolverine PostgreSQL schema、对象元数据和恢复后校验的外部工具及隔离演练，见
[备份恢复](backup-recovery.md)。当前工具生成离散恢复点，不是 PITR。Redis 可丢失并重建。
QQBot 不部署。
