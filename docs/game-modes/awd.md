# AWD

## 配置契约

Competition 必须配置 `HardeningDurationSeconds >= 0`、`RoundDurationSeconds > 0`，并提供以下比赛默认规则；CompetitionChallenge 的 `RulesJson` 可用 nullable 字段逐项覆盖：

```text
AttackRewardMode: FixedPerAttack | SplitVictimDefensePool
AttackPoints: bigint >= 0
VictimDefensePoolPoints: bigint >= 0
CheckerIntervalSeconds: int > 0
ServiceHealthyPoints: bigint >= 0
ServiceUnhealthyPenalty: bigint >= 0
```

`Challenge.DefinitionJson` 管理 Runtime、Checker、Flag 生成与注入：

```text
Runtime: Container | Compose
Checker:
  Job: RunnerJobConfiguration
  TargetServiceName: 仅 Compose 必填
FlagInjection:
  Command: 包含 ${FLAG} 的非空模板
  ServiceName: 仅 Compose 必填
```

FixedPerAttack 使用 AttackPoints；SplitVictimDefensePool 忽略 AttackPoints。两者都使用 VictimDefensePoolPoints。覆盖值 0 是显式 0，只有 null 继承。AWD 不使用 EvaluationDispatchMode/最大提交次数；Flag 接入总是 Automatic。

Competition 的可选 `FlagTemplate` 是动态轮换 Flag 的竞赛默认；Challenge.DefinitionJson 的可选
`FlagTemplate` 优先覆盖，未配置时使用平台默认 `flag{[TEAMHASH]}`。模板变更只影响后续尚未生成的
“队伍×题目×轮次”Flag；已经持久化的当轮事实保持原值并继续有效。静态和管理员手工 Flag 不读取
该模板。

Challenge 当前 schemaVersion 为 4，不提供旧 schema 兼容层。Challenge 不声明 Docker/Kubernetes Provider 或 RunnerPool。Checker 与长期 Runtime 由平台放入同一内部网络；平台只注入 `NOCTF_TARGET_HOST`。Container 使用固定 `target` DNS，Compose 使用 `TargetServiceName` 对应的服务 DNS。Checker 自己知道服务端口，不配置 Target URL 或 TargetPort，也不复用仅属于 KoH 的 `ControlCheckUrlBinding`。

## 时钟与加固期

Competition 配置 `HardeningDurationSeconds >= 0`、`RoundDurationSeconds > 0`。均使用 EffectiveRunningTime，Pause 冻结。

加固期内：

- 团队只能看到自己的 Runtime 地址/凭据，不能看到对手实例；
- 禁止攻击 FlagAttempt GameplayFact；
- 不轮换 AWD Flag，不计攻防/服务分；
- Checker 仍按 Interval 运行以建立真实状态。

加固期结束后，AWD targets API 才返回全部有效对手的公开 URL；只切换可见性，不重建 Runtime/URL。Ban/删除 Team 立即从 targets 消失。

首次配置下 Round 1 在加固结束时开始：

```text
round = floor((effectiveRunningTime - hardeningDuration) / roundDuration) + 1
```

Round 不建表。Round 1 生成后，ChallengeFlag 的 AwdRound Specification 与 ValidStart/ValidUntil 是已发生轮次的持久化时间线；下一轮编号始终是已生成最大编号+1。Paused/Resume 顺延当前 Flag.ValidUntil。Running 中修改 RoundDuration 从当前轮 ValidUntil 后的下一轮生效；修改 HardeningDuration 只在 Round 1 尚未生成时生效，不能重新开启加固期。EndAt 绝对结束；未自然完成的最后一轮不结算服务状态分，但结束前有效攻击仍计分。

## Runtime 与 Flag

系统为每个有效 Team/已发布 AWD 题自动保持一个 Container 或 Compose RuntimeInstance；OVA 因只允许固定 Flag 而禁止用于 AWD。Ban 回收，Unban 重新配置。加固期只隐藏对手地址，不停止实例。

每个 RoundStart 后，持 Competition advisory lock 的 Worker 在事务中：

1. 为全部 Team/题生成缺失的当轮 Flag；
2. SpecificationKind=AwdRound，窗口严格 RoundStart..RoundEnd；
3. Outbox 发布 Runner 注入任务。

不提前生成/注入；调度迟到仍使用原定窗口；重启只补当前轮，不补历史。注入按 raw `${FLAG}` Shell 模板，失败复用同一 Flag 重试到 ValidUntil。窗口不等待注入成功。

