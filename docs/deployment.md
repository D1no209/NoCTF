# 部署架构与操作契约

统一入口见 [deploy](../deploy/README.md)。Docker 是单 Host 组合三角色；K8s 用同一镜像运行
API/Worker Deployments 和一个 Runner StatefulSet。API/Worker 可扩，每个执行资源域只有一个 Runner。

## 自动迁移与持久化

Docker/K8s 都设置 `Database__AutoMigrate=true`。所有 Host 角色启动前完成 EF 迁移、成员基线与
幂等管理员初始化，成功后才开始 HTTP/消费者。EF/Npgsql 自带迁移互斥，管理员竞争回读同一唯一键
获胜者，不重设已有密码。其他初始化错误阻止启动；短暂不可用可在启动预算内重试。
K8s 不要求独立数据库迁移 Job；`--migrate-only` 是维护/演练命令。S3 首次桶用
`--initialize-storage-only`，不会启动业务角色。

更新保留 Secret/PVC/bind 目录，但重启会自动应用待执行迁移，必须备份并审查 schema。
非兼容升级应停写旧角色；不承诺旧模型可直接应用当前 baseline，不手改迁移/snapshot 或清空生产库。
JetStream 保存持久消息和消费者，NATS KV 保存注册/心跳/fencing，EF 是容量与准入事实源。
没有 Wolverine Database Message Store/Inbox；Webhook PostgreSQL outbox 是明确例外。
Redis 只保存 FusionCache/backplane，丢失后重建，不承担业务租约、配额或容量持久化。
Docker 本地文件默认、RustFS 可选；K8s 默认 RustFS。存储迁移保留不可变 FileId/ObjectKey。
JWT/Runner/邮箱加密/数据库/S3 密钥不能在普通更新时轮换。

## Runtime 和安全边界

部署选择一个活动 Container Provider 和 pool，题目不选择 Provider。
1–64 个命名服务；服务用 CPU cores/MiB，准入用 millicores，PID 是平台配置。
Docker 单服务复用题目 bridge，多服务独占网络，公开端口请求宿主 0，由 Docker 动态分配。
K8s 每服务一个 Pod，多服务才 headless discovery，共享 runtime namespace，不按实例创建 namespace。
重置产生新 UUID，没有 Generation/替换链或 Compose Runtime 契约。
RunnerId 稳定，资源域不能重复申报；kind 节点共享物理 VM，不能把节点数当作机器数。
容量核算 CPU、内存、Pod 槽位和端口并保留 Checker/重置余量，不足或超时有界失败、清理。

K8s 先核验 kubelet PID=256，再认证题目节点标签；服务/Checker 强制匹配。
Cilium 默认隔离，实例内通信、DNS、公开入口和授权回调按方向放行。
私有镜像由平台级 ImagePullSecrets 或标准 Docker auth 提供。API 无 Provider 控制权限。

## 入口与运行手册

Docker 运维外部反代、K8s Envoy Gateway。显式可信来源和 AllowedHosts、对称转发头，公网 HTTPS，
Refresh Cookie Secure/HttpOnly。验收 SignalR、WSRX、流式上传、文件入口；内部回调/归档不绕公网。
监控 `/metrics` 仅私有 9464，公开 8080 为 404。日志仍脱敏并加密 UserId，Loki/Grafana/Prometheus
及 NATS/RustFS 管理入口私有。SMTP/SSO/Webhook 外部目标需要精确 egress。

[Docker](../deploy/docker/README.md)、[K8s](../deploy/k8s/README.md)、
[切换与回退](kubernetes-operations.md)、[一致恢复](backup-recovery.md)。
历史本地验收不等于生产容量/恢复证据。
