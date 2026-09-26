# alpha.51 缓存与协调切换验收（2026-09-26）

## 范围

- 应用业务代码不再直接读写 Redis；Redis 仅供 FusionCache L2/backplane 使用。Runner 在线/租约、排行榜发布版本和实时提示走 NATS；容量、SSO 流程和多键请求准入由 EF Core 保存。多键准入选择关系事务，是为了保留原来一次请求同时消耗多个额度的原子性；NATS KV 不提供跨键事务。
- 管理日志经脱敏 OTLP 进入私有 Loki；UserId 在 Loki 文档中加密，管理员查询时解密。旧日志只保留离线归档，不进入 Loki。
- PostgreSQL exporter 不再挂载自定义业务 SQL；Npgsql、EF Core、Wolverine、NATS、FusionCache 与标准 PostgreSQL/Redis 指标进入 Prometheus/Grafana。

## 数据与回退点

- 旧 Host 在 `08:37:45 UTC` 停止，等待超过 10 分钟后启动新 Host。新镜像 `noctf-host:0.2.1-alpha.51` 的 Docker ID 是 `sha256:6eba95f2d12d32c80a3e4b84e8866060732d6a47788da17810950d02d109bb08`；`alpha.50` 的 `sha256:175a8fc20488881a43caf202d3694b39102556dc1ff1ae963cbd594c6faf7d36` 保留。
- 停机后最终备份目录：`/opt/noctf/data/backups/prealpha51-final-20260926T0837Z/`。`postgres.dump` SHA-256 `4c2aa3e373c1b0660f77bb440703725f830417c1536cffb8c1603666e116b012`；`uploads-config.tar.gz` SHA-256 `0cabcc4731a2424ed70c805e3fa702f16aa45d38009f34d03d39751e41cd6d29`。已验证 `pg_restore --list` 与 tar 目录可读。备份包括旧 Compose、环境配置、上传目录和数据库中的 Data Protection Keys。
- 旧平台日志主档在 `/opt/noctf/data/backups/platform-logs-prealpha51-20260926T064354Z.tar.gz`，2,963 条，SHA-256 `970ebee2ef81147768b428df8eabe2830eccdf2c03f7d8a09f9510971b1bd49`；最终增量 `/opt/noctf/data/backups/platform-logs-final-delta-20260926T083644Z.jsonl`，3 条，SHA-256 `37092e65af2f0c67d92d9209f9232b4365dea9603134351771eb798382a8e495`。两者之间的空档经旧管理导出接口确认无记录。
- 通过 `NoCTF.Host --migrate-only` 应用了 EF 生成的 `PersistedSsoFlows`、`PersistedRequestAdmission` 两个加法迁移；未手动修改 PostgreSQL 或 Redis 业务数据。回退时恢复旧 Compose/.env、旧镜像及必要时从上述最终备份恢复数据；新表对旧镜像是加法变更，不能把它们误认为旧模型兼容层。

## 验证结果

- Release 构建无警告/错误；非集成 TUnit 1,230 通过；完整容器集成 264 通过、6 项需专用 Kubernetes/Libvirt/daemon-host 环境而跳过。日志加密/真实 Loki、FusionCache 命中事件和 Runner 数据库瞬断另有针对性测试。隔离 CTF、AWD、AWDP、KoH 完整 E2E 和 Redis/PostgreSQL 重启恢复全部通过；最后一处仅针对无缓存测试宿主的 DI 防护调整后，最终镜像再次通过 CTF 流程冒烟。
- OpenAPI 导出及 SDK 再生成前后 18 个产物哈希不变；EF 模型无待生成迁移。Bun 测试 578/578、typecheck、架构审计和静态生成通过。
- 生产切换前后：用户 `97 → 97`、题库模板 `63 → 63`、比赛 `1 → 1`、公开队伍 `42 → 42`、上传文件 `191 → 191`；活跃 Runtime `0`。Host 健康、零重启，管理员日志接口 200，Loki 与各采集目标在线。上线约 10 分钟内 Prometheus 观察到约 587 次 REST 成功、未见错误 outcome。未向真实比赛提交合成 Flag。
- 同机同路径的有界只读样本，预热 5 次后各 50 次：比赛列表 p95 `26.7 → 25.2 ms`，排行榜 schema `38.9 → 37.2 ms`，排行榜题目目录 `41.0 → 44.8 ms`；排行榜正文初次 50 次 p95 `120.6 → 133.2 ms`，追加 100 次 p95 `119.4 ms`，响应大小均不变。样本混有网络/冷态抖动，不据此宣称提速；旧版自然流量与新版本混合窗口也不可作为严格因果对比。真实 Flag 端到端延迟需等待足够自然流量。

旧 Redis 业务键未被人工清理；它们不再由新应用读取。正式回退仍需先停新 Host，避免混合版本并发运行。