## 攻击 GameplayFact

AWD 可使用单 `flag` 或 `flags` 数组；无尝试上限。每个批量请求最多 64 项、Flag UTF-8 正文合计最多
256 KiB，超限在创建任何 GameplayFact 前原子拒绝。Worker 以原文、CompetitionChallenge、
ReceivedAt 窗口、AwdRound Specification 反查：

- ChallengeFlag.TeamId 是 VictimTeamId；
- 自己 TeamId -> Rejected/SelfAttackRejected；
- 当前窗口无匹配 -> Wrong/FlagNotMatched；只命中其他窗口 -> Wrong/FlagExpired；
- 匹配不同 Victim 多个 -> PlatformFailed/AmbiguousFlagMatch；
- 唯一匹配后完成同一 FlagAttempt GameplayFact，写入 VictimTeamId 与 AwdRound Reference。

攻击重复唯一维度：

```text
(CompetitionChallengeId, Round, AttackerTeamId, VictimTeamId)
```

同一攻击队对同一受害队同轮同题只首次为 Correct；之后为 Duplicate/DuplicateAttack。其他攻击队可各得一次。一次攻击只产生一条事件，投影器同时计算双方，不建 Victim 事件。

## 攻击与防守分

配置 `AttackRewardMode`：

### FixedPerAttack

每个首次有效攻击者 `+AttackPoints`。受害队在该题该轮只要至少被攻破一次，只扣一次 `VictimDefensePoolPoints`，不按攻击者重复扣。

### SplitVictimDefensePool

若 N 支不同攻击队首次攻破该受害队：

```text
each attacker += Floor(VictimDefensePoolPoints / N)
victim -= VictimDefensePoolPoints once
```

余数丢弃。N 因重判变化时自动重投影。未被攻破不扣、无人瓜分。两种模式下值非负，总分可负；Competition 默认、题目覆盖。

## Checker 状态机

每题配置 CheckerIntervalSeconds（Competition 默认、题覆盖，正整数）。Running 时持续检查，加固期也检查；Paused 停止且不补，Resume 立即一次；同队同题最多一个在执行，前次未结束则跳过 interval。

Checker 是附着到特定 Runtime Generation 的可信一次性 Container，与目标处在同一内部网络。单 Container 目标以 `target` 作为稳定 DNS，Compose 目标使用 service name；Checker 镜像自己知道目标端口，平台不向 Checker传递端口配置。后续 Runtime/Checker 定义变化只影响下一次 Start/Reset。

Docker Provider 从持久 receipt 与 ownership labels 解析该 Generation 的实际 Container/Compose 网络，不假设 Compose `_default` 网络；Checker 同时加入目标内部网络和隔离 callback 网络，结束时只清理自身与 callback 网络。Kubernetes Provider 复用同一 Runtime identity 与 NetworkPolicy，并只为带 `awd-checker` purpose 的 Pod 增加 callback egress，不把平台访问能力授予题目业务 Pod。

Checker 通过 JWT callback：

```text
POST /api/internal/v1/awd/check-results
body: { state: Up | Down }
```

JWT audience/permission/resource claims 固定。一次 Checker 执行可以多次调用，后一次状态覆盖前一次。服务端接收成功时间为 OccurredAt；恰好等于 RoundEnd 的变化从下一轮生效。

数据库只写状态变化：

```text
Up + Up: no event
Up + Down: AwdServiceStatus/Result.Wrong
Down + Down: no event
Down + Up: AwdServiceStatus/Result.Correct
```

正常退出且无 callback 为 `Unknown`；异常退出为 `CheckerAbnormalExit`；超时为 `CheckerTimedOut`。这些是 Checker 诊断状态，不由退出码推断服务 Up/Down，也不直接产生服务分。只有 Checker 主动回报的 Up/Down 参与服务状态计分。

每个完整轮次只看 `OccurredAt < RoundEnd` 的最新状态：Up `+ServiceHealthyPoints`，Down `-ServiceUnhealthyPenalty`。不按在线时长比例；状态跨轮持续；禁用 Checker 时两项为 0/无事件。

## 排名

1. 总分降序；
2. 攻击所得分降序；
3. 完整轮次 Up 的“题×轮次”数量降序；
4. 首次有效攻击数量降序；
5. 最后一次有效攻击时间升序；无攻击为无穷大；
6. Team.RegisteredAt 升序；
7. TeamId 升序。
