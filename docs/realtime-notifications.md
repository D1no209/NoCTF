# 实时与站内通知

## SignalR

Hub：`/hubs/v1/competitions`，使用 Access JWT。按授权加入：

- `competition:{id}:public`；
- `team:{teamId}`；
- `competition:{id}:admin`。

推送仅是 invalidation/提示，不是事实源。消息可包含实体 Id、状态 enum 与公开摘要；不得包含 Flag、Token、Patch URL/ObjectKey、内部日志、Provider receipt。

事件包括：GameplayFactStateChanged、RuntimeStateChanged/Expiring、LeaderboardRefreshed、CompetitionLifecycleChanged、Challenge/HintPublished、ManagementFailure。

API 多副本使用 Redis SignalR backplane。断线不补历史；客户端重连后通过 REST/cursor 恢复。

## 排行榜刷新

排行榜刷新不感知订阅者。Worker 每 15 秒扫描 `LeaderboardDirty`，只为脏比赛生成全量快照并替换 FusionCache；SignalR 的 `leaderboardRefreshed` 只在新快照写入成功后发送。缓存缺失的 GET 置 Dirty 并返回 202，订阅动作本身不触发投影。

## notifications

通知是一行动态受众消息，而不是按 User 展开的收件箱，不维护已读状态或未读数。字段为 Source/Target type+id、Kind、Content、SentAt、Related 引用和 ReplyToId；Question 根、回复、状态变化、公告和自然通知共享此表。比赛管理员公告的 Source 是发送者 UserId，Target 是比赛且默认 TargetType=CompetitionCollaborators。

读取时解析受众：协作者包含 Owner/Manager/Judge/Observer，参赛者包含当前有效 Approved 队伍成员，TeamMembers 解析队伍成员，平台管理员解析当前有效管理员。Question 根发送者永久继承线程访问权限。`GET /notifications/{id}/thread` 用 recursive CTE 返回线性链；SignalR 只发刷新提示，Feed 以 PostgreSQL 为准。

Payload 由 NotificationKind 对应强类型 DTO 序列化，只含安全展示字段。按 CreatedAt desc/Id desc keyset 查询。没有 Read/MarkAllRead/Delete/Expiry API。

### 前端信息分层

- 比赛「动态」是按访问级别过滤后的不可变 `competition_events` 完整事实流，用于追溯比赛状态和操作，不等同于个人消息。
- 题目页「赛事播报」是公开事实流的紧凑投影，只展示一二三血、作弊封禁、申诉纠正、提示发布、题目描述更新和题目开放；不复制事件数据。
- 全局「消息中心」使用 `GET /notifications?scope=Inbox`，只展示手工官方通知以及与当前账号、队伍或管理职责直接相关的消息。系统自动写入 `CompetitionParticipants` 的公开播报不进入个人收件箱，避免不同账号收到相同内容后被错误描述为“你的队伍”。
- `GET /notifications/feed` 和默认 `scope=All` 保持完整动态受众语义，BOT 与已有消费者不受个人收件箱投影影响。

旧的 QQBot 公钥 Agent、群组同步和专用投递协议不在目标架构中。QQBOT 作为普通
User Bot 使用 Bearer JWT 消费自身的 `/notifications/feed`，并复用现有排行榜接口；
平台不维护 QQ 群、投递状态或 QQBOT 专用通知表。详见 [QQBOT JWT 接入](qqbot-jwt.md)。
