# NoCTF 压测基线

此脚本提供 k6 **开放式到达率**基线，用于比较相同业务组合下的 HTTP 延迟、业务准入和异步完成量。它不是容量实测结果，也不是完整的长稳或 Runtime 生命周期测试。只在明确获授权的隔离测试环境运行；脚本默认只允许本机目标，远程目标还需 `ALLOW_REMOTE_TEST_TARGET=I_UNDERSTAND`。

`BASE_URL` 必须是 HTTP(S) origin，例如 `http://127.0.0.1:8080`，不包含路径、凭据、查询或片段。脚本不关闭鉴权、MFA、人机验证、限流或一致性约束。登录未完成 MFA 或验证失败会结束 setup；写请求被拒绝会记录为失败，不会当作有效吞吐。

## 工作负载与口径

- `public_reads` 每次迭代读取比赛列表；配置 `COMPETITION_ID` 时再读取比赛详情和排行榜，共 **3 个 HTTP 请求**。比赛及榜单必须允许匿名访问。需要 Bearer 的 `/events` 不属于此公开读取场景。
- `MUTATION_OPERATION=none` 明确关闭认证场景，可单独运行公开读取。默认 `runtime-state`，必须提供账号、比赛和题目 UUID；缺少配置会立即报错，不会空跑通过。
- `runtime-state` 读取 `GET .../runtimes/current`，预期 200；未创建 Runtime 的 404 不算成功。
- `runtime-start` 调用 `POST .../runtimes`；`flag` 调用 `POST .../flag-submissions`。每次业务写请求生成新的随机 UUID `Idempotency-Key`，不自动重发写请求。
- 每个合法 202 响应必须包含操作 ID 和同源 `statusUrl`，随后轮询该地址，核对返回 ID。Runtime 只有对应实例进入 `Running` 才完成；Flag 只有有效时间内的 `Completed/Correct` 或 `Completed/Wrong` 才计入有效完成量。
- `Duplicate`、`Rejected`、`AttemptsExhausted` 等终态单独记录，不计入有效完成吞吐。仍在排队、状态接口持续失败、平台失败或观察超时也不算成功。HTTP 202 只代表准入，不代表异步处理完成。

轮询不跟随重定向，绝不把 Bearer 发往 `statusUrl` 指定的其他 origin。状态请求和等待时间属于同一次迭代；开放式 executor 仍按照计划开始新迭代，前提是有足够 VU。脚本不再用末尾 `sleep` 控制到达率；仅状态轮询间隔使用 `sleep`。

