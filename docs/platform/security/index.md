# 邮件、验证与身份安全设置

平台安全设置分为两个工作区：`/admin/platform/email` 保存邮件和人机验证，`/admin/platform/authentication` 保存 SSO。每个分区各自保存，secret 用独立替换入口。

| 工作 | 操作说明 |
| --- | --- |
| 配置 SMTP、验证期限、重发与密码找回 | [邮件与找回](./email.md) |
| None/Cap/Turnstile、Runtime 与 Evaluation 独立策略 | [人机验证](./human-verification.md) |
| OIDC/CAS、连接测试与真实认证测试 | [SSO 配置](./authentication.md) |
| 角色、状态与邮箱验证标记 | [用户管理](../users/accounts.md) |
| Bot JWT、撤销与模拟 | [访问令牌](../users/tokens.md) |

先在隔离账号完成发送、验证、找回和绑定测试，再启用会影响真实用户的新策略。公网域名、代理、egress、Backend URL 和加密密钥由部署运维准备，见 [部署配置](../../installation/configuration.md)。

当前数据库保存的 Provider/配置是事实源，环境变量回退用于未保存时的初始化，不通过改部署变量假定覆盖所有现行管理员设置。所有 API 副本和相关角色共享稳定保护密钥，升级不自动随机换 key。
