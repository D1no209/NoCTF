# CTF 选手热点流程

2026-10-18 使用 Chrome 打开 Release Host 上的 Nuxt 生产静态构建，登录独立测试账号，依次操作比赛列表、进入比赛、下载附件、查看提交记录、提交正确 Flag、记分板和队伍详情。运行标识 `20261018063836-f4ecfc`；脱敏证据位于对应 `results/`，只记录方法、路径、状态和时间，不记录请求正文、认证信息或查询字符串。

| 操作 | 实际请求 / 连接 | 触发方式 | k6 指标名称 |
|---|---|---|---|
| 浏览比赛、进入比赛 | GET competitions、competition | 入场 | competitions、competition |
| 比赛列表的管理范围 | GET admin/competitions?includeDeleted=false | 登录用户进入比赛列表 | managed_competitions |
| 全局导航权限 | GET theory/questions，limit=1 | 路由变化；检查题库入口是否可见 | theory_navigation |
| 导航未读通知 | GET notifications，Inbox、20 条 | 初次读取、notificationChanged、每 5 分钟补查 | shell_notifications |
| 比赛外层区域 | GET teams/me、teams/me/standing、notifications、events | 入场及相关事件 | my_team、standing、notifications、events |
| 题目导航 | GET challenges、teams/me | 挂载导航；GameplayFactAdjudicated 等事件 | challenges、my_team |
| 题目页记分板数据 | GET leaderboard/challenges、leaderboard | 初始并行；后续按版本只读 leaderboard（自带轮次与列） | scoreboard_catalog、leaderboard |
| 切换题目 | GET challenge、challenge/attachments | 并行；提示包含在 challenge 中 | challenge、attachments |
| 下载附件 | POST attachment/browser-download → GET attachment | 用户点击；短期 HttpOnly Cookie | download_ticket、attachment_download |
| 提交 Flag | POST flag-submissions → GET gameplay-fact | 事件优先刷新；350 ms 起步的后备查询 | submit、fact、evaluation_duration |
| 打开本题提交记录 | GET gameplay-facts，按题目及 FlagAttempt 筛选 | 用户点击 | history |
| 记分板页面 | 目录、排行榜和 leaderboard/trends | 页面挂载及版本事件 | scoreboard_catalog、leaderboard、trends |
| 点击记分板的队伍详情 | 使用现有快照 | 点击时无额外 HTTP | 不虚构接口 |
| 在线会话 | notifications、competitions negotiate + WebSocket | 登录后、进入比赛 | notifications_negotiate、competitions_negotiate、hub_connections |
| 比赛在线状态 | JoinCompetition、HeartbeatCompetition | 加入后每 45 秒心跳 | hub_call_duration |

浏览器中一次正确提交返回 `202`（615.8 ms），随后三次状态查询约 36.1 / 17.9 / 18.3 ms，接着触发队伍、题目列表、播报、通知、排行榜和本队排名读取。该次单用户采样用于确定请求关系，不能用于宣称并发性能。

前端来源：`useCompetitionChallengeNavigator.ts`、`useCompetitionChallengeDetail.ts`、`useFlagSubmit.ts`、`useScoreboardMatrix.ts`、`useCompetitionHub.ts`、`useNotificationHub.ts`、`useCompetitionBroadcastPanel.ts`、`useCompetitionAnnouncementCatchUp.ts`。SignalR 在生产构建中使用 WebSocket；开发模式的长轮询不用于本次负载模型。

前端对事件引起的重复刷新合并；记分板版本未变化时不重新读取，目录与结构按各自 revision 更新。投影 `202` 后每 2 秒重试目录、结构和快照；本队排名单独重试。通知请求使用 Inbox 范围；播报请求限定公开事件类型和时间窗口，并沿用最新事件时间缩小窗口。

可复现的访问节奏为做题 → 记分板 → 本队 → 做题。默认每 60 秒访问下一个页面，做题时每 5 秒切换题目；提交按独立间隔执行并在各 VU 完成入场后分散首次提交。停留时间是配置假设，后续可根据真实访问日志调整。k6 使用 HTTP 请求和真实双 WebSocket 会话模拟服务器负载，浏览器静态资源、Nuxt 链接预取和渲染成本没有计入 API SLO。导航权限探测来自 `features/shell/useDefaultLayout.ts` 对 `route.path` 的监听；已纳入测量。
