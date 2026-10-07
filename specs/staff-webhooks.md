# Staff Webhook v1

工作人员订阅与公开 Webhook 独立，仅分享获准的疑似作弊、咨询和封禁申诉摘要。聊天群、BOT master、聊天凭据、消息模板及出站状态由 BOT 维护。

## 管理与授权

入口：`/admin/competitions/:id/webhooks` 的工作人员分区，或 `/competitions/:id/staff?kind=Webhooks`。平台 Administrator、赛事 Owner 和 Manager 可配置；Judge 和 Observer 只读名称、状态、类别和投递结果，不能读取完整 URL 或签名密钥。Draft 和 StaffOnly 赛事可配置。

```text
GET    /api/v1/admin/competitions/{competitionId}/staff-webhooks
POST   /api/v1/admin/competitions/{competitionId}/staff-webhooks
PUT    /api/v1/admin/competitions/{competitionId}/staff-webhooks/{targetId}
DELETE /api/v1/admin/competitions/{competitionId}/staff-webhooks/{targetId}
POST   /api/v1/admin/competitions/{competitionId}/staff-webhooks/{targetId}/rotate-secret
POST   /api/v1/admin/competitions/{competitionId}/staff-webhooks/{targetId}/test-deliveries
GET    /api/v1/admin/competitions/{competitionId}/staff-webhooks/{targetId}/test-deliveries/{deliveryId}
GET    /api/v1/admin/competitions/{competitionId}/staff-webhooks/{targetId}/deliveries
GET    /api/v1/competitions/{competitionId}/staff-work-items
```

列表支持 offset/limit/desc。待办查询默认 pendingOnly=true，使用当前关系事实，详情仍走原有鉴权、审计、回复和裁定接口。

创建／更新正文为 name、endpointUrl、enabled、categories。类别为 CheatIncident、Consultation、BanAppeal；省略默认三类，至少选一类且不重复。创建／轮换响应的 signingSecret 仅返回一次。接收端和平台公开来源均须 HTTPS，复用现有 DNS/IP 防 SSRF、防重定向与私网白名单。

每次正式发送前重新检查批准人仍是活跃管理员、Owner 或 Manager。撤销职务、停用账号或删除比赛后，待发送私有事件失效。停用／删除／授权撤销时尽可能发送不含事项的停用控制事件。删除保留加密投递墓碑供控制事件与诊断使用，不出现在目标列表。

## 信封和签名

CloudEvents 1.0 Structured JSON，Content-Type 为 application/cloudevents+json。source 为 HTTPS 公开来源下的 `/api/v1/competitions/<id>/staff-events`，不复用公开赛事 source。Schema 发布在 `/schemas/webhooks/staff-events-v1.schema.json`。

所有 data 都有 subscriptionId、competition（仅 id/title）、持久递增的正整数 sequence。以下 type 均以前缀 `com.noctf.staff.` 开始：

| type 后缀 | 附加字段 |
| --- | --- |
| work-item.created.v1 | changeKind=Created、item |
| work-item.updated.v1 | changeKind、item |
| pending.snapshot.v1 | snapshotId、snapshotReason、asOfSequence、pageIndex、pageCount、items、pendingUrl |
| heartbeat.v1 | capturedAt、latestBusinessSequence |
| subscription.disabled.v1 | disabledAt |
| test.v1 | 无事项 |

changeKind 为 Created、ParticipantMessage、StaffReply、StatusChanged、MetadataChanged。同一次回复及状态变化合并为一个完整 item 更新；元数据变化不应触发聊天进展播报。

同一业务事件向多个目标使用相同 id/sequence；接收方以 (source,id) 持久去重。重试保持原始正文，重新生成签名时间戳。Header 为 webhook-id、webhook-timestamp、webhook-signature。HMAC-SHA256 输入是 UTF-8 的 `id.timestamp.` 后拼接原始正文；密钥去除 whsec_ 后 Base64 解码。接收方检查五分钟窗口并常量时间比较。

只有持久化后 HTTP 204 才算正式成功，200/202 会重试。429/5xx 使用退避及 Retry-After，最长 24 小时；410 停用目标。轮换时签上当前和上一密钥，上一密钥 24 小时后失效。测试不触发事项或聊天消息。

## 允许字段及工作流

item 完整投影包含 kind/id、对应 status、requiresStaffAction、当前连续等待的 actionRequiredSince、createdAt/updatedAt、可选 detectedAt、team/relatedTeam/challenge、reasonCode、系统咨询 subject、actorDisplayName、managementUrl 和 lastChangedSequence。

不包含 Flag、value、源 IP、个人私有档案、Runtime receipt、附件内容、咨询标题正文、申诉陈述或裁定理由；黑榜期间也不增加计分或血榜字段。深链和 pendingUrl 均为同源 HTTPS，不带凭据。

作弊状态 Pending/Confirmed/Dismissed/Superseded/Corrected；咨询 Pending/Replied/Resolved/Closed；申诉 Submitted/Upheld/Accepted。工作人员发起、等待选手的咨询不进入待办；选手补充开始新一轮等待。同一连续等待的后续消息不延后起点。旧封禁的申诉不进入当前待办。

## 顺序、快照与恢复

每场赛事通过带并发戳的关系流分配序号，业务事实、允许投影及事件 Outbox 同事务保存。Worker 扫描恢复漏发唤醒，通过 NATS Webhook 队列处理；投递尝试令牌和事务准备防止重复队列消息同时发起 HTTP。HTTP 仍为至少一次。

启用创建使用 Initial；重新启用、改变地址或类别使用 Reenabled；系统及故障恢复使用 Resync。快照在一致性事务水位读取当前待办，每页至多 100 项，空集合为一页。每页共享 snapshotId/asOfSequence，pageIndex 从 0 起。快照所有页完成 204 接收前，不发送水位之后的业务增量；水位之内历史事件不逐条重播。

每 60 秒心跳。持续故障超过 180 秒暂停业务增量，心跳探测成功后生成完整 Resync 快照，完成后恢复较新增量。永久失败或超出重试窗口也要求重新对齐。

BOT 收齐所有页后再替换待办，保留 lastChangedSequence 大于水位的较新事项，不将快照缺失的旧待办推断成具体裁定。按事项序号处理乱序，不能用最近一个全局序号丢弃其他事项。Initial/Reenabled 可发一次摘要；Resync 只修复状态，不补播历史进展。请求最大 1 MiB。

## 虚构样例与互操作

`backend/artifacts/webhooks/staff-events-v1.examples.json` 含三类创建及进展、空快照、两页快照、心跳、停用、测试，共 12 组精确 rawBody 和签名 Header。公开 demoSigningSecret 禁止用于生产；验样例时以 capturedAt 为测试时钟。样例由平台真实序列化器和签名器生成。

本地已验证 BOT 的真实验签、投影及持久接收入口：首次和重复请求返回 204，过期签名被拒绝。生产仍须手动配置获授权的目标和工作群，核对 HTTPS 反向代理、双方密钥及 Worker Webhook 队列；本地验收不会启用生产群。
