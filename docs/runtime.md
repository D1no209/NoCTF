# Runtime 规范

本文定义当前强类型关系模型下的 Runtime 语义。旧版数据模型与消息传输规范仅作为历史记录，
不得为其增加兼容字段。

## 题目定义与平台放置

题库 `Challenge` 只声明 provider-neutral 的技术定义：镜像或 Compose 内容、命令、环境、
逻辑端点、资源需求、Checker 与 Flag 注入位置。题目和比赛均不选择 Provider、RunnerPool
或具体 Runner。

部署在平台配置中选择唯一活动的容器 Provider（Docker 或 Kubernetes）和可用 Runner。
Worker 根据平台事实选择具体 `RunnerId`，随后把消息投递到该节点的 JetStream subject。
Runner 不从共享 pool queue 中竞争任务；节点所有权由 NATS KV 租约保护。

公开 Docker 端口必须使用 host port `0`，由 Docker 随机分配。禁止为容器 Runtime 增加
HAProxy 或入口代理。

## 最小持久化模型

容量恢复扩展见 [Runtime capacity contract](runtime-capacity.md)：关系化 Allocation 和 Ledger 保存
活动工作负载分配、所属资源域、Runner 及额度；清理后删除对应分配。
这不是题目定义快照、工作流阶段、调度游标或历史操作列表。

`runtime_instances` 只保存恢复和清理外部资源所需事实：

- 比赛/练习实例保存 Competition、CompetitionChallenge、Team 和可选 GameplayFact 关系；题目测试实例改用互斥的 Challenge 关系；
- Runtime kind、purpose、provider、`RunnerId`；
- 状态、稳定失败码、类型化 provider receipt；
- 关系化访问端点与已发布端口；
- 创建、开始、到期、停止和更新时间。

以下内容不得持久化：

- generation、replacement 链或旧 Runtime 关系；
- Runner pool、容量释放 token 或 unavailable 标志；
- 题目定义 snapshot、source revision 或 participant URL indexes；
- ProcessingVersion、checker sequence、next-run、deadline、host/status 等调度游标；
- 控制面临时 URL、AWDP Fix stage 或等价的隐式工作流状态。

访问 URL 的可见性在读取时根据当前定义和调用者权限过滤；Checker target 从 provider receipt
和当前技术定义推导。不得为读取便利重新加入冗余列。

## 身份与生命周期

每次 Start 或 Reset 都创建全新的 Runtime UUID。Reset 的事务边界是：请求旧 Runtime 停止，
并创建新的 `Queued` Runtime；新旧实例没有持久化 replacement/generation 关系。旧 UUID 的
迟到消息只能作用于旧实例，不能覆盖新实例。

题目技术定义没有 Runtime 快照。为保证运行中实例不会读到半途修改的 Runtime、Checker 或 Flag
注入定义，只要该模板仍有 Queued、Provisioning、Running 或 Stopping Runtime，技术定义更新就返回
`ActiveRuntimeDefinitionConflict`；标题、题面、方向和可见性等元数据仍可独立更新。停止全部相关实例后，
新定义对之后的 Start/Reset 生效。

```text
Queued -> Provisioning -> Running -> Stopping -> Stopped
   |           |            |           |
   +-----------+------------+-----------+-> Failed
```

- Start：通过活动 Runtime slot、普通唯一约束及 Serializable 事务保证同一作用域只有一个活动实例。
- Stop：Queued 可直接收敛为 Stopped；已分配外部资源的实例进入 Stopping 并直投原 Runner。
- Reset：停止旧 UUID、创建新 UUID；不得复用旧 ID。
- Extend：只更新当前 Running 实例的 `ExpiresAt`。到期 Handler 重新读取事实；若当前尚未到期，
  即视为旧消息并安全返回。
- Failed：失败事实保留；若 receipt 表明可能存在资源，仍须由原 Runner 幂等清理。

JetStream 至少一次投递的重投由业务唯一约束、状态检查和当前 `ConcurrencyStamp` 幂等处理。
有限时间的 `Nats-Msg-Id` 去重只是辅助，不能替代业务幂等或并发校验。

## 创建、停止与恢复

Worker 创建 Runtime 时：

1. 在业务事务中创建 `Queued` 事实，成功提交后发布 JetStream 调度消息；若提交后发布前崩溃，由当前 Pending 状态重新派发；
2. 选择具体 Runner，持久化 `RunnerId`；
3. 直投该 Runner 的命名 JetStream subject；
4. Runner 幂等 claim 容量并创建 provider resource；
5. receipt 先写回数据库，再把实例推进为 Running；
6. 任何失败都保留足以对账和清理的 provider/runner/receipt 事实。

Stop/cleanup 始终以 Runtime UUID、Runner、Provider 和 receipt 为依据。即使 receipt 为空，
Runner 也必须按 Runtime UUID/平台标签检查节点资源，确认不存在后才释放容量并回写 Stopped。
清理动作必须可重复执行；关系数据库或 NATS KV 不可用时不得猜测资源不存在。

周期调度的 NATS KV Leader 从关系数据库扫描需要恢复的业务事实并派发消息，不直接操作
Provider。接管者从当前事实重建调度；不保存业务 next-run 或依赖旧消息补跑停机窗口。

## 模式用途

