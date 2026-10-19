# NoCTF k6 性能测试

## 环境要求

- Windows PowerShell 7.4 或更新版本。
- Docker Desktop Linux 引擎，能够下载 Compose 中固定的镜像。
- 仓库 `global.json` 要求的 .NET SDK，以及 PATH 中的 `dotnet`、`docker`、`k6`、Bun。入口通过锁定文件安装 Microsoft SignalR 协议库并生成 k6 可用的协议模块。
- 运行入口必须从当前仓库调用。Host 使用 Release 构建及 Production 配置，启用 Api、Worker，使用真实 PostgreSQL、Redis、NATS JetStream 和 RustFS S3 存储。
- 默认规模为 500 名独立选手、500 支单人队伍，正式测量持续 10 分钟。环境启动、构建、迁移、数据准备和登录另行耗时。

## 执行

```powershell
.\perf\Run-Performance.ps1 -Vus 500 -Duration 10m
```

完整业务冒烟：

```powershell
.\perf\Run-Performance.ps1 -Vus 1 -Duration 45s -SubmissionIntervalSeconds 15 -PageDwellSeconds 10
```

会话刷新验证（默认访问令牌有效期为 15 分钟）：

```powershell
.\perf\Run-Performance.ps1 -Vus 1 -Duration 16m -SubmissionIntervalSeconds 60
```

也可以显式配置独立 Host 的真实令牌有效期，缩短刷新验收时间：

```powershell
.\perf\Run-Performance.ps1 -Vus 1 -Duration 3m -AccessTokenMinutes 2 -SubmissionIntervalSeconds 30 -PageDwellSeconds 20 -DiagnosticsMode None
```

持续时间超过令牌有效期时，额外要求出现成功刷新，以及初始令牌到期后的成功请求。生命周期使用 Host 的实际配置，测试不伪造过期时间。

| 参数 | 默认值 | 含义 |
|---|---:|---|
| `Vus` | 500 | 独立在线选手数量，必须为正整数 |
| `Duration` | 10m | 正式测量时间，支持 `45s`、`10m`、`1h30m` |
| `ThinkTimeSeconds` | 5 | 做题页面中两次选题动作之间的间隔，事件刷新继续运行 |
| `PageDwellSeconds` | 60 | 做题、记分板、本队页面之间的访问间隔 |
| `SubmissionIntervalSeconds` | 60 | 每个选手提交间隔的请求值 |
| `ReadP95Ms` | 500 | 各读取接口 p95 上限，毫秒 |
| `SubmitP95Ms` | 1000 | 提交接口 p95 上限，毫秒 |
| `EvaluationP95Ms` | 5000 | 提交至异步完成 p95 上限，毫秒 |
| `EvaluationTimeoutSeconds` | 30 | 单次异步结果最长等待时间，秒 |
| `Observability` | `$true` | 控制 NoCTF 应用 Observability，可设为 `$false` |
| `DiagnosticsMode` | Trace | `None`、`Counters` 或 `Trace`，独立于 Observability |
| `WarmupSeconds` | 30 | 认证完成后的读取预热时间，排除在测量阈值之外 |
| `TraceDelaySeconds` | 30 | 正式测量开始后等待多少秒采集轨迹 |
| `TraceSeconds` | 30 | 有界轨迹窗口；自动限制到剩余测量时间内 |
| `DiagnosticsIntervalSeconds` | 5 | 依赖与进程采样间隔 |
| `ServeFrontend` | 关闭 | 构建生产 SPA 并随独立 Host 提供，用于浏览器复查流程 |
| `AccessTokenMinutes` | 15 | 独立 Host 的真实访问令牌寿命；最小 2 分钟，可用于刷新验证 |
| `EntrySpreadSeconds` | 30 | 按 VU 编号分散入场；设为 0 可测试同时入场 |
| `Workload` | Participant | `Participant` 使用共享比赛页面与轻量导航能力接口；`Fixed` 保留原有热点接口集合，用于同类接口比较 |
| `ApplicationSnapshot` | 空 | 使用已保存的 Release Host 目录执行 `Fixed` 比较；报告保存实际程序集 SHA256 |
| `CaptureDatabasePlanning` | 关闭 | 记录 PostgreSQL 规划次数及耗时；用于诊断规划成本，比较运行必须保持相同设置 |

