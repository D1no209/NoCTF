# AWDP

## 产品边界

AWDP 是“队伍长期攻击实例 + 一次性 Fix 验证”的攻防模式，不是 AWD 的周期服务检查模型。

- **Break**：队伍启动绑定自己的攻击 Runtime，利用漏洞获得该 Runtime UUID 的动态 Flag。
- **Fix**：队伍申请一个全新、干净、无公开入口的验证 Target，唯一上传一次 Patch，平台只运行
  一次 Checker，得到强类型结果后立即清理所有临时资源。

攻击和防御是两条独立事实流与分值曲线。比赛可通过
`CompetitionChallenge.RulesJson` 配置是否要求先成功 Break 才允许申请 Fix。

AWDP 不使用 AWD 的 `AwdRound`、加固期、周期健康检查、批量攻击目标或轮次 Flag 提交。

## 配置归属

题库 `Challenge.DefinitionJson` 只保存可复用技术定义：

- Player 攻击容器镜像、命令、资源与公开端点；
- `FlagEnvironmentVariableName` 或目标文件位置；
- Patch 入口、命令、大小和超时限制；
- 一次性 Checker 镜像、命令和就绪超时。

比赛配置和比赛题目规则保存比赛专属语义：

- 轮次时长；
- Break/Fix 两条独立分值曲线；
- 错误、EXP 成功、服务异常等罚分；
- Break/Fix 提交限制；
- `RequireBreakBeforeFix`；
- 动态 Flag 模板。

不同比赛可以为同一题目使用不同 Flag 格式。出题人只声明注入位置，不填写队伍 ID、
Runtime ID 或具体动态 Flag。旧的 AWDP 专属 `flagInjection` 对象不属于目标模型。

## 攻击 Runtime 与动态 Flag

玩家启动环境只创建 `Player/AwdpAttack` Runtime：

- 必须绑定 Competition、CompetitionChallenge 和真实 Team；
- 同队同题同时只有一个活动攻击实例；
- Start、Stop、Reset、Extend 复用标准 Runtime 状态机；
- Reset 停止旧 UUID 并创建全新 Runtime UUID，不保存 generation/replacement；
- Worker 生成绑定新 Runtime UUID 的精确动态 Flag，并在 provider 创建请求中注入；
- Runtime Running 后 Flag 生效；Stop、Reset、失败或到期后失效；
- 明文 Flag 不进入 URL、普通 API、事件、通知、日志或指标。

管理端 Runtime 列表必须显示真实队伍名。只有 KoH 等真正共享实例可以显示“共享”。

## Break 判定

AWDP 接受单个 `BreakAttempt`：

- 当前队伍当前活动 Runtime UUID 的有效 Flag：`Correct`；
- 已停止/已替换 Runtime UUID 的 Flag：`Wrong / FlagExpired`；
- 普通错误或格式错误 Flag：`Wrong`；
- 其他队伍的有效 Flag：内部 `Rejected / ForeignTeamFlagDetected` 并创建工作人员可见作弊事实；
  参赛者响应降级为普通错误，不能泄露归属。

同一队伍同题首次有效 Correct 建立持久攻击成功事实。后续即使再次提交正确或错误 Flag，也只
返回判定结果，不重复计分、不覆盖已成功状态、不创建失败状态、不重复播报，也不能被恶意提交
用于刷事实。正确 Break 后按规则自动停止攻击 Runtime，但继续允许无副作用的正确性验证，便于
复现与编写 Writeup。

## 一次性 Fix 流程

1. 队伍点击“申请防御”；
2. API 在 PostgreSQL 临界区校验资格、次数和 Break 前置条件，创建 Fix GameplayFact；
3. 创建绑定 Team 与 GameplayFact 的全新 `AwdpTarget` Runtime；
4. Target Running 后允许唯一上传一次不可变 `tar.gz` Patch；
5. 事务内把 PatchUpload、GameplayFact 和 Target 原子绑定；
6. Runner 安全解包并以 argv 形式执行 Patch；
7. 只启动一次 Checker；
8. Checker 返回强类型业务 outcome；
9. 不论成功、业务失败、平台失败或超时，均幂等停止 Checker/Target，清理网络、端口和容量。

同一 Target 的并发上传只有一个成功。文件在建立业务绑定前即因大小或格式被拒绝时，可以在该
Target 上重新选择文件；一旦绑定 PatchUpload/FixAttempt，就永久锁定。再次尝试必须重新申请
新的 Target 和 Runtime UUID。

Fix Target 不是长期环境，不显示访问地址，不可续期或重置。管理端显示“一次性 Fix 验证
Target”，并通过 GameplayFact/PatchUpload 展示来源队伍，不得显示“共享”。

