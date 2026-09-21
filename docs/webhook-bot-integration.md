# NoCTF Webhook 与 BOT 对接手册

本文面向需要把 NoCTF 公开赛事事件转发到 QQ、Discord、Telegram、飞书、Slack 或其他聊天平台的
BOT 开发者。BOT 是独立的 Webhook 消费者；NoCTF 不保存群组、聊天平台凭据、消息模板或聊天
Provider 状态。

本文对应 Webhook v1。正式 JSON Schema 由 NoCTF 实例公开在：

```text
GET /schemas/webhooks/competition-events-v1.schema.json
```

平台 API 的完整字段定义位于：

```text
GET /openapi/v1.json
```

## 1. 推荐架构

```text
NoCTF business transaction
  -> PostgreSQL Outbox
  -> NATS JetStream
  -> NoCTF Worker
  -> HTTPS POST + HMAC
  -> BOT WebhookIngress
  -> durable local inbox / queue
  -> event renderer
  -> chat provider adapter
  -> group/channel
```

接收 HTTP 请求和向聊天平台发消息必须解耦。Webhook 入口只完成验签、幂等写入和本地排队，成功后
立即返回 `2xx`。聊天平台限流、断线或封禁由 BOT 自己的出站队列处理，不能让一个缓慢的聊天 API
长期占用 NoCTF Worker 的 HTTP 请求。

建议将 BOT 拆成以下模块：

| 模块 | 责任 |
| --- | --- |
| `WebhookIngress` | 读取原始 Body、验签、检查时间戳、解析 CloudEvent |
| `WebhookSubscriptionStore` | 通过 URL 路径找到当前/上一签名密钥和比赛绑定 |
| `WebhookInbox` | 以 `(source, id)` 幂等保存事件，并在同一事务内创建本地任务 |
| `EventRenderer` | 将强类型事件与公开资源映射为聊天消息 |
| `OutboundQueue` | 保存待发送消息、限流、分片、重试和聊天平台回执 |
| `ChatProvider` | 只处理具体聊天协议，不解析 NoCTF 事件 |

## 2. 在 NoCTF 中创建目标

进入赛事管理页面的 **Webhook** 分区，创建目标并填写：

- 名称：只用于赛事管理页面识别目标；
- Endpoint URL：BOT 暴露的 HTTPS POST 地址；
- 是否立即启用。

平台会返回一次以 `whsec_` 开头的密钥。关闭弹窗后不能再次读取，只能轮换。请把密钥放入 BOT 的
Secret Manager、环境变量或加密配置，不要写入源码、普通日志或聊天消息。

一个 BOT 服务可以接收多个赛事。推荐给每个目标使用不可预测的独立路径，例如：

```text
https://bot.example.com/webhooks/noctf/7ff86bdb1f7448c2
```

BOT 应根据已经可信的 URL 路径选择密钥，随后再验签。不要先相信未验签 Body 中的
`competitionId`，再用它选择密钥。

### 2.1 管理 API

管理页面调用以下 API；自建运维工具也可以使用相同接口：

```text
GET    /api/v1/admin/competitions/{competitionId}/webhooks?offset=0&limit=50&desc=true
POST   /api/v1/admin/competitions/{competitionId}/webhooks
PUT    /api/v1/admin/competitions/{competitionId}/webhooks/{targetId}
DELETE /api/v1/admin/competitions/{competitionId}/webhooks/{targetId}
POST   /api/v1/admin/competitions/{competitionId}/webhooks/{targetId}/rotate-secret
POST   /api/v1/admin/competitions/{competitionId}/webhooks/{targetId}/test-deliveries
GET    /api/v1/admin/competitions/{competitionId}/webhooks/{targetId}/test-deliveries/{deliveryId}
```

创建目标：

```http
POST /api/v1/admin/competitions/01990000-0000-7000-8000-000000000001/webhooks
Authorization: Bearer <administrator-or-competition-manager-token>
Content-Type: application/json

{
  "name": "Official announcement bot",
  "endpointUrl": "https://bot.example.com/webhooks/noctf/7ff86bdb1f7448c2",
  "enabled": true
}
```

成功响应中的 `signingSecret` 只显示一次：

