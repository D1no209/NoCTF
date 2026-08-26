# CTF

## 配置契约

Competition 配置 schema v2 提供 `DefaultScoreCurve`、`WrongSubmissionPenalty` 和三个血位 Reward 默认值。CompetitionChallenge 配置 schema v2 可用 nullable `ScoreCurve` 整体覆盖，并另外定义：

```text
FlagSelectionPolicy: All | RandomOnePerTeam
EvaluationDispatchMode: Automatic | ManualBatch
MaxFlagSubmissions: int                 // <=0 无限
Runtime?                               // 按队 Runtime 配置
Runtime.FlagSource: PerTeam
```

`ScoreCurve` 包含 `InitialPoints`、`MinimumPoints`、`DecayTeamCount`、`DecayMode` 和仅在 Custom 模式使用的 `CustomExpression`。数值约束：InitialPoints 为 1..1,000,000，MinimumPoints 为 0..InitialPoints，DecayTeamCount>1，Penalty/Reward 非负，百分比 0..100。覆盖值为 0 时就是显式 0，不表示继承；只有 null 表示继承。所有配置对象带 `schemaVersion`，未知版本拒绝保存。

## 题目形态

CTF CompetitionChallenge 可为：

- Static：题面、Attachment、外部链接；
- PerTeamRuntime：Container、Compose 或 OVA；按队按需启动，没有多阶段语义。

只要配置 Runtime，`Runtime.FlagSource` 就必须为 PerTeam。Container 使用
`FlagEnvironmentVariableName`，Compose 使用 `FlagEnvironmentVariables[serviceName]`，平台在创建 Runtime
时注入固定队伍 Flag。无 Runtime 的静态题才使用管理员维护的精确或正则 Flag。

Competition 可用 `FlagTemplate` 配置动态 Flag 的默认 Header、BodyTemplate 与字面文本 leet；
CompetitionChallenge.RulesJson 可用同名字段为本场比赛的该题覆盖。优先级为比赛题目规则、
竞赛默认、平台默认 `flag{[TEAMHASH]}`。该配置只在缺失的每队容器 Flag 首次生成时读取：已经持久化的
Flag 不轮换，Reset/重启继续复用原值；静态题和管理员维护的手工 Flag 完全不受影响。
Challenge.DefinitionJson 只保存 Runtime 与注入目标，不保存动态 Flag 前缀或正文模板。

## Flag 与附件

无 Runtime 的静态题可有多个正确 Flag。模板级静态 Flag 用 ChallengeId；比赛/团队 Runtime Flag 用 CompetitionChallengeId。动态 Runtime 判题只接受当前题目生成的 Team + RuntimeDefinition Flag，不混入模板或手工静态 Flag。

Attachment 策略：

- All 默认：显示全部 Attachment；全局符合条件 Flag 均可正确。
- RandomOnePerTeam：只用于多附件；首次下载隐式随机固定一个 Attachment，并复制其全部 Flag 为团队级记录。玩家不能指定 Id，后续不重抽。

## FlagAttempt GameplayFact

只接受单个 `flag`，不接受 `flags`。题目配置 `EvaluationDispatchMode` 与 `MaxFlagSubmissions`；<=0 无限。API 预检+Worker 二次验证。

同队同题按 OccurredAt、GameplayFactId 的第一条当前 Correct FlagAttempt 是 solve；后来匹配正确答案的事实为 Duplicate。没有当前有效匹配为 Wrong，只命中过期窗口记录稳定 FailureCode；Wrong/Duplicate 都消耗已接收尝试，平台失败不消耗。重判可改变 solve 与血位。

## 动态题值

CTF 题值完全来自有效 `ScoreCurve`。内置 Fixed、Linear、Quadratic、Exponential、Logarithmic 五种模式，也可以使用受限的 DynamicExpresso 自定义公式。变量、取整、安全与错误行为见 [计分规范](../scoring-projection.md#共享分值衰减曲线)。

当前所有有效 solve 共享同一当前题值。配置变更立即使榜单 dirty，但不重判 GameplayFact。

## 血奖

一血、二血、三血按 OccurredAt+GameplayFactId；Ban/删除队伍排除。RewardKind：FixedPoints、InitialPointsPercentage、SolveValuePercentage。百分比 0..100、奖励非负。SolveValuePercentage 以血位 solveCount 调表达式；奖励不随以后人数变化，但配置/重判重新投影。

排行榜公开每题前三血，而不是只公开一血：顶层 `bloods[]` 的每项带
`First/Second/Third` 强类型血位（OpenAPI 数值 1/2/3），队伍 slot 同步带血位与时间。
同一队伍重判产生的多条 Correct 只占其最早血位。

## Wrong 与 Hint

Competition `WrongSubmissionPenalty`，题目可覆盖，非负。只扣当前 Wrong；其他结果不扣。HintUnlock 按当前 Cost 扣分且解锁前必须有足够权威总分。总分可因后续配置/重判变负。

## 排名

1. 总分降序；
2. 最后一次有效解题时间升序；无 solve 视为无穷大；
3. 有效解题数降序；
4. Team.RegisteredAt 升序；
5. TeamId 升序。

## PerTeamRuntime

按需 Start、每队同题一个当前实例；TTL、十分钟续期、Stop/Reset/容量与 URL 依 [Runtime](../runtime.md)。Start 前保证固定 PerTeam ChallengeFlag 存在；失败不删除 Flag，后续新 UUID Runtime 可复用。
