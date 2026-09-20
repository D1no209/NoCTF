# 统一风格规范（沿用你选定的版本3：CTF硬核极简风格）

前缀统一替换：`【NoCTF】` → `▌NoCTF`
引导标记统一：`▷`
时间单位：`N 天 N 小时` → `Nd Nh`，`N小时N分`→`Nh Nm`，`N分`→`Nm`

>
> 占位符保留 `{xxx}` 不变，只修改外层排版与前缀符号。

## 2. 全局错误与限流

### 请求过于频繁

```
▌NoCTF
▷ 请求过于频繁，请稍后再试。
```

### 命令执行发生未分类异常

```
▌NoCTF
▷ 命令暂时无法处理，请稍后再试。
```

### 命令格式无效

先发送：

```
▌NoCTF
▷ 命令格式无效。
```

随后同消息追加帮助文本。

## 3. 群授权与启停

### master 首次启用，无历史绑定

```
▌NoCTF
▷ master 已授权并启用本群 BOT，请由管理员执行 bind <competitionId>。
```

### master 首次启用，恢复旧库保留的绑定

```
▌NoCTF
▷ master 已授权并启用本群 BOT，已恢复比赛绑定：{competitionId}。
```

### 未授权群执行 revoke

```
▌NoCTF
▷ 本群尚未获得 master 授权。
```

### 已关闭群重新启用

```
▌NoCTF
▷ 已重新启用本群 BOT。
```

### 已启用群再次 enable

```
▌NoCTF
▷ 本群 BOT 已处于启用状态。
```

### 非 master 执行 revoke

```
▌NoCTF
▷ 只有 master 可以撤销群授权。
```

### 已关闭群由 master 撤销授权

```
▌NoCTF
▷ 已撤销本群 BOT 授权。
```

### 已启用群由 master 撤销授权

```
▌NoCTF
▷ 已撤销本群 BOT 授权并清除群配置。
```

### 非管理员执行 disable

```
▌NoCTF
▷ 只有 master 或本群管理员可以关闭 BOT。
```

### disable 成功

```
▌NoCTF
▷ 已关闭本群 BOT；首次授权仍保留，管理员可以重新 enable。
```

## 4. 自定义 admin

### 非 master 管理 admin

```
▌NoCTF
▷ 只有 master 可以管理自定义 admin。
```

### admin 列表为空

```
▌NoCTF
▷ 本群没有自定义 admin。
```

### admin 列表非空

```
▌NoCTF · 本群自定义 admin
▷ {userId-1}
▷ {userId-2}
```

### 用户 ID 无效

```
▌NoCTF
▷ 用户 ID 格式无效。
```

### 添加 admin 成功

```
▌NoCTF
▷ 已将 {userId} 添加为本群自定义 admin。
```

### 移除 admin 成功

```
▌NoCTF
▷ 已移除本群自定义 admin：{userId}。
```

### 移除不存在的 admin

```
▌NoCTF
▷ 该用户不是本群自定义 admin。
```

## 5. 比赛绑定与配置

### 非管理员修改配置

```
▌NoCTF
▷ 只有 master、群主、群管理员或本群自定义 admin 可以修改配置。
```

### 平台凭据失效时 bind

```
▌NoCTF
▷ 平台凭据已失效，无法绑定比赛。
```

### 比赛不可公开访问

```
▌NoCTF
▷ 该比赛当前不可公开访问，未创建绑定。
```

### bind 成功

```
▌NoCTF
▷ 已绑定比赛「{competitionTitle}」。
▷ {competitionUrl}
```

### unbind 成功

```
▌NoCTF
▷ 已解除本群比赛绑定。
```

### unbind 时没有绑定

```
▌NoCTF
▷ 本群当前没有绑定比赛。
```

### 配置命令执行时没有绑定

```
▌NoCTF
▷ 本群尚未绑定比赛。
```

### 普通查询时没有绑定

```
▌NoCTF
▷ 本群尚未绑定比赛，请由管理员执行 bind <competitionId>。
```

### 绑定已暂停

```
▌NoCTF
▷ 本群绑定已暂停，请由管理员重新执行 bind。
```

