# 比赛管理手册

本章逐项对应比赛管理工作区，面向赛事负责人、裁判和承担管理工作的平台管理员。先读本页了解入口和职责，再按菜单进入对应操作说明。

## 进入正确比赛

1. 登录，进入顶栏“竞赛”。
2. 在左侧选择要管理的比赛，核对标题、模式和时间。
3. 使用“我的队伍”右侧的管理入口进入 `/admin/competitions/比赛ID`。
4. 在管理工作区再次核对比赛标题与状态，再切换左侧菜单。

当前 SPA 的 `/admin/competitions/...` 使用 **Administrator 专属路由中间件**，普通 User/Organizer 不显示管理入口。后台 API 仍按照 Owner/Manager/Judge/Observer 等比赛资源权限校验；平台角色有 Organizer 或后台已获协作授权，不代表当前浏览器管理工作区一定开放。使用非 Administrator 自动化账号时，按实际 API 授权接入，不能通过改 URL 绕过界面限制。

创建按钮位于竞赛浏览页右上角，不存在单独的比赛管理列表/新建页面。管理员的比赛列表包含草稿和已删除项；删除分类不代表正式比赛仍对普通用户公开。

## 按管理菜单阅读

| 当前菜单 | 本手册章节 | 操作目标 |
| --- | --- | --- |
| 概览 | [创建与概览](./settings/overview.md)、[生命周期](./operations/lifecycle.md) | 海报、开赛检查、补 Flag、发布和启停 |
| 配置 | [基本设置](./settings/basic.md)、[计分规则](./settings/scoring.md) | 时间、报名、Runtime、咨询、练习、题解与模式规则 |
| 赛道 | [赛道与准入](./participants/tracks.md) | 分组资格、内部测试、邀请码与 SSO |
| 方向 | [方向目录](./settings/directions.md) | 题目分类、图标和排序 |
| 题目 | [比赛题目](./content/challenges.md)、[提示与 Flag](./content/hints-flags.md) | 引用模板、发布、单题规则和敏感内容 |
| 队伍管理 | [队伍审核](./participants/teams.md) | 报名、详情、赛道、调分、封禁与申诉 |
| 大屏/控制屏、动态 | [赛事动态与大屏](./operations/events-live.md) | 展示公开进展，追溯管理事件 |
| 提交 | [评测与重判](./judging/submissions.md) | 查询尝试、手动派发、证据和差异预览 |
| 运行实例 | [比赛 Runtime](./operations/runtimes.md) | 启动、续期、终止和强制处理 |
| 流量抓取 | [抓包管理](./operations/traffic-captures.md) | 筛选、PCAPNG、批量 ZIP 与删除 |
| 作弊 | [作弊与申诉](./judging/cheating-appeals.md) | 线索调查、裁决和纠正 |
| 排行榜 | [榜单可见性](./operations/leaderboard.md) | 正常、冻结、隐藏和成绩检查 |
| 公告 | [公告管理](./communication/announcements.md) | 受众、发布、修改与撤回 |
| 题解 | [题解审核](./judging/writeups.md) | PDF、咨询和人工评分 |
| 导出 | [赛事导出](./integrations/exports.md) | 时间范围 JSONL、归档 ZIP 与敏感导出理由 |
| Webhook | [Webhook 管理](./integrations/webhooks.md) | 外部播报、密钥、测试和投递诊断 |
| 闯关与勋章 | [CTF 编排](./content/progression.md) | 前驱、批量连边、布局和勋章 |
| 权限 | [协作者与所有权](./participants/permissions.md) | Manager/Judge/Observer 和 Owner 转让 |

咨询入口在比赛选手工作区，并由线程授权区分管理操作，见 [咨询处理](./communication/questions.md)。删除/恢复/永久删除在概览，见 [比赛删除](./operations/deletion.md)。

## 推荐工作流

第一次举办比赛按 [最小 CTF 教程](./quick-start.md)完成闭环。正式筹备顺序为：基本配置 → 方向/赛道 → 题库与比赛题目 → 协作者 → 报名审核 → 开赛检查 → 发布/开始。运行期间主要查看提交、Runtime、咨询、作弊和榜单；结束后处理题解、申诉、导出和清理。

## 通用操作原则

列表筛选后回到第一页，核对是否包含已删除记录；详情抽屉可以保持列表，但操作仍作用于当前详情 UUID。表单内修改不等于已保存，分区拥有各自保存按钮。202 代表异步受理，最终状态需轮询/刷新核对；重判、终止和调分客户端超时后先查询是否已入库，避免重复。

权限不足、无效状态和业务引用冲突是不同问题。409 不一律代表“版本冲突”；按具体错误码、引用预览和资源 ID 修复。只在确实提供并发图标记的闯关保存等操作中处理对应并发冲突。
