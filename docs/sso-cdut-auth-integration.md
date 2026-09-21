# CDUT Auth 接入 NoCTF

本文说明如何把 `https://id.example.com` 作为 NoCTF 的 OIDC 身份源。
参数依据 CDUT Auth 当前对接说明整理；NoCTF 负责完整的授权码、PKCE、Nonce、ID Token
和 UserInfo 校验，浏览器不会持有 Client Secret。

## 接入结果与边界

- 协议为 OIDC Authorization Code，并强制 PKCE S256。
- NoCTF 使用机密客户端，CDUT Auth 端选择 `client_secret_basic`。
- NoCTF 用精确匹配的 `iss + sub` 识别外部身份；`preferred_username` 只用于展示。
- SSO 不自动创建 NoCTF 账户，也不按学号、姓名或邮箱合并账户。
- 用户首次使用前必须先用本地账号登录，在“账户安全”中完成绑定。
- 本地密码登录始终保留；启用 SSO 不会替换 NoCTF 自己的 JWT 和 Refresh Cookie。

## 准备信息

准备以下值：

| 名称 | 示例或固定值 |
| --- | --- |
| NoCTF 公开地址 | `https://noctf.example.com` |
| Issuer | `https://id.example.com` |
| Discovery URL | `https://id.example.com/.well-known/openid-configuration` |
| Allowed Host | `id.example.com` |
| Scopes | `openid`、`profile` |
| 显示名称 Claim | `preferred_username` |
| Client ID | 在 CDUT Auth 中为 NoCTF 选择的唯一标识 |
| Client Secret | CDUT Auth 创建机密客户端时一次性显示的密钥 |

生产环境必须使用从浏览器可访问的 HTTPS NoCTF 地址。公开地址包含端口时，端口也必须
写入配置和回调地址；末尾不要加 `/`。

## 配置顺序

回调地址包含 NoCTF 生成的 Provider UUID，因此使用两阶段配置。

### 1. 创建暂不启用的 NoCTF Provider

以平台管理员身份进入“平台设置 → 身份认证”，先保存：

1. “平台公开地址”填写 `https://noctf.example.com`，总开关保持关闭。
2. 新增身份源，名称可填写“成都理工大学统一认证”。
3. 协议选择 `OIDC`，身份源“已启用”保持关闭。
4. “允许登录”和“允许绑定”均可先打开；Provider 未启用时不会出现在用户界面。
5. 超时填写 `10` 秒。
6. “允许访问的主机”只填一行：`id.example.com`。
7. Issuer 填 `https://id.example.com`。
8. Discovery 地址填
   `https://id.example.com/.well-known/openid-configuration`。
9. Client ID 填准备在 CDUT Auth 中登记的值，例如 `noctf-production`。
10. Scopes 分两行填写 `openid` 和 `profile`。
11. 打开“读取 UserInfo”，显示名称 Claim 填 `preferred_username`。
12. 图标可留空，也可填写自行托管的公开 HTTPS 图片地址。地址不能含查询参数或 `#`；
    建议使用方形 PNG/WebP，并避免需要 Cookie 或防盗链的地址。

保存后，身份源列表会显示 Provider UUID。也可以通过管理接口读取：

```http
GET /api/v1/admin/platform/sso
Authorization: Bearer <平台管理员 Access Token>
```

在响应的 `providers` 中按名称找到 `id`。例如 Provider UUID 为
`00000000-0000-0000-0000-000000000001` 时，CDUT Auth 要登记的回调地址是：

```text
https://noctf.example.com/api/v1/auth/sso/callback/oidc/00000000-0000-0000-0000-000000000001
```

回调地址由“平台公开地址”和 Provider UUID 决定。协议、主机、端口、路径及末尾斜杠都必须
逐字一致，不使用通配符，不登记 NoCTF 的 `/auth/sso/complete` 前端页面。

### 2. 在 CDUT Auth 创建客户端

进入 `https://id.example.com/admin`，创建应用：

| CDUT Auth 字段 | 配置 |
| --- | --- |
| Client ID | 与 NoCTF Provider 中完全相同 |
| 应用名称 | 用户可识别的 NoCTF 名称 |
| 认证方式 | 网站 · HTTP Basic（`client_secret_basic`） |
| 回调地址 | 上一步生成的 NoCTF OIDC 回调地址 |
| SPA Origin | 留空；NoCTF 在服务端完成 Discovery、换码和 UserInfo |
| 启用应用 | 开启 |