### 配置详情

```
▌NoCTF · 本群比赛配置
▷ 比赛：{competitionId}
▷ 赛事通知：开启|关闭
▷ AWDP 榜单：开启|关闭
▷ 血榜：开启|关闭
▷ 状态：正常|已暂停
```

### 开关修改成功

```
▌NoCTF
▷ {option}已开启。
```

```
▌NoCTF
▷ {option}已关闭。
```

## 6. 帮助与链接

### 帮助

```
▌NoCTF · 可用命令
▷ /ctf status
▷ /ctf challenges
▷ /ctf rank [赛道] [1-20]（默认总榜）
▷ /ctf team <队伍名>
▷ /ctf link
▷ 管理员：enable、disable、bind <competitionId>、unbind、broadcasts on|off、scoreboard on|off、blood on|off、config
▷ master：admin add|remove、admins、revoke
```

### 比赛链接

```
▌NoCTF
▷ {competitionUrl}
```

## 7. 比赛状态查询

### 正常状态（距开始）

```
▌NoCTF · {competitionTitle}
▷ 状态：{status}
▷ Start：{startTime}
▷ End：{endTime}
▷ Remain：{duration}
```

### 正常状态（距结束）

```
▌NoCTF · {competitionTitle}
▷ 状态：{status}
▷ Start：{startTime}
▷ End：{endTime}
▷ Remain：{duration}
```

### 比赛不可访问

```
▌NoCTF
▷ 比赛当前不可访问。
```

### 平台不可用

```
▌NoCTF
▷ 平台暂时不可用，请稍后再试。
```

## 8. 题目列表查询

### 没有可见题目

```
▌NoCTF
▷ 当前没有可见题目。
```

### 正常列表

```
▌NoCTF · 当前可见题目
▷ [{direction}] {challengeTitle}
▷ [{direction}] {challengeTitle}
```

超长追加：

```
…另有 {count} 道题，请前往平台查看。
```

### 比赛或题目不可访问

```
▌NoCTF
▷ 比赛或题目列表当前不可访问。
```

### 题目列表暂时不可用

```
▌NoCTF
▷ 题目列表暂时不可用，请稍后再试。
```

## 9. 排行榜查询

### 黑榜/隐藏

```
▌NoCTF
▷ 排行榜当前不可见。BOT 不会推测实时分数或名次。
```

### 没有可显示队伍

```
▌NoCTF
▷ 排行榜暂时没有可显示的队伍。
```

### 实时榜

```
▌NoCTF · 实时排行榜
▷ #{rank} {teamName}  {score} pts
▷ #{rank} {teamName}  {score} pts
```

不指定赛道时合并全部赛道并重新计算总榜名次。指定赛道时，标题和内容限定为该赛道：

```
▌NoCTF · {trackKey} 赛道实时排行榜
▷ #{rank} {teamName}  {score} pts
▷ #{rank} {teamName}  {score} pts
```

### 冻结榜

```
▌NoCTF · 冻结排行榜（截至 {dataAsOf:yyyy-MM-dd HH:mm:ss zzz}）
▷ #{rank} {teamName}  {score} pts
▷ #{rank} {teamName}  {score} pts
```

指定赛道的冻结榜标题：

```
▌NoCTF · {trackKey} 赛道冻结排行榜（截至 {dataAsOf:yyyy-MM-dd HH:mm:ss zzz}）
▷ #{rank} {teamName}  {score} pts
▷ #{rank} {teamName}  {score} pts
```

>
> 超过12行拆分，第二条不带标题

### 排行榜正在生成

```
▌NoCTF
▷ 排行榜正在生成，请在 {seconds}s 后重试。
```

### 排行榜投影不可用

```
▌NoCTF
▷ 排行榜投影暂时不可用，请稍后再试。
```

## 10. 队伍查询

### 黑榜/隐藏

```
▌NoCTF
▷ 排行榜当前不可见。
```

### 没有匹配队伍

```
▌NoCTF
▷ 公开排行榜中未找到队伍「{query}」。
```

### 唯一匹配

```
▌NoCTF · {teamName}
▷ Rank：#{rank}
▷ Score：{score} pts
```

