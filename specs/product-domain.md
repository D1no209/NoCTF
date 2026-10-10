# 产品范围与领域模型

## 产品范围

NoCTF 是面向团队的竞赛平台，定义五种模式：

- `Ctf`：Jeopardy/普通 CTF；可包含静态题和按队 Runtime。
- `Awd`：按轮换 Flag、持续服务状态和队间攻击计分。
- `Awdp`：Break Flag 与独立 Fix archive 判定。
- `Koh`：共享 Hill Runtime，通过平台轮询返回的队伍 Control Flag 判定控制权。
- `LiveSolo`：独立的 Match/Round、受理顺序结果与媒体/节目投影；见[模式规范](live-solo.md)和[验收状态](live-solo-status.md)。

以下常规生命周期、计分与题目时间规则的适用范围应结合各模式规范；LiveSolo Round 不套用原四模式的题目时间协议。

Penetration 是普通 CTF 内容，不是模式。平台不实现多阶段题、插件拥有的模式或静态容器群题型；进程拓扑不改变产品领域模型。

## 聚合与身份

### User

`User` 是平台身份，拥有 `UserKind.Human | Bot` 与独立的
`UserRole.User | Organizer | Administrator`。Human 通过公开注册创建；Bot 只能由
Administrator 创建，并由服务端填入 `.invalid` dummy Email 和随机 GUID 生成的非空
PasswordHash。Bot 是否能密码登录由 UserKind 规则决定，而不是由 dummy 值保密决定；它也不能
刷新 Token。两者使用相同的角色、团队、Competition/Challenge 和平台管理权限模型；Bot 可直接
拥有 Administrator，但至少一个 Active Human Administrator 的安全不变量始终保留。用户名与邮箱都保存原文及
大小写不敏感 Normalized 值。`TokenVersion` 是全局 Token 失效来源。

### Competition

`Competition` 是比赛聚合根，拥有：

- Mode、标题、说明、StartAt、EndAt、Status；
- 与具体 sealed Competition leaf 同模式的 typed TPH configuration；
- OwnerId 与带 Role 的 CompetitionCollaborator 关联；
- TeamRegistrationAutoApprove、MaxTeamMembers、MaxConcurrentRuntimeInstancesPerTeam；
- 32-byte `FlagDerivationSecret`；
- 显式 TracksEnabled 开关与有 Position 的关系化赛道集合；
- sealed CompetitionEvent TPH 生命周期审计。

`(CompetitionId, UserId)` 唯一约束保证 Manager、Judge、Observer 角色互斥。OwnerId 是唯一 Owner
来源，且 Owner 不得同时出现在 Collaborator 关联中。

### Team

Team 只属于一个 Competition。字段包含当前、大小写不敏感的 `TrackKey`、`CaptainId` 与
`team_members` 关系。`(CompetitionId, UserId)` 唯一约束保证用户在同一比赛只能加入一个队；
`team_captains` 的复合外键保证 Captain 必须是本队 TeamMember。队伍改道后，其全部历史
GameplayFact 按当前赛道重新投影。

### Competition Track

Competition 以普通列 `TracksEnabled` 显式启停赛道，并以带 `Position` 的 `competition_tracks`
保存 1–32 条跨模式赛道；GameplayFact 不复制赛道。每场比赛必须显式持久化且恰有一个公开、
非内部的默认赛道；不存在空配置回退或旧版本推断。Running 与 Paused 允许启停、修改定义和调整
归属；Finished 只读。删除使用中赛道必须在 Serializable 事务中指定目标赛道并迁移全部受影响队伍；
关闭赛道会把所有队伍原子归并到当前默认赛道，再次开启不会恢复旧归属。

每条赛道独立声明是否允许公开选择、计分、参与 CTF 血榜、影响 CTF 动态分值、出现在公开排行榜以及影响 AWD/AWDP/KoH 的竞争性结果，并可选用一个 SSO Provider UUID 作为身份门禁。内部赛道必须关闭所有公开与竞争开关，但仍可正常查看题目、运行 Runtime/Checker 并产生永久 GameplayFact 和工作人员事件。Administrator、Owner、Manager 可配置赛道；Judge、Observer 只读。内部赛道及队伍只对平台 Administrator 与比赛 Owner、Manager、Judge 可见，Observer 与普通用户均不得通过排行榜或详情协议读取。赛道开启时，普通参赛者必须显式选择公开可报名赛道并同时满足邀请码与 SSO 门禁；建队检查创建者，邀请加入检查新成员，管理员改道检查全体成员。门禁只在入场时校验，新增门禁与成员后续解绑均不追溯既有队伍。赛道关闭时门禁被忽略，协议允许省略 TrackKey，服务端忽略多传的赛道信息并在比赛行锁内写入默认赛道。

团队拥有一个全局唯一、明文的 32 字符 Base62 InvitationToken。字母表固定为 `0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz`，使用 CSPRNG rejection sampling 逐字符无偏生成；若撞全局唯一索引就整体重生。持有 Token 的已登录用户在 Published 状态直接加入；轮换立即使旧值失效。

