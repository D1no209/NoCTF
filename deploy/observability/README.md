# NoCTF 可观测性

该编排在现有 API、Worker、Runner 的 `/metrics` 端点上采集 OpenTelemetry 指标，并补充 PostgreSQL、Redis 与主机指标。平台日志仍使用现有 Redis 日志能力；如设置 `OTEL_EXPORTER_OTLP_ENDPOINT`，Trace 会同时发送到外部 OTLP Collector。

启动：

```bash
docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.observability.yml up -d
```

必须设置 `GRAFANA_ADMIN_PASSWORD`。Prometheus 和 Grafana 默认只绑定 `127.0.0.1`，建议通过 SSH 隧道访问，不要直接暴露到公网。

生产部署仍由 CI 调用 `deploy/docker-compose.yml` 和
`deploy/docker-compose.observability.yml` 完成；本节命令仅用于本地编排验证或运维排障，
不是另一套手工发布流程。

为控制有限服务器磁盘，Prometheus 默认保留 7 天且最多占用 2GB，可通过 `PROMETHEUS_RETENTION_TIME` 与 `PROMETHEUS_RETENTION_SIZE` 调整。告警规则位于 `prometheus/alerts.yml`，默认覆盖关键队列、Runtime 等待、排行榜投影、API、PostgreSQL、Redis、Runner 容量和磁盘空间。

Prometheus 标签仅使用 endpoint、outcome、mode、queue、pool 等有限集合。比赛、队伍和 Runtime 等具体标识只进入结构化日志与 Trace，不得加入指标标签。

## 指标来源

| 范围 | 指标来源 |
| --- | --- |
| API | NoCTF HTTP 中间件，按稳定路由模板记录吞吐、结果与延迟 |
| Flag / Fix / Runtime | 仅统计真实变更接口；读取接口和题库 Flag 管理不混入玩法吞吐 |
| SignalR | NoCTF Hub 连接与发布埋点 |
| Wolverine | Wolverine 原生 OpenTelemetry 计数器和 Handler 延迟直方图 |
| 队列积压 | Worker 对四条 PostgreSQL 持久队列执行的轻量聚合查询 |
| PostgreSQL | postgres-exporter 与 Npgsql OpenTelemetry 指标 |
| Redis | redis-exporter，以及 NoCTF 对缓存、发布和 Runner Lua 操作的客户端埋点 |
| Runner | 心跳时按资源池汇总在线数量及 CPU、内存、PID 可用/总容量 |
| 排行榜 | 脏比赛数量、最老脏标记、投影规模/耗时和发布失败数 |

Runner 容量不使用不存在的预计算 ratio 指标。Dashboard 和告警均由
`noctf_runner_capacity_available / noctf_runner_capacity_total` 即时计算每种资源的剩余比例；
`up{role="runner"}` 独立监测 Runner 进程不可达，避免目标消失时容量告警也随时间序列一起消失。

Wolverine 吞吐、成功、失败、死信与执行耗时直接使用 Wolverine 原生 Meter，
不维护第二套需要手工调用、容易长期为零的重复计数器。具体队列的当前积压和最老消息年龄
则使用 `noctf_worker_queue_*` 指标补充。

## Worker 队列隔离

Worker 使用四条独立的 Wolverine PostgreSQL 持久队列：

| 队列 | 负载 | 默认并发 |
| --- | --- | ---: |
| `noctf-control` | 生命周期、轮次、超时恢复、Runtime 调度与资源对账 | 2 |
| `noctf-gameplay` | Flag、Fix、GameplayFact 收敛与作弊事实 | 8 |
| `noctf-projection` | 排行榜与比赛投影 | 2 |
| `noctf-background` | 邮件、通知、导出、文件清理 | 2 |

默认单实例监听全部队列。扩容时可对同一 Worker 镜像设置 `Worker__Queues__0`、
`Worker__Queues__1` 等，只监听指定的 `control`、`gameplay`、`projection` 或
`background` 队列；对应并发由 `Worker__Concurrency__{Queue}` 控制。至少保留一个
实例监听每条队列。所有队列继续使用 Durable Inbox/Outbox，Wolverine 指标按有限的
消息类型和目标队列维度导出，不添加比赛、队伍或 Runtime ID 标签。
