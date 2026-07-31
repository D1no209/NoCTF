# 计分与排行榜投影

## 原则

ScoringEvent 只保存事实，绝不保存分数。排行榜使用当前 Competition/CompetitionChallenge/Hint 配置，每次从当前未删除事件重算。所有配置在 Draft/Visible/Published/Running/Paused/Finished 都可修改；保存后自动递增 LeaderboardRevision，不自动重判。

配置修改的生效边界固定为：

- Points、Penalty、DynamicExpresso、血奖、Hint Cost、AWDP Achievement settlement/RoundDuration、排名可见性等投影参数立即对全部当前事件重新计算；不保存旧值，也不冻结 Finished 榜。AWDP 轮次按当前配置和生命周期审计重分组。
- Flag、次数、Evaluator/Checker、Runtime/Archive 规则只用于之后的接入/判定/新 Runtime；既有 ScoringEvent 仍是事实，必须由管理者显式重判才改变。
- 正在 Running 的 Runtime 不热改镜像/资源/URL/环境；Challenge 定义修改只用于下一次 Start/Reset，不记录定义版本，也不自动更新现有 Generation。AWD Checker interval/definition 从下一次尚未开始的检查生效。
- 已生成的 PerTeam/RandomOne/AWD Flag 不因模板变化自动替换；具体规则见 Flag 文档。修改 AWD RoundDuration/HardeningDuration 不改写已生成 Flag 的 ValidStart/ValidUntil，也不重编号已产生事件；当前 AWD 轮完成后再按新 duration 生成下一轮。

因此 `configuration_revision` 记录某次判定使用的配置版本只用于诊断和重判选择，不用于恢复旧分值；投影永远读取当前投影参数。

封禁/软删除 Team 不进入榜单；其事实、针对它的攻防事实均从当前投影排除。Unban 恢复未删除事实。

## DynamicExpresso CTF 衰减

Competition 默认表达式，题目可完整覆盖表达式和 DecayParameter；InitialPoints 永远来自 CompetitionChallenge.BaseScore。

固定变量：

```text
initialPoints      decimal
minimumPoints      decimal
solveCount         int
eligibleTeamCount  int
decayParameter     decimal
```

默认：

```csharp
solveCount <= 1
    ? initialPoints
    : solveCount >= decayParameter
        ? minimumPoints
        : initialPoints
          + (minimumPoints - initialPoints)
          * ((solveCount - 1m) / (decayParameter - 1m))
          * ((solveCount - 1m) / (decayParameter - 1m))
```

约束：BaseScore>0；MinimumPoints 在 0..BaseScore；DecayParameter>1。表达式 UTF-8 长度必须为 1..4096 bytes，大小写敏感，保存时必须编译成功；空白不同视为不同表达式。

### DynamicExpresso 实现契约

使用 `DynamicExpresso.Core`，每次构造只读 Interpreter 模板：

```csharp
new Interpreter(InterpreterOptions.Default)
    .EnableAssignment(AssignmentOperators.None)
    .SetDefaultNumberType(DefaultNumberType.Decimal);
```

- 绝不调用 `EnableReflection()`。DynamicExpresso 默认的 Reflection 阻断必须保持；禁止通过 fork、wrapper 或自定义 member resolver 绕过。
- 不启用 `LambdaExpressions`、`LateBindObject` 或 `CaseInsensitive`；不调用 `Reference()` 引入额外程序集/业务类型。
- 不注入 `this`、object/dynamic、array、DbContext、服务、实体、队伍、时间或随机；只以强类型 `Parameter` 注入上列五个 decimal/int 标量。
- 不做逐函数白名单；`InterpreterOptions.Default` 自带的 primitive/system keyword/common type 能力可用，但 assignment 与 Reflection 始终不可用。表达式不能产生外部副作用。
- 使用上述固定 Parameter 名称和类型 Parse 为 decimal 返回值；未知 identifier、错误大小写、非 decimal 可转换结果、parse/overflow/divide-by-zero 均失败。
- 编译结果按 `SHA256(UTF8(expression))` 存入进程内有界 1024 项 LRU；缓存只保存不可变 Lambda，可并发 Invoke，不保存输入或输出。配置 revision 仍是投影一致性来源，缓存不是业务事实。
- 保存配置时至少用边界样例 `(solveCount=0,1,eligibleTeamCount)` 编译并调用；这不保证所有运行输入成功。实际投影每次仍捕获 DynamicExpresso/算术异常。

表达式作者是受信任管理者。这里不采用严格函数白名单，但“可修改表达式”不等于“可执行任意 .NET”：Reflection、assignment、额外程序集和复杂对象注入均被永久禁止。

