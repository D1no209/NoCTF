# 系统架构

## 角色与进程拓扑

```text
Browser/Client
     |
     v
NoCTF.API  ---- Redis (cache/rate limit/SignalR/heartbeat)
     |
     +---- PostgreSQL (business facts)
     +---- NATS JetStream (durable messages, consumers, retries, DLQ)
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

`NoCTF.Bot` 不是第四个平台角色。它是可选的独立边缘消费者，仅通过公开 HTTPS API、比赛
SignalR 与私网 Milky 工作，不引用 Domain/Application/Infrastructure，不接入 PostgreSQL、Redis、
NATS、Wolverine 或 Runtime Provider。其 QQ 群订阅、快照与发送重试只保存在独立 SQLite。

### BOT 开发期仓库边界

当前开发阶段暂时将 `NoCTF.Bot` 源码、测试及依赖版本放在 NoCTF 单仓内，并加入
`backend/NoCTF.slnx`，用于同步验证公开 API/SignalR 契约、复用统一代码质量检查并降低联调成本。
这只是源码协同与 CI 组织方式，不改变运行时所有权：平台发布物、平台配置、数据库、消息总线、
部署清单和进程角色均不得包含 BOT、Milky、UniQsign、QQ群或其 Secret。

单仓期间仍必须保持以下可分离边界：

- `NoCTF.Bot` 不得引用 API、Domain、Application、Infrastructure、Hosting、Worker 或 Runner 项目；
- BOT 只能通过现有公开 HTTPS API 与 SignalR 契约访问平台；
- 平台只能创建普通 User Bot 身份并签发、校验、轮换和撤销 JWT，不保存 BOT 运行配置；
- BOT、Milky、UniQsign 与 SQLite 必须独立部署和运维，不能进入平台 Compose/Kubernetes 拓扑；
- 为 BOT 开发所需的测试不得要求平台新增专用接口、通知、表、队列或权限。

待公开契约稳定、独立部署与至少 24 小时公测验收完成后，将 `NoCTF.Bot` 的源码、测试和依赖
迁移到独立仓库，建立独立版本、CI、发布与安全扫描。拆分后平台仓库仅保留通用 Bot 身份/JWT
能力、公开协议契约及必要的接入说明，不保留 QQ 协议端或签名服务的实现与配置。

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

- PostgreSQL：唯一业务事实源；不承载 Wolverine message transport。
- NATS JetStream：跨角色 durable transport、consumer group、重投和 DLQ。
- Redis：FusionCache 排行榜 L2、TokenVersion 缓存、分布式限流、SignalR backplane 和 Runner heartbeat/capacity。Redis 丢失不丢业务事实，也不决定排行榜是否刷新。
- Object Storage：Challenge Attachment 与 AWDP Patch archive；支持 S3Compatible 和开发用 LocalFileSystem。
- Runtime Provider：Docker、Kubernetes、Libvirt/QEMU/KVM。Provider 隐藏资源创建、查询、销毁与 receipt 细节。

## SPA 分享元数据

Nuxt 保持 `ssr: false`。API 返回 SPA 文档时从平台配置读取名称、描述和 Logo，在静态
`index.html` 的 `<head>` 中注入 `description`、Open Graph、Twitter Card 与 `WebSite`
JSON-LD。分享爬虫无需执行客户端 JavaScript 即可取得平台描述；配置更新不要求重新构建
前端。注入值必须进行 HTML/JSON 安全转义，图片与页面地址使用代理转发头还原后的绝对 URL，
HTML 响应使用 `no-cache`。API、Hub、健康检查、OpenAPI 与带扩展名的静态资源不进入 SPA fallback。

## 依赖方向

```text
Domain <- Application <- API / Worker / Runner / Host
                       <- Infrastructure

NoCTF.Bot -> public API / SignalR protocol only
```

- Domain 不依赖 EF、HTTP、Redis、Wolverine 或 Provider SDK。
- Application 以功能纵切组织，用例接口与使用者就近。
- Infrastructure 实现业务职责，名称不以 `Ef` 泛滥；只有区分 Provider 时使用 Postgres 等前缀。
- API Endpoint 只处理协议；业务规则在 Application/Domain。
- Runner Provider 接受强类型 Runtime/Job 定义，不传递 provider/kind 字符串。

## 一致性边界

消息通过 NATS JetStream durable publish/consumer 传递；业务写入 PostgreSQL 后由状态、唯一约束和业务幂等键收敛。数据库事务与 NATS 发布之间不提供跨系统原子提交。

分数投影不写回 GameplayFact。影响排行榜的业务提交发布 NATS 失效消息；Worker 按比赛合并 500ms 内的失效并从 PostgreSQL 全量投影到命名 FusionCache。缓存丢失时由 PostgreSQL 重建，不扫描 Dirty 业务列。

Flag、AWDP Break 与 Fix 只有在 GameplayFact 事务提交成功后才增加低基数 Prometheus counter；幂等重放与拒绝请求不重复计数。Flag/Break 首次判定终态另记录 `correct | incorrect | platform_error` 低基数结果和从接收到最终判定的完整处理耗时，重判不重复计数。平台监控将 Flag 与 Break 合并展示，提供五分钟正确率、处理 P95、平台错误率、Flag/Fix 平均速率与提交量；正确率排除平台失败，平台错误率以全部终态处理为分母。

## Runner Pool

平台部署配置一个活动 RuntimeProvider（Docker 或 Kubernetes）与 RunnerPool；Challenge/Competition 不引用 Provider 或 RunnerPool。节点通过 Redis TTL heartbeat 发布容量，Worker 从 Registry 原子选择具体 RunnerId，并将 durable 命令直接投递到该节点的 `runner-node-{runnerId}` PostgreSQL queue。RuntimeInstance 只持久化本次调度实际使用的 RunnerId、RuntimeProvider、ProviderReceiptJson 与展开 URL，不保存 pool 路由状态。

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