```json
{
  "target": {
    "id": "01990000-0000-7000-8000-000000000002",
    "name": "Official announcement bot",
    "endpointHost": "bot.example.com",
    "endpointUrl": "https://bot.example.com/webhooks/noctf/7ff86bdb1f7448c2",
    "enabled": true,
    "secretConfigured": true
  },
  "signingSecret": "whsec_<base64>"
}
```

权限规则：

- 平台 Administrator、赛事 Owner、Manager 可以修改目标；
- Judge、Observer 只能查看名称、主机与状态，完整 URL 会被隐藏；
- 相同赛事不能重复配置完全相同的 Endpoint URL；
- 目标数量没有产品层上限，列表使用 `offset/limit/total` 分页。

## 3. HTTP 请求

NoCTF 发送：

```http
POST /webhooks/noctf/7ff86bdb1f7448c2 HTTP/1.1
Content-Type: application/cloudevents+json; charset=utf-8
User-Agent: NoCTF-Webhook/1.0
webhook-id: 01990000-0000-7000-8000-000000000010
webhook-timestamp: 1795000000
webhook-signature: v1,<base64-signature> [v1,<previous-secret-signature>]

<CloudEvents JSON bytes>
```

Header 名称不区分大小写。不要重新序列化 JSON 后再验签；空格、换行、转义和属性顺序的任何变化
都会改变签名。

### 3.1 CloudEvents Envelope

```json
{
  "specversion": "1.0",
  "id": "01990000-0000-7000-8000-000000000010",
  "source": "https://ctf.example.com/api/v1/competitions/01990000-0000-7000-8000-000000000001",
  "type": "com.noctf.competition.challenge.published.v1",
  "subject": "competitions/01990000-0000-7000-8000-000000000001",
  "time": "2026-09-21T08:00:00Z",
  "datacontenttype": "application/json",
  "dataschema": "https://ctf.example.com/schemas/webhooks/competition-events-v1.schema.json",
  "data": {
    "capturedAt": "2026-09-21T08:00:01Z",
    "event": {
      "competitionId": "01990000-0000-7000-8000-000000000001",
      "competitionChallengeId": "01990000-0000-7000-8000-000000000020",
      "hintId": null,
      "notificationId": null,
      "teamId": null,
      "gameplayFactId": null,
      "from": null,
      "to": null,
      "award": null,
      "outcome": null
    },
    "resources": {
      "competition": {
        "id": "01990000-0000-7000-8000-000000000001",
        "title": "Example CTF",
        "mode": "Ctf",
        "status": "Running",
        "leaderboardVisibility": "Normal",
        "accessMode": "Public"
      },
      "challenge": {
        "id": "01990000-0000-7000-8000-000000000020",
        "competitionId": "01990000-0000-7000-8000-000000000001",
        "title": "babyinclude",
        "direction": "Web",
        "isPublished": true,
        "hints": []
      },
      "announcement": null,
      "leaderboard": null
    }
  }
}
```

上例只展示 BOT 常用字段。`resources` 中非空资源实际使用对应公开 API 的完整响应结构，接收端必须
容忍未来增加可选字段。

### 3.2 时间与快照

- `time`：业务事件发生时间；
- `data.capturedAt`：本次 HTTP 尝试生成公开资源快照的时间；
- `webhook-timestamp`：本次 HTTP 尝试开始时的 Unix 秒，用于防重放。

同一个事件重试时 `id` 和 `time` 不变，但 `capturedAt` 以及 `resources` 可能更新，因为它们是每次
投递时的当前公开投影。接收端应以第一次成功持久化的 `(source, id)` 为准，后续重复请求直接返回
`2xx`，不要用重试正文覆盖已经处理的事件。

## 4. `data.event` 字段

`event` 始终包含 `competitionId`，其余字段按事件类型填写，不适用时为 `null`：

| 字段 | 含义 |
| --- | --- |
| `competitionChallengeId` | 赛事题目实例 ID |
| `hintId` | 提示 ID |
| `notificationId` | 公告 Notification ID |
| `teamId` | 公开事件关联队伍 ID |
| `gameplayFactId` | Break/Fix 等 GameplayFact ID |
| `from` / `to` | 生命周期变化前后的状态 |
| `award` | `First`、`Second`、`Third` |
| `outcome` | GameplayFact 业务结果，可能为 `null` |

