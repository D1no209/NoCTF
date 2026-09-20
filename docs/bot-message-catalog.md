# NoCTF BOT 回显文案清单

本文按当前代码逐项导出所有会发送到群聊的文本，用于统一调整语气、层级、符号和排版。
本文不包含 systemd、HTTP、SignalR 或 Provider 日志。

## 1. 占位符

| 占位符 | 含义 |
| --- | --- |
| `{competitionTitle}` | 比赛标题，最长 120 字符 |
| `{competitionId}` | 比赛 UUID |
| `{competitionUrl}` | 比赛公开页面 URL |
| `{userId}` | Provider 规范化后的用户 ID；Milky 下为 QQ 号 |
| `{status}` | 草稿、可见、已发布、进行中、已暂停、已结束 |
| `{startTime}` / `{endTime}` | BOT 配置时区下的 `yyyy-MM-dd HH:mm:ss` |
| `{duration}` | `N 天 N 小时`、`N 小时 N 分` 或 `N 分` |
| `{direction}` | 题目方向，最长 32 字符 |
| `{challengeTitle}` | 题目标题，最长 100 字符 |
| `{teamName}` | 队伍名，最长 100 字符 |
| `{rank}` | 排名；缺失时显示 `-` |
| `{score}` | 权威排行榜总分 |
| `{delta}` | 与前一快照的分差，如 `+100`、`-50`；0 时省略 |
| `{seconds}` | `Retry-After` 向上取整后的秒数 |
| `{announcementTitle}` / `{announcementBody}` | 公开公告标题和正文 |

所有正文会移除控制字符，并按 Provider 最大文本长度拆分。当前 Milky 单条上限为 3500 字符。

## 2. 全局错误与限流

### 请求过于频繁

```text
【NoCTF】请求过于频繁，请稍后再试。
```

### 命令执行发生未分类异常

```text
【NoCTF】命令暂时无法处理，请稍后再试。
```

### 命令格式无效

先发送：

```text
【NoCTF】命令格式无效。
```

随后在同一条消息中追加完整帮助文本，见“帮助”。

## 3. 群授权与启停

### master 首次启用，无历史绑定

```text
【NoCTF】master 已授权并启用本群 BOT，请由管理员执行 bind <competitionId>。
```

### master 首次启用，恢复旧库保留的绑定

```text
【NoCTF】master 已授权并启用本群 BOT，已恢复比赛绑定：{competitionId}。
```

### 未授权群执行 revoke

```text
【NoCTF】本群尚未获得 master 授权。
```

### 已关闭群重新启用

```text
【NoCTF】已重新启用本群 BOT。
```

### 已启用群再次 enable

```text
【NoCTF】本群 BOT 已处于启用状态。
```

### 非 master 执行 revoke

```text
【NoCTF】只有 master 可以撤销群授权。
```

### 已关闭群由 master 撤销授权

```text
【NoCTF】已撤销本群 BOT 授权。
```

### 已启用群由 master 撤销授权

```text
【NoCTF】已撤销本群 BOT 授权并清除群配置。
```

### 非管理员执行 disable

```text
【NoCTF】只有 master 或本群管理员可以关闭 BOT。
```

### disable 成功

```text
【NoCTF】已关闭本群 BOT；首次授权仍保留，管理员可以重新 enable。
```

## 4. 自定义 admin

### 非 master 管理 admin

```text
【NoCTF】只有 master 可以管理自定义 admin。
```

### admin 列表为空

```text
【NoCTF】本群没有自定义 admin。
```

### admin 列表非空

```text
【NoCTF】本群自定义 admin
{userId-1}
{userId-2}
```

### 用户 ID 无效

```text
【NoCTF】用户 ID 格式无效。
```

### 添加 admin 成功

```text
【NoCTF】已将 {userId} 添加为本群自定义 admin。
```

### 移除 admin 成功

```text
【NoCTF】已移除本群自定义 admin：{userId}。
```

### 移除不存在的 admin

```text
【NoCTF】该用户不是本群自定义 admin。
```

## 5. 比赛绑定与配置

### 非管理员修改配置

```text
【NoCTF】只有 master、群主、群管理员或本群自定义 admin 可以修改配置。
```

### 平台凭据失效时 bind

```text
【NoCTF】平台凭据已失效，无法绑定比赛。
```

### 比赛不可公开访问

```text
【NoCTF】该比赛当前不可公开访问，未创建绑定。
```

### bind 成功

