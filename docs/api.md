# API v1 清单

本文列出目标资源面。每个路由对应一个 StronglyTyped FastEndpoint 文件；请求/响应/Validator 放在该文件。业务码和精确 result union 依 [API 通用规范](api-conventions.md)。

## Platform

### 可选公网访问（仅平台管理员）

```text
GET /api/v1/admin/platform/public-gateway
PUT /api/v1/admin/platform/public-gateway
GET /api/v1/admin/platform/public-gateway/status
```

配置只允许选择部署批准的连接器、HTTPS 入口、主机显示与端口配额，不返回配对密钥或允许任意网络探测。保存返回 202 和状态地址；状态读取使用有界缓存，不同步请求 Docker/FRP。Runtime 响应的 `access` 与 Runtime 自身状态独立，公网不可用时不回落为内网地址。默认未安装时仍使用原有直连行为。

### 私密账户资料（不属于公开用户协议）

```text
GET /api/v1/auth/me/school-identity
PUT /api/v1/auth/me/school-identity
GET /api/v1/admin/platform/users/{userId}/private-profile
GET /api/v1/admin/competitions/{competitionId}/teams/{teamId}/members/{userId}/private-profile
```

```text
GET  /api/v1/platform/configuration
GET  /api/v1/platform/logo
```

公开平台配置返回名称、简介与通过 LinkGenerator 生成的 revisioned Logo 路径；未配置自定义
Logo 时 Logo 路由返回 404，前端使用随包默认品牌资源。

## Authentication

```text
POST /api/v1/auth/register
POST /api/v1/auth/login
POST /api/v1/auth/refresh
POST /api/v1/auth/logout
POST /api/v1/auth/email-verification/request
POST /api/v1/auth/email-verification/verify
POST /api/v1/auth/email-verification/resend
POST /api/v1/auth/password-reset/request
POST /api/v1/auth/password-reset/complete
GET  /api/v1/auth/me
PUT  /api/v1/auth/me/profile
POST /api/v1/auth/me/avatar
GET  /api/v1/auth/me/wallpaper
POST /api/v1/auth/me/wallpaper
PUT  /api/v1/auth/me/wallpaper-preference
PUT  /api/v1/auth/password
POST /api/v1/auth/logout-all
GET  /api/v1/users/{userId}
GET  /api/v1/users/{userId}/avatar
```

Login/Refresh 返回 AccessToken 与 ExpiresAt；Refresh Cookie 不出现在 body。
公开用户资料默认隐藏邮箱；本人和 Administrator 始终可见，其他访问者仅在用户主动公开后可见。
密码重置请求对所有合法邮箱格式统一返回 202；完成接口成功返回 204，无效、过期或已消费 Token 返回 typed 400 `InvalidOrExpired`。完成后不会签发新 Token，并清除当前 Refresh Cookie。

## Competition 与 Team

```text
GET  /api/v1/competitions
GET  /api/v1/competitions/{competitionId}
GET  /api/v1/competitions/{competitionId}/poster
GET  /api/v1/competitions/{competitionId}/leaderboard
GET  /api/v1/competitions/{competitionId}/leaderboard/trends
GET  /api/v1/competitions/{competitionId}/leaderboard/challenges
GET  /api/v1/competitions/{competitionId}/leaderboard/schema
GET  /api/v1/competitions/{competitionId}/leaderboard/teams/{teamId}/columns/{columnIndex}
GET  /api/v1/competitions/{competitionId}/leaderboard/teams/{teamId}/adjustments
GET  /api/v1/competitions/{competitionId}/tracks

POST /api/v1/competitions/{competitionId}/teams
GET  /api/v1/competitions/{competitionId}/teams
GET  /api/v1/competitions/{competitionId}/teams/me
GET  /api/v1/competitions/{competitionId}/teams/{teamId}
PUT  /api/v1/competitions/{competitionId}/teams/{teamId}
DELETE /api/v1/competitions/{competitionId}/teams/{teamId}
GET  /api/v1/competitions/{competitionId}/teams/{teamId}/avatar
POST /api/v1/competitions/{competitionId}/teams/{teamId}/avatar
DELETE /api/v1/competitions/{competitionId}/teams/{teamId}/avatar
POST /api/v1/competitions/{competitionId}/teams/join
POST /api/v1/competitions/{competitionId}/teams/{teamId}/invitation-token/rotate
POST /api/v1/competitions/{competitionId}/teams/{teamId}/captain/transfer
DELETE /api/v1/competitions/{competitionId}/teams/{teamId}/members/{userId}
DELETE /api/v1/competitions/{competitionId}/teams/me/membership
POST /api/v1/competitions/{competitionId}/teams/{teamId}/registration/resubmit
```

Team response 使用 CaptainId 与 MemberIds 数组，不返回成员顺序。
Team Avatar 与 Competition Poster 都通过不可变 File 引用上传；上传/清除需要对应管理权限，读取路由不暴露通用 File 下载能力。
邀请加入使用队伍当前的 32 位 InvitationToken；比赛运行中仅在 `AllowTeamRegistrationWhileRunning` 开启时允许加入。
加入失败返回强类型 `TeamMembershipFailureCodeProtocol`，前端不得将阶段锁定、队伍已满或无效邀请码表现为无响应。

