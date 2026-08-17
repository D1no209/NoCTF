# AWDP

## 模式边界

AWDP 由攻击与防御两条独立事实流组成；是否允许未完成 Break 就提交 Fix，
由当前比赛题目的 `RequireBreakBeforeFix` 决定：

- 攻击轨 `BreakAttempt`：队伍启动自己的长期攻击 Runtime，利用漏洞取得该 Runtime generation 的动态 Flag；首次有效 Correct 从所属逻辑轮开始持续累计攻击分。
- 防御轨 `FixAttempt`：队伍上传不可变 `tar.gz` Patch，平台在全新的临时 Fix Target 中应用 Patch，并只运行一次 Checker；首次有效 Correct 从所属逻辑轮开始持续累计防御分。

AWDP 不是 AWD，不使用 `FlagAttempt`、`AwdRound`、加固期、周期服务上下线检查、批量提交其他队伍轮次 Flag 或 AWD 目标列表。AWDP 的 Checker 只属于一次 Fix 验证，不会周期运行，也不会修改队伍的长期攻击 Runtime。

## schema v2 配置

新建 AWDP 比赛、比赛题目规则和题目定义均使用 `schemaVersion: 2`。Competition 配置包含：

```text
RoundDurationSeconds: int > 0
Break.Points / Fix.Points: bigint >= 0       // 每个有效逻辑轮的分值
BreakWrongPenalty / FixFailurePenalty: bigint >= 0
ViolationPenalty / ServiceDownPenalty: bigint >= 0
MaxBreakSubmissions / MaxFixSubmissions: int // <= 0 表示无限
EvaluationDispatchMode: Automatic | ManualBatch
FlagTemplate: PerTeamFlagTemplate
```

`CompetitionChallenge.RulesJson` 可以逐项覆盖 Break/Fix 结算方式、分值、罚分、次数、
派发方式、`RequireBreakBeforeFix` 和 `FlagTemplate`。覆盖值 `0` 是显式零，只有
`null` 表示继承。不同比赛可以为同一题目使用不同 Flag 前缀与正文模板。

题库 `Challenge.DefinitionJson` 只保存可复用的技术定义：

```text
Runtime                        // 玩家攻击服务，Container、PerTeam
Runtime.Definition.FlagEnvironmentVariableName
PatchEntrypoint / PatchCommand / PatchTimeoutSeconds
Checker / ReadyTimeoutSeconds
MaximumPatchUploadBytes
```

题目定义不得保存 CompetitionId、TeamId、具体 Flag 或比赛专属 Flag 前缀。AWDP Runtime
固定使用 `Allocation=PerTeam`、`FlagSource=PerTeam`，并通过 Container 的
`FlagEnvironmentVariableName`（例如 `FLAG`）声明注入位置。它不使用 AWD 的
`AwdRotation`，也不另设一套 AWDP 专属 Flag 注入对象。

未知 schema、缺失 Runtime/Checker、公开端点无有效 OwnerOnly URL、非法环境变量名，
以及与模式不兼容的 Runtime 均在保存或 Start Gate 阶段拒绝。

## schema v1 历史兼容

`schemaVersion: 1` 仅用于读取既有比赛，不自动迁移，也不改写历史 GameplayFact、
CompetitionEvent 或排行榜事实。当前 schema 的 `Milestone | PerRound` 与
`RequireBreakBeforeFix` 均是有效产品配置；前者控制计分投影，后者控制 Fix 接入资格。

平台管理员可调用只读历史影响预览，比较 v1 当前分数与按 v2 持续计分规则推导的预期分数。预览只读取 PostgreSQL 一致快照，不写事实、不启用 v2、不更新排行榜；是否迁移指定历史比赛必须另行审批。

## 攻击 Runtime

玩家在题目页创建攻击实例。平台复用标准 `RuntimePurpose.Player` 创建队伍绑定、
对本队开放的长期 Runtime：