注意：第二行当前再次包含 `【NoCTF】` 前缀。

```text
【NoCTF】已绑定比赛“{competitionTitle}”。
【NoCTF】{competitionUrl}
```

### unbind 成功

```text
【NoCTF】已解除本群比赛绑定。
```

### unbind 时没有绑定

```text
【NoCTF】本群当前没有绑定比赛。
```

### 配置命令执行时没有绑定

```text
【NoCTF】本群尚未绑定比赛。
```

### 普通查询时没有绑定

```text
【NoCTF】本群尚未绑定比赛，请由管理员执行 bind <competitionId>。
```

### 绑定已暂停

```text
【NoCTF】本群绑定已暂停，请由管理员重新执行 bind。
```

### 配置详情

```text
【NoCTF】本群比赛配置
比赛：{competitionId}
赛事通知：开启|关闭
AWDP 榜单：开启|关闭
血榜：开启|关闭
状态：正常|已暂停
```

### 开关修改成功

根据命令替换 `{option}` 为“赛事通知”“AWDP 榜单播报”“血榜播报”。

```text
【NoCTF】{option}已开启。
```

```text
【NoCTF】{option}已关闭。
```

## 6. 帮助与链接

### 帮助

```text
【NoCTF】可用命令
/ctf status
/ctf challenges
/ctf rank [1-20]
/ctf team <队伍名>
/ctf link
管理员：enable、disable、bind <competitionId>、unbind、broadcasts on|off、scoreboard on|off、blood on|off、config
master：admin add|remove、admins、revoke
```

### 比赛链接

```text
【NoCTF】{competitionUrl}
```

## 7. 比赛状态查询

### 正常状态

`距开始`、`距结束` 只在适用时出现；否则整个末行省略。

```text
【NoCTF】{competitionTitle}
状态：{status}
开始：{startTime}
结束：{endTime}
距开始：{duration}
```

或：

```text
【NoCTF】{competitionTitle}
状态：{status}
开始：{startTime}
结束：{endTime}
距结束：{duration}
```

### 比赛不可访问

```text
【NoCTF】比赛当前不可访问。
```

### 平台不可用

```text
【NoCTF】平台暂时不可用，请稍后再试。
```

## 8. 题目列表查询

### 没有可见题目

```text
【NoCTF】当前没有可见题目。
```

### 正常列表

```text
【NoCTF】当前可见题目
- [{direction}] {challengeTitle}
- [{direction}] {challengeTitle}
```

超过 3500 字符时追加：

```text
…另有 {count} 道题，请前往平台查看。
```

### 比赛或题目不可访问

```text
【NoCTF】比赛或题目列表当前不可访问。
```

### 题目列表暂时不可用

```text
【NoCTF】题目列表暂时不可用，请稍后再试。
```

## 9. 排行榜查询

### 黑榜/隐藏

```text
【NoCTF】排行榜当前不可见。BOT 不会推测实时分数或名次。
```

### 没有可显示队伍

```text
【NoCTF】排行榜暂时没有可显示的队伍。
```

### 实时榜

```text
【NoCTF】实时排行榜
#{rank} {teamName}  {score} pts
#{rank} {teamName}  {score} pts
```

### 冻结榜

```text
【NoCTF】冻结排行榜（截至 {dataAsOf:yyyy-MM-dd HH:mm:ss zzz}）
#{rank} {teamName}  {score} pts
```

超过 12 行时拆成两条消息；第二条没有重复标题。

### 排行榜正在生成

```text
【NoCTF】排行榜正在生成，请在 {seconds} 秒后重试。
```

### 排行榜投影不可用

```text
【NoCTF】排行榜投影暂时不可用，请稍后再试。
```

## 10. 队伍查询

### 黑榜/隐藏

```text
【NoCTF】排行榜当前不可见。
```

### 没有匹配队伍

```text
【NoCTF】公开排行榜中未找到队伍“{query}”。
```

### 唯一匹配

```text
【NoCTF】{teamName}
排名：#{rank}
积分：{score} pts
```

### 多个匹配

```text
【NoCTF】找到多个匹配队伍：
#{rank} {teamName}  {score} pts
#{rank} {teamName}  {score} pts
```

### 排行榜正在生成

```text
【NoCTF】排行榜正在生成，请稍后再试。
```

### 查询失败

```text
【NoCTF】暂时无法查询该队伍。
```

## 11. 自动播报：比赛生命周期

```text
【NoCTF】{competitionTitle}：比赛已开始。
```

