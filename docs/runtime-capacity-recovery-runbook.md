# Runtime 容量恢复与回滚

本手册描述本地验证后的操作顺序；不代表已经部署到公测环境。默认
`Runtime__CpuOvercommitFactor=1`。倍率 2 只用于明确配置的新 Docker/Kubernetes
分配，Checker、Libvirt、内存与 PID 始终严格计账。

## 部署配置

`Runner__Capacity__MemoryBytes`、`NanoCpus`、`PidsLimit` 是扣除系统预留后的额度。
有效额度取配置与可信执行域观测边界的较小值。不要把宿主总量直接作为题目额度。
Docker 使用实际 daemon 宿主的只读 `/host/proc`、`/host/sys/fs/cgroup`、
`/host/etc/machine-id` 挂载和本地 Unix socket。远程 Docker 无可信观测时停止新增。
Kubernetes Node 指标必须覆盖与题目相同的 `noctf.io/pod-pids-limit` 节点选择器。
缺权限、缺指标或节点压力状态不明均不应通过修改余额绕过。

`Runner:Admission` 默认每 5 秒采样、15 秒过期、主启动并发 2、辅助并发 1，
为辅助任务预留 256 MiB / 0.25 CPU / 128 PID。CPU 90% 持续 20 秒，内存可用
比例低于 10%，或 PID 使用率达到 90% 时阻断；CPU <80%、可用内存 >15%、
PID <80% 连续三个样本后恢复。OOM 和 Provider 故障独立阻断。这些值需要在
目标部署做压力验收，不能用压力阈值计算或重写分配余额。

## 恢复顺序

1. 保留 PostgreSQL、Redis 和 Provider 清单证据。不要删除 Claim 或把余额重置为总额。
   数据库迁移使用 EF CLI；本轮新增 `20260918101546_AddRuntimeCapacityAllocations`。
2. 停止旧协议的 Runner/Worker 新分配入口后，启动具有该资源域唯一所有权的新版 Runner。
   旧进程不参与新协议的所有权锁，不能与新版共同写账本。新版重复所有者启动失败。Runner 会先关闭准入，
   等待本进程中的资源变更收敛；清理与恢复通过同一协调器。Provider 清单和 Redis Claim
   扫描位于数据库事务外，跨节点 Claim 扫描有 10,000 条上限，超过时保持关闭并调查。
3. 恢复器比较活动 JSONB 分配、旧 Claim 和 Provider 身份。旧 Claim 的完整预算先提交到
   PostgreSQL，再替换 Redis；缺少身份或预算证据的旧资源保持待核对，不能按零处理。
   不根据今天的题目配置反推旧分配。
4. 短事务下重新读取活动分配，原子重建 Claim、余额和启动名额；健康观测后恢复候选索引。
   准入中断期间心跳独立续租，仍可接收清理。配额下调造成的负余额是预算缺口，已有实例不终止。
5. 验证监控中的观测时间、准入状态、Budget/Limit/Usage/Allocatable，以及各 Runtime 的
   活动分配。核对正在创建和辅助任务，不能只数 Running 实例。清理失败保留占用。
6. Redis 成功但数据库提交不明时，先重读数据库并对账。数据库没有已提交分配与持久消息时
   不创建资源。Provider 创建后进程退出时依靠确定性身份查找原资源；禁止改 UUID 重试创建。
7. 确认资源不存在后移除该分配并发布释放消息。重复/迟到释放不会重复归还。全部资源清理后，
   主启动及辅助名额归零，剩余预算精确等于有效额度。

## 停止共享与回滚

首先把新任务的 `Runtime__CpuOvercommitFactor` 改为 1，并按角色重启或滚动更新。
保留已有分配的预算数额，直到原资源释放；不要把倍率 2 的活动 Claim 直接翻倍，也不要将
可调度总额翻倍。若额度不足，等待队列保留原因，允许用户取消，清理继续运行。

代码回滚只能回到能读取新账本的兼容版本。旧 Runner 不能连接新账本并重新写入余额。
确需回到旧协议时，先停止所有新准入，使用新版完成资源清理并确认活动分配为空，保存备份，
再离线验证协议转换及消息兼容性。不能在运行中直接回退迁移或丢弃新 JSONB 列。

历史裁决预览独立回滚页面/API 即可：它没有重判、改分、补发血榜或写事件的副作用。
新血榜事件的 ParentEventId 作为证据保留，旧事件不回填、不删除。

## 本地复验

在仓库根目录运行 `backend/scripts/Verify-CoreRecovery.ps1 -Configuration Release`。
CPU 共享诊断运行 `backend/scripts/Measure-CoreCapacity.ps1`，使用独立 Testcontainers
Redis 和随机 UUID 的 Docker 资源，不连接公测服务器。它验证实际硬限制、保留额度、
并发领取、Redis 丢失恢复和精确释放；健康观测由固定测试数据提供，不能替代实际宿主压力验收。

增加 `-HostPressure -RuntimeImage <包含 ASP.NET 10 运行时的本地镜像>` 可在 Linux Docker
宿主上执行真实压力测试。它会短暂使用宿主 CPU，默认阈值保持不变，测试容器自动清理。
测试 DLL 与报告目录单独挂载，宿主指标只读挂载；Docker socket 用于创建和清理测试资源。
本次 Docker Desktop VM 的 32 CPU 实验已观察到高压阻断及三个健康样本后恢复。

Kubernetes/Libvirt 真实环境和 Linux 同机压力实验缺失时，验收状态必须明确为未验证。
跨进程崩溃、Provider 不可达、数据库提交不明等故障须保留各系统证据，不能仅以最终 UI
显示正常认定跨系统收敛通过。性能门槛为同环境普通业务 P50/P95 与资源消耗不超过 5% 回归；
样本或部署环境不等价时，应报告未建立结论。
