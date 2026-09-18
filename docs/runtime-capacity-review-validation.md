# 审查后修正与验证

本记录接续 `7709b8e9e` 的审查，保留原有工作区修改；所有提交仅在本地。

| 问题 | 提交 | 验证 |
| --- | --- | --- |
| R1 | `8445e590d` | 领取→回滚→取消后由普通审计回收；持久化 4、Redis 13、消息 2 项通过 |
| R2 | `5e8741510` | 单容器/Compose 修改 Limit 的真实 PostgreSQL/Redis 回归 2 项；既有生命周期/预算/工厂 19 项通过 |
| R3 | `515087d1e` | 父 RunnerId 清空后，以新审计上下文回收真实 Docker Checker；分配回收 1、既有审计 3 项通过 |
| J1 | `355122423` | 无奖励/无调整的正常非参与队伍不再误报；历史专项 25 项通过 |
| J2 | `862e1911e` | 父事件类型、事实范围、Completed/Correct 与截断分类；历史专项 29 项通过，OpenAPI 不变 |

R1 的普通审计回归使用真实 PostgreSQL/Redis 与受控 Provider 探针；R3 使用真实 Docker
和新的服务实例。这些不冒充进程强杀验收；组合进程故障测试另行记录。

## V2：历史预览性能

保留全部新增证据语义，优化空证据路径、每页共享资格及资格事件解码，并在同一次查询中
读取首次完成与队伍资格。数据库读取从 7 次降为 6 次。分页上限、稳定顺序、128 条证据
前缀、RepeatableRead、内部赛道权限及 J1/J2 规则均保留。SQL 约束检查和历史专项通过。

同机同 Release 配置，原始实现 `957a05e6b` 与当前实现交替进行三组采样，每组预热 10 次，
测量 50 次；601 条事实，每页 500 条。基线仅加入相同测量代码及相同时间窗口的测试语料。
分配字节使用 `GC.GetTotalAllocatedBytes`，包含测试进程的后台开销，不能当作生产常驻内存。
测量期间不并行运行构建/其他测试。

| 配对 | 版本 | 平均 ms | P50 ms | P95 ms | 平均分配字节 | 数据库读取 |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| 1 | 基线 | 17.47 | 16.75 | 22.33 | 997390 | 7 |
| 1 | 当前 | 15.48 | 15.56 | 18.00 | 824178 | 6 |
| 2 | 基线 | 16.99 | 16.76 | 20.93 | 996970 | 7 |
| 2 | 当前 | 15.13 | 15.09 | 20.70 | 824313 | 6 |
| 3 | 基线 | 15.14 | 14.67 | 17.99 | 997585 | 7 |
| 3 | 当前 | 14.10 | 14.29 | 16.37 | 823873 | 6 |

三组平均、P50、P95 均未回归，分配量降低约 17%。**原先未达标的同语料预览场景现已达到
5% 门槛**，不以计分投影微基准代替。原始 300 个样本、查询耗时和分配量见
[preview-summary.csv](validation/runtime-capacity-review/preview-summary.csv) 及该目录六份 JSON。
这不是所有规模、所有 HTTP 业务或整个平台的端到端性能结论。

## V1：真实进程故障窗口

真实 Docker 宿主只读指标、生产 Runner 注册、Worker 派发/回写、Singular Agent、
PostgreSQL EF Outbox、Redis 和 Wolverine/NATS 在 Linux 子进程中组合运行。测试替身仅用于
本语料不调用的 AWD/KoH provisioner；Provider 清单限定为本测试 UUID，防止清理其他本地资源。
故障屏障位于测试进程的 EF interceptor 或真实 Docker adapter 外层，生产代码没有测试开关。

五个场景全部通过，合计 6 分 56 秒：

