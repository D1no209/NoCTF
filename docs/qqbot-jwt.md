# QQBOT JWT 接入

QQBOT 是 NoCTF 的普通 API 消费者。平台不会创建 QQBOT 专用通知类型、通知表、群组绑定
或投递协议。机器人只读取自身的增量通知流，并使用既有排行榜接口响应 `/rank`。

## 1. 身份与最小权限

在管理后台创建“通知转发 Bot”。该身份的 `UserKind` 为 `Bot`，平台角色为 `User`，不能
密码登录、不能刷新令牌，也不能获得平台管理员权限。GitOps Bot 是另一种用途，使用
`Organizer` 角色，不应复用为 QQ 通知转发身份。

## 2. 创建身份与签发 JWT

管理员在“用户”页面选择“创建 Bot”，用途选择“通知转发 Bot”，然后签发有界有效期的
Access JWT。令牌只展示一次，关闭对话框后前端清除该值，也不会写入 localStorage。

API 等价操作：

```http
POST /api/v1/admin/platform/bots
Authorization: Bearer <administrator-access-token>
Content-Type: application/json

{"userName":"competition-qq-relay","role":"User"}
```

```http
POST /api/v1/admin/platform/bots/{userId}/tokens
Authorization: Bearer <administrator-access-token>
Content-Type: application/json

{"expiresInSeconds":2592000}
```

响应中的 `accessToken` 应直接进入部署平台的 Secret 管理，不要写入仓库、日志、截图或
普通配置文件。

## 3. 加入比赛观察员

在比赛权限面板把通知转发 Bot 加入 `ObserverIds`。Bot 观察员不需要伪造邮箱验证；人工
Judge/Observer 仍必须完成邮箱验证。观察员身份使机器人进入该比赛通知受众，并允许它
读取该私有比赛的排行榜，但不会授予任意私有比赛读取权限。

## 4. 运行时配置

建议 QQBOT 运行时只从 Secret/环境变量读取以下配置：

```text
NOCTF_API_BASE_URL=https://noctf.example.com/api/v1
NOCTF_ACCESS_TOKEN=<secret>
NOCTF_COMPETITION_ID=<uuid>
NOCTF_NOTIFICATION_CURSOR_PATH=/data/noctf-notification.cursor
NOCTF_NOTIFICATION_DEDUPE_PATH=/data/noctf-notification-ids.sqlite3
NOCTF_POLL_INTERVAL_SECONDS=2
```

`NOCTF_API_BASE_URL` 必须由配置提供，不能在机器人代码中写死。生产环境只使用有效 HTTPS
证书，不跟随跨域重定向转发 Authorization 头。

## 5. 初始化安全检查点

第一次调用不携带 cursor：

```http
GET /api/v1/notifications/feed?limit=100
Authorization: Bearer <bot-access-token>
```

平台返回空 `items` 和非空 `nextCursor`。这是“从现在开始”的安全检查点，不会在机器人
首次上线时重放整个历史。先持久化该 cursor，再进入正常轮询。

## 6. 增量消费与提交顺序

后续请求携带上次成功保存的 cursor：

```http
GET /api/v1/notifications/feed?limit=100&cursor=<url-encoded-cursor>
Authorization: Bearer <bot-access-token>
```

事件按 `(createdAt, id)` 升序返回。每批应按以下顺序处理：

1. 按通知 `id` 查询本地去重记录。
2. 未处理的事件转换为 QQ 消息并发送。
3. 整批发送完成后，原子持久化通知 ID 与响应中的 `nextCursor`。
4. 任一事件失败时不要前移 cursor，重试整批并依赖通知 ID 去重。

这形成 at-least-once 消费语义。cursor 是签名且绑定 Bot 身份的 opaque 字符串，禁止解析、
拼接或复用其他账号的 cursor。

## 7. 比赛广播事件

`kind` 当前新增四类比赛广播：

- `BloodAwarded`：包含比赛、比赛题目、题目标题、`bloodRank`（1/2/3）、队伍和发生时间。
- `ChallengePublished`：包含题目标题、方向和发布时间，不含 Flag、运行定义或附件密钥。
- `HintPublished`：包含 Hint ID、题目标题、花费与发布时间，不含提示正文。
- `TeamBanned`：包含队伍 ID、名称与封禁时间，不含原始封禁原因。

客户端必须忽略未知字段，并对未知 `kind` 做可观测的安全降级，不能因此停止推进 cursor。
已改期或删除的定时提示由平台版本栅栏淘汰；机器人不会收到尚未到发布时间的提示。

## 8. `/rank` 命令

`/rank` 只调用现有接口：

```http
GET /api/v1/competitions/{competitionId}/leaderboard
Authorization: Bearer <bot-access-token>
```

处理结果：

- `200` 且 `dataScope=Live/Frozen`：展示 `entries`、`bloods` 等现有强类型排行榜数据；
  `Frozen` 必须同时标注 `dataAsOf`，不能伪装成实时榜单。
- `200` 且 `visibility=Blackout`、`dataScope=Hidden`：平台返回空的排行榜集合。BOT 必须进入
  黑灯状态，停止 `/rank` 结果、定时排名、分数、解出数、血榜及其他常规战况播报，不能把
  空集合解释为“所有队伍零分”。
- `202`：读取 `Retry-After`，等待后重试同一 URL。
- `404`：比赛不存在，或该 Bot 不是私有比赛的显式 Observer。
- `503`：排行榜投影暂不可用，向群内返回简短故障提示并稍后重试。

不要从通知事件自行累计分数，通知不是排行榜事实来源。

黑灯只限制常规战况读取。BOT 必须继续推进通知 feed 的 cursor，并照常转发平台通知与比赛
通告；不得因为 `dataScope=Hidden` 暂停通知消费。比赛结束后平台自动恢复最终实时榜单，BOT
在下一次排行榜读取看到 `dataScope=Live` 后恢复常规播报。

## 9. 撤销与轮换

JWT 到期前由管理员重新签发并替换 Secret。需要立即撤销时，在用户页面执行“使令牌失效”；
平台递增该 Bot 的 `TokenVersion`，所有旧 JWT 随即失效。轮换 Access JWT 不应清空 cursor
或通知 ID 去重库。

## 10. 故障排查与安全边界

- `401`：检查 JWT 是否过期、是否已被 TokenVersion 撤销，以及 API 基础 URL 是否正确。
- `400 cursor_invalid`：cursor 损坏、来自其他身份或来自其他端点；保留告警后由管理员决定
  是否重新建立“从现在开始”的检查点。
- 空 feed：确认 Bot 已加入目标比赛 Observer，且事件发生在初始检查点之后。
- `/rank` 返回 `404`：确认请求中的 Competition ID 与 Observer 授权属于同一比赛。
- 永远不要记录 Authorization 头、完整 JWT、通知 payload 原文或 Secret 环境变量。
- Bot 不需要也不应拥有 Administrator 角色；不要调用管理写接口来实现广播或排行榜。
