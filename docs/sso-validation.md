# 通用 SSO 首期验收记录

日期：2026-09-20
范围：OIDC 授权码＋PKCE S256、CAS 3.0、单外部身份绑定、管理端与前端流程。

## 已通过

- Release solution build：0 warnings、0 errors。
- 后端非 Integration：1402 passed、0 failed。
- SSO 专项：22 passed，包括 OIDC 签名/Issuer/Audience/Nonce/PKCE、CAS service/XML/XXE、配置验证和密钥用途隔离。
- 真实 PostgreSQL：4 passed，包括迁移、JSONB/绑定约束、匿名化、Data Protection 跨实例解密、并发 Provider 更新、绑定唯一性与解绑撤销。
- 真实 Redis：1 passed，以 16 个并发回调验证单消费者领取、浏览器关联、完成与消费。
- 前端：594 passed、0 failed；typecheck、架构审计和 Nuxt 生产生成通过。
- OpenAPI 与生成 SDK 连续生成两次哈希一致。
- EF `has-pending-model-changes`：无 drift。
- `git diff --check`：通过。

服务器隔离容器执行了全仓 Integration：310 total、276 passed、22 failed、12 skipped。
其中本轮引入的业务表计数和历史迁移夹具各有一项失败，修正后已在相同 Linux/Docker 环境单独复跑通过；Docker 工作负载清单测试在全并行运行时受其他容器干扰，串行复跑通过。

其余 19 项失败属于该嵌套容器环境缺口：宿主端口被测试进程按 `127.0.0.1` 访问、宿主 bind mount 在测试容器内不存在、镜像代理拒绝未列入白名单的 GreenMail 镜像，以及缺少 Relay/Gateway 专用二进制或状态目录。它们没有经过伪造通过处理。

## 性能边界

SSO 服务只被 SSO 登录、绑定和平台身份认证管理端点解析。普通平台配置、比赛、计分、Runtime 和通知请求没有新增 SSO 数据库读取或身份源外呼。登录页单独按需读取公开 Provider 列表。

本地可控协议测试不能代表外部身份源网络延迟，因此未给出真实 OIDC/CAS P50/P95。生产启用前需在目标身份源上记录 Discovery、Token/UserInfo 或 CAS 验票的阶段耗时；未取得真实样本前不宣称满足跨网络 5% 延迟门槛。

## 尚未验证

- 真实第三方 OIDC 和 CAS 浏览器环境、其 Client 注册与服务白名单。
- 多 API 生产副本滚动期间的真实浏览器回调；跨实例 Redis 与加密 Data Protection 行为已在真实依赖测试覆盖。
- Kubernetes/Libvirt 环境与本功能无直接变更，本轮未重新建立结论。

## 启用步骤

1. 备份 PostgreSQL 和 `EmailVerification__EncryptionKey`，应用迁移后保持 SSO 总开关关闭。
2. 验证本地管理员密码登录、CAP、Refresh、退出和密码找回。
3. 在“平台管理 → 身份认证”填写公开 HTTPS 地址并创建禁用状态的 Provider。
4. OIDC 单独替换 Client Secret；依次执行连接测试和浏览器认证测试。
5. 先启用 Provider 的登录/绑定能力，再启用 SSO 总开关。
6. 验证未绑定提示、绑定、SSO 登录、解绑后 TokenVersion 撤销和平台审计。

## 恢复与回滚

- Redis 丢失只使未完成流程过期；不从 Redis恢复绑定，用户重新发起认证。
- PostgreSQL 中的 User 绑定、Provider 配置和 `data_protection_keys` 是恢复事实源。
- 恢复时必须同时恢复相同的 `EmailVerification__EncryptionKey`，否则 Client Secret 与浏览器关联 Cookie 无法解密。
- 身份源故障时先关闭 Provider 或 SSO 总开关；既有 NoCTF 会话继续按 TokenVersion 与有效期工作。
- 应用回滚保留新增列、JSONB、绑定和 Data Protection 表，不执行破坏性降级。旧版本会忽略这些增量结构。
