# 延迟直方图及监控口径修复记录

日期：2026-09-08。本次仅本地实现、测试和提交，不推送、不部署。

## 第一阶段：纠正秒级直方图

在 `ObservabilityExtensions` 中按原始 Instrument 名显式配置桶，而不是匹配 Prometheus 改写后的名字。没有全局 `AddView("*")`。

覆盖全部 7 个自定义秒级 Histogram：

- `noctf.api.request.duration`
- `noctf.redis.operation.duration`
- `noctf.signalr.publish.duration`
- `noctf.runner.claim.duration`
- `noctf.leaderboard.projection.duration`
- `noctf.scheduler.rebuild.duration`
- `noctf.scheduler.dispatch.lateness`

共同边界，单位为秒：

```text
0.0001, 0.00025, 0.0005, 0.001, 0.0025, 0.005, 0.01, 0.025,
0.05, 0.1, 0.25, 0.5, 0.8, 1, 2.5, 5, 10, 30, 60, 120, 300
```

HTTP 直方图额外保留 `600, 1800, 3600, 21600, 86400` 秒长尾桶，容纳已完成的 SignalR 长连接和大文件传输；不牺牲普通 REST 的毫秒级精度。

保留 `TotalSeconds`、`unit: "s"`，只在应用层监控视图中做一次 `×1000`。前端收到的毫秒值直接格式化，不再转换。事实数、队伍数、调度项数量等 Histogram 不使用时间桶。

真实 MeterProvider 和 Prometheus exporter 验证了 `/metrics` 的桶边界、累计计数和量纲。输入 1、2、4、12、40 ms 样本，基于实际导出桶估算的 P95/P99 落在 40–50 ms 区间，而不是 4750/4950 ms。测试不要求桶内插值等于原始样本的精确分位数。

## 第二阶段：口径、样本和配额

### HTTP 分类

自定义请求计数和耗时使用完全相同的 `endpoint`、`outcome`、`request_kind` 标签；耗时在 finally 中记录，包含异常、4xx 和 5xx。

`request_kind` 为 `rest`、`signalr`、`upload`、`download`。上传下载端点使用显式元数据，所以即使返回 JSON 错误、上传校验失败或未能开始下载，也不会变成普通 REST。文件类端点包括题目附件、Patch、头像、海报、Logo 和导出。

- 普通 REST 摘要和其速率、5xx 比例统一过滤 `role="api",request_kind="rest"`。
- 静态页面、资源和健康检查不计入自定义普通 API 指标。
- SignalR 的 HTTP 耗时是已完成连接的存续时间，不是发布一条消息的延迟。当前连接数量仍独立显示。
- 上传下载不丢弃监控，在独立明细中展示。
- 修正低流量下的 5xx 比例分母，不再用 `clamp_min(rate, 1)` 稀释错误率。

### Redis 和样本质量

摘要改为“平台 Redis 操作 P99 耗时”。按 `endpoint` 展示心跳、容量领取、排行榜发布等封装操作，不描述为 Redis 服务端单条命令耗时。一个封装操作可能包含多次 Redis 调用。

分位数、均值、速率、错误率、样本量均采用同一个五分钟窗口，并向所有 Prometheus 查询传递同一个固定 `time`，避免多次请求的评估时间漂移。样本量由对应 Histogram `_count` 的 `increase(...[5m])` 取得，属于 Prometheus 外推估算，界面格式化为近似整数，不另选窗口。

状态区分：

- 没有样本：`NoSamples`，延迟为 null，页面显示“暂无样本”，不显示 0 ms。
- 少于最低样本量：`InsufficientSamples`，保留估算值并显示“样本不足／低置信度”，不触发延迟告警。这是样本充足性提示，不是统计置信区间计算。
- 当前超阈值但尚未达到持续条件：`Observing`，显示“超阈值，持续观察”。
- 样本充足且持续超阈值：使用原有告警级别。
- 查询失败：`Unavailable`，不能冒充无样本或正常零值。

没有提高现有阈值：普通 API 仍为 800 ms，平台 Redis 操作仍为 50 ms。新增配置（环境变量形式，未修改任何 `.env`）：

```ini
Observability__Monitoring__LatencyMinimumSamples=100
Observability__Monitoring__LatencySustainedWindowMinutes=3
```

样本门槛必须为正数，持续时间为 1–60 分钟。持续判定使用 15 秒子查询步长检查同一五分钟分布，要求完整观察点及足够样本；不是扩大分布窗口。普通 API / Redis 有延迟阈值，文件传输与连接生命周期明细不套用普通 REST 阈值。

### 资源池配额

“Runner 最低可用容量”改为“资源池最低剩余配额”。公式仍先按 `(pool, resource)` 汇总在线 Runner，再取可用量／总配额的最低比例，不改成节点级最小值。

展示每个池的内存、CPU、PID 可用量与总配额，以及在线 Runner 数量。零总配额不计算比例，不伪装为 0% 或 100%。节点离线可能提高剩余比例，页面明确提醒同时检查在线数量；全无在线节点时在线数量仍独立告警。

主机真实利用率与调度配额分开，复用现有 Grafana 主机监控入口。没有新增 exporter、监听端口、部署监控容器或采集服务。

## 验证

- 后端构建：0 警告、0 错误；1084 项非 Integration 测试通过。
- 真实 MeterProvider + `/metrics`：7 个秒级 Histogram 的桶和计数、数量类桶不受影响、已知毫秒样本的合理分位数区间。
- 真实 ASP.NET 请求管线：REST、SignalR、失败上传、失败下载、抛出异常，以及静态资源和健康检查排除；计数与耗时标签一致。
- 监控查询：无样本、低样本、正常、超阈值、持续判定、同时间点/窗口/过滤、多个 Redis endpoint、多个资源池。
- 本地临时 Prometheus：所有实际监控查询能够执行，空分布不产生伪零延迟。
- 12 项真实 Redis 容量测试通过，包括领取减少配额、释放恢复、心跳刷新导出值、离线节点；独立 MeterListener 测试覆盖单节点、多节点、多池、零配额及池级与节点级比例差异。
- 前端：389 项测试、类型检查、静态生产构建通过；使用独立本地模拟 API 完成深色/浅色浏览器核验，不是生产数据。
- OpenAPI 与生成 SDK 已同步，没有手写改动生成文件。

## 后续部署验收（本次未执行）

1. 得到明确指令后先部署测试环境，通过现有内网指标访问路径检查 `/metrics`，确认 `le="0.05"`、`le="0.8"` 等秒级边界；部分序列需先有实际请求才出现。
2. 避免同一个聚合查询混入新旧桶结构。安排一致的指标切换或受控替换，等待旧五分钟窗口退出后对比；普通 API 新标签也需要积累样本。
3. 同负载下对比分位数、均值、样本量和错误率，确认所有结果使用相同标签和五分钟窗口。
4. 验证后再按独立授权手动部署生产。没有执行任何测试服务器或生产服务器部署。

本次不需要 Migration，不修改 Redis 参数、持久化配置、数据卷、CI、版本号或 `1panel-network`。

分位数估算语义参考：[Prometheus histogram_quantile 官方说明](https://prometheus.io/docs/prometheus/latest/querying/functions/#histogram_quantile)。