有效提交间隔为 `max(SubmissionIntervalSeconds, ceil(Vus * 60 / 960))` 秒，首次提交分散到一个有效间隔内。此配置遵守默认每 IP 每分钟 1200 次敏感请求额度，并保留容量余量。需要验证正确及错误提交时，测量时间必须覆盖首次提交偏移和至少一个提交间隔；较短运行可能因缺少业务样本而失败。

每次执行使用独立运行标识和 Compose 项目，基础设施端口由 Docker 分配并绑定 `127.0.0.1`。入口启动依赖、构建 Host、初始化存储、执行迁移、启动服务，然后通过 HTTP 创建比赛、题目、附件、账号和队伍。准备期间的认证请求之间等待至少 260 ms。

## 性能诊断与 Observability 开关

排行榜读取最近一次成功发布的不可变快照，后台计算期间继续返回现有计分结果。快照年龄只作为观测信息；权限及题目可见性仍依据当前访问配置。正式环境的 PostgreSQL 连接上限为 100，当前 Api、Worker 合并进程共享一个上限为 80 的应用连接池。

公开、未封榜、未启用赛道的 CTF 排行榜会按不可变发布版本复用序列化响应。每次请求仍验证当前身份、公开访问配置和快照访问修订；缓存关闭互斥等待，并分别保存 gzip 与普通表示。`scoreboard_response_cache_hits` 记录共享响应的命中率，`scoreboard_snapshot_age` 记录实际快照年龄。

Participant 按生产前端的固定 500 ms 窗口合并排行榜通知，窗口内继续展示已有快照。题目列表按题目配置、资格变化及本队解锁状态变化读取；题目解出数量、分数和血奖来自共享排行榜。Flag 受理和处理状态查询独立执行。Fixed 保留原有通知读取频率与接口集合。

测试环境停止后，可通过 `Inspect-Database.ps1 -RunId <RunId>` 读取保留数据库的 SQL 统计；通过 `-QueryFile` 执行只读诊断 SQL（例如 `EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)`）。该入口拒绝正在运行的测试，启动 PostgreSQL 后会在 `finally` 回收容器和网络。统计只写入本地报告目录。

`Inspect-Database.ps1 -RunId <RunId> -Projection` 会调用真实排行榜投影读取代码，逐条保存 PostgreSQL 的 `EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)` 结果。连接强制只读，输入凭据保存在 `.local/`；`database-plans/projection-plans.json` 按 SQL SHA256 列出规划与执行耗时。此操作重复执行读取，用于离线诊断，不能混入正式负载的延迟结果。

交替运行 Observability 开启、关闭各三轮，逐轮回收 Host 和 Compose，并保留所有失败报告：

```powershell
.\perf\Run-PerformanceMatrix.ps1
```

`results/matrix-*/matrix.json` 记录每轮标识与结果。读取 `results/<RunId>/diagnostics/inspection.json` 和原始汇总比较各接口分布，不能只选择最快轮次。正式测量期间避免同时构建或运行其他压测。

通过 `Compare-Performance.ps1 -RunIds <RunId1>,<RunId2>,<RunId3>` 生成逐轮性能、业务正确性、资源回收结果，以及各接口 p95 的中位数、最小值和最大值。结果同时保存为 JSON、CSV 和 Markdown，包含脚本、程序集与资源配置证据。

