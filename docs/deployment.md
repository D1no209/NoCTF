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

- Docker Pool：Docker daemon，禁止把 socket 暴露给题目 Container。
- Kubernetes Pool：统一 Namespace、NetworkPolicy 能力、固定 Kompose 版本与 kubectl/API 权限。
- Libvirt Pool：QEMU/KVM/Libvirt、Runner 可访问 OvaSourceUrl；需要 URL 的 VM 具备 QEMU Guest Agent。

同 Pool 节点对 file:// OVA 路径必须有一致挂载。Provider/Pool 不可用会阻止新 Runtime 派发，不影响静态 API。

## 网络

外部 TLS 在可信代理终止或进程端到端 TLS。ForwardedHeaders 只信任明确代理。API CORS 精确 Origin+credentials；Refresh Cookie Secure/SameSite Strict。

Runtime 网段与平台数据网隔离；默认拒绝横向访问和云元数据。Runner 仅允许必要 PostgreSQL/Redis/Provider/内部 API 流量；Patch archive 通过绑定单 Submission 的内部 API 读取，不开放对象存储通用网络/凭据。

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
