# CTF

## 配置契约

Competition 配置提供 `MinimumPoints`、`DecayParameter`、`DecayExpression`、`WrongSubmissionPenalty` 和三个血位 Reward 默认值。CompetitionChallenge 配置可用 nullable 字段逐项覆盖，并另外必须定义：

```text
FlagSelectionPolicy: All | RandomOnePerTeam
EvaluationDispatchMode: Automatic | ManualBatch
MaxFlagSubmissions: int                 // <=0 无限
Runtime?                               // 按队 Runtime 配置
Runtime.FlagSource: Static | PerTeam
```

数值约束：MinimumPoints 0..BaseScore，DecayParameter>1，Penalty/Reward 非负，百分比 0..100。覆盖值为 0 时就是显式 0，不表示继承；只有 null 表示继承。所有配置对象带 `schemaVersion`，未知版本拒绝保存。

## 题目形态

CTF CompetitionChallenge 可为：

- Static：题面、Attachment、外部链接；
- PerTeamRuntime：Container、Compose 或 OVA；按队按需启动，没有多阶段语义。

Runtime.FlagSource：Static 或 PerTeam。OVA 只能 Static；Container PerTeam 使用
`FlagEnvironmentVariableName`，Compose PerTeam 使用
`FlagEnvironmentVariables[serviceName]`，在创建时注入固定队伍 Flag。

Competition 可用 `FlagTemplate` 配置动态 Flag 的默认 Header、BodyTemplate 与字面文本 leet；
Challenge.DefinitionJson 可在 `Runtime.FlagSource=PerTeam` 时用同名字段覆盖。优先级为题目模板、
竞赛默认、平台默认 `flag{[TEAMHASH]}`。该配置只在缺失的每队容器 Flag 首次生成时读取：已经持久化的
Flag 不轮换，Reset/重启继续复用原值；静态题和管理员维护的手工 Flag 完全不受影响。

## Flag 与附件

一题可有多个正确 Flag。模板级静态 Flag 用 ChallengeId；比赛/团队 Runtime Flag 用 CompetitionChallengeId。任一符合 scope、Team、Specification 与 ReceivedAt 时间窗的 Flag 可正确。

Attachment 策略：

- All 默认：显示全部 Attachment；全局符合条件 Flag 均可正确。
- RandomOnePerTeam：只用于多附件；首次下载隐式随机固定一个 Attachment，并复制其全部 Flag 为团队级记录。玩家不能指定 Id，后续不重抽。

## Submission

只接受单个 `flag`，不接受 `flags`。题目配置 `EvaluationDispatchMode` 与 `MaxFlagSubmissions`；<=0 无限。API 预检+Worker 二次验证。

同队同题按 ReceivedAt、SubmissionId 的第一条当前 Correct 是 solve；后来匹配正确答案的 Submission 为 Duplicate/DuplicateSolve。没有当前有效匹配为 Wrong/FlagNotMatched，只命中过期窗口为 Wrong/FlagExpired；判定只加载通用 Flag 与本队 Flag，其他队同值不会参与。Wrong/Duplicate 都消耗已接收尝试；平台失败不消耗。重判可改变 solve 与血位。

## 动态题值

InitialPoints=CompetitionChallenge.BaseScore。Competition 提供 MinimumPoints、DecayParameter、DynamicExpresso 表达式，题目可覆盖 Minimum/Parameter/表达式。表达式变量、默认公式、安全与错误行为见 [计分规范](../scoring-projection.md#dynamicexpresso-ctf-衰减)。

当前所有有效 solve 共享同一当前题值。配置变更立即使榜单 dirty，但不重判 Submission。

## 血奖

一血、二血、三血按 ReceivedAt+SubmissionId；Ban/删除队伍排除。RewardKind：FixedPoints、InitialPointsPercentage、SolveValuePercentage。百分比 0..100、奖励非负。SolveValuePercentage 以血位 solveCount 调表达式；奖励不随以后人数变化，但配置/重判重新投影。

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

按需 Start、每队同题一个当前实例；TTL、十分钟续期、Stop/Reset/容量与 URL 依 [Runtime](../runtime.md)。Start 前保证固定 PerTeam ChallengeFlag 存在；失败不删除 Flag，任何 Generation 复用。
