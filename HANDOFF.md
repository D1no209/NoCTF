# NoCTF 数据模型重构交接

## 2026-08-24 数据模型与 Wolverine 简化：阶段 11

- 当前分支：`codex/data-model-wolverine-simplification`；阶段 11 父提交为 `4f3d3d99`，版本已递增为
  `0.1.0-alpha.80`。本节随阶段 11 本地提交交付；未推送、未部署，未操作生产数据库、生产 Wolverine
  队列、生产 Redis、生产 Runner、对象存储或生产数据。
- OpenAPI 与 TypeScript SDK 已用仓库工具重新生成，两次复跑哈希一致；EF model drift 无变化，单一
  InitialBaseline 仍严格对应 15 张业务表。没有手改 migration、snapshot 或生成 SDK。
- 阶段收尾修复了独立 Worker 开发宿主依赖 Wolverine 内部控制器、Docker 并发网络创建/重复删除、
  Runner 对账错误遍历未启用 provider、AWDP 缓存跨轮有效期和 KoH adjudication 未发布排行榜失效事件
  五类问题；AWDP/KoH 完整 E2E 已同步到新 Runtime、Fix、隐私与事件驱动语义。
- 最终验证：Release build 0 warning/0 error；完整 TUnit 1091 项中 1089 通过、0 失败、2 跳过；跳过项
  分别是未启用的真实 Kubernetes provider 与未提供磁盘路径的真实 Libvirt provider，未当作通过。
  CTF/AWD/AWDP/KoH 完整 E2E 和 API/PostgreSQL/Redis/Worker/Runner 恢复场景通过；前端 275/275、
  2029 assertions，typecheck、production build、static generate 通过。完整命令、哈希与证据见
  `docs/data-model-wolverine-stage11-validation.md`。
- 全新共享测试环境部署、浏览器发布验收、生产快照转换、停机窗口、负责人签字和 Go/No-Go 因当前未
  授权而未执行。不得把本地 Testcontainers/开发备份恢复演练解释为生产切换完成；执行前必须遵循
  `docs/data-model-wolverine-cutover.md`。
- 恢复：checkout 本阶段提交后执行
  `dotnet build backend/NoCTF.slnx --configuration Release --no-restore`、
  `dotnet test backend/NoCTF.slnx --configuration Release --no-build`，再进入 ClientApp 执行
  `bun run test`、`bun run typecheck`、`bun run build`。下一步只能在主线程明确授权后构建发布镜像、
  部署全新测试环境并进入正式 Go/No-Go；不要直接连接旧生产库执行 InitialBaseline。

## 2026-08-24 数据模型与 Wolverine 简化：阶段 10

- 当前分支：`codex/data-model-wolverine-simplification`；阶段 10 父提交为 `681138cc`。
  本节随阶段 10 功能提交交付。未推送、未部署，未操作生产数据库、生产 Wolverine 队列、对象存储
  或生产数据。
- 旧七个源码 migration 已通过 EF CLI `migrations remove` 逆序移除，没有直接删文件或手改 snapshot；
  EF CLI 生成新的单一 `20260824060413_InitialBaseline`。Release `migrations list` 只含该项，
  `has-pending-model-changes` 返回无漂移。
- 一次性开发 PostgreSQL 已完成旧 migration 链应用、custom-format backup 和独立库 restore；开发证据
  位于被忽略的 `backend/artifacts/stage10/`，哈希与生产转换/停机/回滚边界见
  `docs/data-model-wolverine-stage10-ef-baseline.md`。这些文件不是生产备份，也不会提交。
- 新空库一次应用成功，public schema 恰好 15 张业务表加 EF 历史表；删除的 `data_exports`、Revision、
  Dirty 和旧 Runtime 调度字段不存在。数据库共有 15 个业务主键、36 个外键、36 个 check constraint、
  81 个 public index，外键无 Cascade。
- `DataModelSchemaTests` 现在使用真实 `MigrateAsync`；定向真实 PostgreSQL/Testcontainers 2/2 和
  migration 架构测试 1/1 通过，
  并验证 Api/Worker/Runner 生产持久化配置能在同库建立 `wolverine_api`、`wolverine_worker`、
  `wolverine_runner` durable schema。Release 测试项目构建 0 warning/0 error；阶段 10 的精确开发容器
  `noctf-stage10-pg-20260824` 已删除。
- 恢复：从本提交 checkout 后运行阶段文档中的 EF list/model-drift 和两项 schema 集成测试；不要连接
  旧生产库尝试原位 migration。下一步只能进入阶段 11，统一 OpenAPI/SDK/文档/Alpha 版本并运行完整
  测试矩阵；全新测试环境、推送、部署和生产切换仍未授权。

## 2026-08-24 数据模型与 Wolverine 简化：阶段 9

- 当前分支：`codex/data-model-wolverine-simplification`；阶段 9 父提交为 `5d2dc1df`。
  本节随阶段 9 功能提交交付。未推送、未部署，未操作生产数据库、生产队列、对象存储或生产数据。
- 排行榜已删除 `LeaderboardDirty`、15 秒扫描与临时 refresh tick，改为已提交比赛事件驱动：
  leaderboard Sticky PostgreSQL Handler 立即写 Redis tombstone fence 并删除缓存，在 Wolverine leader
  进程内按比赛 fixed 500ms 合并；同一 leader 的 Singular Agent 只 durable 派发
  `ProjectLeaderboard`，projection competing consumer 从 PostgreSQL 全量重建并发布。
- cache miss 使用比赛级 keyed lock 从 PostgreSQL 同步重建；Redis/Fusion 丢失可恢复。失效时递增的
  tombstone fence 阻止已经在途的旧投影晚到恢复过时缓存。leader 在内存合并窗内退出时不补跑 tick，
  因缓存已失效，后续读取或业务事件会恢复投影。
- `noctf-events-leaderboard` 必须是 pinned-to-leader durable PostgreSQL listener；缺失 Sticky、退化
  `local://`、scope 错误、重复 Handler 或全局 separated behavior 均 fail closed。监控已由 dirty scan
  指标切换为 invalidation、merge、cache miss、projection 与 publish failure 低基数指标。
- 验证：Release build 0 warning/0 error；合并窗 4/4；真实 Redis fencing 4/4；真实 PostgreSQL/Redis
  投影 9/9；真实 PostgreSQL/Wolverine fan-out 3/3；双 Worker durable 合并派发/failover 1/1；拓扑
  8/8；监控 3/3；前端 275/275、typecheck/build 通过；`git diff --check` 无 whitespace error。
  完整证据见 `docs/data-model-wolverine-stage9-leaderboard.md`。
- OpenAPI/生成 SDK 在阶段 11 统一刷新，当前 `wwwroot/openapi/v1.json` 中旧 dirty 监控 enum 是待生成
  产物，不允许手改。下一步只能进入阶段 10：先保存旧 schema 与生产备份/转换/切换/回滚方案，再仅用
  EF CLI 在可丢弃开发数据库上移除旧 migration 并生成单一 `InitialBaseline`，验证最终 15 张业务表。

## 2026-08-24 数据模型与 Wolverine 简化：阶段 8

- 当前分支：`codex/data-model-wolverine-simplification`；阶段 8 父提交为 `55e26af4`。
  本节随阶段 8 功能提交交付。未推送、未部署，未操作生产数据库、生产队列、对象存储或生产数据。
- 周期调度已迁移到 Wolverine 6.29.2 集群 `SingularAgent`：AWD Round、AWD Checker、KoH Poll
  与比赛生命周期由唯一活动 Agent 从 PostgreSQL 当前事实重建内存优先队列并只派发 durable 消息。
  AWD Round、KoH Poll 和生命周期 Handler 不再递归创建 Wolverine Scheduled Message。
- 停机恢复采用 no-catch-up：逾期计划最多在接管时执行当前一次，下一次从当前时刻继续。AWD 按当前
  有效运行时长恢复当前轮次；没有合格参赛队伍时不制造空轮次，队伍后续加入时直接从当前轮次开始。
  KoH Poll 以题目和 due time 派生确定性 FactId，重投幂等。
- readiness 现在 fail closed 地核验 Wolverine balanced agent assignment、当前 leader、scheduler owner、
  接管时间、本地重建状态和诊断租约；Redis 只保存 15 秒低敏诊断租约，不参与业务选主。
- 验证：Release build 0 warning/0 error；调度时钟/KoH FactId 单元 2/2；真实 PostgreSQL AWD 当前轮次
  恢复与无空轮次 3/3；真实 PostgreSQL/Wolverine 双 Worker 唯一 Agent 和 owner 停止后 failover 1/1。
  完整阶段证据见 `docs/data-model-wolverine-stage8-singular-agent.md`。
- 本阶段没有业务表、EF migration、HTTP/OpenAPI 或 TypeScript SDK 变化。阶段 9 前临时保留由同一
  Agent 派发的 15 秒排行榜刷新；下一步只能进入阶段 9，删除 `LeaderboardDirty`、旧扫描和临时 tick，
  实现事件驱动失效、500ms 合并、全量 PostgreSQL 投影、cache miss keyed-lock 重建与乱序保护。

## 2026-08-24 数据模型与 Wolverine 简化：阶段 7

- 当前分支：`codex/data-model-wolverine-simplification`；阶段 7 父提交为 `b70f8546`。
  本节随阶段 7 功能提交交付。未推送、未部署，未操作生产数据库、生产队列、对象存储或生产数据。
- 四类单消费者工作继续使用命名 PostgreSQL queue 和 durable inbox，由多 Worker 作为 competing
  consumers 消费。`CompetitionEventCommitted` 已拆成 realtime 与 leaderboard 两个唯一 Sticky
  Handler，分别绑定同名 durable PostgreSQL endpoint；MessageIdentity 固定为 `IdAndDestination`，
  一个订阅者失败不会阻塞另一个。
- 新增启动期 fail-closed 拓扑校验：在 Wolverine transport 初始化后的 `StartedAsync`、Host 宣告启动
  成功前核对 Sticky 名称、同名 PostgreSQL listener、durable mode、唯一 Handler 与路由，并拒绝
  Wolverine 6.29.2 的静默
  `local://` fallback 或全局 `MultipleHandlerBehavior.Separated`。开发期 stub host 显式关闭该生产
  校验，生产 Worker/Host 强制启用。
- 基础设施瞬时故障按 control/gameplay/projection/background/runner queue 使用不同 retry schedule；
  确定性业务失败不无限重试，旧 Revision 并发异常重试已删除。六个稳定 Worker/fan-out queue 均采集
  backlog 与 oldest-age；完成/失败/执行耗时/dead-letter 使用 Wolverine 原生低基数 OpenTelemetry
  destination/message/exception 标签和关联 trace。
- 验证：Release build 0 warning/0 error；非 Integration TUnit 899/899；WorkerRole 定向单元 7/7；
  transactional outbox 10/10；真实 PostgreSQL/Wolverine
  拓扑 3/3，覆盖双 fan-out 各一次、订阅者故障隔离、缺 Sticky endpoint 真实 Host 启动失败、两个
  competing consumer 在 PostgreSQL 重启后处理 24 条消息逐条严格一次及 replay probe 无重复。
  完整阶段证据见 `docs/data-model-wolverine-stage7-messaging-topology.md`。
- 本阶段没有数据模型、migration、HTTP/OpenAPI 或 SDK 变化。下一步只能进入阶段 8 Singular Agent：
  从 PostgreSQL 事实重建周期任务、只派发 durable 消息、多 Worker 单活动 Agent、failover 与停机不补跑；
  不得在阶段 8 提前实现阶段 9 的排行榜 500ms 合并投影。

## 2026-08-24 数据模型与 Wolverine 简化：阶段 6

- 当前分支：`codex/data-model-wolverine-simplification`；阶段 6 父提交为 `851fc689`。
  本节随阶段 6 功能提交交付。未推送、未部署、未操作生产数据库、生产队列、对象存储或生产数据。
- GameplayFact 现在是 AWD Checker 与 AWDP Fix 的唯一权威结果事实。每次 AWD Checker 执行预创建
  独立事实，健康、不健康和回调超时均按同一个 FactId 幂等收敛为 `Completed`；服务当前状态从最新
  `(OccurredAt, Id)` 的已完成事实派生，不再依赖“只有 Up/Down 变化才记录”的旧转换语义。
- AWDP 六类 Fix outcome 已固定映射：DefenseSucceeded 为 Correct，ExploitSucceeded 为 Wrong，
  ServiceAbnormal、PatchFailed、PatchTimeout、PlatformFailed 为 Rejected，且全部收敛到 Completed。
  新增 schemaVersion 1 的强类型 `AwdpFixResolved` Payload，只包含事实、Patch、Runtime、队伍、题目、
  outcome、稳定失败码和时间，不写入 Flag、Patch 内容、凭据或 Runner stderr。
- AWDP Checker 回调只接受当前三个业务枚举值，删除旧协议别名；排行榜只读取合法已完成 GameplayFact。
  `AwdpPlatformFailed` 追加在失败码枚举末尾，避免改变当前阶段数据库里已有枚举整数含义。
- 验证：Release build 0 warning/0 error；非 Integration TUnit 896/896；阶段单元 9/9；真实
  PostgreSQL/Wolverine 定向 2/2、transactional outbox 10/10；ClientApp 275/275、2029 assertions，
  typecheck/build 通过；OpenAPI/SDK 双次生成哈希一致；`git diff --check` 通过。
- 全量 TUnit 共 1074 项：1068 通过、4 失败、2 跳过。4 项仍断言阶段 8/9 才删除的 scheduled
  successor、AWDP 全表轮询和 `LeaderboardDirty` 扫描/即时投影旧语义；2 项因未配置真实
  Kubernetes/Libvirt。当前 EF model drift 是阶段 10 单一 InitialBaseline 前的预期状态，未手改
  migration/snapshot。精确证据与恢复边界见 `docs/data-model-wolverine-stage6-gameplay-facts.md`。
- 下一步只能进入阶段 7 Wolverine competing consumers 与显式 fan-out：必须复用阶段 0 对
  Wolverine 6.29.2 的真实 Spike 结论，补 Sticky endpoint fail-fast，并保持 EF transactional outbox、
  durable inbox 和每条独立队列的重试/死信语义；不得提前修改 Singular Agent 或排行榜投影。

## 2026-08-24 数据模型与 Wolverine 简化：阶段 5

- 当前分支：`codex/data-model-wolverine-simplification`；阶段 5 父提交为 `f3767f58`。
  本节随阶段 5 功能提交交付。未推送、未部署、未操作生产数据库、生产队列、对象存储或生产数据。
- Runtime 已缩减为资源与生命周期事实：删除 AWDP Fix stage、generation/replacement、runner pool、
  assignment release/unavailable、参与者 URL index、control/checker URL、Checker 状态/sequence/deadline、
  PublishedPort 分配时间等冗余列。Reset 现在创建全新 Runtime UUID，旧行作为历史保留并停止。
- 删除 pool Claim 消息、`IRunnerPoolMessage`、pool queue 命名与 Runner Claim Handler。Worker 从
  Redis Runner Registry/Capacity 选择具体节点，在业务事务中写入 `RunnerId` 并通过 EF transactional
  outbox 直投 `runner-node-{runnerId}` Sticky endpoint；Runner 只监听自己的 node queue。
- 节点消息不再携带 generation/pool。相同 MessageId 由 Wolverine durable inbox 幂等；不同 Envelope
  采用 last-completer-wins。资源停止和清理只以 RuntimeId、RunnerId、provider 与真实 Provider Receipt
  为依据；无 Receipt 但残留 RunnerId 的失败实例会先由节点确认资源不存在，再释放容量和分配。
- AWD Checker 回调改用预创建的 GameplayFactId；Runtime 参与者 URL 按读取时最新题目定义过滤，
  管理 API/OpenAPI/SDK/前端不再暴露 generation、pool、receipt 和 checker/control 内部字段。
- 验证：Release build 0 warning/0 error；非 Integration TUnit 896/896；ClientApp 275/275、typecheck、
  production build；OpenAPI/SDK 双次生成哈希一致；`git diff --check` 通过。真实 PostgreSQL/Redis
  定向测试覆盖 Reset 新 UUID、多 Runner 节点直投、不同 Envelope 完成顺序、URL 最新定义过滤、
  Receipt 清理及无 Receipt 失联容量释放。
- 完整 Integration 共 177 项：171 通过、4 失败、2 跳过。2 项只因未配置真实 Kubernetes/Libvirt；
  4 项均断言阶段 8/9 将删除的旧 AWDP 全表轮询、`LeaderboardDirty` 扫描/即时重投影和 Wolverine
  scheduled successor，未将其误报为阶段 5 回归或伪造通过。阶段 6 不得修改这些后续阶段测试。
- migration/snapshot 仍保留旧基线；阶段性 Integration 使用 `EnsureCreated` 验证当前关系模型，阶段 10
  必须恢复 migration/model-drift 门禁并只通过 EF CLI 生成单一 InitialBaseline。恢复时先阅读
  `docs/data-model-wolverine-stage5-runtime-routing.md`，确认工作区和最新提交，再只进入阶段 6
  GameplayFact 与 AWDP 强类型结果闭环。

## 2026-08-24 数据模型与 Wolverine 简化：阶段 4

- 当前分支：`codex/data-model-wolverine-simplification`；阶段 4 父提交为
  `2dcdc59a`，本节随阶段 4 功能提交交付。未推送、未部署、未操作生产数据库、生产队列、
  对象存储或生产数据。
- 已全链删除 `DataExport` 领域实体、枚举、DbSet、配置、Store、Processor、Application 用例、
  Wolverine 消息/Handler、异步列表/状态/下载 API、通知类型、实体引用、硬删除引用及前端轮询。
  仅保留 `POST /admin/competitions/{competitionId}/data-export` 与
  `POST /admin/platform/audit-logs/data-export` 两个强类型同步 ZIP 端点。
- 新导出在请求内以 PostgreSQL `RepeatableRead` 读取一致快照，先完成范围和受保护 Flag 鉴权，
  再写入受控临时 ZIP；设置总记录数、压缩后字节数、单记录序列化内存和执行时间上限。客户端
  取消立即传播；失败和超限返回稳定枚举失败码，不创建 `File`、业务导出记录、通知任务或后台消息，
  临时文件在失败时删除、成功流关闭时 `DeleteOnClose` 删除。
- 普通比赛导出对 Flag 明文做脱敏；明文只允许人类平台管理员并要求 8—512 字符理由。成功比赛导出
  写工作人员可见 `CompetitionArchiveExported` 事件，成功平台审计导出写强类型
  `PlatformAuditExported` 管理审计事实；两者都不记录导出内容或明文 Flag。
- OpenAPI 与 TypeScript SDK 已由工具重新生成并二次校验哈希一致；前端删除任务列表和轮询，改为
  直接调用生成 SDK 下载，保留 protected-Flag 理由输入、loading、防重复和失败表单状态。
- 验证：Release build 0 warning/0 error；真实 PostgreSQL 同步归档 3/3、Application 用例 3/3、
  当前关系模型 15 表守卫 1/1、Architecture/OpenAPI 路由守卫均通过；ClientApp 275/275、
  typecheck、production build 通过；`git diff --check` 通过。测试覆盖成功 ZIP、脱敏、权限、
  Forbidden、NotFound、取消、记录/压缩字节/时间/内存超限、审计和无临时/业务残留。
- migration/snapshot 仍故意保留旧基线，因阶段 10 独占 EF CLI 基线重建；旧 migration 中仍可看到
  历史 `DataExport`，不得提前手改。恢复时先阅读
  `docs/data-model-wolverine-stage4-streaming-exports.md`，运行其中命令；下一步只能进入阶段 5
  Runtime 最小模型与节点直投。

## 2026-08-24 数据模型与 Wolverine 简化：阶段 0—3

- 当前分支：`codex/data-model-wolverine-simplification`；权威规范为
  `docs/data-model-wolverine-simplification.md`。阶段 0 提交 `1a195f6c`，阶段 1 提交
  `e264d49f`，阶段 2 提交 `371fd93d`，阶段 3 为本节所在提交。未推送、未部署、未操作生产数据库或生产队列。
- 阶段 0 已同步治理文档、保存 schema/契约/测试基线，并以 Wolverine `6.29.2` 对真实
  PostgreSQL 完成最小 Spike：PostgreSQL transport、durable inbox/outbox、EF transactional
  outbox、competing consumers、fan-out、`IdAndDestination` 与 Singular Agent 均有版本实证；
  同时确认缺失 Sticky endpoint 会静默退化到 local queue，阶段 7 必须补启动 fail-fast。
- 阶段 1 已删除持久化 Revision、ExpectedRevision、ConcurrencyToken、处理版本与相关 HTTP/
  OpenAPI/SDK 伪乐观并发协议，保留 JWT Token、定义 schema、事件 payload 和外部协议的真实版本；
  普通更新采用 last-write-wins，业务不变量继续使用 PostgreSQL 锁、稳定 UUID 和 durable inbox。
- 阶段 2 已删除 canonical Email 双写与公开邮箱开关、队伍规范化名称/唯一约束、比赛配置时间、
  Runtime 累加缓存、旧榜单可见性/冻结快照/Dirty 状态、AWD 下一轮调度列。新增两个榜单可见性
  时间戳、生命周期事件派生有效运行时间，以及明文赛道邀请码的固定时间比较；邀请码只向
  Owner/Manager/Platform Admin 的管理协议回显。公共用户资料不再含邮箱，重复队名以稳定
  TeamId 前 8 位在同屏消歧。
- 阶段 2 验证：Release solution build 0 warning/0 error；定向 Unit `27/27`、Architecture
  `52/52`、真实 PostgreSQL/Testcontainers `5/5`；前端 `bun test` `274/274`、typecheck 和
  production build 通过；OpenAPI/SDK 重新生成幂等，`git diff --check` 通过。
- 当前 migration/snapshot 故意仍是旧基线；权威顺序要求阶段 10 才能通过 `dotnet ef` 删除并
  重建单一 `InitialBaseline`，因此阶段 1—9 的完整旧 migration Integration 套件会出现预期
  model drift，不得误报为通过，也不得手改 migration。当前模型的阶段性关系约束使用
  `EnsureCreated` 的可丢弃 Testcontainers PostgreSQL 验证。
- 数据切换风险：canonical Email 冲突必须人工处理；旧赛道邀请码哈希不可逆，需要 Owner/Manager
  在切换前重设；旧冻结快照不迁移，切换后从 PostgreSQL 事实重建。任何一项责任人或转换方案
  不成立，必须在阶段 10/11 暂停，不能以清库替代生产迁移。
- 阶段 3 为 Notification 增加可空 `ThreadRootId`：咨询根为 null，回复/状态节点直接指向根；
  `ReplyToId` 退回为非唯一的可选回复上下文。咨询查询不再递归 ReplyTo 链，统一按
  `(SentAt, Id)` 稳定排序并从最后一个状态语义节点确定当前状态。Question HTTP 协议、通知载荷、
  OpenAPI、生成 TypeScript SDK 与前端读取状态全部改用 `threadRootId`，不再生成或兼容旧
  `questionId` 通知载荷；来源队伍、题目所有者、裁判/管理员和观察者权限保持不变。
- 阶段 3 验证：Release build 0 warning/0 error；定向 Unit/HTTP 20/20；真实 PostgreSQL
  `CompetitionQuestionPersistenceTests` 2/2（含并发回复、稳定顺序、隐私和 append-only 根保护）；
  ClientApp 275/275、typecheck、production build；OpenAPI/SDK 二次生成 SHA-256 不变；
  `git diff --check` 通过。旧 `MigrateAsync` 集成测试仍因阶段 10 前的预期 model drift 被阻塞，
  未报告为通过。
- 阶段 3 已完成。恢复前先执行
  `git status --short`、`git log --oneline -5`，再阅读
  `docs/data-model-wolverine-stage2-core-privacy.md`、
  `docs/data-model-wolverine-stage3-notification-threads.md`；下一步只能进入阶段 4 同步流式导出，
  禁止提前跳到 migration 或调度阶段。

## 2026-08-23 Alpha.79 私有指标与平台监控入口

- 平台管理的“运维”分组新增“监控”页面，只展示 API、实时连接、关键 Wolverine 队列、
  Runtime 等待、排行榜投影、Runner 容量、PostgreSQL、Redis 和磁盘空间的固定低基数摘要；
  页面通过新生成的 `AdminPlatformGetMonitoring` SDK 调用管理员专用强类型接口，每 15 秒仅在
  页面可见时刷新。前端没有 PromQL 输入、原始指标代理或写死的 Prometheus/Grafana 地址。
- API、Worker 与 Runner 的 OpenTelemetry Prometheus 抓取端点迁移到容器内专用 `9464`
  监听器。业务监听器请求 `/metrics` 返回 404，专用监听器除精确 `/metrics` 外同样返回 404；
  Compose 只在内部网络 `expose` 该端口，不发布到宿主机。Prometheus 与 Grafana 管理端口仅绑定
  服务器 `127.0.0.1`，完整 Dashboard 通过 SSH 隧道或另行配置的受保护入口访问。
- 后端只执行预定义 PromQL 并返回枚举化指标、单位和健康状态；具体 ID 不进入标签。Prometheus
  不可用时接口返回可用的降级快照而非暴露内部异常，页面保留上一份快照并显示本地化错误。
  HTTP 契约已导出 OpenAPI 并重新生成 TypeScript SDK，路由文档与 OpenAPI 守卫同步更新。
- CI 的 Compose 门禁新增观测编排验证。生产部署脚本由 CI 使用既有精确提交发布流程启用
  Prometheus、Grafana 与 exporters；Grafana 初始密码只在服务器本地以 `0600` 文件生成，
  不进入仓库或发布包。Prometheus 默认保留 7 天且最多 2GB，以适应有限服务器磁盘。
- 平台版本由 `0.1.0-alpha.78` 递增为 `0.1.0-alpha.79`。没有新增数据表、EF migration 或
  model snapshot。本阶段未推送、未部署。
- 验证：Release solution build 0 warning/0 error；完整后端 TUnit 1103 项中 1101 通过、0 失败，
  2 项仅因未启用真实 Kubernetes 集群与未配置 Libvirt 磁盘按设计跳过；前端完整测试、typecheck
  与 production build 通过；C# analyzer、EF model drift、OpenAPI/SDK 双次生成幂等、主/single/
  observability Compose、部署脚本语法及 `git diff --check` 均通过。

## 2026-08-23 alpha.78 可观测性审计修复

- 修复 CI 与生产自动部署共同使用的 `deploy/docker-compose.yml` 中 Worker `environment` 缩进错误；主 Compose、single Compose 及 observability overlay 均已通过解析。生产部署仍由 CI 唯一负责，本节没有建立第二套手工部署流程。
- 删除从未被调用、会永久显示为零的自定义 Wolverine 消息/Handler 指标，改为订阅 Wolverine 官方 `Wolverine:{ApplicationName}` Meter 的原生吞吐、成功、失败、死信和执行耗时指标；补订阅 Npgsql Meter。API 的 Flag/Fix/Runtime 操作埋点只匹配真实 POST 变更端点，不再把读取接口、题库 Flag 管理或受保护 Flag 查看误计为玩法吞吐。
- Runner 心跳、候选池读取、容量 Claim 与释放全部纳入 Redis 延迟/失败指标。心跳 Lua 原子返回剩余 CPU、内存和 PID，Runner 容量快照改为按同一资源池的所有在线 Runner 汇总，不再被最后一条心跳覆盖；Grafana 和告警使用 available/total 即时计算容量比例，并用 Prometheus target `up` 独立检测 Runner 不可达。
- 运维快照不再每 15 秒把全部脏比赛与等待 Runtime 时间戳载入内存，改为 PostgreSQL 端聚合 count/min。Grafana 总览补齐 Wolverine、Npgsql、Runner 容量与队列面板；告警不再引用不存在的 ratio 指标。标签继续只使用 endpoint、outcome、queue、pool、mode、resource 等有界值，没有加入比赛、队伍、Runtime 或 Runner ID。
- 版本从 `0.1.0-alpha.77` 递增至 `0.1.0-alpha.78`。没有新增业务表、字段、EF migration、OpenAPI 或 TypeScript SDK 变化；未推送、未部署、未操作生产数据。
- 验证：Release solution build 0 warning/0 error；完整后端 TUnit 1096 项中 1094 通过、0 失败，2 项仅因未启用真实 Kubernetes 集群与未配置 Libvirt 磁盘按设计跳过，真实 PostgreSQL/Redis/Wolverine/Docker 集成均已执行；新增 API 操作分类测试 11/11、Runner 多实例容量聚合测试 1/1；C# analyzer、EF model drift、主/single/observability Compose、Grafana JSON、Prometheus 配置与 10 条告警、`git diff --check` 均通过。

## 2026-08-23 alpha.77 可观测性、Worker 隔离队列与高并发调度优化

- 建立 OpenTelemetry/Prometheus/Grafana 可观测性基线。API、Worker、Runner 与统一 Host 暴露 Prometheus 指标并保留现有 Redis 平台日志；指标覆盖 API 请求与限流、Flag/Fix/Runtime 操作、SignalR、Wolverine 队列与 Handler、PostgreSQL/Redis、Runner 容量与 Claim、排行榜脏标记/投影/发布。Prometheus 标签仅使用 endpoint、queue、outcome、pool、mode 等有界维度，具体比赛、队伍与 Runtime 标识只进入日志或 Trace。新增可选 observability Compose、Prometheus 抓取与首批告警、Grafana provisioning/dashboard 及部署说明。
- 新增 `tests/load/noctf-baseline.js` 的 k6 可重复压测基线，包含公开读取和受控认证操作、P95/P99/错误率阈值、远程目标硬阻断及凭据仅从环境变量注入。已使用官方 `grafana/k6:0.57.0` 容器执行 `inspect` 并修复 k6 不提供浏览器 `URL` 构造器导致的本地目标误判；没有向生产或现有测试服务器发送压测流量。完整优化前后实压仍需要一套隔离、可丢弃且带专用比赛/账号的环境，未将该缺口误报为通过。
- Worker 仍使用同一二进制和 Durable Inbox/Outbox，但 Wolverine 工作负载按 `noctf-control`、`noctf-gameplay`、`noctf-projection`、`noctf-background` 四条持久队列隔离；支持按配置选择监听队列并设置独立并发上限，维护 Agent 只派发重活。生命周期/轮次/恢复、Flag/Fix/Runtime/作弊、排行榜投影、邮件/通知/导出/文件清理分别路由到对应队列，并记录独立积压、最旧消息、吞吐、重试、死信与执行耗时指标。
- 排行榜发布改为 PostgreSQL advisory lock + RepeatableRead 快照 + Redis fencing token。数据库事务提交后领取单调 fence 并立即释放数据库锁/连接，Redis Lua 仅允许新 fence 覆盖缓存，随后才发布 SignalR；旧 Worker 晚到无法覆盖新快照，Redis 重启可由 PostgreSQL 重新投影，发布失败会重新标脏。测试覆盖乱序发布、Redis 状态丢失、失败重试和 Worker 重投。
- Runner 分配改为 `runner-pool:{pool}:candidates` Redis Sorted Set 候选索引，分数取 CPU/内存/PID 的主导资源使用率并带微小随机扰动；热路径只探测压力最低的 8 个候选，Lua 原子检查心跳、容量和现有 `runner-claim:{runtimeInstanceId}` 后扣减容量并更新索引。前 8 个均不合适时只进行一次全池索引重建并原子选择可用候选，避免漏掉第 9 个可用 Runner；释放 Claim 会恢复容量和候选分，旧 Claim 仍可兼容释放。指标不包含 Runner/Runtime 高基数 ID。
- 提交边界：`57fa59e6` 可观测性基线、`ae9b40b0` 压测基线、`54da3515` Worker 队列隔离、`4e2070dd` 排行榜 fencing、`da042adb` Runner 候选索引、`f0c06042` k6 运行时兼容修复、`170ed10a` 版本递增。版本从 `0.1.0-alpha.76` 递增至 `0.1.0-alpha.77`；没有新增业务表、EF migration 或 snapshot。
- 验证：Release solution build 0 warning/0 error；完整后端 TUnit 1084 项中 1082 通过、0 失败，2 项仅因未启用真实 Kubernetes 集群与未配置 Libvirt 磁盘按设计跳过，真实 PostgreSQL/Redis/Wolverine/Docker 集成均已执行；Runner 真实 Redis 定向测试 11/11；前端 `bun test` 272/272、typecheck 与 production build 通过（仅既有大 chunk、plugin timing 与第三方 exports deprecation warning）；EF model drift、OpenAPI 导出/TypeScript SDK 生成幂等、k6 `inspect` 与 `git diff --check` 均通过。本节提交后统一推送 `main`，未执行生产部署或生产数据操作。

## 2026-08-22 alpha.76 参赛页面边界、审计恢复与邮箱激活管理

- 参赛端移除旧侧边栏并按页面职责拆分布局：仅题目工作区显示按方向分组的题目列表；咨询、我的队伍、我的提交保留主内容与右侧赛事区域；概览不再显示侧栏；记分板成为独立页面并提供“返回比赛”入口。普通参赛者仍看不到中控大屏、队伍管理和动态入口，这些入口保留在竞赛管理侧。
- 管理端队伍列表的队名可打开详情抽屉，展示赛道、报名/封禁状态、注册时间、成员和队长身份。平台用户抽屉新增与账号启用状态相互独立的“邮箱激活状态”：管理员可以标记邮箱已验证或撤销验证；变更递增 TokenVersion、写入强类型账号生命周期审计，重复设置幂等，匿名化账号保持不可变。
- 修复历史审计事件中旧数值枚举导致平台审计接口 500 的问题：无效枚举负载按未知值安全呈现，不再中断整页查询。中文界面的未知英文后端诊断统一回退为本地化状态提示，同时保留题目定义错误等专用中文指导；下载、导出和列表错误不会再泄露英文兜底文本。
- 邮箱激活管理使用独立强类型 FastEndpoints `ExecuteAsync`/`TypedResults` 端点，并通过 Application/Infrastructure 事务写入；接口、OpenAPI、生成 TypeScript SDK 和 `docs/api.md` 同步更新。没有新增业务表、EF migration 或 snapshot，EF model drift 检查无变化。版本从 `0.1.0-alpha.75` 递增至 `0.1.0-alpha.76`。
- 验证：Release solution build 0 warning/0 error；完整非 Integration TUnit 877/877；前端 `bun test` 272/272、`bun run typecheck` 与 production `bun run build` 通过（仅既有大 chunk、plugin timing 与第三方 exports deprecation warning）；OpenAPI 导出与 `bun run api:gen` 幂等，`git diff --check` 通过。本机 Docker daemon 不可用，真实 PostgreSQL/Redis/Wolverine Integration 测试未执行，未将其误报为通过。本节提交后的统一 `main` push 将触发受控 CI 与自动部署。

## 2026-08-22 alpha.75 自动部署长连接与外部工具下载加固

- 首次 alpha.74 自动部署的完整 `build-test` 全部通过，但生产部署在 Runner 镜像下载 Kompose CLI 期间连续五分钟没有终端输出，GitHub Actions 到生产服务器的 SSH 会话被中间网络设备以 `Broken pipe` 断开。切换尚未发生，原 Backend/Worker/Runner 始终保持健康；失败构建进程与未生效 release 已按精确 PID/路径终止和删除，数据库备份继续保留。
- 版本递增至 `0.1.0-alpha.75`。生产发布不再要求服务器直接访问缓慢的 GitHub Release：CI 下载固定版本的 Kompose amd64 二进制并校验 SHA-256，随后和精确提交归档一同上传；服务器部署脚本再次校验后才注入该 release 的 Docker 构建上下文，Dockerfile 最终还会第三次校验。非 CI 的本地构建保留有界、可续传的 `curl` 回退。SSH/SCP 同时增加连接超时、30 秒应用层 keepalive 与容错次数，长时间镜像构建不再因无控制台输出断线。架构测试覆盖预载资产、下载回退与 SSH keepalive 配置。
- 生产磁盘的有界清理已在失败部署前生效，根盘使用率从约 92% 降至 78%、可用约 9.1 GiB；未执行全局 prune，未删除 volume、题目镜像、PostgreSQL/Redis 数据或生产专用配置。alpha.75 推送后应重新观察完整 CI、备份校验、迁移、服务健康与 release 指针，再将实际结果补充到本节。

## 2026-08-22 alpha.74 CI 恢复与受控生产自动部署

- 版本从 `0.1.0-alpha.73` 递增至 `0.1.0-alpha.74`。修复随机附件批次文件名校验依赖当前操作系统路径规则的问题：统一下载名与以 Flag 为名的变体现在显式拒绝 `/`、`\\`，Linux CI 不会再把 Windows 风格的子路径当作合法 Flag；补充两个方向的跨平台回归测试。
- GitHub Actions 在完整 `build-test` 成功后，仅对 `main` 的 push 串行执行生产部署。CI 使用 `git archive` 打包精确提交，上传到独立临时目录并解包至 `/root/noctf-releases/<sha>`，不覆盖或依赖服务器 `/root/NoCTF` 中已有的生产专用未提交配置；Compose 继续复用 `/root/NoCTF/.env` 与 `/root/NoCTF/deploy/docker-compose.prod.yml`。
- 部署前自动验证 Compose、检查磁盘、备份 PostgreSQL 为 custom-format dump 并用 `pg_restore --list` 校验；数据库迁移成功后才切换 Backend/Worker/Runner，等待健康检查并验证 `/health/ready`。失败时恢复先前平台镜像；release 目录保留 current/previous 两版，数据库备份保留最近十份。
- 空间清理保持有界：仅清理停止超过 24 小时且带 `noctf.io/managed=true` 标签的容器、迁移临时容器、dangling 镜像、旧 build cache 和无容器引用的旧 `deploy-*` 平台镜像；至少保留当前镜像和一组回滚镜像，不执行 volume/system 全局 prune，不删除题目镜像、数据库卷或生产配置。低于 6 GiB 时清空未使用 build cache，并在仍不足时安全终止部署。
- 将 Refresh Cookie 的 Secure 策略集中为一个实现。默认仍使用 `__Secure-noctf_refresh`、`Secure=true`；明确隔离的纯 HTTP 测试部署可通过 `AUTHENTICATION_REFRESH_COOKIE_SECURE=false` 使用无前缀 Cookie，登录、刷新、退出、全局退出、改密和重置密码使用同一策略。公网 TLS 部署继续保持默认安全值。
- 本地验证：Release solution build 0 warning/0 error；完整 TUnit 1072 项中 878 通过、0 失败，194 项因本机 Docker/Kubernetes/Libvirt 环境不可用按设计跳过；随机附件定向 19/19、认证端点定向 3/3、部署拓扑 10/10；C# analyzer、两份 Compose config、GitHub Actions YAML 解析、部署脚本 `bash -n` 和 `git diff --check` 通过。首次生产自动部署由本节所属 `main` 推送触发，最终 CI 与生产健康结果以该次 GitHub Actions 运行记录为准。

## 2026-08-22 alpha.73 参赛端工作区、AWDP 攻防语义与赛道邀请码本地修复

- 本阶段在本地 `main` 上继续修复参赛端体验与 AWDP 产品语义，尚未推送或部署。版本从 `0.1.0-alpha.72` 递增至 `0.1.0-alpha.73`。
- 参赛端题目页整理为三栏工作区：左侧按方向分组的题目导航，中间页内展示题面、附件、Runtime、Flag/Fix 操作与 Fix 历史，右侧上部为赛事导航、下部为赛事播报；咨询、我的队伍、我的提交、记分板等参赛页面复用同一工作区布局，旧 Fix 历史独立页面改为重定向到题目工作区。
- 普通参赛者不再显示中控大屏、3D 大屏、队伍与动态入口；管理员/裁判/观察员的监控入口转移到竞赛管理侧。首页在已登录状态隐藏“立即注册”按钮，参赛端删除了过密的说明性文案，题目详情页减少悬浮卡片感和无效留白。
- CTF 与 AWDP Break Flag 首次正确后会请求关停对应攻击 Runtime；AWDP 在攻击成功后继续允许只读式判定后续 Flag 是否正确，便于复现和写 WP，但不再重复计分、创建 GameplayFact、赛事播报或作弊/失败状态，也不再因后续随意提交错误 Flag 覆盖已成功状态。
- AWDP Fix 历史改为题目工作区内弹窗，不再新开页面；Fix 结果兜底统一为“防御异常：服务异常”。AWDP 面板文案压缩为“攻击靶机 / Break 环境”和“一次性防御验证”，避免暗示长期防御环境。
- 赛道新增邀请码能力：赛道可配置是否需要邀请码；启用邀请码的公开赛道在创建队伍/报名时必须输入邀请码；若赛道被改为内部或取消公开，后端会清除邀请码要求与哈希。前端表单保留当前输入，错误提示走强类型失败码与 i18n。
- 后端新增 AWDP Break Flag 只读判定能力，运行时持久化与提交处理补齐自动停止逻辑；没有新增业务表、EF migration 或 snapshot。接口变化通过 OpenAPI 导出并重新生成 TypeScript SDK，路由漂移测试已同步更新至 207。
- 验证：前端 `bun test` 268/268 通过，`bun run typecheck` 通过，`bun run build` 通过（仅既有大 chunk、plugin timing 与第三方 exports deprecation warning）；后端 `dotnet test backend/tests/NoCTF.Tests/NoCTF.Tests.csproj -c Release --no-restore` 1068 项中 874 通过、0 失败、194 按环境跳过；`git diff --check` 通过，仅提示 Windows 下部分文件未来会 CRLF 规范化。未执行推送、部署或生产数据操作。

## 2026-08-22 alpha.72 遗漏功能归并与三栏题目工作区

- 将此前滞留在 `codex/fix-scoreboard-audit-20260820` 工作树中的全部有效产品改动纳入 Git 历史，并在最新 `origin/main`（alpha.71）之上无冲突 rebase。改动包含：参赛者三栏题目工作区（左侧按方向分组题目、中间页内题目详情、右上赛事导航、右下赛事播报）、旧题目详情与通知/提交/Fix 历史链接的页内定位、参赛资格校验、AWDP 首次攻防成功状态锁定、排行榜矩阵与雷达图修正、首页登录后隐藏注册入口、参赛者说明文案压缩、平台审计日志降噪与可读操作说明，以及管理员设置用户账号激活状态。
- 平台账号状态接口使用独立强类型 FastEndpoints `ExecuteAsync`/`TypedResults` 端点并由 Application/Infrastructure 执行业务与持久化；OpenAPI 由工具导出，TypeScript SDK 由 `bun run api:gen` 生成，前端未手写 URL、DTO、枚举或失败码。没有新增业务表、EF migration 或 snapshot；EF model drift 检查无变化。
- 版本从 `0.1.0-alpha.71` 递增至 `0.1.0-alpha.72`。功能代码已快进推送到远程 `main@050aab33`，生产仓库 `/root/NoCTF` 通过先决提交为 `053fd74c` 的增量 Git bundle 快进到同一提交；bundle 为 86,969 字节，SHA-256 为 `272db8b9cf5f97cd9753b2a097ad2246e4a07ed1b60af107f1d3f780b5501b38`，生产专用且未跟踪的 Compose overlay 保持原样。
- 部署前数据库备份为 `/root/backups/noctf-pre-alpha72-050aab33-20260822T061551Z.dump`，SHA-256 为 `8f1c16ada82e54585d4572a51aa02f9a46d853aaca059771da39354144a07842`，并已使用当前 PostgreSQL 容器中的 `pg_restore --list` 验证可读；本阶段没有 EF migration 或模型变化，因此没有运行迁移。
- 新 Backend 镜像为 `8c377d8846a`、Worker 镜像为 `77710937036`，均保留 `alpha72-050aab33` 标签；上一版 Backend `alpha71-053fd74c` 与 alpha.70 平台回滚镜像继续保留。仅 Backend 与 Worker 以 `--no-deps --no-build --force-recreate` 切换，Runner、PostgreSQL、Redis 未重启；两项新服务均为 `healthy`、RestartCount 为 0，容器内程序集均确认包含 `0.1.0-alpha.72`，公开首页、`/health`、`/health/ready` 均返回 HTTP 200，生产 OpenAPI 已包含管理员账号状态接口。
- 部署前后 `noctf.io/managed=true` Runtime 容器数量均为 0，切换后的 Backend/Worker 日志没有 Error、Critical、Fatal 或 Unhandled 匹配。空间清理只删除不再承担当前或相邻回滚用途的四个精确 alpha.59 平台镜像；没有执行全局 prune，也没有删除题目镜像、卷、备份或生产配置。根盘部署后剩余约 4.3 GB。
- 验证：Release solution build 0 warning/0 error；完整非 Integration TUnit 870/870；前端 `bun test` 264/264、`bun run typecheck` 与 production `bun run build` 通过；C# analyzers、EF model drift 和 `git diff --check` 通过；OpenAPI 与 SDK 连续第二轮生成 SHA-256 完全一致。新增真实 PostgreSQL 定向测试已纳入测试集，但本机 Docker daemon 不可用，测试框架将对应 Integration 用例按设计跳过，未将其误报为通过。

## 2026-08-22 alpha.71 AWDP 防御结果兜底文案与生产部署

- `ad444bb2` 修正 AWDP 一次性 Fix 验证结果的前端兜底语义：除明确的 `Correct` 与 `AwdpExploitSucceeded` 外，未知或缺失失败码统一显示“防御异常：服务异常”，不再显示并非后端预设结果的“防御未通过”。选手题目面板与 Fix 历史页使用同一规则；删除了已失效的英文翻译键，并补充前端回归测试。`053fd74c` 将版本从 `0.1.0-alpha.70` 递增到 `0.1.0-alpha.71`。
- 本阶段没有新增业务表、字段、EF migration、HTTP/OpenAPI 或 TypeScript SDK 变化。精确比较 `alpha.70..alpha.71` 后，业务改动仅涉及 Backend 镜像内嵌的 SPA；Worker、Runner 与数据库代码没有变化，因此生产只强制重建并切换 Backend，PostgreSQL、Redis、Worker、Runner 均未重启。
- 代码已快进推送到远程 `main`，生产仓库 `/root/NoCTF` 同步到 `053fd74c`。部署前数据库备份为 `/root/backups/noctf-pre-alpha71-053fd74c-20260822T042801Z.dump`，SHA-256 为 `4f8692a582048be6c386b6498d494f6413cafc8d7e68840835c0e34968ae44a3`，并已使用 `pg_restore --list` 验证可读；本阶段没有迁移需要执行。
- 新 Backend 镜像 ID 为 `1cca3692e155`，保留回滚标签 `deploy-backend:alpha71-053fd74c`；上一版 alpha.70 平台镜像与标签原样保留。切换后 `deploy-backend-1` 为 `healthy`，容器内程序集确认包含 `0.1.0-alpha.71`；`https://101.43.46.244/`、`/health`、`/health/ready` 均返回 HTTP 200，部署后近 10 分钟日志未出现 Error、Critical、Fatal 或未处理异常。
- 部署前后 `noctf.io/managed=true` Runtime 容器数量均为 0。清理只移除了本次 Runner 构建诊断产生的精确 Alpine 临时容器和临时 Dockerfile/Kompose 下载文件，没有执行全局 prune，也没有删除题目镜像、卷、数据库备份或生产专用 Compose 覆盖文件。服务器仓库状态仍只包含原有未跟踪的 `deploy/docker-compose.prod.yml` 与 `deploy/docker-compose.prod.yml.pre-alpha55`。
- 验证：原工作树定向前端测试 14/14 通过，`bun run typecheck` 与 `git diff --check` 通过；干净发布工作树定向 AWDP 测试 7/7 通过；生产 Backend Docker 构建（含 Nuxt production generate）成功，只保留既有大 chunk、plugin timing 与第三方未使用导入 Warning。生产健康、版本、外部 HTTPS、日志、回滚标签和 Runtime 资源边界均已实际核验。

## 2026-08-21 alpha.70 仓库清理与生产部署

- `fe422201` 将本地部署传输产物 `.tmp-noctf-*.bundle` 纳入根 `.gitignore`，防止临时 Git bundle 污染状态；没有删除业务代码、测试、部署清单或用户文件。`af907ecb` 将版本从 `0.1.0-alpha.69` 递增到 `0.1.0-alpha.70`。本阶段没有新增业务表、字段、EF migration、HTTP/OpenAPI 或 TypeScript SDK 变化。
- 代码已快进推送至远程 `main`，生产部署的代码基线为 `af907ecbed83d6b643044656db8b0156e159e2f5`。服务器 `/root/NoCTF` 通过已验证的增量 Git bundle 从 `870b5201` 快进到该提交；生产专用且未跟踪的 `deploy/docker-compose.prod.yml` 与 `deploy/docker-compose.prod.yml.pre-alpha55` 均原样保留。
- 部署前数据库备份为 `/root/backups/noctf-pre-alpha70-af907ecb-20260821T082100Z.dump`，SHA-256 为 `b2abaa00dcf78f46140ffd49f3558a842482c90ea7e5cfe8355646eb14a7763c`，并已用当前 PostgreSQL 容器中的 `pg_restore --list` 验证可读。迁移容器返回数据库已是最新状态，没有应用 migration。
- 新镜像及回滚标签分别为 backend `4af2fbe1f87c`、worker `b812ecb06b1e`、runner `81b44af71fe2`、migration `affd76a0f502`，统一保留 `alpha70-af907ecb` 标签；上一版 `alpha59-70c2b54d` 四个镜像标签继续保留。Runner 构建时 GitHub Release 下载节点不稳定，改为从当前线上 Runner 提取相同 Kompose 1.38.0 二进制，并以仓库锁定的 SHA-256 `65a6a720605bead3964e8b22d423a0763de451a236fe03de902e366cf3d9c147` 验证后作为临时构建输入；临时 Dockerfile、二进制和传输 bundle 均已删除，未提交到仓库。
- 仅 backend、worker、runner 被强制重建；PostgreSQL 与 Redis 未重启。部署后三个服务均为 `healthy`、RestartCount 为 0，容器内程序集均确认包含 `0.1.0-alpha.70`；`https://101.43.46.244/`、`/health`、`/health/ready` 均返回 HTTP 200。自部署时间 `2026-08-21T08:49:49Z` 起的三服务日志未出现 Error、Critical、Fatal 或未处理异常，仅保留既有 DataProtection、动态 Wolverine codegen 与端口覆盖 Warning。
- 部署前后 `noctf.io/managed=true` Runtime 容器数量均为 0。空间清理只删除 alpha.58 旧平台镜像及本次临时 Runner 构建产生的三个精确 dangling 中间镜像，没有执行全局 prune，也没有删除题目镜像、当前/回滚平台镜像、卷或备份；根盘最终剩余约 7.2 GB（82% 使用）。
- 验证：本地 `dotnet build backend/NoCTF.slnx -c Release --no-restore` 通过，0 warning/0 error；服务器四镜像构建成功，前端只出现既有大 chunk、plugin timing 与第三方未使用导入 Warning；迁移、容器健康、版本、外部 HTTPS、启动日志、回滚标签和磁盘边界均已实际核验。

## 2026-08-21 alpha.69 冻结榜不可变性与 KoH 首次投影时序修复

- `55f28f1d` 修复冻结排行榜仍接受 `endingRound`、并可能从当前数据库与当前比赛配置重建历史窗口的问题。`Frozen` 状态的排行榜、Schema 与 Slot Detail 现在始终只返回已经持久化的冻结快照并忽略轮次窗口参数；只有实时榜可以浏览历史轮次。前端在冻结榜隐藏轮次导航，不能再用后续重判、题目配置或队伍状态改变冻结结果。
- `46d4f97b` 修复 KoH 刚启动时偶发长时间没有首个稳定排行榜投影的问题。根因是 Worker 的 Wolverine singular maintenance agent 在节点启动后可能较晚完成健康检查与领导权分配，虽然 KoH GameplayFact 已按约每 2 秒落库且 `LeaderboardDirty=true`，15 秒维护循环却尚未接管，客户端在 45 秒内持续读到初始零分缓存。Worker 现在把首次健康检查与 agent assignment 检查都明确设为启动后 1 秒；排行榜仍由既有 15 秒维护任务合并刷新，没有把每次 KoH 观测改成昂贵的全量投影。
- `c20eb264` 将版本从 `0.1.0-alpha.68` 递增到 `0.1.0-alpha.69`。本阶段没有新增业务表、字段、EF migration、HTTP/OpenAPI 或 TypeScript SDK 变化，也没有改变 KoH 轮询周期和记分规则。
- 验证：Release solution build 0 warning/0 error；最终完整非 Integration TUnit 859/859；完整 Integration 192 项中 190 通过、0 失败，2 项仅因未配置真实 Kubernetes/Libvirt 环境按设计跳过；前端 `bun test` 247/247（1819 assertions）、typecheck 与 production build 通过；CTF、AWD、AWDP Full E2E 分别 1/1 通过。KoH Full E2E 在审计中先复现过首次排行榜投影超时；修复后连续独立执行 3 次均 1/1 通过，每次都完成 API、Redis、PostgreSQL 韧性重启检查并按精确 Compose 项目身份清理容器、网络和测试镜像。完整测试套件曾有一次 Wolverine 双节点 maintenance failover 30 秒时序超时，随后该用例隔离复跑 1/1、完整类 10/10 通过，没有将偶发超时伪报为未发生。
- C# analyzers、EF `has-pending-model-changes`、前端 production build 与 `git diff --check` 均通过。仓库没有 lint script，未将 lint 误报为已执行；Microsoft Edge 未连接到本地 alpha.69 环境，因此没有把旧部署页面冒充为本轮浏览器验收。
- 本阶段位于独立工作树 `E:\SourceCode\NoCTF-leaderboard-matrix-20260819`、分支 `codex/fix-scoreboard-audit-20260820`。按用户指令，发现问题并完成修复后只创建本地提交；未推送、未部署、未操作生产数据，也未切换或同步本地 `main`，等待下一步指令。

## 2026-08-21 alpha.68 AWDP 权威攻防分拆与当前轮操作统计修复

- `d6534a8d` 修复 AWDP 长赛累计攻防分被前端按当前最多 50 轮窗口重新计算的问题。规范化排行榜现在直接返回后端完整历史投影的 `attackScore`、`defenseScore`，并按 `CompetitionChallengeId` 返回权威 `challengeScores`；主榜窗口仍保持有界，前端不再从可见 Slot 推测整场或单题累计分。人工调分继续只进入总分，不被错误归类为攻击分或防御分。
- 同一提交修复 AWDP 中控“当前轮”操作统计跨轮泄漏。顶栏及当前队伍的攻击/防御成功数与总提交数改为读取当前轮矩阵 Slot 的权威 breakdown；题目最新攻防状态只接受当前轮 `[startAt, endAt)` 内事件。左侧动态、中央播报及历史回放仍保留完整事件窗口，不因当前轮过滤丢失历史展示。
- HTTP 响应、两份 OpenAPI 与 TypeScript SDK 均通过工具更新；没有手写 URL、DTO、枚举或生成文件。两份 OpenAPI SHA-256 均为 `CEE1FDDBCF585AC562C7A743D59B8F9823189C4EC26E0B6B473997BF2BF7E703`，`index.ts` 为 `3450501619DC098C61604A6709DCFFB0B5FB3BE88B147FA1637868816A86D822`，`types.gen.ts` 为 `5DCC8F2D92B8C4A377C9A88625E2A777CED1FA73E977A1BB01A9867C10112FCF`；连续第二轮导出与生成没有差异。
- `6e922296` 将版本从 `0.1.0-alpha.67` 递增到 `0.1.0-alpha.68`。本阶段没有新增业务表、字段、EF migration 或 snapshot；EF `has-pending-model-changes` 确认无模型漂移。
- 验证：Release solution build 0 warning/0 error；后端完整 TUnit 1048 项中 1046 通过、0 失败，2 项仅因未配置真实 Kubernetes/Libvirt 环境按设计跳过；权威分拆投影 14/14、OpenAPI 5/5；AWDP Full E2E 1/1（5m01.638s），随后 API、Redis、PostgreSQL 韧性检查通过，测试 Compose、镜像、容器和网络已按项目身份清理；前端 `bun test` 246/246（1813 assertions）、typecheck、production build、`bun audit` 0 漏洞；C# analyzers、OpenAPI/SDK 双次幂等、EF model drift 和 `git diff --check` 均通过。仓库没有 lint script，未将 lint 误报为已执行。
- 本阶段位于独立工作树 `E:\SourceCode\NoCTF-leaderboard-matrix-20260819`、分支 `codex/fix-scoreboard-audit-20260820`，基线为 `3d69fbeeba8d9f2ded01b7c70a5dd36c3441bb7d`。按用户指令，发现问题并完成修复后只创建本地提交；未推送、未部署、未操作生产数据，也未切换或同步本地 `main`，等待下一步指令。

## 2026-08-21 alpha.67 记分板总分守恒与投影提交原子性修复

- `150d5426` 修复 AWDP 长赛和历史轮次窗口的总分表达缺口。主矩阵仍只返回最多 50 个连续轮次，但每支队伍新增后端权威 `scoreOutsideWindow`；现在稳定满足 `totalScore = 当前可见 Slot 净分 + scoreOutsideWindow + 全局人工调分`。历史窗口中的窗口外分同时包含所选窗口之前和之后已经结算的轮次，前端不得自行累计或推测整场分数；CTF、AWD、KoH 的该字段固定为 0。
- 同一提交修复排行榜投影事务与 FusionCache 更新顺序。投影现在在 PostgreSQL `RepeatableRead` 事务成功提交后才替换缓存和发布通知；提交失败会保留最后一份成功缓存。跨进程刷新使用 PostgreSQL session advisory lock 覆盖事务提交、缓存替换和通知发布，避免较旧投影在并发刷新中反向覆盖新结果。
- 前端 `useScoreboardMatrix` 将目录、Schema 和 Snapshot 的 Hub 通知全部纳入同一个 trailing refresh 合并队列；刷新进行中收到的新通知不会被丢弃，重连也会请求完整三件套。AWDP Full E2E 同步使用 canonical Int64 十进制字符串协议，并分别验证当前窗口算术与已结算历史窗口不可变性。
- `f8e34dea` 将版本从 `0.1.0-alpha.66` 递增到 `0.1.0-alpha.67`。本阶段没有新增业务表、字段、EF migration 或 snapshot；EF `has-pending-model-changes` 确认无模型漂移。
- 两份 OpenAPI 连续第二轮导出后 SHA-256 均为 `28DF1207F59F96C4BF587982490207A21145D5D2E94891A6DBE87261A43287A0`；生成的 `types.gen.ts` SHA-256 为 `B4684E87C0528149783861788F4E2325D43890BACF4BD1939C51797B9224A662`，第二轮生成无差异，未手工修改生成 SDK。
- 验证：Release solution build 0 warning/0 error；后端完整 TUnit 1047 项中 1045 通过、0 失败，2 项仅因未配置真实 Kubernetes/Libvirt 环境按设计跳过；真实 PostgreSQL 提交失败缓存保留、AWDP 长历史窗口和历史窗口守恒测试通过；CTF、AWD、AWDP、KoH 四种 Full E2E 均 1/1 通过，并完成 PostgreSQL/Redis/API 韧性与测试资源清理；前端 `bun test` 244/244（1809 assertions）、typecheck、production build、`bun audit`；C# analyzers、EF model drift、OpenAPI/SDK 双次幂等和 `git diff --check` 全部通过。前端构建只保留既有大 chunk、plugin timing 与第三方 Node exports deprecation 警告。
- 本阶段位于独立工作树 `E:\SourceCode\NoCTF-leaderboard-matrix-20260819`。按用户指令，发现问题并完成修复后只创建本地提交；未推送、未部署、未操作生产数据，也未切换或同步本地 `main`，等待下一步指令。

## 2026-08-21 alpha.66 记分板投影标识与历史窗口一致性修复

- `436b0ad3` 修复记分板协议标识在 JavaScript 中丢失精度的问题。内部仍以 `long` 保存 Snapshot version、Schema revision 与题目目录 revision；HTTP、SignalR 和 Redis 外部边界统一输出不变区域十进制字符串，前端只做精确字符串比较，不再将 64 位值转换为 IEEE-754 `number`。OpenAPI 与 TypeScript SDK 均由工具重新生成，没有手改生成文件。
- 同一提交修复 AWDP 历史轮次窗口跨多次查询可能读取到混合数据库状态的问题。历史窗口现在在 PostgreSQL `RepeatableRead` 快照中完成；生命周期事件严格裁剪到 `projectedAt`，因此截止时间之后的 Finished 等状态不会污染此前窗口，也不会把仍在进行的历史轮次提前标记为已结算。
- 前端历史轮次选择现在区分真实请求失败、投影处理中、版本一致性重试和已被新请求取代四种结果。只有真实失败才回退到上一窗口；202 或目录/Schema/Snapshot 暂时不一致时保留用户选择并继续请求该窗口，不再静默跳回旧轮次。
- `a50588dd` 将 Nuxt/PostCSS 的传递依赖 `nanoid` 从存在高危公告的 `3.3.17` 兼容锁定到修复版本 `3.3.18`；`bun audit` 返回 0 漏洞，没有升级 Nuxt/PostCSS 或改变产品运行时依赖边界。
- `fe16b2a0` 将版本从 `0.1.0-alpha.65` 递增到 `0.1.0-alpha.66`。本阶段没有新增业务表、字段、EF migration 或 snapshot；EF `has-pending-model-changes` 确认无模型漂移。
- 两份 OpenAPI 连续第二轮导出后 SHA-256 均保持 `3C5EA333AB2B4B1C1ECCB5C484D72EF39E3B5F524492F7691BCCE0639182CA80`；生成的 `types.gen.ts` SHA-256 保持 `F87A095B28D699E67676AD57669E00E314A1D5BCCD889B15630AA6EDED0DCCF2`，证明导出与 SDK 生成幂等。
- 验证：Release solution build 0 warning/0 error；后端完整 TUnit 1046 项中 1044 通过、0 失败，2 项仅因未配置真实 Kubernetes/Libvirt 环境按设计跳过；排行榜 API 17/17、OpenAPI 5/5、真实 PostgreSQL 投影 4/4；前端 `bun test` 244/244（1806 assertions）、typecheck、production build、`bun audit`；C# analyzers、EF model drift 与 `git diff --check` 全部通过。前端构建只保留既有大 chunk、plugin timing 与第三方 Node exports deprecation 警告。
- 本阶段位于独立工作树 `E:\SourceCode\NoCTF-leaderboard-matrix-20260819`。没有重新执行四模式 Full E2E，未将旧结果冒充为本轮验证；未推送、未部署、未操作生产数据，也没有切换或同步本地 `main`，等待用户下一步指令。

## 2026-08-21 alpha.65 AWDP 长赛轮次窗口与排行榜一致性修复

- `f3a77951` 将 AWDP 主排行榜矩阵约束为最多 50 个连续轮次，并提供按 `endingRound` 向前、向后及返回最新轮次的历史窗口导航。全赛历史仍用于权威累计总分；历史窗口只读取自身 `[startAt, endAt)` 内的 Break/Fix 事实，后续轮次提交不会污染旧窗口，人工调分始终从全历史只计一次，不会因窗口查询重复累加。
- Schema 明确返回 `roundWindowStart`、`roundWindowEnd` 与 `latestRound`；Leaderboard、Schema、Slot Detail 三个强类型接口使用同一窗口边界，Slot Detail 签名游标也绑定窗口范围。冻结榜忽略实时历史窗口参数，继续只读取冻结时的权威快照。
- 主快照继续只保留每个 Slot 最多 5 条摘要，但内部明细 allocation 来自完整已构建 Slot，因此分页详情不会再丢失被压缩摘要之外的旧记录。历史窗口导航失败会保留上一份成功数据；实时刷新会同步 Schema 与 Snapshot，客户端不再把 wall-clock 版本号误当作跨重启绝对单调时钟，缓存端则在同一缓存历史中保证新版本高于旧版本。
- Challenge 模板标题、方向、分类、顺序、发布状态与 revision 都进入目录 revision；标题或方向变更会将所有引用比赛标记为 `LeaderboardDirty`，避免排行榜长期显示旧题目元数据。
- `8a68159c` 将版本从 `0.1.0-alpha.64` 递增到 `0.1.0-alpha.65`。本阶段没有新增业务表、字段、EF migration 或 snapshot；EF `has-pending-model-changes` 确认无模型漂移。
- 两份 OpenAPI 由工具连续导出两轮且 SHA-256 均为 `84569D15E9DB5ECAED2259004674C9AFD25B78BE567AB3D4B6F9DE4F64219060`；TypeScript SDK 连续生成两轮，`types.gen.ts` SHA-256 均为 `EEFA2DB75F47DEAD473305A390132248144F39EB194A67A18BA794BB50BC04C0`，生成结果幂等且未手工修改 SDK。
- 验证：Release solution build 0 warning/0 error；完整非 Integration TUnit 856/856；完整 Integration 189 项中 187 通过、0 失败，2 项仅因未配置真实 Kubernetes/Libvirt 环境按设计跳过；排行榜 Endpoint 16/16；真实 PostgreSQL 长历史窗口、未来事实隔离、人工调分去重、封禁/解封重投影与题目元数据失效测试均通过；前端 `bun test` 243/243、typecheck、production build；C# analyzers 与 `git diff --check` 通过。仓库没有 lint script，未将 lint 误报为已执行。
- 本阶段位于独立工作树 `E:\SourceCode\NoCTF-leaderboard-matrix-20260819`；未推送、未部署、未操作生产数据。本轮没有重新执行四模式 Full E2E，未将既有结果冒充为本轮验证。

## 2026-08-21 alpha.64 排行榜权威状态与暂停轮次修复

- `3cbcdb8f` 修复公开 CTF/AWDP 中控对封禁、失格队伍伪造名次的问题。前端现在只展示后端投影返回的权威 `rank`；无名次队伍显示 `—` 和强类型排名状态，不再用数组位置生成排名或趋势。
- 同一提交修复工作人员排行榜错误隐藏内部/隐藏赛道的问题。平台管理员以及比赛 Owner、Manager、Judge、Observer 可以切换后端已经授权返回的全部赛道；普通参赛者仍只看到本人赛道与公开非内部赛道，没有扩大 API 数据范围。
- AWDP 在暂停期间不再继续推进当前轮倒计时。投影把暂停时冻结的剩余秒数映射为相对于快照生成时刻的未来结束边界；前端以 `GeneratedAt` 作为冻结参考点，避免结束时间早于开始时间或页面计时继续归零。
- 文档已统一为 AWDP schema v4，并明确 15 秒维护任务以 500 场为单批、按 Competition UUID keyset 遍历全部 Running AWDP 比赛，不再表述为只检查前 500 场。
- `53dda317` 将版本从 `0.1.0-alpha.63` 递增到 `0.1.0-alpha.64`。本阶段没有新增业务表、字段、EF migration、HTTP/OpenAPI 或 TypeScript SDK 变化。
- 验证：Release solution build 0 warning/0 error；完整非 Integration TUnit 853/853；完整 Integration 188 项中 185 通过、2 项因未配置真实 Kubernetes/Libvirt 环境按设计跳过，唯一 Wolverine 双节点维护代理故障转移用例发生 30 秒时序超时，随后隔离复跑 1/1 通过；标准化投影定向 11/11；前端 `bun test` 242/242（1790 assertions）、typecheck、production build；变更 C# whitespace 与 `git diff --check` 均通过。仓库没有 lint script，未将 lint 误报为已执行。
- 本阶段位于独立工作树 `E:\SourceCode\NoCTF-leaderboard-matrix-20260819`，未覆盖主工作区修改；未推送、未部署、未操作生产数据。

## 2026-08-21 alpha.63 排行榜批次遍历与隐藏赛道修复

- `af42683a` 修复 AWDP 轮次维护只取固定前 500 场比赛的问题。Worker 现在按 Competition UUID 稳定排序并使用 keyset 游标逐批读取，每批最多 500 场但会持续遍历至结果集末尾；无效配置只会跳过当前比赛，不再使第 501 场及后续比赛永久无法触发结算重投影。真实 PostgreSQL 回归一次创建 501 场 Running AWDP 比赛，并确认 501 场全部派发 `ProjectLeaderboard`。
- 同一提交补齐参赛者隐藏/内部赛道的响应语义。后端只在当前调用者自己的赛道上返回强类型 `isViewerTrack`，仍裁剪其他隐藏赛道及队伍；前端将本人赛道纳入可选项并优先作为默认赛道，公开赛道行为不变。OpenAPI 由工具导出，TypeScript SDK 由 `bun run api:gen` 重新生成，未手写 URL、DTO、枚举或生成文件。
- `6190abb3` 将版本从 `0.1.0-alpha.62` 递增到 `0.1.0-alpha.63`。没有新增业务表、字段、EF migration 或 snapshot；EF `migrations has-pending-model-changes` 确认无模型漂移。
- OpenAPI 两份制品 SHA-256 均为 `E043CEA4BF617E61B7F69081B0AA4EF024B1805564D3B793DDFCFC16420D88C7`，`types.gen.ts` 为 `EA2FF56C811564CAC8260F407576188CA819BBEED6932EAEC5F72378FF0B4E13`；连续第二轮导出与生成哈希不变。
- 验证：Release solution build 0 warning/0 error；完整非 Integration TUnit 852/852；完整 Integration 188 项中 186 通过、0 失败，2 项仅因未配置真实 Kubernetes/Libvirt 环境按设计跳过；真实 PostgreSQL 排行榜投影类 3/3；排行榜 Endpoint 16/16、标准化投影 10/10、OpenAPI 5/5；前端 `bun test` 241/241（1783 assertions）、typecheck、production build；变更 C# 文件格式、EF drift 与 `git diff --check` 均通过。全仓 whitespace 检查仍报告未触碰文件的既有格式偏差；仓库没有 lint script，均未误报为通过。
- 本阶段位于独立工作树 `E:\SourceCode\NoCTF-leaderboard-matrix-20260819`，未覆盖主工作区修改；未推送、未部署、未操作生产数据。

## 2026-08-20 alpha.62 统一排行榜最终审计修复

- `fe47240e` 修复排行榜缓存仍暗中保留无界明细历史的问题。`ScoreboardProjection` 现在只保留主快照已经压缩到每个 Slot 最多 5 条的内部 Entry allocation，并移除全部人工调分历史 allocation；完整 Slot 与人工调分历史只在签名游标详情请求时，从 PostgreSQL 按页读取。内部 Actor 目录也只保留主快照实际引用的 Actor，不再随整场提交者数量无界增长。
- PostgreSQL 明细读取继续受 `DataAsOf`、Schema revision、Snapshot version 与调用者签名游标约束。每页在同一个 `RepeatableRead` 快照中查询事实及当前脱敏后的用户显示名；API 为当前页重新建立稠密 Actor 索引，不把缓存中的内部索引直接暴露，也不会因用户匿名化继续显示旧用户名。
- `8c04d9ab` 让前端不再直接显示生成协议中的 `Banned`、`Disqualified`、`Attack`、`Defense`、`Succeeded`、`Rejected` 等原始值。排名状态、明细类型与结果均通过生成 SDK 类型约束的穷尽映射实时翻译；中英文切换无需刷新页面。
- 补齐真实封禁/解封重盘验证。真实 PostgreSQL + `TeamModerationStore` + Worker maintenance + FusionCache 集成测试证明：队伍初始具有已结算分数；封禁后排名状态为 Banned、总分归零且 Slot 清空；解封后按不可变 GameplayFact 历史重新投影，原分数和已结算 Slot 恢复。AWDP Full E2E 也通过管理员强类型 HTTP 封禁/解封接口验证了同一闭环。此前 `5e61f391` 的交接条目提前声称 Full E2E 已覆盖封禁/解封重盘，实际当时没有对应操作；本阶段已补成真实用例并纠正该记录。
- 没有新增业务表、字段、EF migration、HTTP 响应契约或 OpenAPI/TypeScript SDK 变化；版本仍为 `0.1.0-alpha.62`，属于同一发布候选的审计修复。
- 验证：Release solution build 0 warning/0 error；后端完整 TUnit 1038 项中 1036 通过、0 失败、2 项仅因未配置真实 Kubernetes/Libvirt 环境按设计跳过；排行榜定向单元 15/15、真实 PostgreSQL 定向集成 2/2；AWDP Full E2E 1/1（约 4m23s，包含 Redis/PostgreSQL/API 韧性检查及封禁/解封重盘）；前端 `bun test` 241/241（1782 assertions）、typecheck、production build；C# analyzers、EF model drift 与 `git diff --check` 均通过。仓库没有 lint script，未将 lint 误报为已执行。
- 本阶段位于独立工作树 `E:\SourceCode\NoCTF-leaderboard-matrix-20260819`，未覆盖主工作区的用户/协作者修改；未推送、未部署、未操作生产数据。

## 2026-08-20 alpha.62 统一排行榜分页明细完整性修复

- `1a00d069` 修复数据库聚合投影与分页详情的数量边界。原投影会把同类 `GameplayFact` 折叠为一条并用 `Multiplicity` 计入 `EntryCount`，但 Slot 和全局人工调分详情仍只分页聚合后的代表记录，导致“完整数量为 N，详情只有 1 条”。现在主快照仍保持有界聚合，只有签名游标详情请求会按当前页有界读取 PostgreSQL 原始事实，从而让 `EntryCount`、breakdown 和可遍历详情一致，不向主响应塞入无界历史。
- 详情分页严格绑定快照 `DataAsOf`、Schema revision 和 Snapshot version，并在 PostgreSQL `RepeatableRead` 快照中使用该时点前最后一条不可变 `GameplayFactAdjudicated` 事件还原状态/结果。后续重判不会改写已打开的分页链。对于旧事件未保留 failure/victim 等完整计分身份的歧义数据，后端不猜测单条得扣分，而是返回 `null`，并保留主 Slot 聚合分数为权威结果。
- Schema revision 原先只哈希列坐标，轮次的开始、结束、结算时间或状态单独变化时不会变更。现在轮次 ID/序号/时间/状态与列目录共同生成稳定 revision；公开榜的过滤 Schema 使用同一规则，前端不会复用过期轮次元数据。
- 新增和更新的设计约束已记录在 `docs/leaderboard-matrix-main-agent-prompt.md`。本次没有新增业务表、EF migration、snapshot 或 HTTP 响应契约；OpenAPI 工具导出和 `bun run api:gen` 均无实质差异。两份 OpenAPI SHA-256 均为 `DA38D2C5B1F65AF177B5AC141393752A636427417672C574BAF17B2B9DEF7A50`，`types.gen.ts` 为 `4A906B41FA880C835EE2D2125477628AD135AC75A30377F6BCF584A74003062B`；EF model drift 检查无变化。
- 验证：Release solution build 0 warning/0 error；排行榜 Endpoint 15/15；真实 PostgreSQL 排行榜投影/历史分页 1/1；完整非 Integration TUnit 851/851；完整 Integration 186 项中 184 通过、0 失败、2 项因未配置真实 Kubernetes/Libvirt 环境按设计跳过；前端 `bun test` 240/240（1771 assertions）、typecheck 和 production build 通过；变更 C# 文件 whitespace、全仓 analyzers、EF drift 与 `git diff --check` 通过。仓库全量 whitespace 仍会报本次未触及文件的既有格式偏差，未越界格式化无关代码。
- 版本仍为 `0.1.0-alpha.62`；这是同一发布候选的审计修复，未重复递增版本。本阶段未推送、未部署、未操作生产数据。

## 2026-08-20 alpha.62 统一排行榜精确明细审计与发布收口

- `a633acd9` 修复排行榜明细与主快照之间的事实边界。旧实现会在打开 Slot 明细时重新读取当前 `GameplayFact`，冻结榜单可能因此混入冻结后的事实，并把主快照未携带的精确分值重建为 `0`。现在 Slot 明细和全局人工调分明细都从生成主榜时的同一份 `ScoreboardProjection` 读取；实时榜使用同一投影版本，冻结榜保持冻结时事实，后续数据不会改写已打开的分页链。
- 主榜响应继续保持有界，每个 Slot 最多携带 5 条摘要；精确明细通过强类型签名游标分页。内部 `DetailActors` 去重保存投影 Actor，Entry 只保存 `actorIndex`；每一页只返回该页使用的 Actor 目录。游标签名绑定 endpoint、比赛、调用者、队伍、列、Schema revision、Snapshot version 与 `DataAsOf`，不能跨比赛、跨用户、跨列或跨版本重放。
- 新增强类型 `GET /competitions/{competitionId}/leaderboard/teams/{teamId}/adjustments`，总分入口可分页查看全部全局人工调分；前端仅调用重新生成的 SDK。Slot 与调分弹窗均使用请求代次栅栏，旧响应不会覆盖新选择；失败会保留已加载内容并显示可理解错误。
- 最终复核发现公开榜隐藏未发布题目后会重排列索引，但内部明细 allocation 仍引用旧索引；同时赛道过滤没有同步裁剪内部 allocation。现已按公开列映射重排 `EntryAllocations`，并按可见队伍过滤 Entry/Adjustment allocations，避免详情为空、串列或跨赛道残留。新增公开列重排回归后，排行榜 Endpoint 定向测试为 13/13。
- `ee042c79` 将版本从 `0.1.0-alpha.61` 递增到 `0.1.0-alpha.62`。本阶段没有新增业务表、EF migration 或 snapshot 变更；未恢复旧的 raw-fact 明细读取器，也没有在前端重算分数。
- OpenAPI 两份制品均由工具导出且 SHA-256 同为 `DA38D2C5B1F65AF177B5AC141393752A636427417672C574BAF17B2B9DEF7A50`；TypeScript SDK 由 `bun run api:gen` 生成，`types.gen.ts` SHA-256 为 `4A906B41FA880C835EE2D2125477628AD135AC75A30377F6BCF584A74003062B`。连续第二轮导出和生成无差异，生成 SDK 未被手工修改。EF model drift、C# format/analyzers 与 `git diff --check` 均通过。
- 最终验证：Release solution build 0 warning/0 error；完整非 Integration TUnit 847/847；真实 PostgreSQL `LeaderboardProjectionPersistenceTests` 1/1；排行榜 Endpoint 13/13；前端 `bun test` 240/240（1771 expectations）、typecheck 与 production build 通过。仓库没有 lint script，未将 lint 误报为已执行。
- 完整 Integration 共 186 项：183 通过，2 项因未配置真实 Kubernetes/Libvirt 环境按设计跳过；唯一失败为 Docker Hub 匿名拉取 `busybox:1.37.0-glibc` 返回 `EOF`。该用例独立复跑仍失败，直接执行 `docker pull busybox:1.37.0-glibc` 也得到相同 `EOF`，因此记录为外部镜像仓库/网络阻塞，不冒充通过，也没有通过修改产品或测试绕过。
- 四模式 Full E2E 已在本分支的统一排行榜实现上顺序通过并记录于下方 alpha.61 交接；本次精确明细修复未改变计分、生命周期或 Runtime。该轮未重新操作生产比赛数据，也未部署；按用户授权，本记录提交后仅将审计通过的提交快进推送到远程 `main`。

## 2026-08-20 alpha.61 统一排行榜一致性与详情边界修复

- `97cc8fa5` 修复统一排行榜矩阵审计发现的问题。主快照仍保持有界：每个 Slot 最多携带 5 条摘要记录、每队最多携带 5 条全局调整，同时新增 `GlobalAdjustmentCount` 保留完整计数；完整历史继续通过签名游标详情读取，没有新增业务表、EF migration 或 snapshot 变更。
- 排行榜详情请求现在绑定主快照的 `DataAsOf`，数据库只读取该时间点以前的事实；签名游标同时绑定比赛、调用者、队伍、列、Schema revision、Snapshot version 与时间截止点，后续事实不会混入已打开的分页链。`FusionLeaderboardCache` 使用 PostgreSQL `RepeatableRead` 生成同一快照中的目录、Schema 与分数投影，避免跨查询撕裂。
- 公开排行榜会在后端过滤无权查看的内部赛道、内部队伍及 Actor；当前队伍仍可看到自己的内部赛道。详情页每一页都返回页内 Actor 目录，前端按记录 ID 保存显示名，翻页后不会因页内 ActorIndex 复用而显示错误提交者。
- 前端 `useScoreboardMatrix` 只接受 catalog revision、schema revision、snapshot schema revision/version 相互一致的三件套；发现并发刷新产生的混合版本时最多重试 3 次，失败保留上一份成功数据并显示明确错误。AWDP 防御成功动画同时移除了无效 CSS 百分比计算，production build 不再产生该动画的 CSS 解析警告。
- OpenAPI 两份制品均由工具导出，SHA-256 同为 `C75CEE69F5B93966CE36C0CF868F9FF303F69000E0915A2562C2307FDAB5F80C`；TypeScript SDK 由工具生成，`types.gen.ts` SHA-256 为 `FD974665BE841A8DBE0C73000BFBC2D1B84B82B6237CD5E5E91B20BEA`。连续第二轮导出与生成哈希一致，未手改生成 SDK。
- 验证结果：Release solution build 0 warning/0 error；完整非 Integration TUnit 844/844；排行榜 Endpoint 定向 3/3、OpenAPI Actor 契约 1/1、真实 PostgreSQL 投影/详情 1/1；前端 `bun test` 240/240（1768 assertions）、typecheck 与 production build 通过；C# analyzers、EF model drift 和 `git diff --check` 通过，仓库没有 lint script。
- 完整 Integration 共 1029 项：1026 通过、2 项因未配置真实 Kubernetes/Libvirt 环境按设计跳过；唯一失败是 Redis Runner capacity TTL 的瞬时时序断言，同一 `RedisRunnerCapacityGateTests` 测试类立即独立复跑 9/9 通过。最终 Actor 详情变更后另行执行的真实 PostgreSQL 定向测试 1/1 通过。该抖动未被误报为全量通过，也未通过延长产品超时掩盖。
- 本阶段位于独立工作树 `E:\SourceCode\NoCTF-leaderboard-matrix-20260819`，未推送、未部署、未操作生产数据；版本仍为 `0.1.0-alpha.61`，本修复没有额外递增发布版本。

## 2026-08-20 alpha.61 统一排行榜矩阵有界投影修复

- `c73240cc` 修正 alpha.60 统一排行榜矩阵的生产投影边界。`FusionLeaderboardCache` 不再把整场 `GameplayFact` 历史全部载入内存，而是通过 `LeaderboardFactProjectionReader` 在 PostgreSQL 中按 CTF、AWD、AWDP、KoH 各自的权威语义做集合式聚合；完整不可变事实仍由已有强类型签名游标详情接口分页读取。没有新增业务表、EF migration 或 snapshot 变更。
- 聚合事实新增 `Multiplicity` 与最后发生时间，所有尝试数、成功数、错误提交扣分、Hint 扣分、人工调分、KoH 控制观察和旧投影总分均按聚合数量计算。主快照只生成非空 Slot，保留完整 `EntryCount`、breakdown、得分摘要及受控数量的最近零分操作，避免历史事实数量线性放大维护投影内存。
- AWDP 已结算轮次现在为每条攻击/防御轨道生成稳定、确定性的结算得分记录；原始 Break/Fix 操作在 Slot 详情中只作为操作事实显示为 `0/0/0`，不会重复承载该轮结算分。当前轮仍为 `Pending` 且分数为 `null`，后续轮次不会改写历史结算。空生命周期事件列表会正确回退到比赛开始时间，不再把有效运行时长误算为零。
- 真实 PostgreSQL 对照测试为四种模式各写入 250 条事实，比较数据库聚合投影与原始事实权威投影的总分、稀疏 Slot、分数状态、攻防 breakdown 和完整记录数；每场投影访问 `gameplay_facts` 的查询保持在 1–2 次。100 队伍 × 20 题 × 50 轮的理论 100,000 单元格压力测试只生成 49 个有事实的 Slot，未生成空矩阵。
- `8bc6669b` 将版本从 `0.1.0-alpha.60` 递增到 `0.1.0-alpha.61`。本阶段未手改 OpenAPI、生成 SDK、migration 或 snapshot；也未操作生产数据、推送或部署。
- 验证结果：Release solution build 0 warning/0 error；C# analyzers 与 EF model drift 通过；非 Integration TUnit 840/840；真实 PostgreSQL 排行榜投影对照 1/1；前端 `bun test` 238/238（1755 assertions）、typecheck、production build 通过，仓库没有 lint script。完整 Integration 共 186 项：183 通过、2 个环境型跳过（真实 Kubernetes/Libvirt 未配置），唯一失败发生在 Testcontainers Ryuk 创建测试资源前；同一 `BotAuthenticationTests` 独立复跑 1/1 通过，确认不是产品逻辑失败。
- 四模式 Full E2E 顺序执行并全部通过：CTF 1/1（58.673s）、AWD 1/1（1m30.573s）、AWDP 1/1（4m24.848s）、KoH 1/1（1m11.909s）；每套均包含 API/Redis/PostgreSQL 重启韧性检查，Compose、容器、网络与临时镜像由编排器按精确项目身份清理。
- OpenAPI 导出与 TypeScript SDK 生成连续执行两轮，OpenAPI SHA-256 始终为 `D4A4C316CB5186EABA8FDDBDBB83864763F665F5E7BE69851E589EF42E5D58D7`，`types.gen.ts` 为 `9240DE2D0550DC78A8FEB6911795C47D888A72E6D9BD9BFD61E3BF482E8E5597`，全部生成制品哈希保持一致。两份 Compose 配置通过；Kubernetes 49 个资源中 46 个有效、3 个 Cilium CRD 因无外部 schema 按门禁策略跳过、0 个错误。

## 2026-08-19 alpha.60 统一排行榜矩阵协议

- 基线为远程 `main` 的 `870b5201f0b80e25af4871a6f5a56166cf8cf090`，实现位于独立工作树 `E:\SourceCode\NoCTF-leaderboard-matrix-20260819`、分支 `codex/leaderboard-matrix-20260819`；原工作区的用户/协作者修改未被切换、清理或覆盖。本阶段未推送、未部署、未操作生产数据。
- `042ad90f` 引入统一规范化排行榜查询投影。四种模式共用 `ScoreboardSchema`、`ScoreboardSnapshot`、Actor 目录、稀疏 `ScoreSlot`、`ScoreBreakdown`、`ScoreEntry` 与全局调整协议；横轴按 `CompetitionChallengeId + RoundId` 建立稳定连续列，队伍每行只传非空 Slot。Challenge、轮次、队伍和提交者信息分别去重，不创建 `leaderboard_slots` 等业务表。FusionCache 在一次投影中原子替换旧排行榜和规范化 Schema/Snapshot，失败不会覆盖上一份成功快照。
- 规范化投影的所有分数、衰减、奖励、扣分与总分均由后端生成，前端只做布局和简单算术诊断。CTF 使用 `Solve/BloodAward/Hint`，动态分为 `Provisional`；AWD 使用 `Attack/Defense/Availability`；AWDP 每题每轮分别映射 `Attack/Defense`，当前轮 Slot 为 `Pending` 且三项分数均为 `null`，只展示成功数/尝试数，结算轮次才写入固定整数分；KoH 使用 `Control`。AWDP 成功队伍数按 `AffectsCompetitiveResults` 计算，未再错误复用仅属于 CTF 动态题的 `AffectsDynamicChallengeScore`，也不再为对齐旧总分伪造 `BanRecalculation` 调整。
- `4ae028e5` 将现有排行榜响应演进为规范化 Snapshot，并新增强类型 Challenge Catalog、Scoreboard Schema、Slot Detail 三个资源；Slot Detail 使用现有签名游标并绑定 endpoint、competition、调用者、team、column 与筛选状态。工作人员在题目取消发布后仍可取得最小历史目录信息，参赛者不会获得未发布题面或敏感资料。SignalR/Redis 只广播 `competitionId/version/schemaRevision/challengeCatalogRevision`，不广播完整排行榜 JSON。
- 两份 OpenAPI 均由工具导出且 SHA-256 同为 `D4A4C316CB5186EABA8FDDBDBB83864763F665F5E7BE69851E589EF42E5D58D7`；TypeScript SDK 由 `bun run api:gen` 生成。连续两轮 OpenAPI 导出和 SDK 生成覆盖 18 个制品，SHA-256 全部保持一致；生成 SDK 未被手工修改。
- `6e787802` 提供共享 `useScoreboardMatrix` 加载/刷新状态机和协议工具。Catalog、Schema、Snapshot 并行加载；按 `CompetitionChallengeId`、round id、column index、actor index 建立映射；旧 version/旧 schema 响应不能覆盖新状态；短时通知合并，刷新期间到达的新通知会执行 trailing refresh；失败保留上一份成功快照。排行榜、题目卡、CTF 中控、AWDP 中控和详情页均使用生成 SDK 与同一矩阵协议，前端没有计分公式。
- `5e61f391` 补齐后端/API/前端/四模式 Full E2E 回归。100 支队伍 × 20 题 × 50 轮的理论 100,000 单元格场景只生成非空 Slot，主快照不嵌 Challenge；Slot 明细通过游标分页。AWDP Full E2E 覆盖两队、两题、至少三轮、当前轮 Pending、已结算 Attack/Fix、历史轮次不受后轮影响、封禁/解禁重盘、Redis/PostgreSQL/API 重启恢复与详情分页。
- `4856d4f9` 将版本从 `0.1.0-alpha.59` 递增到 `0.1.0-alpha.60`。本阶段没有新增业务表、EF migration 或 snapshot 修改；`dotnet ef migrations has-pending-model-changes --configuration Release --no-build` 返回无模型漂移。
- 最终验证：Release solution build 0 warning/0 error；完整非 Integration TUnit 838/838；前端 `bun test` 238/238（1755 assertions）、`bun run typecheck`、production `bun run build`；C# analyzer 与 `git diff --check` 通过。完整 Integration 在最终分支共 185 项：182 通过，2 项因未配置真实 Kubernetes 集群和 Libvirt disk path 按设计跳过，1 项 Wolverine 双节点维护代理故障转移出现 30 秒时序超时；该唯一失败用例随后独立重跑 1/1 通过。仓库不存在 lint script，未将 lint 报告为已执行。
- 四模式 Full E2E 在最终 AWDP 结算修正后分别通过：CTF 1/1（约 1m03s）、AWD 1/1（约 1m33s）、AWDP 1/1（约 3m47s）、KoH 1/1（约 1m32s）；测试创建的 Compose、容器、网络和数据均由编排器按精确项目身份清理，没有执行全局 prune。
- Microsoft Edge 当前仅打开已部署的生产 `alpha.59`，本分支未部署且本任务没有部署授权。因此无法对 alpha.60 做可信的 Edge Network/Console 验收；该项明确阻塞于合并/部署后的可访问环境，未用旧生产页面冒充通过。需要后续确认：题目目录仅在 catalog revision 变化时重取，ScoreboardUpdated 不包含完整快照，连续通知仅触发合并刷新，Console 无新增错误。

## 2026-08-19 alpha.59 AWDP 参与者页与中控布局修正

- `63c79b79` 移除 AWDP 题目详情页中脱离具体操作语境的整页错误横幅；运行环境与一次性 Fix 操作仍保留各自的明确错误反馈。与此同时，将题目详情移动为 `challenges/[ccId]/index.vue`，使详情页与 `fix-history.vue` 成为同级 Nuxt 路由，修复“Fix 历史”按钮地址变化但页面无响应的问题。
- `8b60c20c` 修正 AWDP 中控“队伍题目动态”布局：翻页控制改为固定 30px 宽度，中间队伍列表连续占满剩余空间；单队时翻页键禁用，不再出现箭头轨道占据大块空白。题目状态卡固定为 150px，并同步收紧图标与内容间距，约为原拉伸高度的一半，攻防状态和结算分仍完整可见。
- 前端定向回归 14/14 通过；完整 `bun test` 233/233 通过，`bun run typecheck`、production build 与 `git diff --check` 均通过。构建只保留既有 CSS 表达式、chunk 大小、plugin timing 与第三方 Node exports deprecation 警告。
- 版本从 `0.1.0-alpha.58` 递增到 `0.1.0-alpha.59`。本阶段未改变 HTTP/OpenAPI/生成 SDK、后端业务逻辑、数据库模型或 migration；原工作区已有 `TODO.md`、临时部署包及未跟踪目录继续原样保留。
- 功能、版本与本阶段交接提交已快进推送到远程 `main`，功能部署代码基线为 `70c2b54ddfbf8af4794cb47f65126692911b08a0`；部署机 `/root/NoCTF` 通过经过 `git bundle verify` 的增量 bundle 快进到同一提交，没有覆盖生产 `.env`、Compose overlay 或其他未跟踪配置。
- 部署前创建 PostgreSQL custom-format 备份 `/root/backups/noctf-pre-alpha59-70c2b54d-20260819T025000Z.dump`，`pg_restore --list` 校验通过，SHA-256 为 `824e4ecdbf8a750ed6aa81c1e80d21f67a6dde45951fd97bb4d4330543893534`。migration 容器确认数据库已经是最新状态，本次没有应用 migration，也没有手工修改生产业务数据。
- 服务器完整构建并固化 `deploy-backend:alpha59-70c2b54d`、`deploy-worker:alpha59-70c2b54d`、`deploy-runner:alpha59-70c2b54d`、`deploy-migration:alpha59-70c2b54d`，镜像 ID 分别为 `96b25d306efc`、`0e1f90a4f227`、`01aa686247f4`、`a99194edd228`。当前 alpha.58 API、Worker、Runner 镜像保留 `rollback-alpha58-57337799` 标签作为直接回滚基线。
- 仅强制重建 API、Worker、Runner；PostgreSQL 与 Redis 自 `2026-08-08T10:50:03Z` 起持续运行，部署期间未重启且 RestartCount 为 0。三个 alpha.59 服务均为 `healthy`、RestartCount 为 0；外部 `https://101.43.46.244/`、`/health`、`/health/ready` 均返回 HTTP 200。从切换时间 `2026-08-19T02:55:49Z` 起扫描三项服务日志，Error、Critical、Fatal 与未处理异常匹配数均为 0。
- 部署前清理服务器中过时且未运行的 NoCTF 平台构建镜像、一个已退出 migration 容器及无引用 BuildKit 缓存；没有删除题目镜像、生产卷、数据库备份或比赛 Runtime 资源，也没有执行全局 Docker prune。根卷可用空间由约 3.4 GB 提升到部署完成后的 9.1 GB；平台托管 Runtime 容器与网络在切换前后均为 0。

## 2026-08-19 alpha.58 测试生产环境推送与部署

- alpha.58 功能、版本、测试可见性补充与阶段交接已合并到最新远程 `main` 并推送，部署代码基线为
  `57337799807bdf332b02ca123923fc6913d1f26b`。部署机通过已校验的增量 Git bundle 从
  `279e3179` 快进到同一提交；生产工作树仅保留既有未跟踪 Compose overlay
  `deploy/docker-compose.prod.yml` 及其 `pre-alpha55` 回滚副本，没有覆盖 `.env` 或其他生产配置。
- 干净合并工作树复验时发现远程 `main` 已有 Runner timeout 测试使用 internal seam、但遗漏测试程序集可见性声明；
  `57337799` 补入最小 `InternalsVisibleTo("NoCTF.Tests")`，没有改变产品运行时逻辑、HTTP 契约、数据模型或迁移。
- 部署前创建 PostgreSQL custom-format 备份
  `/root/backups/noctf-pre-alpha58-57337799-20260819T020041Z.dump`，文件大小 206324 bytes，SHA-256 为
  `8a29b10543ce32d833e35e863a9f1a1bf2fbcd68ae20ded5d8f03bf86a5186e1`。migration 容器确认数据库已经是
  最新状态，本次没有应用 migration，也没有手工修改生产业务数据。
- 在服务器从部署基线完整构建并固化 `deploy-backend:alpha58-57337799`、
  `deploy-worker:alpha58-57337799`、`deploy-runner:alpha58-57337799`、
  `deploy-migration:alpha58-57337799`；对应镜像 ID 分别为 `aceeb61af31d`、`c2b436abd50e`、
  `19c21c4273ea`、`3da097c20f6f`。部署前运行的 alpha.57 镜像保留
  `rollback-alpha57-279e3179` 标签用于回滚。
- 切换前后平台管理的 Runtime 容器和网络均为 0。仅滚动重建 API、Worker、Runner，PostgreSQL 与 Redis
  始终未重启；三个新服务均为 `healthy` 且 RestartCount 为 0。外部
  `https://101.43.46.244/`、`/health`、`/health/ready` 均返回 HTTP 200，API 容器内确认程序集版本为
  `0.1.0-alpha.58`。
- 从切换时间 `2026-08-19T02:06:56Z` 起检查 API、Worker、Runner 启动与运行日志，未发现 Error、Critical、
  Fatal 或未处理异常。仅保留既有 DataProtection 临时密钥、显式 URL 端口覆盖及 Wolverine 动态代码生成提示。
- 部署前在合并后的干净工作树执行完整门禁：Release solution build 0 warning / 0 error；后端完整 TUnit
  1014 total、1012 passed、0 failed、2 个环境型 skipped（真实 Kubernetes/Libvirt 环境未配置）；前端
  `bun test` 231/231（1718 assertions）、typecheck 与 production build 通过。前端构建仍只有既有 CSS
  表达式、chunk 大小、plugin timing 与第三方 Node exports deprecation 警告。
- 部署后根卷约有 3.4 GB 可用（92% 使用）。没有执行全局 Docker prune，没有删除数据库备份、生产卷、
  回滚镜像或题目镜像；原工作区已有 `TODO.md` 修改、临时部署包及未跟踪目录均未纳入提交。
- 本次未在 Microsoft Edge 中伪报视觉验收；用户可直接检查 AWDP 中控的跨轮累计分数、实时本轮倒计时、
  攻防成功/总提交、事件聚焦以及 WEB/PWN 图标。HTTPS 与服务健康闭环已完成。

## 2026-08-19 alpha.58 AWDP 跨轮累计计分与中控动态增强

- `a67bc6ab` 修正 AWDP 计分激活语义。每支队伍、每道题、每条 Break/Fix 轨道只取首个有效 Correct 作为唯一激活点；从激活轮开始，每个已完成轮次都按该轮截至当时的累计激活队伍数计算独立动态分值并持续累加。后续轮次新增队伍只影响后续轮次，重复正确提交、重复 callback 与 Wolverine 重投不会建立第二次激活；当前进行中轮次仍不提前计分，Pause/Resume/Finish 与封禁/解禁的权威重盘语义保持不变。
- 同一提交扩展公开排行榜契约，新增可空 `roundDurationSeconds` 与 `currentRoundRemainingSeconds`。AWDP 投影使用暂停感知的有效比赛时间生成权威当前轮和剩余秒数；前端以排行榜 `generatedAt` 为基准每秒推进 Running 比赛的本轮倒计时，Paused 时冻结，Finished 时归零。两份 OpenAPI 制品由工具导出，TypeScript SDK 由工具重新生成，没有手写协议。
- AWDP 中控顶栏不再用累计攻击分/防御分冒充操作统计，改为攻击与防御各自的“成功数 / 总提交数”；Attempted 与 Resolved 通过 GameplayFactId 合并后只计一次，并通过签名游标读取当前比赛窗口内全部 AWDP 操作事件。事件播放会自动选中对应队伍并将对应题目置顶、持续聚焦；题目卡复用全站方向图标，WEB 显示网络/地球图标，PWN 显示 Bug 图标。新增红/青双轨能量场、雷达扫描、事件爆发高亮及 scanline，同时保留 `prefers-reduced-motion` 降级。
- `9d9f94f0` 将版本从 `0.1.0-alpha.57` 递增到 `0.1.0-alpha.58`。本阶段没有新增业务表、字段或 EF migration；没有推送、部署或修改生产业务数据。用户已有 `TODO.md` 修改、临时部署包及未跟踪目录均原样保留且未纳入提交。
- 验证结果：
  - `dotnet build backend/NoCTF.slnx -c Release --no-restore`：通过，0 warning / 0 error。
  - `AwdpGameplayFactEvaluatorTests`：9/9 通过；非 Integration TUnit：829/829 通过。覆盖首次 Break/Fix 后无需逐轮重复提交仍连续累加、动态人数变化不追溯旧轮、重复事实去重、封禁重盘、暂停感知和结束冻结。
  - 前端 `bun test`：231/231 通过（1718 assertions）；`bun run typecheck` 与 production `bun run build` 通过。构建仅保留既有动画组件 CSS 表达式、chunk 大小、plugin timing 与第三方 Node exports deprecation 警告。
  - OpenAPI 与 TypeScript SDK 连续第二轮导出/生成哈希一致；两份 OpenAPI SHA-256 均为 `CD34201C266ADBEDD8F8E652AE7F5A3E31C72DAB350FEF21379B4B87A33FCB76`，生成 `types.gen.ts` SHA-256 为 `52DBF7FD7C4E01C1462F3F8F8599E35CD7E403535DC6B0DEB9C454F4B64E052A`。
  - 变更 C# 文件 `dotnet format ... analyzers --verify-no-changes` 与 `git diff --check` 通过。
- 尚未执行生产环境浏览器验收：本阶段尚未推送或部署，因此没有在生产页面上伪报视觉通过。部署后应使用 Microsoft Edge 验收本轮倒计时跨轮归位、操作总数、WEB/PWN 图标、事件聚焦和 reduced-motion，并确认 Network/Console 无新增错误。

## 2026-08-19 alpha.57 测试生产环境推送与部署

- alpha.57 功能、版本和阶段交接内容已经推送到远程 `main`。由于直接 Git HTTPS 推送间歇性被连接重置，使用 GitHub Git Data API 按本地已验证树重建提交，并以 `108b94742cb26769923b4c17b058749b511debbf` 完成快进；远程最终 tree `39fbf854acea6db2350d707e9d8564d8874d3df8` 与本地 alpha.57 tree 完全一致。部署机通过经过 `git bundle verify` 的增量 bundle 快进到同一提交，没有覆盖生产 `.env`、Compose overlay 或其他未跟踪配置。
- 部署前创建 PostgreSQL custom-format 备份 `/root/backups/noctf-pre-alpha57-108b9474.dump`，文件大小 204541 bytes，SHA-256 为 `9258b8b8a507a3e4dc6dff175ac5bc039e6248a7e13823fa98cd7408be54bbf2`。migration 容器确认数据库已经是最新状态，本次没有应用 migration，也没有手工修改生产业务数据。
- 在服务器从完整源码构建并固化 `deploy-backend:alpha57-108b9474`、`deploy-worker:alpha57-108b9474`、`deploy-runner:alpha57-108b9474`、`deploy-migration:alpha57-108b9474`；对应镜像摘要分别为 `d975ca7234b6`、`cc138ead0877`、`2d2c9fa39517`、`070cc75a36ae`。旧 alpha.56 镜像同时保留 `pre-alpha57-108b9474` 和 `alpha56-f0420fd9` 回滚标签。
- 仅滚动重建 API、Worker、Runner，PostgreSQL 与 Redis 始终未重启。切换后五个服务全部 `healthy`，API、Worker、Runner 的 RestartCount 均为 0；外部 `https://101.43.46.244/`、`/health`、`/health/ready` 均返回 HTTP 200，API 容器内确认程序集版本为 `0.1.0-alpha.57`。
- 从切换时间 `2026-08-19T01:04:51Z` 起检查 API、Worker、Runner 日志，未发现 `Error`、`Critical`、`Exception`、未处理异常或失败记录。切换前后没有平台托管的 Runtime 容器或网络，部署未清理、修改或重建任何比赛 Runtime 资源。
- 部署后根卷约有 4.7 GB 可用。没有执行全局 Docker prune，没有删除数据库备份、生产卷或回滚镜像；用户已有 `TODO.md` 修改、临时部署包及未跟踪目录均原样保留且未纳入提交。

## 2026-08-19 alpha.57 AWDP 赛事中控大屏

- `3ffeb85e` 新增 AWDP 专用 `/competitions/{competitionId}/awdp-live` 中控大屏，并只在 AWDP 比赛侧栏显示入口；CTF 原有 3D 大屏保持不变。页面使用固定 `1920×1080` 虚拟画布和等比留黑缩放，包含顶栏轮次/倒计时、左侧实时操作流、中央 FIFO 动画区、已结算攻击/防御/总分 Top 8、队伍题目轮播及底部 JavaScript 连续速览。
- 中央区域实现四套彼此独立的代码动画：攻击成功、攻击失败、防御成功、防御失败。每条已裁决结果完整播放 5.4 秒，突发事件按 `(OccurredAt, Id)` 排队且不覆盖；首次载入历史只建立去重基线，不回放旧动画。队伍轮播每 8 秒切换，底部速览使用 `requestAnimationFrame`，悬停或键盘聚焦时暂停；不引入 MP4 或外部动画资产。
- Domain/API 契约新增永久公开事件 `AwdpBreakResolved` 与 `AwdpFixResolved`。Break 与一次性 Fix 在终态裁决后分别写事件，公开载荷只含比赛、题目、队伍、GameplayFact 状态/结果和必要 Runtime generation，不复制 Flag、Patch、Checker 输出、失败原因或作弊证据。前端通过生成 SDK 拉取事件和排行榜，SignalR 只触发权威数据重读，并保留 10 秒断线兜底刷新。
- 大屏严格使用已完成轮次的排行榜投影，分开显示 `attackScore`、`defenseScore` 与总分，不预测当前轮分数；内部赛道在队伍榜、轮播及计数中排除。文案只表达“队伍对题目的攻击/防御”，没有虚构受害队伍。普通比赛动态页也为两类结果事件提供中英文成功/失败文案，但不广播内部验证过程。
- `3ad94578` 将平台版本从 `0.1.0-alpha.56` 递增到 `0.1.0-alpha.57`。本阶段没有新增业务表、字段或 EF migration；OpenAPI 两份制品与 TypeScript SDK 均由工具生成。没有推送、部署或修改生产业务数据；用户已有 `TODO.md` 修改、临时部署包及未跟踪目录原样保留且未纳入提交。
- 验证结果：
  - `dotnet build backend/NoCTF.slnx -c Release --no-restore`：通过，0 warning / 0 error。
  - 非 Integration TUnit：829/829 通过；完整 Integration：185 total，183 通过、0 失败、2 个环境型跳过，耗时 5 分 29.576 秒。跳过项仅为本机未配置的真实 Kubernetes 集群与 Libvirt 磁盘；PostgreSQL、Redis、Wolverine 与 Docker 的其余真实集成用例均通过。
  - AWDP Full E2E：1/1 通过，耗时 4 分 33.465 秒；攻击 Runtime、一次性 Fix、动态 Flag、按轮结算以及 API/Redis/PostgreSQL 重启韧性检查通过，编排器已精确删除测试容器、网络与镜像。
  - 前端 `bun test`：229/229 通过（1708 assertions）；`bun run typecheck` 与 production `bun run build` 通过。构建仅保留既有大 chunk、plugin timing 与第三方 Node exports deprecation warning。
  - OpenAPI 与 TypeScript SDK 连续导出/生成两次哈希一致；两份 OpenAPI SHA-256 均为 `0971DDA946F4494F87A314EA0AFC2C48896FC4AA180B7784910DD150ECDF7ED9`，生成 `types.gen.ts` SHA-256 为 `1FAE91B83F8056B8438DB2565E6931C407BA4C58023D82F3D318215E68E2B2FE`。
  - `dotnet format backend/NoCTF.slnx analyzers --verify-no-changes --no-restore`、EF `migrations has-pending-model-changes --configuration Release --no-build` 和 `git diff --check` 均通过。
- Microsoft Edge 视觉验收未完成：已在本地可丢弃 AWDP E2E 环境运行期间打开独立 Edge 窗口，但 Computer Use 因无法可靠确认浏览器当前 URL 而按安全策略终止。没有绕过检查、没有切换到内置浏览器，也没有将该项误报为通过；下一步是在 alpha.57 部署到可访问环境后使用 Edge 验收 1920×1080、2560×1440 和非 16:9 缩放、20 条事件队列、四类动画、控制台与 Network。

## 2026-08-19 alpha.56 测试生产环境推送与部署

- 功能、版本与阶段交接提交 `2f18f836`、`eff4901c`、`d7dbfa3a`、`f0420fd9` 已推送到远程
  `main`；远程 `main` 与部署代码基线均为 `f0420fd9f436d8841455ef60bac9c4f6fd3ca55c`。部署机到
  GitHub 的出站连接仍不可用，因此使用本地生成并通过 `git bundle verify` 的完整 Git bundle 同步源码；同步后
  仅保留部署机原有的未跟踪生产 Compose overlay 及其回滚副本，没有覆盖 `.env` 或其他生产配置。
- 部署前创建 PostgreSQL custom-format 备份
  `/root/backups/noctf-pre-alpha56-f0420fd9.dump`，文件大小 204662 bytes，SHA-256 为
  `e1398a7dd153de82506c7d062d4b8e0be2612d0cd14100a121a838fc27ca175c`。迁移容器检查结果为数据库已是
  最新状态，本次没有应用 migration，也没有手工修改生产业务数据。
- 在服务器从同一完整源码构建 API、Worker、Runner 与 migration 镜像，并分别保留
  `deploy-backend:alpha56-f0420fd9`、`deploy-worker:alpha56-f0420fd9`、
  `deploy-runner:alpha56-f0420fd9`、`deploy-migration:alpha56-f0420fd9` 回滚标签；对应镜像 ID 分别为
  `5acd1c0e`、`0803f08a`、`a4fa98f7`、`6c107f25`。PostgreSQL 与 Redis 未重建，只强制重建 API、Worker、Runner。
- 切换后 API、Worker、Runner、PostgreSQL、Redis 均为 healthy；外部
  `https://101.43.46.244/`、`/health`、`/health/ready` 均返回 HTTP 200，容器内程序集版本确认
  为 `0.1.0-alpha.56`。从切换时间起检查 API、Worker、Runner 日志，未发现 `Error`、`Critical`、
  `Exception` 或未处理失败。
- 部署完成后根卷仍有约 5.9 GB 可用。临时 Git bundle 与部署时间标记已精确删除，未执行全局 prune，未删除
  生产 Runtime、卷、数据库备份或旧回滚镜像。部署后的源码工作树仅保留既有生产 overlay 未跟踪文件。

## 2026-08-19 alpha.56 AWDP 轮次结算、攻防分拆、操作播报与咨询布局

- `2f18f836` 重构 AWDP 排行榜的结算时间语义：只累计已经结束的逻辑轮次，当前轮次不提前展示未结算分数；
  比赛结束时结算不足一整轮的最后一轮，暂停时长不进入逻辑时钟，封禁或解禁后继续由全部权威事实重放历史轮次。
  Break 与 Fix 各自使用独立动态曲线，并在排行榜响应中明确提供 `attackScore`、`defenseScore`、`penaltyScore`、
  `currentRound` 和 `settledThroughRound`；总分为已结算攻击分、防御分、惩罚及人工调整之和。
- 同一提交将 CTF 正确 Flag 与 AWDP 正确 Break 的长期选手攻击 Runtime 自动推进到停止流程。AWDP 只停止
  `AwdpAttack`，不会误停一次性 `AwdpTarget`；重复 Worker 消息由既有结果转换栅栏保证不会重复发送停止操作。
  Full E2E 已改为明确验证攻击环境停止，再从 Stopped 状态启动新 generation，而不是对已停止实例调用 reset。
- AWDP 接受的 Break/Fix 操作会分别写入永久的 `AwdpBreakAttempted` / `AwdpFixAttempted` 公开比赛事件；事件只包含
  比赛、题目、队伍和操作类型，不复制 Flag、Fix 内容或验证过程。参赛者 Fix 历史列表新增题目与 kind 强类型筛选，
  signed cursor 同时绑定比赛、用户和筛选条件；没有新增手写 URL、DTO、枚举或失败码。
- `eff4901c` 重做参赛者 AWDP 展示：题目卡分别显示“攻击成功”“防御成功”，只有两者都成功才显示“已解出”；
  排行榜和队伍详情分别展示攻击、防御、惩罚与总分，并明确显示已结算轮次，避免把当前轮次的参考曲线误当成实得分。
  题目详情移除冗长内部流程说明，Fix 结果只展示“防御成功”“防御异常：EXP 利用成功”或“防御异常：服务异常”。
  新增选手可访问的单题 Fix 历史页，分页展示状态与脱敏最终结果，不返回 Flag 或 patch 内容。
- 同一前端提交补齐 AWDP 攻击/Fix 操作的赛事播报。咨询页改为左侧紧凑列表、右侧会话区占满剩余宽度；详情卡
  提供至少 36rem 的交流空间，回复输入区增至 7 行并保留窄屏单列布局。
- OpenAPI 两份制品与 TypeScript SDK 均由工具重新生成，并连续导出/生成两次验证幂等。两份 OpenAPI SHA-256
  均为 `2DB8498F88663E9EF5A82D282E651F01F95488CCF49DA84169243989A0D3B5E3`，生成 SDK SHA-256 为
  `B4E4C563E66CE9E37CC9D47ACB4F0201C92747CD5875C6BED67E634AAF3DB0E2`。
- `d7dbfa3a` 将平台版本从 `0.1.0-alpha.55` 递增到 `0.1.0-alpha.56`。本阶段没有新增业务表、字段或 EF migration，
  没有推送、部署或修改生产业务数据；用户已有 `TODO.md` 修改、临时部署包及未跟踪目录均原样保留且未纳入提交。
- 验证结果：
  - `dotnet build backend/NoCTF.slnx -c Release --no-restore`：通过，0 warning / 0 error；版本递增后已复跑。
  - 非 Integration TUnit：829/829 通过。新增 AWDP 轮次、攻防分拆、封禁重盘和结束时不足整轮结算测试均通过；
    CTF/AWDP 正确 Flag/Break 自动停止的真实 PostgreSQL 定向测试 2/2 通过。
  - 完整 Integration：185 total，182 通过、1 个 Wolverine 双节点维护故障转移时序用例在全量并行中超出 30 秒、
    2 个环境型跳过；该失败用例隔离复跑 1/1 通过。跳过项仅为本机未配置的真实 Kubernetes 集群与 Libvirt 磁盘，
    PostgreSQL、Redis、Wolverine 和 Docker 的其余真实集成用例均通过。
  - CTF Full E2E：1/1 通过，耗时 54.514 秒；AWDP Full E2E：1/1 通过，耗时 3 分 50.937 秒；两种模式的
    API、Worker、Runner、PostgreSQL、Redis、MinIO、Docker 闭环及 API/Redis/PostgreSQL 重启韧性检查通过，
    E2E 容器、镜像和网络均由编排器精确清理。
  - 前端 `bun test`：224/224 通过；`bun run typecheck` 与 production `bun run build` 通过。构建仅保留既有
    大 chunk、plugin timing 与第三方 package deprecation warning。
  - `dotnet format backend/NoCTF.slnx analyzers --verify-no-changes --no-restore --severity warn` 通过；EF
    `migrations has-pending-model-changes --configuration Release --no-build` 返回无模型漂移；`git diff --check` 通过。

## 2026-08-19 alpha.55 测试生产环境推送与部署

- 将本阶段功能分支与最新 `origin/main` 合并，保留协作者的 `e8b71bee` SSH.NET 安全版本固定，合并提交为
  `6e7024a2`；该提交已推送到远程 `main`。由于服务器到 GitHub 的出站连接连续失败，部署机使用本地生成并
  校验过的 complete Git bundle 同步到同一提交，没有改写历史或使用不完整源码包。
- 部署前已创建 PostgreSQL custom-format 备份
  `/root/backups/noctf-pre-alpha55-6e7024a2.dump`，SHA-256 为
  `843a088be21e3e984543b604fe79c92b8f66e1ea986e803ccb8eff0ba9806da3`。迁移容器成功应用 EF 工具生成的
  `20260818170133_BindAwdpFixTarget`；没有手工修改 migration 或 snapshot。
- 完成生产拓扑迁移：宿主 Nginx 终结 80/443 TLS，API 仅绑定 `127.0.0.1:8080` 的 HTTP，Worker、Runner
  和内部 callback 继续使用容器网络 HTTP。既有证书复制到 root/nginx 受限目录，Nginx 配置检查通过并设置
  HSTS；HTTP 返回 308，IP、域名、`/health`、`/health/ready` 与首页均返回预期 200。
- 上线验收发现客户端可伪造的转发链会触发 ASP.NET Core 严格头对称校验；`e35108ef` 将唯一可信边缘 Nginx
  的 `X-Forwarded-For` 改为覆盖写入 `$remote_addr`，并增加部署拓扑守卫测试。测试环境同时明确允许域名与
  `101.43.46.244` 两个公开 Host；恶意多值转发头回归请求返回 200，后端不再产生 Forwarded Headers 警告。
- 服务器不能访问 GitHub 下载构建依赖时，从部署前 Runner 镜像提取既有 Kompose 1.38.0，并核对其 SHA-256
  `65a6a720605bead3964e8b22d423a0763de451a236fe03de902e366cf3d9c147` 与仓库固定值一致，再用于构建当前
  Runner；没有引入未校验二进制。新镜像分别保留 `alpha55-6e7024a2` 标签，旧 Runner 保留
  `pre-alpha55-74023e94` 回滚标签。
- 切换后 API、Worker、Runner 均为 healthy，宿主 Nginx 为 active。滚动重建初期出现一次 Wolverine 对已退出
  旧节点发送 `StopRemoteAgent` 等待确认超时；新节点完成选主后没有继续出现 Error、Critical、Exception 或失败
  消息，最终连续 3 分钟日志检查为空。其余仅保留 ASP.NET Core DataProtection 临时密钥和显式
  `ASPNETCORE_URLS` 覆盖默认端口的既有警告，不影响当前无服务端 refresh-session 的认证模型。
- 新 Runner 已按 provider receipt 自动收敛此前残留资源：部署前约 29 个 AWDP target 容器及其网络均被精确
  清理，最终 `noctf.io/managed=true` 容器为 0、网络为 0；数据库 30 条相关 Runtime 全部收敛为 Stopped，
  未通过跳过保护直接删库。另精确删除一个 39 小时前遗留、无端口的 Compose one-off backend 容器。
- 远程构建前仅清理可重建的 BuildKit cache，回收约 7.9 GB；部署完根卷仍有约 7.1 GB 可用。临时 bundle、
  Kompose 与构建目录均已精确移除，保留数据库备份、生产 overlay 及 overlay 回滚副本。除 EF migration 与
  Runtime 自动恢复收敛外，没有手工修改生产业务数据。
- 部署后附加验证：`DeploymentTopologyTests` 10/10 通过；Nginx `nginx -t` 通过；外部 HTTP 308、HTTPS
  health/readiness/home 200，HSTS 响应头存在；运行程序集版本确认 `0.1.0-alpha.55`。

## 2026-08-19 alpha.55 AWDP Fix 失败收敛、资源回收与 Runner 就绪状态

- `54a4f739` 修正内部 API 通信拓扑：API 不再对内部 HTTP 请求执行 HTTPS 重定向，反向代理边界显式处理
  Forwarded Headers，Compose 后端端口仅绑定 loopback，Kubernetes、Nginx 示例与部署文档同步；外部认证刷新 Cookie
  仍保持 `Secure`。这避免 AWDP Runner 下载 Fix archive 时被重定向到容器内不可达的 HTTPS 地址。
- `d3a76c85` 将 AWDP Fix archive 下载、解压或准备阶段的失败改为终态收敛，而不是 Wolverine 重投后不断创建替代
  target。下载结果使用强类型状态、30 秒边界且禁止重定向；失败日志只记录结构化标识和失败码，不记录 archive
  内容、URL 查询参数或敏感信息。
- `25bb9492` 让正常停止、强制停止和恢复流程优先使用经校验的 `ProviderReceiptJson` 精确删除 Container、Compose、
  OVA 及隔离网络；只有没有 receipt 时才按精确 `RuntimeId + Generation` 发现资源。Docker Compose 工作目录已消失时
  幂等视为已停止，避免因标签漂移遗留 `AwdpTarget`、checker、网络、端口或 Runner 容量。
- `e09b6a05` 增加 Runner provider 故障就绪状态：创建拒绝、超时或清理失败会在 120 秒保持窗口内使 readiness 返回失败，
  同时将 Redis Runner 可用性标为离线但保留容量事实；后续成功操作会清除故障。Docker 地址池耗尽等资源故障因此
  不再被 `/health/ready` 误报为健康。
- `6e7425dc` 修复 Integration fixture 中无效的空 `jsonb` 种子，并明确 practice 与 player Runtime 属于不同 purpose、
  各自从 generation 1 开始。该提交只修测试数据和断言，没有放宽生产 JSON 校验。
- `6cff06a0` 将平台版本从 `0.1.0-alpha.54` 递增到 `0.1.0-alpha.55`。本阶段没有新增业务表、字段或 EF migration，
  没有改变 HTTP/OpenAPI 契约；两份 OpenAPI 与生成 TypeScript SDK 已连续复核且无漂移。未推送、未部署，也未修改
  生产或测试业务数据。用户已有的 `TODO.md` 修改、临时部署包和未跟踪目录均原样保留且未纳入提交。
- 验证结果：
  - `dotnet build backend/NoCTF.slnx -c Release --no-restore -m:1`：通过，0 warning / 0 error。
  - 非 Integration TUnit：829/829 通过。
  - 完整 Integration：183 total，181 通过、0 失败、2 个环境型跳过，耗时 5 分 35.359 秒；跳过项仅为本机未配置
    的真实 Kubernetes 集群与 Libvirt 磁盘。真实 PostgreSQL、Redis、Wolverine 与 Docker 用例全部通过。
  - AWDP Full E2E：1/1 通过，耗时 3 分 42.887 秒；API、Redis、PostgreSQL 重启韧性检查通过；测试前后平台管理的
    Docker 容器与网络均为 0，测试资源已精确清理。
  - 前端 `bun test`：221/221 通过（1622 assertions）；`bun run typecheck` 与 production `bun run build` 通过。
    构建仅保留既有大 chunk、plugin timing 与第三方 Node exports deprecation warning。
  - `dotnet format backend/NoCTF.slnx analyzers --verify-no-changes --no-restore` 通过；EF
    `migrations has-pending-model-changes --configuration Release --no-build` 返回无模型漂移；`git diff --check` 通过。
  - OpenAPI 工具导出和 TypeScript SDK 连续生成两次均无差异；两份 OpenAPI SHA-256 均为
    `4151471B13D6CAFFECBA10F508ECA3E6E53A8D583EDE15FF37F6BC667641D271`。
  - 两份 Docker Compose 配置通过解析；Kubernetes `kubeconform -strict -ignore-missing-schemas` 检查 49 个资源：
    46 valid、0 invalid、0 error、3 个 Cilium CRD 因无内置 schema 按 CI 规则 skipped。

## 2026-08-19 alpha.54 AWDP Runtime 队伍归属与管理端展示

- `f84a3386` 修正 AWDP 攻击 Runtime 的用途与队伍归属链路：参赛者或管理员启动的长期 Break 环境现在以
  `RuntimePurpose.AwdpAttack` 持久化，并始终携带已通过审核的真实 `TeamId`；启动、停止、重置、代际
  Flag 创建与失效均按攻击 Runtime purpose 收敛，不再与一次性 `AwdpTarget` 混用。
- 管理端 Runtime 响应新增强类型 `Purpose`、`SourceTeamId` 与 `SourceTeamName`。直接绑定队伍的攻击环境
  显示真实队伍名；一次性 Fix Target 显示“Fix 验证 Target”，并优先通过关联 `GameplayFact` / `PatchUpload`
  投影来源队伍。只有没有队伍归属的普通共享 Runtime（当前为 KoH）仍显示“共享”。Fix Target 行不再提供
  启动、重置或续期操作，避免这些队伍级操作误作用到长期攻击环境；精确终止和强制清理仍保留。
- OpenAPI 两份制品与 TypeScript SDK 已由工具重新生成并连续生成两次验证幂等；没有手写 URL、DTO、枚举
  或失败码。`a2b91eb2` 将版本从 `0.1.0-alpha.53` 递增到 `0.1.0-alpha.54`。
- 本阶段没有新增业务表、字段或 EF migration；没有推送、部署或修改生产/测试业务数据。用户已有
  `TODO.md` 修改、临时部署包和其他未跟踪目录均未纳入提交。
- 验证结果：
  - `dotnet build backend/NoCTF.slnx -c Release --no-restore`：通过，0 warning / 0 error。
  - 非 Integration TUnit：818/818 通过。
  - AWDP Runtime 真实 PostgreSQL 定向测试：4/4 通过，覆盖选手启动与管理员启动的 TeamId、Purpose、
    管理端真实队名投影及并发启动边界。
  - 前端 `bun test`：221/221 通过；`bun run typecheck` 与 production `bun run build` 通过。构建仅保留
    既有大 chunk、plugin timing 与第三方 Node exports deprecation warning。
  - `dotnet format ... analyzers --verify-no-changes` 通过；EF
    `migrations has-pending-model-changes --configuration Release --no-build` 返回无模型漂移；
    `git diff --check` 通过。

## 2026-08-19 alpha.53 AWDP 一次性防御验证模型

- `e49ef2d4` 将 AWDP 的两类 Runtime 明确分离：选手端“启动环境”只创建长期存在、可重置和续期的
  `Player / AwdpAttack` 攻击靶机；Fix 改为先申请全新的 `AwdpTarget` 一次性防御验证环境，待其进入
  `AwaitingPatch` 后才允许上传一个 patch。上传成功即原子绑定该 target、`PatchUpload` 与
  `FixAttempt`，随后锁定 target、应用一次 patch、运行一次 Checker，并在成功、失败、超时及重投路径中
  幂等清理 target、checker、网络、端口与 Runner 容量。
- 参赛者 API 新增强类型 `POST
  /api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/awdp-defense-targets`；Fix 上传改为
  `POST
  /api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/awdp-defense-targets/{runtimeInstanceId}/fix`。
  原先“直接提交 Fix 后再隐式创建 target”的入口已删除。AWDP 状态响应现在同时返回攻击 Runtime 与防御
  target 的 Runtime、stage、Fix fact、patch、结果及回收时间；OpenAPI 与 TypeScript SDK 均由工具重新生成。
- 前端 Fix 区域按“未申请 → 创建中 → 可上传 → 验证中 → 已完成且已回收 / 失败可重新申请”展示；请求和上传
  期间均防止重复操作，失败保留文件选择。管理端及中英文文案统一使用“攻击靶机 / Break 环境”“一次性防御
  验证环境”“Fix 一次性验证 Checker”，不再把 AWDP Checker 描述成 AWD 的周期性健康检查。
- EF migration `20260818170133_BindAwdpFixTarget` 由 `dotnet ef` 生成：在现有 `patch_uploads` 增加可空的
  `runtime_instance_id`，以唯一过滤索引保证一个 target 最多绑定一个 patch，并以 Restrict 外键和 Runtime
  check constraints 保证未绑定、已锁定和完成阶段合法；Runtime generation 唯一键加入 purpose，使同题同队的
  攻击 Runtime 与一次性 target 能按用途区分且不会发生唯一键冲突。本阶段没有新增业务表，仍遵守现有 16 张
  业务表基线。
- Runner/Worker 收敛补强：Fix archive 使用有界下载并保持路径、大小和压缩包安全校验；receipt 已创建但消息重投
  时优先按 Runtime identity 清理；Docker/Kubernetes reconciliation 覆盖 target 与 checker 资源；内部
  checker callback 继续使用隔离网络和认证回调，不套用 AWD 周期调度。
- `18e70549` 将平台版本从 `0.1.0-alpha.52` 递增为 `0.1.0-alpha.53`。本阶段只创建本地提交，未推送、
  未部署，也未修改生产或测试业务数据；用户现有 `TODO.md` 修改与未跟踪临时文件均未纳入提交。
- 验证结果：
  - `dotnet build backend/NoCTF.slnx -c Release --no-restore`：通过，0 warning / 0 error；版本递增后复跑仍通过。
  - 非 Integration TUnit：818/818 通过；AWDP 定向后端测试 70/70 通过；真实 Docker resource reconciler
    定向测试 1/1 通过。
  - AWDP Full E2E：1/1 通过，耗时 3 分 46 秒，覆盖攻击 Runtime、独立 target、恶意/合法 Fix、一次性
    Checker、结果和资源回收；随后 API 重启、Redis 中断恢复、PostgreSQL 重启韧性检查全部通过，E2E 创建的
    容器、镜像和网络均已精确清理。
  - 前端 `bun test`：217/217 通过；`bun run typecheck` 与 production `bun run build` 通过。构建仅保留
    既有大 chunk、plugin timing 与第三方 Node exports deprecation warning。
  - `dotnet format ... analyzers --verify-no-changes` 通过；EF 10.0.9
    `migrations has-pending-model-changes` 返回无模型漂移；`git diff --check` 通过。
  - OpenAPI 与 SDK 连续导出/生成两次哈希一致；两份 OpenAPI SHA-256 均为
    `A2BE1805E4E37954ACE995BB2408EFBEC758A4774C51146FECCA7E92F81105AF`。
  - 另一次不筛选的完整后端执行共 999 项：956 通过、41 失败、2 个环境型跳过。41 个失败均落在既有集成
    fixture 将基线 `Competition.ConfigurationJson` / `Challenge.DefinitionJson` 的 `string.Empty` 写入 PostgreSQL
    `jsonb` 所触发的 `22P02 invalid input syntax for type json`；本阶段 AWDP 定向 PostgreSQL/Redis/Wolverine、
    Docker 和 Full E2E 全部通过。该基线 fixture 问题未在 AWDP 功能提交中顺手修改，需后续独立修复。

## 2026-08-18 alpha.52 竞赛概览倒计时实时刷新

- `f9d0cefd` 修复选手端竞赛概览页的大号倒计时仅在刷新页面或等待 30 秒后才更新的问题。页面存活期间现在每秒刷新当前时间，因此“距开始 / 距结束”会连续更新，并在越过开始或结束时刻后立即切换对应文案；组件卸载时继续清理定时器。
- 新增 `competition-countdown.test.ts`，锁定概览倒计时的一秒刷新频率和卸载清理，避免与已每秒更新的页头倒计时再次出现行为分叉。
- `1762624f` 将平台版本从 `0.1.0-alpha.51` 递增为 `0.1.0-alpha.52`。
- 本阶段没有新增业务表、字段、EF migration、OpenAPI 契约或 TypeScript SDK 变更；未推送、未部署，也未修改生产/测试业务数据。
- 验证结果：
  - `bun test tests/competition-countdown.test.ts`：1/1 通过。
  - `bun test`：215/215 通过（1602 assertions）。
  - `bun run typecheck`：通过。
  - `bun run build`：通过；仅保留既有大 chunk、plugin timing 与第三方 Node exports deprecation warning。
  - `dotnet build backend/NoCTF.slnx -c Release --no-restore`：通过，0 warning / 0 error。
  - `git diff --check`：通过。

## 2026-08-18 alpha.51 推送与生产测试环境部署

- 已将 `main` 从 `d1a9bf54` 快进推送到 `cbab9f4d`。生产测试环境
  `https://101.43.46.244/` 已同步到 `cbab9f4d` 并完成部署。
- 本次发布内容：
  - `e328dd5f` 移除 AWDP 攻击 Runtime 旧版专属 `flagInjection` gate；Runner 不再要求题库/比赛配置里存在旧注入对象，而是校验 Worker 已按当前 Runtime generation 生成 `ChallengeFlag` 并注入到 Claim 环境变量。
  - `797a825a` 记录 AWDP 动态 Flag 注入模型收口。
  - `cbab9f4d` 将平台版本从 `0.1.0-alpha.50` 递增为 `0.1.0-alpha.51`。
- 远程部署过程：
  - 远端 `/root/NoCTF` 保留 `.env` 与未跟踪的 `deploy/docker-compose.prod.yml`，执行
    `git fetch origin main && git reset --hard origin/main`，最终 HEAD 为 `cbab9f4d`。
  - 使用 `deploy/docker-compose.yml` + `deploy/docker-compose.prod.yml` 和 `.env` 在远端构建
    `migration`、`backend`、`worker`、`runner` 镜像；构建成功。前端静态资源生成阶段仅出现既有大 chunk、plugin timing 与第三方 Node exports deprecation warning。
  - `docker compose ... run --rm migration` 返回 “No migrations were applied. The database is already up to date.”。
  - 已重建并启动 `backend`、`worker`、`runner`，PostgreSQL 与 Redis 继续复用现有健康实例。
  - 新镜像已打标签：`deploy-backend:alpha51-cbab9f4d`、`deploy-worker:alpha51-cbab9f4d`、
    `deploy-runner:alpha51-cbab9f4d`、`deploy-migration:alpha51-cbab9f4d`。
- 部署后验证：
  - `deploy-backend-1`、`deploy-worker-1`、`deploy-runner-1` 均为 `Up / healthy`。
  - 外部 HTTPS 检查：`https://101.43.46.244/` 返回 HTTP 200；`https://101.43.46.244/health` 返回 HTTP 200。
  - 启动后三分钟 backend、worker、runner 日志未发现新的 `fail` / `error` / `exception` / `critical` 关键字输出。
- 本阶段没有新增业务表、字段、EF migration、OpenAPI 契约或 TypeScript SDK 变更；未修改生产业务数据。
- 本地验证结果：
  - `dotnet build backend/NoCTF.slnx -c Release --no-restore`：通过，0 warning / 0 error。
  - `dotnet test backend/tests/NoCTF.Tests/NoCTF.Tests.csproj -c Release --no-build`：989 total，0 failed，813 succeeded，176 skipped。本机 Docker provider 不可用，因此 Docker/Kubernetes/Libvirt 专用集成用例由测试框架跳过；远端本次部署未执行破坏性 E2E。
  - `bun test`：214/214 通过。
  - `bun run typecheck`：通过。
  - `bun run build`：通过；仅保留既有大 chunk、plugin timing 与第三方 Node exports deprecation warning。
  - `git diff --check`：通过。

## 2026-08-18 alpha.50 后续未发布 AWDP 攻击 Runtime 动态 Flag 模型收口

- `e328dd5f` 移除 AWDP 旧版专属 `flagInjection` 配置模型与管理端组件。AWDP schema v4 不再读取、不升级也不兼容旧 `flagInjection` 字段；旧字段会按未知 JSON 字段在后端保存/校验边界被拒绝，符合当前 Alpha 阶段“不保留旧模型兼容”的决策。
- Runner 的 `AwdpAttackProvisioningPlanReader` 不再解析 `AwdpChallengeConfiguration.FlagInjection`，也不再自行注入环境变量或写文件。新流程为：Worker 在 PostgreSQL 临界区创建/读取当前 Runtime generation 的 `ChallengeFlag`，用题目 Runtime 定义中的 `FlagEnvironmentVariableName` 写入 Claim 环境变量；Runner 只防御性校验该 Claim 已携带当前 generation 的正确动态 Flag，然后启动容器。
- `RuntimeHandlers` 删除 AWDP 攻击 Runtime 的旧文件注入路径，避免再次出现“Worker 已注入但 Runner 仍要求旧配置”的失败状态。
- 前端 `game-config.ts` 删除 `AwdpFlagInjectionKind` / `awdpFlagInjection` 模型；AWDP 题库 UI 仅保留 Runtime 的 `FlagSource=PerTeam` 与环境变量名，比赛题目规则继续承载本场 Flag 模板覆盖。
- `docs/processes-messaging.md` 已同步新职责边界：Worker 生成并注入动态 Flag，Runner 只校验和启动；不存在 AWDP 专属注入对象。
- 本阶段没有新增业务表、字段、EF migration、OpenAPI 契约或 TypeScript SDK 变更；未推送、未部署，也未修改生产/测试数据。
- 验证结果：
  - `dotnet build backend/NoCTF.slnx -c Release --no-restore`：通过，0 warning / 0 error。
  - `dotnet test backend/tests/NoCTF.Tests/NoCTF.Tests.csproj -c Release --no-build`：989 total，0 failed，813 succeeded，176 skipped。本机 Docker provider 不可用，因此新增 AWDP 攻击 Runtime PostgreSQL/Docker 集成用例随测试框架跳过，需在远端或 CI 有 Docker 的环境补跑。
  - `bun test`：214/214 通过。
  - `bun run typecheck`：通过。
  - `bun run build`：通过；仅保留既有大 chunk、plugin timing 与第三方 Node exports deprecation warning。
  - `git diff --check`：通过；Git 仅提示 Windows 工作区 LF/CRLF 行尾替换警告。

## 2026-08-18 alpha.50 当前模式配置默认值与旧版修复入口

- `7f54f1dc` 删除 Domain 与 Challenge API 中会静默写入 `schemaVersion: 1` 的危险默认值。题目模板创建或更新未提供定义时，由 Application 根据所选游戏模式写入当前默认定义；AWDP 当前写入 schema v4，调用方显式提交 v1 时仍按不支持版本拒绝，不做隐式迁移。
- 新建比赛继续由 `GameModeDefaultConfiguration` 写入各模式当前配置版本；新增真实 PostgreSQL 覆盖用例，逐一锁定 CTF、AWD、AWDP、KoH 的持久化 `schemaVersion`。Domain 实体本身不再伪造任何旧版 JSON。
- 管理端新增两类显式修复入口：比赛配置可重置为当前模式默认配置，题目模板可重置为当前模式默认定义。重置只修改当前表单，必须再次点击保存才会写入，历史配置不会被静默覆盖。
- StartGate 中包含不支持 `schemaVersion` 的 `CompetitionConfigurationInvalid`、`RuntimeDefinitionInvalid`、`ChallengeRulesInvalid` 已按强类型 code 显示中文/英文可操作提示，并保留关联题目的“查看题目”入口。
- OpenAPI 与 TypeScript SDK 已由工具重新生成：创建/更新题目模板的 `definitionJson` 变为可选；缺省语义由 Application 负责。二次导出和生成哈希一致，未手工修改生成文件。
- `cc54f615` 将平台版本从 `0.1.0-alpha.49` 递增为 `0.1.0-alpha.50`。
- 本阶段没有新增业务表、字段或 EF migration；未查询或修改业务数据，因请求未提供待检查的具体比赛 ID。
- 已将远程 `main` 从 `35186e2b` 快进到 `4befa3b9`，并部署生产测试环境 `https://101.43.46.244/`：
  - 远端 `/root/NoCTF` 保留 `.env` 与未跟踪的 `deploy/docker-compose.prod.yml`，同步到 `4befa3b9`。
  - 后端、Worker、Runner 与 migration 镜像均在远端由同一源码成功构建；留存标签为 `alpha50-4befa3b9`。
  - migration 返回数据库已是最新状态，没有应用新迁移；随后重建 backend、worker、runner。
  - 新容器镜像 ID：backend `sha256:e7915ac6b1fff53c8ebe66367c6fe5a7f17b4b9bbcfea56383694d003f752f7e`、worker `sha256:f02b67ccb9508a48f0ecaa00bad8b8abd871bef6fbac027820a0517c933ae8f0`、runner `sha256:de41f9b8dc85bd06d1142e4dad7bd9327b81bcfefd642e92b9c3b455115f53da`。
  - 三个应用容器均为 `running/healthy`；外部 `/health`、`/health/ready` 与首页均返回 HTTP 200；启动后五分钟日志未发现新的 `fail`、`error`、`exception` 或 `critical` 输出。
- 验证结果：
  - `dotnet build backend/NoCTF.slnx -c Release --no-restore`：通过，0 warning / 0 error（版本递增后复跑）。
  - 非 Integration TUnit：810/810 通过。
  - 题目默认定义、协议、OpenAPI 与 AWDP StartGate 定向测试：全部通过。
  - 新增 PostgreSQL 各模式默认版本用例已执行，但本机 Docker provider 不可用，结果为 1 skipped、0 executed；不得视为通过，需在有 Docker 的 CI/测试环境补跑。
  - `bun test`：213/213 通过；`bun run typecheck`、`bun run build` 通过。前端构建仅保留既有大 chunk、plugin timing 与第三方 Node exports deprecation warning。
  - `dotnet ef migrations has-pending-model-changes`：无模型漂移。
  - OpenAPI/SDK 二次生成幂等、改动 C# whitespace 校验与 `git diff --check`：通过。

## 2026-08-18 alpha.49 管理端竞赛题目删除按钮修复

- `ef04dd5e` 修复管理后台“竞赛管理 / 题目”列表中的删除题目按钮无效问题。
  删除确认框不再使用会自动关闭并清空目标状态的 `AlertDialogAction`，改为受控 destructive `Button`：
  删除请求期间显示 loading 并禁止重复提交；成功后关闭确认框并刷新列表；失败时保留确认框、待删除题目和当前输入状态，并在确认框内与 toast 中显示可读错误。
- 新增 `competition-challenge-delete.test.ts`，锁定删除流程继续通过生成 SDK
  `adminDeleteCompetitionChallenge` 调用，并携带当前题目的 `expectedRevision`。
- `92bfb072` 将平台版本从 `0.1.0-alpha.48` 递增为 `0.1.0-alpha.49`。
- 本阶段没有新增业务表、EF migration、OpenAPI 契约或 TypeScript SDK 变更；未推送、未部署。
- 验证通过：
  - `bun test tests/competition-challenge-delete.test.ts`：2/2 通过。
  - `bun test`：210/210 通过。
  - `bun run typecheck`：通过。
  - `bun run build`：通过；仅保留既有大 chunk、plugin timing 与第三方 Node exports deprecation warning。
  - `git diff --check`：通过。

## 2026-08-18 alpha.48 分值曲线提示与中文错误提示修复

- `4207856d` 修复管理端分值衰减曲线预览的鼠标提示行为：浮层现在跟随鼠标指针，并与指针保持固定偏移；曲线上高亮点仍按横轴换算为整数“解题队伍数”，显示该解题数对应的取整分值。
- 同一提交修复未开赛比赛的选手端“动态”页：普通选手查询范围在比赛开始时间晚于当前时间时会夹到当前时刻的空范围，避免向后端发送 `from > to` 导致红色错误。
- 同一提交新增 API problem detail 本地化兜底：已知后端英文 `detail/title/field error` 在中文界面会转换为中文提示，覆盖事件范围、作弊查询范围、平台日志范围、上传图片类型、运行环境操作、SMTP/平台配置等当前常见红色错误。
- `c4399487` 将平台版本从 `0.1.0-alpha.47` 递增为 `0.1.0-alpha.48`。
- 本阶段没有新增业务表、EF migration、OpenAPI 契约或 TypeScript SDK 变更；未推送、未部署。
- 验证通过：
  - `bun test tests/challenge-definition-defaults.test.ts tests/api-error-localization.test.ts tests/competition-event-history.test.ts tests/i18n.test.ts`：26/26 通过。
  - `bun test`：208/208 通过。
  - `bun run typecheck`：通过。
  - `bun run build`：通过；仅保留既有大 chunk、plugin timing 与第三方 Node exports deprecation warning。
  - `git diff --check`：通过。

## 2026-08-18 alpha.47 推送与生产测试环境部署

- 已将 `main` 从 `0a3cd3f9` 快进推送到 `45d1e878`。生产测试环境
  `https://101.43.46.244/` 已同步到该提交并完成部署。
- 部署过程：
  - 远端 `/root/NoCTF` 保留未跟踪的 `deploy/docker-compose.prod.yml` 与 `.env`，执行
    `git fetch origin main && git reset --hard origin/main`。
  - 使用 `deploy/docker-compose.yml` + `deploy/docker-compose.prod.yml` 和 `.env` 构建
    `migration`、`backend`、`worker`、`runner`。远端 `backend`、`worker`、`migration` 构建成功；远端
    runner 构建卡在 GitHub `kompose` 下载步骤，已改为在本机用同一源码构建 `deploy-runner:latest`，
    上传 tar 后在远端 `docker load`。
  - 新镜像已打标签：`deploy-backend:alpha47-45d1e878`、`deploy-worker:alpha47-45d1e878`、
    `deploy-runner:alpha47-45d1e878`。
  - `docker compose ... run --rm migration` 返回 “No migrations were applied. The database is already up to date.”。
  - 已重建 `backend`、`worker`、`runner`，并删除远端 `/root/deploy-runner-45d1e878.tar` 与本地临时 runner tar。
- 部署后验证：
  - `deploy-backend-1`、`deploy-worker-1`、`deploy-runner-1` 均为 `healthy`。
  - 运行中的镜像 ID：
    - backend `sha256:f0582ed0ea37982dc7f61bda0c92ba8cc507bc9a60e085f5822c87f469607cf6`
    - worker `sha256:d79b84a5ec3d757516760fdfef2d9020e30158493f07174f71b99a8b7b908d62`
    - runner `sha256:02a86867620b5d268134e42805d11901cad34a794ba02f13e514d8847adf68b5`
  - 外部 HTTPS 检查：`https://101.43.46.244/health` 返回 `{"status":"Ok"}`；
    `https://101.43.46.244/health/ready` 返回 `Healthy`；首页 HTML 正常返回。
  - 启动日志尾部未发现新的 `fail` / `error` / `exception` / `critical` 关键字输出。

## 2026-08-18 alpha.47 AWDP 示例题镜像构建修复

- `dcdc7870` 修正 `docs/challenge-authoring-templates/examples/awdp-pwn-index-vault/checker/.dockerignore`，
  将 `exploit.py` 放入 checker 镜像构建上下文。此前 checker Dockerfile 已引用该文件，但白名单式
  `.dockerignore` 仅放行 `Dockerfile` 与 `checker.py`，导致本地构建失败并可能误用旧本地镜像。
- 已重新构建并推送示例题测试镜像：
  - `crpi-263xwliy5b2vru33.cn-chengdu.personal.cr.aliyuncs.com/noctf_challenge/noctf-awdp-index-vault-target:test`
    → `sha256:7ab6dbc757b333bdc127cb5c8eaeadd4575936f257623e0c499bc9c2be2c661a`
  - `crpi-263xwliy5b2vru33.cn-chengdu.personal.cr.aliyuncs.com/noctf_challenge/noctf-awdp-index-vault-checker:test`
    → `sha256:0401a01fe2522ca38fdad682e48e314e6d3f0d8bfa1616ded22b246dd7cb764d`
- 验证通过：
  - `docker build -t noctf-awdp-index-vault-target:local docs/challenge-authoring-templates/examples/awdp-pwn-index-vault/target`
  - `docker build -t noctf-awdp-index-vault-checker:local docs/challenge-authoring-templates/examples/awdp-pwn-index-vault/checker`
  - `bash docs/challenge-authoring-templates/examples/awdp-pwn-index-vault/scripts/build-fix-packages.sh`
  - `bash docs/challenge-authoring-templates/examples/awdp-pwn-index-vault/tests/invalid-archives.sh`
  - `bash docs/challenge-authoring-templates/examples/awdp-pwn-index-vault/tests/smoke.sh`
- 本小节仅涉及示例题构建上下文和测试镜像；没有新增业务表、迁移、OpenAPI/SDK 或平台运行时代码变更。

## 2026-08-18 alpha.47 AWDP Fix 三态验证语义修正

- `b2678ded` 将 AWDP Fix 验证统一为产品三态：`ExploitSucceeded`、`DefenseSucceeded`、
  `ServiceAbnormal`。内部回调协议边界临时兼容旧值：`Fixed -> DefenseSucceeded`、
  `StillVulnerable -> ExploitSucceeded`、`RuleViolation/ServiceUnavailable -> ServiceAbnormal`；
  新导出的 OpenAPI 与 TypeScript SDK 只公开新三态。新增 OpenAPI document processor 是为了在使用自定义
  JSON converter 的同时保持协议 schema 为严格字符串枚举，未手写 SDK。
- Domain/Application 映射同步调整：`DefenseSucceeded` 记录为 Correct；`ExploitSucceeded` 记录为 Wrong +
  `AwdpExploitSucceeded`；`ServiceAbnormal` 记录为 Wrong + `AwdpServiceAbnormal`；Runner / Provider /
  存储 / 消息派发等平台问题继续使用 `PlatformFailed`，不进入选手计分。Runner 中 checker 整体验证超时现在
  判为 `ServiceAbnormal`；checker 主进程异常且没有可信业务结果判为 `PlatformFailed`；exit 0 且无回调不再
  猜测为防御成功。
- AWDP 配置升级为 schema v4，保留三类可选单次罚分并默认 0：`FlagWrongPenalty`、
  `ExploitSucceededPenalty`、`ServiceAbnormalPenalty`。删除旧语义 `FixFailurePenalty`、
  `ViolationPenalty`、`ServiceDownPenalty`；Patch 解包失败、Patch 命令异常、Patch 超时、平台错误均不会套用
  上述玩家罚分。动态排行榜投影按轮累计 Break/Fix 正确事实分数，同时只对唯一 GameplayFact 应用一次对应罚分；
  外队 Flag 被接入为 Rejected 时也归入 Flag 错误罚分。
- ClientApp 管理配置、选手 AWDP 面板、Flag 提交提示、选手/管理员提交列表均改为新三态和新失败原因文案；
  英文资源已补齐。AWDP starter kit 与 `awdp-pwn-index-vault` 示例题改为真实编排 checker：先运行 EXP 子进程，
  再执行正常服务交互，最终只回调一次三态结果；示例 Fix 包目录同步改为
  `defense-succeeded`、`exploit-succeeded`、`service-abnormal-bypass`、`service-abnormal-down`。
- `c37b174d` 将平台版本由 `0.1.0-alpha.46` 递增为 `0.1.0-alpha.47`。本阶段没有新增业务表、列、EF
  migration 或 snapshot；`dotnet ef migrations has-pending-model-changes` 返回无模型变化。
- 验证通过：
  - OpenAPI export + `bun run api:gen` 连续两轮幂等；OpenAPI 与 SDK 文件 SHA-256 前后完全一致，AWDP
    checker 回调 schema 仅包含 `ExploitSucceeded`、`DefenseSucceeded`、`ServiceAbnormal`。
  - `dotnet build backend/NoCTF.slnx --configuration Release --no-restore`：0 warning / 0 error。
  - `dotnet test backend/tests/NoCTF.Tests/NoCTF.Tests.csproj --configuration Release --no-build`：
    981 总计，979 成功、0 失败、2 个环境型跳过（Kubernetes 与 Libvirt 专用环境未配置）。
  - AWDP Full E2E：`$env:NuGetAudit='false'; dotnet run --file backend/tests/e2e.cs -- --mode awdp --suite full`
    1/1 通过，包含 API/Redis/PostgreSQL resilience 检查与 Docker 资源精确清理。首次 `dotnet run` 未带
    `NuGetAudit=false` 时因 NuGet audit 无法访问 `https://api.nuget.org/v3/index.json` 在恢复阶段失败，
    不是业务测试失败。
  - ClientApp `bun test` 205/205（1556 assertions）、`bun run typecheck`、production `bun run build`
    均通过；构建仅保留既有大 chunk、plugin timing 与第三方 Node exports deprecation warning。
  - EF model drift 与 `git diff --check` 均通过；OpenAPI 导出期间本地 Redis 未启动产生 FusionCache
    backplane 连接警告，但导出进程成功完成且生成物幂等。
- 本阶段代码随后已按上方部署小节推送并部署；工作区中既有未跟踪的临时归档、Runner Properties、local ports、
  `frontend/` 与 `scripts/` 均未纳入提交。

## 2026-08-18 alpha.46 CTF/AWDP 可配置动态分值与按轮结算

- `8be9031b` 以不兼容升级方式统一了 CTF/AWDP 分值曲线。两种模式均支持 `Fixed`、`Linear`、
  `Quadratic`、`Exponential`、`Logarithmic` 与 `Custom` 六种衰减模式；曲线使用初始分、最低分、
  衰减队伍数和可选自定义公式描述，最终分值统一钳制后按 AwayFromZero 取整。CTF 配置升级为 schema v2，
  AWDP 配置升级为 schema v3，不保留旧 schema 兼容分支。
- AWDP Break 与 Fix 使用两条完全独立的动态曲线，分别按本轮取得对应 Correct 事实的不同队伍数量结算。
  每轮在事实发生时固化该轮所得分，后续轮次的队伍数量和曲线分值不会追溯改变此前轮次；队伍封禁、解禁和
  rejudge 仍通过全量权威投影重盘。AWDP 不再使用 `CompetitionChallenge.BaseScore` 参与计分，管理端也不再
  暴露该字段；排行榜/题目卡分别返回和显示当前 Break/Fix 分值。
- 删除了旧的一次性 AWDP 计分影响预览端点及其 Application/Infrastructure 实现；OpenAPI 路由基线同步由
  202 调整为 201，管理端路由基线由 132 调整为 131。两份 OpenAPI 制品由工具导出，SHA-256 均为
  `0BED6B5A6462E47B6B66AB4727CA21EE33080051461E1F6F5E3D7A4F2F62104A`；TypeScript SDK 由工具重新
  生成，连续两次 OpenAPI export 与 SDK generation 的全部文件哈希完全一致。
- 管理端曲线预览扩大为带坐标轴的详细图：Y 轴 9 个取整分值刻度、X 轴最多 13 个计分队伍数刻度，辅以虚线
  网格；鼠标移动会吸附到最近的整数队伍数并显示取整后的具体分值，键盘左右键、Home、End 也可逐点查看。
  自定义公式仍由后端权威解析和逐点校验，前端不会用不一致的 JavaScript 表达式模拟结果。
- `f3e66613` 将平台版本由 `0.1.0-alpha.45` 递增为 `0.1.0-alpha.46`。本阶段没有新增业务表、列、
  migration 或 snapshot；`dotnet ef migrations has-pending-model-changes` 返回无模型变化。
- 验证通过：
  - `dotnet build backend/NoCTF.slnx --configuration Release --no-restore`：0 warning / 0 error。
  - 非 Integration TUnit：794/794；`ScoreCurveEvaluatorTests` 8/8；AWDP evaluator 9/9。
  - 真实 PostgreSQL、Redis、Wolverine、Docker Integration：174/176 通过、0 失败；Kubernetes 与
    Libvirt 两项因专用环境未配置按设计跳过。
  - CTF Full E2E 1/1、AWDP Full E2E 1/1，均通过 API/Redis/PostgreSQL resilience 检查并精确清理
    本轮 Docker 资源；AWDP E2E 验证 Break/Fix 独立按轮结算、空轮不增长及暂停/恢复行为。
  - ClientApp `bun test` 205/205（1556 assertions）、`bun run typecheck`、production `bun run build`；
    构建仅保留既有 chunk/plugin timing 与第三方 Node exports deprecation warning。
  - OpenAPI/SDK 双次生成幂等、EF model drift、`git diff --check` 均通过。
- 本阶段尚未推送、部署或操作生产数据；工作区中既有未跟踪的临时归档、Runner Properties、local ports、
  `frontend/` 与 `scripts/` 均未纳入提交。

## 2026-08-17 alpha.45 题目列表布局、3D 视角与附件哈希标识

- `9373b679` 按最终确认将协作者 3D 大屏相机由约 50° 调整为 45°：初始相机高度调整为 101，巡航高度调整为
  `1.1 * citySpan + 42`；保留旋转、题目分值与解题数量、一二三血标识以及解题更新后的持续聚焦行为。
- `ab86b9d2` 调整选手题目页的双栏布局：题目列表使用更靠左的主内容列，赛事播报作为右侧窄栏稍向右展开；
  响应式窄屏布局保持单列。`918ebc3b` 在题库模板附件表新增“哈希”列，展示附件 SHA-256 前 8 位并通过
  `title` 保留完整哈希，便于区分统一下载名下的多个附件变体；没有向选手端暴露内部附件哈希或 Flag。
- `88b04a7a` 将平台版本由 `0.1.0-alpha.44` 递增为 `0.1.0-alpha.45`。本阶段没有修改 HTTP/OpenAPI/
  TypeScript SDK、数据模型、migration 或 snapshot，也没有修改题目 Runtime/Checker 镜像策略。
- 验证通过：附件定向测试 4/4；ClientApp 完整 `bun test` 203/203（1544 assertions）、
  `bun run typecheck`、production `bun run build`；后端 `dotnet build backend/NoCTF.slnx -c Release --no-restore`
  0 warning / 0 error；`git diff --check` 通过。构建仅保留既有的大 chunk、plugin timing 与第三方 Node exports
  deprecation warning。
- 功能与版本提交已快进推送到远程 `main@88b04a7a`，生产 `/root/NoCTF` 快进到同一提交；未跟踪的
  `deploy/docker-compose.prod.yml` 保持原样。Linux/amd64 三服务镜像归档 SHA-256 为
  `BEF59CCC6CFA524E2F51DC68B13F79B8A61B013486C4D6D9A03C96A28FEB5429`；导入后 API、Worker、
  Runner 镜像分别为 `sha256:ed280f519b77cbb000d703d62905f442f8e288dfe6495955cbf06cd8f60de595`、
  `sha256:c49c7b8725ce6d344c7352b378e4ee87d6c47768d728cdbdc8cd3ca980c02e0f`、
  `sha256:645dd61d30699b3a025351326faf6945545a54c5f90414eb59cd282d7d729e2a`；原 Alpha.44 镜像保留
  `rollback-a3f40386` 标签。
- 生产只以 `--no-deps --no-build --force-recreate` 重建 API、Worker、Runner；PostgreSQL、Redis、卷、
  生产数据及题目容器均未重建。三服务均为 running/healthy、restart count 0，程序集均确认
  `0.1.0-alpha.45`；公开首页返回 200，`/health` 返回 `{"status":"Ok"}`，部署资源确认包含“哈希”列，
  发布后 API/Worker/Runner 的 Fatal、Critical、Unhandled、Exception 和失败日志匹配均为 0。

## 2026-08-17 alpha.44 CTF 3D 大屏高位俯视修正

- `3707f499` 将协作者 3D 大屏的初始相机高度由 68 提升为 108，巡航高度由
  `0.82 * citySpan + 26` 提升为 `1.35 * citySpan + 40`；拉远半径、旋转、题目分值、解题数量、
  一二三血标记及解题后持续聚焦行为保持不变。默认题目矩阵下视角由约 35° 提升至接近 50°，可以从
  更高位置同时观察完整柱体矩阵与遥测标签。
- `1454f742` 将平台版本从 `0.1.0-alpha.43` 递增为 `0.1.0-alpha.44`。本阶段没有修改
  HTTP/OpenAPI/TypeScript SDK、数据模型、migration 或 snapshot，也没有修改题目 Runtime/Checker
  镜像策略。
- 验证通过：ClientApp 定向 `bun test tests/control-screen.test.ts` 4/4、完整 `bun test` 203/203
  （1539 assertions）、`bun run typecheck`、production `bun run build` 与 `git diff --check`。构建仅保留
  既有的大 chunk、plugin timing 与第三方 Node exports deprecation warning。
- 功能与版本提交已快进推送到远程 `main@1454f742`，生产 `/root/NoCTF` 快进到同一提交；未跟踪的
  `deploy/docker-compose.prod.yml` 保持原样。Linux/amd64 三服务镜像归档 SHA-256 为
  `F8C8E9A03DB1E9AB27C9AE10C4CF7B642CF115B428A9D9DC611CDBDCAAF9D68C`；导入后 API、Worker、
  Runner 镜像分别为 `sha256:56fb6f5c4d01c2a4def67bea6ec593286c956970b07a9b58ef1a2ab5b8dc01d5`、
  `sha256:dea6983060c26a4683f49cf47cb3b2114e9b8ec5a7d82fe5d2425d138438c418`、
  `sha256:ab0e2573697c1bd24e1cd25c896653cf2383bf3309db754635efd13dbfb2fbc1`；原 Alpha.43 镜像保留
  `rollback-9da6f2f3` 标签。
- 生产只以 `--no-deps --no-build --force-recreate` 重建 API、Worker、Runner；PostgreSQL、Redis、
  卷、生产数据及题目容器均未重建。三服务均为 running/healthy、restart count 0，程序集均确认
  `0.1.0-alpha.44`；公开 `https://101.43.46.244/` 与 `/health` 返回 200，健康正文为 `Ok`，发布后
  API/Worker/Runner 的 Fatal、Critical、Unhandled、Exception 和失败日志匹配均为 0。服务器根分区
  仍有约 13 GB 可用空间，远端本次精确传输归档已删除，没有执行全局 Docker prune。

## 2026-08-17 alpha.43 CTF 3D 大屏总览与实时聚焦

- 本阶段在协作者 3D 大屏实现上完成定向修正。`e04bf87e` 扩大巡航范围并调整题目标签，
  `aeacd8f6` 让每个题目标牌持续显示当前分值、解题数量及按一血、二血、三血排序的血榜标记；
  同一题目在聚焦期间出现新解题时会刷新展示并重新计算 5.2 秒聚焦窗口，其他题目的解题更新继续排队。
  `5ce279a5` 按最终产品确认将相机改为高位俯视：保留拉远半径，初始高度由 44 提升为 68，
  巡航高度使用 `0.82 * citySpan + 26`，旋转时可以同时观察题目矩阵、柱体与遥测标签。
- `b396981f` 将平台版本从 `0.1.0-alpha.42` 递增为 `0.1.0-alpha.43`。本阶段没有修改
  HTTP/OpenAPI/TypeScript SDK、数据模型、migration 或 snapshot，也没有修改题目 Runtime/Checker
  镜像策略。
- 验证通过：ClientApp 完整 `bun test` 203/203（1539 assertions）、`bun run typecheck`、production
  `bun run build` 与 `git diff --check`。构建仅保留既有的大 chunk、plugin timing 与第三方 Node exports
  deprecation warning。
- 功能、远程 `main` 合并与版本提交已推送至 `main@b396981f`。生产 `/root/NoCTF` 快进到同一提交，
  既有未跟踪 `deploy/docker-compose.prod.yml`、`.env`、HTTPS 证书、PostgreSQL/Redis/上传卷、题目数据和
  题目 Runtime 均保持原样。本地构建并传输的三服务 Linux/amd64 镜像归档 SHA-256 为
  `E012ABE731010B5D11C94D613C06335DDD862862A85B5A426C3972ED5BAAEDEA`；导入后 API、Worker、Runner
  镜像分别为 `sha256:43b544ae31c503e50cf156d3ecf9bf95d0c4c21a6378908c34d811dbd2f08d30`、
  `sha256:c50e0524388de8679d544aacfffb9af066cd45a22f7e685cb0ddb58068653cd1`、
  `sha256:172c40f3f6c543fae9be60a7d4b164031f5fc85ab5d0efac3bf303d6211badd8`；原 Alpha.42 三镜像保留
  `rollback-1a71f0bd` 标签。
- 生产只用 `--no-deps --no-build --force-recreate` 重建 API、Worker、Runner，没有执行 migration，
  没有重建 PostgreSQL、Redis、卷或题目容器。三服务均为 running/healthy、restart count 0，程序集均确认
  `0.1.0-alpha.43`；公开 `https://101.43.46.244/` 与 `/health` 返回 200，健康正文为 `Ok`，发布后
  API/Worker/Runner 的 Fatal、Critical、Unhandled、Exception 和失败日志匹配均为 0。服务器根分区仍有
  约 14 GB 可用空间，本地与远端本次精确传输归档均已删除，没有执行全局 Docker prune。

## 2026-08-17 alpha.42 AWDP 队伍攻击实例与一次性 Fix 语义收口

- 本阶段以 `origin/main@b96f4cc5` 为基线，在独立分支
  `codex/awdp-player-fix-20260817` / 工作树
  `E:\SourceCode\NoCTF-awdp-player-fix-20260817` 完成，没有覆盖主工作区
  `E:\SourceCode\NoCTF` 中的其他修改。提交依次为：`0eca4032`（AWDP 队伍攻击
  Runtime、generation Flag 注入与按模式判定）、`8a9be67a`（前端 AWDP 攻击/防御配置体验）、
  `d2c2f010`（AWDP Full E2E、Fix 归档边界测试与 index-vault 示例题更新）和
  `957d865b`（版本由 `0.1.0-alpha.41` 递增为 `0.1.0-alpha.42`）。
- AWDP 现在严格区分队伍攻击实例与一次性 Fix 验证实例：选手侧可为本队启动 Player/PerTeam
  攻击 Runtime，平台在每个 Runtime generation 自动生成并注入本队精确 Flag；Fix 验证仍使用
  TeamId 为空、无公开入口的一次性 `AwdpTarget`，不会把 Fix 建模为 Flag 操作，也不会复用 AWD
  的轮询服务语义。容器题目的动态 Flag 配置留在比赛题目管理处，题库模板只保留 Runtime/Checker
  与注入位置等技术定义，方便不同比赛使用不同 Flag 头。
- Runner 在 AWDP Player Runtime 领取时会要求存在当前 generation 的 exact Flag，并在容器创建前通过
  Runtime claim 注入环境变量；Runner 成功创建后激活该 generation 的 Flag，停止、重置或 generation
  变化时旧 Flag 失效。AWDP Break 只接受本队当前 generation 的 Flag；本队 Flag、其他队伍 Flag、过期
  Flag、重复 Flag 与错误 Flag 继续按强类型 GameplayFact 规则判定。
- 前端 AWDP 配置默认展示“攻击轨 · Break”和“防御轨 · Fix”：攻击 Runtime 使用本队独立实例、`FLAG`
  环境变量和 OwnerOnly 访问 URL；Fix 区域强调补丁包只执行一次 Checker。前端没有新增手写 API URL、
  DTO 或枚举；本阶段 API/OpenAPI/TypeScript SDK 无实质契约变化，`bun run api:gen` 后 `app/api`
  内容无漂移。
- AWDP Full E2E 已覆盖：开赛前不能启动环境、三支队伍 Player Runtime 分配、Break 成功/错误/重复/过期/
  外队 Flag、Fix 成功、StillVulnerable、RuleViolation、ServiceUnavailable、补丁非零退出、超时、
  缺少 `fix.sh`、空包、绝对路径、`../` 路径穿越、符号链接、硬链接、重复路径、非 gzip、超限包、
  pause 后逻辑轮冻结、resume 后继续计分、finish 后释放 Runtime。测试夹具同步更新为可区分
  `Fixed`、`StillVulnerable`、`RuleViolation` 和 `ServiceUnavailable`。
- `docs/challenge-authoring-templates/examples/awdp-pwn-index-vault/` 已同步到新模型：目标镜像不再烘焙真实
  Flag，服务从环境变量读取动态 Flag；Checker 不依赖固定 Flag 前缀；新增非法 Fix 包生成脚本和烟测，
  覆盖合法 Fix 与多类非法归档。starter-kit 和权威文档同步强调：AWDP 出题人配置攻击 Runtime、
  Checker 和 Fix 契约，不手工创建队伍 Flag 或把 Break/Fix 混为同一操作。
- 验证通过：
  - `dotnet build backend/NoCTF.slnx -c Release --no-restore`：0 warning / 0 error。
  - `powershell -ExecutionPolicy Bypass -File backend/scripts/Verify-Backend.ps1 -RequireDockerIntegration`：
    Release build 0 warning / 0 error；非 Integration TUnit 793/793；真实 PostgreSQL、Redis、Wolverine、
    Docker Integration 175/177 通过、0 失败，Kubernetes 与 Libvirt 两项因专用环境未配置按设计跳过；
    EF model drift 无变化；OpenAPI export 与 artifact drift 通过；`git diff --check` 通过。
  - `dotnet run --file backend/tests/e2e.cs -- --mode awdp --suite full`：AWDP Full E2E 1/1，通过后 resilience
    检查也通过；E2E Docker 容器、网络、卷和本地镜像已由编排器精确清理。
  - `bun test`：203/203；`bun run typecheck`：通过；`bun run build`：通过，仅保留既有大 chunk、
    plugin timing 与 Node exports deprecation 警告。
  - `bun run api:gen; git diff --exit-code -- app/api`：通过，无 SDK 内容漂移。
  - WSL 下 `tests/invalid-archives.sh` 与 `tests/smoke.sh`：均通过。
- 本阶段没有新增业务表、列、migration 或 snapshot，没有固定或改写题目 Runtime/Checker/Compose 镜像
  digest，没有新增 registry allowlist，没有推送、部署或操作生产数据。生产仍以既有远程 `main` 和当前
  部署为准，等待新的明确授权。

## 2026-08-17 alpha.41 AWDP 持续攻击、防御与按轮计分模型

- 本阶段以 `origin/main@40722584` 为基线，在独立分支
  `codex/awdp-product-model-20260817` / 工作树
  `E:\SourceCode\NoCTF-awdp-product-model-20260817` 完成。提交依次为：
  `761e65b0`（AWDP schema v2 配置与 v1 只读兼容）、`9e4e0c54`（攻击 Runtime、动态
  Flag、一次性 Fix 阶段、持续计分、维护刷新、参赛者状态和历史影响预览）、
  `c94c8854`（生成 SDK 与中英文攻击/防御前端）、`57220ace`（权威 AWDP 文档）和
  `550c85da`（版本由 `0.1.0-alpha.40` 递增为 `0.1.0-alpha.41`）。
- AWDP v2 明确分离攻击轨 `BreakAttempt` 与防御轨 `FixAttempt`。玩家攻击环境使用新的
  `RuntimePurpose.AwdpAttack`，按队伍、比赛题目和 Runtime generation 生成、注入并失效
  `SpecificationKind.RuntimeGeneration` 动态 Flag；环境变量与绝对文件两种注入均由题库技术定义配置，
  比赛专属 Flag 模板留在 Competition/CompetitionChallenge 配置。未复用 AWD 的
  `FlagAttempt`、`AwdRound`、目标列表或周期服务 Checker。
- Fix 无需先 Break。每次 Fix 使用 TeamId 为空、无公开入口的全新 `AwdpTarget`，阶段为
  TargetProvisioning、PatchApplying、CheckerRunning、Completed；Patch 与一次 Checker 的 durable
  revision/processing fence 保持幂等，exit 0 但没有认证 `Fixed` callback 不会被推导为 Correct。
- schema v2 的 Break/Fix 分别选取每队每题最早有效 Correct 作为激活点，从所属逻辑轮开始持续累计；
  当前轮立即计入，两轨可叠加，失败罚分只应用一次。Pause 使用 EffectiveRunningTime 冻结轮次，Resume
  接续，Finish 最终投影后冻结，Rejudge 会重新选择激活事实。singular maintenance 每 15 秒有界检查最多
  500 场 Running AWDP v2，只在逻辑轮前进或缓存缺失时置脏，不新增 schedule/积分状态业务表。
- 新增强类型 API：`GET /api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/awdp-state`
  返回本队攻击 Runtime、Break/Fix 激活轮与一次性 Fix 阶段；
  `GET /api/v1/admin/awdp/scoring-impact-preview` 只允许平台 Administrator 只读比较历史 v1 当前分数与
  假设 v2 分数。历史 GameplayFact、CompetitionEvent 与排行榜未自动修改，也没有提供应用纠正端点。
  OpenAPI 两份制品由工具导出且 SHA-256 均为
  `274407C7E946619445B972FBE3F26DF34937D39D4BE62E465CD92E097D5923BC`；TypeScript SDK 由工具生成，
  两次导出和两次生成均无漂移。
- EF migration `20260816181000_AddAwdpContinuousRuntime` 由 `dotnet ef` 生成，新增
  `runtime_instances.awdp_fix_stage`、AWDP Attack 活动实例唯一索引、Runtime generation Flag 唯一索引，
  并将外队 Flag 的 victim 约束扩展至 BreakAttempt。没有新增业务表；`dotnet ef migrations
  has-pending-model-changes`（dotnet-ef 10.0.9）确认无模型漂移。
- 最终验证：Release solution build 0 warning / 0 error；完整非 Integration TUnit 784/784；强制真实
  PostgreSQL、Redis、Wolverine、Docker Integration 175/177 通过、0 失败，Kubernetes 与 Libvirt 两项因
  专用环境未配置按设计跳过；CTF、AWD、AWDP、KoH Full E2E 均 1/1 并通过各自 resilience 检查，其中
  AWDP Full E2E 最终再次以保留环境模式 1/1 通过（2m17s），随后精确清理该 Compose project 的容器、
  网络、卷和本地镜像，残留均为 0。C# analyzer、EF drift、两份 Compose config、kubeconform
  46 valid / 3 Cilium schema skipped / 0 invalid、`git diff --check` 均通过。
- 前端完整 `bun test` 202/202、`bun run typecheck` 与 production `bun run build` 通过；仓库没有 lint
  script。构建仅保留既有大 chunk、插件耗时和依赖 exports deprecation 警告。Microsoft Edge 自动化在
  读取已有 Edge 窗口时因无法可靠确认当前 URL 而由 Computer Use 安全策略终止；没有回退到内置浏览器，
  因此本阶段的 Edge 人工交互、Console 与 Network 验收准确标记为阻塞，不能误报为通过。
- 本阶段未固定或改写题目 Runtime/Checker/Compose 镜像 digest，没有新增 registry allowlist；镜像 tag/digest
  继续按可信出题人定义原样使用。
- 功能、前端、文档、版本与初始 HANDOFF 共 6 个提交已快进推送到远程 `main@d19cff91`。生产
  `/root/NoCTF` 通过 SHA-256 校验后的增量 Git bundle 快进到同一提交，既有未跟踪
  `deploy/docker-compose.prod.yml`、`.env`、HTTPS 证书、PostgreSQL/Redis/上传卷和题目数据均保持原样。
  Git bundle SHA-256 为 `677CBD0F596665D222836EF9BD4D8FC96CFFF1007A7B41707F2C9A890E5457F4`，
  三服务 Linux/amd64 镜像归档 SHA-256 为
  `5665BA975EB16CB34F07D45DD71997E746CB2FEE2F90E93B5BED1F03F157B4AC`。
- 部署前使用生产 PostgreSQL 容器内凭据创建 custom-format 备份
  `/root/noctf-backups/pre-alpha41-d19cff91-20260817.dump`，并通过 `pg_restore --list` 验证；备份
  SHA-256 为 `26F07A44F7F2BA035532AF62FF4CDB3F1A226E335C2F8D287E5FF9EC4744D8AF`。Migration 容器成功应用
  `20260816181000_AddAwdpContinuousRuntime`，生产复核确认 `runtime_instances.awdp_fix_stage` 与
  Runtime-generation Flag 唯一索引均存在。
- API、Worker、Runner 均使用 `--no-deps --no-build --force-recreate` 切换，PostgreSQL、Redis、上传卷和
  题目 Runtime 没有重建。运行镜像分别为 API
  `sha256:7888f3936ed49c1f2dc81757b76764b113ad691deb5f16854bbfd0cd40675c9d`、Worker
  `sha256:c221a7e361fca04f6ce7dae50e76d7193eba3fe7d21457b572ddb51d86a94e67`、Runner
  `sha256:e548f6a181c917b6fb8c31d36e3992c0f68a5a5915d8ae74c3a2607bcbe2401b`；原三服务与 Migration 镜像保留
  `rollback-40722584` 标签。
- 部署后五项服务均 healthy；API、Worker、Runner restart count 均为 0，程序集均确认
  `0.1.0-alpha.41`。公网 `https://101.43.46.244/` 与 `/health` 返回 200，健康正文为 `Ok`；生产 OpenAPI
  包含 AWDP participant state 与管理员 scoring-impact preview 路由，两条路由未认证访问均返回预期 401。
  部署后 15 分钟 API/Worker/Runner 日志中 Fatal、Critical、Unhandled、Exception 与失败匹配均为 0。
  远端精确传输目录已删除，未执行全局 Docker prune，根分区仍有约 15 GB 可用空间。

## 2026-08-16 AWDP PWN 示例题 index-vault

- 文档提交 `33b72f7a` 在 `docs/challenge-authoring-templates/examples/awdp-pwn-index-vault/` 新增一套可直接打包交付的 AWDP PWN 示例题。示例包含 vulnerable/fixed/rule-violation 三个目标二进制构建、可信 AWDP Checker、6 类 Fix 样例、可选外部 Break 靶机 compose、作者 exploit、交付单和测试生产环境部署说明，并在出题模板索引中加入入口链接。
- 示例漏洞为 TCP note 服务越界读：`READ 4` 在漏洞版本中泄露 `FLAG` 环境变量；合法 Fix 保留 `READ 0` 正常业务并将 `READ 4` 收敛为 `ERR range`。Checker 会区分 `Fixed`、`StillVulnerable`、`RuleViolation` 和 `ServiceUnavailable`；额外 Fix 包覆盖补丁非零退出和补丁超时，用于平台侧验证 `AwdpPatchFailed` 与 `AwdpPatchTimeout`。
- 本地生成产物位于 `docs/challenge-authoring-templates/examples/awdp-pwn-index-vault/artifacts/fixes/` 与 `dist/noctf-awdp-pwn-index-vault.tar.gz`；这些产物按示例 `.gitignore` 保留在本机，不提交到 Git。最终交付包 SHA-256 为 `CD915758DFB0A451D58AA75BB1B6A0D488C75369E2394C8573966F5193CBDD8F`，包内包含 6 个已生成 Fix 归档，不包含 `__pycache__`、`.pyc`、平台地址、Token、SSH 口令、私钥或生产凭据。
- 验证通过：`bash -n` 覆盖全部 shell/Fix 脚本；`python -m py_compile` 覆盖 Checker、callback server 和 exploit；`bash scripts/build-fix-packages.sh` 成功生成 6 个 Fix 包；逐包 `tar -tzf` 确认条目仅为 `fix.sh` 或 `fix.sh + payload/pwn-note`；`bash tests/smoke.sh` 真实构建 target/checker/callback Docker 镜像并验证 StillVulnerable、Fixed、RuleViolation、ServiceUnavailable、非零退出和超时；`bash scripts/package-delivery.sh` 成功生成交付包；敏感关键字扫描和 `git diff --check` 均通过。
- 本阶段只新增出题示例和文档，没有修改业务代码、API、OpenAPI、TypeScript SDK、数据模型、migration、平台配置或运行版本；没有推送、部署或操作测试生产环境。部署测试说明见示例目录 `DEPLOYMENT.md`，其中明确 NoCTF 当前 AWDP Fix 验证 target/checker 是内部 disposable runtime；若要演示 Break，可由工作人员单独启动可控外部靶机并在 NoCTF 中配置相同静态精确 Flag。

## 2026-08-16 AWD / AWDP 可打包出题 Starter Kit

- 文档提交 `718d742e` 将原先仅供阅读和复制的 Markdown 表单具象为可直接交付的目录树，新增 `docs/challenge-authoring-templates/starter-kits/awd/` 与 `awdp/`。两套 Starter Kit 均包含可构建的 Runtime/Target、Checker、平台配置说明、出题交付清单和本地烟测；AWDP 另附合法 Fix 以及 StillVulnerable、RuleViolation、ServiceUnavailable、非零退出和超时等失败样例与批量打包脚本。
- 根目录 `starter-kits/pack.ps1` 与 `pack.sh` 可分别在 Windows 和 Linux/WSL 中生成独立的 `noctf-awd-authoring-kit.tar.gz`、`noctf-awdp-authoring-kit.tar.gz`。归档固定以 `awd/` 或 `awdp/` 为唯一顶层目录，不包含本地 `dist/`、AWDP `artifacts/`、生产地址、Token、Flag 或凭据；脚本文件在 Git 中保留 executable bit，并通过 `.gitattributes` 固定 LF。
- AWD 烟测真实构建并运行 Runtime、Checker 与本地回调接收器，验证动态 `FLAG` 环境变量注入和 `Up` 回调；AWDP 烟测真实构建并运行漏洞 Target、Checker 与回调接收器，先验证 `StillVulnerable`，再应用合法 Fix 并验证 `Fixed`。首次 AWD 烟测发现非 root Python 进程无法穿越错误权限的 `/app` 目录，随后将入口文件调整为与仓库既有 E2E 一致的根目录只读脚本并完成复验；最终 AWD、AWDP 均为 1/1 通过，测试容器、网络和镜像为 0 残留。
- 额外验证通过：全部 Starter Kit shell 脚本 `sh -n`；4 个 Python 文件语法编译；11 个相关 Markdown 文件本地链接扫描，0 个断链；敏感平台域名、生产 IP 与私钥头扫描 0 命中；`git diff --check` 通过。最终本地交付归档分别为 17、33 个条目，均无路径穿越条目且不含 `artifacts/`；SHA-256 为 AWD `6728DFBBB992652595A9B5E8109EF89822A8F0C53D9B109F806B18DD44438A32`、AWDP `95846B247F3D51EE2505D5CE4CD12B7311D50FC6E4A9B03957B81371F859E5C1`。
- 本阶段只新增和更新出题文档、示例与打包工具，没有修改业务代码、API、OpenAPI、TypeScript SDK、数据模型、migration 或生产配置，也没有部署需求；平台运行版本继续为 `0.1.0-alpha.40`。

## 2026-08-16 alpha.40 平台图标、AWD/AWDP 出题文档与模板

- 前端提交 `d1dfcd90` 将浏览器 favicon 与平台配置中的 Logo 统一：应用启动时读取既有平台配置，使用带修订号的 `logoUrl` 更新页签图标；管理员更换 Logo 后无需维护另一份图标资源。该改动没有新增 API、DTO、业务枚举、表或 migration，也没有手工修改生成 SDK。
- 文档提交 `10a1569c` 新增中文《AWD / AWDP 出题规范》，覆盖两种模式的职责边界、镜像与 Checker 契约、动态 Flag、Break/Fix、Runtime、暂停/恢复、常见失败、发布清单和验收流程。模板提交 `8010b60a` 在 `docs/challenge-authoring-templates/` 新增可复制的 AWD、AWDP 出题模板及索引，供出题人逐项填写题目信息、运行环境、Checker、Flag/Fix 和交付验收内容。
- 发布提交 `5e1037ce` 将平台版本从 `0.1.0-alpha.39` 递增为 `0.1.0-alpha.40`。Release solution build 通过，0 warning / 0 error；favicon 定向前端测试 2/2、`bun run typecheck` 与 production build 通过；文档 Markdown 本地链接扫描和 `git diff --check` 通过。
- 功能、出题规范、模板及版本提交已快进推送到远程 `main@5e1037ce`。生产 `/root/NoCTF` 通过校验后的增量 Git bundle 快进到同一提交；Git bundle SHA-256 为 `98795BCE62571A834EFFD6CF097D246F71AD26E8A282A01FB51DEC08474EC999`，Linux/amd64 API 镜像归档 SHA-256 为 `5369B245182DC06D65EDEB8D2728B06A1551712ED1C9FCB5A0F89F5584AC7A3C`，运行镜像为 `sha256:a6a8aecdb6f8a228bfa9115f7b78b3785bc1530b9d1aeb10901e39fbc6292e54`；上一版镜像保留 `rollback-10a1569c` 标签。
- 部署只用 `--no-deps --no-build --force-recreate` 重建 API；Worker、Runner、PostgreSQL、Redis、证书、上传卷、生产数据和题目 Runtime 均未重建。部署后五项服务全部 healthy、restart count 均为 0，公开 `https://101.43.46.244/health` 返回 `Ok`，API 程序集确认包含 `0.1.0-alpha.40`，部署后 API 日志无 Fatal、Critical、Unhandled、Exception 或 fail 匹配，服务器仍有约 16 GB 可用空间。
- 已将本机现有 SSH 公钥添加到服务器并以 `BatchMode=yes` 独立验证免密登录；未在仓库、远程文件、命令行参数、环境变量或交接文档中保存口令。生产仓库继续保留未跟踪的 `deploy/docker-compose.prod.yml`，未覆盖该运维文件。

## 2026-08-14 静态题目运行环境入口与 3D 中控大屏收口

- 功能提交 `dab289d1` 为选手题目接口新增只读 `hasRuntime` 契约，由后端使用现有 Runtime 模板目录解析题库定义；无 Container/Compose Runtime 的静态 CTF 题目不再挂载 Runtime 组件，也不会请求实例状态或显示“启动环境”。该字段已通过 OpenAPI 工具导出并重新生成 TypeScript SDK，没有手写 DTO、URL 或端点路径。
- 功能提交 `11060f01` 删除旧的自研二维 `/competitions/{id}/screen` 页面及导航入口，只保留协作者实现的 `/live` 3D 大屏。3D 大屏不再提供赛道切换，统一聚合所有 `isInternal != true` 的赛道数据；内部赛道不会进入排行榜、题目状态、解题流或动画队列。
- 3D 城市默认视角拉远并抬高：巡航半径和高度随城市跨度扩大，观察中心同步抬升，使空闲巡航时可以看到各队柱体的整体高度与相对态势，聚焦解题动画仍沿用既有镜头切换。
- 测试夹具提交 `30a6cab0` 补齐测试对象存储的只读对象检查能力，不改变生产对象存储实现。
- 验证通过：Release solution build 0 warning/0 error；完整非 Integration TUnit 777/777；前端完整 `bun test` 200/200（1504 assertions）、`bun run typecheck`、production `bun run build`；相机最终调整后的定向前端测试 9/9；C# analyzers、EF `has-pending-model-changes`、OpenAPI/SDK 双次生成幂等和 `git diff --check` 均通过。完整 Integration 本轮执行 173 项时曾有 2 项失败：一项是 Docker Hub 拉取 `busybox` 的瞬时 EOF，另一项是测试对象存储夹具未实现本次合法调用的 `InspectAsync`；夹具修正后两项均分别真实重跑 1/1 通过。Kubernetes 与 Libvirt 两项因环境未配置按设计跳过。
- 本阶段没有新增业务表、列、migration 或 snapshot，没有修改题目 Runtime/Checker 镜像策略。发布提交 `51468078` 将平台版本从 `0.1.0-alpha.38` 递增为 `0.1.0-alpha.39`；功能、测试、初始 HANDOFF 与版本提交已快进推送到远程 `main@51468078`。
- 生产 `/root/NoCTF` 已快进到 `51468078`。本地构建的 Linux/amd64 API/Worker 镜像归档 SHA-256 为 `B6EC465051B4AFE84BDC992CACCEB29A8E4C2D7ED2CD7C4FA3C9E23940096DC2`；远程运行的 API 镜像为 `feaf6b3cd4202230ceda89612dcf3b18864154f11476f99250cc14b8dcd75162`，Worker 镜像为 `d0977e75be28a9ee75a0c1269382fe0cfb87e77df754cd665e4225e1a8f6e436`，原 Alpha.38 镜像保留 `rollback-720e3758` 标签。部署仅以 `--no-deps --no-build --force-recreate` 重建 API 与 Worker；Runner、PostgreSQL、Redis、证书、上传卷和题目 Runtime 均未重建。部署后 API、Worker、Runner 全部 healthy，重启次数均为 0，Runner 启动时间保持 `2026-08-13T06:20:18Z`；公开 `/health` 返回 `Ok`，生产 OpenAPI 已包含 `hasRuntime`，最近 15 分钟 API/Worker 日志无 Fatal、Critical、Unhandled、Exception 或 fail。远端与本地传输归档均已清理，服务器仍有约 17 GB 可用空间；未执行数据修改。

## 2026-08-14 随机附件交付与比赛级动态 Flag 配置

- 功能提交 `4ff6a257` 重构题目附件与 Flag 管理。普通 `All` 模式允许上传并向选手展示全部附件；`RandomOnePerTeam` 模式由工作人员设置统一下载文件名并批量上传变体，原始上传文件名完整解析为对应的精确 Flag，不删除扩展名，也不向选手、下载响应、对象存储公开地址或普通日志暴露。批次会在写入前完整验证，文件暂存、数据库写入与失败补偿组成原子流程；没有新增附件—Flag 映射表。
- 随机附件在队伍第一次下载时分配。PostgreSQL 对比赛题目取得临界区锁：同队并发始终得到同一变体；仍有未使用变体时不同队伍不会重复，耗尽后才从全部变体随机复用。文件可用性在保存首次分配前确认，历史分配继续复用现有 `ChallengeFlag`、`ChallengeAttachment` 与 `File`，队伍封禁、解散或账号匿名化不会触发静默改派。
- 判题链路区分全局静态 Flag、当前队伍附件 Flag 与其他附件变体 Flag。共享 Flag 已分配给当前队伍时合法；已知但未分配给当前队伍的附件 Flag 拒绝得分并产生脱敏作弊事件，只有唯一归属队伍时才记录受影响队伍。相同队伍重复或并发提交同一外队 Flag 由既有 GameplayFact 顺序和幂等链路收敛，不重复计分或重复创建作弊事实，事件、通知和普通日志均不保存明文 Flag。
- 普通静态 CTF Flag 表单只保留精确/正则匹配和规则正文；规格类型、规格 ID、队伍 ID及生效窗口从手工界面移除。系统生成的动态 Flag 只读展示，不能作为普通静态 Flag 编辑、删除或恢复。AWDP 继续保持 Break/Fix 语义，AWD 与 KoH 的动态 Flag 生命周期没有改成手工 Flag。
- 按产品补充要求，容器动态 Flag 的每场比赛差异配置归属 `CompetitionChallenge.RulesJson`：CTF 与 AWD 的 `FlagTemplate` 可在比赛题目管理中覆盖并继承比赛默认值；题库 `Challenge.DefinitionJson` 只保留 Runtime、Checker、环境变量或目标文件等技术注入定义。不同比赛可以为同一题库模板配置不同 Flag 头，既有 Runtime generation 不会被热更新。
- 新增强类型批量上传端点 `POST /api/v1/admin/challenges/{challengeId}/attachments/random-batch`，使用请求 DTO 的 `IReadOnlyList<IFormFile>`、`AllowFileUploads()`、`ExecuteAsync`、`Results<T...>` 与 `TypedResults`。两份 OpenAPI 由工具导出、TypeScript SDK 由 `bun run api:gen` 生成；连续第二轮生成无漂移，两份 OpenAPI SHA-256 均为 `777B9FE0B0B4D98D51D98FFDEC5D2B06046DD214EB2ADDFB4CB23A0107D92128`。前端只调用生成 SDK，没有手写 URL、DTO、枚举或失败码。
- 验证通过：Release solution build 0 warning/0 error；完整非 Integration TUnit 777/777；完整 Integration 173 项中 171 通过、0 失败，2 项仅因未配置真实 Kubernetes 集群和 Libvirt disk path 按设计跳过；真实 PostgreSQL 随机附件并发/对象可用性、CTF Runtime Flag 与 AWD 轮次协调定向测试全部通过；CTF、AWD、AWDP、KoH Full E2E 各 1/1；前端 `bun test` 199/199、`bun run typecheck`、production `bun run build`；analyzers、EF `has-pending-model-changes`、OpenAPI/SDK 双次幂等与 `git diff --check` 均通过。E2E Docker 资源已精确清理。
- 本阶段没有新增业务表、列、migration 或 snapshot。发布提交 `d394959a` 将平台版本从 `0.1.0-alpha.37` 递增为 `0.1.0-alpha.38`；功能、初始 HANDOFF 与版本提交已快进推送到远程 `main@d394959a`。工作在独立分支 `codex/random-attachment-delivery-20260814` / 工作树 `E:\SourceCode\NoCTF-random-attachment-delivery-20260814` 完成，未覆盖 `E:\SourceCode\NoCTF` 中用户已有修改。
- 生产 `/root/NoCTF` 已快进到 `d394959a`。本地构建的 Linux/amd64 API/Worker 镜像归档 SHA-256 为 `A07F9FA128225FDF6DB5D06FF85C1651DB7479B5C14F01E6794D27DB996CE446`；远程导入后 API 镜像为 `a3422f023b7e38ceda430f0185d7edee443ed41be051bbba9753f4ad8cc90a33`，Worker 镜像为 `f09411304cd8c0ec95b8939bbbdcfa496b4ef4e2a501fa00750f737a62e929fb`，原 `alpha.37` 镜像保留 `rollback-ec0d7cbd` 标签。
- 部署使用 `--no-deps --no-build --force-recreate` 仅重建 API 与 Worker；Runner、PostgreSQL、Redis、HTTPS 证书、上传卷和选手 Runtime 均未重建。部署后五项服务全部 healthy，API/Worker restart count 均为 0，Runner 启动时间保持 `2026-08-13T06:20:18Z`；`https://noctf.fa1lsnow.com/health` 返回 `{"status":"Ok"}`，生产 OpenAPI 已包含随机附件批量上传路由，API/Worker 最近日志无新增 Fatal、Critical、Unhandled、Exception 或 fail。远端传输目录已删除，磁盘仍有约 17 GB 可用空间；未执行数据修复或生产业务数据修改。

## 2026-08-14 alpha.37 队伍赛道选择与 CTF Runtime Flag 注入

- 功能提交 `fae710a1` 将队伍赛道归属改为显式选择：参赛者创建队伍时必须从当前比赛可公开选择的赛道中选择一条，API 的 `CreateTeamRequest.trackKey` 从可空可选字段改为必填非空字符串；服务端不再静默回退默认赛道。Administrator、Competition Owner 和 Manager 仍可配置内部或公开赛道，并可在 Running、Paused、Finished 等生命周期中通过队伍管理页调整既有队伍归属；赛道定义本身在首次开赛后继续冻结。每次归属变化仍写入既有不可变 `TeamTrackChanged` 事件，没有新增赛道或分配表。
- 同一提交纠正 CTF Flag 模型：无 Runtime 的静态题继续支持 `Exact` 与 `RegularExpression`；任何 CTF Container/Compose Runtime 必须使用 `PerTeam` Flag。平台按队伍生成 Flag，Container 通过 `FlagEnvironmentVariableName`（新建默认 `FLAG`）、Compose 通过已有服务环境变量映射在启动时注入。正式比赛与赛后练习判题都只接受当前队伍、当前 `CompetitionChallengeId`、`RuntimeDefinition` 来源的动态 Exact 记录，不会把模板静态 Flag 或正则规则误用于容器题。内部持久化的 Exact 只是动态 Flag 的判定实现，管理端统一显示为“环境变量注入”，并隐藏动态题的静态 Flag 增删改入口。
- OpenAPI 两份制品由工具重新导出，TypeScript SDK 由 `bun run api:gen` 重新生成；连续第二轮导出/生成保持完全一致。两份 OpenAPI SHA-256 均为 `C17A2E7CEBFA66A7DBC903044635FFE0601BAD4891AAF6602749EA6C01B53840`，`sdk.gen.ts` 为 `7A951631F16C5AA69499FD9AFE81BFC0734296923AE7076168C39EA73D7FC1D9`，`types.gen.ts` 为 `6C47A377A55F91E57C53E8F6B1EE7EC29D42C6FAC815D10C04187ED7513EF27F`。没有手写 URL、DTO、枚举或生成 SDK。
- 验证通过：Release solution build 0 warning/0 error；完整非 Integration TUnit 763/763；真实 PostgreSQL 定向测试 6/6，覆盖赛道跨生命周期重分配、CTF 每队 Runtime Flag 生成、静态题正则判定及练习 Runtime 动态 Flag 隔离；前端完整 `bun test` 196/196（1463 assertions）、`bun run typecheck`、production `bun run build`；EF `has-pending-model-changes` 无漂移；变更 C# analyzer 与 `git diff --check` 通过。前端构建只保留既有大 chunk、plugin timing 和第三方 Node exports warning。
- 发布提交 `a9b0a531` 将平台版本从 `0.1.0-alpha.36` 递增为 `0.1.0-alpha.37`。本阶段没有新增业务表、列、migration 或 snapshot；功能、版本及初始 HANDOFF 均在独立分支 `codex/team-track-dynamic-flags-20260814` / 工作树 `E:\SourceCode\NoCTF-team-track-dynamic-flags-20260814` 完成，没有覆盖原工作区中的用户未提交修改。功能、版本及初始 HANDOFF 已快进推送到远程 `main@3b76ebbf`。
- 生产 `/root/NoCTF` 已快进到 `3b76ebbf`，并保留未跟踪的生产 Compose override。API/Worker 镜像在本地以 Linux/amd64 构建后通过归档上传，归档 SHA-256 为 `702559d46927820aace16c1c2aae2974876d864efb07ba7c0d0a8f81393cbd30`；远程导入后 API 镜像为 `c8f315f9004d5530df1d16de0b805cb534135bdc8a4ea1b32766263c1eb1b6ac`，Worker 镜像为 `f046f7e619325a3894c8a59c852c4de85d16747ea41389a376a85bad8c4af0e6`。旧镜像保留 `rollback-816bf1c4` 标签。
- 部署仅使用 `--no-deps --no-build --force-recreate` 重建 API 与 Worker；Runner、PostgreSQL、Redis、证书、上传卷和选手 Runtime 均未重建。部署后 API/Worker/Runner 全部 healthy，`https://noctf.fa1lsnow.com/health` 返回 `{"status":"Ok"}`，API 与 Worker 启动日志无新增 Fatal、Unhandled、Critical 或失败异常；Runner 保持原容器连续运行。

## 2026-08-14 alpha.36 失败 Runtime 归属收敛

- 功能提交 `ac481a32` 修复 `Failed + provider_receipt_json=null +
  runner_assignment_release_token=null + runner_id!=null` 被永久视为活动资源的问题。Worker 的既有
  Runner 归属维护扫描现在会接管这类无回执失败实例，将其进入 `Stopping`，并向原 Owner Runner
  派发精确到 Runtime ID、Generation、Pool 和 Runner 的 Stop 消息；没有绕过强制删除的活动资源保护，
  也没有直接修改或删除数据库记录。
- Runner 继续复用既有安全清理链路：按实例标签执行幂等资源清理，重新列举并确认相同
  `(RuntimeInstanceId, Generation)` 的资源确实不存在，随后按原 Runner Owner 释放可能残留的容量。
  只有上述步骤全部成功，Worker 才接受 `RuntimeStopped` 回执并写入 `StoppedAt`、将状态收敛为
  `Stopped`；清理失败、资源仍存在或容量 Owner 不匹配时保持阻塞并由 Wolverine 重试。
- 该修复覆盖生产中 `TEST GAME III` 的三条 `InvalidConfiguration` 无回执残留记录。部署后由维护消息
  自动触发收敛；确认三条 Runtime 均为 `Stopped` 后，强制删除预览不再将它们计为活动资源。
- 没有新增业务表、迁移、API、OpenAPI、SDK 或前端变更。发布提交 `74092eaf` 将平台版本从
  `0.1.0-alpha.35` 递增至 `0.1.0-alpha.36`。
- 验证通过：Runner 归属定向单元与真实 PostgreSQL 闭环 12/12；完整
  `RunnerAssignmentReconciliationTests` 19/19；Release solution build 0 warning/0 error；完整 TUnit
  930 通过、0 失败，2 项仅因未配置真实 Kubernetes 集群和 Libvirt disk path 按设计跳过；
  `dotnet format ... whitespace --verify-no-changes` 与 `git diff --check` 通过。
- 功能、版本及初始 HANDOFF 提交已快进推送到远程 `main@a99e2c9c`。生产 `/root/NoCTF` 使用校验过
  SHA-256 的增量 Git bundle 快进到同一提交；API/Worker 镜像归档 SHA-256 为
  `d5e7c8f27c8b983da0b03a9b35f4ee1c341a0a1659d562f885ceaa77be71a67d`，Git bundle 为
  `769358dc60755dd157dc3dcfcd44573aef7972504e90d2866861b356c658d931`。生产只重建 API 和 Worker，
  Runner、PostgreSQL、Redis、证书、上传卷和题目 Runtime 均未重启或覆盖；旧 API/Worker 镜像保留
  `rollback-d9c79b90` 标签。
- 部署后 API/Worker healthy、Runner 继续 healthy，HTTPS `/health` 返回 200，三服务 restart count
  均为 0，最近日志无新增 fail/critical/Fatal/Unhandled。API 程序集确认包含
  `0.1.0-alpha.36`。维护链路随后自动将上述三条生产 Runtime 收敛为 `Stopped` 并写入
  `stopped_at=2026-08-13 16:31:10Z`；三个精确 Redis capacity claim 均不存在，按 Runtime 标签查询
  Docker 也无资源，强制删除活动资源判定计数为 0。没有直接 UPDATE/DELETE Runtime 记录，也没有
  为验收删除 `TEST GAME III`；现在可由管理员在二次确认后执行强制删除。

## 2026-08-13 alpha.35 题目配置反馈与分值曲线

- 前端修复提交 `a5f8cd6e` 不再把所有 HTTP 409 解释为修订冲突；从生成 SDK 的
  `CompetitionChallengeConflictResponse.code` 精确区分题目模板重复、顺序冲突、资源标识冲突和
  `RevisionConflict`。添加失败时弹窗、已选模板、自定义标题、基础分和顺序均保留；通用
  409 只显示状态冲突，仅精确 `RevisionConflict` 会刷新并提示“已被他人修改”。
- 新建题库模板和切换 CTF/AWD/AWDP/KoH 模式时，前端现在由既有结构化定义模型生成该
  模式的完整默认 JSON；提交前再解析并规范化，保证 CTF 等模式包含必需的
  `schemaVersion`。定义无效时显示本地化错误，不暴露原始英文，也不清空当前表单。
- CTF 分值规则下新增实时虚线衰减曲线，直接复用后端权威的二次衰减公式：第 1 支解题
  队伍为初始分，到“衰减系数”指定的队伍数时降至最低分。初始分、最低分或衰减系数修改后立即
  重绘；题目未覆盖分值规则时，会加载并绘制竞赛级实际继承曲线，不再显示空输入。图表使用
  语义化主题色、SVG 虚线和可访问图像标签，没有引入新的图表依赖。
- 验证通过：前端定向 31/31；完整 `bun test` 195/195（1445 assertions）；
  `bun run typecheck`、production `bun run build` 和 `git diff --check` 通过。构建仅保留既有的大 chunk、
  plugin timing 与第三方 Node exports warning。本阶段没有 API、OpenAPI、生成 SDK、数据模型、
  migration 或业务表变化；尚未推送、合并远程 `main`、部署或操作生产数据。

## 2026-08-13 alpha.35 比赛题目自定义展示名称

- 功能提交 `c5de8114` 为既有 `competition_challenges` 增加可选 `custom_title`。比赛 Owner/Manager
  在“从题库添加题目”时可以直接填写本场比赛名称，也可以在题目基本设置中修改；空白输入规范化为
  null，并实时回退到题库模板 `Challenge.Title`，不会修改或复制全局题库题面。
- 有效标题统一为 `CompetitionChallenge.CustomTitle ?? Challenge.Title`，已经覆盖管理列表、选手题目卡、
  排行榜/中控大屏、血榜与题目发布播报、Hint 通知、咨询、作弊详情、历史裁决预览、比赛事件和数据
  导出，避免管理端改名后其他页面继续显示模板原名。
- 数据模型没有新增业务表，只在 `competition_challenges` 增加 nullable varchar(160)；migration
  `20260813042039_AddCompetitionChallengeCustomTitle` 与 snapshot 完全由 `dotnet ef` 生成，EF
  `has-pending-model-changes` 返回无漂移。Create/Update/Response 强类型协议增加 `customTitle`，OpenAPI
  两份制品由工具导出，TypeScript SDK 由 `bun run api:gen` 生成；连续两轮导出/生成 SHA-256 均保持
  OpenAPI `6F69359939F28EE3515D508FC034AC60A2C36C17C5A2DD4E5FD27E3972CBA522`、SDK types
  `F73DAC8DFC8F768B9E18560D2AB2CFCDD643C4FB51A4E55D5B0815B2AA2FE22D`。
- 验证通过：Release solution build 0 warning/0 error；完整非 Integration TUnit 759/759；真实
  PostgreSQL GitOps 聚合持久化测试 1/1（含自定义名持久化与清空回退）；前端完整 `bun test`
  182/182（1402 assertions）、`bun run typecheck`、production `bun run build`、EF drift 与
  `git diff --check`。构建只保留既有大 chunk、plugin timing 和第三方 Node exports warning。
- 发布提交 `0b755804` 将平台版本由 `0.1.0-alpha.34` 递增为 `0.1.0-alpha.35`。本阶段在独立分支
  `codex/competition-challenge-title` / 工作树 `E:\SourceCode\NoCTF-flag-rules-20260813` 完成，未覆盖
  原工作区的用户未提交修改；尚未推送、合并远程 `main`、部署或操作生产数据。

## 2026-08-13 alpha.34 CTF 中控大屏解题镜头

- 功能提交 `5149bd4e` 依据 `DemoVideo/output.mp4` 的完整 38 秒镜头重新编排 CTF 中控大屏：保留
  左侧题目城市、右侧排行榜与实时报板的信息骨架，同时新增缓慢巡航、轨道、扫描光束、星点和交替
  绿紫题目建筑。视觉实现仍为原生 Vue/CSS，没有新增图片资产、动画库或手写接口。
- 中控大屏现在会先把首屏历史解题作为基线，后续按 `solvedAt` 与稳定 key 识别所有新增解题并排入
  队列，不再只比较一条最新记录。每条新解题依次触发目标题目聚焦、其他建筑降噪、扩散冲击环、
  粒子爆发、血榜名次、队伍、题目和得分确认牌；连续解题不会丢失或重复播放，切换赛道会重新建立
  当前赛道基线。`prefers-reduced-motion` 下会关闭巡航与庆祝动画，保留完整信息。
- 前端仅继续使用生成 SDK 与既有 Leaderboard/CompetitionHub 契约，没有 API、OpenAPI、SDK、数据模型、
  migration 或业务表变化。英文资源补齐 `Solve confirmed`。
- 验证通过：前端完整 `bun test` 180/180（1395 assertions）、`bun run typecheck`、production
  `bun run build` 与 `git diff --check`。构建仅保留既有大 chunk、plugin timing 和第三方 Node exports
  warning。使用 Microsoft Edge 1920×1080 与可丢弃本地假数据完成实际视觉验收，确认刷新后排名、
  实时报板、目标题目聚焦和解题确认牌同步出现；临时服务、截图与视频拆帧均已清理。
- 发布提交 `24126f08` 将平台版本由 `0.1.0-alpha.33` 递增为 `0.1.0-alpha.34`。本阶段在独立工作树
  `E:\SourceCode\NoCTF-flag-rules-20260813` 完成，没有覆盖原工作区中的用户未提交文件；尚未推送、
  合并远程 `main`、部署或操作生产数据。

## 2026-08-13 alpha.33 Flag 默认模板与静态正则匹配

- 功能提交 `456a913c` 将未指定或仅含空白的动态 Flag 前缀、正文分别归一为 `flag` 与 `[GUID]`，
  默认生成 `flag{<随机 UUIDv4>}`。生成后的 Flag 仍由既有 `challenge_flags`、Runtime generation 和
  轮次流程保存，不会因为同一环境被读取多次而重新生成；显式配置的 TEAMHASH 等模板继续保持原行为。
- 同一提交允许题目 Owner、题目 Manager 和平台 Administrator 在题库或比赛题目 Flag 管理中选择
  `RegularExpression`。正则采用整段、区分大小写匹配，使用 .NET `NonBacktracking`、
  `CultureInvariant` 和 100ms 超时；不支持的回溯结构会作为稳定输入错误拒绝，不会抛成 500。仅
  CTF 无 Runtime 或 `FlagSource=Static` 的题目允许正则；CTF `PerTeam`、AWD、AWDP 与 KoH 仍只接受
  精确 Flag。模板若仍有有效正则 Flag，切换到动态 Flag 或其他模式会被拒绝；软删除后可切换，之后
  不允许把该正则 Flag 恢复，避免规则静默失效。
- 正则规则同时进入正式比赛判题与 CTF 赛后练习判题。练习仍只返回 Correct/Wrong，不产生分数、
  GameplayFact、事件或通知。附件随机 Flag 选择和数据导出保留 `MatchKind`；AWDP Break、AWD 动态
  Flag 等模式边界有显式回归测试。
- 数据模型只在既有 `challenge_flags` 增加非空 `match_kind smallint default 0`，没有新增业务表。
  migration `20260812165207_AddChallengeFlagMatchKind` 由 `dotnet ef` 工具生成，snapshot 同步，EF
  `has-pending-model-changes` 为无漂移。Flag 列表/保存的强类型协议已由工具导出 OpenAPI 并重新生成
  TypeScript SDK；连续两轮导出/生成无漂移，两份 OpenAPI SHA-256 均为
  `21109CEB00029DC986CF18AE0732B7F3BFCF7A68A2C70623B88C63AB93940025`。
- 验证通过：Release solution build 0 warning/0 error；完整非 Integration TUnit 755/755；完整真实
  依赖 Integration 170 项中 168 通过、0 失败，2 项仅因未配置 Kubernetes cluster 与 Libvirt disk
  path 按设计跳过。第一次 Integration 的 BusyBox 拉取遇到 Docker Hub auth EOF，目标用例随后 1/1
  通过，整套重跑归零。前端 179/179（1389 assertions）、TypeScript typecheck、production build、
  analyzer、EF drift、OpenAPI/SDK 幂等和 `git diff --check` 均通过；构建仅有既有 chunk/plugin timing
  与第三方 Node exports warning。
- 发布提交 `4684c84a` 将平台版本由 `0.1.0-alpha.32` 递增为 `0.1.0-alpha.33`。本阶段在独立工作树
  `E:\SourceCode\NoCTF-flag-rules-20260813` 完成，没有覆盖原工作区中的用户未提交文件；尚未推送、
  合并远程 `main`、部署或操作生产数据。

## 2026-08-13 alpha.32 强制级联删除与 CTF 赛后练习

- 功能提交 `f63d7d97` 新增平台 Administrator 专用的强制级联删除入口。操作必须输入完整竞赛标题、
  填写 8–500 字符原因并再次确认；Running/Paused 竞赛以及仍有 Queued、Provisioning、Running、
  Stopping 或待清理 Provider 资源的 Runtime 会稳定拒绝。成功后在同一 PostgreSQL 事务内物理删除
  竞赛作用域的队伍、题目实例、Flag、GameplayFact、Runtime、补丁、事件、通知线程和导出数据，
  并通过现有 Outbox 清理不再引用的文件。平台管理员通知中保留一条不可变的
  `CompetitionForceDeleted` 审计事实，记录操作者、标题、原因、时间及已删除引用计数；没有新增审计表。
- 同一提交为 CTF 增加可配置的赛后练习模式。仅 Finished 且开启练习的 CTF 竞赛、原已审核且未封禁
  队伍以及 Container/Compose 题目可以启动 `Practice` Runtime；它继续使用既有 Runner 容量、TTL、
  Docker host port `0` 和清理流程。关闭练习模式前会锁定竞赛并拒绝仍有活动练习 Runtime 的更新。
  `POST /api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/practice-flag` 只返回
  Correct/Wrong；它会验证当前运行环境和当前有效静态/队伍 Flag，但不会创建 GameplayFact、比赛事件、
  血榜、通知、分数或排行榜脏标记。前端在已结束比赛的题目区复用现有 Runtime 卡片和 Flag 输入，明确
  标注“不计分”，并补齐中文、英文、loading、错误保留及正确 Flag 动画。
- 数据模型只在 `competitions` 增加 `practice_mode_enabled`，并为 `RuntimePurpose.Practice` 调整现有活动
  Runtime 唯一索引；没有新增业务表。migration `20260812152239_AddCompetitionPracticeMode` 完全由
  `dotnet ef` 生成，snapshot 同步，EF `has-pending-model-changes` 为无漂移。强制删除复用现有 16 张
  业务表、`notifications` 和 Wolverine Outbox，没有引入级联导航实体或额外审计模型。
- 新增强类型 FastEndpoints `AdminForceDeleteCompetition` 与 `JudgePracticeFlag`；OpenAPI 两份制品由工具
  导出，TypeScript SDK 由 `bun run api:gen` 生成。连续两轮导出/生成 SHA-256 保持一致：OpenAPI
  `93B331573062B9049A3C920017CB161326F0A7E4F9A64379E06CA6091B0BC560`、`types.gen.ts`
  `0F33238D41B2E76534397F6B46965A64C27BF6D2AA41995DCC11734E90026B39`、`sdk.gen.ts`
  `27E07699FC62DF90B21AF476C5CF8FDB7E4AF5C196009E9891E596C132871C18`；当前共 200 个 Endpoint，
  前端没有手写 URL、DTO 或协议枚举。
- 验证通过：Release solution build 0 warning/0 error；完整非 Integration TUnit 749/749；强制真实依赖
  Integration 169 项中 167 通过、0 失败，2 项仅因未配置真实 Kubernetes cluster 与 Libvirt disk path
  按设计跳过，真实 PostgreSQL、Redis、Wolverine、Docker Container/Compose 路径均通过；前端
  178/178（1381 assertions）、TypeScript typecheck、production build、analyzer 与 `git diff --check`
  均通过。完整集成第一次运行遇到既有 Wolverine 单活故障转移时序抖动，独立重跑 1/1 后整套重跑
  归零；未修改产品逻辑或放宽断言。
- 发布提交 `1c4cf2db` 将平台版本由 `0.1.0-alpha.31` 递增为 `0.1.0-alpha.32`。当前功能与版本均为
  本地提交，尚未合并发布后可能出现的远程 `main`、推送或部署；生产迁移、服务健康与 Microsoft Edge
  验收结果将在部署完成后补记。

## 2026-08-12 alpha.31 CTF 中控大屏

- 功能提交 `3dfd0049` 新增 CTF 专用路由 `/competitions/{competitionId}/screen`，并在比赛工作区加入
  “中控大屏”入口。页面参考 `DemoVideo/output.mp4` 的赛事态势布局，但沿用 NoCTF 深色工业视觉：
  中央按当前题目分值与解题数生成题目塔阵，右侧展示当前赛道前十名与最新解题战报，顶部展示队伍、
  解题、题目攻克、剩余时间和已进行时间；支持全屏、手动刷新、15 秒轮询与登录态实时失效刷新。
- 多赛道比赛只展示公开且允许进入排行榜的赛道，并确保排名、题目解题数和战报随赛道隔离；最多同时
  展示 8 道题，更多题目每 12 秒自动轮播。血榜使用一血、二血、三血视觉层级；新解题到达时显示短暂
  战报强调。排行榜隐藏、冻结、投影中、加载失败和非 CTF 模式都有明确状态，不伪造或补齐服务端没有
  提供的数据。
- 数据只来自生成 SDK 的 `GetCompetitionEndpoint` 与 `GetLeaderboardEndpoint`，没有手写 API URL、DTO
  或协议枚举；没有新增后端 Endpoint、数据表、列、migration、snapshot、OpenAPI 或生成 SDK 变化。
  `DemoVideo/` 已由独立提交 `ff435f47` 加入 `.gitignore`，本地参考视频不会进入仓库。
- 发布提交 `f20686a1` 将平台版本由 `0.1.0-alpha.30` 递增为 `0.1.0-alpha.31`。前端完整测试
  177/177（1371 assertions）、TypeScript typecheck、production build 与 `git diff --check` 通过；API
  Release build 0 warning/0 error。构建只有既有的大 chunk、插件耗时与第三方 Node exports 弃用提示。
- `3dfd0049` 与 `f20686a1` 已快进推送到远程 `main`。部署与生产页面只读验收将在本节后续更新；部署前
  的 Microsoft Edge 自动化因无法可靠识别当前浏览器 URL 被安全机制终止，未据此伪报视觉验收通过。

## 2026-08-12 alpha.30 多赛道发布

- 功能提交 `71e900c3`、`722b262d`、`df7a145b`、`6b7cf07a` 与 `fc58bd6e` 为一场比赛增加最多
  32 个可配置赛道。赛道配置复用 `competitions` 的结构化 JSON，队伍只增加 `TrackKey`；EF 工具生成
  migration `20260812075438_AddCompetitionTracks`，没有新增业务表。历史比赛和历史队伍通过
  `default` 赛道保持兼容，平台仍只支持 CTF、AWD、AWDP、KoH 四种模式。
- 赛道支持公开/内部、计分、动态分值、血榜资格等强类型规则；内部赛道固定为不公开且不计分。排行榜
  按赛道独立排名，非计分赛道不进入公开榜；CTF 动态分值与一二三血、AWD/AWDP 攻防结算、KoH
  当前 King 和占领区间均遵守赛道资格。比赛第一次进入 Running 后冻结赛道定义，工作人员仍可将队伍
  调整到已有赛道；内部队伍、事件和封禁公告不会泄露给参赛者。
- 新增强类型公开赛道读取以及管理端赛道读取/保存、队伍赛道分配接口；所有协议变更由工具导出
  OpenAPI 并重新生成 TypeScript SDK，前端没有手写 URL、DTO 或协议枚举。两份 OpenAPI 连续两轮
  生成均为 SHA-256 `2067A330F314105C108C0C195A52B59608FA6AAFA8333D3217A3D9A94DEE62C8`，
  总计 198 个 Endpoint，第二轮无差异。
- 管理端增加“赛道”配置页和队伍赛道分配；参赛者报名只显示可公开加入的赛道，队伍卡片和详情显示
  赛道名称，排行榜只展示公开且计分的赛道并支持切换。中文、英文、loading、失败保留与冻结提示均已
  补齐，并保持现有前端视觉风格。
- 同批发布包含 `f8a88f6e` 的受保护 Flag 最终权限：平台 Administrator 直接查看且不记读取审计；比赛
  Owner、Manager、Judge 直接查看、不要求理由，但每次成功读取仍追加不可变审计；Observer 与参赛者
  继续不可读取。发布提交 `8b6516ec` 将平台版本递增为 `0.1.0-alpha.30`。
- 最终门禁：Release build 0 warning/0 error；完整 TUnit 913 项，911 通过、0 失败，2 项因未配置真实
  Kubernetes cluster 与 Libvirt disk path 按设计跳过，真实 PostgreSQL、Redis、Docker 与 Wolverine
  场景均通过。四模式 Full E2E 为 CTF 1/1（55.313s）、AWD 1/1（1m35.674s）、AWDP 1/1
  （1m30.215s）、KoH 1/1（1m29.183s），资源已精确清理。前端 174/174（1351 assertions）、
  typecheck、production build、`bun audit`、NuGet vulnerable/deprecated 审计、analyzer、EF model drift、
  API/Host/Worker/Runner publish、两份 Compose config、恢复脚本语法和 `git diff --check` 均通过。
  kubeconform 严格校验 49 个资源：46 valid、0 invalid/error、3 个预期 Cilium CRD schema skip。
- 浏览器验收使用 Microsoft Edge 151 与可丢弃本地数据：管理员创建公开与内部赛道、保存刷新、冻结态，
  参赛者仅看到公开赛道并在指定赛道建队，队伍详情与排行榜筛选均正确；最终相关页面无新增 Console
  错误。生产发布后再次用 Edge 只读验证匿名首页、已结束比赛详情和单默认赛道排行榜，均正常加载。
- `main` 与生产工作副本已同步到 `8b6516ec8c062f88aaf173bc9549eea5505e63d5`。迁移前快照保存在服务器
  `/root/noctf-backups/pre-alpha30-20260812-194930.dump`；migration 成功应用上述三列、一列和索引变更，
  现有 4 支队伍均为 `default`。API、Worker、Runner 已切换到 alpha.30 镜像并全部 healthy；公开
  `/health/live`、`/health/ready` 与首页均为 HTTP 200，启动后未发现新增 Error、Fatal、Unhandled 或
  Exception。发布时无活动 Runtime，PostgreSQL、Redis、上传卷、HTTPS 证书与生产 override 未被替换。
- 生产服务器访问 Docker Hub 校验 Runner 的已固定基础镜像时超时；因此使用相同 Dockerfile 与相同
  digest 在本机构建 Linux/amd64 Runner，传输并加载最终镜像后完成切换。该过程没有改变题目镜像策略：
  Runtime、Checker 和 Compose 镜像仍按用户决策接受可信 tag 或 digest，不做平台自动 digest 固定。

## 2026-08-12 受保护 Flag 读取权限与审计策略

- 功能提交 `f8a88f6e` 按最终产品语义调整受保护 Flag 读取：平台 Administrator、比赛 Owner、
  Manager、Judge 均可直接读取，不再要求填写理由；Observer 与参赛者仍不可读取。管理端只有具备
  Judge 及以上权限的角色显示“读取 Flag”入口，打开后立即加载，并保留 loading、错误、重试及过期
  响应隔离。
- 审计按角色区分：平台 Administrator 的成功读取不追加读取审计；比赛 Owner、Manager、Judge 的
  每次成功读取仍追加不可变 `ProtectedGameplayFactValueAccessed` 事件，但不再记录人工理由。该规则
  同时覆盖提交列表中的显式读取和作弊事件详情中的 Flag 展示；作弊确认、驳回、纠正等处置审计没有
  改动。
- 强类型接口 `POST /api/v1/admin/competitions/{competitionId}/gameplay-facts/{gameplayFactId}/value`
  保留原路径和响应，但请求体已移除。两份 OpenAPI 由工具重新导出，TypeScript SDK 由
  `bun run api:gen` 重新生成，并通过连续两轮生成幂等检查；两份 OpenAPI SHA-256 一致。没有手工
  修改 SDK、没有新增表、列、migration 或 snapshot。
- 验证通过：Release solution build 0 warning/0 error；完整非 Integration TUnit 717/717；作弊接口
  定向测试 5/5；真实 PostgreSQL CompetitionEvent 测试 2/2；前端测试 170/170（1313 assertions）、
  typecheck 与 production build；改动 C# 文件的 whitespace/analyzer verify；`git diff --check`。
  前端构建只有既有的大 chunk、插件耗时与第三方 Node exports 弃用 warning。
- 平台版本仍为 `0.1.0-alpha.29`。本阶段尚未推送、部署或操作生产数据。

## 2026-08-12 alpha.29 生产发布

- 生产首次切换 alpha.28 后，API 存活正常，但 API/Worker/Runner 的 `/health/ready` 把 Wolverine
  不提供端点健康信息时的 `Unknown` 当成故障，导致 Worker/Runner 长期 unhealthy、公开 ready 返回
  503。根因不在 PostgreSQL、Redis 或消息代理；日志确认相应 Wolverine listener/agent 已正常启动。
- 修复提交 `bf0aa251` 只把明确的 `Disconnected`、`Reconnecting`、`NotStarted`、`Stopped`、
  `Faulted` 和 sender latch 视为不可用，保留依赖失败时的 fail-closed 行为，同时允许传输未实现健康
  上报时的 `Unknown`。新增连接/接收循环状态矩阵与 sender latch 测试；平台版本递增为
  `0.1.0-alpha.29`。没有表、列、migration、snapshot、HTTP/OpenAPI 或 SDK 变化。
- 修复验证：`RoleReadinessHealthCheckTests` 13/13；完整非 Integration TUnit 716/716；Release
  solution build 0 warning/0 error；analyzer verify 与 `git diff --check` 通过。最终 alpha.29 再次强制
  执行全部真实 Docker/PostgreSQL/Redis/Wolverine 测试，共 881 项，879 通过、0 失败、2 项因未配置
  真实 Kubernetes cluster 与 Libvirt disk path 按设计跳过。此前四模式 Full E2E、167/167 前端测试、
  两轮 OpenAPI/SDK 幂等、EF model drift、Compose 与 kubeconform 结果仍适用，本次变更不触及对应
  业务路径。
- 生产数据库迁移容器返回 `No migrations were applied. The database is already up to date.`。随后仅重建
  API、Worker、Runner，保留 PostgreSQL、Redis、上传卷、证书与未跟踪的生产 override compose。
  alpha.29 切换后三个服务均为 running/healthy；公开 `/health/live`、`/health/ready` 和首页均返回
  HTTP 200；启动后未发现新增 Error、Fatal、Unhandled 或 Exception；发布时活跃平台 Runtime 数为 0。
- `main` 已前进至 `bf0aa2516ec05a3fbdf92da143cb0f433fd3eced`，服务器工作副本同步到同一提交。
  alpha.28 前一版本和部署前版本均保留精确镜像标签用于回滚；未删除或修改生产业务数据。

## 2026-08-12 头像图像处理迁移至 ImageSharp

- 功能提交 `08e6a0f8` 将头像元数据识别、解码、居中裁剪、缩放与 WebP 编码从 SkiaSharp
  迁移至纯托管 `SixLabors.ImageSharp 3.1.12`；依赖中已完全移除 `SkiaSharp` 与
  `SkiaSharp.NativeAssets.Linux.NoDependencies`，DI 现在注册 `ImageSharpAvatarImageProcessor`。
- 保留原有安全与产品语义：仅接受 JPEG、PNG、WebP；在像素解码前校验单边尺寸和总像素；拒绝
  APNG、动画 WebP 和多帧图像；以图片中心裁成正方形，缩放到 512×512，并以质量 90 输出单帧
  WebP。上传接口、失败码、对象存储格式和前端裁剪契约均未变化。
- ImageSharp 4.0.0 会在构建阶段强制要求 Six Labors 许可证密钥，因此没有把许可证凭据或构建时
  外部授权依赖引入仓库；采用仍可无密钥构建且 NuGet 未标记已知漏洞/弃用的最新 3.x 维护版
  3.1.12。`dotnet list ... --vulnerable --include-transitive` 与 `--deprecated --include-transitive`
  对全部 13 个项目均返回无问题。
- 测试已随实现改为 ImageSharp，并新增居中裁剪像素断言。验证通过：头像定向测试 9/9；Release
  solution build 0 警告/0 错误；非 Integration TUnit 708/708；analyzer verify、定向 whitespace
  verify、`git diff --check` 及排除 `bin/obj` 的全后端 SkiaSharp 源码扫描均通过。
- 最终发布门禁在统一分支重新完成：强制真实 Docker 的完整 TUnit 共 873 项，871 通过、0 失败、
  2 项因未配置真实 Kubernetes cluster 与 Libvirt disk path 按设计跳过；PostgreSQL、Redis、Docker
  Container/Compose 与 Wolverine 场景均通过。四模式 Full E2E 串行结果为 CTF 1/1（54.782s）、AWD
  1/1（1m32.481s）、AWDP 1/1（1m30.002s）、KoH 1/1（1m16.460s）。`2fb4b69c` 将 KoH
  排行榜断言窗口从恰好等于 15 秒维护周期改为 30 秒；保留的失败现场证明控制事实、Dirty 标记和后续
  投影均正确，没有修改产品计分逻辑。
- 最终前端门禁为 167/167（1300 assertions）、typecheck、production build 与 `bun audit` 全通过。
  OpenAPI 和生成 TypeScript SDK 连续两轮工具生成均无漂移；EF CLI 10.0.9 确认模型无 pending
  changes；Release build、analyzer、NuGet vulnerable/deprecated 审计、API/Host/Worker/Runner publish
  均通过，API/Host 发布包均包含 SPA fallback。两份 Compose config 通过；kubeconform 严格校验
  49 个资源为 46 valid、0 invalid/error、3 个预期 Cilium CRD schema skip。
- 发布提交 `a167993b` 将平台版本递增为 `0.1.0-alpha.28`。本阶段没有新增表、列、migration、
  snapshot、HTTP/OpenAPI 或生成 SDK 变化。最终同步确认 `origin/main` 是当前 HEAD 的祖先，没有待合并的
  远程提交或冲突；截至本记录仍未推送、部署或操作生产头像数据。

## 2026-08-12 全量审计修复最终收口

- 最终功能基线为 `ebc22571`，分支为 `codex/fix-audit-findings-20260811`，平台版本为
  `0.1.0-alpha.27`。本轮没有新增业务表、列、EF migration 或 snapshot，也没有手工修改生成 SDK、
  migration 或 OpenAPI。原始工作区的 `TODO.md`、本地端口清单与其他用户/协作者文件始终未被覆盖。
- 最后一轮安全和一致性修复包括：题目附件下载严格绑定已授权模板作用域（`9ef17b6f`）；角色变更、
  删除与匿名化共用最后一名 Active Human Administrator 的 PostgreSQL 临界区（`80671199`）；Worker
  readiness 纳入 Redis（`daf7f47c`）；分值配置与历史/导入数据的开赛门禁均有有界校验
  （`ba60a42e`、`04c655f3`）；排行榜按完整快照渐进展示（`f29c20ac`）；前端 SignalR 重连、通知精准
  目标、Warning 日志默认值、可访问性与密码控件收口（`f46397a3`）。
- 部署和测试基础修复包括：默认拒绝网络策略在工作负载前应用（`d8696ea1`）；官方 CI、Action、
  Testcontainers 与 E2E 基础依赖固定且部署门禁可执行（`bea86496`、`a96046d7`、`66339ffb`）；Wolverine
  maintenance failover 测试宿主关闭误扫描（`4bf41151`）。题目 Runtime、Checker、Compose 镜像仍按用户
  最终确认，信任出题人输入的 tag 或 digest 原样运行；没有恢复 OCI digest 固定或 registry allowlist。
- 四模式 E2E 已更新为当前字符串协议、异步投影与生命周期契约，并把报名、题目和 Flag 配置全部放在
  `Visible` 阶段完成，随后把日程变为到期、发布并显式启动，避免测试编排器与真实生命周期代理竞争
  （`2cdf8a2e` 至 `822aa8cb`、`ee4d48e7`）。共享 Docker daemon 的 Runner 对其他活跃 assignment 不再
  误判为孤儿资源（`7d69cef5`）。Standalone Runner 已注册 AWDP Fix execution fence，非零 Checker
  exit 会稳定落为 PlatformFailed，成功 exit 仍只接受认证 callback（`629333cd`、`b48487e3`）。
- 队伍创建与全部成员变更现在于 Read Committed 事务第一步锁定对应 Competition 行；报名、邀请加入、
  离队、移除、转让队长和邀请令牌轮换共享同一临界区，避免排行榜维护导致 SQLSTATE 40001/HTTP 500，
  并保证同用户并发创建/加入只能成功一次。`LeaderboardDirty` 在唯一 SaveChanges 前写入，提交后才刷新
  Wolverine Outbox（`ebc22571`）。真实 PostgreSQL 红测已在旧实现稳定复现 40001，并验证修复后的
  refresh-vs-create、双创建、create-vs-join、成员数组并发更新与 dirty=false→true。
- OpenAPI 两份制品已由工具导出，TypeScript SDK 已由 `bun run api:gen` 生成；连续两轮导出/生成均无
  diff，OpenAPI SHA-256 为
  `7D35FCCE6F2FC2BD226404BBA6CC3567407EB6DA1C31D07857D3D19422A3311E`。EF CLI 10.0.9 的
  `migrations has-pending-model-changes` 返回无变化。
- 最终后端门禁：Release solution build 0 warning/0 error；analyzer verify 通过；完整 TUnit 共 872 项，
  870 通过、0 失败、2 项按环境缺失跳过（真实 Kubernetes cluster、Libvirt disk path）。强制真实
  PostgreSQL、Redis、Docker Container/Compose、Wolverine Integration 均通过。四模式 Full E2E 串行
  结果为 CTF 1/1（1m00.710s）、AWD 1/1（1m31.406s）、AWDP 1/1（1m29.959s）、KoH 1/1
  （1m16.513s）；所有临时容器、网络、卷和本地测试镜像均精确清理。
- 最终前端门禁：`bun test` 167/167（1300 assertions）、typecheck、production build 与 `bun audit`
  全部通过；只有既有的大 chunk 与第三方 Node exports 弃用 warning。API、Host、Worker、Runner Release
  publish 均成功，API/Host 包含 SPA fallback 文件。两份 Compose config 通过；kubeconform 严格校验
  49 个资源为 46 valid、0 invalid/error、3 个预期的 Cilium CRD schema skip。
- 灾备门禁：全部 `deploy/recovery/*.sh` 先通过 `bash -n`；`rehearse.sh` 随后完成加密签名备份、隔离
  restore，并确认 PostgreSQL rows、Wolverine schemas、对象 key、内容与 metadata 一致；演练容器、网络
  和临时镜像已清理。
- 浏览器验收只使用 Microsoft Edge `msedge` channel 与可丢弃本地数据。已验证管理员登录、登录密码
  明文切换、语言/主题切换不导航、不替换当前 main/input 且保留表单值、通知中心空态、平台日志默认
  Warning 且实时流连接、账户资料、竞赛永久删除影响预览、停止后的 Runtime 操作页，以及提交筛选和
  历史裁决预览。最后一轮有效页面访问为 0 个 HTTP >=400、0 个 request failure、0 个 console error。
- 本轮尚未推送、部署或操作生产数据。真实 Kubernetes/Libvirt 验收仍是明确的外部环境阻塞项，不能
  报告为通过；历史裁决纠正继续只提供只读预览，任何生产分数/血榜变化都需要逐比赛明确批准。

## 当前状态

- 已用 EF CLI 重建 `InitialBaseline`，业务 schema 已按 GameplayFact 收敛为 16 张表；旧
  `submissions`/`scoring_events` 已由单一 `gameplay_facts` 当前事实表取代。
- API、测试与四个发布入口当前均可编译/发布；EF model snapshot 与当前模型无 pending changes
  （2026-08-12 使用 EF CLI 10.0.9 再次验证）。
- OpenAPI 与 Nuxt 生成客户端已在最终统一分支完成同步，并通过连续两次工具生成幂等检查。
- 根 `AGENTS.md`、`CONTEXT.md` 与数据库/API/消息/计分/存储文档已同步。
- 当前完整测试基线为 870 通过、2 跳过、0 失败；PostgreSQL、Redis、Docker Container/Compose 与
  Wolverine 真实依赖场景已强制执行，四模式 Full E2E、灾备演练、发布与 Edge 验收均已完成。只有
  未配置真实 Kubernetes cluster 与 Libvirt disk path 的两项外部集成按设计跳过。

## 2026-08-11 全量审计修复：历史裁决差异只读预览

- 功能提交 `93d3c245` 新增强类型管理端只读 API 与比赛“提交”页预览；`99b64880` 随后把推导收紧为
  保守的历史分析。当前只完整重算 CTF Flag 的确定性期望结果；AWDP 不做一般性重放，只由
  `e2bfb7f0` 识别旧实现留下的 `BreakAttempt + DuplicateAchievement + Duplicate` 这一种确定性缺陷，
  其正确期望为 `Correct`。合法的后续/跨轮 Correct、其他 Duplicate、AWD 与 KoH 均不会被擅自推断。
- 预览按权威 `(OccurredAt, Id)` 顺序读取。缺少裁决记录、队伍资格历史无法证明、冲突历史以及
  Correct→Wrong→Correct 等情形统一标记 `NeedsReview`；只有证据完备的差异标记 `Deterministic`。
  API 对不存在的比赛返回强类型 404，不提供应用更正、重判或写入按钮。
- 查询使用签名 keyset cursor、可选题目筛选和 1..100 页大小；每页证据读取有 500 条硬边界。Cursor
  绑定 endpoint、比赛、调用人和筛选，不能跨比赛、跨角色或跨筛选复用；读取在 Repeatable Read 快照
  中完成，并以集合查询判断首次正确事实，避免分页内部产生自相矛盾的视图。
- Owner、Manager、Judge、Observer 与平台管理员可读，参赛者不可读；`24e2be9d` 进一步保留已归档比赛
  的工作人员永久历史与裁决预览访问，不因 `DeletedAt` 误报 404，也没有扩大参赛者权限。响应只包含
  工作人员原本有权查看的事实元数据与受保护值，预览前后不修改 GameplayFact、CompetitionEvent 或
  Notification。
- `cdb22924` 仅修正管理端中英文说明和测试断言，使界面准确描述“CTF 完整预览 + 仅旧
  DuplicateAchievement AWDP Break 缺陷”，没有扩大后端推导范围。该提交验证：定向前端测试 3/3
  （11 assertions）、typecheck 与 `git diff --check` 通过。
- 本阶段没有新增表、列、migration 或 snapshot。历史阶段曾生成过 OpenAPI/SDK，但上述合并后的最新
  协议仍须在统一收口阶段重新由工具生成。尚未推送、部署或修改任何生产历史；实际历史纠正仍须先
  生成预览并由用户逐比赛明确批准。

## 2026-08-11 全量审计修复：可信容器安全兼容开关

- 功能提交 `003983a5` 落实用户确认的可信兼容策略：缺省的
  `NoNewPrivileges`、`RunAsNonRoot` 与 `ReadOnlyRootFilesystem` 均按 `false` 处理；题目显式关闭时
  Docker 不再伪装成已启用该限制，允许可信旧镜像按原有用户和可写根文件系统运行。
- 当 Docker 题目显式要求 `RunAsNonRoot=true` 时，Runner 会在创建容器前检查镜像 `Config.User`，只接受
  数字形式的非零 `uid` 或 `uid:gid`；空值、root、名称形式或无法证明的用户都会 fail closed。
  Kubernetes 保留既有 securityContext 行为，Compose 的兼容边界未被暗中改变。
- 本阶段没有新增表、列、migration、snapshot、OpenAPI 或 SDK 变化。验证通过：Release solution build
  0 警告/0 错误、非 Integration 684/684、真实 Docker 定向测试 12/12、前端 game-config 7/7 与
  `git diff --check`。尚未推送、部署或操作生产 Runtime。

## 2026-08-11 全量审计修复：可信题目镜像策略

- 用户最终明确选择信任 Organizer、题目 Owner/Manager 提供的 Runtime、Checker 与 Compose 镜像，
  不要求把题目镜像 tag 解析或固定为 OCI digest，也不增加 Registry origin、认证源、DNS 或私网
  allowlist。题目仍按现有 tag/digest 原样交给所选 Runtime provider；这是明确的产品信任边界。
- `e4235b5f` 曾实现题目镜像 digest 固定，但该方案被用户明确否决；`623e66a4` 以可追溯 revert 完整删除
  OCI resolver、发布/开赛/恢复门禁、Runner mutable-image guard、相关协议、前端提示与测试，没有留下
  隐藏的固定逻辑或新增数据模型。平台自身构建基础和官方依赖镜像的可复现供应链固定不属于题目镜像
  运行语义，本次没有回退。
- 撤销后验证通过：Release solution build 0 警告/0 错误、Container Runtime handler 5/5、Compose
  policy 16/16、AWDP execution fence PostgreSQL 3/3、真实 Docker lifecycle 12/12 与
  `git diff --check`。日志轮转、AWDP durable replay fence、失联 Runtime 恢复和显式
  `RunAsNonRoot` 校验仍保留。
- 本阶段没有新增表、列、migration、snapshot、OpenAPI 或 SDK 变化；尚未推送、部署或操作生产镜像。

## 2026-08-11 全量审计修复：归档比赛工作人员永久历史

- 功能提交 `24e2be9d` 修复软删除比赛后工作人员历史入口被 `DeletedAt` 提前过滤的问题。Owner、Manager、
  Judge、Observer 与平台管理员仍可分页读取归档比赛的永久 `competition_events`，并可运行只读历史
  裁决预览；参赛者窗口和权限没有扩大，不存在的比赛继续返回强类型 404。
- 事件读取、比赛管理授权和历史预览共用归档感知授权语义，避免一个页面可读、另一个页面误报不存在；
  受保护 GameplayFact 值与审计可见性仍按原角色边界执行。
- 本阶段没有新增表、列、migration、snapshot、OpenAPI 或 SDK 变化。验证通过：Release solution build
  0 警告/0 错误、非 Integration 722/722、CompetitionEventHistory PostgreSQL 1/1、受保护
  GameplayFact value + 审计 PostgreSQL 1/1、HistoricalAdjudicationPreview PostgreSQL 3/3、Preview
  HTTP 4/4 与 `git diff --check`。尚未推送、部署或读取生产归档历史。

## 2026-08-11 当前统一收口状态

- 当前功能基线为 `623e66a4`。以上最近阶段均为本地提交，没有新增数据表或 EF migration，也没有推送、
  部署或操作生产数据。
- Registry/题目镜像信任边界已由用户选择，不再是阻塞项。剩余工作是工程收口：重新导出 OpenAPI、
  重新生成 TypeScript SDK 并验证二次生成无漂移；把平台版本递增为 `0.1.0-alpha.26`；运行
  Release build、完整后端/真实依赖/四模式 E2E、EF drift、前端测试/typecheck/build 与
  `git diff --check`；最后只用 Microsoft Edge 和可丢弃本地数据做浏览器验收。
- 未完成、被跳过或受外部环境阻塞的门禁不得报告为通过。完成上述收口前不得推送或部署。

## 2026-08-11 全量审计修复：SMTP 精确 DNS 策略

- 功能提交 `f55a930c` 补齐 Cilium `toFQDNs` 的 DNS 观察前提：示例 policy 仅允许 Backend/Worker
  向集群 `kube-dns` 查询配置的精确 SMTP FQDN，并只允许该解析结果上的精确 SMTP TCP 端口。
- 后续功能提交 `d8ab3ba4` 同时把基础 DNS allow 从“任意目标 53 端口”收紧为经 `kube-dns` 的
  `*.svc.cluster.local` L7 查询。这样基础 L4 allow 不会覆盖 SMTP 的精确 L7 规则；其他外部名字也必须
  显式获得自己的 exact-FQDN policy。
- 运维生成 policy 时必须同时替换 DNS 规则与目标规则中的两处示例主机名；仍禁止通配域名、任意地址
  放行与把生产 SMTP 地址或凭据提交进仓库。默认未应用 policy 时邮件投递继续 fail closed。
- 本阶段没有业务表、列、migration、snapshot、HTTP/OpenAPI、SDK 或版本号变化。验证通过：
  DeploymentTopology 6/6 与 `git diff --check`；当前环境没有 Kubernetes API，未伪报实集群 smoke。
  尚未推送或部署。

## 2026-08-11 全量审计修复：平台供应链固定

- 功能提交 `ebedac01` 将 Bun、Docker CLI、Alpine、.NET SDK/ASP.NET Runtime 构建基础，以及官方
  Compose/Kubernetes 中的 PostgreSQL、Redis、MinIO 与 MinIO Client 全部固定为“精确版本标签 +
  64 位多架构 manifest digest”；不再使用外部 `latest` 或只有可变 tag 的镜像。
- Kompose 继续固定 `v1.38.0`，但下载后会按 BuildKit `TARGETARCH` 选择官方发布的 amd64、arm64 或 arm
  SHA-256，并在赋予执行权限前用 `sha256sum -c -` 验证；未知架构 fail closed。
- digest 通过 Docker Hub tag API、MCR OCI manifest HEAD 和 Kompose 官方 GitHub Release 元数据核对；
  架构测试会拒绝漏掉 digest、重新引入外部 `latest` 或取消 Kompose checksum。生产自建 `noctf-*` 镜像
  仍必须由发布流程按平台版本和构建产物 digest 重标，不能把本地示例 `latest` 当作生产身份。
- 验证通过：Release solution build 0 警告/0 错误、DeploymentTopology 6/6、两份 Compose config
  离线解析与 `git diff --check`。当前网络能读取官方元数据，但 `registry-1.docker.io` 拉取通道超时，故
  没有伪报完整镜像构建；最终在线构建/扫描仍是发布门禁。无数据模型、migration、HTTP/OpenAPI、SDK
  或版本号变化，尚未推送或部署。

## 2026-08-11 全量审计修复：Kubernetes PID 限制证明链

- 功能提交 `953b68e9` 没有伪造 Kubernetes 不支持的 Pod 级 PID limit。运维确认 kubelet Pool-wide
  `PodPidsLimit` 后，必须以 `noctf.io/pod-pids-limit=<精确数值>` 标记允许承载题目的 Node；标签只是
  明确 attestation，不会修改 kubelet。
- Runner 启动时使用只读 Node `get/list` 权限检查至少一个标签值与配置完全一致、Ready 且可调度的节点；
  缺少证明时 fail closed。单容器、Checker、AWDP 验证与 Compose Runtime Pod 均强制同一
  `nodeSelector`，因此新节点在核验并打标签前不能接收题目工作负载。
- RBAC 仅增加 Node 元数据读取，Runner 仍无修改 Node 权限；部署说明给出显式核验/标记顺序，并重申
  Application 配置不会代替 kubelet 设置。本阶段无业务表、列、migration、snapshot、HTTP/OpenAPI、
  SDK 或版本号变化。
- 统一分支验证：Release solution build 0 警告/0 错误；StartupCheck 6/6、Kubernetes Container 25/25、
  Compose manifest 9/9、DeploymentTopology 5/5 与 `git diff --check` 通过。当前机器没有可用 Kubernetes
  API，故未伪报实集群验收；尚未推送或部署。

## 2026-08-11 全量审计修复：Kubernetes 平台出站边界

- 功能提交 `ac628036` 移除 Runner 对 `0.0.0.0/0:443,6443` 的宽泛出站放行。官方清单已要求
  Cilium，因此改用 `CiliumNetworkPolicy` 的 `kube-apiserver` 实体，只允许 Runner 到真实 Kubernetes
  API 身份的 443/6443，不能再借这两个端口访问任意 Internet 地址。
- 平台 Namespace 继续默认拒绝外联。新增不随默认清单直接应用的 `smtp-egress.example.yaml`：启用邮箱
  验证或密码找回投递前，运维必须在仓库外复制并将示例值替换为平台设置中的精确 SMTP FQDN 与 TCP
  端口，再应用该 Cilium policy；Backend 的测试邮件和 Worker 的实际投递都被同一精确边界覆盖。
- SMTP 主机或端口变化时必须同步替换 policy；文档明确禁止通配 FQDN、`0.0.0.0/0` 与提交生产地址或
  凭据。未配置 policy 时邮件保持 fail closed，而不是静默开放整段 Internet。
- 本阶段只有部署清单、权威文档和架构门禁变化，没有业务表、列、migration、snapshot、HTTP/OpenAPI、
  SDK 或版本号变化。DeploymentTopology 5/5 与 `git diff --check` 通过；尚未推送、部署或读取生产 SMTP
  配置，最终 K8s smoke 需使用部署环境的实际 API identity 和运维生成的非敏感 policy。

## 2026-08-11 全量审计修复：AWDP Fix 崩溃重放栅栏

- 功能提交 `bfb71721` 使 AWDP Fix 的一次性验证在任何外部副作用前，先于 PostgreSQL 行锁事务中推进
  execution fence，并与 Wolverine Outbox 一起发布对应超时消息；只有当前 generation 与 processing
  version 的回调或结果可以落库。
- 如果同一执行消息在结果尚不确定时重投，Worker 会先推进 recovery fence，使旧 Checker 回调、Patch
  结果与超时消息立即失效；Runner 随后按 RuntimeInstanceId 与 Generation 精确清理 target、checker、
  network 和本地工作目录，并确认 Provider 已不存在资源后才 owner-check 释放容量。
- 清理或确认失败时不会创建替代环境，也不会伪造终态；Wolverine 可以继续安全重试。清理成功后使用原
  GameplayFact、PatchUpload 与 archive 创建新的 RuntimeInstanceId 和 generation 重放；题目、比赛题目
  或规则 revision 已变化时明确 PlatformFailed，不会静默改用新配置。
- 迟到/重复 callback、重复 cleanup completion、旧 generation 与重复 replay 均由状态、generation、
  runner assignment 和 processing version 栅栏幂等忽略。本阶段复用 RuntimeInstance、GameplayFact 与
  Wolverine，没有新增表、列、migration、snapshot、HTTP/OpenAPI 或生成 SDK 变化。
- 独立阶段验证通过：Release solution build 0 警告/0 错误、非 Integration 648/648、真实 PostgreSQL +
  Wolverine durable retry 4/4，以及 Runner 精确清理/资源残留/版本回执单测、analyzer 与
  `git diff --check`。统一分支会在最终完整门禁再次覆盖；尚未推送、部署或操作生产 Fix/Runtime。

## 2026-08-11 全量审计修复：Runtime 访问地址协议边界

- 功能提交 `44b585e7` 新增 Domain 强类型 `RuntimeAccessUrl` / `RuntimeAccessScheme`，按用户确认的
  allowlist 只接受绝对的 `http`、`https`、`tcp`、`udp` 与 `ssh` 地址；统一 trim、host/authority 和
  URI 规范化校验，拒绝 `javascript:`、`data:`、`file:`、`mailto:` 与相对地址。
- 同一校验覆盖题目 Runtime 模板保存、Runner 占位符展开和 Provider 最终回写三道边界。Runner 回写
  非法地址时不会持久化或公开该 URL；实例保留 provider receipt 后进入 `Stopping`，并向原 Runner
  派发清理，避免“拒绝恶意 URL”同时泄漏实际容器资源。
- 前端新增统一 `RuntimeAccessUrl` 组件并替换选手 Runtime 卡、AWD、KoH 与管理员 Runtime 四处直接
  链接。仅 `http/https` 使用可点击链接；`tcp/udp/ssh` 显示为可选择、可复制文本，不交给浏览器导航。
- 本阶段没有新增表、列、migration、snapshot、HTTP/OpenAPI 或生成 SDK 变化；Runtime 权威文档已
  同步 allowlist 与前端点击边界。
- 验证通过：RuntimeAccessUrl 11/11、RuntimeUrlExpander 4/4、前端 Runtime URL/卡片 6/6；根分支
  ClientApp typecheck、Release solution build（0 警告/0 错误）与 `git diff --check` 通过。独立阶段还
  验证 Challenge configuration 47/47、恶意回写真实 PostgreSQL 1/1、非 Integration 662/662、前端
  146/146 及 production build。
- 尚未推送、部署或操作生产 Runtime；最终 Edge 验收只使用可丢弃实例，并分别核对 Web 链接与 TCP
  文本呈现。

## 2026-08-11 全量审计修复：归档与永久删除边界

- 功能提交 `69d1a4c5` 和 `492bc7f4` 落实用户确认的永久历史不变量：软删除/恢复是可逆归档操作；
  物理删除是独立的不可逆操作，只允许从未产生永久比赛事件或任何业务引用的空竞赛。正常创建本身会
  写入永久 `CompetitionCreated`，因此真实使用过的比赛不会因先软删除而获得物理删除资格。
- 新增强类型 `GET /admin/competitions/{id}/hard-delete-preview`。预览与 DELETE 在同一个 PostgreSQL
  竞赛行锁不变量下计算 HistoricalEvent、Team、CompetitionChallenge、GameplayFact、RuntimeInstance、
  PatchUpload、DataExport、Notification 与 PosterFile 阻塞数量；DELETE 返回 204、404 或携带最新预览
  的 typed 409。并发插入永久事件与物理删除通过外键锁和事务重检 fail closed，不会出现预览后竞态删除。
- 管理端明确分开“软删除竞赛”“恢复”和“彻底删除”。永久删除按钮只在服务端预览确认无引用时显示；
  阻塞状态展示引用种类与数量，409 会保留页面事实并刷新影响预览，不会错误切换本地删除状态。
- OpenAPI、SDK 与 API 文档已从合并后的 193 个 Endpoint 工具重生成，没有手写 URL、DTO 或枚举；
  没有新增数据表、列、migration 或 snapshot。
- 验证通过：Release solution build 0 警告/0 错误；硬删除强类型 Endpoint 与真实 PostgreSQL 竞态测试
  合计 2/2；相关 OpenAPI/路由/Raw SQL 门禁通过；ClientApp 删除与 i18n 定向测试 11/11、typecheck
  通过。独立阶段还验证了 PostgreSQL 3/3、前端 production build 与 EF model drift；合并后的 OpenAPI
  再导出和 SDK 再生成无实质漂移。
- 尚未推送、部署或删除任何生产竞赛。最终 Edge 验收仅使用新建的空竞赛验证物理删除；包含永久事件的
  测试比赛只验证阻塞预览，绝不为验收清理其历史。

## 2026-08-11 全量审计修复：比赛事件永久历史导航

- 功能提交 `11734de9` 按用户确认的保留策略区分事件读取窗口：参赛者和队伍仍必须提交最多 31 天的
  `from/to`，前端默认显示最近 30 天；Owner、Manager、Judge、Observer 与平台管理员可以省略日期，
  通过现有签名 keyset cursor 遍历该比赛永久保留的完整 `competition_events`。未授权用户尝试无界读取
  会得到强类型 403，不会因省略日期绕过可见性过滤。
- 游标签名现在同时绑定比赛、调用用户、事件筛选与日期窗口；工作人员的游标不能跨用户复用，参赛者
  的有界游标也不能切换成无界历史。导出仍强制显式的 0–31 天窗口，避免把永久历史一次性装入内存；
  本阶段只扩展分页浏览，不改变受保护事实值和 Team/Staff 事件的可见性规则。
- 参赛者动态页通过生成 SDK 判断现有比赛协作者身份。工作人员显示“完整历史”并可持续加载更多；
  其他用户显示“最近 30 天”。身份探测遇到非 403/404 的故障时明确告警并安全降级到最近 30 天，
  不会静默把服务错误表现成完整历史。没有手写 URL、DTO 或协议枚举。
- OpenAPI 两份制品已由后端工具重新导出，TypeScript SDK 已重新生成；`from/to` 由必填改为成对可选。
  没有新增数据表、列、migration、snapshot 或版本号变化。
- 验证通过：Release solution build 0 警告/0 错误；真实 PostgreSQL 永久历史与权限测试 1/1；请求验证
  单测 1/1；ClientApp `bun test` 148/148、`bun run typecheck`、`bun run build` 与 `git diff --check`
  通过。OpenAPI 与 SDK 二次工具生成成功；Nuxt 仅保留既有大 chunk、插件耗时和第三方弃用警告。
- 尚未推送、部署或读取生产比赛历史；工作人员/参赛者两种浏览器角色将在所有审计阶段收束后使用
  Microsoft Edge 和可丢弃本地数据统一验收。

## 2026-08-11 全量审计修复：官方 Checker 回调拓扑

- 功能提交 `fccd9101` 修复官方 Docker/Kubernetes 部署没有可工作的 AWD/AWDP Checker 回调路径。
  没有新增网关进程、业务表或回调协议；仍使用现有 Runner 签发的短期 JWT 和 API 强类型内部 Endpoint，
  并保留一次 Checker 执行可多次回调、后写覆盖的既有契约。
- Docker 官方 Compose 为所有 API 角色容器注入精确
  `noctf.io/internal-role=scoring-callback-gateway` 标签。Runner 默认按该标签发现全部运行中的 API 容器，
  并仅将它们接入本次 Checker 的 internal callback network；显式 `CallbackContainer` 配置仍可用且同样
  强制校验角色标签。回调容器扩缩容不再依赖固定 container_name。
- Kubernetes 回调地址改为准确 FQDN `backend-service.noctf.svc.cluster.local:8080`；Runtime namespace
  的 Checker egress 同时约束平台 namespace、callback Pod 标签与 TCP 8080，平台 ingress 反向只接受
  purpose 为 `awd-checker` 或 `awdp-checker` 的 Runtime Pod。题目业务 Pod 不获得平台 API 访问能力。
- Docker/Kubernetes 配置、NetworkPolicy、部署说明与 Runtime 文档已同步。没有 migration、OpenAPI、
  SDK 或版本号变化。统一分支验证：Release build 0 警告/0 错误，Deployment topology 4/4、Kubernetes
  lifecycle 25/25、真实 Docker lifecycle 11/11、真实 Docker Compose 4/4；独立阶段另验证 46/46 K8s
  manifests、两份官方 Compose config 及 analyzer。尚未推送、部署或操作生产 Checker/Runtime。

## 2026-08-11 全量审计修复：Runtime 隔离网络语义校正

- 用户在 `/grilling` 中明确选择保留 Docker 当前 bridge 行为，不增加宿主防火墙或 Egress Gateway。
  功能提交 `682dae90` 因而将跨 Provider 的 `RuntimeEgressPolicy.DenyAll` 更名为
  `RuntimeEgressPolicy.Isolated`；枚举数值仍为 0，既有 DefinitionJson 与前端配置保持兼容。
- Docker Container/Compose 的 `Isolated` 使用每个 Runtime 独立、且不接入平台网络的 routed bridge；
  它隔离其他 Runtime 和平台容器网络，但 Docker 默认 NAT 外联仍存在，不能声称阻断公网、宿主、局域网
  或云元数据。`InternetOnly` 继续在保存和执行边界拒绝。Compose 生成结果继续显式
  `internal: false`，并由测试锁定，避免未来再次把名称误解为断网保证。
- Kubernetes 的同名 `Isolated` 仍由每 Runtime NetworkPolicy 实施缺省拒绝，只放行同一 Runtime、DNS、
  精确回调与声明的入站；`InternetOnly` 仍仅增加排除受保护网段后的公网 IPv4。权威 Runtime 文档已明确
  两个 Provider 的最低共同能力名称不代表 egress 等价。
- 没有新增表、列、migration、snapshot、HTTP/OpenAPI、生成 SDK 或版本号变化。验证：Release solution
  build 0 警告/0 错误；Compose policy 16/16、Runtime claim 11/11、Kubernetes lifecycle 24/24，
  ClientApp game-config 5/5 与 `git diff --check` 通过。尚未推送、部署或操作生产 Runtime。

## 2026-08-11 全量审计修复：前端生成契约边界与咨询历史分页

- `5c204b03`、`99ae4e65`、`d579509e`、`3b1d3aab`、`fddbaf8a`、`dee9fa63`、
  `878dae75`、`c8c07953`、`c0528d35`、`4e79f9f3` 与 `e77bd322` 收敛前端协议边界：认证刷新使用
  隔离的生成 Client 与生成 Endpoint，是否刷新只由请求是否携带 Access Token 判定；角色、比赛模式、
  状态、GameplayFact 筛选和标签映射均使用生成协议类型，不再维护漂移的手写枚举或 DTO。
- 题目附件、随机附件、平台日志、平台审计、比赛事件和数据归档下载全部由生成 SDK 构造请求并以 Blob
  解析；统一下载工具只负责校验生成客户端响应、解析文件名和触发浏览器保存，不再拼接 API 路径或自行
  读取 Token。平台 Logo 使用后端返回的带 revision URL。新增边界测试递归禁止产品源码出现手写 REST
  路径或 `$fetch`；唯一保留的 raw `fetch` 只用于重放生成 SDK 已构造且携带认证的 `Request`。
- `6e8c93b5` 将参赛者咨询列表接入后端签名游标，每页 50 条并提供明确“加载更多”。首屏列表与
  `?question=` 深链详情并行读取；列表、详情、创建、回复及状态变化按 id、revision、更新时间合并，
  快速切换详情使用 latest-request fence，SignalR 连续失效使用 trailing refresh，旧响应不能覆盖新事实。
  失败时保留已加载列表、详情与输入，后台不可见时不会误把新回复标记为已读。
- 合并时保留既有 cursor generation、列表错误态和 SignalR 提前刷新逻辑，并以 `bf31eae2` 修正隔离
  生成刷新客户端对应的测试断言；没有修改手写 OpenAPI/SDK 产物、数据模型、表、migration、snapshot
  或版本号。
- 统一分支验证：ClientApp `bun test` 144/144、`bun run typecheck`、`bun run build` 和
  `git diff --check` 通过；Nuxt 只保留既有大 chunk、插件耗时和第三方 trailing-slash deprecation
  Warning。尚未推送、部署或操作生产通知/咨询数据；浏览器动态验收留到所有审计修复合并后使用
  Microsoft Edge 与可丢弃数据统一进行。

## 2026-08-11 全量审计修复：用途级上传上限与 AWDP Fix 配置

- 用户通过 `/grilling` 选择用途级上传上限，并明确要求 Fix 包可在题目管理中配置。功能提交
  `471c3577` 新增启动时验证的 `Uploads` 配置：用户/队伍头像、平台 Logo、比赛海报默认均为 12 MiB，
  题目附件默认 1 GiB；每项允许 1 byte 至 1 GiB。各上传 Endpoint 同时设置“文件上限 + 64 KiB
  multipart 开销”的传输硬上限，防止模型绑定前无限写临时盘。
- 超过精确业务上限统一返回 413/`UploadTooLarge`，不会进入图片解析、对象存储或业务引用写入。此前
  题目附件的写权限预检仍在对象暂存前执行，最终引用写入继续二次授权，保持 TOCTOU fail-closed。
- AWDP `Challenge.DefinitionJson` 新增 nullable `MaximumPatchUploadBytes`，题目管理以 MiB 编辑；缺省
  256 MiB、最大 1 GiB。上传作用域从现有 Competition/CompetitionChallenge/Challenge JSON 解析有效值，
  超限在 tar.gz 解压校验与对象存储前返回 413/`ArchiveTooLarge`。没有新增表、列、migration 或 snapshot。
- OpenAPI 两份制品已由后端工具重新导出，TypeScript SDK 已重新生成；六个上传端点均声明 typed 413，
  Patch 同时保留 typed 422。前端只编辑现有强类型配置模型和生成 SDK，不手写 URL/DTO。
- 验证通过：超限 Fix 真实 TestServer HTTP 场景 1/1（并断言存储零调用）、完整非 Integration TUnit
  649/649、Release solution build 0 警告/0 错误、ClientApp 完整 `bun test` 140/140、typecheck、production
  build 与 `git diff --check`。构建只保留既有 chunk-size/第三方 deprecation 警告。
- 尚未推送、部署或操作生产文件；部署 overlay 如配置反向代理 body limit，必须不低于 Endpoint 的传输
  硬上限，否则会在应用强类型错误前被代理拒绝。

## 2026-08-11 全量审计修复：AWD Flag 不限量批次语义

- 用户随后明确覆盖先前的批次上限选择。功能提交 `03d57937` 撤销 `994c4fff` 加入的 64 项/256 KiB
  上限和 `FlagBatchLimitExceeded` 失败码；AWD `flags` 数组现在不限元素数量、无批次总字节上限。
- 每个 Flag 仍必须为 1–4096 UTF-8 bytes 且禁止 NUL；任一元素非法时整批原子零写入并返回
  `FlagInvalid`。合法批次按输入顺序一次持久化，每项创建独立 GameplayFact，不新增 Batch 实体。
- 多 Flag 仍只属于 AWD；CTF/AWDP 继续要求单 Flag并返回 `FlagBatchNotSupported`。Redis submission
  rate limit 仍按一次 HTTP 请求计费，未擅自改成逐项计费。
- 权威 API/AWD 文档与测试已同步；没有数据表、列、migration、snapshot、OpenAPI/SDK 实质变化或
  版本号变化。
- 验证通过：FlagSubmissionBatch 6/6、SubmitFlagProtocol 5/5、真实 HTTP 限流 1/1、完整非
  Integration 655/655、Release solution build 0 警告/0 错误。回归明确覆盖 65×4096-byte（超过旧
  256 KiB）批次成功，以及单项 4097 bytes/NUL 仍零写入；OpenAPI 二次导出无制品漂移，
  `git diff --check` 通过。尚未推送、部署或操作生产 GameplayFact。

## 2026-08-11 全量审计修复：无 receipt Runtime 恢复闭环

- 功能提交 `347ca7e8` 修复 `Provisioning`、`Running` 或 `Stopping` 实例保留 `runner_id`、但缺少
  provider receipt 时被 Worker 直接写成 `Stopped` 的资源泄漏路径。此类实例现在必须由原 Runner 按
  RuntimeInstanceId 与 Generation 幂等检查并清理实际资源；清理确认前不写终态、不释放容量、不派发
  replacement。
- 清理成功后先执行 owner-checked 容量释放，再接受同时携带 generation、pool 与 runner 栅栏的回执并
  派发替代实例。错误 owner/generation 回执不能推进状态；重复清理或重复回执只释放和派发一次。
- 清理失败会保留原 owner/容量事实，并把旧实例及 replacement 明确置为 `Failed`；后续 Start 会先重试
  尚未清理的 ancestor，避免在同一身份上叠加新资源。
- 本阶段只调整既有 RuntimeInstance 与 Wolverine/Runner 内部消息状态机；没有新增表、列、migration、
  snapshot、HTTP/OpenAPI、生成 SDK 或版本号变化。
- 原独立阶段验证受影响 8 个测试类共 80/80，通过真实 PostgreSQL + Redis 的 receipt 丢失、重复消息、
  容量与 replacement 顺序及 owner/generation 栅栏场景。合入统一分支后完整 Release build 为 0 警告/
  0 错误，RunnerAssignmentReconciliation PostgreSQL 定向测试 16/16 与 `git diff --check` 通过。
- 尚未推送、部署或操作生产 Runtime；部署前应继续用可丢弃实例做 Runner 进程中断与消息重投验收。

## 2026-08-11 全量审计修复：Runtime 管理操作响应性

- 功能提交 `3e82ca7e` 将管理端 Runtime 启动、重置、终止和强制终结改为按实例协调：同一实例单飞防止
  重复提交，不同实例可以并行操作，页面不再因某个实例的状态收敛而全局禁用全部操作。
- 后端返回 202 后立即显示“已受理”，状态读取转入后台；轮询最多 12 次且总计不超过 30 秒，离开页面会
  取消在途读取。超时只提示稍后刷新，请求失败保留现有列表和详情事实，不会本地伪造终态。
- 列表的人工刷新、筛选刷新、实时失效刷新和后台操作刷新通过同一串行合并器协调，避免互相重置游标或
  用旧页覆盖最新状态。
- 本阶段没有 HTTP/OpenAPI、生成 TypeScript SDK、数据模型、数据表、migration、snapshot 或版本号变化；
  所有请求继续调用生成 SDK。
- 合入统一分支后定向测试 6/6 与 `bun run typecheck` 通过；原独立阶段完整 `bun test` 130/130、
  `bun run build` 和 `git diff --check` 通过。尚未推送、部署或操作生产 Runtime；并行操作、离页取消和
  慢状态提示将在 Microsoft Edge 使用可丢弃实例最终验收。

## 2026-08-11 全量审计修复：键盘导航与图标按钮语义

- 功能提交 `2700814a` 让平台用户、死信队列和参赛者排行榜的整行详情入口可通过 Tab 聚焦，并支持
  Enter 或 Space 打开；焦点状态清晰可见，读屏标签包含当前用户、消息类型或队伍名称。
- 排行榜上一页/下一页、通用 Dialog 与 Sheet 关闭按钮补齐本地化 `aria-label`；纯装饰图标对读屏隐藏，
  关闭按钮的隐藏文本也不再写死英文。
- 本阶段没有 HTTP/OpenAPI、生成 TypeScript SDK、数据模型、数据表、migration、snapshot 或版本号变化；
  `app/api` 生成目录未被修改。
- 验证通过：无障碍与 i18n 定向测试 11/11，ClientApp 完整 `bun test` 133/133、
  `bun run typecheck`、`bun run build` 和 `git diff --check`；构建只保留既有警告。
- 尚未推送、部署或操作生产数据；最终键盘焦点顺序和视觉焦点环将在 Microsoft Edge 统一验收。

## 2026-08-11 全量审计修复：密码可见性控件

- 功能提交 `d425a268` 新增统一 `PasswordInput`，在输入框右侧提供小型眼睛按钮，并覆盖登录、注册、
  邮件重置密码及账户改密的全部密码与确认密码字段。按钮固定为 `type=button`，不会误触发表单提交；
  切换只改变输入呈现，不改变密码值、浏览器 autocomplete 或既有校验流程。
- 控件使用 `aria-label` 与 `aria-pressed` 明确表达“显示密码/隐藏密码”状态，图标对读屏隐藏；中英文资源
  均已补齐，保持现有组件和视觉风格。
- 本阶段没有 HTTP/OpenAPI、生成 TypeScript SDK、数据模型、数据表、migration、snapshot 或版本号变化；
  `app/api` 生成目录未被修改。
- 验证通过：相关表单与 i18n 定向测试 15/15，ClientApp 完整 `bun test` 131/131、
  `bun run typecheck`、`bun run build` 和 `git diff --check`；构建只保留既有大 chunk、插件耗时与第三方
  trailing-slash deprecation 警告。
- 尚未推送、部署或操作生产数据；最终键盘、读屏标签与四类表单交互将在 Microsoft Edge 统一验收。

## 2026-08-11 全量审计修复：GameplayFact 权威顺序与血榜幂等

- 功能提交 `86cc092f` 让 GameplayFact 的裁决顺序由 `(OccurredAt, Id)` 决定，而不是由 Wolverine 消息
  到达或 Worker 抢占顺序决定。CTF FlagAttempt 在 CompetitionChallenge 行上按题目串行；其余事实按
  Team 行与比赛/题目/Kind 作用域串行。任一重复或乱序消息只负责触发 drain，不再改变权威先后。
- 每个作用域至多存在一个 Processing；完成后自动派发下一条最早 Queued，AWDP 正常回调、超时和
  Runtime 配置失败也会继续排空。PriorFacts 查询收窄为同队、同题、严格早于当前且 Correct 的必要事实，
  不再为每个裁决物化整场永久事实历史。现有 AWD 同一 checker sequence 后写覆盖语义保持不变。
- 功能提交 `766bceb5` 使结果未变化的 `Correct → Correct` 重判不再重复追加 BloodAwarded 消息或
  First/Second/ThirdBloodAwarded 永久事件；普通首次正确与 `Wrong → Correct` 仍只产生一次奖励副作用。
- 本阶段没有 HTTP/OpenAPI、生成 SDK、数据模型、数据表、migration、snapshot 或版本号变化。
- 验证通过：真实 PostgreSQL 权威顺序测试 5/5（逆序、同时间 UUID tie-break、跨队并发一二三血、
  1,000 条无关历史、无变化重判）；非 Integration TUnit 629/629；完整 Release build 0 警告/0 错误；
  `git diff --check` 通过。
- 尚未推送、部署或自动改写生产历史。`Correct → Wrong → Correct` 如何表达旧血榜恢复，以及已有乱序/重复
  奖励历史是否纠正，仍等待 grilling；不会删除或改写不可变 CompetitionEvent。

## 2026-08-11 全量审计修复：通知历史精准深链

- 功能提交 `8dd7a9aa` 修复消息中心只在已加载的前 50 条通知中查找 URL 里的 `notification`，导致旧消息
  深链静默打不开的问题。列表未包含目标时，前端现在直接调用生成 SDK 的线程读取接口，以 URL 中的
  notification ID 取得经后端鉴权的完整线性线程，再选择精确消息并展示后续状态。
- 深链读取期间显示详情骨架；通知不存在、已不可见或请求失败时显示可理解错误，不清空通知列表，也不会
  回退成“请选择消息”。快速切换 URL 继续使用 latest-request 守卫，旧线程响应不能覆盖新选择。
- 本阶段没有 HTTP/OpenAPI、生成 SDK、数据模型、数据表、migration、snapshot 或版本号变化；没有手写
  URL、端点路径或 DTO。
- 验证通过：通知路由与 i18n 定向测试 18/18，ClientApp 完整 `bun test` 130/130、
  `bun run typecheck`、`bun run build` 和 `git diff --check`。仅保留既有构建警告。
- 尚未推送、部署或操作生产数据；实际旧消息 URL 将在 Microsoft Edge 最终验收。

## 2026-08-11 全量审计修复：语言切换原地响应

- 功能提交 `f206bc95` 移除根 `NuxtPage` 的 locale key；切换中文/英文不再销毁并重建当前路由，
  因此不会产生页面黑屏，也不会丢失未提交表单、筛选条件、滚动位置或在途组件状态。
- 功能提交 `b77638c8` 将管理格式、比赛配置选项、血榜标签、咨询角色/失败信息、题库规格、事件筛选、
  首页模式卡、编辑器默认标签和 ECharts 趋势等模块级翻译改为渲染/调用时求值；语言状态变化后标签会在
  原组件中立即更新，不再依赖整页重挂载。咨询页的裸中文题目插值也已改为参数化翻译并补齐英文资源。
- 本阶段没有 HTTP/OpenAPI、生成 TypeScript SDK、数据模型、数据表、migration、snapshot 或版本号变化；
  `app/api` 生成目录未被手工修改。
- 合入统一修复分支后验证通过：ClientApp `bun test` 130/130、`bun run typecheck`、
  `bun run build` 和 `git diff --check`。生产构建只保留既有大 chunk、插件耗时与第三方
  trailing-slash deprecation 警告；仓库仍没有 lint 脚本。
- 尚未推送、部署或操作生产数据；最终中英双向切换和页面状态保持将在 Microsoft Edge 统一验收。

## 2026-08-11 全量审计修复：咨询历史查询有界化

- 功能提交 `e1654323` 保留现有 append-only `notifications`、`reply_to_id` 与 `ContentJson` 模型，
  将咨询列表改为 PostgreSQL 先按当前线程状态、角色可见范围、题目/主题筛选、最后活动时间排序并限页，
  再用一次递归集合查询加载本页线程；用户、队伍、题目标题和比赛限额也按本页批量装配，不再为每个历史
  咨询执行递归 CTE 和多组详情查询。
- 队伍活跃咨询计数现在用单次集合查询投影每个 root 的最终状态，只统计 `Pending` / `Replied`；
  `Resolved` / `Closed` 永久历史不会再被逐根加载。队伍 advisory lock 与事务内原子限额检查保持不变。
- 新增真实 PostgreSQL 长历史回归：250 条已关闭咨询下，筛选后的 20 条列表保持不超过 10 次读取，
  新咨询的活跃计数与完整创建流程保持不超过 14 次读取；同时验证 Manager、Observer、题目 Owner 与
  非关联题目 Owner 的权限边界和当前状态筛选。
- 本阶段没有新增表、列、投影实体、migration 或 snapshot，也没有 HTTP/OpenAPI、生成 SDK 或版本号变化；
  咨询正文仍只存在私有线程，未复制到公开比赛事件。
- 验证通过：CompetitionQuestionLimits PostgreSQL 4/4、完整私有对话 PostgreSQL 1/1、Raw SQL 架构
  约束 1/1、完整 Backend Release build 0 警告/0 错误、格式和 `git diff --check` 通过。
- 尚未推送、部署或操作生产数据；前端超过 100 条咨询的游标导航仍是后续独立协议阶段。

## 2026-08-11 全量审计修复：Runtime 日志、就绪探针与 Kubernetes 身份隔离

- 功能提交 `fd17a3ca` 为平台创建的 Docker Container 与 Compose 服务统一注入 `local` 日志驱动，
  默认每容器轮转 `10 MiB × 3`；一次性 Checker/Fix 任务的 stdout、stderr 分别最多保留 1 MiB，超出内容
  仍会持续读取以避免阻塞，但不会无界驻留内存。限制可以通过现有 Runtime 配置覆盖，日志正文不会写入
  平台诊断日志。
- 功能提交 `7c13ff69` 将进程存活与角色就绪分开：`/health/live` 只检查进程，`/health/ready` 使用 2 秒
  有界检查验证对应角色的 PostgreSQL、Wolverine、Redis 和实际 Runtime Provider。API 的 Redis 故障按
  降级报告；Worker 新增独立健康端口；Docker Compose 与 Kubernetes 清单已全部改用正确探针。
- 功能提交 `d6247e02` 让 Kubernetes 单容器 Runtime 与 Compose Runtime 保持相同身份隔离，显式设置
  `automountServiceAccountToken=false` 和 `enableServiceLinks=false`，题目容器不再默认取得集群 API
  凭据或平台服务环境变量。
- 本阶段没有 HTTP/OpenAPI、生成 TypeScript SDK、业务数据模型、数据表、migration、snapshot 或版本号
  变化；没有改变仍待 grilling 的回调网关、DenyAll、镜像 digest、非 root、SMTP、PID 或端口语义。
- 验证通过：完整 Backend Release build 0 警告/0 错误；定向单元/架构测试 58/58；真实 Docker 测试
  3/3（输出截断、镜像拉取、日志限制、Compose 与资源清理）；Kubernetes Container 生命周期 24/24；
  两份 Docker Compose 清单解析和 `git diff --check` 通过。
- 尚未推送、部署或操作生产数据。

## 2026-08-11 全量审计修复：前端故障态与浏览器存储韧性

- 功能提交 `6714e5eb` 新增浏览器存储安全适配层；`localStorage` 对象访问、读取或写入抛出异常时，
  通知未读标记和咨询已读状态会降级到当前会话内存，不再让页面挂载或成功的数据加载被浏览器隐私策略
  误报为失败。
- 功能提交 `88fb6bf4` 修复参与者 Runtime 与题目排行榜的错误空态。只有明确的 404 才表示环境尚未启动；
  认证、服务端、网络和数据异常会保留已有事实、显示可理解错误并提供重试，且不会暴露无效的启动/停止
  操作。排行榜现在区分 202 投影中、成功和真实失败，失败时不再伪造基础分或 0 解出。
- 功能提交 `04971472` 统一修复平台审计、平台日志、比赛提交、Runtime、队伍申诉、比赛/平台导出、
  题目 Flag 与 Hint 等管理列表的失败呈现；刷新或筛选失败时保留最后一次成功数据并持续显示错误，
  不再把 5xx/网络故障显示为“暂无数据”。游标分页的 `preserveItems` 语义也有回归测试锁定。
- 本阶段没有 HTTP/OpenAPI、生成 TypeScript SDK、数据模型、数据表、migration、snapshot 或版本号变化；
  `app/api` 生成目录未被手工修改。
- 合入统一修复分支后验证通过：ClientApp `bun test` 128/128、`bun run typecheck`、
  `bun run build` 和 `git diff --check`。生产构建只保留既有大 chunk、插件耗时和第三方
  trailing-slash deprecation 警告。
- 尚未推送、部署或操作生产数据；浏览器故障注入和快速路由切换将在全部前端阶段收口后使用
  Microsoft Edge 统一验收。

## 2026-08-11 全量审计修复：前端请求一致性与依赖安全

- 功能提交 `fbf0a4a3` 使 i18n 资源覆盖测试在 Windows CRLF 和仓库 LF 下采用同一语义，消除与产品代码无关的换行误报。
- 功能提交 `aad7d997` 将认证 401 处理改为精确识别匿名认证端点；`me`、个人资料、头像、修改密码、
  全局注销和重发验证邮件等受保护端点现在可以使用有效 Refresh Cookie 单飞刷新，不再被 `/auth/*`
  粗粒度跳过规则误伤。
- 功能提交 `d46b8777` 删除管理端 GameplayFact 筛选中的不安全强制转换，改用生成 SDK 的
  `FlagAttempt`、`BreakAttempt`、`FixAttempt` 协议值，并移除后端不存在的 `PlatformFailed` 结果。
- 功能提交 `73e20c1a` 为作弊事件、咨询、通知和平台用户详情增加 latest-request 守卫；快速切换或关闭
  详情后，旧响应不能覆盖当前选择，也不能让管理员在过期对象上继续执行处置。
- 功能提交 `2c46a42f` 为游标分页增加请求代次；重置筛选后旧响应、旧错误和旧 `finally` 不再污染新页，
  新的首页请求也不必等待已失效请求结束。
- 功能提交 `d8608cbe` 通过 Bun overrides 将间接依赖 `js-yaml` 固定为已修复的 `4.3.1`，保持
  `@hey-api/openapi-ts` 生成链不变；`bun audit` 从两个 High 降为 0。
- 功能提交 `f22f6062` 将题目附件、随机附件、平台日志、平台审计归档、比赛事件和比赛数据归档下载
  全部改为调用生成 SDK 并显式按 Blob 解析。下载现在复用统一认证拦截器和单飞 Refresh Cookie 恢复，
  Access Token 过期时不再因原生 `fetch` 只携带旧内存 Token 而失败；同时移除这些页面的手写 API 路径、
  查询串和重复下载实现。
- 功能提交 `418e31a3` 让认证刷新本身也改用生成 SDK，并在 SignalR 初次连接或自动重连前检查 JWT
  有效期；Token 已过期、将在 30 秒内过期或格式无效时，先通过 HttpOnly Refresh Cookie 单飞换取新
  Access Token。比赛实时事件和平台实时日志不再因旧 Token 被自动重连永久卡在断开状态。
- 本阶段没有 HTTP/OpenAPI、生成 TypeScript SDK、数据模型、数据表、migration、snapshot 或版本号变化；
  `app/api` 生成目录未被手工修改。
- 验证通过：ClientApp `bun test` 112/112、`bun run typecheck`、`bun run build`、`bun audit`（0 漏洞）
  和 `git diff --check`。生产构建仅保留既有大 chunk 和第三方 deprecation 警告。
- 修复位于隔离分支 `codex/fix-audit-findings-20260811`；尚未推送、部署或操作生产数据。浏览器动态验收
  将在所有前端修复收口后统一使用 Microsoft Edge 执行。

## 2026-08-11 全量审计修复：附件、管理员与提示解锁安全边界

- 功能提交 `6755e3c8` 将全局题库附件的写权限检查移到文件暂存、哈希和对象存储登记之前；无权限用户
  对任意 Challenge UUID 发起上传时不会先消耗临时磁盘或对象存储。最终建立附件引用时仍再次授权并
  保留既有失败补偿，避免权限在上传期间变化造成 TOCTOU 绕过。
- 功能提交 `16fe394e` 在 PostgreSQL 事务内串行化平台管理员角色降级；最后一名 Active Human
  Administrator 无论自降级、被他人降级或与另一管理员并发互降级，都返回稳定冲突码
  `LastAdministratorProtected`。Bot 不计入可保留管理员，成功变更仍递增 TokenVersion。
- 功能提交 `8ae967ca` 在提示解锁接入事务、GameplayFact 初次异步评估和完成事务复评三个位置统一
  要求比赛为 `Running`；Draft、Visible、Published、Paused、Finished 均拒绝，排队后暂停或结束也不会
  继续扣分并解锁提示。
- 管理员冲突码已由 OpenAPI 工具导出并重新生成 TypeScript SDK；其余两项没有 HTTP 契约变化。三项均
  未新增数据表、列、migration 或 snapshot。
- 原独立阶段验证通过：Release build 0 警告/0 错误；非 Integration TUnit 629/629；最后管理员与提示
  生命周期 PostgreSQL 定向集成测试各 1/1；Platform Bot 协议测试 4/4；前端 typecheck、OpenAPI 导出、
  SDK 二次生成和 `git diff --check` 均通过。合入统一修复分支后再次验证 Release build 0 警告/0 错误，
  ClientApp typecheck 通过。
- 尚未推送、部署或修改生产数据；合法上传本身的大小上限仍等待 grilling 决策，未借本阶段擅自改变
  `docs/api-conventions.md` 的现行无限请求契约。

## 2026-08-11 全量审计修复：CTF 分值表达式全区间验证

- 功能提交 `0a15357c` 修复 CTF 自定义分值表达式只验证 `solveCount=0`、`1` 和参赛队总数、
  因而漏过中间解题人数除零或溢出的缺陷。保存比赛/题目配置时现在会验证从 `0` 到
  `eligibleTeamCount` 的每个可达整数值，避免比赛进行到中间解题人数后使整场排行榜投影失败。
- 同时拒绝负数 `eligibleTeamCount`，并补充中间奇点拒绝与默认表达式全区间通过测试。
- 本阶段没有 HTTP/OpenAPI、TypeScript SDK、数据模型、数据表、migration、snapshot 或版本号变化。
- 验证通过：
  - `dotnet test backend/tests/NoCTF.Tests/NoCTF.Tests.csproj -c Release --no-restore --treenode-filter "/*/*/CtfScoreExpressionTests/*"`：5/5；
  - `dotnet test backend/tests/NoCTF.Tests/NoCTF.Tests.csproj -c Release --no-restore --treenode-filter "/*/*/ChallengeConfigurationCatalogTests/*"`：46/46；
  - `dotnet build backend/src/NoCTF.GameModes/NoCTF.GameModes.csproj -c Release --no-restore`：0 警告、0 错误；
  - `git diff --check`：通过。
- 该提交只保证以后保存的表达式安全；尚未自动修改生产配置或历史排行榜。后续继续处理
  GameplayFact 稳定时序、重判副作用幂等及其历史差异预览，禁止直接改写不可变比赛事件。

## 2026-08-11 Alpha.25 AWD、AWDP 与 KoH 运行链路恢复

- 功能提交 `4cf4f6e2cf59d8c3accb19429a2d0146f2ca3ca9` 基于协作者最新 `main`
  `b7e49b4c29a45e967638d4eb4d310d9af53bae6f` 制作；协作者的终端风格界面和分区式 Runtime
  编辑器完整保留。发布提交只包含 6 个生产后端文件和 `backend/Directory.Build.props` 版本标记，
  没有包含隔离测试分支中的 E2E、测试夹具、测试断言、测试 Dockerfile 或测试编排器。
- AWDP 内部 JWT 权限与 GameplayFact 迁移后的契约重新对齐：下载与回写使用
  `gameplay_fact_id`，不再要求已废弃的 `submission_id` / `processing_version`；Fix 工作读取从不可变
  `files` 元数据取得归档文件名、长度与 SHA-256，避免继续读取已收敛的上传冗余字段。
- AWD 自动环境创建不再在逐队事务中提前退出并遗留待发送消息；已应用的变更统一在循环后刷新
  Wolverine outbox，同时保留比赛状态和容量上限的强类型终态。GameplayFact 处理不再在事务提交后
  重复强制刷新 outbox，并以强类型生命周期 payload 计算 AWD 有效运行时间，恢复暂停/继续后的轮次
  计分。Docker 一次性任务允许容器在首次检查前已正常退出，KoH/Checker/Fix 等快速任务不再被误判为
  “未进入 Running”。
- 平台版本由 `0.1.0-alpha.24` 递增为 `0.1.0-alpha.25`。本阶段没有 HTTP/OpenAPI 契约、生成
  TypeScript SDK、数据模型、数据表、列、migration 或 snapshot 变化。
- 隔离测试分支上的相同生产修复此前已完成 AWD、AWDP、KoH 三种模式完整 E2E，后端合计 762 通过、
  2 个外部环境项按设计跳过、0 失败。合并最新 `main` 后重新验证：Release build 0 警告/0 错误；
  非 Integration TUnit 627/627；`dotnet format ... analyzers --verify-no-changes`、EF pending-model
  changes、ClientApp `typecheck`、`generate`、production `build` 和 `git diff --check` 通过。
  ClientApp 84 项测试中 83 项通过；唯一失败是既有 `i18n.test.ts` 在 Windows 工作树中按字节要求 LF，
  而 Git checkout 为 CRLF，实际 `ThemeToggle` 与 `LanguageToggle` 顺序正确。遵照“不要混入测试代码”
  的发布要求，没有移植隔离分支中仅放宽换行判断的测试改动。
- 一次全量真实依赖复验因 Docker 重置后重新拉取/启动依赖超过 300 秒门限而终止；精确留下的两个
  `it-36410...` 测试容器和三张 `noctf-platform-it-*` 测试网络已逐一删除。未执行全局 prune，最终
  本机测试容器、测试网络和数据卷均为零。
- 用户明确授权后，协作者提交 `b7e49b4c`、功能提交 `4cf4f6e2` 和部署前 HANDOFF 提交
  `b0e1fcb2` 已快进推送到远程 `main`；发布差异自动检查未包含任何隔离分支测试文件。生产
  `/root/NoCTF` 从 `4e2f03d9` 通过带先决提交校验的 60,190 字节 Git bundle 快进到
  `b0e1fcb2`。Bundle SHA-256 为
  `2551c191033f1e719bb6c9f8ddf8ffb3f59d892c0f0482ba371e2afe202f8f46`；683,999,232 字节镜像归档
  SHA-256 为 `680d69636d304af0d7a9ac5a900b2ff22c82fb705535090554fb91b2b542f557`，本地和服务器端一致。
- Alpha.25 API/Migration、Worker、Runner 的 `linux/amd64` 镜像 ID 分别为 `b4600bc4dfa0`、
  `ec1b263c62b4`、`8a1e63c6ef1a`，OCI revision 均为 `b0e1fcb2c79cee128cdeca79c77ed9b35a0d7f18`，
  镜像和三个运行中程序集均确认包含 `0.1.0-alpha.25`。Migration 明确报告数据库已是最新；随后只以
  `--no-build --no-deps` 重建 API、Worker 和 Runner，没有重建 PostgreSQL/Redis，也没有修改上传卷、
  HTTPS 证书、题目镜像、Runtime 或比赛事实。
- 稳定后 API/Runner healthy、Worker running，三个服务 restart count 均为 0；外网 HTTPS `/`、
  `/auth/login` 和 `/health` 均返回 200。10 分钟部署日志中 Error、Exception、Fatal、Unhandled、
  OutOfMemory 和 ArgumentNullException 均为 0；Warning 仅为既有 DataProtection 临时密钥与 Hosting
  端口覆盖类别。服务器端精确传输目录和 Alpha.23 三个旧服务镜像已删除，Alpha.24 保留为即时回滚；
  未执行全局 Docker prune。根分区最终恢复到 68% 使用、约 14 GB 可用。

## 2026-08-10 Alpha.24 语言切换黑屏修复

- 功能提交 `be97a8ec` 修复页头语言按钮短暂黑屏。根因是 `useLocale().switchLocale()` 在更新共享语言状态和
  `localStorage` 后仍调用 `window.location.reload()`，导致浏览器卸载整套 Nuxt SPA，并在重新下载、解析和
  挂载静态资源期间露出空页面底色。
- 语言切换现在只更新现有响应式 locale，不再触发文档导航或整页刷新；根 `NuxtPage` 以 locale 为 key
  在当前 SPA 内重新挂载当前路由，使 `<script setup>` 中构建的静态选项和普通 `$t()` / `translate()`
  消费者在同一页面内统一重算。URL、布局、认证会话和浏览器文档保持不变，切换过程中不再出现黑屏。
- 平台版本由 `0.1.0-alpha.23` 递增为 `0.1.0-alpha.24`。本阶段没有 HTTP/OpenAPI 契约、生成 TypeScript
  SDK、数据模型、数据表、列、migration 或 snapshot 变化，生成 API 目录确认无差异。
- 新增 locale 响应式更新与“不得调用 `window.location.reload()`”回归，并锁定根页面按 locale 重挂载。
  验证：ClientApp `bun test` 84/84、`bun run typecheck`、`bun run build` 均通过；
  `dotnet build backend/NoCTF.slnx --no-restore -m:1` 为 0 警告/0 错误，`git diff --check` 通过。构建只保留
  既有大 chunk、插件耗时及第三方 trailing-slash deprecation 警告；仓库仍无 lint script。
- 本地 production preview 浏览器验收通过：中→英和英→中均保持 `http://127.0.0.1:3418/`，`html lang`
  与页头、首页模式文案同步更新，控制台无 Warning/Error；验收临时服务与标签页已清理。
- 用户明确授权后，功能提交 `be97a8ec` 与本阶段原 HANDOFF 提交 `ff953a2d` 已从 `ddb3eb4c`
  无冲突快进推送到远程 `main`；`git ls-remote` 核对远程提交为
  `ff953a2d3d12471ed110460f5555b418c3bd4f95`。生产 `/root/NoCTF` 通过带先决提交校验的增量
  Git bundle 快进到同一提交；既有未跟踪 `deploy/docker-compose.prod.yml`、`.env`、HTTPS 证书、
  PostgreSQL/Redis/上传卷、题目镜像和 Runtime 均未覆盖或修改。
- Alpha.24 Git bundle 为 2,917 字节，SHA-256
  `10f4de5742c60ed9b66fdca0d903007455315d4d4bcb1c6a9fc4e414e86496e5`；三镜像归档为
  681,565,184 字节，SHA-256
  `0d47cbf272f5e596e9364ecb04b9ab56d66dd3d74e61a4a05cbd9a6917c96730`，本地与服务器校验一致。
  API/Migration、Worker、Runner 的 `linux/amd64` 镜像 ID 分别为 `6d118d62dd23`、
  `bcf0941a8bb1`、`ed561f130c63`，镜像内程序集均包含 `0.1.0-alpha.24`。
- Migration 容器明确报告数据库已是最新，没有应用 schema 变化；随后只以 `--no-build --no-deps`
  原地重建 API、Worker 和 Runner。API 与 Runner healthy、Worker running，三者 restart count 均为
  0；外网 HTTPS `/` 与 `/health` 均返回 200，生产首页已加载 Alpha.24 静态产物和语言首屏恢复脚本。
- 生产内置浏览器能够读取新版无刷新语言按钮，但点击后的自动化状态读取因浏览器控制内核超时重置，
  因此没有伪造生产交互结论；完整中英双向切换已在本地 production preview 实际验收通过。部署后的
  API 启动期间 Wolverine 成功接管旧节点后，向已退出节点发送停止命令的确认等待在 60 秒后记录一次
  瞬时 timeout；该时间点之后的 API、Worker、Runner 日志未再出现匹配的错误签名，服务持续稳定。
- 已校验并删除服务器精确传输目录 `/root/noctf-deploy-alpha24-ff953a2d`，移除未被容器引用且不再作为
  即时回滚版本的 Alpha.21 三个服务镜像；没有执行广域 Docker prune。保留 Alpha.23 为即时回滚：
  API/Migration `36ca335e6423`、Worker `a995edb3b860`、Runner `8a54ce1ea688`。根分区恢复为
  68% 使用、约 14 GB 可用；数据库、Redis、上传文件、题目镜像、Runtime 和比赛事实均未删除或修改。

## 2026-08-10 Alpha.23 中英文界面适配

- 基础提交 `230386f2` 建立轻量的强类型语言层，功能提交 `2c74f31a` 完成参与者端、比赛管理、
  题库管理、平台管理、认证、账户、通知、运行时和排行榜等现有界面的中英文适配。中文仍是无偏好时
  的默认语言；首次访问识别浏览器语言，之后把明确选择保存到 `noctf-locale`。切换时同步更新
  `document.documentElement.lang`，日期和数字使用 `zh-CN` / `en-US`，不会把队伍名、题目名、用户名、
  Flag、邮箱等用户数据当作翻译键。
- 全局页头在白日/黑夜主题切换右侧新增紧凑语言按钮，中文界面显示 `EN`，英文界面显示 `中`；
  图标、ARIA 文案和主题按钮本身也会随当前语言切换。Nuxt 启动前脚本在首屏渲染前恢复语言，避免
  刷新时先显示错误语言再闪烁；页面脚本复用同一 `translate()` / `$t()` 入口，没有引入第二套路由、
  手写 API URL 或服务端会话状态。
- 英文资源当前包含 1,444 个界面条目，自动覆盖扫描确认 1,431 个已使用中文源键全部存在英文值，
  英文资源值不含遗留汉字。动态错误、状态、权限角色、比赛模式、血榜、通知、咨询、作弊处置、
  Runtime 和管理表单均走参数化翻译；源文本中的队伍、题目和其他开放文本只作为插值保留。
- 平台版本由 `0.1.0-alpha.22` 递增为 `0.1.0-alpha.23`。本阶段没有 HTTP/OpenAPI 契约、生成
  TypeScript SDK、数据模型、数据表、列、migration 或 snapshot 变化，因此没有重新生成 SDK 或 EF
  migration；检查确认生成 API 目录无差异。
- 验证：ClientApp `bun test` 为 83/83（含 6 项语言切换、插值、区域格式、资源覆盖、页头顺序和
  持久化回归），`bun run typecheck` 与 `bun run build` 通过；`dotnet build NoCTF.slnx --no-restore`
  为 0 警告/0 错误，`git diff --check` 通过。仓库仍没有 lint script/ESLint 配置，`bun run lint`
  明确报告脚本不存在，未伪造 lint 结果；Nuxt 只保留既有大 chunk、插件耗时及第三方
  trailing-slash deprecation 警告。
- Edge 对本地 production preview 的实际验收通过：英文页头显示 `Competitions` 和“切换为中文”，
  中文页头显示“竞赛”和“切换为英文”；两次切换后 `html lang` 分别为 `en` / `zh-CN`，刷新后中文
  选择保持，主题按钮与语言按钮顺序正确，浏览器控制台无新增 Warning/Error。验收临时进程和标签页
  已清理。
- 用户明确授权后，Alpha.22–Alpha.23 的 5 个提交已从 `cebfef4d` 无冲突快进推送到远程 `main`，
  部署基线为 `9f3f5a0d`。生产 `/root/NoCTF` 通过要求先决提交 `cebfef4d` 的增量 Git bundle 快进到
  同一提交；既有未跟踪 `deploy/docker-compose.prod.yml`、`.env`、HTTPS 证书、PostgreSQL/Redis/
  上传卷和题目镜像均未覆盖。681,564,160 字节镜像归档 SHA-256 为
  `5e31bc8d5e44f7c8cbfe6a16d23eb5364b0031a44fef03fb94ebd0652f1914bc`，113,397 字节 Git bundle
  SHA-256 为 `787c356999e40f14101ee5360a0489e4a417a19e0b20150a4fc19cde4dabf61b`；本地与服务器校验一致。
- Alpha.23 API/Migration、Worker、Runner 的 `linux/amd64` 镜像 ID 分别为
  `36ca335e6423`、`a995edb3b860`、`8a54ce1ea688`，镜像内程序集均包含 `0.1.0-alpha.23`。迁移容器
  明确报告数据库已是最新，没有应用 schema 变化；随后只以 `--no-build --no-deps` 原地重建 API、
  Worker 和 Runner，没有重建 PostgreSQL/Redis。API 与 Runner healthy、Worker running，三者
  restart count 均为 0；外网 HTTPS `/` 与 `/health` 均返回 200，部署后服务日志未发现 fail、crit、
  Fatal、Unhandled、ArgumentNullException、OutOfMemory 或 error。
- 生产首页 HTML 已确认包含语言首屏恢复脚本；其 48 个预加载 JavaScript 资源均可从 HTTPS 正常读取，
  并包含 `noctf-locale`、`Switch to Chinese`、“切换为英文”、`Competitions` 和“竞赛管理”等 Alpha.23
  标识。Edge 扩展能发现生产标签页，但两次读取生产 DOM 均超时并重置连接，因此没有伪造生产视觉
  交互结果；本节上方记录的本地 production preview 双语交互验收仍完整通过。
- 部署校验后删除了精确传输目录 `/root/noctf-deploy-alpha23-9f3f5a0d` 和不再作为即时回滚版本的
  Alpha.20 三个服务镜像；没有运行广域 Docker prune。保留的即时回滚镜像是 Alpha.21
  `rollback-cebfef4`：API/Migration `0e32e54ae2ff`、Worker `8d0967a03b8f`、Runner
  `d91cfcf7f90e`。根分区从加载镜像后的 75% 恢复到 68%，约 14 GB 可用；数据库、Redis、上传文件、
  题目镜像、Runtime 和比赛事实均未删除或修改。

## 2026-08-10 Alpha.22 裁判处理封禁申诉

- 功能提交 `0e842531` 修复封禁申诉仍错误复用 `CanModerateAsync` 的权限缺口。申诉列表现在用
  `CanJudgeAsync` 计算每条待决申诉的 `canResolve`，接受申诉与维持封禁端点也使用同一裁判授权；
  Judge、Manager、Owner 和平台 Administrator 均可完成两种裁决，Observer 仍只能读取私有申诉。
- 管理端申诉卡片由 `canJudge` 控制处置按钮，因此裁判能看到并调用生成 SDK 完成“接受申诉（解封）”
  或“维持封禁”。普通解封、无申诉纠正封禁、报名审批和配置写入继续受 `canWrite` / `CanModerateAsync`
  约束，没有扩大裁判的其他权限。
- 新增真实 FastEndpoints HTTP 回归，验证 Judge 列表返回 `canResolve=true` 且 Accept/Uphold 各只执行
  一次，Observer 列表返回 `canResolve=false` 且处置得到 403；前端回归同时锁定裁判申诉按钮与管理者
  专属反向操作的边界。平台版本递增为 `0.1.0-alpha.22`。
- OpenAPI 描述已从后端重新导出，`swagger.json`、`wwwroot/openapi/v1.json` 与 TypeScript SDK 均由
  生成工具更新并通过二次生成 SHA-256 幂等检查；没有新增或修改路由、请求/响应结构、数据表、列、
  migration 或 snapshot。EF CLI 明确报告模型无 pending changes。
- 验证：`dotnet build backend/NoCTF.slnx --no-restore -m:1` 为 0 警告/0 错误；新增端点测试 2/2、
  非 Integration 627/627、现有 TeamBanAppeal PostgreSQL Testcontainers 1/1 通过；ClientApp
  `bun test` 77/77、`bun run typecheck`、`bun run build`、`git diff --check` 全部通过。仓库仍无 lint
  script/ESLint 配置；Nuxt 仅保留既有大 chunk、插件耗时与第三方 trailing-slash 警告。
- 本阶段尚未推送或部署，生产仍运行 `0.1.0-alpha.21`。没有使用真实申诉做破坏性浏览器验收；获得
  明确推送部署授权后，应使用可丢弃的待决申诉和 Judge 账号分别复核接受与维持封禁两条路径。

## 2026-08-10 Alpha.21 消息中心、赛事播报与管理角色修正

- 功能提交 `8cd8ef63` 重新划分比赛信息的三个前端入口。比赛「动态」继续投影按权限过滤的完整
  `competition_events`；全局「消息中心」改用新增的强类型 `scope=Inbox`，只展示手工发布的官方
  公告，以及与当前账号、队伍或管理职责直接相关的消息；系统自动投递给全部参赛者的公开赛事播报
  不再混入个人收件箱。原比赛内「公告/通知」标签被移除，旧路由保留为到全局消息中心的重定向。
  默认 `scope=All` 与 `/notifications/feed` 仍保留完整动态受众语义，现有 BOT 消费不受影响。
- 题目页右侧新增紧凑的「赛事播报」窗口，直接查询既有不可变比赛事件，只展示一血、二血、三血、
  作弊封禁、申诉纠正、提示发布、题目描述更新和题目开放；卡片按题目或队伍落到对应页面，并通过
  既有实时事件失效刷新。为补齐真实事实链，Domain/Application/Infrastructure/API 协议追加
  `ChallengeDescriptionUpdated`，作弊确认产生幂等的公开 `TeamBanned` 事件；事件列表支持强类型
  `kinds[]` 查询。没有复制播报数据，也没有新增 read-state、broadcast 或其他业务表。
- 两个账号收到相同通知的根因是公开自动化通知使用 `CompetitionParticipants` 动态受众，旧前端又把
  这些公共信息描述成“你的队伍”。Inbox 现在只允许 `SourceType=User` 的人工官方公告继续面向全部
  参赛者显示，`SourceType=System` 的公开自动播报由题目页比赛事件投影承担；直接用户、队伍、工作
  人员和平台管理员消息继续保持各自动态可见性。
- 修复 Fa1lSnow 等 Judge 在竞赛管理列表被显示为 Observer。旧前端通过 1970–2999 的作弊查询探测
  权限，超过后端 31 天范围后请求失败并静默回退为 Observer。管理比赛列表现在和详情接口一致，直接
  返回强类型 `administrationRole`（Owner/Manager/Judge/Observer），前端不再探测或猜测权限；Judge
  会稳定显示为「裁判」。
- OpenAPI 已由后端重新导出，`swagger.json`、`wwwroot/openapi/v1.json` 和 TypeScript SDK 均由生成
  工具更新，没有手写 URL、DTO、枚举或端点路径。平台版本由 `0.1.0-alpha.20` 递增为
  `0.1.0-alpha.21`。EF CLI 确认模型无 pending changes，本阶段没有新增或修改 migration、snapshot、
  数据表或列。
- 验证：`dotnet build backend/NoCTF.slnx --no-restore -m:1` 为 0 警告/0 错误；非 Integration
  625/625；强制 Docker/Testcontainers Integration 132 通过，Kubernetes/Libvirt 两项按既有外部
  配置门禁跳过；ClientApp `bun test` 77/77、`bun run typecheck`、`bun run build` 全部通过；EF
  model drift 与 `git diff --check` 通过。仓库仍没有 lint script/ESLint 配置，因此未伪造 lint 结果；
  Nuxt 只保留既有大 chunk、插件耗时和第三方 trailing-slash 警告。
- 用户明确授权后，`8cd8ef63` 与 `cd18dfd8` 已无冲突快进推送到远程 `main`。生产
  `/root/NoCTF` 通过先决提交为 `b757a254` 的增量 bundle 快进到 `cd18dfd8`；既有未跟踪
  `deploy/docker-compose.prod.yml`、`.env`、HTTPS 证书、PostgreSQL/Redis/上传卷及题目镜像均未
  覆盖。681,527,808 字节镜像归档 SHA-256 为
  `7935ebc45eefdd75f7366705261d2169c86808969a633e91b495b5fc3c969ff7`，Git bundle SHA-256 为
  `00c10bcddca2e4d7b78a2fab5a94a0fb45aa5d4c03a1de835788666f232a0401`；本地与服务器端校验一致。
- Alpha.21 API/Migration、Worker、Runner 镜像 ID 分别为 `0e32e54ae2ff`、`8d0967a03b8f`、
  `d91cfcf7f90e`，均为 `linux/amd64`，程序集包含 `0.1.0-alpha.21`。旧 Alpha.20 镜像保留
  `rollback-b757a254` 精确标签；Migration 容器报告数据库已是最新，没有应用 schema 变化。API、
  Worker、Runner 重建后 API/Runner healthy、Worker running，restart count 均为 0；外网 HTTPS
  `/`、`/health`、`/notifications`、管理比赛列表和目标题目页均返回 200，新静态产物包含「赛事播报」。
- API 启动时 Wolverine 检测到旧 leader 并尝试停止已退出节点，等待确认 60 秒后记录两条瞬时 Timeout
  fail；10 秒后新节点成功接管数据库代理，后续完整观察窗口 API/Worker/Runner 均无新增 fail、crit、
  Fatal、Unhandled、ArgumentNullException 或 OutOfMemory。只读 PostgreSQL 核对 Fa1lSnow 在
  `TEST GAME II` 的角色码为 `2 = Judge`，与新版强类型列表响应一致。浏览器现有页面停在登录页，
  没有 Fa1lSnow 登录会话，因此未重置账号或伪造有会话的视觉验收。
- 部署后已校验并删除精确传输目录 `/root/noctf-deploy-alpha21-cd18dfd8`，同时移除已被 Alpha.20
  回滚镜像取代、未被容器引用的 Alpha.16 三个服务镜像；没有执行广域 Docker prune。根分区从加载
  镜像后的 75% 使用恢复到 68%，约 14 GB 可用。数据库、Redis、上传文件、题目镜像、Runtime 和
  业务事实均未删除或修改。

## 2026-08-10 Alpha.20 GameplayFact/组合宿主同步、数据迁移与生产部署

- 已同步并审阅协作者提交 `f1e5f02c`、`07f98b9a`：新增可组合的 `NoCTF.Hosting` 与
  `NoCTF.Host`，同时保留 API/Worker/Runner 独立进程；Submission 与 ScoringEvent 收敛为单一
  `GameplayFact` 当前权威事实，排行榜由 `Competition.LeaderboardDirty` 驱动全量刷新。Domain、
  Application、Infrastructure、API、OpenAPI 和生成 TypeScript SDK 已同步改名；生产本次继续使用
  既有三进程拓扑，没有把进程合并与数据模型切换同时进行。
- 新 EF CLI 基线为 `20260810002212_InitialBaseline`，业务表从 17 张减为 16 张。协作者明确取消旧
  persistence/messaging 合约兼容，直接在 Alpha.19 数据库运行新基线会因 `files` 等表已存在而失败。
  `6df94001` 因此新增一次性、事务化的 `deploy/data-migrations/alpha19-to-alpha20.sql`，没有新增业务表，
  也没有手改 migration/snapshot。脚本只接受“新基线空目标 + `legacy_alpha19` 源”，按共享列复制 15 张
  既有表，把每条旧 Submission 及其当前 ScoringEvent 合成同 ID GameplayFact，并迁移 Runtime、
  CompetitionEvent、Notification 的强类型引用和 JSON payload；计数、GameplayFact 外键、Runtime/
  Event 引用和旧 payload 键均在提交前验证，失败会回滚整个数据复制事务。平台版本递增为
  `0.1.0-alpha.20`。
- 使用生产只读压缩备份在独立 PostgreSQL 16 容器中重复演练完整流程：重命名旧 schema、应用
  `dotnet ef 10.0.9 migrations script` 生成的基线 SQL、运行数据迁移、启动新 API/Worker。演练保留
  4 用户、2 比赛、4 队伍、4 GameplayFact、84 CompetitionEvent、14 Notification、5 Runtime；
  两场比赛各恢复 1 条作弊投影，排行榜能重建，第二场 `TEST_CHALL` 为 500 当前分 + 25 一血奖励 =
  525。新镜像构建成功；协作者精确提交 `07f98b9a` 的 GitHub 门禁中 restore/audit/build、四种 publish、
  后端测试、analyzer/EF drift 和 Kubernetes 验证均通过，唯一 Compose 失败是 CI 未传
  `EMAIL_VERIFICATION_ENCRYPTION_KEY`，用户已明确延后处理，本提交未混入 CI 修复。
- `6df94001` 已快进推送到远程 `main`，生产 `/root/NoCTF` 通过带先决提交 `3edb0ae6` 的增量 bundle
  快进到相同提交。681,519,616 字节三镜像归档 SHA-256 为
  `4f83cb365fba657261697b55e7032fce45b8a5b272e3d9e89402e8e1040050db`；加载后的 API/Migration、
  Worker、Runner 镜像 ID 分别为 `19c521bbd43a`、`eebd3099c0df`、`71ca9462c5f5`。既有未跟踪生产
  Compose override、`.env`、HTTPS 证书、PostgreSQL/Redis/上传卷均未覆盖。
- 停机前只有一个周期性 `DispatchAwdCheckers` 调度，5 个 Runtime 均为 Stopped，且没有运行中的题目
  Docker 容器。首次 schema 切换因远程 `docker exec psql` 未挂标准输入而没有执行，新基线的非空目标
  保护立即中止；自动回退恢复 Alpha.19 镜像和 HTTPS 200，未复制或修改业务数据。改为显式
  `psql -c` 事务后，生产成功切换 schema、应用新 EF 基线和已演练的数据复制；旧 Wolverine API/
  Worker/Runner/Queue schema 因消息类型不兼容而清空重建，周期维护代理重新生成调度，没有丢弃待发送
  邮件或人工业务命令。
- 部署后 API、Runner healthy，Worker 正常，HTTPS `/health` 返回 200；迁移历史仅为新基线。强类型
  管理登录确认平台版本 `0.1.0-alpha.20`、贡献者 3 人、两场比赛各 1 条作弊事件；排行榜分别恢复
  1000 和 525 的头名分数。最近启动日志没有真实 Fatal/Unhandled/fail，日志筛选只命中 Wolverine
  建表 DDL 的 `exception_*` 列名。Edge 能发现生产标签页，但两次读取页面 DOM 时扩展超时，因此没有
  伪造浏览器交互验收，也没有处置任何真实比赛数据。
- 迁移前后数据库备份已移到本机 `E:\Backups\NoCTF`：Alpha.19 SHA-256
  `60387279de53ace12293c35af3d81de6cd19ea5231815f4d69b8862138668c83`，Alpha.20 SHA-256
  `58132af239416e5e289c2376c1fdfa1c300dd5f631450d7fe877360799d23806`。生产验收后已按用户授权删除
  `legacy_alpha19` 和所有历史回退镜像标签，并精确清理一个 16 小时前的已退出 Bun 构建容器；未执行
  广域 Docker prune。根分区由 82% 降至 68%，约 14 GB 可用，当前仅保留 Alpha.20 服务镜像。

## 2026-08-10 Alpha.19 私有比赛咨询工单

- `343f4ed7`：完成比赛咨询工单化并修复平台/赛事咨询必然失败的根因。创建上下文不再复用语义
  含混的 `HasValidChallenge`，改用明确的 `Missing/Valid/Invalid` 题目引用状态：Platform 只能不带
  `CompetitionChallengeId`，Challenge 必须引用本比赛已发布题目。稳定失败码覆盖比赛状态、队伍资格、
  题目引用、队伍活跃咨询上限、选手连续消息上限、关闭状态和 revision 冲突，前端均提供中文反馈。
- 咨询继续复用现有不可变 Notification、`reply_to_id`、`ContentJson` 和咨询投影，没有新增 conversation、
  message、participant、read-state 或咨询业务表。初始提问、选手补充、工作人员多次回复和状态迁移组成
  一个私有线程；Closed 后终止写入，Resolved 允许选手追问并原子重开为 Pending。正文不写入公开比赛
  事件，永久事件仅保存 questionId、状态、操作人等必要元数据；失败 Warning 只记录 failureCode 及比赛、
  咨询、队伍、用户标识，不记录正文、Token 或凭据。
- 默认限制为每队 5 个 Pending/Replied 活跃咨询、工作人员回复前最多连续 3 条选手消息（初始提问计
  1 条）。PostgreSQL 事务和 advisory lock 以队伍/咨询串行化检查，防止多名队员并发突破上限；
  Handler 回复重置额度，Resolved 追问重新检查活跃上限。跨模式配置直接落在现有 `competitions`
  表：`MaxActiveQuestionsPerTeam=5`、`MaxParticipantMessagesBeforeHandlerReply=3`、
  `AllowChallengeOwnersToHandleQuestions=true`。唯一 migration
  `20260809175649_ConfigureCompetitionQuestions` 由 EF CLI 生成，只增加这 3 列及正值约束，没有新表。
- 权限边界：队伍成员共享本队线程；Judge、Manager、Owner 和平台 Administrator 可处理比赛咨询；
  Observer 只读；题目所有者仅在配置允许时查看和回复自己题目的咨询，不能查看平台咨询或其他题目，
  也不会因此获得 Flag、SMTP、Token 等受保护数据。保留既有题目所有者处理能力，因此配置默认值
  采用 `true`；如产品后续要求 opt-in，可单独调整默认值而无需改表。
- 比赛事件协议补齐 `AnnouncementPublished`、`QuestionOpened`、`QuestionReplied`、
  `QuestionStatusChanged` 的 Domain/Application/Infrastructure/API/OpenAPI/前端筛选全链路。通知读取从
  任意回复节点都会先追溯线程根再展开完整后续记录；全局通知详情和比赛咨询通知精确导航到
  `/competitions/{competitionId}/questions?question={questionId}`，不会再只回比赛首页。
- 参赛者咨询页改为左侧工单列表、右侧完整不可变线程，显示关联题目、状态、未读数、最后回复角色与
  时间、角色区分和当前剩余额度。请求有 loading/重复提交保护，失败保留输入，成功即时追加并刷新；
  达到上限后保留历史并明确提示等待工作人员，Resolved 明示追问会重新打开。未读状态复用浏览器
  本地 read marker，不新增 read-state 表。管理端创建/配置页同步暴露三项强类型比赛配置。
- OpenAPI 已从后端重新导出，`swagger.json`、`wwwroot/openapi/v1.json` 和 TypeScript SDK 由工具
  重新生成；再次导出/生成后三项 SHA-256 保持不变，确认无手工 SDK 漂移。平台版本递增为
  `0.1.0-alpha.19`。
- 验证：`dotnet build backend/NoCTF.slnx --no-restore -m:1` 为 0 警告/0 错误；非 Integration
  631/631；强制 Docker/Testcontainers Integration 158 通过、Kubernetes/Libvirt 外部环境 2 项按
  设计跳过；ClientApp `bun test` 71/71、`bun run typecheck`、`bun run build` 全部通过；EF
  `has-pending-model-changes` 无漂移；OpenAPI/SDK 幂等；`git diff --check` 通过。仓库仍无 lint
  script/ESLint 配置，因此没有伪造 lint 结果。构建仅保留既有大 chunk、插件耗时和第三方
  trailing-slash 警告。
- Edge 一次性本地环境实际验收：Platform + 空题目引用创建成功并进入精确 question URL；初始消息
  显示 2/3，补充两条后输入区改为等待工作人员；平台管理员回复显示正确角色并将选手额度重置为
  3/3；Resolved 追问重开 Pending；通知详情展示从根到当前回复的完整线程，“查看咨询”精确跳回
  question URL。浏览器控制台无新增咨询错误，仅观察到仓库既有 `Toaster` 组件解析 Warning。独立
  PostgreSQL/Redis、测试账号/比赛和本地进程均已清理，未修改生产数据。
- 本阶段功能提交与原 HANDOFF 提交已经用户授权推送并部署；生产结果、镜像、迁移和验收记录见下节。

## 2026-08-10 Alpha.17–Alpha.19 生产部署

- 用户明确授权后，Alpha.17–Alpha.19 共 6 个功能/文档提交已从 `f2144eed` 无冲突快进推送到远程
  `main`，功能与部署镜像基线为 `e64c8bca`。生产 `/root/NoCTF` 通过只包含该范围的增量 Git bundle
  快进到相同提交；既有未跟踪 `deploy/docker-compose.prod.yml`、根目录 `.env`、HTTPS 证书、
  PostgreSQL/Redis/上传卷及运行中的题目容器均未改写。
- 在独立干净 worktree 构建并核对的 `linux/amd64` 镜像为：API
  `sha256:68bc848d34f83b2bdcd9d21b5effae4a3a5f23f2b320a8bec2900e26520a83ad`、Migration
  `sha256:b93335c8053f340df5af8bcd4602ea8bdb7e88b26a4bbd6c2b37cbec261fb308`、Worker
  `sha256:0cdf3f83c82e33cf6e20382926ca009a1095f074ba7a22173bbeed01b7e3f668`、Runner
  `sha256:dc8640c9c5793b26938ab42d6ada7005a1f761b86259c5ebfd6325dc4ccc7092`；程序集包含
  `0.1.0-alpha.19`。681,551,360 字节镜像归档和 82,748 字节 Git bundle 的本地/服务器 SHA-256
  完全一致后才加载。原 Alpha.16 四类镜像保留 `rollback-f2144eed` 标签。
- Migration 容器成功应用 EF CLI 生成的 `20260809175649_ConfigureCompetitionQuestions`：只在
  `competitions` 增加 `max_active_questions_per_team=5`、
  `max_participant_messages_before_handler_reply=3`、
  `allow_challenge_owners_to_handle_questions=true` 和正值约束，没有新增业务表。随后使用生产 `.env`
  与既有双 Compose 文件以 `--no-build --no-deps` 重建 API、Worker、Runner；PostgreSQL 与 Redis
  未重启。API、Runner 为 healthy，Worker 正常，三者 restart count 均为 0。
- 外网 HTTPS `/`、`/health` 返回 200；浏览器式 `Accept: text/html` 下 `/competitions`、
  `/notifications`、`/verify-email`、`/reset-password` 深链均返回 SPA 入口 200。API/Worker/Runner 启动
  后仅出现一次 Wolverine 在旧节点退出期间清理远端 agent 的瞬时超时；服务健康未受影响，后续观察
  窗口未复发。部署传输目录已按精确路径删除，未执行全局 Docker prune；回滚镜像保留。
- 本次生产验收没有创建、回复、关闭或修改真实咨询、比赛、队伍、作弊事实、通知、Flag 或 Runtime。
  完整工单流程已在部署前用 Edge 和可丢弃本地数据验收；若要做生产有状态复核，必须使用明确标记的
  可丢弃比赛，不能消费现有真实咨询或申诉证据。

## 2026-08-08 生产部署与邮箱验证链接热修

- `main@2056bdc1` 已在生产环境以全新 17 张业务表基线部署；Migration 退出码为 0，API、Runner、PostgreSQL、Redis 健康，Worker 正常运行，HTTPS 首页、`/health` 与 Nuxt 静态资源均返回 200。
- `df4335f3`：修复验证邮件链接与 Nuxt 页面路由不一致。新邮件使用 `/auth/verify-email`；页面保留 `/verify-email` 路由别名，因此已投递的旧链接继续有效。
- 验证：`dotnet build backend/NoCTF.slnx --no-restore -m:1` 为 0 警告/0 错误；非集成测试 609/609 通过；`SmtpEmailVerificationDeliveryTests` 4/4 通过；`bun run typecheck` 与 `bun run generate` 通过，生成路由包含 `/auth/verify-email` 及 `/verify-email` alias；`git diff --check` 通过。
- 本热修未修改 HTTP/OpenAPI 契约、数据模型、migration 或 SDK。`c744d155` 已推送并完成生产热更新；旧路径返回 HTML 200，线上入口脚本同时包含 `/auth/verify-email` 与 `/verify-email` alias，最近 5 分钟 API/Worker/Runner 无 `fail:`，EF Information 日志未再增长。

## 2026-08-08 续推记录

- `7f94be24`：合并并精确同步 `new-frontend@5631584f40b1cff9df3c1c16e8b0c76f05f9b972`，保留 17 表新基线与 Nuxt ClientApp。
- `5e41b485`：修复 Nuxt 与合并后强类型契约漂移；Team API 增加生成式 `avatarUrl`，重新导出 OpenAPI 并生成 TypeScript SDK。
- `6a33a6ac`：将邮件验证配置和密码重置持久化测试改写到 `platform_settings` 与统一 `account_tokens`，排除文件由 21 个降至 19 个。
- `ea8c004d`：将用户物理删除/匿名化的操作人、原因、时间和动作写为管理员受众的不可变 Notification 原始事实，管理审计直接投影该事实；修复管理员通知角色判断，并将用户关联 Notification 纳入删除影响预览。对应两项集成测试已恢复，排除文件由 19 个降至 17 个。
- `15d6fc50`：将 Competition Event、Lifecycle、Leaderboard Visibility 共 6 个持久化场景改写到不可变 `competition_events`，排除文件由 17 个降至 14 个；同时修复暂停生命周期 payload 使用默认大小写敏感 JSON 选项导致无法恢复暂停起点的问题。
- `0ee346b8`：恢复作弊事件、队伍封禁申诉和私密问答 3 个持久化场景，排除文件由 14 个降至 11 个；Event 记录器会把未被选为 Subject 的用户/队伍/比赛题目等首要引用写入 Related，并拒绝不成对的显式 Related 字段。问答测试按当前产品语义移除旧公开投影，公开答复继续由 Hint 或比赛公告承担。
- `87fb8e1c`：恢复 AWDP Fix admission 与 Hint unlock 两个持久化场景，排除文件由 11 个降至 9 个；Patch 测试使用统一不可变 File 引用，Hint unlock 测试验证异步 Queued Submission 与 `EvaluateSubmission` 投递，并确认评测完成前不产生 ScoringEvent 或排行榜失效。
- `f840f7de`：恢复 DataExport 与 PatchUpload replacement 共 4 个文件生命周期场景，排除文件由 9 个降至 7 个；测试改用统一 `StoredFile/FileId`、不可变比赛事件与管理员生命周期 Notification。数据导出的作弊处置查询不再让 EF 翻译 `[NotMapped] ScoringEventId`，改为直接查询强类型 Subject/Related 引用。
- `477c3b85`：恢复 AWD checker、AWDP fix result 与 KoH polling 共 11 个异步计分场景，排除文件由 7 个降至 4 个；AWD checker 测试改为当前无调度业务表的 `DispatchAwdCheckers(At, cursor)`，AWDP Patch 使用统一 File 引用，KoH 生命周期并发断言改读不可变 `competition_events` 及其 `from/to` payload。
- `1e60c640`：恢复 CompetitionChallenge lifecycle revision 与 Competition notification delivery 共 4 个场景，排除文件由 4 个降至 2 个；Hint 断言改读所属 CompetitionChallenge 的 JSON 集合，通知测试改为动态受众。投递器将稳定 `sourceEventKey` 写入 Content JSON 并在写入前按目标、类型、比赛和内容查重，确保 Wolverine 重放不会重复投递。
- `bd5e2cc3`：恢复 Wolverine transactional outbox 与 Runner assignment reconciliation 最后 2 个测试文件；维护测试改为当前无业务调度表的幂等 Tick 语义，Runner 对账改用当前时间/游标契约并验证显式重试消息。`NoCTF.Tests.csproj` 已无任何 `Compile Remove` 测试排除。
- `6ee8ae02`：修复 `MaintenanceTickAgent` 单例直接依赖 scoped `IMessageBus` 导致 Development Host 无法构建的问题；每个 Tick 现在从短生命周期 scope 解析消息总线。17 表 schema 验收也统一接入 `DockerIntegrationTest`，本机缺少 Docker 时跳过、设置强制集成环境变量时仍会失败。
- `361cb4f0`：将架构验证对齐 17 表收敛模型。参数化 PostgreSQL advisory lock、行锁与递归 CTE 仅允许在 4 个明确责任文件中出现，继续禁止 Raw SQL；JSON owned Hint 验证级联所有权，File FK 保持 Restrict；同步 189 条 API/122 条管理 API 清单、管理端 operationId/描述、OpenAPI 与 Nuxt SDK。
- `0a699031`：DataExport 过期和清除不再先行删除对象与 `files` 记录；处理器在同一数据库事务中解除 DataExport 引用并投递 `CleanupFile(FileId)`，提交后由统一文件生命周期处理器检查全局引用并完成对象/元数据清理。持久化测试覆盖 Expire/Purge 在消息处理前保留对象、消息处理后删除对象的边界。
- `0d93b70a`：补充统一 File 清理的 PostgreSQL 验收测试；同一 File 同时被用户头像和平台 Logo 引用时，`CleanupFile` 在任一引用存在期间均保持对象与元数据，首次对象存储删除失败后保留 File 供 Wolverine 重试，解除全部引用后的重试完成对象与行删除，重复消息保持幂等。
- `5aeddb4b`：修复 Notification 线程历史参与者权限；任意本人发送的节点会递归追溯到根，再向下展开后续回复，因此被移出比赛协作者的既有处理人仍可读取其参与过的完整私密线程。PostgreSQL 测试同时覆盖协作者/参赛队成员动态变化、分页途中追加回复、增量 feed 及不重不漏边界。
- `b67935c8`：实现受控文件注册生命周期并接入 ChallengeAttachment/PatchUpload。请求流先落入独占且关闭即删除的临时文件计算长度/SHA-256，File 行与 24 小时延迟 `CleanupFile` 租约先事务提交，最终对象上传后业务 Store 锁定 File 行再建立引用；业务拒绝会加速清理，延迟租约提供持久兜底。附件对象键同步为 `attachments/{attachmentId}`。
- `97e00eb1`：将用户头像、平台 Logo、队伍头像和比赛海报全部迁移到同一受控文件生命周期；Application 只把已注册的 FileId 交给业务 Store，Store 在业务事务中锁定 File 行后建立引用，拒绝或异常会加速清理。共享 File 行锁改用事务内参数化标量查询，避免 PostgreSQL `ExecuteNonQuery` 无法用受影响行数判断 `SELECT` 是否命中的问题。新增四类图片的注册/引用/补偿单元测试，并让用户头像与平台 Logo 的 PostgreSQL 持久化测试直接覆盖预注册 File 引用。
- `28fef980`：HintUnlock 的余额判断改为复用正式 `ILeaderboardSnapshotFactory`，因此 CTF/AWD/AWDP/KoH 与公开排行榜使用完全相同的当前事实、动态 Hint 价格和模式投影；重判时显式反向剔除该提交当前生效的 Hint 扣分，恢复解锁前余额。删除 Hint Store 内未被调用的重复投影实现，并新增同队并发双 Hint 单赢家及四模式改价重判的 PostgreSQL Testcontainers 场景。
- `21199393`：补齐 Maintenance 的集群与分页验收。两个共享 PostgreSQL Wolverine 存储的 Worker Host 必须始终仅有一个 `MaintenanceTickAgent` 处于 Running，原持有者停止后备用 Worker 接管并继续发布 Tick；Runner Assignment 对账在恰好 500 条时仅发布一个带末尾 UUID 游标的续页消息，空尾页不再续发，防止边界遗漏与空转循环。
- `35d700c9`：在可用的 Docker Desktop 上强制执行全部真实依赖测试并清零 19 个既有失败。不可变 CompetitionEvent 默认 payload 现在保留 Subject/Related 以外的强类型引用，缺失的 Guid/枚举/int payload 不再误投影为零值；所有 EF 查询改读可翻译的 Subject/Related 列。Competition Question 恢复 Open/Reply/Status 的动态受众投递和初始状态时间线，平台管理员通知使用数据库约束规定的固定 sentinel。受控文件注册不再提前 flush 共享 Wolverine 上下文，避免后续 replacement cleanup 丢失。测试宿主补齐生产 snake_case、受控上传依赖、真实生命周期 Event Store、Docker 主机回环地址和按 Host 观测的 Maintenance 故障转移。
- 新增协议枚举 `NotificationKind.UserAccountLifecycleChanged`；OpenAPI 与 Nuxt SDK 已同步。没有新增数据表或 migration。
- 本轮验证：`dotnet build backend/NoCTF.slnx --no-restore` 为 0 警告/0 错误；非集成测试 609/609 通过；强制 Docker 集成测试 152 项中 150 通过、2 个外部环境预期跳过；合计 761 项中 759 通过、2 跳过、0 失败。`dotnet ef migrations has-pending-model-changes` 无漂移，17 张业务表基线通过真实 PostgreSQL 迁移验收；OpenAPI 重新导出、`bun run api:gen` 后无产物漂移；`bun run typecheck` 与 `bun run generate` 通过。Nuxt 构建只保留既有 chunk/Nitro 第三方警告。本轮未改 HTTP 契约、SDK、数据表或 migration ID。
- 计分决策保持不变：HintUnlock 重判先反向剔除该 Submission 当前生效的 Hint 扣分，以解锁前余额按当前 Hint Cost 判定；成功后排行榜仍按当前 Cost 重投影。测试中原“100 余额改价到 60 后拒绝并得到 50 分”的错误期望已纠正为成功且最终 40 分，不引入不存在的拒绝惩罚。
- 未推送、未部署；原工作区 `TODO.md`、`PLAN.md` 及其他用户/协作者未提交内容保持不动。

## 2026-08-08 头像裁剪、Alpha 版本与导航可读性修正

- `321ae19d`：修复新版账户页头像按钮静默无效的问题。根因是把 shadcn `Input` 组件实例当作
  原生文件输入读取 `files`，导致请求从未发出；现在由原生隐藏文件输入的 `change` 事件取得文件，
  打开头像裁剪器后再通过生成 SDK `authenticationUploadMyAvatar` 上传固定 512×512 结果。
  裁剪器支持鼠标指针锚定滚轮缩放（1× 至 4×）、Pointer Events 与 pointer capture 拖动、
  90° 旋转、边界钳制、重置及 WebP/PNG 导出；预览和导出复用同一变换计算。新增 Bun 测试覆盖
  旋转、缩放、拖动、非有限状态和账户页文件输入接线。
- `ff647827`：提高新版顶部导航和比赛管理标签栏的可读性。顶部栏为 64px，品牌 18px，导航
  16px 并增加控件间距；比赛标签为 16px、36px 点击高度，标签栏 44px，窄屏只允许横向滚动且
  隐藏垂直溢出。
- `e0076eea`：结束 .NET 隐式 `1.0.0` 作为平台版本的状态，建立显式
  `0.1.0-alpha.1` 预发布版本。后续 Alpha 部署递增 `alpha.N`；进入 Beta 后使用 `beta.N`，正式版
  才移除预发布后缀。关闭 InformationalVersion 的源码哈希附加，管理后台显示稳定、可辨识版本。
- 生产只读排查确认上传卷 `/app/uploads` 属主为 `app:app`，近 72 小时没有头像请求或头像异常；
  与前端在读取文件前静默返回的根因一致。本轮没有修改 HTTP/OpenAPI 契约、生成 SDK、数据模型、
  migration 或生产数据。
- 验证：`bun run test` 5/5、`bun run typecheck`、`bun run generate` 通过；
  `dotnet build backend/NoCTF.slnx --no-restore -m:1` 为 0 警告/0 错误；完整非集成测试
  609/609 通过；API 生成的 AssemblyInformationalVersion 为 `0.1.0-alpha.1`；目标前端文件未新增
  手写 API 路径、域名、IP 或端口，`git diff --check` 通过。
- 本地浏览器验收：头像选择可打开 360×360 裁剪区，圆形裁剪直径与方形画布宽度相等；滚轮从
  100% 放大到 172%，拖动后无控制台错误。导航实测顶部栏 64px、导航 16px，比赛标签栏 44px、
  标签 36px/16px；一次性验收页和后台进程均已清理。
- 本轮只创建本地提交，未 push、未部署。生产仍运行 `7e5a1c07`；需要新的明确授权后才能推送和
  部署 `321ae19d`、`ff647827`、`e0076eea` 及本 HANDOFF 提交。

## 2026-08-08 头像与导航修正生产部署

- 用户明确授权后，`321ae19d`、`ff647827`、`e0076eea`、`2cd03663` 已快进推送至远程
  `main`，并在生产服务器 `/root/NoCTF` 以相同提交构建部署。
- 生产 Docker Hub 出口在构建期间超时；部署复用本机已校验的精确基础镜像
  `oven/bun:1.3.14`、`docker:28-cli`、`alpine:3.22`，并从原生产 Runner 提取同版本 Kompose
  二进制完成离线构建。临时 Dockerfile、Kompose 文件、镜像归档和部署日志均已删除，仓库与
  `deploy/docker-compose.prod.yml` 未被改写。
- Migration 容器退出码为 0；API、Runner、PostgreSQL、Redis 均为 healthy，Worker 正常运行。
  生产 API 程序集版本确认为 `0.1.0-alpha.1`；HTTPS `/`、`/health` 返回 200，携带浏览器
  `Accept: text/html` 的 `/account`、`/competitions`、`/admin/competitions`、`/verify-email`
  直达请求均返回 200。
- 新容器启动后的 API、Worker、Runner 日志中未发现 Error、Exception 或 `fail:`；数据库、
  Redis、HTTPS 证书与上传卷均保持原有数据和配置。

## 2026-08-09 血榜当前分值奖励与端口输入修正

- `b640f075`：CTF 血榜奖励新增 `BloodRewardPolicy.CurrentPointsPercentage = 3`。该策略按排行榜
  本次投影得到的题目当前分值计算奖励；既有 `SolveTimePointsPercentage = 2` 仍按对应队伍解题时
  的题目分值计算，现有枚举数值保持不变。前端配置项同步新增“当前分值百分比”，并将原有文案明确为
  “解题时分值百分比”。
- 修复题目运行环境的端口列表编辑：数字框被清空或浏览器因字母输入产生空值时，编辑行保留并显示
  行内错误，不再被数组更新逻辑误当作删除；只有点击右侧删除按钮才会移除该行。序列化时过滤仍为空
  的草稿行，仅提交有效数字端口。
- 平台预发布版本由 `0.1.0-alpha.1` 递增为 `0.1.0-alpha.2`。本轮未修改 HTTP/OpenAPI 契约、
  生成 SDK、数据模型或 migration。
- 验证：`dotnet build backend/NoCTF.slnx --no-restore -m:1` 为 0 警告/0 错误；非集成测试
  610/610 通过；`bun run test` 9/9 通过；`bun run typecheck`、`bun run generate` 通过；
  MSBuild `Version` 为 `0.1.0-alpha.2`；`git diff --check` 通过。新增测试区分当前投影分值与解题时
  分值，并覆盖端口空草稿保留、修正以及序列化过滤。
- 本轮只创建本地提交，未 push、未部署；生产继续运行 `0.1.0-alpha.1`，等待新的明确授权。

## 2026-08-09 管理员精确终止运行时与缺失镜像自动拉取

- `17f2ca11`：管理端运行时列表新增破坏性“终止”操作与二次确认。新接口
  `POST /api/v1/admin/competitions/{competitionId}/runtimes/{runtimeInstanceId}/terminate`
  以运行时实例 ID 和必填 `expectedProcessingVersion` 精确定位目标；过期页面返回 409，避免原有按
  队伍/题目停止“最新一代”时误伤另一实例。终止沿用既有 `Stopping -> StopRuntime -> Runner ->
  Stopped` 持久清理链路，排队中且尚未创建资源的实例直接进入 Stopped；操作写入带管理员 Actor 的
  不可变比赛事件。
- Docker 单容器创建前先检查本地镜像，仅在镜像不存在时从远程仓库拉取并再次校验；已有本地镜像
  不会被强制更新。Compose 启动显式使用 `--pull missing`，同样只补齐本地缺失镜像。当前单容器拉取
  使用 Docker daemon 的公开仓库访问能力，未新增或持久化私有仓库凭据。
- OpenAPI 已重新导出，Nuxt TypeScript SDK 已重新生成；管理端只调用生成的
  `adminTerminateRuntime`，没有手写 URL、端点路径、DTO 或状态枚举。管理 API 清单由 122 增至
  123，全部 API 路由清单由 189 增至 190。平台版本由 `0.1.0-alpha.2` 递增为
  `0.1.0-alpha.3`。
- 验证：`dotnet build backend/NoCTF.slnx --no-restore -m:1` 为 0 警告/0 错误；非集成测试
  610/610 通过；真实 PostgreSQL `RuntimeQuotaPersistenceTests` 3/3 通过，覆盖精确目标、停止消息、
  操作人审计和过期版本冲突；真实 Docker 缺镜像拉取测试 1/1 通过；`bun run test` 9/9、
  `bun run typecheck`、`bun run generate` 通过。OpenAPI 确认
  `expectedProcessingVersion` 为必填 int64 且最小值为 0；EF pending-model-changes 检查无漂移，
  `git diff --check` 通过。
- 本轮未修改数据模型、数据表或 migration。只创建本地提交，未 push、未部署；生产仍运行
  `0.1.0-alpha.1`，等待新的明确授权。

## 2026-08-09 Alpha.3 生产部署

- 用户明确授权后，`b640f075`、`4544de6a`、`17f2ca11`、`53104c14` 已快进推送至远程
  `main`；生产服务器 `/root/NoCTF` 同步至 `53104c14` 并完成镜像构建与 Compose 滚动重建。
- 生产访问 GitHub Release 下载 Kompose 时再次长时间无响应；部署终止了尚未切换服务的卡住构建，
  复用原生产 Runner 中相同的 Kompose `v1.38.0` 工具层，仅重新编译并替换 Runner 应用程序集。
  临时 Dockerfile、日志和状态文件均已删除，仓库文件和未跟踪的生产 Compose 覆盖文件未被改写。
- Migration 容器退出码为 0；API、Runner、PostgreSQL、Redis 均为 healthy，Worker 正常运行。
  API、Worker、Runner 程序集版本全部确认为 `0.1.0-alpha.3`；HTTPS `/` 与 `/health` 返回 200，
  新精确终止路由的未认证请求返回 401。新容器启动后的 API、Worker、Runner 日志中无 `fail:`、
  `crit:`、未处理异常或 Fatal。
- 已清理本次部署的临时文件、14.46 GB BuildKit 缓存和 1.315 GB 无引用镜像；根分区占用由构建
  峰值 82% 降至 61%，剩余约 16 GB。数据库、Redis、上传卷和 HTTPS 证书保持原有数据与配置。

## 2026-08-09 Running 队伍注册策略与卡住实例恢复

- `1167cd0b`：比赛新增 `AllowTeamRegistrationWhileRunning` 配置，默认关闭。Visible/Published
  阶段仍允许创建或重新报名；Running 阶段仅在该配置开启时允许；Paused/Finished 始终关闭。
  持有邀请 Token 加入既有队伍不等同于创建队伍，因此不受该配置限制。Store 在 Serializable
  写事务中重新读取状态和配置，避免比赛切换状态时的 TOCTOU 绕过。
- 新增 EF CLI 生成的 migration `20260808173522_AllowTeamRegistrationWhileRunning`；没有手工编辑
  migration 或 snapshot。Create/Update/Get Competition OpenAPI 契约、管理端创建/配置页和参赛者端
  报名入口已同步，TypeScript SDK 由 OpenAPI 重新生成。
- 新增 Administrator 专用强制终结接口
  `POST /api/v1/admin/competitions/{competitionId}/runtimes/{runtimeInstanceId}/force-terminate`。
  仅对带 Runner 归属、在 `Provisioning`/`Stopping` 停留至少 5 分钟的实例开放；该阈值与运行时
  最大操作超时 300 秒一致。请求必须携带当前 ProcessingVersion 和 8–512 字符原因，前端要求
  原因与二次勾选确认。
- 强制终结不直接改写终态：Worker 先持久化请求并把带 generation/provider/Runner fence 的消息
  定向投递到原 Runner；Runner 按 RuntimeInstanceId + generation 标签执行幂等 Provider 清理，重新
  枚举确认资源不存在后才释放容量；Worker 收到成功回执后才写回 `Stopped` 并派发等待的替代实例。
  查询不到当前工作记录的幂等重试也必须执行身份清理和资源复查。资源残留、清理异常和容量归属冲突
  均保持旧实例未完成状态，不允许数据库单边“强制成功”。Docker 端口随实际资源删除释放，历史
  PublishedPorts 继续保留用于审计和端口追溯。
- 新增工作人员可见的不可变比赛事件 `RuntimeForceTerminationRequested/Completed/Failed`，记录
  Administrator、原因、时间、generation、状态与强类型清理结果；管理审计继续从比赛事件投影，
  未新增独立审计表。
- 修复两个根因：Runtime claim 将缺失的 `security.capAdd` 规范化为空数组，Docker/Kubernetes
  adapter 同时防御 null；Runner assignment reconciliation 对“无 provider receipt、但仍保留
  runner_id”的失联 Provisioning/Stopping 实例改为送回原 Runner 按身份清理，容量在清理确认前
  保持占用，迟到的 provision receipt 会升级 processing fence 后再次精确清理。
- Docker 单容器流程在既有“缺镜像才拉取”、确定性名称/标签、NotFound 幂等删除基础上，补充启动后
  inspect 与有限等待；未真正进入 Running 的容器会清理并明确失败，不再生成虚假的 Running 回执。
  设计参考了 GZCTF DockerManager 中值得保留的资源身份、缺镜像拉取和幂等清理思想，但未复制其
  受限许可代码，并保留 NoCTF 更严格的 generation fence、资源复查和容量所有权约束。
- 竞赛列表卡片整体成为可聚焦的命名路由链接，不再只有标题可点击。平台版本由
  `0.1.0-alpha.3` 递增为 `0.1.0-alpha.4`。管理 API 清单由 123 增至 124，全部 API 路由清单由
  190 增至 191；OpenAPI、API 文档与 Nuxt SDK 已同步。
- 验证：`dotnet build backend/NoCTF.slnx --no-restore` 为 0 警告/0 错误；非集成/架构测试
  617/617 通过；真实依赖 `RunnerAssignmentReconciliationTests` 14/14、
  `RuntimeQuotaPersistenceTests` 3/3、`CompetitionManagementPersistenceTests` 1/1、
  `RuntimeReplacementCleanupFailureTests` 18/18 通过。`dotnet ef migrations has-pending-model-changes`
  无漂移；`bun run test` 9/9、`bun run typecheck`、`bun run generate` 通过；`git diff --check`
  通过。Nuxt 构建仅保留既有 chunk/Nitro 第三方警告。
- 本地浏览器以开发种子管理员验收：竞赛创建页显示 Running 队伍创建开关及说明，平台信息显示
  `0.1.0-alpha.4`，页面无控制台 Error；本地开发库无竞赛数据，因此整卡跳转由组件 DOM 结构、
  命名路由和生产构建验证。验收进程与临时日志已清理。后续浏览器验收按用户要求优先使用 Chrome
  或电脑控制，避免继续依赖存在闪退问题的内置浏览器。
- 本轮只创建本地功能提交与本 HANDOFF 提交，未 push、未部署；生产仍运行
  `0.1.0-alpha.3`，等待新的明确授权。

## 2026-08-09 Alpha.5 生产同步与 Runner 启动热修

- 远程 `main` 在部署开始后由协作者继续前进至 `c7cd766d`（`feat: simplify leaderboard and
  refresh client UI`）。本地分支和生产服务器均以 fast-forward 同步该提交；它以
  `fbb9cf13` 为直接祖先，因此没有冲突，也没有覆盖协作者修改。生产既有未跟踪
  `deploy/docker-compose.prod.yml` 始终保留且未改写。
- 首次 `0.1.0-alpha.4` 切换成功并执行 EF migration
  `20260808173522_AllowTeamRegistrationWhileRunning`，但上线日志验收发现 Runner 的 Wolverine
  codegen 无法解析 `RuntimeStopped` 回写处理器所需的 `ICompetitionEventRecorder`。健康端点仍为
  200，但运行时停止和强制终结回执存在失败风险，因此没有把该状态视为部署完成。
- `b2e5561d`：Standalone Runner 现在显式注册真实的 scoped `CompetitionEventStore` 作为
  `ICompetitionEventRecorder`，确保运行时回写继续写入 PostgreSQL 不可变比赛事件，而不是丢弃事件
  或使用空实现。新增宿主注册回归测试，并将平台预发布版本递增为 `0.1.0-alpha.5`。
- 本地验证：`dotnet build backend/NoCTF.slnx --no-restore -m:1` 为 0 警告/0 错误；
  `CompetitionEventRegistrationTests` 2/2 通过；`git diff --check` 通过。本热修未修改 HTTP/OpenAPI
  契约、生成 SDK、数据模型或 migration。
- `b2e5561d` 已推送至远程 `main` 并部署。Migration 容器退出码为 0，迁移历史最新记录为
  `20260808173522_AllowTeamRegistrationWhileRunning`；API、Worker、Runner 程序集均确认
  `0.1.0-alpha.5`，API、Runner、PostgreSQL、Redis 健康，Worker 正常运行。HTTPS `/`、`/health`、
  `/competitions`、`/admin/competitions` 返回 200；新强制终结路由未认证请求返回 401。等待 Wolverine
  完成启动编译后，API、Worker、Runner 的 `fail:`、`crit:`、未处理异常、Fatal 和
  `UnResolvableVariableException` 计数均为 0。
- Runner 构建继续复用线上同版本 Kompose `v1.38.0` 工具层，避免 GitHub Release 下载卡住；临时
  Dockerfile、Compose 覆盖、脚本和部署日志均已删除。清理 16.28 GB BuildKit 缓存后根分区占用为
  60%，剩余约 17 GB；未删除题目镜像、数据卷、数据库、Redis、上传文件或 HTTPS 配置。

## 2026-08-09 管理操作按钮与生产卡死实例修复

- `ce2678e3`：修复管理端运行时终止确认按钮的目标丢失。普通终止和强制终结原先使用
  `AlertDialogAction`，该组件会先关闭受控弹窗并清空当前 Runtime，再执行页面点击处理函数；处理函数
  因读到空目标而直接返回，所以不会发出 API 请求、不会显示结果。确认按钮改为普通破坏性 `Button`，
  仅由成功分支显式关闭弹窗，取消按钮仍保留 AlertDialog 语义。
- 队伍解封改为显式二次确认弹窗，确认后只调用生成 SDK `adminUnbanTeam`，并在成功或失败时给出明确
  toast；“纠正封禁”的原因输入与后端契约对齐为 8–512 字符，显示实时长度并在无效时禁用提交。
  新增 Bun 接线回归测试，防止再次把会自动关闭的 Action 组件用于异步提交目标，也覆盖解封确认和
  纠正原因约束。未修改 HTTP/OpenAPI 契约、生成 SDK、数据模型或 migration。
- 平台预发布版本由 `0.1.0-alpha.5` 递增为 `0.1.0-alpha.6`。
- 生产比赛 `019fe148-36a9-7581-a1f8-eec98c768ccb` 的旧 Runtime
  `019fe218-1e46-7e98-90d7-a2a455b69d8e` 经只读检查确认无 Docker 资源且无 Redis capacity claim；
  原始 Provision 死信明确记录旧版 `security.capAdd = null` 引发的 `ArgumentNullException`，生产当前
  版本已经包含该根因修复。随后通过现有管理员强制终结接口重新触发 Runner 身份清理：Runner 回写
  `ResourcesAbsent`，旧实例进入 `Stopped`，不可变比赛事件同时记录 Requested/Completed；替代 Runtime
  `019fe25a-d206-7c98-9e3c-3032cfe78c28` 自动派发并进入 `Running`，容器
  `noctf-019fe25ad2067c989e3c3032cfe78c28` 正常运行，Docker 映射为 `32769 -> 9999`。没有直接修改
  数据库、Redis 或 Docker 状态。
- 验证：先运行新增回归测试得到 2/2 预期失败，修复后 `bun test` 11/11、`bun run typecheck`、
  `bun run generate` 通过；`dotnet build backend/NoCTF.slnx --no-restore -m:1` 为 0 警告/0 错误；
  TUnit 直接运行 773 项，622 通过、151 个因本机 Docker/外部集群不可用按设计跳过、0 失败；
  `git diff --check` 通过。内置浏览器能导航到生产 URL，但页面结构读取仍发生超时，因此修复后的
  实际页面点击验收需在部署后完成。
- 本轮仅创建本地功能提交与本 HANDOFF 提交，未 push、未部署 `0.1.0-alpha.6`；生产仍运行
  `0.1.0-alpha.5`，但指定卡死实例已通过线上现有正式接口恢复。

## 2026-08-09 Alpha.6 管理操作修复生产部署

- 用户明确授权后，`ce2678e3` 与 `1dc69909` 已快进推送至远程 `main`；生产服务器
  `/root/NoCTF` 同步至 `1dc69909`，保留且未改写未跟踪的
  `deploy/docker-compose.prod.yml`，完成 API、Worker、Runner 与 Migration 镜像构建和 Compose
  滚动重建。本轮没有新的 migration、HTTP/OpenAPI 契约、生成 SDK 或数据模型变化。
- GitHub Release 的 Kompose 下载再次长时间无进展；在服务切换前终止该构建，使用原生产 Runner
  镜像中已验证的 Kompose `1.38.0` 工具层完成 Runner BuildKit 构建。没有修改仓库 Dockerfile；
  临时 Dockerfile、Compose 覆盖、构建/部署日志与状态文件均已删除。
- Migration 容器退出码为 0；API、Runner、PostgreSQL、Redis 均为 healthy，Worker 正常运行。
  API、Worker、Runner 程序集全部包含 `0.1.0-alpha.6` 版本标记，管理员平台信息接口也返回
  `0.1.0-alpha.6`。HTTPS `/`、`/health`、目标比赛的运行时管理页与队伍管理页均返回 200，管理员
  登录及运行时列表接口返回 200。
- 部署后旧卡死 Runtime `019fe218-1e46-7e98-90d7-a2a455b69d8e` 保持 `Stopped`；替代 Runtime
  `019fe25a-d206-7c98-9e3c-3032cfe78c28` 保持 `Running`，Runner 仍为
  `docker-runner-prod-1`，Docker 映射保持 `32769 -> 9999`，部署没有破坏题目容器。
- 新容器启动后的 API、Worker、Runner 日志中 `fail:`、`crit:`、Fatal、未处理异常和 Exception
  计数均为 0。现有 Warning 仅来自 ASP.NET DataProtection 临时密钥和 Hosting 端口覆盖提示；没有
  新业务错误。内置浏览器在登录提交时两次中断，Chrome 扩展当前不可用，因此本次生产 UI 的
  非破坏性弹窗点击没有形成额外验收证据；Bun 接线回归测试、生产静态页面、管理员认证和强类型
  API 查询均已通过。
- 已清理本次部署的全部临时文件、8.808 GB BuildKit 缓存及一个无引用中间镜像；根分区占用由构建
  峰值 70% 降至 63%，剩余约 15 GB。数据库、Redis、数据卷、题目镜像、上传文件和 HTTPS 配置均
  保持原状。

## 2026-08-09 到期环境界面收敛与题目无变化保存修正

- `5adf9926`：参赛者端题目运行环境卡片不再把最新一条 `Stopped` 历史实例显示为仍在运行；该状态
  会归一化为“尚未启动”界面。对于到期但后端尚未完成清理的 `Running` 实例，页面会在到期时自动
  开始轮询，并持续经过 `Stopping` 直到后端返回终态，避免停留在“运行中 / 已到期”。`Failed` 等
  需要用户感知的终态仍按原语义展示，没有被错误隐藏。
- 题库模板更新改为后端强制无变化幂等：在 expected revision 校验通过后，比较模式、可见性、标题、
  描述、方向和 Definition JSON；JSON 按结构语义比较，因此仅空白、缩进或属性顺序变化不会产生新
  修订。全部内容相同时直接返回当前投影，不写数据库、不递增 Revision、不更新 UpdatedAt；任一真实
  内容变化仍沿用原有事务、活动比赛模式约束和修订递增流程。过期 expected revision 仍返回冲突，
  不会被无变化判断绕过。
- 平台预发布版本由 `0.1.0-alpha.6` 递增为 `0.1.0-alpha.7`。本轮未修改 HTTP/OpenAPI 契约、
  生成 SDK、数据模型或 migration。
- 验证：`dotnet build backend/NoCTF.slnx --no-restore -m:1` 为 0 警告/0 错误；TUnit 共 773 项，
  622 通过、151 项因本机 Docker/Kubernetes/Libvirt 条件未满足而按设计跳过、0 失败。新增的真实
  PostgreSQL 题目无变化持久化断言包含在 Docker 跳过范围，已成功编译但本机未执行；它验证不同 JSON
  格式不会改变持久化 Revision 与 UpdatedAt。`bun test` 13/13、`bun run typecheck`、
  `bun run generate`、`git diff --check` 通过；Nuxt 生产构建只保留既有 chunk/Nitro 第三方警告。
- 本轮只创建本地功能提交与本 HANDOFF 提交，未 push、未部署；生产仍运行
  `0.1.0-alpha.6`，等待新的明确授权后再进行生产浏览器验收。

## 2026-08-09 新前端作弊事件处置链路修复

- `ac46286c`：将作弊详情的“驳回”“确认作弊并封禁”和“纠正”统一收口到单一处置状态机。
  根因与此前运行时终止按钮相同：确认按钮使用 `AlertDialogAction` 时会先关闭受控弹窗并清空目标，
  异步处理容易表现为无反应；同时原页面只校验非空，既未与后端 8 字符约束对齐，也没有行内反馈。
  现在异步提交改用普通 `Button`，只有 SDK 成功后才显式清理弹窗状态，失败会保留已输入理由与目标。
- 二次确认会明确显示操作类型、来源队伍和审计影响；理由不足 8 个字符时实时显示仍缺字符数并禁用
  提交。提交期间显示 loading、锁定取消/关闭并通过同步 pending fence 阻止重复请求。成功提示使用
  提交前捕获的强类型 action，不再依赖随后被清空的状态；随后重新读取当前作弊详情和列表，来源
  队伍封禁状态与事件处置状态均由后端事实刷新。队伍管理页没有跨路由客户端缓存，进入时会重新
  读取，因此无需维护第二份本地队伍状态。
- 所有请求继续只调用 OpenAPI 生成 SDK `adminDismissCheatIncident`、`adminConfirmCheatIncident` 和
  `adminCorrectCheatIncident`；没有手写 URL、端点、DTO、枚举或本地成功状态，也未修改生成 SDK、
  HTTP/OpenAPI 契约、数据模型或 migration。平台预发布版本由 `0.1.0-alpha.7` 递增为
  `0.1.0-alpha.8`。
- 新增 9 项 Bun 行为/接线回归，覆盖两类弹窗打开、短理由零请求、dismiss/confirm 单次 SDK 分派与
  参数、pending 防重复、成功清理及刷新、失败保留、取消/重开。顺带将既有管理操作源码断言改为
  CRLF/LF 均可运行，避免干净 Windows worktree 的换行差异造成伪失败。
- 验证：ClientApp `bun test` 22/22 通过；`bun run typecheck`、`bun run generate` 通过；
  `dotnet build backend/NoCTF.slnx -m:1` 为 0 警告/0 错误；`git diff --check` 通过。ClientApp
  当前没有仓库级 ESLint 配置或 lint script，因此没有伪造 lint 结果。Nuxt 构建只保留既有
  chunk/Nitro 第三方警告。生产浏览器只做非破坏性页面验收，不会为验证按钮处置真实作弊证据。

## 2026-08-09 Alpha.8 到期环境、题目修订与作弊处置生产部署

- 用户明确授权后，`5adf9926`、`24e961c7`、`ac46286c` 与 `810630c1` 已从生产基线
  `865af7fd` 无冲突快进推送至远程 `main`；生产 `/root/NoCTF` 通过带先决提交校验的增量 Git
  bundle 快进到 `810630c1`。既有未跟踪 `deploy/docker-compose.prod.yml`、`.env`、HTTPS 证书、
  PostgreSQL/Redis/上传卷及题目容器均未改写。
- 从干净 worktree 构建的 API/新前端镜像为
  `sha256:d5a558116c0456e596f02bb9b4646f592b5e31948f9f6ca3c1ad46c153b89a55`，OCI revision 为
  `810630c17ac5a842d5fe2b995aa7be796fbcbaf3`、version 为 `0.1.0-alpha.8`。镜像压缩包与 Git
  bundle 均在本地和服务器端核对 SHA-256 一致后才加载。差异不包含 migration/model snapshot，
  Compose 静态配置通过，因此只以保留旧镜像回滚标签的方式重建 `deploy-backend-1`；Worker、Runner、
  PostgreSQL 和 Redis 的镜像及启动时间均未改变。
- 新 API 第 3 次健康轮询进入 healthy，restart count 为 0；程序集版本为 `0.1.0-alpha.8`，部署后的
  Nuxt chunk 已确认包含“理由至少需要 8 个字符”新交互。HTTPS `/`、`/health`、
  `/admin/competitions` 与目标比赛 `/cheats` 的浏览器式 HTML 请求均返回 200。新容器启动后的
  `fail:`、`crit:`、Fatal、Unhandled、Exception 计数均为 0；仅保留既有临时 DataProtection key
  与 Hosting 端口覆盖警告。
- 内置浏览器导航再次超时并重置连接；Chrome 正在运行，但当前配置未安装 ChatGPT 浏览器扩展，
  因而没有伪造自动点击结果，也没有创建或处置任何真实作弊事件。交互行为由新版 ClientApp 22/22
  测试、类型检查、生产静态生成、镜像内产物指纹和生产 HTTPS 路由共同验收；后续可由管理员在有
  可丢弃测试事件时补一次人工点击检查。
- 已删除服务器端 265 MB 传输目录；旧 API 镜像已无标签且未被任何容器引用，Docker 已自动清除。
  根分区最终为 66% 使用、约 14 GB 可用。没有执行广域 Docker prune，也未删除数据库、Redis、上传
  文件、题目镜像、运行时容器或任何业务数据。本部署文档提交只需同步生产 checkout，无需再次迁移
  或重建服务。

## 2026-08-09 Alpha.9–Alpha.15 管理、参赛与通知链路续推

- `e098e17c`（`0.1.0-alpha.9`）：管理端作弊事件列表收到实时更新后会回到第一页并重新读取最新事实；
  连续事件会合并刷新，加载更多与实时刷新不会互相覆盖。
- `4f55bd1c`（`0.1.0-alpha.10`）：恢复比赛删除、已删除比赛恢复和彻底删除操作。三类操作保留各自的
  二次确认目标直至异步 SDK 请求真正开始，避免受控弹窗先清空操作状态后表现为按钮无响应。
- `e1f36068`（`0.1.0-alpha.11`）：参赛题目响应补充解题人数和本队已解状态；开赛前由服务端和前端
  双重隐藏题目。题目卡显示解题人数及颜色无关的已解旗帜标记，正确 Flag 仅触发一次并尊重
  `prefers-reduced-motion` 的庆祝动画。比赛动态从不可变事件 payload 恢复题目名称。
- `675227c2`（`0.1.0-alpha.12`）：封禁申诉与私密咨询改为显式提交、loading 防重复、行内校验和
  保留输入的错误状态；咨询弹窗扩大且题目类咨询必须选择题目。增加 `/reset-password` 页面别名，
  与邮件中已投递的密码重置 URL 一致。
- `af0cc13b`（`0.1.0-alpha.13`）：通知铃铛按最新不可变通知 ID 显示未读红点，通知中心读取后写入
  本地已读游标；管理比赛页恢复全局赛事公告，Owner、Manager、Judge 可通过生成 SDK 向参赛者发布，
  Observer 保持只读。事件投影优先读取 payload 中保存的题目引用，因此旧事实也能显示题目名称。
- `a0fbfbb1`（`0.1.0-alpha.14`）：确认作弊并封禁来源队伍时，`TeamBanned` 消息携带强类型
  `ConfirmedCheating` 公告类型，Worker 幂等发布参赛者纪律公告：
  “队伍「名称」经核实存在作弊行为，现已由赛事组委会予以封禁。”普通人工封禁新增
  `announcePublicly` 强类型请求字段及默认关闭的前端选项；开启时使用“违反赛事规则”文案，均不公开
  管理员填写的原因或证据。通知中心同时显示公告标题和正文。
- `52a5a788`（`0.1.0-alpha.15`）：CTF Flag 首次由非正确结果转为 Correct 后，在同一评测事务中认领
  该队伍、该比赛题目的 Player Container/Compose Runtime。Queued 实例直接标为 Stopped，其余活动
  或带资源回执的失败实例进入现有 `Stopping -> StopRuntime -> Runner 清理 -> Stopped` 状态机；记录
  Submission 与 Runtime 关联的不可变比赛事件。团队级事务锁在计分/血榜判定后获取，避免并发提交
  重复派发且不阻塞排行榜并发更新。重复重判已正确结果不会误停后来重新启动的环境；AWD、AWDP、
  KoH 和 OVA Runtime 不受影响。
- HTTP/OpenAPI 契约变化：参赛题目投影新增解题进度字段；`BanTeamRequest` 新增可选布尔字段
  `announcePublicly`。两次契约变更均通过 API 导出重新生成 `swagger.json`、`v1.json` 和 TypeScript
  SDK；生成文件没有手工编辑。没有新增数据表或 migration。
- 最终验证：`dotnet build backend/NoCTF.slnx --no-restore` 为 0 警告/0 错误；作弊持久化测试 1/1、
  通知投递 PostgreSQL 测试 3/3、提交处理与运行时自动停止 PostgreSQL 测试 4/4 通过；ClientApp
  `bun test` 41/41、`bun run typecheck`、`bun run build` 通过，OpenAPI 导出和 `bun run api:gen` 成功。
  Nuxt 仅保留既有大 chunk/Nitro 第三方警告。ClientApp 没有 lint script 或 ESLint 配置，因此没有
  伪造 lint 结果。`git diff --check` 通过。
- 本轮使用独立干净 worktree 和 `codex/cheat-latest-refresh` 分支，未覆盖主工作区或其他 Agent 的
  未提交修改。以上提交尚未 push、尚未部署，生产仍不包含 Alpha.9–Alpha.15；因此破坏性按钮、作弊
  封禁公告和正确 Flag 自动停容器没有在生产浏览器中执行。部署授权后应使用可丢弃比赛数据分别验收。
- 后续用户已明确“自定义配置 Flag 头”的作用域、静态 Flag 边界与历史值策略；决策及实现记录见下方
  Alpha.16 小节。

## 2026-08-09 Alpha.16 动态 Flag 模板与 Header 配置

- 用户通过 `/grilling` 明确：配置只作用于动态容器生成的 Flag；手工/静态 Flag 不受影响；模板修改
  只影响今后首次生成的 Flag，已持久化事实继续有效。`af2b481c` 据此为 CTF 竞赛配置和题目
  Definition 增加可选 `flagTemplate`，优先级固定为“题目覆盖 > 竞赛默认 > 平台默认
  `flag{[TEAMHASH]}`”。题目覆盖仅允许和 `Runtime.FlagSource=PerTeam` 一起保存，RulesJson 禁止承载
  Definition 所有的模板字段；非法占位符在保存阶段被拒绝。
- CTF 每队 Flag Store 现在在创建缺失事实前同时读取 Competition 配置、Challenge Definition 与派生
  Secret，通过既有 `PerTeamFlagGenerator` 生成；默认配置仍严格复现历史 `flag{32 位 TEAMHASH}`。
  Store 仍先按“队伍×比赛题目×RuntimeDefinition”读取旧事实，因此配置更新、Reset 和容器 Generation
  变化都不会轮换旧 Flag。真实 PostgreSQL 回归同时验证题目覆盖、竞赛回退、静态题零生成、旧队伍
  保持原值和新队伍采用新 Header。
- AWD 修复了一个既有接线缺口：管理端早已能保存竞赛级 `FlagTemplate`，但轮次生成器此前只读取题目
  Definition，导致竞赛默认实际无效。现在统一按同一优先级解析；当轮已有 `ChallengeFlag` 仍幂等复用，
  新模板只作用于后续缺失的“队伍×题目×轮次”事实。前端模板初值由随机正文改为平台真实默认
  `[TEAMHASH]`，避免管理员仅改 Header 时意外改变正文语义。
- AWDP 当前产品模型没有长期参赛者攻击 Runtime 或自动动态 Break Flag producer；只有不暴露公网、
  `TeamId=null` 的一次性 Fix 验证 target，Break Flag 仍来自题库/比赛 Flag 事实。本轮严格遵守“手工
  Flag 不受影响”，没有把 AWDP 手工 Flag 伪装为自动生成，也没有新增无效配置项。若要让 AWDP
  也具备可配置 Header 的动态攻击 Flag，必须另立阶段先实现长期攻击靶机、生成、注入与轮换闭环，
  不能只加一个无人消费的前端字段。
- 平台版本由 `0.1.0-alpha.15` 递增为 `0.1.0-alpha.16`。配置 JSON 是既有 `configurationJson` /
  `definitionJson` 的内部强类型模式字段，schemaVersion 维持现有值；没有 HTTP/OpenAPI 路由、请求/
  响应 DTO、生成 TypeScript SDK、数据表或 migration 变化。`dotnet ef migrations
  has-pending-model-changes` 确认模型无变化。
- 验证：`dotnet build backend/NoCTF.slnx --no-restore -m:1` 为 0 警告/0 错误；非集成 TUnit
  627/627；真实依赖集成组 154/154，通过且仅 Kubernetes、Libvirt 两项因未配置外部环境按门禁跳过；
  其中 CTF Flag PostgreSQL 2/2、AWD 轮次 PostgreSQL 1/1、Flag 模板单测 7/7。ClientApp
  `bun test` 43/43、`bun run typecheck`、`bun run build`、`git diff --check` 全部通过；Nuxt 仅保留
  既有大 chunk、插件耗时与第三方 trailing-slash deprecation 警告。
- 本阶段已创建本地功能提交 `af2b481c`，尚未 push、尚未部署。生产仍不包含 Alpha.9–Alpha.16，
  因此浏览器不能验收新配置控件；部署获得明确授权后，应使用可丢弃 CTF/AWD 比赛验证保存 Header、
  新 Flag 前缀与旧 Flag 不变，并检查浏览器控制台无新增错误。

## 2026-08-10 Alpha.9–Alpha.16 生产部署

- 用户明确授权后，Alpha.9–Alpha.16 的 10 个提交已从 `dc3772f4` 无冲突快进推送至远程
  `main`，功能/文档部署基线为 `91dd4434`。生产 `/root/NoCTF` 通过带先决提交
  `dc3772f4` 的增量 Git bundle 快进到同一提交；既有未跟踪
  `deploy/docker-compose.prod.yml`、根目录 `.env`、HTTPS 证书、PostgreSQL/Redis/上传卷及题目
  容器均未改写。
- 从独立干净 worktree 构建并核对的 Alpha.16 镜像为：API/Migration
  `sha256:db5d41bf47e78acfd25d11a0908a9468acfd08211e311dfc766fdd0eb8a29e77`、Worker
  `sha256:9abbb72b0954fe807400ca99ce517128aa8a274e3f52b6be7fc2f6ba73445277`、Runner
  `sha256:b5c6ebef8e02cac70eb2007b1f614d166c8b9660cb558998e9a5c2e606156952`，均为
  `linux/amd64`。681,460,736 字节镜像归档和 66,318 字节 Git bundle 的本地/服务器 SHA-256
  完全一致后才加载；旧四类镜像保留 `rollback-dc3772f4` 标签，没有覆盖回滚入口。
- 差异不含 migration/model snapshot；新 Migration 容器退出码为 0，并明确报告数据库已是最新。
  随后使用生产 `.env` 和既有双 Compose 文件以 `--no-build` 重建 API、Worker、Runner。稳定后 API、
  Runner 均为 healthy，Worker 正常运行，三者 restart count 均为 0；镜像内程序集均包含
  `0.1.0-alpha.16`。
- 外网 HTTPS `/` 与 `/health` 返回 200。浏览器式 `Accept: text/html` 回归确认
  `/verify-email`、`/reset-password` 及对应 `/auth/*` 规范路由均返回 SPA 入口 200；不携带 HTML
  Accept 的通用 curl 会按 API/静态资源语义得到 404，这不是浏览器路由故障。切换后 API、Worker、
  Runner 日志未发现 `fail:`、`crit:`、Fatal、Unhandled、OutOfMemory 或
  `ArgumentNullException`；Warning 仅为既有 DataProtection 临时密钥和 Hosting 端口覆盖提示。
- 本次没有为验收创建、处置或修改真实比赛、作弊事件、队伍、Flag 或 Runtime。动态 Flag Header、
  正确 Flag 自动停止容器和作弊封禁公告等有状态流程仍应只在可丢弃比赛数据上补浏览器验收；构建前
  的完整后端、真实依赖集成和 ClientApp 门禁结果见上方 Alpha.9–Alpha.16 两节。

## 2026-08-10 Alpha.17 作弊筛选与通知导航修复

- `654dbc6d` 修复管理端作弊页首次加载把空日期展开为 1970–2999、违反后端 31 天查询约束的问题。
  空日期现在固定为页面本次已应用的最近 31 天窗口；筛选草稿与已应用筛选分离，分页、加载更多和
  SignalR 失效刷新复用同一范围，不会因每次重新取当前时间而使签名 cursor 的筛选键漂移。
- 状态筛选新增可见的“全部”选项并设为默认值，调用生成 SDK 时映射为 `status: null`。手动日期筛选
  必须同时填写起止时间；无效日期、倒序和超过 31 天均在前端显示中文行内错误并停止请求，合法的
  0–31 天范围继续传给 `adminListCheatIncidents`。页面同时明确提示空日期和最大范围语义。
- 通知中心新增集中式卡片目标解析：`CheatIncidentDetected` 使用通知既有强类型 kind 和 content 中的
  `competitionId` 跳转到 `/admin/competitions/{competitionId}/cheats`；其他比赛通知保持跳转参赛者
  工作区，没有比赛关联的通知保持在通知中心。没有新增接口、手写 API URL、DTO 或枚举。
- 平台版本由 `0.1.0-alpha.16` 递增为 `0.1.0-alpha.17`。没有 HTTP/OpenAPI 契约、生成 SDK、数据模型
  或 migration 变化。新增 9 项回归测试；ClientApp `bun test` 52/52、`bun run typecheck`、
  `bun run build` 通过，Nuxt 只保留既有大 chunk、插件耗时及第三方 trailing-slash deprecation
  Warning；`dotnet build backend/NoCTF.slnx --no-restore -m:1` 为 0 警告/0 错误，`git diff --check`
  通过。仓库没有 lint script 或 ESLint 配置，因此未伪造 lint 结果。
- 本阶段仅创建本地功能提交与本 HANDOFF 提交，尚未 push、尚未部署。生产仍运行
  `0.1.0-alpha.16`；获得新的明确授权后再构建 Alpha.17 API 镜像，并在浏览器中验收默认“全部”、
  空日期正常加载、非法范围中文反馈及作弊通知卡片跳转。

## 2026-08-10 Alpha.18 比赛通知、裁判处置与动态分值一致性

- 功能提交 `450748b9a8758e095846ed8a65013dfb9199878d` 完成三个相互关联的比赛工作台缺口。
  参赛者比赛页新增“公告/通知”入口，复用现有不可变 Notification 与 ReplyTo 线程：列表按
  `competitionId` 强类型查询，卡片打开同页详情，完整显示官方公告标题、正文、发布人和发布时间，
  并展示同一事件后续回复/状态变化。题目发布、血榜、咨询、封禁/纠正、作弊、提交、Runtime、报名
  与普通生命周期消息分别精确导航到题目、咨询详情、封禁申诉区、管理作弊详情、提交、题目环境、
  我的队伍或带事件类型筛选的比赛动态。全局通知中心改为复用同一详情组件。
- Notification HTTP 契约新增可选 `competitionId` 查询条件和 `sourceDisplayName` 响应字段；签名 cursor
  scope 包含用户与比赛，禁止跨筛选复用。PostgreSQL 仍用原有递归可见性/线程查询，Development 的
  EF InMemory 仅增加等价内存遍历分支，以保证本地浏览器验收可用。`TeamBanCorrected` 只投递受影响
  队伍成员，不再把个别队伍纠正广播给全部参赛者。没有新增数据表、DbSet、migration 或 snapshot。
- 裁判权限边界改为显式角色：管理比赛响应新增 `administrationRole`（Owner/Manager/Judge/Observer），
  前端不再从 `canConfirm` 反推 Manager。Judge 可以人工封禁队伍，并可驳回或确认作弊事件；解封、
  封禁纠正、报名审批和配置写入仍只属于 Owner/Manager/平台管理员，Observer 保持只读。真实 HTTP
  回归覆盖 Judge 封禁成功、Observer 403，以及 Judge 对两种作弊处置均可执行。
- 排行榜题目元数据新增 `currentScore`。CTF 投影使用与排行榜行分数相同的有效规则、动态表达式、
  当前有效解题数和有效队伍数计算当前题目分值；题目卡不再展示可能失真的模板 `BaseScore`，而是
  显示动态当前分值。本队已解题同时显示实际结算分（包含已配置血榜奖励）以及一血/二血/三血标记，
  因此截图中的“卡片 1000、榜单 500、血榜不可见”不再出现。投影测试同时断言 CurrentScore 与
  CurrentPoints 百分比血奖语义。
- OpenAPI 已从后端重新导出，`swagger.json`、`wwwroot/openapi/v1.json` 与 TypeScript SDK 均由工具
  重生成，没有手工编辑生成文件。平台版本由 `0.1.0-alpha.17` 递增为 `0.1.0-alpha.18`。
- 验证：`dotnet build backend/NoCTF.slnx --no-restore` 为 0 警告/0 错误；非 Integration TUnit
  629/629、Notification PostgreSQL Testcontainers 1/1 通过；ClientApp `bun test` 56/56、
  `bun run typecheck`、`bun run build`、`git diff --check` 全部通过。仓库仍无 lint script/ESLint
  配置，因此未伪造 lint 结果；Nuxt 只保留既有大 chunk、插件耗时和第三方 trailing-slash 警告。
- 浏览器在可丢弃 Development 比赛中实际验收：比赛导航出现“公告/通知”，工作人员公告卡片可打开，
  URL 带稳定 notification id，详情正确显示完整标题、正文、`dev-admin` 发布人、发布时间和“暂无后续
  回复或状态变化”。验收临时进程与内存数据已清理；没有修改生产比赛、队伍、作弊事实或通知。
- 本阶段只创建本地功能提交与本 HANDOFF 提交，尚未 push、尚未部署；生产仍运行
  `0.1.0-alpha.16`。获得明确授权后再推送并部署 Alpha.17–Alpha.18，使用可丢弃生产比赛复核动态
  分值/血榜、Judge 封禁边界、通知精确跳转及控制台状态。

## 已落地的主要能力

- `CompetitionEvent` 合并生命周期与排行榜可见性事实。
- `platform_settings` 合并邮件设置；`account_tokens` 合并验证与重置 Token。
- Notification 使用 Source/Target/Kind/Content/Related/ReplyTo，并以动态受众和线性线程承载 Question/Announcement。
- Hint 与 Runtime published ports 改为所属实体的 JSON；旧子表已删除。
- HintUnlock 与 ManualAdjust 均走 Submission + ScoringEvent；ManualAdjust delta 保存于 `SubmittedFlag`。
- `files` 成为唯一文件元数据表；User/Team Avatar、Competition Poster、Platform Logo、附件、Patch、DataExport 使用 FileId。
- 新增 Team Avatar、Competition Poster、比赛公告、Notification thread、ManualAdjust endpoint。
- Wolverine `SingularAgent` 投递维护 tick；删除 durable maintenance schedule 业务表。

## 验证入口

```powershell
dotnet build backend/NoCTF.slnx --no-restore
dotnet test backend/tests/NoCTF.Tests/NoCTF.Tests.csproj --no-restore
dotnet ef migrations has-pending-model-changes `
  --project backend/src/NoCTF.Infrastructure/NoCTF.Infrastructure.csproj `
  --startup-project backend/src/NoCTF.API/NoCTF.API.csproj
dotnet run --project backend/src/NoCTF.API/NoCTF.API.csproj --no-build -- --export-openapi
```

## 后续必须继续处理

1. 所有原先绑定旧表/旧协议的测试文件均已恢复，`NoCTF.Tests.csproj` 不再排除源码。PostgreSQL、Redis、Docker Container/Compose 与 Wolverine 真实依赖门禁已在本机执行通过；剩余外部门禁仅为需要 `NOCTF_KUBERNETES_INTEGRATION` 实集群的 Kubernetes 测试和需要 `NOCTF_LIBVIRT_DISK_PATH` 镜像的 Libvirt 测试。

## 工作区保护

- `.webbridge-tmp/`、`PLAN.md`、`backend/src/NoCTF.API/storage/` 是本地/用户内容，不应加入提交。
- 不恢复旧 migration、旧表或兼容层；migration 与 snapshot 只能通过 EF CLI 修改。
