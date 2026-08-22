# NoCTF 可观测性

该编排在现有 API、Worker、Runner 的 `/metrics` 端点上采集 OpenTelemetry 指标，并补充 PostgreSQL、Redis 与主机指标。平台日志仍使用现有 Redis 日志能力；如设置 `OTEL_EXPORTER_OTLP_ENDPOINT`，Trace 会同时发送到外部 OTLP Collector。

启动：

```bash
docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.observability.yml up -d
```

必须设置 `GRAFANA_ADMIN_PASSWORD`。Prometheus 和 Grafana 默认只绑定 `127.0.0.1`，建议通过 SSH 隧道访问，不要直接暴露到公网。

为控制有限服务器磁盘，Prometheus 默认保留 7 天且最多占用 2GB，可通过 `PROMETHEUS_RETENTION_TIME` 与 `PROMETHEUS_RETENTION_SIZE` 调整。告警规则位于 `prometheus/alerts.yml`，默认覆盖关键队列、Runtime 等待、排行榜投影、API、PostgreSQL、Redis、Runner 容量和磁盘空间。

Prometheus 标签仅使用 endpoint、outcome、mode、queue、pool 等有限集合。比赛、队伍和 Runtime 等具体标识只进入结构化日志与 Trace，不得加入指标标签。

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
