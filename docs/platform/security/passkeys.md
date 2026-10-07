# 通行密钥（Passkey）

通行密钥是人类平台账号的另一种主认证方式，支持 Windows Hello、设备验证器、支持 WebAuthn 的密码管理器和安全密钥。服务器保存公钥，私钥留在验证器中。Bot 与内部服务凭据沿用原有认证。

## 开启站点支持

生产使用 HTTPS，明确配置网站 RP ID 和来源，不根据请求 Host 推断凭据范围：

```text
Passkeys__ServerDomain=noctf.fa1lsnow.com
Passkeys__AllowedOrigins__0=https://noctf.fa1lsnow.com
Passkeys__MaximumCredentials=10
Passkeys__CeremonyLifetimeSeconds=180
```

多个受信任前端来源使用连续的 AllowedOrigins 索引，每项必须是精确 HTTPS Origin。不得添加不受信任的子域或带账号信息、路径、查询参数的 URL。Origin 必须属于 RP ID 或其子域。现有凭据不能迁移到另一 RP ID；更换 RP ID 后应使用密码或 SSO 登录并重新注册。

Docker 示例从 NOCTF_PUBLIC_HOST/NOCTF_PUBLIC_URL 生成这些配置；Kubernetes 的 noctf.invalid 占位值需按实际域名覆盖。开发配置只允许 localhost 的 HTTP Origin，打开 `http://localhost:3000` 或 `http://localhost:3001` 进行设备测试。未配置时隐藏登录入口。现代浏览器需支持 WebAuthn JSON 解析和安全上下文。

## 添加和管理

头像菜单 → 账户安全 → 通行密钥。填写名称、完成必要的本地第二因素验证，再由浏览器请求设备确认。默认最多十个，名称最多 64 字符。成功添加后，现有登录态失效，请重新登录。

没有 MFA 要求的账号在管理前需于五分钟内完成真实主认证；有 MFA 要求的账号管理时需再次验证本地 TOTP 或一次性恢复码。管理员代签令牌不能注册、改名或撤销通行密钥。名称作为用户内容展示。

重命名保留凭据；移除使凭据立即失效并递增 TokenVersion，相关 Access/Refresh JWT 和未完成 MFA 流程不能继续使用。系统记录安全活动并发送可重新派发的安全邮件通知。

## 登录和恢复

登录页选择“使用通行密钥登录”，浏览器选择凭据并验证指纹、面容或 PIN。支持可发现凭据，无需先填写用户名。需要 MFA 的账号仍进入 TOTP／恢复码第二步，通行密钥不会自动替代现有双重验证。

密码和 SSO 登录保持可用。首版不创建只有通行密钥且没有备用主认证方式的账号。丢失设备时通过原有密码或 SSO 登录，完成适用的 MFA 后移除丢失凭据并添加新凭据。验证器和 MFA 恢复码都丢失时，按 [双重验证恢复流程](./mfa.md)处理。

## 协议与部署验收

基于 ASP.NET Core 10 官方 PasskeyHandler 校验，不自写 WebAuthn 签名验证。要求用户存在和用户验证，精确 Origin 校验，拒绝跨源 iframe。使用消费场景的 none attestation，不将 AAGUID 当作已认证的硬件型号。

WebAuthn 临时状态在关系表中加密保存，绑定账号、浏览器、用途、策略与配置版本；具有短期有效和事务性单次消费。协议计数器与挑战消费同一事务提交；同步凭据的双零计数器仍由单次挑战限制防止重放。

上线前检查实际 HTTPS 域名与 RP ID、反向代理 Host 验证、共享密钥配置及设备注册和登录。多节点共用数据库；不使用 Redis 业务锁。注册与断言失败不会形成业务身份。变更后更新 OpenAPI、生成 SDK 与 Wolverine 静态适配器。

参考：[Microsoft 官方接入说明](https://learn.microsoft.com/zh-cn/aspnet/core/security/authentication/passkeys/?view=aspnetcore-10.0)。
