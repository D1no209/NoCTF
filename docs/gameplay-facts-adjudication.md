# GameplayFact、判定与重判

## 单一当前事实

`GameplayFact` 同时表示客观比赛行为、当前处理状态、当前唯一结果和稳定失败原因。不存在 Submission、ScoringEvent、CurrentScoringEventId、ProcessingVersion、ClaimId、callback body hash、判定配置 revision 或判定历史表。

Kind：FlagAttempt、BreakAttempt、FixAttempt、HintUnlock、ManualAdjustment、AwdServiceTransition、KohControlObservation。状态流为：

```text
Pending/Queued -> Processing -> Completed
                         \-> PlatformFailed
```

首次平台失败保持 `Result=null`。重判开始只把 State 设为 Queued，旧 Result 继续供排行榜使用；成功时覆盖 Result/FailureCode，平台失败时保留已有 Result 并把 FailureCode 更新为最新平台原因。不复制事实，也不保留旧判定。

Flag 与 Break 保存原文 `Value` 和 32-byte SHA-256。Fix、Hint、AWD Round 通过同时为空或同时存在的 `(ReferenceKind, ReferenceId)` 关联；多态目标由 Application/Infrastructure 在事务内验证。

ManualAdjustment 由管理员创建即为 Completed/Applied，Value 是非零 canonical signed Int32。AWD 每次 Checker 执行先创建独立事实，健康和不健康分别收敛为 ServiceUp/ServiceDown；当前状态取 `(OccurredAt, Id)` 最大的已完成事实。KoH 每次完成的轮询都创建事实，Producer/Timeout/Ambiguous 记为 PlatformFailed。

## 接入与尝试

Flag、Break、Hint 先创建事实并通过 Wolverine Outbox 发布 `EvaluateGameplayFact(GameplayFactId)`。AWDP Fix 可在一次性 Target 为 Queued、Provisioning 或 Running 时，由唯一一次 Patch 上传创建 Pending FixAttempt；Target Running 后 Worker 将其推进为 Processing 并发布 `RunAwdpFixVerification`。消息和内部 JWT 不携带事实处理版本或 callback hash。批量领取按 `(TeamId, CompetitionChallengeId, Kind)` 和 `(OccurredAt, Id)`，使用短事务及 `FOR UPDATE SKIP LOCKED`；PlatformFailed 是否消耗尝试沿用各模式规则。

Fix 事务在同一个按 Team/题/Kind 串行的临界区内验证 Competition、CompetitionChallenge、Team、一次性 AwdpTarget 与上传者，锁定 Target/File，并创建相互绑定的 PatchUpload、FixAttempt 和 Outbox。RuntimeInstance 与 PatchUpload 的部分唯一索引、Target 上的 GameplayFactId 共同保证一个 Target 只能消费一次；PatchUpload 不保存 ConsumedAt，消费事实由 GameplayFact Reference 与 Target 绑定共同确定。

## 重判与作弊事件

管理端可批量 queue evaluation/rejudge 或精确重判一个 GameplayFact。Drain 消息分为 `DrainGameplayFactEvaluation` 与 `DrainGameplayFactRejudge`，每批最多 500 条。旧持久化消息不兼容。

ForeignTeamFlagDetected 作弊事件直接以 GameplayFactId 为身份；Confirm、Dismiss、Correct、Supersede CompetitionEvent 都引用该 ID。重判覆盖一个仍有未解决作弊事件的事实时写 CheatIncidentSuperseded，但不创建判定版本。

## API 可见性

玩家查询 `/competitions/{competitionId}/gameplay-facts` 及单项状态，只能读取自己 Team 的 FlagAttempt、BreakAttempt、FixAttempt、HintUnlock，且不返回 Value。系统事实、管理员事实及其他队事实不可见。

管理路由统一位于 `/admin/competitions/{competitionId}/gameplay-facts`，支持 Challenge、Team、VictimTeam、Actor、Kind、State、Result、FailureCode、时间、Value 精确匹配和 Reference 筛选；排序固定为 `(OccurredAt desc, Id desc)` 的 signed keyset pagination。

## 历史差异预览

工作人员可按 signed keyset cursor 读取有界、只读的裁决预览。CTF 分析当前正式比赛窗口内的
FlagAttempt 与 PatchVerification FixAttempt；AWDP 保留 BreakAttempt 的历史
Duplicate / DuplicateAchievement 异常识别。其他赛制不在此预览范围内。

事实、资格和事件在同一 RepeatableRead 快照读取。每次最多扫描 500 条事实，每条事实保留
最新 128 条相关事件并探测截断；事件保留 Id、时间、处理状态、结果、操作者和 ParentEventId，
按 `(OccurredAt, Id)` 稳定排列。稳定排序不代表同时间事件的业务因果顺序。
资格调整另保留最多 64 条并探测截断，两类前缀在同一次数据库读取中返回。
任一证据范围被截断，都标明证据不完整。

- 当前结果与可信的最新 Completed 业务裁决不一致，属于明确异常。
- 完整扫描范围内缺少必要裁决事件，属于完整性异常；截断不能证明事件不存在。
- `Correct → Wrong → Correct`、多次相同结果的合法重判、排队期间或平台失败后保留旧 Result，
  都不因历史存在多个结果而被判为损坏；合法变化作为信息，默认不单独列出。
- 同时间冲突、旧事件缺少状态、证据截断以及无法关联到具体裁决的重复播报，需要人工复核。
- 当前血位按共享资格、赛道、交互方式与首次完成顺序计算，单独标明为“当前投影血位”；
  它不能证明过去的资格或播报有误。已知赛道/资格调整作为调整说明，未知历史保持未知。
  正常不参与血榜且没有奖励或资格调整记录的事实，不因缺少历史资格证明而单独进入复核。
- 新血榜事件用 ParentEventId 指向对应裁决。只有同一已确认裁决的重复血榜子事件才有确定的
  重复证据；旧事件不回填关联，不重写历史。
  非空父关联必须属于同一 GameplayFact，类型为裁决、状态为 Completed、结果为 Correct。
  完整范围内的错误类型、跨事实或无效裁决关联是完整性异常；证据截断或必要字段缺失时标为证据不足。

预览不读取 Flag 原文，不排队评测、不改分、不补发血榜、不删除或修改事实与事件。
沿用工作人员授权和软删除比赛的归档只读权限；参赛者不可访问。

预览通过 `includeInformational=true` 展开合法变化，默认突出异常与待复核项。页面仅在用户
发起分析后加载预览；单事实的 `adjudication-events` 接口按需读取证据，默认 50 条、最多
100 条。签名游标绑定比赛、事实、调用者和内部赛道访问范围，不能跨范围复用。
Observer 不可通过预览或证据接口读取内部赛道事实；管理员及有内部审计权限的工作人员
沿用既有范围。接口仅提供事件摘要，不提供原始 Payload 或 Flag。
