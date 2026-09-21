# 通用 SSO 首期验收记录

日期：2026-09-21
范围：OIDC 授权码＋PKCE S256、CAS 3.0、单外部身份绑定、未绑定登录引导、本人及管理员绑定管理、赛道身份门禁。

## 已通过

- Release solution build：0 warnings、0 errors。
- 后端非 Integration：1410 passed、0 failed。路由冻结计数为 218，其中管理员路由 118。
- 全仓 Integration：315 total、301 passed、0 failed、14 skipped；并发上限 16，使用真实 PostgreSQL、Redis、NATS JetStream 和 Docker。
- SSO 流程使用真实 Redis 验证 16 个并发回调只有一个处理者；未绑定登录首次返回 `IdentityNotLinked` 后 Flow 被删除，读取和重放返回失效。
- 真实 PostgreSQL 覆盖 Provider 并发更新、绑定唯一性、本人和管理员解绑、Subject 搜索、Provider 筛选、`TokenVersion` 撤销，以及赛道建队/加入/改道门禁与既有队伍豁免。
- Webhook fan-out 加入后的迁移清单、历史迁移夹具和事务 Outbox 数量已按当前契约恢复，并在真实依赖下逐项及全量通过。
- 前端：602 passed、0 failed；typecheck、架构审计和 Nuxt 生产生成通过。
- OpenAPI 与生成 SDK 连续生成两次聚合 SHA-256 均为 `0DC2D6067A98442AF6778F3B6DD0094D1ED51FED22D78F5E654468B3B706CB06`，工作区无生成差异。
- EF `has-pending-model-changes`：无 drift。
- `git diff --check`：通过。

首次全并行运行出现 Docker 启动超时和本机 Desktop 凭据助手不可用于 Host 镜像；预算用例串行通过，拉取用例使用无凭据助手的隔离 Docker 配置后通过。固定 SHA-256 的 FRP 0.68.0 归档、Linux relay 与 SSH 原型均按仓库复现文档构建，原型测试 3/3 通过。最终全量运行使用同一组显式依赖，不以禁用断言消除失败。

## 性能边界

SSO 服务只被 SSO 登录、绑定、平台身份认证管理，以及实际配置了 SSO 门禁的赛道读取。没有门禁或赛道关闭时，公开赛道查询不读取 Provider 或用户绑定。普通计分、Runtime 和通知请求没有新增身份源外呼。登录页单独按需读取公开 Provider 列表。

本地可控协议测试不能代表外部身份源网络延迟，因此未给出真实 OIDC/CAS P50/P95。生产启用前需在目标身份源上记录 Discovery、Token/UserInfo 或 CAS 验票的阶段耗时；未取得真实样本前不宣称满足跨网络 5% 延迟门槛。

## 尚未验证

- 真实第三方 OIDC 和 CAS 浏览器环境、其 Client 注册与服务白名单。
- 多 API 生产副本滚动期间的真实浏览器回调；跨实例 Redis 与加密 Data Protection 行为已在真实依赖测试覆盖。
- 未配置 `NOCTF_GITOPS_TEMPLATE_ROOT` 的真实 GitOps 模板验收。
- 需要专用 Linux 故障镜像、宿主只读挂载或 bind 权限语义的 8 项容量/文件系统测试。
- 真实 Kubernetes 的 3 项指标、调度与 Checker 测试，以及未配置磁盘的 Libvirt 测试。

## 启用步骤

1. 备份 PostgreSQL 和 `EmailVerification__EncryptionKey`，应用迁移后保持 SSO 总开关关闭。
2. 验证本地管理员密码登录、CAP、Refresh、退出和密码找回。
3. 在“平台管理 → 身份认证”填写公开 HTTPS 地址并创建禁用状态的 Provider。
4. OIDC 单独替换 Client Secret；依次执行连接测试和浏览器认证测试。
5. 先启用 Provider 的登录/绑定能力，再启用 SSO 总开关。
6. 验证未绑定提示会消费旧 Flow，注册或登录后进入账户安全，并以本地密码发起新的绑定 Flow。
7. 验证本人和管理员解绑后的 `TokenVersion` 撤销、平台审计、Subject 搜索和 Provider 筛选。
8. 若启用赛道门禁，分别验证建队、邀请加入、管理员改道、邀请码组合和既有队伍不追溯。

## 恢复与回滚

- Redis 丢失只使未完成流程过期；不从 Redis恢复绑定，用户重新发起认证。
- `IdentityNotLinked` 同样不会保留可续接身份；用户必须从账户安全重新认证并绑定。
- PostgreSQL 中的 User 绑定、Provider 配置和 `data_protection_keys` 是恢复事实源。
- 恢复时必须同时恢复相同的 `EmailVerification__EncryptionKey`，否则 Client Secret 与浏览器关联 Cookie 无法解密。
- 身份源故障时先关闭 Provider 或 SSO 总开关；既有 NoCTF 会话继续按 TokenVersion 与有效期工作。
- 应用回滚保留新增列、JSONB、绑定和 Data Protection 表，不执行破坏性降级。旧版本会忽略这些增量结构。