复制创建结果中的 Client Secret。它只应保存到 NoCTF 管理页面或受控密钥管理系统，不能写入
前端、文档、截图、源代码或日志。

### 3. 写入密钥并完成测试

回到 NoCTF 身份源列表：

1. 点击“替换密钥”，写入 CDUT Auth Client Secret。
2. 点击“测试连接”，确认 Discovery、Issuer、协议端点、JWKS 地址和允许主机检查通过。
3. 点击“测试认证”，在同一浏览器完成学校登录并返回 NoCTF。该测试不创建账户绑定，
   也不会签发 NoCTF 用户会话。
4. 测试成功后编辑身份源，打开“已启用”。
5. 最后打开 SSO 总开关并保存。

管理员认证测试可以在 Provider 和 SSO 总开关关闭时执行，便于先验证再向用户开放。

## 用户绑定和后续登录

现有用户首次接入：

1. 使用 NoCTF 用户名/邮箱和本地密码登录。
2. 打开右上角账户面板，进入“账户安全”。
3. 选择“成都理工大学统一认证”，输入当前 NoCTF 密码并点击“绑定身份”。
4. 在 CDUT Auth/学校页面完成认证，返回 NoCTF 后确认绑定成功。
5. 退出后，登录页会显示带自定义图标的身份源按钮；之后可直接使用该按钮登录。

一个 NoCTF 账户最多绑定一个外部身份，一个 CDUT Auth 身份也只能绑定一个 NoCTF 账户。
直接用尚未绑定的学校身份登录会得到 `IdentityNotLinked`，这是预期行为；用户应先完成上述绑定。

解除绑定需要再次输入 NoCTF 当前密码。成功后 NoCTF 会递增 `TokenVersion`，旧会话失效，
用户仍可使用本地密码登录。

## 验收清单

- 登录页只在 SSO 总开关、Provider 和“允许登录”同时开启后显示该身份源。
- 自定义图标在登录按钮、账户绑定选择和已绑定信息中显示；无图标时文字仍完整可用。
- 管理端“测试连接”和“测试认证”均成功。
- 未绑定的学校身份不能自动创建或匹配 NoCTF 账户。
- 完成绑定后可使用 SSO 登录，并获得原 NoCTF 账户的角色和权限。
- 停用 Provider 后不能发起新登录，本地密码登录仍正常。
- NoCTF 日志中不出现授权码、Access Token、ID Token、Client Secret、`sub` 或完整属性。

## 常见故障

| 现象 | 排查 |
| --- | --- |
| CDUT Auth 返回 `invalid_request` | 核对 Client ID、应用是否启用，以及回调地址是否逐字匹配。 |
| 换码返回 `invalid_client` | CDUT Auth 必须选择 HTTP Basic；确认 NoCTF 中的 Client Secret 未过期或录错。 |
| 返回 `invalid_scope` | Scopes 只配置 `openid` 和 `profile`。 |
| NoCTF 显示 `ProviderUnavailable` | 检查 Discovery URL、证书、DNS、Allowed Host 和服务器到 CDUT Auth 的网络。 |
| NoCTF 显示 `ProviderChanged` | 流程中修改了公开地址或身份信任边界；重新从登录/绑定按钮发起。 |
| 登录成功后显示 `IdentityNotLinked` | 先用本地账号登录，再从账户安全发起绑定。 |
| 图标不显示 | 使用无需鉴权、无需 Cookie、证书有效且不含查询串的公开 HTTPS 图片地址。 |
| Discovery/JWKS 的浏览器 CORS 报错 | NoCTF 正常流程由服务端访问这些端点，不依赖浏览器 CORS；不要把换码逻辑移到 SPA。 |

CDUT Auth 当前对接说明还指出，其学校上游 `qzticket` 使用固定 DES 密钥。OIDC 签名能够证明结果
由 CDUT Auth 签发，但不能消除该上游凭据本身的伪造风险。生产启用前应由身份源维护方确认风险
接受范围，并用真实学校账户完成人工登录验证。
