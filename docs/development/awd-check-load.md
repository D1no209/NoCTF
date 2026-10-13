# AWD 并发待裁定排查（2026-10-13）

AWD 检查进入待裁定有两个已经复现的原因：领取冲突后的恢复消息被 JetStream 去重，以及短暂的辅助任务并发占满被立即记为检查失败。无额外并行负载时也能出现前一个问题，因此不能把原因归结为机器繁忙，也不能用一次独立 E2E 通过作为结论。

## 工作负载与证据

- 基线提交：`8f5a461a`，使用仓库 Dockerfile 构建的 Release Host，包含生产静态 Wolverine 处理器生成。
- AWD：两个队伍、一道容器题、30 秒一轮、10 秒检查间隔、10 秒轮末检查预算；Runner 辅助任务并发上限保持默认值 1。
- 并行负载：原 Release `NoCTF.Tests.dll` 的 `Integration.Persistence` 测试，最大并行度 4。该进程仅作为负载，在 E2E 结束后停止；不把它的中断结果当作正确性测试结果。
- 压力进程程序集 SHA-256：`5DABDE38BC27CE77D74DE99A1B0416A9D24C2701AAB5949B971223A6D6713BDB`。回归测试另行输出到隔离目录，未替换这个负载程序集。
- 主机为 i9-14900HX、32 个逻辑 CPU；Docker Desktop 29.8.2 / WSL2 提供约 31.15 GiB 内存。主机 SDK 10.0.401，生产镜像 SDK 10.0.302、运行时 10.0.10。
- 每秒读取管理员 AWD 检查证据；同步采集 Docker stats、选定容器生命周期事件、cgroup CPU/内存计数，以及各角色 120 秒的 `System.Runtime` counters。
- API 证据仅保存白名单字段：事实和尝试标识、计划/领取/截止/回执时间、处理状态、原因和证据计数；不保存 Flag 内容、回调令牌或私有读取上下文。

比赛主动结束时，未启动的未来样本会被标记为 `TaskNotExecuted`。统计将“观察到待裁定时尚未到截止时间”的这类记录单独列为 `endedBeforeDeadline`，不算作负载失败；其他实际检查失败仍计入，包括在截止前耗尽重试次数的 `InfrastructureFailure`。

## 领取冲突后的恢复投递

原链路为：

1. Runner 读取待执行事实并尝试领取；数据库并发冲突被识别为可重试异常，读取器返回空结果，本次消息处理结束。
2. Worker 周期扫描再次发布同一个执行尝试。
3. 发布器以消息完整内容计算 `DeduplicationId` 和 `Nats-Msg-Id`。原消息内容完全相同，恢复投递被 JetStream 去重。
4. 事实仍未领取，最终进入 `TaskNotExecuted` / `PendingAdjudication`。

`AwdCheckerRedispatchTests` 使用真实 PostgreSQL 和 NATS/Wolverine：在第一次领取之前通过另一个 DbContext 修改并发令牌，确定性地造成领取冲突。旧实现的第二次投递在 8 秒等待内无法到达；修复后同一个执行尝试可以成功领取，已领取任务的重复执行仍被拒绝。

修复为 `RunAwdChecker` 增加每次投递独立的 `DispatchAttemptId`。Worker 恢复投递产生新编号，业务 `AttemptId` 保持不变，原有执行和回调隔离规则继续有效。

## 辅助任务容量竞争

Runner 日志明确记录拒绝原因是 `StartupConcurrencyLimited`，而不是内存不足或节点 CPU 容量不足。两个队伍同时到达检查时间，默认辅助并发数为 1，第二个任务会在几十毫秒内返回 `InfrastructureFailure`。

一条修复前时间线（UTC）为：

| 时间 | 事件 |
|---|---|
| 11:46:17.752 | 第一次领取 |
| 11:46:17.845 | 容量拒绝回执，等待 Worker 的检查重试间隔 |
| 11:46:23.420 | 第二次领取，仅剩约 2.74 秒 |
| 11:46:26.160 | 原检查截止时间 |
| 11:46:27.139 | 超时回执，进入待裁定 |

另一条轮末样本两次都因并发上限被拒，直接耗尽重试次数。仅修复恢复消息身份后，这条容量路径仍在并行负载中复现。

