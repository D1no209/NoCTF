# 赛事 Webhook 验收记录

日期：2026-09-20。实现基线 `a2e5af535`。本轮仅在隔离本地工作树实现、测试和提交，没有推送或
部署；原工作区的认证、权限、归档、前端和 OpenAPI 在途修改未被暂存或提交。

## 已验证

- CloudEvents v1 JSON Schema 固定 11 种事件类型和必需 Envelope 字段。
- 真实 PostgreSQL 验证 JSONB、密钥密文、赛事＋目标 AAD、24 小时轮换、1,001 个目标及 100 条
  分批扫描。
- 真实 NATS JetStream/Wolverine 验证 Webhook 工作流使用独立 durable stream 和 consumer。
- 真实 Redis 验证测试投递状态及完成状态的十分钟临时存储。
- HMAC 测试验证 `id.timestamp.rawBody`、正文篡改和 24–64 字节密钥要求。
- SSRF 单元测试覆盖 IPv4/IPv6 回环、RFC1918、链路本地、共享地址和公有地址。
- 前端专项测试、TypeScript 检查和架构审计通过。
- OpenAPI 与生成 SDK 连续执行哈希一致。

本轮最终专项结果：Release 全解构建零警告；后端非集成测试 `1391/1391` 通过；Webhook 的真实
PostgreSQL、Redis、NATS 集成测试 `3/3` 通过；Webhook 签名与网络单元测试 `11/11` 通过；前端
Webhook 专项 `2/2`、typecheck、架构审计和生产生成通过。

前端全量测试为 `594` 通过、`2` 失败。失败分别是既有 Runtime 管理布局断言和全局字体断言；对应
测试与样式文件相对本轮基线没有差异，因此记录为基线已有失败，未混入本功能提交修正。

## 投产前核对

- 设置 `Webhooks__PublicBaseUrl` 为平台公开 HTTPS Origin。
- 若接收方位于内网，按最小范围配置私网允许列表；不要把整段 Docker/Kubernetes 网络默认放开。
- 监控 `NOCTF_WEBHOOK` stream、`noctf-webhook` consumer、Wolverine error queue 和 Worker HTTP 失败日志。
- 用管理页测试事件核对接收端原始字节验签、时间窗口和事件 ID 去重。
- 滚动回滚只需停用目标或回滚应用；保留 `webhook_configuration` 列和加密密钥，不执行破坏性数据库降级。
