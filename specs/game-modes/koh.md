# KoH

## 配置契约

Competition 提供 `PollIntervalSeconds > 0` 与 `ControlPointsPerInterval >= 0` 默认值；CompetitionChallenge 可分别 nullable 覆盖，0 Points 是显式 0。每题必须有一个 Container/OvaVm Hill RuntimeDefinition 和一个 `ControlCheckUrlBinding`。KoH 没有 Submission、EvaluationDispatchMode、最大尝试次数或 RoundDuration。

## 共享 Hill

每个已发布 CompetitionChallenge 有一个平台管理的共享 Hill Runtime，所有队攻击同一目标；不建 PerTeam Runtime、不接受 Submission。Running 自动启动，Paused 保持资源但停轮询，Finished 回收。

Hill 可使用 Container/OVA Runtime。公开 URL 与专用 `ControlCheckUrlBinding` 分开；Runner 在执行 Checker 时从 Provider Receipt 与最新题目定义解析受保护 Control URL，不把 URL 或数组下标持久化到 RuntimeInstance，也不向玩家返回。

## Control Flag

每个 Team/题固定一条 ChallengeFlag，Running Start Gate 前预生成；Running 中新批准 Team/新发布题增量生成。窗口无界，Competition 状态控制可见/计分。Team 详情只返回本队 Flag。

## 轮询协议

Worker 按 PollIntervalSeconds（Competition 默认、题覆盖）HTTP GET 展开的 Control URL：

- 仅 2xx 响应可解析；Body 是 Flag UTF-8 原文，不使用 JSON、不 Trim/normalize；
- Flag 1..4096 bytes、无 NUL，ordinal 匹配 challenge_flags；
- 每题最多一个 poll 在执行；timeout=min(interval,30s)；
- Running 执行；Paused 停止不补，Resume 立即一次；Finished 停止；
- OccurredAt=实际完成/失败服务端时间，不伪造计划时间。

每个已发布题只有一条 Wolverine scheduled poll 链：生命周期事务创建首条消息；Handler 完成观测并提交 KohControlObservation GameplayFact 时，在同一 Inbox/Outbox 事务安排下一条。消息携带计划 due time；Wolverine Inbox 以 MessageId 去重。若完成时已错过 interval，下一 due 推进到第一个未来边界，不补观测。每次实际完成轮询都创建事实，即使连续由同队控制。HTTP GET 发生在数据库事务外。

每次观测独立，不延续上一控制者：

| 状态 | GameplayFact |
|---|---|
| 唯一 Team Flag | KohObservation + Correct + TeamId |
| 2xx 无匹配/格式非法 | Wrong / Uncontrolled |
| 非2xx/连接/超时 | PlatformFailed + ProducerUnavailable/Timeout |
| 匹配不同 Team 多个 | PlatformFailed + AmbiguousFlagMatch |

只有 Correct 获得 `ControlPointsPerInterval`（非负，Competition 默认、题覆盖）。不补跑意味着缺失 poll 没有事件/分数。Ban Team 的事实被投影排除。

## 排名

1. 总分降序；
2. Controlled Observation 数降序；
3. 曾控制的不同题目数降序；
4. 首次成功控制时间升序；从未控制为无穷大；
5. Team.RegisteredAt 升序；
6. TeamId 升序。