Competition 列表只返回调用者可见状态：匿名可见 Visible/Published/Running/Paused/Finished，Draft 仅管理者。Team 私有字段（InvitationToken、Ban 原因）只按权限返回；公开 Team DTO 永不包含 InvitationToken。

Leaderboard GET 的 statusUrl 指回自身：无快照时原子置 Dirty 并返回仅含 competitionId/state/statusUrl 的 202；Dirty 且有旧快照仍返回 200 完整旧快照；无快照且最后投影失败返回 503 ProblemDetails。响应不创建 ProjectionOperation。
排行榜拆分为低频题目目录、低频列结构和高频稀疏快照。`leaderboard/challenges` 返回稳定题目标识；
`leaderboard/schema` 返回模式、轮次和 `columnIndex` 定义；`leaderboard` 返回 actors、teams、tracks
以及队伍的非空 `slots[]`。每个 slot 明确区分 `Pending`/`Settled`，并提供 earned、deducted、net、
breakdown 与压缩后的权威事实。完整 slot 历史通过带签名游标的 team/column 明细路由读取；
超出主快照上限的全局调分历史通过 team/adjustments 签名游标路由读取。
CTF 的 `leaderboard/trends` 返回与当前调用者、赛道权限和冻结截止时间一致的队伍累计分值序列；
曲线由完整权威计分分配和已应用人工调分生成，最终点始终与同版本排行榜总分一致。其他模式不使用该接口。
CTF 按题目列，AWD/AWDP 按题目×轮次列，KoH 按控制结算列；AWDP 当前未结算轮次只暴露操作
状态，所有分数字段为 null，轮次结算后才进入累计分数。前端不得重算、预估或重新排序。
AWDP 队伍行额外返回完整已结算历史的 `attackScore`、`defenseScore` 和按题目的
`challengeScores`；这些累计字段不受主快照有界轮次窗口影响，前端不得从可见 Slot 重新求和。
排行榜响应同时返回 `visibility`、`dataScope` 与 nullable `dataAsOf`。Frozen 返回截止时刻的持久化
快照；Blackout 对参赛者和 Bot 返回 `Hidden` 与空集合，但不关闭题目、Runtime、提交或本人
提交结果。比赛结束时自动恢复最终实时榜单。人工 Administrator/Owner/Manager/Judge/Observer
保持实时视图；Observer Bot 在 Blackout 中仍为 Hidden。

## Player Challenge

```text
GET  /api/v1/competitions/{competitionId}/challenges
GET  /api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}
GET  /api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/attachments
GET  /api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/attachments/{attachmentId}
GET  /api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/attachment
POST /api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/hints/{hintId}/unlock
```

`attachments` 列表与带 Id 下载只用于 `All`；`attachment` 单数路由只用于 `RandomOnePerTeam`，抽取发生在该 GET 内且请求没有 AttachmentId。策略不匹配返回 404。KoH 详情在 Running 时对本队返回 Control Flag 与 shared Hill 的公开 urls，不返回 ControlCheckUrl；CTF PerTeamRuntime Flag 不单独返回，注入 Runtime 环境。

Hint unlock 返回 `202 Accepted`，响应只包含 `gameplayFactId`、`state` 和状态 URL；客户端通过 GameplayFact 状态与 Hint 查询获取最终结果。成功结果为 `Unlocked`，重复/拒绝使用强类型 Result/FailureCode。

玩家 Challenge 列表/详情绝不返回 FlagId、正确答案、Flag 数量、RandomOne 候选附件数量/文件名、内部 Runtime 配置或 ObjectKey。All 的列表返回 AttachmentId、显示名、MIME、字节数；RandomOne 首次单数下载请求完成原子抽取后直接 302/stream 被选文件，客户端不能选择，之后固定返回同一附件。
Blackout 不隐藏题面、附件、Runtime 或提交入口；列表与详情返回同一
`leaderboardVisibility` 与 `dataScope`，题目当前分值由模式规则和排行榜投影提供。

## Questions and announcements

```text
GET  /api/v1/competitions/{competitionId}/questions
POST /api/v1/competitions/{competitionId}/questions
GET  /api/v1/competitions/{competitionId}/questions/{threadRootId}
POST /api/v1/competitions/{competitionId}/questions/{threadRootId}/messages
PUT  /api/v1/competitions/{competitionId}/questions/{threadRootId}/status
POST /api/v1/admin/competitions/{competitionId}/announcements
GET  /api/v1/notifications/{notificationId}/thread
GET  /api/v1/competitions/{competitionId}/events
```

咨询默认私密。Challenge 咨询绑定一个 CompetitionChallenge，由比赛
Administrator/Owner/Manager/Judge 与关联模板 Owner/Manager 处理；Platform 咨询由比赛处理者处理。
Observer 只读。只有 Running/Paused 且已批准队伍内的 Human 用户可以发起咨询，Bot 不允许使用。
Finished 后对话只读。Resolved 可由提问者通过追问重新打开，Closed 为终态。