- 绑定 CompetitionId、CompetitionChallengeId、TeamId 和 Generation；
- 同队同题并发 Start 只得到一个活动实例，重复请求返回稳定当前实例；
- Start/Stop/Reset/Extend 使用普通玩家 Runtime 状态机；
- Reset 创建更高 Generation，旧实例进入清理并释放 Runner 容量；
- 公开端口仍由 Docker host port `0` 随机分配，URL 来自题目定义；
- Runtime 与后述临时 Fix Target 是不同用途、不同实例、不同网络和不同生命周期。

Runtime 进入创建流程时，平台在 PostgreSQL 临界区为 `(CompetitionChallengeId, TeamId, RuntimeInstanceId)` 幂等创建一条 `SpecificationKind.RuntimeGeneration` 的精确 Flag。相同 generation 的 Wolverine 重投复用同一条 Flag，不生成第二条有效 Flag。

Worker 在派发 Provider 创建请求前读取当前 generation 的 Flag 和最新有效题目配置：

- 将 Flag 合并进题目声明的环境变量，并覆盖镜像中的同名默认值；
- 只有 Provider Running 写回成功后才设置 `ValidStart`，防止未完成注入的 Flag 被判为有效；
- Stop、Reset、失败或过期会设置 `ValidUntil`，旧 generation 立即失效。

Flag 明文不得出现在 Runtime URL、普通 API 响应、前端状态、事件、通知、错误或结构化日志中。

## Break 判定

AWDP 只接受单个 `BreakAttempt`。v2 只匹配当前 CompetitionChallenge 的精确 `RuntimeGeneration` Flag：

- 当前队伍当前 generation 的有效 Flag：`Correct`；
- 普通错误 Flag：`Wrong`；
- 尚未完成注入、已停止或旧 generation Flag：`Wrong / FlagExpired`；
- 其他队伍的有效 Flag：内部 `Rejected / ForeignTeamFlagDetected`，创建工作人员可见的作弊事件；参赛者响应按受保护结果降级为 `Wrong`，不泄露归属；
- 同队重复提交同一有效 Flag，客观事实仍可保持 Correct，但投影只选 `(OccurredAt, GameplayFactId)` 最早的一条作为唯一攻击激活起点。

AWDP Break 不写 `GameplayFactReferenceKind.AwdRound`，也不创建 AWD 服务状态事实。

## Fix 与一次性验证

若 `RequireBreakBeforeFix=false`，Fix 可独立提交；开启后，后端必须在创建 FixAttempt
前确认当前仍存在有效 Correct Break。标准流程是：

1. 上传不可变 `tar.gz` PatchUpload；
2. 创建独立 `FixAttempt`；
3. 从题目干净镜像创建 `RuntimePurpose.AwdpTarget` 临时目标，TeamId 为空、无公开入口；
4. 安全解包并按 argv 形式执行 Patch；
5. Patch exit 0 后只启动一次 Checker；
6. Checker 通过认证 callback 返回结论；
7. 完成后停止 Checker、Target、隔离网络并释放容量。

Patch 默认入口为 `fix.sh`；不剥离顶层目录，入口必须在 archive 根下精确存在。`{entrypoint}` 只在独立 argv 中替换为 `/noctf/fix/<path>`，不得拼成 shell 文本。

Checker 获得 `TARGET_HOST`、`TARGET_READY_TIMEOUT_SECONDS`、`NOCTF_CALLBACK_URL` 和 `NOCTF_CALLBACK_TOKEN`，但不得获得 Patch 对象键、原文件名、动态 Flag、选手身份或长期攻击 Runtime。回调为：

```text
POST /api/internal/v1/awdp/fix-results
outcome: Fixed | StillVulnerable | RuleViolation | ServiceUnavailable
```

| outcome | Result / Failure |
|---|---|
| Fixed | Correct |
| StillVulnerable | Wrong / AwdpFixFailed |
| RuleViolation | Rejected / AwdpViolation |
| ServiceUnavailable | Wrong / AwdpServiceDown |