返回 decimal，先截断到 [MinimumPoints,InitialPoints]，再以 `decimal.Round(value, 0, MidpointRounding.AwayFromZero)` 转为 checked bigint；溢出使投影失败。所有有效解题队伍共享当前 solveCount 的题值。`solveCount` 是排除 Ban/软删除队伍后当前 Correct solve 的不同 Team 数，`eligibleTeamCount` 是当前 Approved、未 Ban、未删除 Team 数；两者均可为 0。

表达式运行失败时整次投影失败：保留上一 Redis 快照；API 标 stale、旧/目标 revision 与失败时间；不广播错误榜；管理者收到通知；Wolverine 重试后 DLQ。

## CTF 血奖

一血/二血/三血按有效 Correct solve 的 ReceivedAt、SubmissionId 排序。配置 RewardKind：FixedPoints、InitialPointsPercentage、SolveValuePercentage。百分比 0..100，奖励非负。

SolveValuePercentage 以该血位 solveCount 调当前表达式。百分比奖励=`decimal basis * percentage / 100` 后用 MidpointRounding.AwayFromZero 取整。血奖一旦按血位计算，不随以后 solveCount 衰减；配置/重判会重新确定血位和数额。

## 数值规则

所有持久化 Points/Penalty/Cost 与最终分数是 signed bigint；配置要求非负不代表团队总分不能为负。投影用 checked `Int64` 加减，DynamicExpresso/百分比中间值用 decimal。除 AWD Split 明确使用 Floor 外，decimal 到 bigint 一律 `MidpointRounding.AwayFromZero`；任何 decimal/Int64 overflow 使整次 Competition 投影失败并保留旧快照，不能饱和、绕回或跳过单队。

## Wrong/Hint

CTF WrongSubmissionPenalty 只扣当前结果 Wrong；Duplicate、AttemptsExhausted、FlagExpired、PlatformFailed 与接入前失败不扣。AWDP 的罚分见模式文档。总分允许为负。

HintUnlock 以当前 Hint.Cost 扣分。解锁前在 Team/Competition 锁内用数据库事实即时计算权威当前总分，必须 >= Cost；不得读取 Redis 旧榜。已存在当前 HintUnlock 时直接返回已解锁且不写第二条事件；Cost=0 且已发布的 Hint 直接可见且不写事件。解锁后 Cost 修改会按当前值重新投影，配置/重判导致负分也不撤销 Hint。

## 模式投影

- CTF：当前题值 + 血奖 - Wrong - Hint。
- AWD：攻击奖励、受害一次防守扣分、完整轮次末服务状态分 - Hint。
- AWDP：Break/Fix 成就 - Fix/Violation/ServiceDown/BreakWrong - Hint。
- KoH：每个 Controlled Observation 的 ControlPointsPerInterval - Hint。

具体公式与 tie-break 见各模式文件。

## 按需 Redis 快照

每个影响排名的事务只在数据库内原子执行 `LeaderboardRevision++` 并发布 invalidation。
不同 Submission、Runtime、Team、KoH observation 与生命周期事务可以同时影响同一
Competition；禁止用 tracked entity 的客户端 `R -> R+1` 写回，否则旧 Redis 快照可能与
数据库 target revision 相等而被误报为 fresh。

- Redis 维护活跃 leaderboard subscriber TTL。
- 有订阅者时 Worker 合并 invalidation，投影到最新 revision。
- 无订阅者只标 dirty，不重算全量。
- 首次 GET/订阅触发投影；有旧快照则立即返回旧数据并 stale=true；无缓存则 202+status URL。
- 成功后原子替换快照、广播 revision。
- 排行榜 cursor 不绑定 revision；每页读最新快照并携带当前 revision。

status URL 就是同一个 leaderboard GET，不建立 ProjectionOperation 资源。无快照且正在投影时返回 202、targetRevision 与 Retry-After；投影最终失败且仍无快照时返回 503 `LeaderboardProjectionFailed`。有旧快照时即使新投影失败也返回 200 旧页，携带 `stale=true`、snapshotRevision、targetRevision、lastFailureAt。分页过程中 revision 改变可能出现跨页移动，客户端要求严格一致视图时自行从第一页重读；服务端不因 revision 变化拒绝 cursor。

数据库不保存排行榜/轮次分数表。Redis 丢失后按需重建。

## 公共响应

LeaderboardVisibility：Public、ParticipantsOnly、ManagersOnly。Draft 永远仅管理者。不存在冻结快照模式；Finished 后配置和事实仍可重判/重投影。