Question 根就是 `notifications.id`；回复和状态事件使用 `ThreadRootId` 指向根，`ReplyToId` 只是
可空、非唯一回复上下文。线程按 `(sent_at,id)` 排序；读取权限在根发送者离队后仍保留。
删除 Question publication；面向全体参赛者的通用说明创建 CompetitionAnnouncement。管理员公告 Source 是发送者 UserId，
Target 是 CompetitionId，默认 TargetType=CompetitionCollaborators；面向选手必须显式选择 CompetitionParticipants。所有写操作采用 last-write-wins，发起与回复共享每用户/IP
每分钟 8 次的限流策略。

## GameplayFact 动作

```text
POST /api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/flag-submissions
POST /api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/practice-flag
POST /api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/awdp-break-flag-judgement
POST /api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/awdp-defense-targets
POST /api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/awdp-defense-targets/{runtimeInstanceId}/fix
GET  /api/v1/competitions/{competitionId}/gameplay-facts/{gameplayFactId}
GET  /api/v1/competitions/{competitionId}/gameplay-facts/{gameplayFactId}/value
GET  /api/v1/competitions/{competitionId}/gameplay-facts
```

玩家提交列表不返回明文 Value。`value` 路由仅允许当前比赛中该 GameplayFact 所属队伍的成员
按需读取自己的 FlagAttempt/BreakAttempt 原文；跨队、其他事实类型和错误比赛统一返回 404，
成功响应禁止缓存。

Flag request：

```json
{ "flag": "flag{...}", "flags": null }
```

AWD 可二选一使用 flag 或 flags；CTF/AWDP Break 必须只用 flag。AWD flags 不限元素数、无总字节上限、
无尝试次数上限；每项仍必须为 1–4096 UTF-8 bytes 且禁止 NUL。任一项格式非法则整个请求返回
`FlagInvalid`/400 且零写入。每项创建独立 GameplayFact；数据库不保存 batch。单 flag 的 202 body 返回
GameplayFactId/state/statusUrl；flags 保持输入顺序，不返回 BatchId。

AWDP Break 首次正确后，普通 Flag 提交不再创建新 GameplayFact、计分、播报或改变 Runtime；只读 judgement 路由仅验证当前 Flag 正误，供赛后复现和 WP 使用。第一条 AWDP 防御路由申请一次性干净 Target，返回 RuntimeInstanceId 和状态地址。Target 为 Queued、Provisioning 或 Running 时，第二条 multipart 单文件路由即可在事务中唯一绑定 PatchUpload/Pending FixAttempt，并直接返回 PatchUploadId、GameplayFactId 与事实状态；没有独立的 Fix trigger。Target Running 后验证自动继续。归档校验失败不消费 Target，成功绑定后同一 Target 不再接受第二次上传。

## Runtime

```text
GET  /api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/runtime
POST /api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/runtime/start
POST /api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/runtime/stop
POST /api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/runtime/reset
POST /api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/runtime/extend
GET  /api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/targets
GET  /api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/awdp-state
```

变更返回 202、RuntimeInstanceId 与 status URL。只有 Running 返回展开后的 `urls: string[]`。

CTF 开放四个动作；AWD 玩家 GET/Reset，Start/Stop 由平台生命周期控制且 Extend 不支持；KoH 不开放普通玩家 Runtime 动作。AWDP schema v4 的玩家 `Player/AwdpAttack` Runtime 是长期攻击靶机，可通过统一 Runtime 路由启停、重置和续期；一次性 `AwdpTarget` 防御验证环境只通过申请与 Fix 上传路由管理，不通过普通玩家 Runtime 动作暴露。路由保留统一形状，但每个 Endpoint 的 Application policy 必须按 GameMode/RuntimePurpose 拒绝不支持动作。

`awdp-state` 仅用于 AWDP schema v4 参赛队伍，返回本队攻击 Runtime、Break/Fix 生效轮次、当前逻辑轮次与最近一次防御 Target 的 TargetProvisioning/AwaitingPatch/PatchApplying/CheckerRunning/Completed 阶段；不返回动态 Flag 明文、Provider receipt、内部地址或其他队伍状态。

`targets` 只用于 AWD：加固期只返回本队 TeamId/Name/Running urls；加固期结束后返回所有 Approved、未 Ban、未删除队伍的 TeamId/Name 与其 Running `Exposure=Participants` urls，未 Running 的目标保留 Team 条目但 urls 为空。本队仍可从 Runtime GET 取得 OwnerOnly urls。排序 Team.RegisteredAt/TeamId。targets 永不返回 Flag、服务状态、Provider receipt 或内部地址；其他模式返回 404。

## Admin Competition

`/admin` 是管理资源命名空间，不代表只允许平台 Administrator；每个 Endpoint 按产品权限矩阵允许 Administrator 或该 Competition 的 Owner/Manager/Judge/Observer。写 Endpoint 再按动作收紧，Observer 永远只读，Judge 只执行判题/重判类写入。