```text
【NoCTF】{competitionTitle}：比赛已暂停。
```

```text
【NoCTF】{competitionTitle}：比赛已恢复。
```

```text
【NoCTF】{competitionTitle}：比赛已结束。
```

## 12. 自动播报：题目、提示与公告

### 新题，已解析标题

```text
【NoCTF】新题开放：{challengeTitle}、{challengeTitle}
```

### 新题，未能解析标题

```text
【NoCTF】平台开放了新题目，请前往比赛页面查看。
```

### 题面更新，已解析标题

```text
【NoCTF】{challengeTitle}、{challengeTitle}的题目内容已更新，请前往平台查看。
```

### 题面更新，未能解析标题

```text
【NoCTF】某道题的题目内容已更新，请前往平台查看。
```

### 新提示

```text
【NoCTF】平台发布了新提示；BOT 不会自动解锁或转发提示正文，请前往比赛页面查看。
```

### 新公告通用提示

```text
【NoCTF】平台发布了新公告，请前往比赛页面查看。
```

### 新公告正文

```text
【NoCTF】公告｜{announcementTitle}
{announcementBody}
{competitionUrl}
```

当前实现中，同一个 `AnnouncementPublished` 可能先生成“新公告通用提示”，再由公告 Feed 生成
“标题 + 正文 + 链接”。这是两条独立出站消息，调整样式时建议决定是否保留通用提示。

## 13. 自动播报：封禁与纠正

```text
【NoCTF】平台公布了一项队伍封禁事件，请以比赛页面公开信息为准。
```

```text
【NoCTF】平台公布了一项队伍封禁纠正事件，请以比赛页面公开信息为准。
```

## 14. 自动播报：一血、二血、三血

### 已解析队伍和题目

```text
【NoCTF】{teamName} 获得题目「{challengeTitle}」一血！
```

`一血` 会按事件替换为 `二血` 或 `三血`。

### 无法关联公开成就

```text
【NoCTF】产生一血，请前往排行榜查看。
```

同样可能替换为 `二血` 或 `三血`。

## 15. 自动播报：AWDP 榜单差异

只有 `AwdpBreakResolved` 或 `AwdpFixResolved` 引起公开分数/排名变化时发送，最多显示 8 支队伍。

```text
【NoCTF】排行榜更新
#{rank} {teamName}  {score} pts ({delta})
#{rank} {teamName}  {score} pts ({delta})
```

冻结榜使用：

```text
【NoCTF】冻结榜更新
#{rank} {teamName}  {score} pts ({delta})
```

当 `{delta}=0` 时括号整体省略。黑榜/Hidden 时不发送。

## 16. 自动播报：订阅和凭据故障

### 比赛、排行榜变为不可访问

注意：当前文案仍使用旧术语“订阅”。

```text
【NoCTF】比赛已不可访问，该群订阅已暂停。请由群管理员重新订阅后恢复。
```

### JWT 首次失效时向所有已绑定群广播

```text
【NoCTF】BOT 凭据已失效，自动播报和查询已暂停，请联系管理员轮换 Token。
```

### 用户命令触发 JWT 失效

```text
【NoCTF】平台凭据已失效，查询和播报已暂停。
```

## 17. 静默、不回显的情况

下列输入不会发送任何群消息：

- 不是 `/ctf ...`，也不是精确短命令 `enable/disable/bind/unbind`；
- 文本超过 512 字符；
- 群不在可选 `AllowedGroupIds` 白名单；
- 同一 Provider 消息 ID 已处理；
- 群尚未被 master 首次授权，且发送者不是执行 `enable` 的 master；
- 群处于 disabled，且输入不是有权执行的 `enable` 或 master `revoke`；
- BOT 自己发送的消息；
- Milky 私聊消息或没有文本段的消息。

## 18. 已知样式不一致

此节仅记录现状，不在本次导出中修改：

1. `bind` 成功的第二行重复 `【NoCTF】` 前缀。
2. 公告可能同时发送通用提示和正文消息。
3. “绑定”与旧文案“订阅/重新订阅”混用。
4. `BOT 凭据` 与 `平台凭据` 混用。
5. `admin/master/BOT/bind` 中英文混排，没有统一大小写和中文名称。
6. 有些失败说明下一步操作，有些只说明“暂时不可用”。
7. 实时榜、队伍查询和 AWDP 差异使用英文 `pts`，配置和状态使用中文。
8. 排行榜拆成第二条消息时不重复标题，单独查看时上下文不完整。
