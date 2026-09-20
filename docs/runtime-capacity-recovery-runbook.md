# Runtime 容量 schema 3 上线与回滚

本手册用于把旧的“声明预算长期扣减”模型原地升级为“宿主实际占用 + 启动临时预留”。
升级不停止、重建或抢占现有 Runtime。

## 部署前基线

记录以下信息并保留 UTC 时间：

- 当前提交、镜像摘要和各角色重启次数；
- Running / Provisioning / Queued Runtime 数量；
- Redis `registrationSchema`、准入状态和排队原因；
- 宿主 CPU、`MemAvailable`、PID 使用量、cgroup 限制和 OOM 计数；
- Provider 实际资源清单及 PostgreSQL `capacity_allocations` 所有权。

Docker/Libvirt 不再配置 `Runner__Capacity__*`。`Runtime__CpuOvercommitFactor`
和 `Runner__Admission__AuxiliaryReserved*` 也已移除。Runner 必须继续只读挂载实际
daemon 宿主的 `/proc`、`/sys/fs/cgroup`、`/etc/machine-id`。远程 Docker 或缺失
可信宿主观测时不会开放准入。

## 滚动顺序

1. 先滚动 API/Worker。新版 Worker 同时识别 schema 2 和 schema 3；旧 Runner
   继续使用旧容量逻辑。
2. 再逐个滚动 Runner。Runner 先把节点标记为 `reconciling` 并从候选索引移除，
   心跳和清理通道继续工作。
3. Runner 取得执行资源域唯一所有权，读取 Provider 清单，并在短 PostgreSQL
   临界区内读取活动分配。
4. `Provisioning` 分配恢复完整硬上限的启动预留；`Running` / `Stopping` 只恢复
   Claim 所有权；仍有 Provider 资源的 `Failed` 分配保留到清理完成。
5. Redis 原子重建为 schema 3。取得一份新鲜观测后，节点重新进入 Ready。
6. 观察至少两个完整采样周期，确认启动预留在 `Running` 后直到下一份新鲜观测
   才归零，且等待中的 Runtime 由 Singular Agent 自动重试。

对账期间禁止手工删除 Claim、清空 `capacity_allocations` 或重启现有题目容器。
Provider 清单不明、Redis 不可用或资源域所有权冲突都必须保持关闭准入。

## 上线验收

管理监控应显示宿主总量、宿主实际空闲、安全余量、启动临时预留、最终可准入、
运行实例硬上限合计和实际使用。硬上限合计仅用于风险观察，不应等于资源占用。

在 4 CPU / 约 8 GiB 的目标节点上，三个低占用、各自硬上限为
`0.5 CPU / 256 MiB / 128 PID` 的 Running 容器存在时：

- 第四个相同 Runtime 应在下一次重试中启动；
- 原有三个容器的 ID 与重启次数不变；
- 启动时 `startupReserved` 增加；完成后的第一份新鲜观测使其归零；
- CPU/内存/PID 真正不足时仍按对应实际余量原因排队；
- OOM、Provider Pressure 或持续高压仍阻止新启动。

## 回滚

发现 OOM 增量、持续压力、负的最终可准入量、重复所有权或 Claim 不收敛时：

1. 停止继续滚动并保存 PostgreSQL、Redis、Provider 和宿主观测证据。
2. 先回滚 Runner，再回滚 Worker/API 到同一旧版本，不删除现有资源。
3. 旧 Runner 会把 schema 3 视为不可信，暂停准入，并根据 PostgreSQL 与 Provider
   事实重建 schema 2；现有容器继续运行。
4. 确认 schema 2 账本、Claim 所有权、排队重试和清理收敛后再恢复发布。

回滚不会恢复已经删除的 `Runtime__CpuOvercommitFactor` 运行语义；旧镜像所需的旧配置
必须来自该版本自己的受控部署备份，不能用当前宿主总量随意填写。

## 本地复验

```powershell
backend/scripts/Verify-CoreRecovery.ps1 -Configuration Release
backend/scripts/Measure-CoreCapacity.ps1
```

Kubernetes、Libvirt 和 Linux 同机压力验证若未在目标环境执行，必须明确记录为未验证，
不能以 Windows Docker Desktop 或单次 UI 快照替代生产结论。
