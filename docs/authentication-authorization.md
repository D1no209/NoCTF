# 认证与授权

## JWT 签名与验证边界

首版使用 HMAC-SHA-256，部署 `JwtSigningKey` 至少 32 随机 bytes；Issuer 是部署配置的稳定绝对 URI。固定 audience 为 `noctf-access-v1`、`noctf-refresh-v1`、`noctf-internal-v1`，三个 ASP.NET Core Authentication Scheme 各只接受自己的 audience/token_type，校验 issuer、signature、exp 且 `ClockSkew=TimeSpan.Zero`。它们可以读取同一签名 Key，但不能共享验证配置或 forward 到彼此。

## 用户类型与密码

公开注册只创建 `UserKind.Human` 和 `UserRole.User`。Human 用户名 3..64、邮箱最大 320，分别以 Normalized 值做大小写不敏感唯一。密码 8..1024 字符，不 Trim、不 Unicode 归一化、不强制字符组合。

使用 ASP.NET Core `IPasswordHasher<User>` / Identity V3：PBKDF2-HMAC-SHA512，IterationCount=210000。成功验证返回 SuccessRehashNeeded 时更新 Hash。密码修改和密码重置都会递增 TokenVersion，使既有 Access/Refresh Token 与会话立即失效。

`UserKind.Bot` 只能由 Administrator 创建。Bot 的 Email、NormalizedEmail 和 PasswordHash
仍为非空：服务端生成 `bot-<user-id-N>@bot.invalid`，并用一次性随机 GUID 生成 dummy
PasswordHash 后立即丢弃明文。Bot 不能调用 Login、ChangePassword、Refresh 或邮箱验证流程；
即使 dummy password 泄露也必须按 UserKind 拒绝。Bot 登录尝试与普通错误凭据使用相同响应。
Bot 与 Human 共用 UserRole、TokenVersion 和资源授权；比赛 GitOps Bot 通常使用 Organizer
角色并被显式加入目标 Competition.ManagerIds。

## Access JWT

- Human 登录/刷新签发的生命周期固定为 15 分钟；通过 JSON 返回，不放 Cookie。
- Administrator 可以为 Bot 签发指定正数有效时长的普通 Access JWT；它使用相同 access audience 和 `token_type=access`，不建立仓库专用认证 Scheme。
- 客户端在内存持有，以 Bearer Header 使用。
- Claims 至少含 sub、role、user_kind、token_version、CSPRNG jti、iat、exp、aud 与 `token_type=access`。
- 每个认证请求比较当前 TokenVersion：Redis 命中直接比较，未命中查 PostgreSQL 回填；Redis 故障回退数据库，不能绕过。
- 角色/密码/全局退出修改 TokenVersion，并通过 Outbox 失效缓存。

Administrator 将 Organizer 降级为 User 前，必须确认其不是任何未删除 Competition/Challenge 的 Owner 或 Manager；否则返回 409 并列出阻塞资源 Id。角色更新和 TokenVersion++ 同事务，旧 Access/Refresh JWT 随后都失效。

## Refresh JWT

- 只向 Human 签发，Bot 永远没有 Refresh JWT。
- 固定 30 天；使用相同签名密钥、独立 refresh audience 与 `token_type=refresh`。
- Claims：sub、CSPRNG jti、iat、exp、token_version、aud 与 `token_type=refresh`。
- Cookie：`__Secure-noctf_refresh`、HttpOnly、Secure、SameSite=Strict、Path=/api/v1/auth，不设置 Domain。浏览器只会把它发送给 v1 认证路由，不会随普通业务请求发送。
- Refresh/Logout 强制精确 Origin 白名单；CORS credentials 不允许通配符。
- 每次 Refresh 发新 Access 与新 Refresh Cookie，但旧 Refresh 在自身到期前仍有效。
- 不存在 refresh_sessions、rotation replay detection、jti blacklist 或设备撤销。
- Logout 只删除 Cookie；“全部退出”或密码修改依靠 TokenVersion。

## 邮箱验证

部署可开关。开启时新 Human 账号在 EmailVerifiedAt 前不能登录。Bot 不参与邮箱验证。验证 Token 只保存 SHA-256、单次使用且有过期时间。Resend 对未知、已验证、冷却中与待验证地址返回同样 Accepted，避免枚举。

## 密码找回

密码找回独立于注册邮箱验证开关，只接受 `Active`、已验证邮箱的 `Human` 账号。请求接口仅接受邮箱，合法邮箱格式始终返回 `202 Accepted`；未知、未验证、Bot、Banned、Disabled、Anonymized、冷却中和超过账号限额的请求都不得泄露不同响应。

重置 Token 使用 32-byte CSPRNG，业务表仅保存 SHA-256。Token 单次使用、默认 30 分钟过期；同一账号新令牌会使此前未消费令牌失效。默认账号冷却 60 秒、每小时最多 3 次，API 另按来源 IP 限制 15 分钟 5 次。消费时使用 PostgreSQL 事务和账号级 advisory lock，原子更新 PasswordHash、TokenVersion、Token 消费/失效状态与通知 Outbox。成功不自动登录，清除当前 Refresh Cookie，并异步发送密码变更通知。

## 平台与比赛授权

平台角色与比赛 Owner/Manager/Judge/Observer 的矩阵见 [产品与领域模型](product-domain.md#权限矩阵)。授权必须在 Endpoint Configure 中声明基础 policy，再由 Application 对 Competition/Challenge 资源关系做二次检查。不可仅相信客户端 CompetitionId。

玩家私有访问必须同时校验：

- 用户属于该 Competition 的唯一 Team；
- Team Approved、未 Ban、未删除；
- Competition/Challenge 当前允许该动作；
- 资源 TeamId 与当前 Team 一致。

资源不可见与不存在统一返回 404，避免泄露其他队 Id。

## 内部 JWT

内部 Scheme 必须验证精确 audience、permission、资源 Claims 与 exp，不能接受用户 Access/Refresh audience。Token 绑定具体 GameplayFactId 或 RuntimeInstanceId/Generation；请求体不能覆盖 Claims 身份。AWDP Token 不携带 GameplayFact ProcessingVersion。

AWD Checker callback 绑定 Runtime identity/generation、checker sequence、runtime processing
version 和最小写权限。只有 sequence/version 与 Runtime 当前值精确匹配时才可写入；同一次执行
可以多次更新状态，后一次覆盖前一次。请求体不能覆盖 Token 中的资源身份或 fence。

## 日志中的敏感内容

选手永远不能通过 API 看到其他队或系统 GameplayFact 的 Value。平台 Administrator 可直接查看 GameplayFact 原始 Flag 且不记录该读取行为；比赛 Owner、Manager、Judge 也可直接查看，但每次成功读取仍追加受保护访问事件。密码、PasswordHash、InvitationToken、Access/Refresh/Internal JWT、邮箱验证 Token、密码重置 Token、FlagDerivationSecret 永远不得记录。
