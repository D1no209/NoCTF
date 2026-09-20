# BOT 公开只读接入

`NoCTF.Bot` 是独立产品和普通 API 消费者，不属于 NoCTF 的 Api、Worker 或 Runner 角色。
平台不保存聊天 Provider、master、群、绑定、权限、投递或 Secret；BOT 不引用任何平台项目，
只使用公开 HTTPS API 和比赛 SignalR。Milky、OneBot、UniQsign 及其他聊天框架均位于 BOT
运维边界。

## 1. 身份与平台接口

平台管理员创建 `User` 角色 Bot 身份并签发 Access JWT。该账号不设置密码、不获得 Refresh
Token、不加入队伍，也不进入比赛 Owner、Manager、Judge 或 Observer。JWT 只写入 BOT Secret，
不得进入源码、镜像、普通配置、日志或截图。BOT 启动时调用 `GET /api/v1/auth/me`，只有
`kind=Bot` 且 `role=User` 才继续运行。

BOT 使用的接口均为通用公开契约：

```text
GET /api/v1/auth/me
GET /api/v1/competitions/{competitionId}
GET /api/v1/competitions/{competitionId}/challenges
GET /api/v1/competitions/{competitionId}/leaderboard
GET /api/v1/competitions/{competitionId}/announcements
```

公告接口不是 BOT 专用接口。任何有效 Bearer 身份遵循同一规则：只返回 Public、非 Draft
比赛中面向参赛者且事件可见性为 Public 的公告；StaffOnly 比赛和协作者公告不可读取。

SignalR `/hubs/v1/competitions` 只提供失效提示。BOT 在 750 ms 合并后重新读取 HTTP 事实，
60 秒轮询恢复断线遗漏；平台不向 BOT 投递消息，也不知道群绑定关系。

## 2. Core 与 Provider

- `NoCTF.Bot.Core`：命令、权限、比赛同步、播报、SQLite 与可靠队列，不认识聊天框架。
- `NoCTF.Bot.Providers.Milky`：Milky HTTP/WebSocket、QQ 标识、群角色和发送协议。
- `NoCTF.Bot`：独立 Worker 宿主，一个进程选择一个编译期注册 Provider。

Core 使用字符串 `ProviderId`、`GroupId`、`UserId`、`MessageId`。第三方适配器必须实现身份探测、
群消息流、群角色解析、文本发送和能力声明。开发方式见
[BOT Provider 开发指南](bot-provider-development.md)。

## 3. master、群授权与命令

启动必须配置 `Bot__Provider` 与 `Bot__MasterUserId`。每个群第一次使用前，master 必须在该群
执行 `enable`。首次授权后，master、群主、群管理员和本群自定义 admin 均可 `enable/disable`。
`disable` 保留配置；master 的 `/ctf revoke` 清除授权、绑定、自定义 admin 和待发送消息。

master 管理本群自定义 admin：

```text
/ctf admin add <userId>
/ctf admin remove <userId>
/ctf admins
```

管理员命令：

```text
enable
disable
bind <competitionId>
unbind
/ctf broadcasts on|off
/ctf scoreboard on|off
/ctf blood on|off
/ctf config
```

`/ctf bind` 与 `/ctf unbind` 同样有效；`subscribe/unsubscribe` 仅为兼容别名。只能绑定 Public
且非 Draft 的比赛。普通成员在群启用并绑定后可使用：

```text
/ctf help
/ctf status
/ctf challenges
/ctf rank [1-20]
/ctf team <队伍名>
/ctf link
```

未授权或关闭群除合法 `enable` 和 master `revoke` 外静默忽略。单用户限制为 10 秒 5 条命令，
单群限制为每分钟 30 条。

## 4. 播报与黑榜

`broadcasts` 控制比赛开始/暂停/恢复/结束、新题、题面、提示、公告正文、封禁与纠正；`blood`
控制一二三血；`scoreboard` 只控制 AWDP Break/Fix 引起的公开榜单差异。绑定时三类默认开启。

绑定时建立公告检查点，不补发历史。正文按 Provider 最大长度分段，使用公告 ID 和分段序号去重。
`Hidden`/`Blackout` 不展示分数、名次、血榜、解出数、AWDP 差异或推测数据；BOT 仍推进快照，
解除黑榜后不会补发隐藏期间变化。公开的生命周期、题目、提示、公告和封禁信息继续播报。

## 5. SQLite 与可靠性

SQLite WAL 保存 Provider+群授权、群 admin、比赛绑定、播报开关、公告检查点、比赛快照、入站去重
和持久化出站队列。旧 Milky schema 自动升级，保留绑定和开关，但所有群升级后保持关闭，必须由
master 首次启用。

出站失败按 `2s → 5s → 15s → 30s → 1m → 5m` 重试，随后进入 DeadLetter。进程重启恢复
Pending/Sending；聊天协议没有跨系统事务，因此在 Provider 已接收、SQLite 尚未确认时崩溃仍存在
极小重复投递窗口。

## 6. 安全、部署与拆仓

- NoCTF 地址必须是无凭据、无路径的 HTTPS origin，并禁止跨域重定向。
- Provider API/WebSocket 和签名服务只能位于 BOT 私网。
- BOT 不监听管理端口，不执行用户输入，不读取任意文件或 URL。
- `Bot__AllowedGroupIds` 可作为额外本地白名单。
- 平台 Compose/Kubernetes、环境变量和发布物不得包含 BOT 或 Provider 配置。

开发期三个 BOT 项目暂存于同仓以同步验证公开协议；完成公测后整体迁移到独立仓库、版本和 CI。
部署与验收见 [`integrations/qqbot/README.md`](../integrations/qqbot/README.md)。