```text
GET  /api/v1/admin/competitions
POST /api/v1/admin/competitions
GET  /api/v1/admin/competitions/{competitionId}
PUT  /api/v1/admin/competitions/{competitionId}
GET  /api/v1/admin/competitions/{competitionId}/configuration
PUT  /api/v1/admin/competitions/{competitionId}/configuration
GET  /api/v1/admin/competitions/{competitionId}/leaderboard-visibility
PUT  /api/v1/admin/competitions/{competitionId}/leaderboard-visibility
GET  /api/v1/admin/competitions/{competitionId}/tracks
PUT  /api/v1/admin/competitions/{competitionId}/tracks
DELETE /api/v1/admin/competitions/{competitionId}
POST /api/v1/admin/competitions/{competitionId}/restore
GET  /api/v1/admin/competitions/{competitionId}/hard-delete-preview
DELETE /api/v1/admin/competitions/{competitionId}/hard-delete
POST /api/v1/admin/competitions/{competitionId}/force-delete
POST /api/v1/admin/competitions/{competitionId}/publish
POST /api/v1/admin/competitions/{competitionId}/make-visible
POST /api/v1/admin/competitions/{competitionId}/start
POST /api/v1/admin/competitions/{competitionId}/pause
POST /api/v1/admin/competitions/{competitionId}/resume
POST /api/v1/admin/competitions/{competitionId}/finish
GET  /api/v1/admin/competitions/{competitionId}/permissions
PUT  /api/v1/admin/competitions/{competitionId}/permissions
GET  /api/v1/admin/competitions/{competitionId}/permission-candidates
POST /api/v1/admin/competitions/{competitionId}/owner/transfer
POST /api/v1/admin/competitions/{competitionId}/poster
DELETE /api/v1/admin/competitions/{competitionId}/poster
GET  /api/v1/admin/competitions/{competitionId}/start-validation
POST /api/v1/admin/competitions/{competitionId}/flags/generate-missing
GET  /api/v1/admin/competitions/{competitionId}/events/export
POST /api/v1/admin/competitions/{competitionId}/data-export
GET  /api/v1/admin/competitions/{competitionId}/cheat-incidents
GET  /api/v1/admin/competitions/{competitionId}/cheat-incidents/{gameplayFactId}
POST /api/v1/admin/competitions/{competitionId}/cheat-incidents/{gameplayFactId}/dismiss
POST /api/v1/admin/competitions/{competitionId}/cheat-incidents/{gameplayFactId}/confirm
POST /api/v1/admin/competitions/{competitionId}/cheat-incidents/{gameplayFactId}/correct
```

Lifecycle Endpoint 复用同一 Application state machine，但每个动作仍是独立文件/路由/TypedResults。

普通删除是可恢复的软删除，恢复不会清理任何历史。物理删除是独立操作，不要求先软删除；调用方应先读取
`hard-delete-preview`。预览以稳定引用码和数量报告 Team、CompetitionChallenge、GameplayFact、Runtime、
PatchUpload、Notification、PosterFile 及永久 CompetitionEvent。任意引用都会阻止物理删除并
由 DELETE 返回同一强类型 409 预览；不存在或无权访问返回 404。CompetitionEvent 永久不可删除，存在
任何历史事件的比赛因此永远不能物理删除。

排行榜可见性 PUT 采用 last-write-wins，可立即应用 Frozen/Blackout。需要追溯的切换写入不可变
CompetitionEvent；不在 Competition 保存 revision、scheduled next-run 或排行榜 snapshot。

比赛事件使用永久、不可变的单表事实流。活动比赛的公共参与者只能读取公开事件，已审批且
未封禁队伍还能读取本队事件；其查询必须提供最长 31 天的时间窗。Administrator 与该比赛的
Owner/Manager/Judge/Observer 可以省略时间窗，通过签名 keyset cursor 分页读取完整历史，比赛
软删除归档后仍保留这一只读能力；参赛者不能借归档状态继续读取或扩大可见范围。JSONL 导出仍
要求有界时间窗、最多 50,000 条，且仅 Administrator、Owner、Manager 可用。实时通知只携带事件
Id、类型、级别与发生时间，客户端收到后通过本 GET 重新读取，不通过 SignalR 传输敏感正文。

跨队 Flag 线索以触发 ForeignTeamFlagDetected 的 GameplayFactId 为身份，并结合 CompetitionEvent 处置事件推导，不新增独立业务表。
Observer 可读取不含 Flag 的列表，Judge 可显式读取完整 Flag 并驳回线索；归档后 Administrator、
Owner、Manager、Judge 仍可显式读取完整 Flag。Administrator 读取不写审计；Owner、Manager、Judge
每次成功读取都会写入审计事件。响应禁止缓存。
只有 Administrator、Owner、Manager 可确认并封禁，或纠正误判并解封。
驳回不通知参赛者；确认与纠错只发送不含 Flag 和工作人员原因的全场通用通知。误判纠错允许在
Finished 后执行，只恢复历史计分投影，不重启 Runtime。