## Patch 安全

- 只接受配置允许的归档格式与大小；
- 拒绝绝对路径、`..`、符号链接、设备文件和解压炸弹；
- Patch 入口必须在归档根下精确存在；
- 命令以 argv 执行，不拼接 shell 文本；
- Patch 对象键、原文件名、动态 Flag 和选手身份不传给 Checker；
- 验证容器、网络和卷与队伍攻击 Runtime 隔离。

## Checker 结果

Checker 结果在领域和跨进程契约中使用强类型枚举。目标映射：

| Outcome | GameplayFact 状态 | Result | 稳定失败码 |
|---|---|---|---|
| `DefenseSucceeded` | `Completed` | `Correct` | 无 |
| `ExploitSucceeded` | `Completed` | `Wrong` | `AwdpExploitSucceeded` |
| `ServiceAbnormal` | `Completed` | `Rejected` | `AwdpServiceAbnormal` |
| `PatchFailed` | `Completed` | `Rejected` | `AwdpPatchFailed` |
| `PatchTimeout` | `Completed` | `Rejected` | `AwdpPatchTimeout` |
| `PlatformFailed` | `Completed` | `Rejected` | `AwdpPlatformFailed` |

业务 outcome 与详细诊断通过版本化 `AwdpFixResolved` CompetitionEvent payload 传递。详细日志仅
工作人员可见，不复制 Patch、Flag、Token 或私密内容。Checker exit 0 只表示进程结束；没有可信
callback 时不得推导 `DefenseSucceeded`。

同一 GameplayFact/Checker execution 的重投只收敛一次。完成结果后到达的重复或迟到 callback
不得覆盖终态，也不得重新启动验证或清理链。

## 持久防御成功

同一队伍同题首次 `DefenseSucceeded` 建立持久防御成功事实。后续 Fix 尝试可以用于复验，但不能
把已成功状态改回失败，也不能重复建立首次成功积分或播报。攻击成功和防御成功互相独立；UI 在
两者都成功时显示组合状态。

## 轮次与计分

轮次由比赛开始时间和 `RoundDurationSeconds` 计算，不保存 next-run、当前轮或定时消息。
Singular Agent 从 PostgreSQL 比赛事实重建当前内存计划，只派发 durable 轮次结算消息；停机期间
跨过的历史 tick 不补跑。

每个已结束轮次只结算当时已存在的攻击/防御成功事实：

- Break 与 Fix 各自按该轮成功队伍数计算独立动态分值；
- 当前未结束轮不得提前加入正式排行榜；
- 后续轮曲线变化不改写已结算轮；
- 禁赛、解禁或重判触发从 PostgreSQL 事实的确定性全量重投影；
- 计分结果不写累计分或排行榜快照业务表。

排行榜失效通过事件 fan-out 到独立 Sticky PostgreSQL endpoint，500ms 合并同一比赛的连续失效，
随后全量 PostgreSQL 投影并原子替换缓存。缓存丢失时可从事实重建；不存在 `LeaderboardDirty`
或 15 秒脏扫描。

## 事件与赛事播报

平台可以为已验证的攻击/防御操作产生永久事件与简洁赛事播报：攻击提交、攻击成功、申请防御、
Fix 提交、Fix 成功或异常。不得广播明文 Flag、Patch 内容、EXP 细节或私密诊断。重投和重复提交
不得产生重复播报。

## 前端状态

攻击侧显示攻击 Runtime 状态、访问地址、剩余时间和 Flag 判定。防御侧状态固定为：

- 未申请；
- 创建中；
- 可上传；
- 验证中；
- 已完成；
- 平台失败，可重新申请。

Fix 历史在题目工作区内打开，不新开独立页面。结果文案必须映射强类型 outcome；未知/缺失业务
结果默认显示“防御异常：服务异常”，不得自行创造“防御未通过”。

## 必测不变量

- Player/AwdpAttack Runtime 绑定 Team，Reset 使用新 UUID；
- 动态 Flag 注入、失效、跨队作弊检测和重复提交幂等；
- 正确 Break 自动停止 Runtime，后续仅判断正确性且没有业务副作用；
- 申请防御每次创建新的干净 AwdpTarget；
- 同一 Target 只能绑定一个 PatchUpload 和一次 Checker；
- 成功、失败、超时、中断及重投后临时资源全部释放；
- 强类型 outcome、事件 payload、GameplayFact 映射和前端文案一致；
- 每轮只结算结束时事实，Break/Fix 曲线互不影响，历史轮不被后轮改写；
- 缓存丢失可重建，事件乱序不能让旧投影覆盖新事实；
- 普通响应、日志、事件、通知和指标不泄露 Flag、Patch、Token 或内部诊断。
