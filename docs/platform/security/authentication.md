# SSO 身份源配置与测试

入口：平台设置 → 认证，`/admin/platform/authentication`。支持 OIDC 机密客户端授权码 + PKCE S256，以及 CAS 3.0。外部身份关联现有 Human 账号，不自动注册、按邮箱合并或映射平台角色。

平台账号的验证器、恢复码和 OIDC MFA 信任见 [双重验证](./mfa.md)。

## 全局配置

开启 SSO 并填写真实 Public Base URL，保存后重新读取。URL 用来生成回调，域名改变需同步外部客户端登记、proxy Origin、Cookie 和允许网络。

## Provider 通用字段

| 字段 | 含义 |
| --- | --- |
| 名称/图标 URL | 登录和绑定按钮展示，图标按安全 HTTPS 规则 |
| 协议 | OIDC 或 CAS，使用对应一组字段 |
| Provider 启用 | 是否可用，不等同全局启用 |
| Allow Login / Binding | 登录和新绑定分别开关 |
| 超时秒 | 协议外呼预算 |
| Allowed Hosts | 当前 Provider 受支持的目标主机，按行填写 |

Provider UUID 是身份源稳定边界，不能用改名或重复创建实现“同一个身份源”。赛道 RequiredSsoProviderId 引用它。

## OIDC 字段

填写精确 Issuer、Discovery URL、Client ID、Scopes（按行，例如 openid/profile）、是否读取 UserInfo 和显示名称 claim。Client Secret 在单独入口替换，旧值不回显。

在外部服务登记精确回调：`https://平台域名/api/v1/auth/sso/callback/oidc/ProviderId`。Issuer/Subject 是外部身份边界，不依据名字或邮箱合并。

## CAS 字段

填写 Identity Namespace、Login URL、ServiceValidate URL、显示名称 attribute。回调基础是 `https://平台域名/api/v1/auth/sso/callback/cas/ProviderId`，实际 service 加一次性 flow 参数。

反代日志省略 query，避免记录 CAS ticket 或 OIDC code。CAS/域名外呼例外由部署 allowlist 与 egress 管理，不能只在表单选择 HTTP 绕过。

## 创建、编辑和 secret

新建 Provider → 填对应协议字段 → 保存得到 UUID → 外部登记回调 → 替换需要的 secret → 分别开启登录/绑定。编辑现有 Provider 重新读取响应，不用同名创建一条替代已有绑定。

## 两种测试

**连接测试**检查端点可达和协议配置，但不能证明用户已能完成认证。

**认证测试**发起一条浏览器真实协议流程，检查外部跳转、回调和测试结果，不等于自动绑定生产用户。用测试账号完成实际绑定再退出 SSO 登录，才能覆盖账号关系。

## 用户如何首次使用

用户先注册/本地登录，从“账户安全”发起新的绑定流程，每账号最多一个外部身份。未绑定的 SSO 登录返回 IdentityNotLinked 后结果已经消费，不能刷新同一 flow 接着绑定。

本人或管理员解绑会递增 TokenVersion。赛道门禁的后续影响按 [赛道规则](../../competition/participants/tracks.md)，不假定解绑追溯移除已获批资格。

## 故障定位

检查全局、Provider、Login/Binding 四个开关；再看 Issuer、回调、Allowed Hosts、secret、TLS/DNS和实际 egress。流程过期重新发起；多个 API 副本共享部署加密与会话所需密钥。