| 模式 | Purpose | 所有权 | 生命周期 |
|---|---|---|---|
| CTF | Player | Team | 选手按需 Start/Stop/Reset/Extend |
| AWD | Player | Team | 比赛生命周期自动创建；选手按规则 Reset |
| AWDP | Player/AwdpAttack | Team | 长期攻击实例，可 Start/Stop/Reset/Extend |
| AWDP | AwdpTarget | Team；上传后 + GameplayFact | 一次性 Fix 验证，完成/失败/超时即清理 |
| KoH | Shared | CompetitionChallenge | 工作人员控制，参赛者共享 |
| 题库测试 | TemplateTest | Challenge | Owner/Manager/Admin 验证真实 Runtime、动态 Flag 与访问入口；停止后保留节点镜像缓存 |

只有真正的 Shared allocation 才显示“共享”。AWDP Player 必须绑定真实 Team；AwdpTarget 在
管理端显示“一次性 Fix 验证 Target”。申请后先通过 Team 展示归属，Patch 成功绑定后再通过
GameplayFact/PatchUpload 展示评测来源。TemplateTest 不绑定 Competition、CompetitionChallenge、
Team 或 GameplayFact。

KoH Worker 的 Control 检查通过该 Runtime 自身的 Docker 随机发布端口访问，
因此部署的 `Runtime:Docker:PublicHost` 必须同时能从 Worker 容器解析和连接；
它不依赖 Worker 加入 Runner 创建的隔离网络，也不新增入口代理。

## CTF 与 TTL

CTF 队伍 Runtime 不预创建。首次 Start 创建新的 UUID，团队成员共享。进入 Running 后设置
`ExpiresAt`；Extend 更新到期时间，旧到期消息在重新读取时间后自然失效。Stop/Reset 会销毁
现有资源；正确 Flag 后按比赛规则派发 Stop，但后续提交仍可只做正确性判断，不能再次计分、
创建作弊事实或改变状态。

队伍并发额度统计活动 Runtime。Reset 的新旧实例在短暂交叠时按同一用户动作处理，避免错误
占用两个长期配额；该规则通过关系约束和 Serializable 事务实现，不增加 replacement 字段。

## 动态 Flag

动态 Flag 绑定具体 Runtime UUID。创建 Runtime 时，在关系数据库事务中幂等创建或绑定对应
`ChallengeFlag`，Worker 在 provider 请求中注入题目声明的环境变量或文件位置。只有 Runtime
Running 后 Flag 才有效；Stop、Reset、失败或到期使旧 Runtime UUID 的 Flag 失效。

Runtime Reset 生成新 UUID 和新动态 Flag。明文 Flag 不得进入 URL、普通响应、事件、通知、
Prometheus 标签或结构化日志。

TemplateTest 使用独立测试 Flag，不是比赛 Flag。它只可由题目 Owner/Manager 或平台管理员通过
`/admin/challenges/{challengeId}/test-runtime` 的 `no-store` 响应查看，不得进入普通题目、比赛或参赛者响应。

## AWDP 一次性 Target

申请防御会创建一个只绑定 Team、尚未绑定 GameplayFact 的全新 `AwdpTarget` Runtime。Target 在
Queued、Provisioning 或 Running 时即可接收唯一 PatchUpload；成功上传会在同一事务创建 Fix GameplayFact，
并设置 Runtime 的 `GameplayFactId`。应用 Patch 后只运行一次 Checker；成功、业务失败、
平台失败和超时均派发幂等清理，回收 Checker、Target、网络、端口和容量。再次尝试必须创建
新的 Runtime UUID。

AWDP Target 的流程状态来自 GameplayFact、PatchUpload、Runtime state 和类型化事件，
不得写入 `awdp_fix_stage` 或 Checker 调度列。

## 节点标签、对账与清理

Docker/Kubernetes/Libvirt 资源必须至少带平台 managed 标记和 Runtime UUID。可带比赛、题目、
队伍等诊断标签，但清理不能仅凭宽泛比赛/队伍标签执行。节点对账规则：

- 数据库活动实例且归属本节点的精确 UUID 资源保留；
- 终态、已改派或数据库不存在的 managed UUID 进入幂等清理；
- 数据库或租约状态不可用时停止破坏性清理；
- 清理成功后再释放 claim 和容量；重复清理得到相同终态。

## API 与权限

Start/Stop/Reset/Extend 使用强类型 FastEndpoints，并返回 `202 Accepted`、Runtime UUID 和状态
位置。响应不暴露 pool、generation、stage、source revisions、participant indexes 或 Checker
内部状态。Queued/Provisioning/Stopping 只返回状态；Running 才按权限返回 URL；终态返回稳定
失败码。Receipt 和 provider 原始诊断仅向有权限工作人员展示。

可变聚合使用 `ConcurrencyStamp` 乐观并发；请求和响应不携带 ExpectedRevision 或 ProcessingVersion。

## 必测不变量

- Start/Reset 使用新 UUID，迟到旧消息不能改变新实例；
- 业务事务回滚时不发布创建/停止消息；
- Worker 选定 Runner 后消息只到对应命名 JetStream subject；
- Runner subject 缺失或退化为 local queue 时启动失败；
- Runner/Worker 重投不重复创建、停止或释放容量；
- receipt 为空的失联实例先由 Runner 对账，再收敛终态；
- Provider/Worker/Runner 中断后可从关系化业务事实、receipt 和 JetStream 重投恢复；
- AWDP Target 完成、失败和超时后全部资源释放；
- 动态 Flag 随 Runtime UUID 轮换，旧 UUID Flag 失效；
- API、事件、日志和指标不泄露 Flag 或内部调度状态。
