# 系统架构

## 角色与进程拓扑

```text
Browser/Client
     |
     v
NoCTF.Host [Api]  ---- Redis (cache/rate limit/SignalR/heartbeat)
     |
     +---- PostgreSQL (business facts)
     +---- NATS JetStream (durable messages, consumers, retries, DLQ)
                         |
              +----------+----------+
              v                     v
        NoCTF.Host [Worker]   NoCTF.Host [Runner] (N/pools)
                                      |
                         Docker / Kubernetes / Libvirt
```

唯一可执行文件 `NoCTF.Host.dll` 包含三个运行角色；下列名称表示功能类库，不是独立进程入口：

- `NoCTF.API`：FastEndpoints、认证、授权、接入事务、REST、SignalR、内部 Checker callback。
- `NoCTF.Worker`：Submission 普通判定、生命周期、轮次、Flag、排行榜投影、通知与清理。
- `NoCTF.Runner`：Wolverine durable consumer；执行 Docker、Compose/Kompose、Kubernetes、Libvirt/OVA、Checker 和 Patch。

外部通知统一使用赛事级 Webhook。平台不包含聊天协议、群组、消息模板或外部 Provider；Worker
只向赛事负责人配置的目标发送带 HMAC 签名的公开 CloudEvents。浏览器继续通过 SignalR 接收
失效提示并重读 REST。通用非交互自动化账户只负责 API 身份，不承担通知投递模型。

`NoCTF.Host` 是唯一进程入口，可承载任意非空角色组合。统一宿主读取 `Hosting:Roles` 枚举数组，缺省启用
`Api`、`Worker`、`Runner`；配置只在启动时解析。一次发布中的所有角色必须运行同一 Host 镜像，
不支持新旧版本混跑或滚动兼容窗口。

常见拓扑包括：

- 单进程：一个 Host 同时承载全部角色。
- 标准分布式：一个或多个 API、N 个 Worker、按 Pool 部署 N 个 Runner。
- 混合：API+Worker 与独立 Runner，或 API+Runner 与独立 Worker。

Worker、Runner 与 API 均可多副本。每个进程中的每种角色至多一份，每个 Runner 进程
只拥有一个稳定唯一 RunnerId。组合部署仍通过 NATS JetStream durable consumer 传递业务
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

NoCTF.Worker -> signed competition Webhooks
```

- Domain 不依赖 EF、HTTP、Redis、Wolverine 或 Provider SDK。
- Application 以功能纵切组织，用例接口与使用者就近。
- Infrastructure 实现业务职责，名称不以 `Ef` 泛滥；只有区分 Provider 时使用 Postgres 等前缀。
- API Endpoint 只处理协议；业务规则在 Application/Domain。
- Runner Provider 接受强类型 Runtime/Job 定义，不传递 provider/kind 字符串。

## 一致性边界

消息通过 NATS JetStream durable publish/consumer 传递；业务写入 PostgreSQL 后由状态、唯一约束和业务幂等键收敛。数据库事务与 NATS 发布之间不提供跨系统原子提交。

分数投影不写回 GameplayFact。影响排行榜的业务提交发布 NATS 失效消息；Worker 按比赛合并 500ms 内的失效并从 PostgreSQL 全量投影到命名 FusionCache。缓存丢失时由 PostgreSQL 重建，不扫描 Dirty 业务列。

Flag、AWDP Break 与 Fix 只有在 GameplayFact 事务提交成功后才增加低基数 Prometheus counter；幂等重放与拒绝请求不重复计数。Flag/Break 首次判定终态另记录 `correct | incorrect | platform_error` 低基数结果和从接收到最终判定的完整处理耗时，重判不重复计数。外部 Prometheus recording rules 将 Flag 与 Break 合并，计算五分钟正确率、处理 P95、平台错误率、Flag/Fix 平均速率与提交量；正确率排除平台失败，平台错误率以全部终态处理为分母。平台不查询或呈现这些监控数据。

## Runner Pool

平台部署配置一个活动 RuntimeProvider（Docker 或 Kubernetes）与 RunnerPool；Challenge/Competition 不引用 Provider 或 RunnerPool。节点只使用 Runner registration schema 3 发布 TTL heartbeat；Worker 通过关系化 capacity ledger/allocation 与乐观并发选择具体 RunnerId，并将 durable 命令投递到该节点的 JetStream subject。RuntimeInstance 只持久化本次调度实际使用的 RunnerId、RuntimeProvider、结构化 AccessEndpoint 以及一对一 typed receipt，不保存 pool 路由状态或 JSON receipt。

Runner resource-domain 所有权使用 NATS KV CAS 租约：租约 30 秒、每 10 秒续租，KV revision
作为 fencing token。连续两次续租失败时 Runner 进入 draining 并终止进程；接管者从关系数据库
中的 ledger、allocation 与 Runtime 事实重建容量。分配以唯一约束和 concurrency stamp 保证幂等，
不读取旧 Redis Claim、不使用 `__legacy__` pool。Runner/Runtime ID 仅进入 Trace 与结构化日志，
不作为 Prometheus 标签。

`file://` OVA URL 必须在部署所选 Runner 节点可访问。平台 API 不下载或管理 OVA；配置固定
预期 SHA-256，Libvirt Provider 负责读取/下载、校验、内容寻址缓存以及多 VM Appliance
导入。

## 内部 JWT

Access、Refresh 与内部 JWT 可以共用签名密钥，但必须使用互不接受的 audience、token_type/permission 与验证 Scheme。生产部署可以改用不同签名密钥；验证器不得因密钥相同而跨 Scheme 接受 Token。

调度 Checker 的可信进程签发最小权限 internal JWT，并只把它注入对应 Checker，不把签名密钥或其他 Token 交给题目容器。每次 AWD Checker 执行对应一条独立 GameplayFact；Runtime UUID 和 Checker execution identity 来自 Token。运行退出只区分 Checker 自身正常、异常或超时，不用于判断服务 Up/Down。

Runner 本身直接消费 Wolverine、访问必要业务表，不给自己签 callback JWT。Runner 启动的不可信 Checker 只能通过 `/api/internal/v1` 最小权限接口写结果。
