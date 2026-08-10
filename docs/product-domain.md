# 产品范围与领域模型

## 产品范围

NoCTF 是面向团队的竞赛平台，正式支持四种且仅四种模式：

- `Ctf`：Jeopardy/普通 CTF；可包含静态题和按队 Runtime。
- `Awd`：按轮换 Flag、持续服务状态和队间攻击计分。
- `Awdp`：Break Flag 与独立 Fix archive 判定。
- `Koh`：共享 Hill Runtime，通过平台轮询返回的队伍 Control Flag 判定控制权。

Penetration 是普通 CTF 内容，不是模式。平台不实现多阶段题、插件拥有的模式或静态容器群题型；进程拓扑不改变产品领域模型。

## 聚合与身份

### User

`User` 是平台身份，拥有 `UserKind.Human | Bot` 与独立的
`UserRole.User | Organizer | Administrator`。Human 通过公开注册创建；Bot 只能由
Administrator 创建，并由服务端填入 `.invalid` dummy Email 和随机 GUID 生成的非空
PasswordHash。Bot 是否能密码登录由 UserKind 规则决定，而不是由 dummy 值保密决定；它也不能
刷新 Token。两者使用相同的 Competition/Challenge 权限模型。用户名与邮箱都保存原文及
大小写不敏感 Normalized 值。`TokenVersion` 是全局 Token 失效来源。

### Competition

`Competition` 是比赛聚合根，拥有：

- Mode、标题、说明、StartAt、EndAt、Status；
- 公共列与版本化 Mode Configuration JSON；
- OwnerId、ManagerIds、JudgeIds、ObserverIds；
- TeamRegistrationAutoApprove、MaxTeamMembers、MaxConcurrentRuntimeInstancesPerTeam；
- 32-byte `FlagDerivationSecret`；
- ConfigurationRevision、LeaderboardDirty；
- 生命周期审计。

协作者直接存为三个互斥 UUID 数组，不存在 Collaborator 实体。Owner 不得同时出现在数组中。

### Team

Team 只属于一个 Competition。字段包含 `CaptainId` 与无顺序语义的 `MemberIds uuid[]`。数组不得为空、不得重复、必须包含 CaptainId，且长度不超过 Competition.MaxTeamMembers。不存在 TeamMember 表。

团队拥有一个全局唯一、明文的 32 字符 Base62 InvitationToken。字母表固定为 `0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz`，使用 CSPRNG rejection sampling 逐字符无偏生成；若撞全局唯一索引就整体重生。持有 Token 的已登录用户在 Published 状态直接加入；轮换立即使旧值失效。

### Challenge 与 CompetitionChallenge

`Challenge` 是且只属于一个 GameMode 的全局可复用题库模板，拥有题面、方向、模板 Attachment、明文模板静态 Flag，以及 provider-neutral 的 Runtime、Checker、动态 Flag 生成/注入定义。它不包含比赛排序、发布状态、分数、Hint、RuntimeProvider 或 RunnerPool。

`CompetitionChallenge` 是比赛内实例，链接 CompetitionId 与 ChallengeId，拥有 Order、IsPublished、BaseScore、Revision、Rules JSON 和 Hint。引用时 Challenge.Mode 必须等于 Competition.Mode。模板定义修改不改动正在运行的 Generation；下一次 Start/Reset 读取最新定义，不保存题目定义版本，也不自动更新存量 Runtime。

### GameplayFact

GameplayFact 是玩家、管理员或系统在比赛中的客观行为及其当前唯一判定。Flag/Break 保存原文和 Hash，Fix/Hint/AWD Round 使用多态 Reference；ManualAdjustment 保存 canonical Int32 Value。重判覆盖同一行的 Result/FailureCode，不保留旧判定，也不保存分数。

### RuntimeInstance

RuntimeInstance 表示一个具体 Generation 的外部 Runtime。异步状态保存在 RuntimeInstance/GameplayFact/ChallengeFlag 自身，Wolverine 保存投递状态；不存在 RuntimeOperation 表。

## Competition 生命周期

状态：

```text
Draft -> Visible <-> Published
Visible | Published -> Running <-> Paused
Visible | Published | Running | Paused -> Finished
```

- Draft 不公开；Visible 公开但不能报名；Published 公开且允许报名。
- StartAt 到达时，Visible/Published 执行 Start Gate；Draft 不自动开始。
- EndAt 是绝对 UTC 终点，即使 Paused 也进入 Finished。
- Owner/Manager 可提前 Start、Pause、Resume、Finish；Finished 不可恢复。
- Paused 冻结 `EffectiveRunningTime`，因此 AWD 加固期、AWD/AWDP 轮次延后；CTF Runtime TTL 使用真实 UTC，不冻结。
- Start Gate 返回结构化失败列表：稳定错误码、CompetitionChallengeId、TeamId、配置路径与说明。自动启动失败保持原状态并通知管理者。

### Start Gate 原子流程

