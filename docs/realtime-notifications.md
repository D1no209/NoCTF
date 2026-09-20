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

排行榜刷新不感知订阅者。影响投影的业务事务通过 Outbox fan-out 到排行榜 Sticky PostgreSQL
endpoint；消费者立即失效缓存，由 Singular Agent 固定 500 ms 合并并派发全量 PostgreSQL 投影。
只有新快照成功写入缓存后才发送 `leaderboardRefreshed`。缓存缺失由读取路径重建，不存在 Dirty 扫描。

## notifications

通知是一行动态受众消息，而不是按 User 展开的收件箱，不维护已读状态或未读数。字段为
Source/Target type+id、Kind、Content、SentAt、Related、ThreadRootId 和可空 ReplyToId；Question 根、
回复、状态变化、公告和自然通知共享此表。根的 ThreadRootId 为空，回复/状态事件指向根；ReplyToId
只表示非唯一回复上下文。

读取时解析受众：协作者包含 Owner/Manager/Judge/Observer，参赛者包含当前有效 Approved 队伍成员，
TeamMembers 解析队伍成员，平台管理员解析当前有效管理员。Question 根发送者永久继承线程访问权限。
`GET /notifications/{id}/thread` 按 ThreadRootId 查询并以 `(sent_at,id)` 排序；并发回复全部 append。
SignalR 只发刷新提示，Feed 以 PostgreSQL 为准。

Payload 由 NotificationKind 对应强类型 DTO 序列化，只含安全展示字段。按 CreatedAt desc/Id desc keyset 查询。没有 Read/MarkAllRead/Delete/Expiry API。

### 前端信息分层

- 比赛「动态」是按访问级别过滤后的不可变 `competition_events` 完整事实流，用于追溯比赛状态和操作，不等同于个人消息。
- 题目页「赛事播报」是公开事实流的紧凑投影，只展示一二三血、作弊封禁、申诉纠正、提示发布、题目描述更新和题目开放；不复制事件数据。
- 全局「消息中心」使用 `GET /notifications?scope=Inbox`，只展示手工官方通知以及与当前账号、队伍或管理职责直接相关的消息。系统自动写入 `CompetitionParticipants` 的公开播报不进入个人收件箱，避免不同账号收到相同内容后被错误描述为“你的队伍”。
- `GET /notifications/feed` 和默认 `scope=All` 保持完整动态受众语义。公开比赛公告正文另由
  `GET /competitions/{competitionId}/announcements` 提供给所有普通 Bearer 客户端，不改变通知受众。

旧的 QQBot 公钥 Agent、群组同步和专用投递协议不在目标架构中。QQBOT 作为普通
User Bot 使用 Bearer JWT 加入公开比赛 SignalR group，并在失效提示后重读通用比赛、题目、
排行榜与公告接口；平台不维护聊天 Provider、群、权限、投递状态或 BOT 专用通知表。Bot 不加入 Observer、
参赛队伍或其他工作人员角色。详见 [QQBOT 公开只读接入](qqbot-jwt.md)。
