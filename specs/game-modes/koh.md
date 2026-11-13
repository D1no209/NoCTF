# KoH

## 配置契约

Competition 提供 `PollIntervalSeconds > 0` 与 `ControlPointsPerInterval >= 0` 默认值；CompetitionChallenge 可分别 nullable 覆盖，0 Points 是显式 0。每题必须有 Container Hill、Participants 访问入口和完整的 Control 容器配置。KoH 没有 Submission、EvaluationDispatchMode、最大尝试次数或 RoundDuration。

## 共享 Hill

每个已发布 CompetitionChallenge 有一个平台管理的共享 Hill Runtime，所有队攻击同一目标；不建 PerTeam Runtime、不接受 Submission。Running 自动启动，Paused 保持资源但停轮询，Finished 回收。

Control 定义包含镜像、有序命令、环境变量、命名目标服务和执行超时。Runner 从当前 Runtime Receipt 取得目标内部地址，执行一次性容器；容器自行采集和判定控制状态，不发布公开端口。

## Control Flag

每个 Team/题固定一条 ChallengeFlag，Running Start Gate 前预生成；Running 中新批准 Team/新发布题增量生成。窗口无界，Competition 状态控制可见/计分。Team 详情只返回本队 Flag。

## 轮询协议

Worker 按 PollIntervalSeconds（Competition 默认、题覆盖）向目标所属 Runner 派发类型化 Control 任务：

- 容器通过签名回调返回执行状态、控制状态、控制标识、稳定故障码和 `observedAt`；
- `Controlled` 必须携带 1–4096 个字符的非空标识且无 NUL；`Uncontrolled` 不携带标识；`Indeterminate` 必须报告平台故障码；
- 控制标识按当前比赛题目与有效期精确匹配 ChallengeFlag；未知标识记为无人控制，多个队伍匹配记为异常；
- 执行超时取配置预算与任务剩余期限中的较小值；
- Running 执行；Paused 停止不补，Resume 立即一次；Finished 停止；
- OccurredAt=实际完成/失败服务端时间，不伪造计划时间。

集群调度器在 NATS KV 租约保护下，从关系型事实重建每题下一次观察时间。持久消息通过 JetStream 投递，处理前检查当前比赛和题目状态。Control 执行在数据库事务外进行；回调绑定执行身份、Runtime UUID、Runner 和期限。相同回调幂等，冲突拒绝，旧 Runtime 的回调不能更新当前结果。有效回调、正常退出、辅助容器清理与容量释放共同完成观察；崩溃、超时、缺少回调和清理失败均保存明确平台故障。下一次观察从已保存结果推导，不另外持久化递归调度链。每次完成的观察均保存事实，即使连续由同队控制。

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
