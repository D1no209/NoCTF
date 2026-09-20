# 审查后修正与验证

> 本文是容量 schema 2 的历史证据。当前 schema 3 已改为按实际占用准入；以
> `runtime-capacity.md` 与 `runtime-capacity-recovery-runbook.md` 为准，文中的 CPU
> 超售建议不再生效。

本记录接续 `7709b8e9e` 的审查，保留原有工作区修改；所有提交仅在本地。

| 问题 | 提交 | 验证 |
| --- | --- | --- |
| R1 | `8445e590d` | 领取→回滚→取消后由普通审计回收；持久化 4、Redis 13、消息 2 项通过 |
| R2 | `5e8741510` | 单容器/Compose 修改 Limit 的真实 PostgreSQL/Redis 回归 2 项；既有生命周期/预算/工厂 19 项通过 |
| R3 | `515087d1e` | 父 RunnerId 清空后，以新审计上下文回收真实 Docker Checker；分配回收 1、既有审计 3 项通过 |
| J1 | `355122423` | 无奖励/无调整的正常非参与队伍不再误报；历史专项 25 项通过 |
| J2 | `862e1911e` | 父事件类型、事实范围、Completed/Correct 与截断分类；历史专项 29 项通过，OpenAPI 不变 |
| V2 | `386d8e6f1` | 预览三组性能复测达标，新增语义保留 |
| Docker 恢复补充 | `753041844` | 模板测试容器重放保留用途身份 |
| 回写补充 | `1ee390ae0`、`c379ad388` | 短行锁、终止状态保护、无效重放不制造失败；保留有效结果完成顺序 |
| V1 组合故障 | `d8a1aaca2` | 五个真实进程故障窗口通过 |
| 基线断言 | `b2446e4c0` | 不改生产代码，修正异步文件清理的既有测试预期 |
| 大清单争用 | `655604f10` | 8192 Claim 与 16/64 并发验证 |
| Kubernetes | `e26c971e9` | 实际节点、资源请求及指标权限验证 |

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

## 公测容量配置与 CPU 共享策略

2026-09-19 只读复核服务器：宿主 4 CPU、7951 MiB 内存，采样时可用约 4440 MiB；
Runner/Worker 配置均为 2 CPU、4 GiB、2048 PID，未设置 CPU 共享倍率。Docker 对这两个
进程容器未设置 CPU/内存硬额度（inspect 值为 0），这不等于题目的可调度额度无限。
没有修改服务、配置或部署。

保留仓库默认倍率 1。建议后续灰度候选仍使用 **2 CPU / 4 GiB / 2048 PID** 配额，只对
新分配启用 `Runtime__CpuOvercommitFactor=2`，先不同时提高总配额。以每题 0.5 CPU /
256 MiB / 128 PID 为例，预留 Checker 后，空账本严格模式可准入 3 个，倍率 2 可准入 7 个；
这是预算推算，不是吞吐承诺。内存/PID仍按完整上限扣减。

启用前必须完成新账本恢复并处理旧的满额分配：旧 Claim 数额不会因倍率改变而减半，不能
据此假定 Checker 预留立即产生。使用自然到期或明确维护窗口排空，核对余额，再分批创建
新实例并观察压力/排队。回退时只把新任务倍率恢复为 1，保留已有数额。

`min(配置配额, 观测边界)` 的政策仍不自动放宽偏低配额；需要更多规模时应另行压测并调整
部署额度。这一配置候选没有被启用，也不构成“公测排队瓶颈已消除”的结论。

## Libvirt 真实环境

在独立本地 Linux 容器内通过宿主 KVM 运行 libvirt 10.0.0 / QEMU 8.2.2，执行既有
`LibvirtOvaRuntimeIntegrationTests`，1/1 通过，耗时 2 分 15 秒。涵盖真实 OVA 导入、
Guest Agent 启动 HTTP、访客地址访问、替换及按确切身份清理；结束时 `virsh list --all` 为空。
证据见 [libvirt-result.txt](validation/runtime-capacity-review/libvirt-result.txt)。

实验镜像准备依据 [virt-builder 官方说明](https://libguestfs.org/virt-builder.1.html)：
Debian 12 模板、8 GiB qcow2、安装 `qemu-guest-agent,python3` 并启用 Guest Agent，
测试导入资源为 1 vCPU / 512 MiB，网卡 `ens3` 使用 DHCP。签名校验保持开启。
隔离宿主安装 libvirt/qemu/virt-install/libguestfs 与内核，必须使用容器 `--init` 回收
虚拟机子进程；首次无 init 的夹具清理失败以及镜像网卡不匹配均已纠正后重跑。
此结果证明物理 adapter 基本链路，不代表 Libvirt 已覆盖 Docker 的全部强杀矩阵。

## V3：最终全仓及交付门禁

最新 Release solution build 零警告、零错误。全仓 TUnit **1665 项：1657 通过、0 失败、
8 条件跳过**，耗时 21 分 48 秒；包含真实进程故障、PostgreSQL/Redis/NATS/Docker 和
Kubernetes 容量测试。原来失败的网关测试用校验 SHA-256 的官方 FRP 0.68.0 归档和本地
构建的 Linux relay 补齐依赖后通过，没有通过禁用断言绕过失败。

八个跳过项保留可见：GitOps 外部语料/API 测试、只由父测试启动的子进程入口、Linux 宿主
压力入口、Linux 文件权限/agent 两项、Kubernetes 完整 Compose 网络策略及大归档 Checker
两项、Libvirt 一项。宿主压力、子进程和 Libvirt 已在各自环境另行执行；未执行项不能算通过。

前端 **588/588**，typecheck、架构审计 0 violation、生产生成通过。EF drift 为空，
OpenAPI 重新导出和 SDK 生成无差异，`git diff --check` 通过。原有 19 文件 patch 的 SHA-256
与开始前完全一致。所有修正分步本地提交，未推送、未远程部署。

仍不外推的结论：所有 HTTP 业务的端到端 5% 性能门槛、所有 Provider/辅助 Checker 的
完整故障组合、Kubernetes 实际高压与 CNI 网络策略全路径、公测部署效果。这里明确区分
已通过的修正门禁和这些更广的验收范围，不能称全部环境、全部矩阵均已验证。
