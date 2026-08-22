# NoCTF 压测基线

该目录提供可重复的 k6 基线，用于在队列、排行榜锁和 Runner 分配优化前后比较吞吐与延迟。脚本默认仅允许本机地址；远程目标必须显式设置 `ALLOW_REMOTE_TEST_TARGET=I_UNDERSTAND`，且只能使用获授权的可丢弃测试环境。

## 采集窗口

执行前启动 `deploy/docker-compose.observability.yml`，预热 2 分钟，然后记录：

- k6：请求吞吐、失败率、P50/P95/P99。
- Prometheus：API、Wolverine 队列、PostgreSQL、Redis、Runner 和排行榜指标。
- 测试环境的 CPU、内存、磁盘与网络使用率。
- 测试提交、Runtime 和比赛均使用唯一 `PERF-*` 前缀，测试后精确清理。

## 本机执行

```powershell
docker run --rm --add-host host.docker.internal:host-gateway `
  -e BASE_URL=http://host.docker.internal:8080 `
  -e COMPETITION_ID=<competition-id> `
  -e COMPETITION_CHALLENGE_ID=<competition-challenge-id> `
  -e NOCTF_LOGIN=<test-login> `
  -e NOCTF_PASSWORD=<test-password> `
  -e MUTATION_OPERATION=runtime-state `
  -v ${PWD}/tests/load:/scripts:ro `
  grafana/k6:1.4.2 run /scripts/noctf-baseline.js
```

凭据只通过进程环境传入，禁止写入脚本、结果文件或提交记录。`MUTATION_OPERATION` 可选 `runtime-state`、`runtime-start`、`flag`；Flag 压测必须额外设置 `FLAG_VALUE`，并使用不会污染真实成绩的专用测试比赛。

常用调节项：

| 环境变量 | 默认值 | 含义 |
| --- | ---: | --- |
| `READ_START_RPS` | 5 | 初始公开读取速率 |
| `READ_TARGET_RPS` | 25 | 稳态公开读取速率 |
| `RAMP_DURATION` | 2m | 升压时间 |
| `STEADY_DURATION` | 5m | 稳态时间 |
| `MUTATION_RPS` | 1 | 认证操作速率 |
| `MUTATION_DURATION` | 5m | 认证操作持续时间 |
| `REQUEST_TIMEOUT` | 10s | 单请求超时 |

## 基线记录

每次运行应保存 k6 汇总、Grafana 时间区间和代码提交哈希。优化前后必须使用相同测试数据、并发、持续时间和硬件；跳过或被环境阻塞的场景不得记录为通过。
