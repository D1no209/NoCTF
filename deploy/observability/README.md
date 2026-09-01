# NoCTF 可观测性

该编排从 API、Worker、Runner 的容器内专用 `9464` 监听器采集 OpenTelemetry 指标，并补充 NATS JetStream、PostgreSQL、Redis 与主机指标。公开业务监听器不会响应 `/metrics`，专用监听器也不会响应指标之外的业务路由。平台日志仍使用现有 Redis 日志能力；如设置 `OTEL_EXPORTER_OTLP_ENDPOINT`，Trace 会同时发送到外部 OTLP Collector。

启动：

```bash
docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.observability.yml up -d
```

手工启动时必须设置 `GRAFANA_ADMIN_PASSWORD`。CI 首次部署会在服务器本地生成独立凭据文件，既不写入仓库，也不会出现在构建产物中。Prometheus 和 Grafana 默认只绑定 `127.0.0.1`；平台管理的“监控”页仅展示后端筛选后的固定摘要，不代理 PromQL 或原始指标。完整 Grafana 仅通过 SSH 隧道或单独配置的受保护入口访问，不要直接暴露到公网。

生产部署仍由 CI 调用 `deploy/docker-compose.yml` 和
`deploy/docker-compose.observability.yml` 完成；本节命令仅用于本地编排验证或运维排障，
不是另一套手工发布流程。

为控制有限服务器磁盘，Prometheus 默认保留 7 天且最多占用 2GB，可通过 `PROMETHEUS_RETENTION_TIME` 与 `PROMETHEUS_RETENTION_SIZE` 调整。告警规则位于 `prometheus/alerts.yml`，默认覆盖关键队列、Runtime 等待、排行榜投影、API、PostgreSQL、Redis、Runner 容量和磁盘空间。

NATS 官方 Prometheus Exporter 仅在 Compose 内部暴露 `7777`，读取 `http://nats:8222` 的 `varz` 与完整 `jsz`。Exporter 和 NATS 的监控接口都不映射到公网；NATS `8222` 在基础编排中仅绑定宿主机回环地址用于本机排障。Prometheus 的 `nats` Target 必须保持 `UP`。

Prometheus 标签仅使用 endpoint、outcome、mode、stream_name、consumer_name、pool 等有限集合。比赛、队伍和 Runtime 等具体标识只进入结构化日志与 Trace，不得加入指标标签。

## 指标来源

| 范围 | 指标来源 |
| --- | --- |
| API | NoCTF HTTP 中间件，按稳定路由模板记录吞吐、结果与延迟 |
| Flag / Fix / Runtime | 仅统计真实变更接口；读取接口和题库 Flag 管理不混入玩法吞吐 |
| SignalR | NoCTF Hub 连接与发布埋点 |
| Wolverine | Wolverine 原生 OpenTelemetry 计数器和 Handler 延迟直方图 |
| JetStream | NATS 官方 Exporter 的 `varz` 和完整 `jsz`；关键消费者只统计 `NOCTF_CONTROL` 与 `NOCTF_GAMEPLAY` 的 leader 样本 |
| 事务消息 | Wolverine 原生 `wolverine_outbox_count_Messages` 与 `wolverine_inbox_count_Messages`，覆盖消息进入 NATS 前的 PostgreSQL Inbox/Outbox 积压 |
| PostgreSQL | postgres-exporter 与 Npgsql OpenTelemetry 指标 |
| Redis | redis-exporter，以及 NoCTF 对缓存、发布和 Runner Lua 操作的客户端埋点 |
| Runner | 心跳时按资源池汇总在线数量及 CPU、内存、PID 可用/总容量 |
| 排行榜 | 合并分发失败、缓存缺失重建失败、投影耗时、缓存发布失败和 SignalR 发布失败 |

Runner 容量不使用不存在的预计算 ratio 指标。Dashboard 和告警均由
`noctf_runner_capacity_available / noctf_runner_capacity_total` 即时计算每种资源的剩余比例；
`up{role="runner"}` 独立监测 Runner 进程不可达，避免目标消失时容量告警也随时间序列一起消失。

Wolverine 吞吐、成功、Inbox/Outbox 与执行耗时直接使用 Wolverine 原生 Meter，
不维护第二套需要手工调用、容易长期为零的重复计数器。应用不再导出
`noctf_worker_queue_*` 兼容指标；消息传输积压以 JetStream consumer 指标为事实源。

## 管理摘要阈值

`/admin/platform/monitoring` 显示当前真实值，但队列与事务消息的健康状态使用持续窗口内的最低值判断，避免一条正常处理中的消息立即触发告警。默认持续窗口为 3 分钟，可通过下列环境变量调整：

| 环境变量 | 默认值 | 说明 |
| --- | ---: | --- |
| `NOCTF_MONITORING_SUSTAINED_WINDOW_MINUTES` | 3 | 持续积压判断窗口 |
| `NOCTF_MONITORING_PENDING_WARNING` / `NOCTF_MONITORING_PENDING_CRITICAL` | 25 / 100 | JetStream 待投递阈值 |
| `NOCTF_MONITORING_ACK_PENDING_WARNING` / `NOCTF_MONITORING_ACK_PENDING_CRITICAL` | 10 / 50 | JetStream 已投递未确认阈值 |
| `NOCTF_MONITORING_REDELIVERY_WARNING` / `NOCTF_MONITORING_REDELIVERY_CRITICAL` | 1 / 10 | JetStream 重投递阈值 |
| `NOCTF_MONITORING_OUTBOX_WARNING` / `NOCTF_MONITORING_OUTBOX_CRITICAL` | 25 / 100 | Wolverine Outbox 阈值 |
| `NOCTF_MONITORING_INBOX_WARNING` / `NOCTF_MONITORING_INBOX_CRITICAL` | 25 / 100 | Wolverine Inbox 阈值 |
| `NOCTF_MONITORING_JS_STORAGE_WARNING_PERCENT` / `NOCTF_MONITORING_JS_STORAGE_CRITICAL_PERCENT` | 75 / 90 | JetStream 存储占用百分比阈值 |

Warning 阈值必须低于 Critical；配置无效时 API 启动会直接失败。Prometheus 告警规则使用同一组默认阈值，并通过 `for` 保证持续时间语义；若运维修改管理摘要阈值，应同步调整 `prometheus/alerts.yml`。

## Worker 队列隔离

Worker 使用四条独立的 NATS JetStream durable consumer；业务事务仍通过 Wolverine PostgreSQL Inbox/Outbox 保证一致性：

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

## 验收

部署后至少检查：

1. `docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.observability.yml config` 无错误。
2. `docker compose ... exec prometheus wget -qO- http://nats-exporter:7777/metrics` 包含 `jetstream_consumer_num_pending`、`jetstream_consumer_num_ack_pending`、`jetstream_consumer_num_redelivered`、`jetstream_stream_total_messages`、`jetstream_server_total_message_bytes` 与 `jetstream_server_max_storage`。
3. Prometheus Targets 中 `nats` 为 `UP`，正常空闲时关键 pending 为真实的 0。
4. 使用可恢复的测试消息分别验证 pending、ack pending、redelivery、Outbox/Inbox 非零变化；完成后恢复消费者并确认归零。
5. Grafana 所有面板无错误 PromQL；管理页正确区分“暂无样本”“数据不可用”和 NATS Critical。