赛事状态值：`Draft`、`Visible`、`Published`、`Running`、`Paused`、`Finished`。

常见 `outcome`：`Correct`、`Wrong`、`Duplicate`、`AttemptsExhausted`、`Rejected`、`Unlocked`、
`Applied`、`ServiceUp`、`ServiceDown`、`Controlled`、`Uncontrolled`。BOT 必须保留未知值的降级路径，
不能因为增加枚举值而拒绝整个请求。

## 5. 事件目录与 BOT 映射

| `type` | 关键字段 | 非空资源 | 建议 BOT 行为 |
| --- | --- | --- | --- |
| `com.noctf.competition.lifecycle.changed.v1` | `from`, `to` | `competition` | 播报开始、暂停、恢复、结束 |
| `com.noctf.competition.challenge.published.v1` | `competitionChallengeId` | `competition`, `challenge` | 播报新题及方向 |
| `com.noctf.competition.challenge.updated.v1` | `competitionChallengeId` | `competition`, `challenge` | 提示题面已更新，不粘贴超长题面 |
| `com.noctf.competition.hint.published.v1` | `competitionChallengeId`, `hintId` | `competition`, `challenge` | 播报新提示及 Cost；只在 `content` 非空时展示正文 |
| `com.noctf.competition.announcement.published.v1` | `notificationId` | `competition`, `announcement` | 展示公告标题和正文 |
| `com.noctf.competition.team.banned.v1` | `teamId` | `competition`, `leaderboard` | 从公开排行榜解析队名；找不到时只显示安全占位 |
| `com.noctf.competition.team.ban.corrected.v1` | `teamId` | `competition`, `leaderboard` | 播报公开纠正，不猜测内部原因 |
| `com.noctf.competition.blood.awarded.v1` | `teamId`, `competitionChallengeId`, `award` | `competition`, `challenge`, `leaderboard` | 播报队伍、题目和一/二/三血 |
| `com.noctf.competition.awdp.break.resolved.v1` | `teamId`, `gameplayFactId`, `outcome` | `competition`, `challenge`, `leaderboard` | 播报公开攻击结果 |
| `com.noctf.competition.awdp.fix.resolved.v1` | `teamId`, `gameplayFactId`, `outcome` | `competition`, `challenge`, `leaderboard` | 播报公开防御结果 |
| `com.noctf.webhook.test.v1` | `competitionId` | `competition` | 只确认联通与验签，不向群聊广播 |

只会推送 `CompetitionEventVisibility.Public` 且列入上表的事件。工作人员或队伍私有事件不会进入
Webhook。

## 6. 公开资源与隐私边界

`resources` 对应以下 OpenAPI 类型：

| JSON 字段 | OpenAPI/生成 SDK 类型 |
| --- | --- |
| `competition` | `NoCtfapiEndpointsCompetitionsCompetitionResponse` |
| `challenge` | `NoCtfapiEndpointsChallengesChallengeResponse` |
| `announcement` | `NoCtfapiEndpointsCompetitionsCompetitionAnnouncementResponse` |
| `leaderboard` | `NoCtfapiEndpointsCompetitionsScoreboardSnapshotResponse` |

BOT 可以直接使用仓库生成的 TypeScript 类型，也可以从实例的 `/openapi/v1.json` 生成自己的客户端。
不要复制后端数据库实体作为 Webhook DTO。

公开投影遵守：

- `Blackout`：排行榜 `dataScope=Hidden`，队伍和成绩集合为空；
- `Frozen`：返回冻结快照及 `dataAsOf`；
- 内部赛道和不可见队伍不会出现在公开排行榜；
- Hint 正文只有公开投影允许时才非空；
- 不返回 Flag、Patch、Token、Provider Receipt、内部失败详情、个人尝试次数或管理角色；
- 公告正文和队伍名属于外部内容，渲染到聊天平台前仍需转义 Mention、Markdown 和富文本控制字符。

BOT 不得根据缺失队伍、隐藏分数或 `null` 字段推测黑榜内容。

