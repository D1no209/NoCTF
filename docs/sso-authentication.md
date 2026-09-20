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
