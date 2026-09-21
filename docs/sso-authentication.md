# 通用 SSO 契约

NoCTF 首期 SSO 只支持 OIDC 机密客户端授权码流程（强制 PKCE S256）和 CAS 3.0。
外部身份只用于关联现有 NoCTF 人类账户；不自动注册、不按邮箱或姓名合并账户，也不映射外部角色。

一个账户最多绑定一个外部身份。OIDC 的身份边界为精确匹配的 Issuer 与 Subject；CAS 的身份边界为管理员固定的命名空间与验票主体。身份源 UUID 永久稳定且不复用。

SSO 默认关闭。普通密码登录、NoCTF Access Token 与 Refresh Cookie 仍是平台会话的唯一来源。外部 Access Token、Refresh Token、授权码及 CAS 票据不进入持久业务数据或日志。

协议依据：

- [OpenID Connect Core 1.0](https://openid.net/specs/openid-connect-core-1_0.html)
- [OAuth 2.0 PKCE（RFC 7636）](https://datatracker.ietf.org/doc/html/rfc7636)
- [CAS Protocol Specification](https://apereo.github.io/cas/development/protocol/CAS-Protocol-Specification.html)

固定 API 前缀为 `/api/v1/auth/sso`、`/api/v1/auth/me/sso-binding` 和
`/api/v1/admin/platform/sso`。错误通过 Problem Details 的 `code` 返回稳定的
`SsoFailureCode` 名称。

每个身份源可以配置一个可空的 `IconUrl`。该地址随管理、公开登录和账户绑定接口返回，
客户端在身份源按钮和绑定信息中显示它。生产配置只接受不含用户信息、查询串和片段的公开
HTTPS 绝对地址；图片请求使用 `no-referrer`。未配置图标时仍以身份源名称完成操作。

## 登录、绑定与解绑

SSO 登录只接受已经绑定到 NoCTF 人类账户的外部身份。登录完成页读取流程状态时会得到安全的
`ProviderId`，但不会得到 Subject、外部令牌或身份属性。调用 `complete-login` 后，流程结果先被
原子消费；若身份尚未绑定，接口返回 `IdentityNotLinked`，身份结果随即销毁，刷新页面或重放请求
只会得到流程已失效。

完成页会引导用户注册或使用本地账户登录，并通过受限的站内重定向打开“账户安全”。用户在有效
登录状态下从预选的身份源发起一条全新的 SSO 绑定流程，无需再次输入当前密码。平台不会把前一次未绑定登录的
身份结果续接到账号，也不会据此自动注册或匹配账号。

本人可以在“账户安全”查看 Provider、协议、Subject 和绑定时间，并凭当前有效登录状态绑定或解绑。
解绑会递增 `TokenVersion`，使既有 Access/Refresh 会话失效。平台管理员可以在用户管理中按
SSO Subject 搜索、按 Provider 筛选、查看绑定摘要，并解除正常、封禁或停用人类账户的绑定；
管理员解除绑定同样递增目标用户的 `TokenVersion`，审计记录操作者、目标用户和 Provider，
不记录 Subject。

`POST /api/v1/auth/sso/flows/{flowId}/complete-login` 与
`POST /api/v1/auth/me/sso-binding/flows/{flowId}/complete` 都是无请求正文的 POST。调用方不需要发送
JSON 或 `Content-Type`；浏览器关联 Cookie 和路由中的 Flow ID 是完成操作所需的协议输入。

## 赛道身份门禁

赛道配置可用 `RequiredSsoProviderId` 选择一个身份源作为入场门禁。赛道功能开启时，创建队伍的
用户、通过邀请加入的新成员，以及管理员把队伍改入该赛道时的全体成员，都必须绑定同一个 Provider
UUID。邀请码与 SSO 门禁同时配置时，两项都必须满足。Provider 名称和图标可在公开赛道响应中展示，
Subject 不会公开。

门禁只在入场或改道时校验。新增门禁不会追溯既有队伍，成员入场后解绑也不会移出队伍或改变已有
赛道资格；关闭赛道功能时门禁被忽略。平台管理员、比赛 Owner 和 Manager 可以配置门禁，保存时
引用不存在的 Provider 会返回 `SsoProviderNotFound`，入场资格不足则返回
`TrackSsoIdentityRequired`。

## 部署配置

生产环境继续使用 `EmailVerification__EncryptionKey` 保护 Provider Client Secret
和共享 Data Protection 密钥环；该值必须在所有 API 实例间一致并随备份恢复。
Provider 配置由平台管理页面保存到 PostgreSQL，默认关闭。

所有 Provider URL 默认要求 HTTPS 和公网地址。确需访问受控内网时，通过
`Sso__PrivateNetworkAllowList__0` 等部署项加入精确主机、IP 或 CIDR；开发环境如需
明文 HTTP，还必须通过 `Sso__InsecureHttpHostAllowList__0` 加入精确主机。
`AllowInsecureProviderUrls` 只允许保存 HTTP URL，不能单独绕过外呼检查。

OIDC 回调为
`{PublicBaseUrl}/api/v1/auth/sso/callback/oidc/{ProviderId}`。CAS 的基础回调为
`{PublicBaseUrl}/api/v1/auth/sso/callback/cas/{ProviderId}`，实际 service 会附加一次性
`flow` 参数。反向代理访问日志必须省略查询字符串，避免记录授权码或 CAS 票据。

成都理工大学 CDUT Auth 的逐项配置与验收步骤见
[CDUT Auth 接入 NoCTF](sso-cdut-auth-integration.md)。
