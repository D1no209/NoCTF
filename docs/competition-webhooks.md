# 赛事 Webhook

赛事 Webhook 将公开播报事件作为 CloudEvents 1.0 Structured JSON 发送给外部系统。浏览器的
SignalR 仍只负责失效提示；Webhook 由独立 Worker 队列异步发送，不阻塞业务请求、排行榜投影或
SignalR。

## 配置与权限

入口位于赛事管理的“Webhook”页面。每场赛事可创建任意数量的目标；平台 Administrator、赛事
Owner 和 Manager 可以创建、编辑、启停、删除、轮换密钥和发送测试，Judge 与 Observer 只能查看
目标名称、主机和状态。

目标重新启用或更换 URL 时会重置 `EnabledAt`。只有发生时间不早于 `EnabledAt` 的事件会发送，
停用期间的历史事件不会补发。删除目标会使已排队但尚未发送的命令安全失效。

## HTTP 契约

请求使用 `POST` 和 `Content-Type: application/cloudevents+json; charset=utf-8`。正文 Schema 位于
`/schemas/webhooks/competition-events-v1.schema.json`，事件类型包括赛事生命周期、题目发布或
更新、提示、公告、公开封禁或纠正、血榜以及 AWDP Break/Fix 结果。

`data.resources` 使用投递尝试时的非个性化公开表示：

- `competition` 始终存在；
- 题目、提示、血榜和 AWDP 事件包含 `challenge`；
- 公告事件包含完整公开公告正文；
- 队伍、血榜和 AWDP 事件包含公开排行榜；
- 血榜的 `teamId`、`competitionChallengeId`、`award`、`challenge`、`leaderboard` 不得缺失；Live/Frozen 的题名和队名取自同一公开排行榜投影，题目 ID 必须匹配事件，榜单必须包含该队伍；
- Blackout 期间不产生或投递血榜播报；其他需要排行榜的事件只返回 `dataScope=Hidden` 和空队伍集合，Frozen 血榜只使用冻结投影；
- 不包含个人尝试、已解锁付费提示正文、Flag、Patch、Token、Runtime Receipt 或内部失败详情。

`capturedAt` 是本次资源捕获时间，CloudEvent 的 `id` 和 `time` 在重试中保持不变；重试可以重新捕获资源。
事件同时带有 `eventSequence`、`requiredProjectionVersion` 和 `competitionRevision` 以便核对投影检查点。
Live 血榜只有在公开投影包含所需事件序号、题目和队伍后才能发送；投影尚未就绪不会退化为
`challenge=null` 或缺失队伍的成功投递。
Frozen 血榜读取按冻结时间持久化的公开快照；冻结后题目或队伍改名不会改变该快照。快照尚未捕获时
投递保持未就绪并重试，不回退到实时排行榜。

## 验签

创建或轮换目标时，平台返回一次 `whsec_` 前缀的签名密钥。接收方必须保留原始请求字节，并读取：

```text
webhook-id: <event UUID>
webhook-timestamp: <Unix seconds>
webhook-signature: v1,<Base64 HMAC-SHA256> [v1,<old-key signature>]
```

签名输入为 UTF-8/原始正文拼接：

```text
webhook-id + "." + webhook-timestamp + "." + rawBody
```

去掉 `whsec_` 后以 Base64 解码出 HMAC 密钥。使用常量时间比较，拒绝超出本地防重放窗口的时间戳，
并按 `webhook-id` 幂等处理。轮换后的 24 小时内请求同时携带新旧密钥签名。

## 投递与恢复

投递为至少一次且不保证顺序。`2xx` 确认成功；`408`、`429`、`5xx`、网络错误和超时按目标独立
退避重试，`429` 遵守 `Retry-After`；`3xx` 不跟随；`410` 自动停用目标；其他 `4xx` 标记为 DeadLetter。
公开投影未就绪与网络失败分开处理，前者按 100ms、250ms、500ms、1s、2s、5s 快速重试，
超过次数后标记可查询的 DeadLetter，绝不发送不完整资源。

公开业务事件与 PostgreSQL Webhook Outbox 在同一事务提交。JetStream 是快速唤醒和并行投递通道；
Webhook Worker 每 250ms 扫描未完成的 Outbox 与到期目标记录，覆盖提交后消息尚未发布、Worker 重启、
消息丢失等窗口。每个目标保留独立状态、HTTP 码、投影等待、重试时间和 DeadLetter 原因，不保存
完整受保护正文或签名密钥。网络超时后的重投仍可能到达接收方两次，接收方必须按 `webhook-id` 幂等。
测试投递状态是短期状态，不代表正式 Outbox。

## 观测与 SLO

赛事管理的 Webhook 页面可分页查看事件 ID/类型、目标、业务事件时间、Outbox 写入、Worker 出队、
公开投影就绪、资源捕获、首次/最近 HTTP 尝试与完成时间、HTTP 状态码、阶段性重试次数、下次重试、
资源完整性和 DeadLetter 原因；不会返回签名密钥或受保护正文。

首个 HTTP 尝试的目标为业务事件产生后 p95 小于 2 秒、p99 小于 5 秒。Prometheus 记录
`noctf_webhook_queue_age_seconds` 的 p50/p95/p99、`noctf_webhook_oldest_pending_age_seconds`、
`noctf_webhook_consumer_lag`、Webhook JetStream pending、投影等待、HTTP 耗时、重试及 DeadLetter。
p99 排队超过 5 秒触发告警，最老待投递超过 10 秒触发严重告警。网络不可用时单个目标进入独立退避，
不能使其他目标或比赛的首轮分发停顿。

默认只允许公开 HTTPS 目标，不跟随重定向，并阻止回环、链路本地、私网、云元数据地址和 DNS
重绑定。必须访问内网时，由运维设置 `Webhooks__PrivateNetworkAllowList__N`；开发 HTTP 目标还必须
同时设置 `Webhooks__InsecureHttpHostAllowList__N`。赛事管理员不能自行放开网络边界。

## 接收方最小处理顺序

1. 读取原始正文和三个 `webhook-*` Header；
2. 校验时间窗口和至少一个 HMAC 签名；
3. 以 `webhook-id` 检查幂等记录；
4. 解析 CloudEvent `type` 与 `data`；
5. 完成本地事务后返回 `2xx`；
6. 无法暂时处理时返回 `429` 或 `5xx`，永久拒绝时返回普通 `4xx`，停止订阅时返回 `410`。

面向聊天平台 BOT 的模块划分、完整事件目录、.NET 验签代码、幂等 Inbox、消息映射和联调步骤见
[Webhook 与 BOT 对接手册](webhook-bot-integration.md)。