比赛队伍封禁申诉同样只投影 `competition_events`，不新增申诉表。队长可针对当前 `TeamBanned`
事件提交一次 16–512 字符的私密申诉，全队可读取状态；受影响队伍只看到 `ManualModeration` 或
`CheatIncident` 来源类别，不读取工作人员理由和证据。Observer/Judge 只读，Administrator、Owner、
Manager 可私密维持封禁或接受申诉。接受申诉和管理员主动纠错都可在 Finished 后解除当前封禁、
发布排行榜失效事件并重投影历史事实；公开事件和通知仅包含比赛、队伍和纠错时间。

权限 snapshot 与候选用户只允许 Competition Owner 或平台 Administrator 读取。候选响应仅含
Id、UserName、Kind、Role 与 EmailVerified，不开放平台用户目录中的 Email、TokenVersion。
权限全量替换和 Owner transfer 采用 last-write-wins。Manager 必须是 Organizer/Administrator，
Judge/Observer 必须完成邮箱验证；资格或角色冲突返回 typed 409。

`start-validation` 是只读 GET，返回当前完整结构化错误数组。`flags/generate-missing` 在 Competition advisory lock 下同步只补 CTF PerTeam/KoH 缺失 Flag，成功返回 200 与 `failures` 数组；每项包含 CompetitionChallengeId、TeamId、稳定 code 和说明。响应不返回 Flag 原文、FlagId、生成/现有数量；空数组表示全部目标已满足。请求不处理 RandomOne 或 AWD Round。

## Admin Team

```text
GET  /api/v1/admin/competitions/{competitionId}/teams
PUT  /api/v1/admin/competitions/{competitionId}/teams/{teamId}/track
POST /api/v1/admin/competitions/{competitionId}/teams/{teamId}/approve
POST /api/v1/admin/competitions/{competitionId}/teams/{teamId}/reject
POST /api/v1/admin/competitions/{competitionId}/teams/{teamId}/ban
POST /api/v1/admin/competitions/{competitionId}/teams/{teamId}/unban
POST /api/v1/admin/competitions/{competitionId}/teams/{teamId}/correct-ban
GET  /api/v1/admin/competitions/{competitionId}/team-ban-appeals
POST /api/v1/admin/competitions/{competitionId}/team-ban-appeals/{appealId}/uphold
POST /api/v1/admin/competitions/{competitionId}/team-ban-appeals/{appealId}/accept

GET  /api/v1/competitions/{competitionId}/team-ban-case
POST /api/v1/competitions/{competitionId}/team-ban-appeals
```

## Global Challenge Bank

```text
GET  /api/v1/admin/challenges
POST /api/v1/admin/challenges
GET  /api/v1/admin/challenges/{challengeId}
PUT  /api/v1/admin/challenges/{challengeId}
DELETE /api/v1/admin/challenges/{challengeId}
POST /api/v1/admin/challenges/{challengeId}/restore
PUT  /api/v1/admin/challenges/{challengeId}/permissions
POST /api/v1/admin/challenges/{challengeId}/owner/transfer
GET  /api/v1/admin/challenges/{challengeId}/attachments
POST /api/v1/admin/challenges/{challengeId}/attachments
POST /api/v1/admin/challenges/{challengeId}/attachments/random-batch
DELETE /api/v1/admin/challenges/{challengeId}/attachments/{attachmentId}
POST /api/v1/admin/challenges/{challengeId}/attachments/{attachmentId}/restore
GET  /api/v1/admin/challenges/{challengeId}/flags
POST /api/v1/admin/challenges/{challengeId}/flags
GET  /api/v1/admin/challenges/{challengeId}/flags/{flagId}
PUT  /api/v1/admin/challenges/{challengeId}/flags/{flagId}
DELETE /api/v1/admin/challenges/{challengeId}/flags/{flagId}
POST /api/v1/admin/challenges/{challengeId}/flags/{flagId}/restore
GET  /api/v1/admin/challenges/{challengeId}/test-runtime
POST /api/v1/admin/challenges/{challengeId}/test-runtime/start
POST /api/v1/admin/challenges/{challengeId}/test-runtime/stop
POST /api/v1/admin/challenges/{challengeId}/test-runtime/reset
POST /api/v1/admin/challenges/{challengeId}/test-runtime/extend
```

题库模板负责人、协作者和平台管理员可为包含 Container/Compose 定义的模板创建一个活动测试
Runtime。测试实例沿用正式 Runner 的镜像拉取、资源限制、安全策略、隔离网络、动态端口和访问地址
展开；CTF/AWDP 使用环境变量注入本次测试 Flag，AWD 在实例启动后执行题目定义中的 Flag 注入命令。
测试 Flag 只由该资源的测试 Runtime 接口返回，不进入普通模板 Flag 列表。停止实例会清理容器与网络，
不会主动删除 Runner 节点的镜像缓存。

更新题目模板技术定义时，如果该模板仍有 Queued、Provisioning、Running 或 Stopping Runtime，接口返回
`409 ActiveRuntimeDefinitionConflict`。标题、题面、方向和可见性等元数据不受此限制。

## Admin Runtime