`Run-BackendComparison.ps1 -BaselineSnapshot <ReleaseHost目录>` 交替执行基线和当前代码各三轮 Fixed 接口负载，默认 50 VU / 90 秒。两种代码都使用 80 个应用连接及相同采集方式；基线失败不会中断后续比较，所有结果保留。此入口与正式压测应串行执行。

```powershell
# 开启应用指标与轨迹关联，采集运行时和依赖证据
.\perf\Run-Performance.ps1 -Vus 500 -Duration 10m -Observability $true -DiagnosticsMode Trace

# 关闭应用 Observability，保留运行时与依赖诊断
.\perf\Run-Performance.ps1 -Vus 500 -Duration 10m -Observability $false -DiagnosticsMode Counters

# 完全关闭外部诊断采集，记录低干扰的性能结果
.\perf\Run-Performance.ps1 -Vus 500 -Duration 10m -Observability $false -DiagnosticsMode None

# 重新生成诊断索引
.\perf\Inspect-Performance.ps1 -RunId <RunId>
```

`Observability` 控制 Host 自身的指标和请求观测中间件；`DiagnosticsMode` 控制本次测试附加的采集器。关闭 Observability 后 `/metrics` 返回 404，报告标记应用指标和服务端请求关联事件不可用；System.Runtime、Npgsql、进程与依赖采样仍可使用。诊断工具以 `perf/.config/dotnet-tools.json` 中的固定版本自动还原。

每次运行会复制 Release 程序、配置与 DLL/PDB，Host 使用该次运行的独立副本。环境、提交与修改状态、CPU、内存、电源模式、SDK、Runtime、工具版本、采集命令和安全配置记录在 `diagnostics/`。认证、读取预热、测量、结束和验证使用 UTC 标记；`samples.jsonl` 单独区分 setup、measurement 和 drain。

读取预热覆盖比赛、题目和排行榜。正式测量中的首次 Flag 评测仍包含提交路径首次使用成本；HTTP 请求附带 `first_use` 或 `steady` 窗口标签。提交与评测结果仍按原有业务断言验证。

`inspection.md` 与 `diagnostics/inspection.json` 提供诊断入口：

- `counters.csv`：GC、分配、线程池、网络、Npgsql 连接池，以及开启 Observability 时的应用阶段指标。计数器使用 20 秒有界采集段，保留分段 CSV 和合并 CSV，段间重新连接的间隔属于采集限制。
- `samples.jsonl`：Host 与 k6 的 CPU、私有内存、工作集、容器资源，以及 PostgreSQL、Redis、NATS JetStream 的同期状态。
- PostgreSQL：启用 `pg_stat_statements`，记录语句 queryId、执行统计、回滚、等待事件、阻塞 PID 和关系锁；不保存 SQL 文本或参数。QueryId 用于在受控数据库中继续检查具体查询。
- `runtime.nettrace`：采样线程时间、GC、线程池、竞争和安全的 `NoCTF-Performance` 事件；保留原始轨迹，同时生成 Speedscope、inclusive/exclusive 方法报告及三次线程快照。
- `trace-analysis.json`、`performance-events.jsonl`：通过固定版本的 Microsoft TraceEvent 解析原始轨迹，检查丢失事件数量，导出阶段中位数、p95/p99 和慢请求关联。阶段为嵌套测量，不应相加。
- `request-evidence.jsonl`：慢请求和错误请求的随机 TraceId、固定路由名称、状态、耗时和稳定错误代码。每个 VU 最多保存十条慢请求和十条失败请求；完整失败率以 k6 指标为准。

用 PerfView 打开 `runtime.nettrace`，在 Events 中筛选 `NoCTF-Performance`，使用请求样本的 TraceId 关联 `RequestCompleted` 和 `StageCompleted`。排行榜阶段区分本地锁等待、集群租约等待、已发布快照读取和时序校验；准入指标区分事务尝试、释放、重试与耗尽，并标记序列化冲突、死锁和其他失败。

也可以在环境仍然运行时独立采集一个有界窗口：

