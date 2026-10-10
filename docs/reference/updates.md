# 手册更新说明

## 2026-10-11

### 目录归类调整

取消 LiveSolo 独立顶层导航及首页功能卡。CTF、AWD、AWDP、KoH、LiveSolo 在“选手指南 → 赛制参赛”和“比赛管理 → 赛制规则”中同级显示。

LiveSolo 参赛内容迁入 player，设置/题组/赛程/裁判/纠正/录像迁入 competition，媒体部署归入 installation，启用验收归入 operations。原 `/live-solo/` 地址保留自动跳转与备用链接，不参与搜索，避免旧链接失效。

### 功能内容更新

本次以工作区代码基线 `ceead077`、当前管理页面、题目时间编辑器及 LiveSolo 功能/验收资料核对，更新面向用户的操作手册，不修改平台业务代码或数据库。

- 新增 [LiveSolo 独立手册](../competition/modes/live-solo/index.md)：配置/阶段规则、题组/复制、单双败/手动 Match、名单/屏幕、场内资源/受理、裁判、纠正、节目、录像、赛后题解、媒体部署和真实验收。
- 新增 [题目时间](../competition/content/timing.md)：自动开放、计分结束、提交截止，以及历史影响预览/确认和重算等待。
- 新增 [平台视频资源策略](../platform/video-policy.md)：宽高/FPS/码率单位、分段生效、三份预留和实际测量边界。
- 修正仍残留的管理员专属入口描述，按比赛资源角色和职责区分菜单；保留平台管理仅 Administrator 的边界。
- 更新 [平台日志](../platform/maintenance/logs.md)：有界 cursor 页码、20/50/100/200 条、固定历史窗口、新日志提示和查看最新。
- 补上账号/代签令牌与 [MFA](../platform/security/mfa.md)、[Passkey](../platform/security/passkeys.md)的关联限制。
- 将第五赛制入口、侧边栏、FAQ、术语和管理覆盖索引同步到当前页面。

### 能力结论

LiveSolo 已有独立页面和基础实现/局部验证。本手册不将模型、迁移、API 或合成媒体当作完整真人比赛验收；待验证范围集中于 [就绪清单](../operations/live-solo-readiness.md)。前四赛制与原安装仍按 Action 完整镜像流程执行，不因文档更新要求用户源码安装平台。