### 多个匹配

```
▌NoCTF · 多支匹配队伍
▷ #{rank} {teamName}  {score} pts
▷ #{rank} {teamName}  {score} pts
```

### 排行榜正在生成

```
▌NoCTF
▷ 排行榜正在生成，请稍后再试。
```

### 查询失败

```
▌NoCTF
▷ 暂时无法查询该队伍。
```

## 11. 自动播报：比赛生命周期

```
▌NoCTF · {competitionTitle}
▷ 比赛已开始。
```

```
▌NoCTF · {competitionTitle}
▷ 比赛已暂停。
```

```
▌NoCTF · {competitionTitle}
▷ 比赛已恢复。
```

```
▌NoCTF · {competitionTitle}
▷ 比赛已结束。
```

## 12. 自动播报：题目、提示与公告

### 新题，已解析标题

```
▌NoCTF
▷ 新题开放：{challengeTitle}、{challengeTitle}
```

### 新题，未能解析标题

```
▌NoCTF
▷ 平台开放了新题目，请前往比赛页面查看。
```

### 题面更新，已解析标题

```
▌NoCTF
▷ {challengeTitle}、{challengeTitle} 的题目内容已更新，请前往平台查看。
```

### 题面更新，未能解析标题

```
▌NoCTF
▷ 某道题的题目内容已更新，请前往平台查看。
```

### 新提示

```
▌NoCTF
▷ 平台发布了新提示；BOT 不会自动解锁或转发提示正文，请前往比赛页面查看。
```

### 新公告通用提示

```
▌NoCTF
▷ 平台发布了新公告，请前往比赛页面查看。
```

### 新公告正文

```
▌NoCTF · Announcement｜{announcementTitle}
▷ {announcementBody}
▷ {competitionUrl}
```

## 13. 自动播报：封禁与纠正

```
▌NoCTF
▷ 平台公布了一项队伍封禁事件，请以比赛页面公开信息为准。
```

```
▌NoCTF
▷ 平台公布了一项队伍封禁纠正事件，请以比赛页面公开信息为准。
```

## 14. 自动播报：一血、二血、三血

### 已解析队伍和题目

```
▌NoCTF
▷ {teamName} 获得题目「{challengeTitle}」{blood}！
```

>
> {blood} 替换：一血 / 二血 / 三血，移除原颜文字

### 无法关联公开成就

```
▌NoCTF
▷ 产生{blood}，请前往排行榜查看。
```

## 15. 自动播报：AWDP 榜单差异

### 普通更新

```
▌NoCTF · 排行榜更新
▷ #{rank} {teamName}  {score} pts ({delta})
▷ #{rank} {teamName}  {score} pts ({delta})
```

### 冻结榜更新

```
▌NoCTF · 冻结榜更新
▷ #{rank} {teamName}  {score} pts ({delta})
▷ #{rank} {teamName}  {score} pts ({delta})
```

>
> delta=0 时，括号部分省略；黑榜不推送

## 16. 自动播报：订阅和凭据故障

### 比赛、排行榜变为不可访问

```
▌NoCTF
▷ 比赛已不可访问，该群绑定已暂停。请由群管理员重新绑定后恢复。
```

### JWT 首次失效广播

```
▌NoCTF
▷ BOT 凭据已失效，自动播报和查询已暂停，请联系管理员轮换 Token。
```

### 用户命令触发 JWT 失效

```
▌NoCTF
▷ 平台凭据已失效，查询和播报已暂停。
```

---

### 配套修正（原文档【已知样式不一致】条目处理）

1. 移除 `bind` 成功第二行重复前缀问题，合并为单条 ▌NoCTF 块
2. 保留公告两条消息逻辑（通用提示 + 公告正文），样式统一
3. 全文术语统一：**订阅 → 绑定**
4. 术语统一：BOT凭据 / 平台凭据 保留原文，排版统一
5. admin/master/BOT/bind 大小写保持原样，仅美化符号，不修改命令关键字
6. 统一报错句式风格
7. 保留 `pts`，符合CTF习惯
8. 排行榜拆分规则不变，第二条无标题

---