## 7. HMAC 验签

### 7.1 算法

1. 获取原始请求 Body 字节；
2. 读取 `webhook-id` 和十进制 `webhook-timestamp`；
3. 拼接：`UTF8(id + "." + timestamp + ".") || rawBody`；
4. 去掉密钥的 `whsec_` 前缀并 Base64 解码；
5. 计算 HMAC-SHA256；
6. 与 `webhook-signature` 中任意一个 `v1,<Base64>` 使用常量时间比较。

建议允许时间偏差 5 分钟。每次重试都会生成新的 `webhook-timestamp` 和签名，所以长时间重试不会
被五分钟窗口误拒绝。

轮换后的 24 小时内 Header 可能包含两个以空格分隔的 `v1` 签名。只要当前或上一密钥有一个验证
成功即可。

### 7.2 测试向量

```text
secret:
whsec_AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=

webhook-id:
01990000-0000-7000-8000-000000000001

webhook-timestamp:
1795000000

raw body:
{"type":"com.noctf.webhook.test.v1","data":{}}

expected signature:
v1,xxjiC24TTSwr7th2v0Y39Ar2BEH2oxWO54U7BJ+XtCs=
```

### 7.3 .NET 10 验签实现

```csharp
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

public static class NoCtfWebhookVerifier
{
    public static bool Verify(
        string messageId,
        string timestampText,
        string signatureHeader,
        ReadOnlySpan<byte> rawBody,
        IEnumerable<string> serializedSecrets,
        DateTimeOffset now,
        TimeSpan tolerance)
    {
        if (!Guid.TryParse(messageId, out _)
            || !long.TryParse(timestampText, NumberStyles.None,
                CultureInfo.InvariantCulture, out var timestamp))
            return false;

        DateTimeOffset attemptedAt;
        try { attemptedAt = DateTimeOffset.FromUnixTimeSeconds(timestamp); }
        catch (ArgumentOutOfRangeException) { return false; }
        if ((now - attemptedAt).Duration() > tolerance)
            return false;

        var supplied = signatureHeader
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Split(',', 2))
            .Where(parts => parts.Length == 2 && parts[0] == "v1")
            .Select(parts => TryBase64(parts[1]))
            .Where(value => value is not null)
            .Select(value => value!)
            .ToArray();
        if (supplied.Length == 0)
            return false;

        var prefix = Encoding.UTF8.GetBytes($"{messageId}.{timestampText}.");
        var signed = new byte[prefix.Length + rawBody.Length];
        prefix.CopyTo(signed, 0);
        rawBody.CopyTo(signed.AsSpan(prefix.Length));
        try
        {
            foreach (var serializedSecret in serializedSecrets)
            {
                if (!serializedSecret.StartsWith("whsec_", StringComparison.Ordinal))
                    continue;
                var key = TryBase64(serializedSecret[6..]);
                if (key is not { Length: >= 24 and <= 64 })
                    continue;
                try
                {
                    var expected = HMACSHA256.HashData(key, signed);
                    if (supplied.Any(value =>
                        value.Length == expected.Length
                        && CryptographicOperations.FixedTimeEquals(value, expected)))
                        return true;
                }
                finally { CryptographicOperations.ZeroMemory(key); }
            }
            return false;
        }
        finally { CryptographicOperations.ZeroMemory(signed); }
    }

    private static byte[]? TryBase64(string value)
    {
        try { return Convert.FromBase64String(value); }
        catch (FormatException) { return null; }
    }
}
```

## 8. .NET 接收 Endpoint 示例

以下示例强调处理顺序。生产实现应使用有上限的流式读取，示例上限为 16 MiB。

