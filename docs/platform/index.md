# 平台管理手册

入口：顶栏“平台设置”，主路由 `/admin/platform`。所有平台管理页面仅对 Administrator 开放，未登录进入登录页，非管理员不会因为知道 URL 而获得管理能力。

## 菜单与职责

| 页面 | 操作手册 | 主要任务 |
| --- | --- | --- |
| 平台信息 | [信息与品牌](./branding.md) | 名称、说明、Logo、平台运行信息 |
| 用户 | [账号管理](./users/accounts.md)、[令牌与模拟](./users/tokens.md)、[删除与匿名化](./users/deletion.md) | 角色、状态、邮箱、Bot、SSO 和账号生命周期 |
| 邮件与人机验证 | [邮件](./security/email.md)、[人机验证](./security/human-verification.md) | SMTP、验证/找回期限、Provider secret 与策略 |
| 认证 | [SSO](./security/authentication.md) | 全局开关、OIDC/CAS、Provider 和两类测试 |
| 实验功能 | [CTF 修复验证](./experiments.md)、[LiveSolo 视频策略](./video-policy.md) | 独立实验开关、视频宽高/FPS/码率与使用边界 |
| 运行容器 | [跨比赛 Runtime](./maintenance/runtimes.md) | 筛选、关联资源、终止与强制终止 |
| 日志 | [日志查询与导出](./maintenance/logs.md) | 级别、来源、窗口、实时读取与 JSONL |
| 审计 | [审计与归档](./maintenance/audit.md) | 操作者、比赛、动作与关联证据 |

比赛管理不是平台信息页下的独立总列表，应从竞赛浏览页选定比赛进入；题库从顶栏自己的入口进入。

## 首次管理员设置顺序

1. 设置品牌，检查公开页面与分享元数据。
2. 检查管理员邮箱、密码和保留至少一个 Active Human Administrator。
3. 配置 SMTP，测试后决定是否启用邮箱验证。
4. 按需要配置人机验证和 SSO，并完成真实流程测试。
5. 查看 Runtime、私有 Loki 与审计是否按部署要求可读。
6. 授予主办和 Bot 最小角色/资源权限，建立变更与撤销记录。

## 页面设置与部署配置

保存数据库设置不等于修改 Role、Provider、Runner Pool、可信代理、NATS 地址或加密 key。这些由部署运维处理，参考 [配置项](../installation/configuration.md)。

页面 secret 采用单独替换入口，只显示是否已配置，不回显旧值；留空不代表删除已有 secret。所有角色使用稳定共享加密 key，升级不要随机重建。