```powershell
.\perf\Collect-Performance.ps1 -RunId <RunId> -Seconds 60
```

独立采集保存在原运行目录的 `capture-<时间>/`，不覆盖原有证据。采集窗口需要同时有真实请求，才能分析请求耗时；空闲窗口仍可用于观察运行时和依赖状态。数据库诊断使用预先建立的持久只读查询连接，占用一个数据库连接位置，避免饱和时反复申请新连接。该开销记录为测量限制。

采集不启用异常正文、数据库命令和 DiagnosticSource 事件，也不采集进程转储。GC、采样方法及阶段事件保存在本地忽略目录，工具日志保留缺失符号、事件丢失或采集失败信息。诊断采集不完整会导致主入口返回非零退出码。

轨迹和线程快照会增加运行开销。定位原因后，使用相同参数、Observability 和 DiagnosticsMode 做前后测量，分别重复至少三次，比较中位数、波动范围和尾部延迟。单次诊断运行用于分析成本与等待，不直接证明优化收益。

## 业务与验收

每个 VU 匿名读取比赛列表、比赛详情和排行榜，使用独立选手身份读取题目列表、题目详情。首次提交正确 Flag，后续向另一道题目提交唯一错误 Flag。两道题目均使用真实静态附件、固定 100 分、无血奖、无限尝试次数。

k6 setup 建立各选手会话。测量阶段走原生浏览器附件授权和下载流程，下载真实内容后才能提交正确 Flag。回环 HTTP 环境下，k6 显式向已验证的同源附件路径转交服务端签发的 Secure Cookie，保持 Chrome 对可信回环地址的实际行为。访问令牌和刷新 Cookie 分别属于对应选手；运行期间在到期前刷新。所有提交包含 UUID `Idempotency-Key`，受理后优先响应状态事件；后备查询在 350 ms、1500 ms 后执行，之后间隔乘以 1.5，最多 8 秒。等待上限使用 `EvaluationTimeoutSeconds`。验证目标比赛、题目、队伍及最终 `Completed / Correct` 或 `Completed / Wrong`。失败提交不会自动重发。

负载依据生产静态前端的真实浏览器流程，详见 [比赛热点流程](journeys/ctf-participant.md)。每个 VU 在测量期间保持比赛和通知两条 WebSocket 连接，通过 Microsoft 官方 `JsonHubProtocol` / `HandshakeProtocol` 编解码，加入比赛并每 45 秒调用 `HeartbeatCompetition`。收到记分板版本更新后读取快照及本队排名，仅在目录或结构版本改变时重新读取对应内容；`202` 每 2 秒重试。事件通知分别触发题目列表、本队信息、播报和通知更新。同一 VU 的重复事件合并为一次待处理刷新。

VU 按做题 → 记分板 → 本队 → 做题访问页面。`ThinkTimeSeconds` 和 `PageDwellSeconds` 是可复现的行为假设，浏览器记录无法证明真实选手的停留时间分布。比赛列表与比赛详情在入场时读取，题目详情及附件列表在切换题目时并行读取。每次提交后查看本题记录。排行榜中的队伍详情由已有快照计算，浏览器点击未产生额外请求。

全局导航在实际路由变化时查询一次理论题库权限，使用 `theory_navigation` 独立计时；比赛列表同时读取本人可管理的比赛，使用 `managed_competitions` 计时。导航未读通知使用 `shell_notifications`（Inbox、20 条），比赛公告补查使用 `notifications`（比赛范围、200 条）。导航通知响应事件，并每 5 分钟进行一次补查。