```text
GET  /api/v1/admin/competitions/{competitionId}/runtimes
GET  /api/v1/admin/competitions/{competitionId}/runtimes/{runtimeInstanceId}
POST /api/v1/admin/competitions/{competitionId}/runtimes/{runtimeInstanceId}/terminate
POST /api/v1/admin/competitions/{competitionId}/runtimes/{runtimeInstanceId}/force-terminate
POST /api/v1/admin/competitions/{competitionId}/teams/{teamId}/challenges/{competitionChallengeId}/runtime/start
POST /api/v1/admin/competitions/{competitionId}/teams/{teamId}/challenges/{competitionChallengeId}/runtime/stop
POST /api/v1/admin/competitions/{competitionId}/teams/{teamId}/challenges/{competitionChallengeId}/runtime/reset
POST /api/v1/admin/competitions/{competitionId}/teams/{teamId}/challenges/{competitionChallengeId}/runtime/extend
POST /api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/runtime/start
POST /api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/runtime/stop
POST /api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/runtime/reset
```

列表按 CreatedAt desc/Id desc keyset，可筛选 CompetitionChallengeId、TeamId、RuntimeKind、Provider、RunnerId、State、ExpiresBefore。Manager 可执行常规动作；Judge/Observer 只读；ProviderReceipt/内部错误只在管理详情返回。精确终止仅使用 RuntimeInstanceId 定位实例，并通过持久化 `Stopping -> Stopped` Provider 清理状态机；同一终态操作依赖状态和幂等资源身份收敛。仅平台 Administrator 可对已停留至少五分钟的 `Provisioning`/`Stopping` 实例执行强制终结，必须提交原因；Runner 先按实例 UUID 标签清理并确认 Provider 资源已不存在，再释放容量、写回 `Stopped` 并派发等待实例，结果记录为工作人员可见的比赛事件。该流程幂等，禁止仅修改数据库状态。带 Team 的动作服务 CTF/AWD，复用玩家 Runtime 状态机，不提供绕过额度或状态的“强制成功”。不带 Team 的三个动作只服务 KoH shared Runtime，不伪造 TeamId；KoH 没有 Extend。

## CompetitionChallenge Management

```text
GET  /api/v1/admin/competitions/{competitionId}/challenges
POST /api/v1/admin/competitions/{competitionId}/challenges
GET  /api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}
PUT  /api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}
GET  /api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/configuration
PUT  /api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/configuration
DELETE /api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}
POST /api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/restore
GET  /api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/hints
POST /api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/hints
GET  /api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/hints/{hintId}
PUT  /api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/hints/{hintId}
DELETE /api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/hints/{hintId}
POST /api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/hints/{hintId}/restore
GET  /api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/flags
GET  /api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/flags/{flagId}
```

CompetitionChallenge create/update/delete/restore 的业务冲突统一返回强类型
`CompetitionChallengeConflictResponse`。create/update 可携带最长 160 字符的 `customTitle`；
空白值会规范化为 null，并回退显示题库模板标题。update body 必须包含完整的 Order、
IsPublished；delete/restore 不携带 expectedRevision。删除状态方向错误返回
`LifecycleStateConflict`。restore 还会在同一事务内重验活动 Challenge
template、Competition mode 与活动唯一约束。稳定 conflict code 为 `ResourceIdConflict`、
`ChallengeOrderConflict`、`ChallengeTemplateConflict`、`LifecycleStateConflict`、
`ChallengeTemplateNotFound` 和 `ChallengeTemplateModeMismatch`。Hint 增删恢复与
CompetitionChallenge 管理写共享 Competition transaction lock，并通过 Outbox 发布排行榜失效事件。

## Admin GameplayFact 与判定

```text
GET  /api/v1/admin/competitions/{competitionId}/gameplay-facts
GET  /api/v1/admin/competitions/{competitionId}/gameplay-facts/adjudication-differences
GET  /api/v1/admin/competitions/{competitionId}/gameplay-facts/{gameplayFactId}
GET  /api/v1/admin/competitions/{competitionId}/gameplay-facts/{gameplayFactId}/patch
POST /api/v1/admin/competitions/{competitionId}/gameplay-facts/{gameplayFactId}/flag-access
POST /api/v1/admin/competitions/{competitionId}/gameplay-facts/queue-evaluation
POST /api/v1/admin/competitions/{competitionId}/gameplay-facts/rejudge
POST /api/v1/admin/competitions/{competitionId}/gameplay-facts/{gameplayFactId}/rejudge
POST /api/v1/admin/competitions/{competitionId}/gameplay-facts/manual-adjustments
```

管理 GameplayFact 列表筛选 competitionChallengeId、teamId、victimTeamId、actorUserId、kind、state、result、failureCode、occurredFrom/To、value 精确匹配、referenceKind/referenceId。顺序 OccurredAt desc/Id desc，limit 1..200，不算 total。