修复后，AWD 执行器仅对 `StartupConcurrencyLimited` 在同一尝试内短暂等待，并使用原绝对截止时间和执行超时中较短的预算。每次等待前，容量调用已经释放资源变更租约；只有容量准入成功才调用容器提供者。已启动的外部任务不会自动重试，内存等其他准入失败也不进入这个等待循环。预算耗尽仍会报告超时。

## 对照结果

“负载待裁定”已排除比赛结束后的未执行未来样本；“E2E 通过”不能单独代表所有样本正常。

| 版本 | 三次有效并行负载中的待裁定数 | 其中轮末样本 | E2E 结果 |
|---|---|---|---|
| 修复前 | 0、3、2 | 0、1、0 | 通过、失败、通过 |
| 仅修复投递身份 | 3、0、1 | 0、0、0 | 全部通过，但仍有实际失败 |
| 同时修复投递与容量等待 | **0、0、0** | **0、0、0** | 加强后的 E2E 全部通过 |

最终三次有效样本的负载进程均持续运行到 E2E 结束。每次观察到 14 个已执行尝试，另外 4 个未来样本因主动结束比赛被单独归类；负载期间没有待裁定。尝试时长 P50 分别为 2.10、2.79、2.23 秒，P95 为 4.28、4.53、4.78 秒。

对应诊断镜像标识前缀分别为：修复前 `214e558a78a6`、仅修复投递 `90ec85326a36`、完整修复 `54e64a7a64c5`。完整标识保存在每轮 `run.json`。最终有效样本位于 `final-pressure/pressure-1`、`final-pressure/pressure-2`、`final-pressure-replacement/pressure-1`；原第三轮因网络地址池耗尽未启动业务测试，未计入结果。

无附加负载的观测基线也有 2 个 `TaskNotExecuted`。该次 E2E 最终在结束比赛请求处遇到独立的 `503 TransactionBusy`，此请求失败不作为 Checker 根因。

E2E 已新增结束比赛前的待裁定总数断言，防止“后续轮次成功”掩盖先前失败。容量等待、原预算耗尽、非并发容量失败、绝对截止时间及调用方取消的单元测试共 10 项通过；AWD 轮次与证据持久化测试 23 项通过。新回归在旧实现上先失败，修复后通过。

## 重复采集

采集镜像需基于相应 Release Host 镜像，额外安装可执行的 `/out/tools/dotnet-counters`；本次工具版本为 10.0.750501。先构建并固定作为负载的 Release 测试程序集，随后使用不同输出目录比较修改前后的镜像：

```powershell
./backend/scripts/Measure-AwdCheckLoad.ps1 -Scenario pressure -Runs 3 `
  -Image noctf-awd-load:capacity-wait-diagnostics -RemoveContainersAfterCapture `
  -OutputDirectory backend/artifacts/performance/awd-load-20261013/repeat

./backend/scripts/Summarize-AwdCheckLoad.ps1 `
  -Directory backend/artifacts/performance/awd-load-20261013/repeat
```

`RemoveContainersAfterCapture` 只移除本轮已停止的测试容器及其项目网络，保留命名数据卷，并记录 `retained-volumes.json`。不使用全局 Docker prune。未成功启动到业务测试的轮次会显示 `measured=false`，不得计入对照结果。

本次本地证据位于忽略目录 `backend/artifacts/performance/awd-load-20261013/`。`run.json` 记录镜像标识、基线提交、工作区改动、负载配置、时间和退出码；`awd-checks-*.jsonl` 保留逐事实时间线。汇总中的尝试时长是 `AttemptStartedAt` 到 `ObservedAt`，包含排队和容器启动，并非纯 Checker 计算时间。

## 结论边界

- 测试覆盖的是上述两个队伍的负载和恢复链路，不是大规模赛场吞吐量认证。持续超过 Runner 可用容量时，无法在截止前完成的检查仍应进入待裁定。
- 观测期间未发现角色 cgroup OOM 或 CPU 节流，Runner 线程池排队很低；这不足以证明整个主机没有任何资源竞争。根因判断来自具体拒绝原因、任务时间线和真实依赖回归。
- counters 每轮只覆盖 120 秒；早期试采因日志缓冲问题延迟启动了资源采集，未用于资源归因。工作站存在其他运行进程，修复前第二轮期间还进行过回归工程构建，故不据此宣称精确的性能提升比例。
- 保留多轮 Docker 网络曾耗尽默认地址池，导致若干环境启动失败。这些轮次单独保留记录并排除；释放本任务网络后补跑有效样本。
