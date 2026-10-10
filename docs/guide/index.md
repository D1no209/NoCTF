# 手册导读

NoCTF 是以队伍为参赛单位的竞赛平台，支持 CTF、AWD、AWDP、KoH 和 LiveSolo。本手册围绕“如何完成一次操作”组织内容，适合部署人员、选手、出题人、赛事组织者和平台管理员。

LiveSolo 是独立的 Match/Round 赛制，配置、判胜和媒体都不同于前四种模式。使用 [LiveSolo 操作手册](../live-solo/index.md)和 [启用验收](../live-solo/readiness.md)，不要直接套用普通积分榜或容器题的开赛流程。

## 选择阅读路线

| 身份 | 建议顺序 | 完成目标 |
| --- | --- | --- |
| 首次部署人员 | 安装方式 → 依赖 → Docker/Kubernetes → HTTPS → 验收 | 管理员可登录、文件可下载、Runtime 可启动 |
| 选手 | 账号 → 队伍 → 题目 → 对应模式 → 排行榜 | 获得 Approved 参赛资格并完成一次提交 |
| 赛事组织者 | 第一场比赛 → 配置 → 题目 → 发布 → 审核 → 评测 | 完成可被选手访问、可正常计分的比赛 |
| 出题人 | 平台概念 → 题库 → Runtime/Checker | 创建可复用题库模板并完成测试 |
| 平台管理员 | 角色 → 账号 → 平台设置 → 实例与日志 | 配置身份、邮件与执行资源并维护平台 |
| 运维人员 | 配置 → 监控 → 升级 → 备份 → 排障 | 可观测、可升级、可恢复 |
| LiveSolo 工作人员/选手 | 独立设置 → 题组/赛程 → 名单/共享 → Round → 节目/录像/纠正 | 按独立规则完成配置和验收，不套用普通积分榜 |

比赛管理从 [完整菜单手册](../competition/index.md)阅读，题库维护从 [题库手册](../challenge-bank/index.md)阅读，平台设置从 [平台管理手册](../platform/index.md)阅读。[覆盖索引](../reference/management-map.md)将实际管理路由逐项对应到操作说明。

## 命令与示例约定

- Bash 代码默认在 Linux/WSL 中执行；PowerShell 代码默认使用 PowerShell 7。
- 普通安装的部署脚本在发布配置包解压目录执行，例如 `/opt/noctf-release`；写有“安装目录”的命令在 `/opt/noctf` 等真实配置与数据目录执行。只有开发者章节使用源码仓库。
- `YOUR_CONTEXT`、`YOUR_REGISTRY`、`YOUR_DIGEST` 等大写文本是必须替换的占位符。
- `noctf.example.com`、`registry.example.com`、`challenges.example.com` 是示例域名，需要替换为自己的 DNS。
- 配置中的 `__` 是 .NET 环境变量层级分隔符，例如 `Runtime__Provider` 对应 `Runtime:Provider`。
- 页面中的比赛 ID、题目 ID、队伍 ID 均是实际创建后得到的 UUID，不用标题替代。

## 操作结果如何判断

保存表单成功只代表配置已受理。报名需要看到 Approved，题目需要已发布，比赛需要 Running，Runtime 需要就绪，评测需要最终结果。排行榜和实时通知是异步更新的；等待时先检查对应业务状态，避免重复提交。

## 版本与资料边界

本手册描述当前仓库能力，不承诺旧数据库或旧 API 兼容。生产部署应固定审核过的 Host 镜像 digest，并保留对应 Git revision 的手册和配置模板。历史上线记录不是新安装命令，不应直接复制历史密钥、生产地址或临时镜像标签。

继续阅读 [平台概念](./concepts.md)，或直接进入 [选择安装方式](../installation/index.md)。
