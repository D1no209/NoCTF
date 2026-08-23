# 系统架构

## 角色与进程拓扑

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

平台包含三个运行角色：

- `NoCTF.API`：FastEndpoints、认证、授权、接入事务、REST、SignalR、内部 Checker callback。
- `NoCTF.Worker`：Submission 普通判定、生命周期、轮次、Flag、排行榜投影、通知与清理。
- `NoCTF.Runner`：Wolverine durable consumer；执行 Docker、Compose/Kompose、Kubernetes、Libvirt/OVA、Checker 和 Patch。

角色可由兼容入口 `NoCTF.API`、`NoCTF.Worker`、`NoCTF.Runner` 分别承载，也可由
`NoCTF.Host` 承载任意非空组合。统一宿主读取 `Hosting:Roles` 枚举数组，缺省启用
`Api`、`Worker`、`Runner`；配置只在启动时解析，切换通过重启或滚动发布完成。

常见拓扑包括：

- 单进程：一个 Host 同时承载全部角色。
- 标准分布式：一个或多个 API、N 个 Worker、按 Pool 部署 N 个 Runner。
- 混合：API+Worker 与独立 Runner，或 API+Runner 与独立 Worker。

Worker、Runner 与 API 均可多副本。每个进程中的每种角色至多一份，每个 Runner 进程
只拥有一个稳定唯一 RunnerId。组合部署仍通过 PostgreSQL/Wolverine durable queue 传递业务
工作，不使用内存 Channel 或 fire-and-forget 代替持久投递。

## 数据依赖

- PostgreSQL：唯一业务事实源；同时承载 Wolverine PostgreSQL persistence。
- Redis：FusionCache 排行榜 L2、TokenVersion 缓存、分布式限流、SignalR backplane 和 Runner heartbeat/capacity。Redis 丢失不丢业务事实，也不决定排行榜是否刷新。
- Object Storage：Challenge Attachment 与 AWDP Patch archive；支持 S3Compatible 和开发用 LocalFileSystem。
- Runtime Provider：Docker、Kubernetes、Libvirt/QEMU/KVM。Provider 隐藏资源创建、查询、销毁与 receipt 细节。

## 依赖方向

```text
Domain <- Application <- API / Worker / Runner / Host
                       <- Infrastructure
```

- Domain 不依赖 EF、HTTP、Redis、Wolverine 或 Provider SDK。
- Application 以功能纵切组织，用例接口与使用者就近。
- Infrastructure 实现业务职责，名称不以 `Ef` 泛滥；只有区分 Provider 时使用 Postgres 等前缀。
- API Endpoint 只处理协议；业务规则在 Application/Domain。
- Runner Provider 接受强类型 Runtime/Job 定义，不传递 provider/kind 字符串。

## 一致性边界

API 的业务写入与 Wolverine Outbox 在同一个 EF Core/PostgreSQL 事务中。消息可至少一次投递，因此 Handler 使用状态、唯一约束或自然幂等规则防重复；不得使用持久化 Revision、ProcessingVersion 或隐藏的版本栅栏替代业务幂等键。

分数投影不写回 GameplayFact。影响排行榜的业务提交通过 transactional outbox 发布失效消息；Worker 按比赛合并 500ms 内的失效并从 PostgreSQL 全量投影到命名 FusionCache。缓存丢失时由 PostgreSQL 重建，不扫描 Dirty 业务列。

## Runner Pool

平台部署配置 RunnerId、一个活动 RuntimeProvider（Docker 或 Kubernetes）与 RunnerPool；Challenge/Competition 不引用 Provider 或 RunnerPool。同 Pool 多节点竞争同一 durable queue。节点通过 Redis TTL heartbeat 发布容量。RuntimeInstance 只持久化本次调度实际使用的 RunnerId、RunnerPool、RuntimeProvider、ProviderReceiptJson 与展开 URL。

每次心跳同时维护 `runner-pool:{pool}:candidates` 有序集合。候选分数采用内存、CPU 与 PID
三者中最高的已用比例，并加入不超过 `1e-6` 的随机扰动避免同分节点长期固定成为首选。
调度热路径只读取压力最低的前 8 个候选，Lua 脚本原子校验成员资格、心跳、容量和
`runner-claim:{runtimeInstanceId}` 幂等所有权，再扣减容量并更新分数。前 8 个候选均不可用时，
只执行一次池索引重建与原子兜底选择，避免池中后续可用节点被误判为容量不足。释放 Claim
会在同一脚本内恢复容量并更新候选分数；过期心跳在分配时从候选集合剔除。Runner/Runtime ID
仅进入 Trace 与结构化日志，不作为 Prometheus 标签。

`file://` OVA URL 必须在部署所选 Runner 节点可访问。平台 API 不下载或管理 OVA；配置固定
预期 SHA-256，Libvirt Provider 负责读取/下载、校验、内容寻址缓存以及多 VM Appliance
导入。

## 内部 JWT

Access、Refresh 与内部 JWT 可以共用签名密钥，但必须使用互不接受的 audience、token_type/permission 与验证 Scheme。生产部署可以改用不同签名密钥；验证器不得因密钥相同而跨 Scheme 接受 Token。

调度 Checker 的可信进程签发最小权限 internal JWT，并只把它注入对应 Checker，不把签名密钥或其他 Token 交给题目容器。每次 AWD Checker 执行对应一条独立 GameplayFact；Runtime UUID 和 Checker execution identity 来自 Token。运行退出只区分 Checker 自身正常、异常或超时，不用于判断服务 Up/Down。

Runner 本身直接消费 Wolverine、访问必要业务表，不给自己签 callback JWT。Runner 启动的不可信 Checker 只能通过 `/api/internal/v1` 最小权限接口写结果。