每个热点操作独立保存 `operation_requests{endpoint:...}`、`operation_failed{endpoint:...}` 和 HTTP p95/p99；计数为零表示未覆盖。除令牌刷新外，所有热点操作都要求实际产生请求，短测试应缩短页面停留时间。`action_duration` 分别记录入场、选题及完整记分板读取；其 `p(95)>=0` 仅用于输出分组分位数，不代表产品 SLO。`hub_call_duration` 记录加入比赛和心跳，`hub_events` 记录有界事件类型，`scoreboard_event_to_snapshot` 记录收到版本更新至取得相应快照的时间。连接、协议及意外断开均记为业务失败；当前压测遇到连接中断终止该 VU 会话并保留失败。

当前场景覆盖 CTF 静态附件题的选手热点。容器启动/续期/停止、扣分提示解锁、题解上传与其他赛制需要各自真实题目及业务夹具，报告不将这些操作标记为已覆盖。HTTP 延迟包括网络和服务端时间；页面操作耗时包括所需请求，资源下载后的浏览器渲染时间不属于此协议负载指标。

读取接口和提交接口分别设置 p95 阈值，HTTP 失败率要求低于 1%，业务检查要求全部通过。准备阶段不进入正式场景的 HTTP 阈值。排行榜 `202` 记录为投影处理中；正式负载开始前已经取得快照，因此测量阶段排行榜、本队排名和趋势接口的 `projection_processing` 必须为零。Worker 重建期间应持续读取现有快照。`429`、`503`、网络错误、业务错误和处理超时都会影响验收结果。

运行结束后，管理员接口分页读取 GameplayFact，并等待排行榜投影完成，检查身份、结果、受理数量和计分。正确完成的队伍应获得 100 分，其他队伍应保持零分。

令牌到期造成的 SignalR 断开按正常会话轮换处理：使用已刷新的令牌重新建立连接、加入比赛，并补查记分板、本队信息、通知与播报。`hub_initial_connections` 验证每位选手最初建立两条连接，`hub_connections` 包含后续重建，`hub_expiration_disconnects` 单独记录到期断开。其他连接失败和协议错误记录为业务失败，失败 VU 停止业务动作直到测量结束，避免自动重新执行入场流程。

HTTP 入场请求按浏览器的数据依赖分组：浏览比赛 → 比赛详情及实时连接 → 比赛外层区域 → 题目导航与记分板 → 题目详情。默认在 30 秒内分散选手入场；两次性能对比应使用相同 `EntrySpreadSeconds`。指标端点或 PostgreSQL 诊断查询失效时记录探测失败并继续采集其他证据；报告仍将采集标记为不完整，保留故障发生时的运行时轨迹。

## 报告与直接执行 k6

每次运行保存在 `perf/results/<RunId>/`：

- `configuration.json`：请求参数、有效提交间隔、运行时间和 k6 退出码。
- `summary.json`：k6 指标及阈值结果，包括接口 p95/p99、吞吐量、失败率、异步耗时、提交数量和令牌刷新次数。`http_reqs{scenario:default}` 统计测量场景请求；原生总体 `http_reqs` 包含 setup 请求。
- `verification.json`：管理员最终业务验证结果。
- `verification.json` 中的 `databaseCompletion`：通过管理员接口取得真实 `OccurredAt` 和完成状态的 `UpdatedAt`，统计数据库完成耗时的 p50/p95/p99、最大值，以及超过 5 秒和 30 秒的数量。分位数使用 nearest-rank；与 k6 的客户端观察耗时分别报告，便于判断排队、通知及查询的影响。
- `k6.log`、`build.log`、`migration.log`、`host.log`、`host-error.log`：执行与诊断输出。
- 发生错误时保存 `dependencies.log`。

生成配置位于 `perf/.local/<RunId>/k6.json`，账号与凭据位于同一目录。两个运行目录均已忽略 Git 跟踪，日志禁止输出原始令牌和 Flag。

需要直接运行 k6 时，通过 `PrepareOnly` 准备环境和全新比赛：

```powershell
.\perf\Run-Performance.ps1 -Vus 500 -Duration 10m -PrepareOnly
```

每次测试必须使用新比赛。准备完成后，替换下面的 `<RunId>`，使用入口输出的配置路径执行：