Checker exit 0 只表示进程正常结束；没有成功 callback 时绝不能推导为 Fixed。非零退出、超时、无 callback、Runner、Provider 或存储故障为 PlatformFailed/稳定平台失败，不得记为 Correct。管理员显式 Rejudge 才会重新创建一次干净验证环境。

## Fix 重放与 revision fence

Provider Running 后，Runner 在执行 Patch 前用 PostgreSQL 事务把 disposable target 推进到 execution fence。结果不确定的 Wolverine 重投不得在同一 Target 上再次执行非幂等 Patch：

1. 先推进 recovery fence，使旧 callback、超时和迟到结果失效；
2. 按 RuntimeInstanceId+Generation 精确清理 Target、Checker、网络和工作目录；
3. 确认 Provider 不存在该 identity，并完成 owner-checked capacity release；
4. 旧 Runtime 标为 Stopped，创建新 RuntimeInstanceId/更高 Generation；
5. 从原 GameplayFact 引用的同一不可变 PatchUpload 重放。

创建 Target 时固化 Competition configuration revision、CompetitionChallenge revision 和 Challenge definition revision。结果落库前任一 revision 变化都使该次验证 PlatformFailed；不得隐式拿新定义解释旧请求。

## 按轮持续计分

逻辑轮使用 Competition 生命周期事件计算的 EffectiveRunningTime：

```text
round(at) = floor(EffectiveRunningTimeAt(at) / RoundDurationSeconds) + 1
```

- Break 在轮 N 首次有效 Correct：从 N 到当前逻辑轮，每轮增加 `Break.Points`；
- Fix 在轮 M 首次有效 Correct：从 M 到当前逻辑轮，每轮增加 `Fix.Points`；
- 当前进行中的轮次立即计入；
- 两轨分数可同时激活并相加；启用 `RequireBreakBeforeFix` 后，Break 被重判为非
  Correct 时，依赖它的 Fix 暂停贡献，恢复 Correct 后再参与投影；
- 每个 Team/CompetitionChallenge/Kind 只选择最早有效 Correct，不建立重叠区间；
- Correct 被重判为非 Correct 时改选下一条有效 Correct；Wrong 被重判为 Correct 时按原 OccurredAt 重新计算起点；
- Pause 不增加 EffectiveRunningTime，因此不推进收益；Resume 从原逻辑时间继续；Finish 使用结束时点冻结；
- Break/Fix 失败罚分仍按每条事实一次性应用；Hint、ManualAdjustment、赛道过滤和 checked Int64 聚合保持原规则。

即使没有新 GameplayFact，轮次边界也会改变分数。singular maintenance 每 15 秒最多检查 500 场 Running AWDP v2 比赛，比较缓存快照 `DataAsOf` 对应轮次与当前逻辑轮；只有跨轮或缺失快照时设置 `LeaderboardDirty`。随后复用现有 `RefreshDirtyLeaderboards -> ProjectLeaderboard`、PostgreSQL 行锁、Wolverine Outbox 和原子缓存替换。没有新增 schedule 或积分状态表，也不会每秒写库。

## 玩家状态接口

题目页面通过单一强类型状态接口恢复：当前逻辑轮、攻击 Runtime、最近 Break、Break 激活轮次、最近 Fix 的 Patch/GameplayFact、TargetProvisioning/PatchApplying/CheckerRunning/Completed 阶段，以及 Fix 激活轮次。响应应用现有玩家结果脱敏，不返回动态 Flag。

前端固定展示“攻击”和“防御”两栏：攻击区管理 Runtime 并提交一个 Flag；防御区上传 Patch 和触发一次 Checker。页面不得展示 AWD 对手目标列表、批量 Flag、加固期或周期 Checker 文案。

## 排名

1. 总分降序；
2. 已激活 Fix 数降序；
3. 已激活 Break 数降序；
4. 累计罚分绝对值升序；
5. 最后一次有效 Fix 时间升序；无 Fix 为无穷大；
6. Team.RegisteredAt 升序；
7. TeamId 升序。
