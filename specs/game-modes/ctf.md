# CTF

## 配置契约

Competition 的强类型配置提供 `DefaultScoreCurve`、`WrongSubmissionPenalty` 和三个血位 Reward 默认值。CompetitionChallenge 的强类型规则可用 nullable `ScoreCurve` 整体覆盖，并另外定义：

```text
MaxFlagAttempts: int?                   // null 无限；配置时必须 >0
ScoreSettlementMode?                   // null 继承比赛；DynamicRecalculation 或 AtSolve
```

附件的 `All | RandomOnePerTeam` 由独立 `AttachmentDeliveryPolicy` 管理，不属于 `Rules`；CTF Flag 判定固定自动执行，不使用 `EvaluationDispatchMode`。

`ScoreCurve` 包含 `InitialPoints`、`MinimumPoints`、`DecayTeamCount`、`DecayMode` 和仅在 Custom 模式使用的 `CustomExpression`。数值约束：InitialPoints 为 1..1,000,000，MinimumPoints 为 0..InitialPoints，DecayTeamCount>1，Penalty/Reward 非负，百分比 0..100。覆盖值为 0 时就是显式 0，不表示继承；只有 null 表示继承。配置以强类型 TPH 聚合与关系子表持久化，不保存 schema-version JSON。Runtime 技术定义属于题库 Challenge，不属于比赛题目规则。

CTF 还可在平台实验开关允许时使用独立的 [PatchVerification](../ctf-patch-verification-experiment.md)
交互；下文 FlagAttempt 描述适用于 Flag 解题，Patch 模式以 FixAttempt 的当前结果判定完成。

## 题目形态

CTF CompetitionChallenge 可为：

- Static：题面、Attachment、外部链接；
- PerTeamRuntime：Container 命名服务或 OVA；按队按需启动，没有多阶段语义。

Container Runtime 使用 `FlagSource=PerTeam`，至少选择一个命名服务的 `FlagEnvironmentVariableName`，平台在创建 Runtime 时注入固定队伍 Flag。OVA 使用预置静态 Flag，不由平台注入；无 Runtime 的静态题可使用管理员维护的精确或正则 Flag。命名服务契约见 [Runtime services](../runtime-services.md)。

Competition 可用 `FlagTemplate` 配置动态 Flag 的默认 Header、BodyTemplate 与字面文本 leet；
CompetitionChallenge.Rules 可用同名字段为本场比赛的该题覆盖。优先级为比赛题目规则、
竞赛默认、平台默认 `flag{[TEAMHASH]}`。该配置只在缺失的每队容器 Flag 首次生成时读取：已经持久化的
Flag 不轮换，Reset/重启继续复用原值；静态题和管理员维护的手工 Flag 完全不受影响。
Challenge.Definition 只保存 Runtime 与注入目标，不保存动态 Flag 前缀或正文模板。

## Flag 与附件

无 Runtime 的静态题可有多个正确 Flag。模板级静态 Flag 用 ChallengeId；比赛/团队 Runtime Flag 用 CompetitionChallengeId。动态 Runtime 判题只接受当前题目生成的 Team + RuntimeDefinition Flag，不混入模板或手工静态 Flag。

Attachment 策略：

- All 默认：显示全部 Attachment；全局符合条件 Flag 均可正确。
- RandomOnePerTeam：只用于多附件；首次下载隐式随机固定一个 Attachment，并复制其全部 Flag 为团队级记录。玩家不能指定 Id，后续不重抽。

## FlagAttempt GameplayFact

只接受单个 `flag`，不接受 `flags`。题目可配置可空的 `MaxFlagAttempts`；null 表示无限，正整数表示接入上限。API 预检并由 Worker 二次验证。

同队同题按 OccurredAt、GameplayFactId 的第一条当前 Correct FlagAttempt 是唯一计分 solve；后续 FlagAttempt 仍按当前有效 Flag 正常判定，匹配为 Correct，不匹配为 Wrong，但不会重复计分或授予血位。尝试计数按当前结果与时间资格计算；平台失败和时间无效事实不消耗，计分截止后的正确结果可为 RightButDue。具体回算行为见[题目时间配置](../challenge-timing-status.md)。重判可改变 solve 与血位。

## 动态题值

CTF 题值完全来自有效 `ScoreCurve`。内置 Fixed、Linear、Quadratic、Exponential、Logarithmic 五种模式，也可以使用受限的 DynamicExpresso 自定义公式。变量、取整、安全与错误行为见 [计分规范](../scoring-projection.md#共享分值衰减曲线)。

`DynamicRecalculation` 使有效 solve 共享当前题值；`AtSolve` 按有效完成顺序计算各自题值，新增 solve 不降低先前得分。两者都从当前事实重建，配置、资格或重判仍可改变历史分数；详见[CTF 计分结算](../ctf-score-settlement.md)。配置变更立即失效排行榜缓存并触发事件驱动全量投影，但不重判 GameplayFact；不存在业务 Dirty 字段或定时脏扫描。

## 血奖

一血、二血、三血按 OccurredAt+GameplayFactId；Ban/删除队伍排除。`BloodRewardPolicy`：`FixedPoints`、`InitialPointsPercentage`、`SolveTimePointsPercentage`、`CurrentPointsPercentage`。百分比 0..100、奖励非负；两种题值百分比策略分别使用该血位解出时题值和当前题值。配置或重判时重新投影。

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