### Challenge 与 CompetitionChallenge

`Challenge` 是且只属于一个 GameMode 的全局可复用题库模板，拥有题面、方向、模板 Attachment、明文模板静态 Flag，以及 provider-neutral 的 Runtime、Checker、动态 Flag 生成/注入定义。它不包含比赛排序、发布状态、分数、Hint、RuntimeProvider 或 RunnerPool。

`CompetitionChallenge` 是比赛内实例，链接 CompetitionId 与 ChallengeId，拥有可选的比赛内展示名称、Order、IsPublished、typed mode-specific rules TPH 和关系化 Hint。CTF/AWD/AWDP/KoH 还可配置开放、计分结束与提交截止时间；当前实现与验证见[题目时间配置](challenge-timing-status.md)。题目分值完全由 Rules leaf 与其关系子项决定，不保存独立基础分。展示名称为空时实时回退到 Challenge.Title；比赛内改名不修改全局题库模板。引用时 Challenge.Mode 必须等于 Competition.Mode。模板定义修改不改动正在运行的 Runtime；下一次 Start 或新 UUID Reset 读取最新定义，不保存题目定义版本，也不自动更新存量 Runtime。

### GameplayFact

GameplayFact 是玩家、管理员或系统在比赛中的客观行为及其当前唯一判定。Flag/Break 保存原文和 Hash，Fix/Hint/AWD Round 使用多态 Reference；ManualAdjustment 保存 canonical Int32 Value。重判覆盖同一行的 Result/FailureCode，不保留旧判定，也不保存分数。

### RuntimeInstance

RuntimeInstance 表示一个具体 UUID 标识的外部 Runtime。Reset 会停止旧实例并创建全新 Runtime UUID。
结构化 AccessEndpoint、PublishedPort、capacity allocation 使用关系行；Container、OVA receipt
使用一对一 RuntimeReceipt TPH。异步状态保存在 RuntimeInstance/GameplayFact/ChallengeFlag 自身，
JetStream 保存至少一次投递状态；不存在 RuntimeOperation 或 provider receipt JSON。

## Competition 生命周期

状态：

```text
Draft -> Visible <-> Published
Visible | Published -> Running <-> Paused
Visible | Published | Running | Paused -> Finished
```

- Draft 不公开；Visible 与 Published 公开且允许报名。
- StartAt 到达时，Visible/Published 执行 Start Gate；Draft 不自动开始。
- EndAt 是绝对 UTC 终点，即使 Paused 也进入 Finished。
- Owner/Manager 可提前 Start、Pause、Resume、Finish；Finished 不可恢复。
- Paused 冻结 `EffectiveRunningTime`，因此 AWD 加固期、AWD/AWDP 轮次延后；CTF Runtime TTL 使用真实 UTC，不冻结。
- Start Gate 返回结构化失败列表：稳定错误码、CompetitionChallengeId、TeamId、配置路径与说明。自动启动失败保持原状态并通知管理者。
- CTF 可显式开启赛后练习模式。已审核且未封禁的原参赛队与赛后自动审核的新队均可为已发布的
  Container 题目启动独立 `Practice` Runtime；练习实例沿用正式 Runtime 的容量、TTL、随机端口
  和回收规则，但不复用正式实例。练习 Flag 复用正式提交接口并创建普通 GameplayFact；其时间位于
  正式窗口外，因此不产生正式分数、血奖或排行榜变化。赛后新队由 RegisteredAt 推导，不存队伍类型标记，
  且不能进入正式榜。Finished 阶段不再允许修改队伍组织资料或追加成员。
- 平台 Administrator 可在比赛非 Running/Paused 且不存在活动或待处理 Runtime 资源时执行强制级联删除。
  操作必须输入完整比赛标题、至少 8 个字符的原因并二次确认；比赛作用域的历史与资源会被永久删除，
  但平台级审计事实保留操作者、比赛标识、标题、原因和时间。

### Start Gate 原子流程

Owner/Manager 手动 Start 与 StartAt 调度共用一个 Application 用例，并在带 concurrency stamp 的
Competition 聚合与必要的 Serializable 事务内：

1. 重读状态与 StartAt/EndAt；只接受 Visible/Published，EndAt 必须仍在未来；
2. 校验 typed Competition configuration、至少一个已发布且未删除的 CompetitionChallenge、所有实例模板存在且未删除；
3. 校验每个已发布题的 leaf mode、typed Definition/Rules、Flag/Attachment/Runtime/Checker/逻辑 URL 组合；
4. 校验所有 Approved Team 未删除/未 Ban 且 TeamMember/Captain 关系有效；
5. CTF：Static 题至少有一个可用通用 Flag或合法 RandomOne 候选；PerTeam 只验证模板，Flag 仍在各队 Runtime Start 前按需确认/生成；
6. AWD：仅 Container，平台部署的 Runtime provider/runner pool 可用，按队 Runtime 额度足以覆盖全部 AWD 题，注入与 Checker 定义有效；此时只建启动任务，不提前生成 Round Flag；
7. AWDP：Container target/Checker/Patch 定义完整，ObjectStorage 与平台 Runtime placement 可用；
8. KoH：共享 Runtime/Control URL 有效，为所有队生成缺失 Control Flag；
9. 无错误时设置 Running/RunningSince、追加 typed lifecycle event；提交成功后发布 Runtime/调度消息。Pending 状态可重建未发布任务；有任一错误则零状态变更、零启动消息。

