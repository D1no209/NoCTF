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

`data.resources` 使用投递尝试时的完整、非个性化公开 API 表示：

- `competition` 始终存在；
- 题目、提示、血榜和 AWDP 事件包含 `challenge`；
- 公告事件包含完整公开公告正文；
- 队伍、血榜和 AWDP 事件包含公开排行榜；
- Blackout 返回空排行榜，Frozen 返回冻结快照；
- 不包含个人尝试、已解锁付费提示正文、Flag、Patch、Token、Runtime Receipt 或内部失败详情。

`capturedAt` 是资源快照生成时间，CloudEvent 的 `time` 是业务事件发生时间。资源是当前公开投影，
不是历史定义快照。

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

投递为至少一次且不保证顺序。`2xx` 确认成功；`408`、`429`、`5xx`、网络错误和超时以抖动退避
重试约 24 小时；`3xx` 不跟随；`410` 自动停用目标；其他 `4xx` 进入 Wolverine 错误队列。

业务事件先由 PostgreSQL 事务 Outbox 写入 JetStream。Webhook fan-out 每次最多扫描 100 个目标，
再向独立 `noctf.webhook` 工作队列发送只含 ID 的命令。Worker 重启、消息重复或 Redis 丢失不会丢失
正式投递；Redis 只保存十分钟的测试投递状态。

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