Owner/Manager 手动 Start 与 StartAt 调度共用一个 Application 用例，并在 Competition advisory lock 的单事务内：

1. 重读状态、StartAt/EndAt 与 revision；只接受 Visible/Published，EndAt 必须仍在未来；
2. 校验 Competition 配置 schema、至少一个已发布且未删除的 CompetitionChallenge、所有实例模板存在且未删除；
3. 校验每个已发布题的 Challenge.Mode、Definition/Rules schema、Flag/Attachment/Runtime/Checker/逻辑 URL 组合；
4. 校验所有 Approved Team 未删除/未 Ban且成员数组有效；
5. CTF：Static 题至少有一个可用通用 Flag或合法 RandomOne 候选；PerTeam 只验证模板，Flag 仍在各队 Runtime Start 前按需确认/生成；
6. AWD：仅 Container/Compose，平台部署的 Runtime provider/runner pool 可用，按队 Runtime 额度足以覆盖全部 AWD 题，注入与 Checker 定义有效；此时只建启动任务，不提前生成 Round Flag；
7. AWDP：Container target/Checker/Patch 定义完整，ObjectStorage 与平台 Runtime placement 可用；
8. KoH：共享 Runtime/Control URL 有效，为所有队生成缺失 Control Flag；
9. 无错误时设置 Running/RunningSince、追加 lifecycle owned audit，并在同事务写 Runtime/调度 Outbox；有任一错误则零状态变更、零启动消息。

稳定 gate code 至少包含 `CompetitionStateInvalid`、`CompetitionAlreadyEnded`、`NoPublishedChallenge`、`TemplateUnavailable`、`ConfigurationSchemaInvalid`、`FlagMissing`、`AttachmentSelectionInvalid`、`RuntimeDefinitionInvalid`、`RunnerPoolUnavailable`、`RuntimeQuotaInsufficient`、`CheckerDefinitionInvalid`、`ObjectStorageUnavailable` 与 `TeamInvariantInvalid`。错误数组完整返回，不遇到第一项即停止；同一资源/路径只返回一项。

## 报名与团队

- 只有 Published 允许创建团队和凭 Token 加入。
- AutoApprove 为 true 时新团队是 Approved，否则 Pending。
- Pending 可被批准或拒绝；Rejected 可重新提交为 Pending。
- Pending/Rejected 可管理成员与资料，但不能获得题目私有数据、Flag、Runtime 或 GameplayFact 权限。
- Running/Paused/Finished 冻结成员与审核状态；违规处置使用 Ban。
- CaptainId 是唯一队长来源。转让队长原子更新 CaptainId；成员数组无顺序。
- Ban 立即拒绝私有数据、Runtime 与 GameplayFact，回收该队 CTF/AWD Runtime，并把该队及与其相关的攻防事实从投影排除。Unban 恢复当前事实；CTF Runtime 由选手重新启动，AWD 由系统重新配置。
- 每次 Ban 对应一个不可变事件。队长可提交一次私密申诉，全队可读；Judge/Observer 只读，Administrator、Owner、Manager 裁决。接受申诉或主动纠错允许赛后恢复历史投影，但 Finished 比赛不重新配置 Runtime。申诉、裁决和公开纠错都保留原始事件关联，不公开工作人员原因或证据。

## 权限矩阵

| 主体 | 权限 |
|---|---|
| Administrator | 全平台全部权限 |
| Competition Owner | 比赛全部权限、权限数组、所有权转让、软/硬删除 |
| Manager | 比赛/题目实例/Flag/附件/配置/团队/生命周期/Runtime/判题与重判；不能管理权限数组或所有权 |
| Judge | 管理查询、GameplayFact 原始 Flag/当前结果、运行诊断、批量判题与重判；不能改配置/团队/生命周期 |
| Observer | 与 Judge 相同的读取范围；无写权限 |
| Player | 公开数据与本队 GameplayFact；绝不能访问其他队 Value、系统事实、管理员事实或内部诊断 |

Owner/Manager 必须是 Organizer 或 Administrator；Judge/Observer 必须完成邮箱验证。Owner
转让后，旧 Owner 自动进入 ManagerIds。权限数组的全量替换由独立 PermissionRevision 防止
陈旧覆盖；Owner transfer 也递增该 revision。

Challenge 自身由 OwnerId/ManagerIds 控制。Shared 模板可被其他 Organizer 查看题面并引用，但其原始 Flag、对象键与内部 Runtime 配置只对模板管理者可见；Private 改为 Shared/反向修改不破坏既有引用。

## 删除

软删除只使用 nullable `DeletedAt`。Running/Paused Competition 必须先 Finish。最终硬删除由 Owner/Admin 明确确认；已软删除 Competition 本身就是清理锚点，在删除最后一行前通过 Outbox 完成 Runtime 与对象清理，不另建 tombstone 表。Challenge 存在未删除 CompetitionChallenge 引用时不得删除。