所有速率变量均为 **iterations/s（迭代/秒），不是 HTTP RPS**。HTTP 量还包括认证和状态轮询。写场景至少需要按「到达率 × 完成耗时」规划 VU，并留出尾延迟空间；`dropped_iterations > 0` 时该轮不能证明目标到达率下的容量。[k6 到达率与 VU 分配说明](https://grafana.com/docs/k6/latest/using-k6/scenarios/concepts/arrival-rate-vu-allocation/)

## 本机执行

先在当前进程环境中设置专用测试账号和 UUID，凭据不要直接写入命令历史、脚本或结果文件。下面的命令会实际发起负载，只有获得测试环境运行授权后才能执行：

```powershell
docker run --rm --add-host host.docker.internal:host-gateway `
  -e BASE_URL=http://host.docker.internal:8080 `
  -e COMPETITION_ID -e COMPETITION_CHALLENGE_ID `
  -e NOCTF_LOGIN -e NOCTF_PASSWORD `
  -e MUTATION_OPERATION=runtime-state `
  -v ${PWD}/tests/load:/scripts:ro `
  grafana/k6:1.4.2 run /scripts/noctf-baseline.js
```

仅测公开读取时设置 `MUTATION_OPERATION=none`，不需要账号或题目 UUID。Flag 场景还需通过环境传入 `FLAG_VALUE`。账号、队伍、题目和比赛必须预先建立，脚本不会替你创建、重置或清理业务数据。

### 账号池

支持单账号 `NOCTF_LOGIN` / `NOCTF_PASSWORD`，或者通过正常认证流程取得的 `NOCTF_ACCESS_TOKEN`。多账号使用 `NOCTF_ACCOUNTS_JSON` 环境变量，结构如下；示例中的值均为占位符：

```json
[
  { "login": "PERF-user-01", "password": "<secret>" },
  { "accessToken": "<legitimately-issued-access-token>" }
]
```

账号池优先于单账号配置；为 Docker 增加 `-e NOCTF_ACCOUNTS_JSON` 即可传入当前环境值。每个条目必须有完整账号密码或 Access Token，脚本按认证场景的迭代序号轮转账号。同一账号仍可能有并发请求，用户配额、队伍限制以及共享出口 IP 配额照常生效。请按目标负载配置足够的独立测试用户和队伍，不能通过关闭配额来制造吞吐量。

账号密码在 setup 中逐个登录；setup 本身受认证 IP 限流和默认超时约束。大量账号可使用经正常登录/MFA 流程签发的 Token，或者明确调整 k6 的 setup 超时，但不要绕过服务端安全验证。本脚本不提供验证码破解、复用一次性验证票据或模拟 MFA 成功功能；开启这些安全门槛的写场景，需要另外接入正规的测试验证流程，403 不能计作业务成功。

**不实现 Token 自动刷新。** setup 的登录 Token 或预先提供的 Token 到期后，401 会记录为失败；不能将这样的运行视作完整长稳测试。长测需要独立实现按账号隔离的刷新 Cookie/MFA 流程，并将刷新负载纳入业务模型，或在真实有效期内结束当前基线。不要延长平台 Token 有效期来掩盖测试工具缺口。

## 配置

| 环境变量 | 默认值 | 含义 |
| --- | ---: | --- |
| `READ_START_ITERATIONS_PER_SECOND` | 5 | 公开读取初始迭代率 |
| `READ_TARGET_ITERATIONS_PER_SECOND` | 25 | 公开读取目标迭代率 |
| `RAMP_DURATION` / `STEADY_DURATION` / `COOLDOWN_DURATION` | 2m / 5m / 1m | 升压、稳态、降载窗口 |
| `READ_PREALLOCATED_VUS` / `READ_MAX_VUS` | 20 / 100 | 读取场景 VU 预算 |
| `MUTATION_OPERATION` | runtime-state | `none`、`runtime-state`、`runtime-start`、`flag` |
| `MUTATION_ITERATIONS_PER_SECOND` | 1 | 认证场景迭代率 |
| `MUTATION_DURATION` / `MUTATION_START` | 5m / 30s | 认证场景持续时间和开始时间 |
| `MUTATION_PREALLOCATED_VUS` / `MUTATION_MAX_VUS` | 5 / 20 | 认证场景 VU 预算 |
| `REQUEST_TIMEOUT` | 10s | 单请求超时，支持正数及 `ms`、`s`、`m`、`h` 后缀 |
| `COMPLETION_TIMEOUT_SECONDS` | 120 | 从业务 POST 开始计算的完成观察预算 |
| `POLL_INTERVAL_SECONDS` | 1 | 状态轮询间隔 |

兼容旧变量 `READ_START_RPS`、`READ_TARGET_RPS`、`MUTATION_RPS`，但它们仍表示迭代率；新变量优先。认证场景的 `gracefulStop` 自动覆盖完成观察预算、请求超时及额外 15 秒，以便停止新增迭代后处理已接受操作。

## 指标与验收

| 指标 | 解释 |
| --- | --- |
| `http_reqs`、`http_req_failed`、`http_req_duration` | 全部 HTTP 请求，包含登录及状态轮询；按固定 `name` / `endpoint` 标签区分 |
| `noctf_operation_errors` / `noctf_operation_latency` | 每次 HTTP 请求是否满足该路径的预期状态、HTTP 延迟 |
| `noctf_rate_limited` / `noctf_unavailable` / `noctf_rejected` | 分别为 HTTP 429、503、其他 4xx；全部仍进入 HTTP 错误率 |
| `noctf_pending_reads` | GET 返回 202 的次数，未计作成功读取 |
| `noctf_mutation_attempts` | 发起的业务写请求数 |
| `noctf_mutation_accepted` | 202 且操作 ID、同源状态地址有效的准入数 |
| `noctf_mutation_terminal` | 已观察到的 Flag Completed 结果或 Runtime Running，按有限结果标签分类 |
| `noctf_mutation_completed` | 有效 Flag 判定或对应 Runtime Running 的完成数，不含重复、拒绝或失败 |
| `noctf_mutation_failures` / `noctf_completion_errors` | 202 后协议异常、业务拒绝、平台失败、实例被替换、持续 Pending 超时等 |
| `noctf_completion_observation_duration` | 从 POST 开始到终态或停止观察的时长，按 `outcome` 区分；超时为截断观察，不是实际服务完成延迟 |
| `dropped_iterations` | 无 VU 等原因导致无法按计划启动的迭代；阈值必须为零 |

默认 HTTP 错误预算为 `<2%`，P95 `<800ms`、P99 `<2000ms`，这是初始基线门槛，不代表平台已经达标。业务写场景还要求至少一个有效准入和有效完成，且所有被观察的 202 操作完成错误率为零。**寻找最大可持续吞吐量时，目标稳态负载下还必须没有非预期 429/503/其他拒绝，不能把默认 2% 预算当作允许丢弃业务的容量定义。**

按 `deploy/docker/observability/README.md` 同时记录 API、Worker、PostgreSQL、Redis、NATS、Runner 指标，以及 CPU、内存、GC、连接/锁等待、队列长度与最老消息年龄。使用独立预热窗口，在稳态时间段计算实际有效完成率；全程汇总混合了升压、降载和排空阶段，不等于稳态吞吐上限。

## 限制与结果记录

- `runtime-start` 不自动 Stop/Reset。一个队伍已有运行中的实例时，后续 Start 的 409 是拒绝；要测持续启动/销毁能力，需要另行编排明确的生命周期工作负载。该脚本可测试预先准备好的独立队伍启动，不能用反复 409 代表启动吞吐。
- 常量正确 Flag 可能只产生一次新成绩。CTF 允许再次正常判为 Correct，但不重复得分；其他路径的 Duplicate/409 不算有效完成。应区分事实完成数与新增成绩数，错误 Flag 也受尝试次数等业务限制。测试数据、账号池和业务混合必须覆盖目标持续时间。
- 轮询会增加鉴权和数据库负载；本脚本测得的是包含该客户端策略的业务链路。没有测量结果到排行榜可见、SignalR、AWD 批量 Flag、Checker 截止时间或媒体容量。
- 正常结束时核对 HTTP 202、有效准入、完成、失败和各结果计数，并检查服务端是否仍有积压。被强制中断、Token 到期、发生器丢迭代或仅得到 Pending 的运行，不能登记为成功容量结果。
- 保存提交/工作树状态、镜像、k6 版本、数据种子、阶段时间区间、资源配置、原始时序及汇总。前后对比使用相同环境和业务分布；不要记录 Bearer、密码、原始 Flag 或开启携带这些内容的 HTTP debug。测试数据使用预先分配的 `PERF-*` 标识，结束后精确清理获授权的数据。

语法与初始化检查可以在断网容器中执行，不会调用 setup 或目标 API：

```powershell
docker run --rm --network none -v ${PWD}/tests/load:/scripts:ro `
  grafana/k6:1.4.2 inspect -e MUTATION_OPERATION=none /scripts/noctf-baseline.js
```

这类检查只能证明脚本可解析、配置可初始化，不能证明实际业务请求、容量或延迟达标。
