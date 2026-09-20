# QQBOT 公开只读接入

`NoCTF.Bot` 是独立的普通 API 消费者，不属于 NoCTF 的 Api、Worker 或 Runner 角色。平台不为
QQBOT 创建专用接口、通知类型、群组绑定或投递协议；BOT 只使用现有公开 HTTP API 与比赛
SignalR Hub，并通过 Lagrange.Milky 收发 QQ 群消息。

## 1. 身份与权限

管理员创建用途为通知转发的 Bot，平台角色必须为 `User`。该账号不设置密码，不获得 Refresh
Token，不加入任何队伍，也不能加入比赛的 Owner、Manager、Judge 或 Observer 列表。

首次联调签发 24 小时 Access JWT；稳定公测可签发 7 至 30 天。不要签发一年期 Token。Token
只写入部署 Secret；不得进入源码、镜像、普通配置文件、日志或截图。`NoCTF.Bot` 启动时调用
现有 `GET /api/v1/auth/me`，只有 `kind=Bot` 且 `role=User` 才会继续运行。

公开比赛允许普通已认证用户加入 `/hubs/v1/competitions` 的 public group。Draft 或 StaffOnly
比赛不会向该身份开放，因此 BOT 无法通过订阅绕过公开可见性。

## 2. 使用的现有接口

BOT 只读取：

```text
GET /api/v1/auth/me
GET /api/v1/competitions/{competitionId}
GET /api/v1/competitions/{competitionId}/challenges
GET /api/v1/competitions/{competitionId}/leaderboard
```

平台仍是可见性裁剪的唯一权威。BOT 不读取题解、咨询、队伍私有信息、Flag、Runtime、管理端点
或工作人员事件，也不自行累计分数。

SignalR 连接 `/hubs/v1/competitions`，连接后对本地订阅的比赛调用
`JoinCompetition(competitionId)`，监听：

- `competitionLifecycleChanged`
- `competitionEventChanged`
- `scoreboardUpdated`
- `gameplayFactStateChanged`（显式忽略，不转发选手提交状态）

SignalR 只作为失效提示。BOT 在 750 ms 合并窗口后重新读取 HTTP 资源，与 SQLite 快照比较后
才生成消息；重连后重新加入全部比赛并重读快照。另有默认 60 秒低频兜底读取，用于恢复断线
期间遗漏的失效提示。

## 3. 排行榜协议

- `200`：使用响应中的 `visibility`、`dataScope`、`dataAsOf` 和 `generatedAt`。
- `202`：遵循 `Retry-After`，不得立即循环请求。
- `401`：全局熔断 HTTP 与 SignalR 读取，向已订阅群提示 Token 失效。
- `404`：比赛或排行榜不可见，暂停本地订阅，等待群管理员重新订阅。
- `503`：保留现有快照并退避，不把投影失败解释为零分。

`Frozen` 只展示冻结快照及时间；`Hidden`/`Blackout` 不显示分数、名次、血榜或解出数，也不
推测实时数据。公告、题目更新等公开非战况通知仍可继续播报。

## 4. QQ 命令

普通成员：

```text
/ctf help
/ctf status
/ctf challenges
/ctf rank
/ctf rank 20
/ctf team <队伍名>
/ctf link
```

群主和群管理员：

```text
/ctf subscribe <competitionId>
/ctf unsubscribe
/ctf broadcasts on|off
/ctf scoreboard on|off
/ctf blood on|off
/ctf config
```

管理员权限来自 Milky `get_group_member_info` 的 `owner/admin` 角色。BOT 不实现 flag、answer、
writeup、runtime 或 admin 等平台写操作。单用户限制为 10 秒 5 条命令，单群限制为每分钟 30
条；`rank` 最多 20 队且最多拆为两条消息。

## 5. 本地可靠性

单实例使用 SQLite WAL，保存：

- 群订阅及三个播报开关；
- 比赛、题目、队伍及 Achievement 快照；
- `(scene, peer_id, message_sequence)` 入站去重；
- 带唯一 `dedupe_key` 的持久化出站队列。

出站失败按 `2s → 5s → 15s → 30s → 1m → 5m` 重试，随后进入 DeadLetter。进程重启会
恢复 Pending/Sending 记录；逻辑事件不会因 SignalR 重投或快照重复读取而重复入队。由于 QQ
协议没有跨系统事务，进程恰好在 QQ 接受消息后、SQLite 完成确认前崩溃时仍存在极小的重复
投递窗口；这是当前 Milky API 下无法伪造“恰好一次”承诺的边界。

## 6. Token 轮换

1. 管理员签发新 Access JWT。
2. 原子替换 `NoCtf__AccessToken` Secret。
3. 重启 `NoCTF.Bot`，确认 `/auth/me` 和 SignalR 正常。
4. 撤销旧 Token。
5. 确认旧 Token 返回 `401`。

轮换不得清空 SQLite；订阅、快照、入站去重和待发送消息必须保留。

## 7. 安全边界

- NoCTF 公网地址强制为无凭据、无路径的 HTTPS origin，HTTP 客户端禁止跨域重定向。
- Milky HTTP/WebSocket 使用私有 Bearer Token，只在内部网络开放。
- `NoCTF.Bot` 不监听管理端口。
- 用户输入只作为命令、UUID 或队伍名称处理，不执行 shell、不读取文件、不发起任意 URL 请求。
- 可用 `Bot__AllowedGroupIds` 再加一层本地群白名单。
- 日志只记录有界错误类型和公开 ID，不记录 JWT、Milky Token 或原始敏感 payload。

## 8. 开发期单仓与后续拆分

当前为了让公开 API/SignalR 契约变更、BOT 客户端实现和自动化测试在同一提交中协同验证，
`NoCTF.Bot` 暂时与平台代码保存在同一仓库并参加解决方案构建。这不代表 BOT、Milky 或
UniQsign 属于平台，也不授权平台读取或管理其配置。

开发期单仓不得突破以下限制：

- 平台运行配置和部署清单中不增加 BOT、Milky、UniQsign 或 QQ 群字段；
- 平台只负责普通 User Bot 身份及 JWT 生命周期；
- BOT 保持零平台项目引用，仅依赖公开网络协议；
- BOT 生产部署、Secret、SQLite、日志和告警全部位于外部 BOT 运维边界。

完成公开契约稳定和公测验收后，BOT 将迁移到独立仓库并使用独立版本、CI 与发布流程；迁移
不得引入平台专用兼容接口，原有 JWT 与公开 API/SignalR 接入方式保持不变。

部署与验收步骤见 [`integrations/qqbot/README.md`](../integrations/qqbot/README.md)。