```csharp
using System.Text.Json;

app.MapPost("/webhooks/noctf/{subscriptionId}", async (
    string subscriptionId,
    HttpRequest request,
    WebhookSubscriptionStore subscriptions,
    WebhookInbox inbox,
    CancellationToken ct) =>
{
    const int maximumBodyBytes = 16 * 1024 * 1024;
    if (request.ContentLength is > maximumBodyBytes)
        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

    await using var body = new MemoryStream();
    await request.Body.CopyToAsync(body, ct);
    if (body.Length > maximumBodyBytes)
        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
    var rawBody = body.ToArray();

    var subscription = await subscriptions.FindAsync(subscriptionId, ct);
    if (subscription is null)
        return Results.NotFound();

    var messageId = request.Headers["webhook-id"].ToString();
    var timestamp = request.Headers["webhook-timestamp"].ToString();
    var signatures = request.Headers["webhook-signature"].ToString();
    if (!NoCtfWebhookVerifier.Verify(
            messageId,
            timestamp,
            signatures,
            rawBody,
            subscription.ValidSecrets,
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(5)))
        return Results.Unauthorized();

    NoCtfCloudEvent? cloudEvent;
    try
    {
        cloudEvent = JsonSerializer.Deserialize<NoCtfCloudEvent>(rawBody,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
    catch (JsonException)
    {
        return Results.BadRequest();
    }
    if (cloudEvent is null
        || cloudEvent.SpecVersion != "1.0"
        || cloudEvent.Id.ToString() != messageId
        || cloudEvent.Data.Event.CompetitionId != subscription.CompetitionId)
        return Results.BadRequest();

    // TryEnqueueAsync must atomically insert the unique event and its local job.
    // A duplicate returns false but is still acknowledged.
    await inbox.TryEnqueueAsync(
        cloudEvent.Source,
        cloudEvent.Id,
        cloudEvent.Type,
        cloudEvent.Time,
        rawBody,
        ct);
    return Results.NoContent();
});
```

对应的最小 DTO：

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed record NoCtfCloudEvent(
    [property: JsonPropertyName("specversion")] string SpecVersion,
    Guid Id,
    Uri Source,
    string Type,
    string Subject,
    DateTimeOffset Time,
    [property: JsonPropertyName("datacontenttype")] string DataContentType,
    [property: JsonPropertyName("dataschema")] Uri DataSchema,
    NoCtfWebhookData Data);

public sealed record NoCtfWebhookData(
    DateTimeOffset CapturedAt,
    NoCtfEventReference Event,
    NoCtfPublicResources Resources);

public sealed record NoCtfEventReference(
    Guid CompetitionId,
    Guid? CompetitionChallengeId,
    Guid? HintId,
    Guid? NotificationId,
    Guid? TeamId,
    Guid? GameplayFactId,
    string? From,
    string? To,
    string? Award,
    string? Outcome);

public sealed record NoCtfPublicResources(
    JsonElement Competition,
    JsonElement? Challenge,
    JsonElement? Announcement,
    JsonElement? Leaderboard);
```

实际项目可以将四个 `JsonElement` 替换为从 OpenAPI 生成的强类型 DTO。

## 9. 幂等 Inbox

NoCTF 使用至少一次投递。HTTP 响应丢失、Worker 重启或 Ack 超时都可能产生重复请求。BOT 必须拥有
持久幂等表，不能只用进程内 `HashSet`。

SQLite 示例：

```sql
CREATE TABLE webhook_inbox (
    source          TEXT NOT NULL,
    event_id        TEXT NOT NULL,
    event_type      TEXT NOT NULL,
    occurred_at     TEXT NOT NULL,
    raw_body        BLOB NOT NULL,
    state           INTEGER NOT NULL DEFAULT 0,
    attempts        INTEGER NOT NULL DEFAULT 0,
    created_at      TEXT NOT NULL,
    completed_at    TEXT NULL,
    PRIMARY KEY (source, event_id)
);

CREATE INDEX ix_webhook_inbox_pending
    ON webhook_inbox(state, created_at);