```powershell
$config = (Resolve-Path perf/.local/<RunId>/k6.json).Path
k6 inspect -e "PERF_CONFIG=$config" perf/scripts/noctf-performance.js
k6 run --summary-mode full --vus 500 --duration 10m -e "PERF_CONFIG=$config" perf/scripts/noctf-performance.js
```

入口会在 `.local/<RunId>/workload/` 保存本次脚本与协议库副本及其 SHA256，正式测试使用该副本。重放时可将上面命令中的脚本路径替换为 `perf/.local/<RunId>/workload/scripts/noctf-performance.js`。

浏览器复查使用 `-PrepareOnly -ServeFrontend`。打开 `.local/<RunId>/state.json` 中的 `baseUrl`，使用同目录 `data.json` 的独立选手账号登录。通过 Playwright 的 `run-code --filename perf/support/Observe-BrowserJourney.js` 安装观察器后，在浏览器进行操作；`eval '() => window.perfJourney' --filename <报告路径>` 导出脱敏的请求耗时和 WebSocket 活动。观察器不会记录请求正文、响应正文或认证信息。

直接执行时 VU 数量应与配置中的账号数量一致，报告路径沿用配置。主入口负责额外的管理员结束验证及最终退出码。

已用 `PrepareOnly` 准备、尚未进入测量且没有 GameplayFact 的比赛，也可以通过完整测量入口运行，继续保留诊断和结束验证：

```powershell
.\perf\Run-PreparedPerformance.ps1 -RunId <RunId>
```

该入口验证 Host 进程身份、账号数量、测量标记和数据库提交数量；已经使用过的比赛立即拒绝运行。

## 停止与清理

正式测试成功或失败后，入口自动停止本次 Host，并执行对应 Compose 项目的 `down`；结果写入 `cleanup.json`。报告、配置和数据卷保留。`PrepareOnly` 保留环境供后续测试或浏览器使用；手动清理命令如下。

```powershell
.\perf\Stop-Performance.ps1 -RunId <RunId>
.\perf\Stop-Performance.ps1 -RunId <RunId> -RemoveData
```

停止入口校验 Host 进程的 PID、启动时间和进程名称，然后仅停止对应 Compose 项目。`RemoveData` 删除该项目的数据卷。报告和本地配置保留，便于后续检查。

每次运行均保留实际阈值及业务验证结果。主入口在依赖、准备、k6 阈值或最终验证失败时返回非零退出码。

已经停止的运行可以重新启动 PostgreSQL，检查保留数据中的实际完成耗时。诊断入口结束后自动回收该 Compose 项目的容器和网络：

```powershell
.\perf\Inspect-Database.ps1 -RunId <RunId> -QueryFile perf/support/Inspect-GameplayFactTiming.sql
```

输出位于 `results/<RunId>/database-plans/query-result.json`。`state`、`result` 使用领域枚举的持久化数值；完成状态 `3` 的耗时表示提交入库至数据库处理完成，其他状态的时间仅表示最近一次状态更新。

`noctf.leaderboard.decoded_cache.reads` 按 `hit`／`miss` 记录解码缓存命中情况，`noctf.leaderboard.decoded_cache.payload_size` 记录需要解码的源内容字符数量，`Leaderboard / Decode` 记录解码耗时。缓存为每个比赛原子替换当前不可变版本，并允许并发解码。`Caching:LeaderboardDecodedBudgetCharacters` 默认 `67108864`，使用按二次幂预留的字符容量限制缓存；源字符串与解码对象同时占用托管内存，因此该配置值不等于内存字节上限。

Trace 模式同时采集分配事件；`trace-analysis.json` 的 `sampledAllocations` 按类型汇总采样字节数量。它用于确定分配来源，不能直接当作精确的堆保留量；运行时 CSV 中的分配速率与 GC 指标独立保留。
