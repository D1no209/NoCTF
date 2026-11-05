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

集群调度器在 NATS KV 租约保护下，从关系型事实重建每题下一次观察时间。持久消息通过 JetStream 投递，处理前检查当前比赛和题目状态。Control 读取在数据库事务外执行，完成后创建 ControlObservation 及当前 Control 证据；观察事务提交后发布事件。下一次观察从已保存结果推导，不另外持久化递归调度链。每次完成的观察均保存事实，即使连续由同队控制。

每次观测独立，不延续上一控制者：

| 状态 | GameplayFact |
|---|---|
| 唯一有效 Team Flag | ControlObservation + Controlled + TeamId |
| 明确无人控制 | ControlObservation + Uncontrolled |
| 非2xx/连接/超时 | PlatformFailed + ProducerUnavailable/Timeout |
| 匹配不同 Team 多个 | PlatformFailed + AmbiguousFlagMatch |

只有有效 Controlled 观察获得 `ControlPointsPerInterval`（非负，Competition 默认、题覆盖）。无法确定时不延续上一次控制收益，保留失败原因。未发生的观察不产生事实或分数，封禁队伍的事实不参与收益。

## 排名

1. 总分降序；
2. Controlled Observation 数降序；
3. 曾控制的不同题目数降序；
4. 首次成功控制时间升序；从未控制为无穷大；
5. Team.RegisteredAt 升序；
6. TeamId 升序。
