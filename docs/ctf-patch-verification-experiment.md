# CTF PatchVerification 实验功能

## 发布边界

CTF PatchVerification 从 `0.2.1-alpha.1` 开始仅在实验分支提供。平台设置中的
`experimentalFeatures.ctfPatchVerificationEnabled` 默认是 `false`。开关只控制新内容的
创作、关联、发布和开赛门禁；排行榜始终依据题目定义和已持久化的 `GameplayFact` 投影，
不会因为平台开关变化而重算历史语义。

关闭开关时：

- Organizer 的题库列表隐藏 PatchVerification 模板，平台管理员仍可查看。
- 不能创建 PatchVerification 模板、关联到比赛、发布未发布实例，或启动含此类题目的比赛。
- 已经在 Running 或 Paused 比赛中发布的题目继续显示、验证和计分。
- 重新开启后，未开始比赛和题库中的内容恢复正常可见。

## 配置协议

`Direction` 继续描述 Web、Pwn、Crypto 等内容方向。`CtfInteractionKind` 单独描述完成方式：

- `FlagSubmission`：普通 Flag 提交。
- `PatchVerification`：上传修复包并在一次性靶机上验证。

CTF 题目定义使用 schema v3；v2 定义会自动补成 `FlagSubmission`。CTF 竞赛配置与题目
计分规则仍使用 schema v2。PatchVerification 定义复用 Runtime、Patch 和 Checker 原语，
默认最多提交 10 次，默认上传上限 64 MiB。上传硬上限、Patch 超时、Checker 超时和清理
预算与 AWDP 共用 `PatchVerificationExecutionBudget`，没有扩大 Runner 的执行时限。

PatchVerification 只接受 Container Runtime，使用 `PerTeam` 分配、`Static` FlagSource，且必须
声明一个 Checker 内部端口。定义不能包含 FlagTemplate、动态 Flag 注入或静态 Flag。
Flag 与 Patch 之间的切换只允许尚未被比赛引用，且没有 Runtime、GameplayFact 或 Flag 的模板。

## 执行与资源

选手使用常规 Player Runtime 分析题目。提交时平台创建新的
`RuntimePurpose.PatchVerificationTarget`，移除公网端口，在隔离网络中应用 Patch 并执行 Checker，
随后强制清理。每队每题最多一个活动验证靶机。

业务配额按不同 `CompetitionChallengeId` 计数。Player Runtime 和同题验证靶机合计一个题目
槽位；不同题目的验证靶机分别占槽。Runner 容量仍对每个物理 Runtime 分别扣减内存、CPU 和
PID，并在清理后分别归还。

归档格式会在创建 `GameplayFact` 前完成校验。非法归档不计次数、不扣分。Patch 仍可利用、
服务异常、Patch 命令失败或超时记为 `Wrong`，复用 CTF `WrongSubmissionPenalty`；存储、Runner、
回调或清理故障记为 `PlatformFailed`，不消耗次数也不扣分。

## 判定与计分

CTF PatchVerification 以 `GameplayFactKind.FixAttempt` 保存审计事实。第一次 `Correct` 在 CTF
排行榜中映射成 `Solve`，使用同一道题的动态分值曲线、血榜奖励和总排名；重复正确事实不会
产生第二次题目分。CTF 排行榜不显示 AWDP 的 Attack/Defense 分栏。

结果事务会同时保存事实、更新 Runtime 清理状态、发布实时失效消息并记录竞赛事件。客户端以
REST 状态为事实源，202 操作使用退避轮询，SignalR 只触发失效重取。

## API

参赛接口：

```text
GET  /api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/patch-verification
POST /api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/patch-verification-targets
POST /api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/patch-verification-targets/{runtimeInstanceId}/patch
```

Runner 回调：

```text
POST /api/internal/v1/patch-verification/results
```

回调结果为 `StillExploitable`、`Verified` 或 `ServiceAbnormal`。回调身份继续使用绑定
GameplayFact、Runtime、Runner 与 deadline 的内部 JWT。AWDP 既有公开路由和 durable 消息名
保持不变，通过共享解析器、目标工厂、预算和 Runner 执行器运行。

## 验收与性能

后端门禁包括 Release build、TUnit 单元与 Testcontainers 集成、EF model drift 和 OpenAPI
路由检查。前端门禁包括 SDK 二次生成幂等、typecheck、测试、production build 与
`audit:architecture`。

性能比较使用相同机器、SDK、ShortRun 参数和功耗模式。Flag-only CTF 的 Mean 与 Allocated
相对 `main@3ee07066c` 都不得回归超过 5%。另记录 16/64 队、12 题且 Flag/Patch 各半的混合
语料；基准结论使用完整统计结果，不使用单次最快值。
