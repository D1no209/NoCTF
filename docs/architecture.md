# 系统架构

## 进程拓扑

```text
Browser/Client
     |
     v
NoCTF.API  ---- Redis (cache/rate limit/SignalR/heartbeat)
     |
     +---- PostgreSQL (business + Wolverine inbox/outbox/queues)
                         |
              +----------+----------+
              v                     v
        NoCTF.Worker (N)      NoCTF.Runner (N/pools)
                                      |
                         Docker / Kubernetes / Libvirt
```

只支持三个独立进程：

- `NoCTF.API`：FastEndpoints、认证、授权、接入事务、REST、SignalR、内部 Checker callback。
- `NoCTF.Worker`：Submission 普通判定、生命周期、轮次、Flag、排行榜投影、通知与清理。
- `NoCTF.Runner`：Wolverine durable consumer；执行 Docker、Compose/Kompose、Kubernetes、Libvirt/OVA、Checker 和 Patch。

Worker 与 Runner 可多副本。不存在 API 内 HostedService 业务消费者或单进程组合模式。

## 数据依赖

- PostgreSQL：唯一业务事实源；同时承载 Wolverine PostgreSQL persistence。
- Redis：排行榜快照、TokenVersion 缓存、分布式限流、SignalR backplane、活跃排行榜订阅和 Runner heartbeat/capacity。Redis 丢失不丢业务事实。
- Object Storage：Challenge Attachment 与 AWDP Patch archive；支持 S3Compatible 和开发用 LocalFileSystem。
- Runtime Provider：Docker、Kubernetes、Libvirt/QEMU/KVM。Provider 隐藏资源创建、查询、销毁与 receipt 细节。

## 依赖方向

```text
Domain <- Application <- API / Worker / Runner
                       <- Infrastructure
```

- Domain 不依赖 EF、HTTP、Redis、Wolverine 或 Provider SDK。
- Application 以功能纵切组织，用例接口与使用者就近。
- Infrastructure 实现业务职责，名称不以 `Ef` 泛滥；只有区分 Provider 时使用 Postgres 等前缀。
- API Endpoint 只处理协议；业务规则在 Application/Domain。
- Runner Provider 接受强类型 Runtime/Job 定义，不传递 provider/kind 字符串。

## 一致性边界

API 的业务写入与 Wolverine Outbox 在同一个 EF Core/PostgreSQL 事务中。消息可至少一次投递，因此 Handler 必须以实体 ProcessingVersion、唯一约束或自然幂等规则防重复。

分数投影不写回 Submission/ScoringEvent。影响排行榜的事务只递增 Competition.LeaderboardRevision 并发出 invalidation。完整投影仅在有人查看或首次读取时执行。

## Runner Pool

Runner 配置 RunnerId、RunnerPool 与支持 Provider。Runtime 配置引用 Provider 与开放文本 RunnerPool；同 Pool 多节点竞争同一 durable queue。节点通过 Redis TTL heartbeat 发布版本与容量。RuntimeInstance 持久化实际 RunnerId、RunnerPool、ProviderReceiptJson 与展开 URL。

`file://` OVA URL 必须在 Pool 所有候选节点可访问。平台 API 不下载、校验或管理 OVA；Libvirt Provider 自行处理导入与多 VM Appliance。

## 内部 JWT

Access、Refresh 与内部 JWT 可以共用签名密钥，但必须使用互不接受的 audience、token_type/permission 与验证 Scheme。生产部署可以改用不同签名密钥；验证器不得因密钥相同而跨 Scheme 接受 Token。

调度 Checker 的可信进程在持久化 Job 时通过 `IInternalTokenIssuer` 签发一次性操作 Token，并把 Token 放入发给 Runner 的 durable message；Runner 只把它注入该 Job 的 Checker，不把签名密钥或其他 Token 交给题目容器。Token 绑定具体 Submission/RuntimeInstance、ProcessingVersion/Generation/CheckerSequence 和单一 permission。它的 `iat` 是签发时间，`exp` 固定为 OperationDeadline 后 24 小时，使迟到 callback 仍能到达版本栅栏；旧 Sequence/Version 被接收为 superseded，但不能覆盖新结果。

Runner 本身直接消费 Wolverine、访问必要业务表，不给自己签 callback JWT。Runner 启动的不可信 Checker 只能通过 `/api/internal/v1` 最小权限接口写结果。
