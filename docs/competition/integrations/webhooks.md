# Webhook 与自动化接入

## 创建赛事 Webhook

入口 `/admin/competitions/比赛ID/webhooks`。目标列表与投递监控分别加载，目标保存成功不代表真实事件已经完成 HTTP 投递。

在赛事管理“Webhook”中创建目标，填写名称和公开 HTTPS URL。Administrator、Owner、Manager 可管理、轮换和发送测试；Judge/Observer 按只读范围查看。

创建或轮换时，`whsec_` 前缀签名密钥只返回一次，立即存入接收服务的 secret 管理。不要贴到群消息、代码或日志。启用时间之后的事件才会发送，停用期间不会补历史。

## 目标的日常操作

| 操作 | 操作后核对 |
| --- | --- |
| 新建 | 名称、HTTPS URL、启用状态与一次性 secret |
| 编辑 | 当前目标 UUID、接收服务主机和保存后的 URL |
| 启停 | EnabledAt 与后续事件范围；停用历史不补发 |
| 轮换 secret | 接收端保存新 key，检查新旧签名过渡 |
| 测试 | 短期测试状态、HTTP 结果与接收服务记录 |
| 删除 | 目标不再继续发送，未发送命令安全失效 |

修改 URL/重新启用会重置生效时间，不作为重新投递全部历史的功能。目标删除与接收方清除历史消息是不同操作。

## 发送测试和查看记录

先配置接收端验签与幂等，再对目标发送一次测试。页面查询测试投递状态直到最终结果；保存测试 ID、HTTP code 和失败摘要。测试记录是短期状态，不代表正式 Outbox 的全部行为。

正式投递列表按当前筛选和分页读取，核对事件 ID/类型、目标、业务发生、队列与投影阶段、HTTP 尝试、次数、下次重试和 DeadLetter。测试成功后用隔离比赛真实事件验证公开资源完整性和可见性，不能只验证某个空 POST 返回 200。

## 投递内容

平台发送 CloudEvents 1.0 Structured JSON，包含公开赛事生命周期、题目/提示/公告、公开作弊纠正、血榜和 AWDP Break/Fix 等事件。资源是投递时的非个性化公开表示，冻结/隐藏规则仍生效。

不发送个人原始 Flag、解锁付费提示正文、Patch、Token、内部 Runtime receipt。Blackout 不产生/投递血榜播报，接收方不能通过 Webhook 绕过隐藏榜。

## 接收方验签

保留原始请求字节，读取三个 Header：

```text
webhook-id
webhook-timestamp
webhook-signature
```

签名输入为 `id + "." + timestamp + "." + rawBody`，去掉 `whsec_` 前缀后 Base64 解码密钥，计算 HMAC-SHA256 并常量时间比较。验证本地时间窗口，按 `webhook-id` 幂等，完成本地事务后再返回 2xx。

轮换后 24 小时内可能同时携带新旧签名。不要先解析再重新序列化 JSON 来验签，空格和属性顺序变化会改变字节。

## 重试与故障

投递至少一次且不保证顺序。2xx 成功；408/429/5xx/网络故障重试，429 遵循 Retry-After；不跟随 3xx；410 停用目标，其他 4xx 可进入 DeadLetter。

在管理页检查每个目标的 HTTP 状态、公开投影等待、首次/最近尝试、重试时间和 DeadLetter 原因。网络超时后对方可能已处理，因此接收方必须幂等。

URL 默认只允许公开 HTTPS，阻止回环、私网、metadata 和 DNS 重绑定。内网例外由部署运维精确配置，不在比赛页面自行放开。

## Bot 与 GitOps

Bot 是普通平台身份，由管理员创建并签发普通 Access JWT。比赛管理自动化通常使用 Organizer Bot，并赋予具体比赛/题库权限；消息 Bot 仅授予需要的普通读取权限。

从实际部署的 `/openapi/v1.json` 和仓库 SDK 读取当前接口。保存稳定资源 UUID，使用服务器并发标记处理冲突；不要按标题每次重建资源或通过直接 SQL 修改业务状态。

Bot 不能密码登录或刷新 Token，长任务需在到期前通过管理员流程更新访问令牌。撤销用 TokenVersion 全量失效，不承诺逐枚 JWT 吊销。

完整事件与验签说明见 [Webhook 契约](https://github.com/D1no209/NoCTF/blob/HEAD/specs/competition-webhooks.md)，GitOps 工作流见 [题库 GitOps](https://github.com/D1no209/NoCTF/blob/HEAD/specs/challenge-repository-gitops.md)。
