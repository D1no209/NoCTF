# 实时与站内通知

## SignalR

Hub：`/hubs/v1/competitions`，使用 Access JWT。按授权加入：

- `competition:{id}:public`；
- `team:{teamId}`；
- `competition:{id}:admin`。

推送仅是 invalidation/提示，不是事实源。消息可包含实体 Id、状态 enum、revision、公开摘要；不得包含 Flag、Token、Patch URL/ObjectKey、内部日志、Provider receipt。

事件包括：SubmissionStateChanged、RuntimeStateChanged/Expiring、LeaderboardRevisionChanged、CompetitionLifecycleChanged、Challenge/HintPublished、ManagementFailure。

API 多副本使用 Redis SignalR backplane。断线不补历史；客户端重连后通过 REST/cursor 恢复。

## 排行榜订阅感知

订阅者在 Redis 写 Competition 活跃计数/TTL 心跳。有活跃订阅时 invalidation 触发合并投影；无人查看只递增 LeaderboardRevision/dirty。首次 GET/订阅触发按需投影。

## notifications

通知是一行动态受众消息，而不是按 User 展开的收件箱，不维护已读状态或未读数。字段为 Source/Target type+id、Kind、Content、SentAt、Related 引用和 ReplyToId；Question 根、回复、状态变化、公告和自然通知共享此表。比赛管理员公告的 Source 是发送者 UserId，Target 是比赛且默认 TargetType=CompetitionCollaborators。

读取时解析受众：协作者包含 Owner/Manager/Judge/Observer，参赛者包含当前有效 Approved 队伍成员，TeamMembers 解析队伍成员，平台管理员解析当前有效管理员。Question 根发送者永久继承线程访问权限。`GET /notifications/{id}/thread` 用 recursive CTE 返回线性链；SignalR 只发刷新提示，Feed 以 PostgreSQL 为准。

Payload 由 NotificationKind 对应强类型 DTO 序列化，只含安全展示字段。按 CreatedAt desc/Id desc keyset 查询。没有 Read/MarkAllRead/Delete/Expiry API。

旧的 QQBot 公钥 Agent、群组同步和专用投递协议不在目标架构中。QQBOT 作为普通
User Bot 使用 Bearer JWT 消费自身的 `/notifications/feed`，并复用现有排行榜接口；
平台不维护 QQ 群、投递状态或 QQBOT 专用通知表。详见 [QQBOT JWT 接入](qqbot-jwt.md)。