```

推荐事务：

1. `INSERT ... ON CONFLICT DO NOTHING`；
2. 新插入时在同一事务创建本地出站任务；
3. 提交事务；
4. 返回 `204`；
5. 独立后台任务生成并发送聊天消息。

幂等记录至少保留超过 NoCTF 的最大重试窗口。推荐保留 7 天；需要完整 BOT 审计时可以永久保留事件
ID、类型和结果，但不建议长期保存完整 Body。

## 10. 返回状态码

| BOT 返回 | NoCTF 行为 | 使用场景 |
| --- | --- | --- |
| 任意 `2xx` | 投递成功，不再重试 | 已完成验签并持久排队；重复事件也返回 `2xx` |
| `408` | 临时失败，重试 | 接收端无法在本次请求内完成持久化 |
| `429` | 临时失败，重试 | BOT Webhook 入口主动限流 |
| `5xx` | 临时失败，重试 | 数据库、队列或服务暂时不可用 |
| `410` | NoCTF 自动停用目标 | 接收方永久取消订阅 |
| `3xx` | 永久失败，不跟随重定向 | 必须在 NoCTF 中更新 URL |
| 其他 `4xx` | 永久失败，进入 NoCTF 错误队列 | 签名、Schema 或配置错误 |

重试退避大致为：`5s`、`30s`、`2m`、`10m`、`30m`、`2h`、`8h`、`24h`，并带随机抖动。

默认 Worker 超时为 10 秒。BOT 应在完成本地持久化后尽快返回，不要等待聊天平台 API。

## 11. 密钥轮换

安全轮换顺序：

1. 在 NoCTF 点击“轮换密钥”；
2. 保存新 `whsec_...`；
3. BOT 同时保留新旧密钥；
4. 用管理页面发送测试事件；
5. 确认新密钥可验证；
6. 24 小时后删除旧密钥。

NoCTF 在 24 小时过渡期内同时使用新旧密钥生成两个 `v1` 签名。不要先删除 BOT 旧密钥再发起
轮换，否则配置更新窗口内可能出现短暂失败。

如果密钥泄漏，应立即轮换，并检查 BOT 的 Inbox 与访问日志。不要把完整签名密钥发送到聊天群中。

## 12. BOT 消息生成建议

### 12.1 不要在 HTTP 线程内格式化并发送

Inbox Worker 读取强类型事件后再构造消息。这样可以独立处理：

- 聊天平台每分钟限额；
- 消息分片与最大长度；
- Markdown/富文本转义；
- `@all`、`@everyone`、链接预览和控制字符过滤；
- 单群订阅开关；
- 聊天平台断线后的重试。

### 12.2 使用资源而不是猜测

- 比赛名使用 `resources.competition.title`；
- 题目名和方向使用 `resources.challenge.title/direction`；
- 公告使用 `resources.announcement.title/body`；
- 队名按 `data.event.teamId` 在 `resources.leaderboard.teams` 查找；
- 排名、分数、黑榜和冻结状态完全服从 `resources.leaderboard`；
- 找不到关联资源时显示“赛事页面已更新”并附链接，不从旧快照猜测。

### 12.3 文案示例

```text
[赛事开始]
Example CTF 已开始
https://ctf.example.com/competitions/<id>
```

```text
[一血]
RedTeam 获得 babyinclude 一血
```

```text
[新公告] 临时维护
比赛环境将在 10 分钟后短暂重启……
```

聊天平台文案属于 BOT 产品，不属于 NoCTF Webhook 协议。接收端应基于 `type` 和结构化资源生成，
不要依赖 NoCTF 服务端日志或枚举的中文显示文本。

## 13. 未知事件与版本演进

事件类型最后的 `.v1` 是该事件 Data Schema 的版本。接收端应：

- 对已知 `.v1` 严格读取必要字段、宽松忽略新增字段；
- 对未知 `type` 记录低级别指标并返回 `2xx`，避免把未来事件永久送入错误队列；
- 不将 `type` 当作任意反射类型名；
- 不因未知枚举值让整个 Webhook 进程退出；
- 升级支持新的主版本后，保留旧版本解析器直到目标平台完成切换。

## 14. 联调步骤

1. BOT 启动一个可公网访问的 HTTPS Endpoint；
2. 在 NoCTF 创建目标但先保持停用；
3. 保存一次性签名密钥；
4. 点击“发送测试”；测试事件即使目标停用也可以发送；
5. BOT 确认 Content-Type、三个 Header、时间戳和 HMAC；
6. NoCTF 页面轮询测试状态，成功时显示 `Succeeded`；
7. 启用目标；
8. 在测试赛事依次触发生命周期、题目、提示、公告和血榜事件；
9. 重放相同请求，确认 BOT 只生成一条本地任务；
10. 临时返回 `429` 或 `503`，确认 NoCTF 重试；
11. 返回 `410`，确认目标自动停用；
12. 轮换密钥并验证新旧双签名窗口。

测试状态只在 Redis 中保留十分钟。页面显示超时不代表正式 Webhook 队列丢失，应同时检查 Worker、
JetStream 和接收端日志。

## 15. 网络与部署

NoCTF 默认：

- 只连接 HTTPS；
- 不跟随重定向；
- DNS 解析后阻止回环、链路本地、RFC1918、共享地址、IPv6 ULA 和云元数据地址；
- 每次连接重新执行允许列表检查，防止 DNS 重绑定；
- 不在日志中记录 URL Query、签名密钥、签名或 Body。

如果 BOT 与 NoCTF 位于同一私网，运维必须显式配置：

```text
Webhooks__PrivateNetworkAllowList__0=bot.internal.example
```

也可以配置精确 IP 或 CIDR。不要为了方便直接放行整个宿主机或容器网络。开发环境若必须使用 HTTP，
还需配置：

```text
Webhooks__InsecureHttpHostAllowList__0=bot.internal.example
```

赛事管理员只能配置 URL，不能修改部署级网络允许列表。

反向代理要求：

- 保留 POST Body 原始字节，不做 JSON 美化或重新编码；
- 不移除 `webhook-*` Header；
- 请求体上限应覆盖最大公开排行榜，建议初始设置至少 16 MiB并结合实际赛事压测；
- 上游超时应短于 NoCTF Worker 超时；
- 只记录事件 ID、类型、结果码和耗时，不记录完整 Body 或 Secret。

## 16. 常见问题

| 现象 | 排查 |
| --- | --- |
| 始终 `401` | 是否使用原始 Body；是否去掉 `whsec_` 后 Base64 解码；是否尝试 Header 中全部签名 |
| 测试显示永久失败 | 查看 BOT 返回的 `3xx/4xx`；NoCTF 不跟随重定向 |
| 测试长期 Pending | 检查 `NOCTF_WEBHOOK` stream、`noctf-webhook` consumer、Redis 与 Worker 日志 |
| 重复群消息 | Inbox 是否用 `(source,id)` 唯一键；是否在本地事务提交前返回了 `2xx` |
| 事件乱序 | 按 `(time,id)` 展示；不要依赖 HTTP 到达顺序 |
| 黑榜期间仍显示旧分数 | BOT 是否错误复用本地旧榜；`dataScope=Hidden` 时必须清空公开分数展示 |
| 找不到队名 | 使用 `teamId` 查本次公开 leaderboard；找不到时不要从内部缓存推测 |
| 轮换后间歇验签失败 | 过渡期是否同时保留新旧密钥；是否遍历了全部 `v1` 签名 |
| Endpoint 收不到请求 | 检查 HTTPS 证书、DNS、公网连通性和部署级私网允许列表 |
| 返回 `410` 后不再推送 | 这是预期行为；目标已自动停用，需要管理员修复 URL 后重新启用 |

## 17. 上线检查表

- [ ] Endpoint 使用有效 HTTPS 证书；
- [ ] Secret 存放在 Secret Manager，不进入源码和日志；
- [ ] 验签使用原始 Body 和常量时间比较；
- [ ] 防重放时间窗口已启用；
- [ ] `(source,id)` 持久幂等键已启用；
- [ ] Inbox 写入与本地任务创建处于同一事务；
- [ ] HTTP 请求不等待聊天平台发送完成；
- [ ] 未知事件返回 `2xx` 并记录指标；
- [ ] Blackout/Frozen 行为已测试；
- [ ] 公告和队名经过 Mention/Markdown 转义；
- [ ] `429`、`5xx`、重复请求、乱序与重启恢复已测试；
- [ ] 密钥轮换与双签名已测试；
- [ ] 测试事件不会向正式群聊广播；
- [ ] 已记录 `webhook-id`、事件类型、处理结果和耗时，但未记录 Secret 或完整 Body。

平台侧实现、运维配置和恢复语义另见 [赛事 Webhook](competition-webhooks.md) 与
[赛事 Webhook 验收记录](competition-webhooks-validation.md)。