Patch 下载仅允许平台管理员、本场 Owner/Manager/Judge，不允许 Observer 或普通选手使用该管理接口。
校验 FixAttempt 与 PatchUpload、File 的关联，比赛、题目、队伍、提交人必须一致；不按评测状态或结果限制下载。
现有提交详情为有权限的工作人员返回 `patch`（文件名、大小、上传时间、SHA-256、文件 ID）、`patchFailure` 和 `canDownloadPatch`。
下载返回 `application/octet-stream` 附件流，禁止共享缓存，不提供内部令牌、对象路径、永久链接或在线预览。
所有成功授权并打开文件的下载，在下发字节前写入 `GameplayFactPatchDownloaded` 审计，包含管理员；审计保存失败则拒绝下发。
失败使用 ProblemDetails 与稳定 `code`：`Forbidden`、`SubmissionNotFound`、`NotFixSubmission`、`PatchNotFound`、
`InvalidAssociation`、`FileNotFound`、`StorageUnavailable`、`AuditUnavailable`。

queue-evaluation 用于 Pending/首次 PlatformFailed 集合；rejudge 选择已有 Result 的 Flag/Break 或精确 GameplayFact。两者必须指定 CompetitionChallengeId，事务写入对应 durable drain message，Worker 以 500 条 SKIP LOCKED 短事务置 Queued 并写逐项 EvaluateGameplayFact Outbox。不使用 ProcessingVersion，也不建 Batch/Rejudge 实体。

ManualAdjustment 请求为 `{ teamId, competitionChallengeId, delta: int }`，仅 Administrator/Owner/Manager 可用，零值返回 400。delta 以 canonical signed Int32 十进制写入 GameplayFact.Value，事实直接为 Completed/Applied，不增加分数字段。

AWDP v3 计分配置不兼容旧版本，平台不再提供旧版计分影响预览或自动迁移入口。

## Notifications

```text
GET /api/v1/notifications
GET /api/v1/notifications/feed
GET /api/v1/notifications/{notificationId}/thread
```

Feed 按动态受众读取：User、CompetitionCollaborators、CompetitionParticipants、TeamMembers、PlatformAdministrators；Question 根发送者及其后继线程拥有永久读取权。按 SentAt/Id keyset 分页，没有读取、标记已读、删除或计数 Endpoint。

## Platform Administration

以下只允许 `UserRole.Administrator`，Competition Owner/Manager/Judge/Observer 不因比赛权限获得访问：

```text
GET  /api/v1/admin/platform/users
GET  /api/v1/admin/platform/users/{userId}
GET  /api/v1/admin/platform/users/{userId}/deletion-preview
DELETE /api/v1/admin/platform/users/{userId}
GET  /api/v1/admin/platform/configuration
PUT  /api/v1/admin/platform/configuration
POST /api/v1/admin/platform/configuration/logo
GET  /api/v1/admin/platform/information
GET  /api/v1/admin/platform/monitoring
GET  /api/v1/admin/platform/runtimes
POST /api/v1/admin/platform/runtimes/{runtimeInstanceId}/terminate
POST /api/v1/admin/platform/runtimes/{runtimeInstanceId}/force-terminate
GET  /api/v1/admin/platform/logs
GET  /api/v1/admin/platform/logs/export
GET  /api/v1/admin/platform/audit-logs
POST /api/v1/admin/platform/audit-logs/data-export
POST /api/v1/admin/platform/bots
POST /api/v1/admin/platform/bots/{userId}/tokens
PUT  /api/v1/admin/platform/users/{userId}/role
PUT  /api/v1/admin/platform/users/{userId}/account-status
PUT  /api/v1/admin/platform/users/{userId}/email-verification
POST /api/v1/admin/platform/users/{userId}/tokens/invalidate
GET  /api/v1/admin/platform/email-verification/configuration
PUT  /api/v1/admin/platform/email-verification/configuration
PUT  /api/v1/admin/platform/email-verification/password
POST /api/v1/admin/platform/email-verification/test
```

平台 Runtime 清单只投影当前处于 Queued、Provisioning、Running 或 Stopping 状态的 Container 与
Compose 实例，包含比赛 Runtime 与题库模板测试 Runtime，并按 CreatedAt/Id 使用签名 keyset cursor
分页。响应带作用域、赛事或模板题目、来源队伍、Provider、Runner、端口及生命周期时间；平台级停止
与强制终结按 RuntimeInstanceId 操作两种作用域，复用同一 durable cleanup 链路。比赛 Runtime 继续写入
比赛审计事件，题库测试 Runtime 不伪造 CompetitionEvent。

Bot 创建请求只包含 UserName 和固定的 `UserRole.Organizer`，不能创建 User 或 Administrator
Bot，也不接受 Email 或 Password。服务端生成不可用的非空 dummy Email 和 PasswordHash。
Token 签发请求包含正数 ExpiresInSeconds，只接受 Bot 身份，返回一次普通 AccessToken 与
ExpiresAt，不签发 Refresh Token。Role 更新与 token invalidate 原子递增 User.TokenVersion。
NATS JetStream 的 consumer、重投与 dead-letter 运维由受控基础设施工具负责，不通过平台 HTTP API
暴露消息正文、异常详情或任意重投能力。比赛管理者不能操作
DLQ，只能从 Competition/GameplayFact/Runtime 领域 API 重新触发。