| 窗口 | 动作 | 检查点至最终收敛秒数 |
| --- | --- | ---: |
| Redis 领取后、分配写入前 | 数据库回滚、通过正式测试 Runtime 用例取消，不重启 | 23.68 |
| 分配写入前 | `SIGKILL`，取消后重启 | 12.83 |
| 分配与 Outbox 提交后、刷新派发前 | `SIGKILL`，重启自动创建，再正常停止 | 69.30 |
| 真实容器创建后、回写前 | `SIGKILL`，重启复用同一容器，再正常停止 | 68.99 |
| 确认清理并移除分配后、Redis 释放前 | `SIGKILL`，重启完成释放与准入恢复 | 82.87 |

以上是故障后的完整检查链耗时（包含重新启动、消息所有权恢复及必要停止），不是 P50/P95
性能基准。每次最终 PostgreSQL 分配为空、Docker 资源不存在、Redis 预算恢复 512 MiB、
启动名额归零且健康准入 Ready；创建恢复只发现一个容器。未手工删除 Redis Key，也未向
恢复器传入内存分配列表。证据见 [crash-summary.csv](validation/runtime-capacity-review/crash-summary.csv)
和同目录五份 `crash-*.jsonl`。副作用全部限定在本地隔离数据库和测试 UUID。

运行：设置 `NOCTF_CAPACITY_FAULT_IMAGE` 为含 ASP.NET 10 的本地 Linux 镜像，
`NOCTF_CAPACITY_MEASUREMENTS` 为报告目录，再执行 TUnit `CapacityCrashRecoveryTests`。
镜像只提供运行时，实际生产程序集来自当前 Release 构建的挂载目录。

## 已有锁范围调整的压力验证

在真实 PostgreSQL/Redis 中放入 8192 条待恢复 Claim，通过 Redis MONITOR 确认清单扫描
已经开始，再由另一个数据库会话获取全局容量锁：16/64 并发场景的等待分别为 5.30/0.86 ms，
取锁时扫描仍未完成，直接验证没有跨扫描持锁。另一 Runner 同时进行持久领取/释放，最终余额
精确回归。扫描与并发工作合计约 4.80/3.30 秒，两项测试通过；这不是恢复路径的通用性能 SLO。
原始诊断在同目录 `inventory-concurrency-16.json` 与 `inventory-concurrency-64.json`。

## Kubernetes 真实资源域验证

使用隔离本地 kind 0.33.0 / Kubernetes 1.35.8 三节点集群，节点镜像固定为
`kindest/node:v1.35.8@sha256:07b2536e30b803ed61d1677a79df6115f798ce64c80f9e22f6ed45afd09323c0`。
配置见 `backend/tests/environments/capacity-kind.yaml`；kubelet 的 `podPidsLimit: 128`
已从实际节点配置核对。只有一个工作节点有匹配标签，其余节点不计入观测资源域。

真实 Pod 落在该节点，CPU Request=101m、Limit=202m，内存 Request=完整 Limit。
Observer 实际 Node/metrics 读取进入 Ready，容量等于选中节点而非三节点总和，PID 数值
仍为未知。无 metrics 权限但可读取 nodes/namespaces/pods 的 ServiceAccount 被实际 API
拒绝后，Observer 关闭准入。结果见 [kubernetes-capacity.json](validation/runtime-capacity-review/kubernetes-capacity.json)。

参考 [kind 官方配置](https://kind.sigs.k8s.io/docs/user/configuration/)；实验使用 metrics-server
0.9.0，仅在此自签名本地集群添加 `--kubelet-insecure-tls`。没有修改全局 kubeconfig。
运行时指定此独立 `KUBECONFIG`，启用 `NOCTF_KUBERNETES_CAPACITY_INTEGRATION=true`。
本项没有验证 CNI 网络策略执行、真实 Node 高压阈值或所有 Compose/Checker 路径，不外推为
整个 Kubernetes Provider 全验收通过。

V3 的完整全仓及 Libvirt 环境结果将在完成后补充；容量配置
仍按部署额度与真实边界取小值，默认 CPU 倍率仍为 1，不宣称原公测瓶颈已经消除。
