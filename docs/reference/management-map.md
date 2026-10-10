# 管理页面覆盖索引

本索引逐项核对当前 ClientApp 管理与 LiveSolo 路由。比赛工作区向本比赛获授权工作人员开放，菜单按角色区分；平台设置仍仅 Administrator，服务器最终执行资源授权。

## 当前路由与章节

| 管理路由 | 对应手册 |
| --- | --- |
| `/admin/challenges/:id` | [操作说明](../challenge-bank/templates.md) |
| `/admin/challenges` | [操作说明](../challenge-bank/templates.md) |
| `/admin/challenges/new` | [操作说明](../challenge-bank/templates.md) |
| `/admin/competitions/:id/announcements` | [操作说明](../competition/communication/announcements.md) |
| `/admin/competitions/:id/challenges/:ccId` | [操作说明](../competition/content/challenges.md) |
| `/admin/competitions/:id/challenges` | [操作说明](../competition/content/challenges.md) |
| `/admin/competitions/:id/cheats` | [操作说明](../competition/judging/cheating-appeals.md) |
| `/admin/competitions/:id/configuration` | [操作说明](../competition/settings/basic.md) |
| `/admin/competitions/:id/directions` | [操作说明](../competition/settings/directions.md) |
| `/admin/competitions/:id/exports` | [操作说明](../competition/integrations/exports.md) |
| `/admin/competitions/:id` | [操作说明](../competition/settings/overview.md) |
| `/admin/competitions/:id/leaderboard` | [操作说明](../competition/operations/leaderboard.md) |
| `/admin/competitions/:id/permissions` | [操作说明](../competition/participants/permissions.md) |
| `/admin/competitions/:id/progression` | [操作说明](../competition/content/progression.md) |
| `/admin/competitions/:id/runtimes` | [操作说明](../competition/operations/runtimes.md) |
| `/admin/competitions/:id/submissions` | [操作说明](../competition/judging/submissions.md) |
| `/admin/competitions/:id/teams` | [操作说明](../competition/participants/teams.md) |
| `/admin/competitions/:id/tracks` | [操作说明](../competition/participants/tracks.md) |
| `/admin/competitions/:id/traffic-captures` | [操作说明](../competition/operations/traffic-captures.md) |
| `/admin/competitions/:id/webhooks` | [操作说明](../competition/integrations/webhooks.md) |
| `/admin/competitions/:id/writeups` | [操作说明](../competition/judging/writeups.md) |
| `/admin/platform/audit` | [操作说明](../platform/maintenance/audit.md) |
| `/admin/platform/authentication` | [操作说明](../platform/security/authentication.md) |
| `/admin/platform/email` | [操作说明](../platform/security/email.md) |
| `/admin/platform/experiments` | [操作说明](../platform/experiments.md) |
| `/admin/platform` | [操作说明](../platform/branding.md) |
| `/admin/platform/logs` | [操作说明](../platform/maintenance/logs.md) |
| `/admin/platform/runtimes` | [操作说明](../platform/maintenance/runtimes.md) |
| `/admin/platform/users` | [操作说明](../platform/users/accounts.md) |

动态用户/队伍/Runtime 详情由所在列表的抽屉承载，也看对应列表章节。比赛大屏和动态、咨询属于 `/competitions/比赛ID/...`，分别见 [动态与大屏](../competition/operations/events-live.md)和 [咨询](../competition/communication/questions.md)。

## LiveSolo 独立工作区

LiveSolo 的操作不放在普通积分排行榜或旧题目工作区。草稿读取和写入按实际工作人员角色授权，尚未完成的正式验收见 [能力边界](../live-solo/readiness.md)。

| 路由 | 手册 |
| --- | --- |
| `/competitions/:id/live-solo` | [导读](../live-solo/index.md) |
| `/competitions/:id/live-solo/settings` | [操作说明](../live-solo/settings.md) |
| `/competitions/:id/live-solo/groups` | [操作说明](../live-solo/question-groups.md) |
| `/competitions/:id/live-solo/groups/:groupId` | [操作说明](../live-solo/question-groups.md) |
| `/competitions/:id/live-solo/bracket` | [操作说明](../live-solo/bracket.md) |
| `/competitions/:id/live-solo/matches/:matchId/:workspace*` | [操作说明](../live-solo/preparation.md) |
| `/competitions/:id/live-solo/program/:matchId` | [操作说明](../live-solo/program.md) |
| `/competitions/:id/live-solo/recordings/:matchId` | [操作说明](../live-solo/recordings.md) |
| `/competitions/:id/live-solo/recordings/:matchId/:recordingId` | [操作说明](../live-solo/recordings.md) |
| `/competitions/:id/live-solo/corrections/:matchId` | [操作说明](../live-solo/corrections.md) |
| `/competitions/:id/live-solo/corrections/:matchId/:correctionId` | [操作说明](../live-solo/corrections.md) |
| `/competitions/:id/live-solo/postgame/:matchId` | [操作说明](../live-solo/postgame.md) |
| `/competitions/:id/live-solo/postgame/:matchId/:roundId/:questionId` | [操作说明](../live-solo/postgame.md) |

## 页面内操作补充

| 管理任务 | 说明 |
| --- | --- |
| 创建、海报、开赛检查、生成缺失 Flag | [概览](../competition/settings/overview.md) |
| 比赛删除、恢复、硬删除预览、强制删除 | [删除](../competition/operations/deletion.md) |
| 题目提示、删除/恢复、敏感 Flag | [提示与 Flag](../competition/content/hints-flags.md) |
| 队伍/题目/PDF 人工调分 | [调分](../competition/judging/adjustments.md) |
| 表单模式默认与单题继承 | [计分配置](../competition/settings/scoring.md) |
| 抓包 PCAPNG 与选中 ZIP | [流量抓取](../competition/operations/traffic-captures.md) |
| SMTP 密码与验证/找回期限 | [邮件](../platform/security/email.md) |
| Cap 工作量、Runtime/Evaluation 策略 | [人机验证](../platform/security/human-verification.md) |
| Provider secret、连接测试、认证测试 | [SSO](../platform/security/authentication.md) |
| Bot、JWT、全量撤销、身份模拟 | [令牌](../platform/users/tokens.md) |
| 删除引用预览、匿名化、最后管理员保护 | [账号删除](../platform/users/deletion.md) |
| 题库附件/Flag 与删除恢复 | [题库附件](../challenge-bank/attachments-flags.md) |
| 单题自动开题、计分结束和提交截止 | [时间设置](../competition/content/timing.md) |
| LiveSolo 平台视频尺寸/帧率/码率 | [视频策略](../platform/video-policy.md) |
| 模板 dirty 保存、测试与引用管理 | [模板测试](../challenge-bank/testing.md)、[引用](../challenge-bank/permissions-placement.md) |

新增管理页面或命令时，在此表补充章节并同时更新相应侧边栏；页面存在而无操作说明应视为文档缺口。历史 specs 和上线报告不替代面向用户的手册。