平台运行日志使用每日 Redis Stream 分片聚合 API、Worker、Runner、Host 的结构化诊断日志，并通过
管理员专用 SignalR Hub `/hubs/v1/admin/platform-logs` 实时推送。每个 UTC 日分片精确保留最多
50,000 条，保留 14 天后由 Redis TTL 删除，不自动归档。历史查询默认从 Warning 开始，使用
与筛选条件绑定的签名游标，支持按服务、最低级别、UTC 时间、Category、Competition、
RuntimeInstance、Team、User、CompetitionChallenge、GameplayFact 以及有界全文摘要筛选；全文
搜索不区分大小写，覆盖 Category、Event、Message 和 Exception。JSONL 导出沿用相同筛选，
范围最多 14 天且最多 50,000 条。

日志入口与读取投影都会保留结构化作用域；密码、Token、Authorization/Cookie、SMTP 凭据和
通用 Secret 必须在写入 Redis 前脱敏。无论调用者角色，Flag 原文都不得进入普通日志层，
业务代码也不得为调试目的主动打印 Flag。管理审计使用签名 keyset 分页，从用户账号生命周期
审计和工作人员可见的 PostgreSQL `competition_events` 投影，不复制事实、不设置 TTL 或新增
审计业务表。`competition_events` 默认永久、append-only，普通物理删除在存在历史事件时必须拒绝；
比赛结束、队伍解散和用户匿名化注销不会清理事件关系。唯一显式例外是平台 Administrator 的强制
级联删除：仅非 Running/Paused 且无活动或待处理 Runtime 资源的比赛可执行，并要求完整标题、原因和
二次确认。该操作删除比赛作用域事件及引用数据，但在平台管理员审计投影中永久保留独立删除事实。

删除用户前必须读取影响预览。没有任何业务引用时可物理删除；存在比赛、题目、队伍、
提交、计分、通知或生命周期审计引用时，只允许不可逆匿名化并停用。匿名化会清除个人
资料、认证信息和资源协作权限，使所有现有 Token 失效，但保留所有权、队伍成员关系和
历史事实。两种删除方式都要求管理员原因，并记录独立账号生命周期审计，不级联删除业务数据。

平台配置以 revision fence 更新名称、简介和对象存储 Logo；Logo 上传只接受内容签名与 MIME
一致的 PNG/JPEG/WebP。平台信息从运行中的 API 程序集读取版本，并返回已核验的仓库贡献者
ID 与头像，不把 GitHub 可用性变成管理后台的运行时依赖。

## Internal v1

```text
POST /api/internal/v1/awd/check-results
POST /api/internal/v1/awdp/fix-results
GET  /api/internal/v1/awdp/fix-archives/{gameplayFactId}
```

全部使用独立 JWT Scheme、精确 audience/permission 与资源 Claims。Request body 不能包含可覆盖 Claims 的 Competition/Team/GameplayFact Id。JWT 由调度该 durable Job 的可信进程签发，不提供公开“任意换 Token”接口。

- AWD callback permission=`awd:check-result:write`，绑定 RuntimeInstanceId 和独立 Checker GameplayFactId；请求只提交 typed checker status。相同 callback identity 的重投幂等命中同一事实，不覆盖其他 Checker 执行。
- AWDP callback permission=`awdp:fix-result:write`，绑定 GameplayFactId、RuntimeInstanceId 和一次性 Fix attempt identity；结果使用强类型枚举和版本化事件 Payload，相同 callback identity 重投幂等。
- Archive permission=`awdp:fix-archive:read`，只绑定一个 GameplayFactId/PatchUpload；Runner 使用它读取 archive，Checker callback Token 不含此权限。S3 可返回短时预签名地址，LocalFileSystem 可流式返回。

callback 成功返回 200。完全相同的 callback identity 重放返回相同 200；同一幂等身份携带不同规范化 body 返回稳定业务冲突。Token/claim 不符返回
401/403；资源对该 Token 不存在返回 404。过时结果可写结构化日志，但不建审计业务表。

Runner 自身通过 Wolverine 读写，不需要 HTTP callback Endpoint。

## 通用成功契约

- 创建普通资源返回 201+资源 DTO+Location；异步 GameplayFact/Runtime 返回 202+资源 Id+`statusUrl`；同步读取/更新返回 200；无 body 删除返回 204。无 Batch/Rejudge 实体的 queue-evaluation/集合 rejudge 返回 202，状态通过 GameplayFact 列表查询。
- 202 不是判定成功。客户端轮询 statusUrl 或接收 SignalR invalidation，最终仍以 GET 资源为事实源。
- 所有写入必须在返回前完成接入事务与 Outbox commit；commit 失败不得返回资源 Id。
- 可变资源写采用 last-write-wins；状态机动作在事务内校验当前状态，并用业务唯一键与 Inbox/Outbox 保证幂等。
- 每个具体 Endpoint 的 `Results<T...>` 只能声明其真实分支；不能为了省事统一声明 200/400/401/403/404/409/500 全家桶。
