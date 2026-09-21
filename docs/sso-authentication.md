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
