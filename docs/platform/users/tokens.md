# Bot 令牌、全量撤销与身份模拟

入口：平台设置 → 用户 → 目标用户详情。操作前核对 UUID、UserKind、AccountStatus 和当前操作者，避免给同名账号签发权限。

## 创建 Bot

点击创建 Bot，填用户名和 User/Organizer/Administrator 角色，保存后在 Bot 筛选中确认。Bot 不通过密码登录、邮箱验证或 Refresh Cookie 取得会话。

创建之后另授予具体比赛/模板权限。例如 GitOps 需要 Organizer 和相应资源管理关系；只有角色没有资源关系不代表能更新所有赛事。

## 签发 Access JWT

1. 打开 Active 目标账号的令牌操作。
2. 填写有效期，单位按页面为秒。
3. 当前允许 60 秒至 1 年，按实际用途选择较短期限。
4. 提交并复制本次返回的令牌，保存到接收程序 secret store。
5. 用 Bearer Header 测所需调用及无权限边界。

令牌使用目标账号普通角色和 TokenVersion，不是额外管理员绕过令牌，不给目标签发 Refresh JWT。记录期满负责人，Bot 无法自行通过 refresh 延期。

人类账号还受当前 MFA 和真实认证上下文约束。要求 MFA 的目标不能由管理员或 Bot 代签完整登录来绕过第二步，代签 token 也不能执行需要真实主认证/本地因素的通行密钥与验证器管理，见 [MFA](../security/mfa.md)和 [Passkey](../security/passkeys.md)。

## 全量撤销

“撤销全部令牌”递增目标 TokenVersion，使该账号既有 Access/Refresh 失效。确认影响全部服务和浏览器，再提交并验证旧令牌被拒绝。

当前没有逐枚 JWT、逐设备吊销列表。`jti` 是标识，不代表存在服务器 blacklist。只想退出当前浏览器时走普通 Logout，它清 Cookie，不实现同样的全量撤销。

## 身份模拟

模拟为目标普通 JWT，原管理员 Access JWT 暂存在 SPA 内存。核对头像/当前身份后操作，以目标实际权限检验可见与读写能力。

不允许嵌套模拟；退出时恢复原 token，刷新页面通常通过管理员 Refresh Cookie 恢复普通管理员会话。不要把模拟 token 当成可长期转交他人的工作凭据。

## 泄露与角色变化

令牌泄露先全量撤销并替换外部 secret，检查必要审计。改角色、密码、解绑 SSO 等也可能递增 TokenVersion，自动化出现 401 后确认身份变化和期限，不反复新建 Bot 掩盖权限问题。
