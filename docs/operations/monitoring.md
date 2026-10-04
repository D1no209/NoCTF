# 监控与容量

监控是独立运维栈。NoCTF 输出私有指标、日志与健康信号，不嵌入 Grafana，也不把监控作为权威业务存储。

## 首次安装监控

Docker 配置向导选择启用监控，会生成 Host 的 `compose.monitoring.yml` 和安装目录下独立 `observability` 栈、secret 文件与共享资产路径。填写 Grafana HTTPS 域名和独立管理员密码，保持 `noctf-proxy` 上的 alias `noctf-grafana`。

部署助手先准备只读 PostgreSQL exporter 账号和监控，再启动启用观测的 Host。手工安装详细说明见 [observability README](https://github.com/D1no209/NoCTF/blob/HEAD/deploy/docker/observability/README.md)，关键步骤：

1. 复制独立栈模板到仓库外的安装目录。
2. 创建受限 Grafana/只读 PostgreSQL exporter 密码文件。
3. 创建持久目录并设置正确属主：Prometheus 65534、Grafana 472、Loki 10001。
4. 核对 core 网络、共享资产路径、数据和 secret 根目录。
5. 启用 Host 9464 观测 listener、Loki 地址和 OTLP 日志。
6. 校验 core overlay 与独立 Compose，再分栈启动。
7. 配置 Grafana TLS 反代，检查目标 up 和仪表盘。

在安装目录下运行独立栈：

```bash
cd /opt/noctf/observability
docker --context YOUR_CONTEXT compose --env-file .env -f compose.yml config --quiet
docker --context YOUR_CONTEXT compose --env-file .env -f compose.yml up -d
```

Cap 监控只在独立 Cap 启用时加对应 overlay，基础监控不需要 Cap 网络。

## 暴露边界

| 端点 | 访问范围 |
| --- | --- |
| Host 8080 `/health/ready` | 运维检查公开入口健康 |
| Host 9464 `/metrics` | 私有网络 Prometheus |
| 公开 Host `/metrics` | 返回 404，不从平台 443 暴露指标 |
| NATS 8222 | 私有诊断 |
| Loki / exporters / Prometheus | 私有网络或本机绑定 |
| Grafana | 独立 HTTPS + 登录授权 |

日志在导出前脱敏，不记录原始 Flag、JWT、SQL 参数和明文 UserId。平台日志页需要 Loki 可读；Loki 受保护文档不可读时应处理错误，不能当作零条正常日志。

## 读取仪表盘

“NoCTF · 性能分析”位于 `/d/noctf-performance`，“日志诊断”位于 `/d/noctf-logs`。先选相同 route/time window，读请求样本量，再看 P95/P99、错误率、Npgsql 耗时、JetStream pending 和线程池队列。

低样本延迟不能证明稳定，聚合直方图也不能直接定位某条 SQL。样本为零可能表示请求没有进入该阶段，不表示这一阶段耗时为零。

Cap dashboard 的 siteverify HTTP 耗时与浏览器解 challenge 耗时是不同指标，日计数按 Cap UTC 边界重置。Prometheus 规则产生告警，实际通知需外部 Alertmanager/Grafana Alerting 配置。

## Runtime 容量规划

分别预算服务 CPU/memory、并发启动、Checker 辅助资源、TTL、Pod slots、公网端口、磁盘与镜像拉取。容器资源按服务相加；一题多个公开入口可能占多个端口。

Kubernetes 可用仓库脚本对隔离集群报告或测量：

```powershell
pwsh -File deploy/k8s/scripts/Report-Capacity.ps1 -Context YOUR_TEST_CONTEXT -TargetPods 1000 -ServiceCpuMillicores 500 -ServiceMemoryMiB 128
pwsh -File deploy/k8s/scripts/Measure-Capacity.ps1 -Context YOUR_TEST_CONTEXT -Image YOUR_TEST_IMAGE -PublicHost YOUR_TEST_NODE_DNS -Counts 100,500
```

容量报告的资源是输入假设，生命周期 fixture 成功也不等于真实题目/玩法吞吐量。区分 target、started、reachable、failed 和 not-run，保留 headroom，不从“50 Pod 本地成功”推断“生产 1000 Pod 能承载”。

Windows kind 多节点共享同一 Docker Desktop VM，不会增加物理 CPU/memory；默认回环端口预算也与生产 NodePort 不同。
