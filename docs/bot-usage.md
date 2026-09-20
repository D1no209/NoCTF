# NoCTF BOT 群聊使用手册

本文面向群成员、群管理员和 BOT master。BOT 是独立部署的公开 API 消费者，不属于 NoCTF
平台进程。当前 QQ BOT 为 `FSBOT`（QQ：`2732484870`），master 为 `2078198329`。

## 1. 首次启用与绑定

每个群都必须由 master 首次授权。将 BOT 加入目标群后，依次发送：

```text
enable
bind <competitionId>
/ctf config
/ctf status
```

第一条 `enable` 必须由 master 在目标群发送。授权完成后，`bind` 可由 master、群主、群管理员
或本群自定义 admin 执行。`competitionId` 是比赛 URL 中的 UUID，例如：

```text
https://noctf.example.com/competitions/01a041a5-d3b7-754b-b399-8106afe5be14
                                         └──────── competitionId ────────┘
```

每个群同时只能绑定一场 Public、非 Draft 比赛。重新 `bind` 会替换原绑定并保留本群的播报开关。
`/ctf bind` 与 `/ctf unbind` 同样有效；`subscribe/unsubscribe` 是兼容别名。

## 2. 权限

| 权限 | 来源 | 能力 |
| --- | --- | --- |
| master | BOT 启动配置 | 首次授权、撤销群授权、管理自定义 admin，以及全部 admin 操作 |
| admin | QQ 群主、QQ群管理员，或 master 添加的本群 admin | 启用/关闭、绑定比赛、调整播报开关 |
| member | 普通群成员 | 查询比赛、题目、排名、队伍和比赛链接 |

自定义 admin 只在当前群有效：

```text
/ctf admin add <QQ号>
/ctf admin remove <QQ号>
/ctf admins
```

以上三个命令只有 master 可以执行。

## 3. 群状态管理

```text
enable
disable
/ctf revoke
```

- 未经 master 首次 `enable` 的群不会响应其他命令。
- 首次授权后，master 或 admin 可以 `disable` 和重新 `enable`。
- `disable` 停止查询和播报，但保留比赛绑定、开关和自定义 admin。
- `/ctf revoke` 仅 master 可用，会清除本群授权、比赛绑定、自定义 admin 和待发送消息；恢复时
  必须由 master 再次首次启用。

## 4. 普通成员命令

```text
/ctf help
/ctf status
/ctf challenges
/ctf rank
/ctf rank 20
/ctf team <队伍名>
/ctf link
```

- `rank` 默认返回前 10 名，可指定 1–20。
- `team` 最多返回 5 个名称匹配项。
- 黑榜期间 `rank` 和 `team` 不返回分数、排名或推测数据。

## 5. 管理员命令

```text
bind <competitionId>
unbind
/ctf broadcasts on|off
/ctf scoreboard on|off
/ctf blood on|off
/ctf config
```

首次绑定时三个播报开关默认开启：

- `broadcasts`：比赛开始、暂停、恢复、结束，新题、题面更新、新提示、新公告、队伍封禁与纠正。
- `scoreboard`：仅 AWDP Break/Fix 引起的公开榜单变化。
- `blood`：一血、二血、三血。

绑定时只建立公告检查点，不补发历史公告。公告正文 API 暂不可用时，只跳过公告正文，不影响
其他查询和播报；平台升级后会自动恢复，无需重新绑定。

## 6. 黑榜与冻结榜

- `Blackout/Hidden`：禁止播报或查询分数、名次、血榜、AWDP 榜单差异和任何推测数据。
- BOT 在黑榜期间继续推进内部快照，解除后不会补发隐藏期间的变化。
- 生命周期、题目、提示、公告和公开封禁信息可继续播报。
- `Frozen` 仅展示平台返回的冻结快照和截止时间，不推测实时状态。

## 7. 限流与常见问题

- 单用户：10 秒最多 5 条命令。
- 单群：每分钟最多 30 条命令。
- 命令过快时 BOT 会提示稍后重试。

BOT 没有响应时按顺序检查：

1. 确认 BOT 已加入群且没有被禁言。
2. 确认 master 已在本群执行首次 `enable`。
3. 使用 `/ctf config` 检查绑定和开关。
4. 确认比赛为 Public 且不是 Draft。
5. 联系运维检查：

```bash
systemctl status noctf-bot.service
journalctl -u noctf-bot.service -n 100 --no-pager
docker logs --tail 100 lagrange
```

运维日志不得包含 NoCTF JWT、Milky Token、UniQsign Token 或公告外的敏感消息正文。
