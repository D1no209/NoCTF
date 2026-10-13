# LiveSolo 比赛管理

LiveSolo 与 CTF、AWD、AWDP、KoH 同属平台赛制。本页面向主办方和裁判：两支队伍在 Match 中进行多个 Round，以服务器持久化的受理顺序确定第一条有效 Flag，累计胜局并晋级。它没有普通题目积分、血奖或付费提示。

本章按 2026-10-14 当前代码核对，覆盖本场题目池、选手与工作人员工作区、选题风险确认、历史回看和权威结果。关闭平台直播时可按无媒体路径对局；开启直播后的真人采集、多节点恢复和生产容量仍须按目标部署实测，见 [启用与验收](../../../operations/live-solo-readiness.md)。

## 入口与阅读路线

文档位于“比赛管理 → 赛制规则 → LiveSolo”。选手准备、场内解题、观赛和投稿统一从 [选手指南](../../../player/modes/live-solo/index.md)进入；媒体与正式验收归入安装运维。

在竞赛创建弹窗选择 LiveSolo。工作人员进入 `/admin/competitions/比赛ID/live-solo`，使用设置、本场题目池、题组和赛程入口；选手进入 `/competitions/比赛ID/live-solo` 选择 Match。草稿仅向获授权工作人员展示，管理账号也可明确切换到选手视图。

| 工作 | 操作说明 |
| --- | --- |
| 胜局、时间、并发、观众与录像配置 | [赛制设置](./settings.md) |
| 本场新建、投放、脱离模板与保存到题库 | [本场题目池](./question-pool.md) |
| 题组、备用组、候选与复用风险 | [题组管理](./question-groups.md) |
| 种子、单败/双败及手动对局 | [赛程管理](./bracket.md) |
| 名单、双方准备、屏幕与倒计时 | [对局准备](../../../player/modes/live-solo/preparation.md) |
| 已开放题目、附件、Runtime 与 Flag | [选手对局](../../../player/modes/live-solo/playing.md) |
| 暂停、恢复、作废与弃权 | [裁判操作](./judging.md) |
| 准备、重试、取消与权威结果 | [工作人员工作区](./workspace.md) |
| 已开放快照与历史结果 | [历史回看](../../../player/modes/live-solo/review.md) |
| 已完成结果纠正和下游重赛 | [结果纠正](./corrections.md) |
| 延迟节目、观众名额和终场结果 | [观赛与节目](../../../player/modes/live-solo/program.md) |
| 录像预览、保留、公开与恢复 | [录像管理](./recordings.md) |
| Match/Round/Question 赛后题解 | [赛后题解](../../../player/modes/live-solo/postgame.md) |
| 独立媒体服务、网络和磁盘 | [媒体部署](../../../installation/live-solo-media.md) |

## 角色分工

Owner、Manager 和平台管理员维护设置、题组、赛程、结果纠正与发布。Judge 及拥有相应能力的管理者执行基础裁判、查看原始屏幕/录像和保留证据；Observer 按当前返回能力只读，不获得裁定按钮。

队长在准备阶段锁定本场名单，任一有效上场成员可确认本队准备。只有锁定名单中的有效成员可以按当前 Round 读取资源和提交；同队未上场不等于拥有场内访问。

观众读取延迟节目，不接收裁判当前实时状态或原始媒体房间凭据。所有接口仍检查比赛可见性、当前身份和适用 MFA，知道 URL 不能扩大访问范围。

## 与其他赛制的操作差异

LiveSolo 使用独立 Round 时间、题组、判胜和媒体界面，不使用 CTF 闯关、普通积分排行榜、CTF/AWDP 大屏或普通题目的绝对开放时间编辑器。CTF/AWD/AWDP/KoH 继续使用各自原流程，不因部署 LiveSolo 加载媒体 SDK。