稳定 gate code 至少包含 `CompetitionStateInvalid`、`CompetitionAlreadyEnded`、`NoPublishedChallenge`、`TemplateUnavailable`、`ConfigurationSchemaInvalid`、`FlagMissing`、`AttachmentSelectionInvalid`、`RuntimeDefinitionInvalid`、`RunnerPoolUnavailable`、`RuntimeQuotaInsufficient`、`CheckerDefinitionInvalid`、`ObjectStorageUnavailable` 与 `TeamInvariantInvalid`。错误数组完整返回，不遇到第一项即停止；同一资源/路径只返回一项。

## 报名与团队

- Visible、Published 允许创建团队和凭 Token 加入；Running 仅在显式开启赛中报名时允许。
- 新团队先保存为 Unregistered，不因邀请码或 SSO 尚未满足而阻止创建和编辑。队长提交报名时才校验当前赛道的邀请码和全体成员 SSO；通过后，AutoApprove 为 true 时成为 Approved，否则成为 Pending。
- Pending 可被批准或拒绝；Rejected 与 Unregistered 可重新提交当前报名草稿。Administrator、Owner、Manager 可把队伍显式设为 Pending。
- 队长可在 Unregistered 状态修改队名、头像和公开可报名赛道。选择赛道与成员加入只构建草稿，不提前校验邀请码或 SSO。
- 队名、头像、赛道、成员加入/退出/移除和队长转让有实际变化时一律退回 Unregistered；无实际变化与邀请 Token 轮换不改变状态。
- 组织变更窗口与报名窗口相同；Paused、Finished 禁止，Running 受 AllowTeamRegistrationWhileRunning 控制。
- Unregistered/Pending/Rejected 不能获得题目、附件、提示、咨询、Flag、Runtime 或新 GameplayFact 权限。退出 Approved 后已有 Runtime 保留至正常生命周期结束，但所有选手读取与操作 API 都拒绝访问。
- CaptainId 是唯一队长来源。转让队长原子更新 TeamCaptain 复合外键；成员关系无顺序。
- Ban 立即拒绝私有数据、Runtime 与 GameplayFact，回收该队 CTF/AWD Runtime，并把该队及与其相关的攻防事实从投影排除。Unban 恢复当前事实；CTF Runtime 由选手重新启动，AWD 由系统重新配置。
- 每次 Ban 对应一个不可变事件。队长可提交一次私密申诉，全队可读；Judge/Observer 只读，Administrator、Owner、Manager 裁决。接受申诉或主动纠错允许赛后恢复历史投影，但 Finished 比赛不重新配置 Runtime。申诉、裁决和公开纠错都保留原始事件关联，不公开工作人员原因或证据。

## 权限矩阵

| 主体 | 权限 |
|---|---|
| Administrator | 全平台全部权限 |
| Competition Owner | 比赛全部权限、Collaborator 角色、所有权转让、软/硬删除 |
| Manager | 比赛/题目实例/Flag/附件/配置/团队/生命周期/Runtime/判题与重判；不能管理权限数组或所有权 |
| Judge | 管理查询、GameplayFact 原始 Flag/当前结果、运行诊断、批量判题与重判；不能改配置/团队/生命周期 |
| Observer | 与 Judge 相同的读取范围；无写权限 |
| Player | 公开数据与本队 GameplayFact；绝不能访问其他队 Value、系统事实、管理员事实或内部诊断 |

Owner/Manager 必须是 Organizer 或 Administrator；Judge/Observer 必须完成邮箱验证。Owner
转让后，旧 Owner 自动成为 Manager。Collaborator 全量替换与 Owner transfer 使用 Serializable
事务、普通唯一约束和 bounded retry；服务端在同一事务校验角色互斥和 Owner 不变量。

Challenge 自身由 OwnerId 与 ChallengeManager 关联控制。Shared 模板可被其他 Organizer 查看题面并引用，但其原始 Flag、对象键与内部 Runtime 配置只对模板管理者可见；Private 改为 Shared/反向修改不破坏既有引用。

## 删除

软删除只使用 nullable `DeletedAt`。Running/Paused Competition 必须先 Finish。最终硬删除由 Owner/Admin 明确确认；已软删除 Competition 本身就是清理锚点，数据库提交后发布幂等 Runtime 与对象清理消息，不另建 tombstone 表。Challenge 存在未删除 CompetitionChallenge 引用时不得删除。
