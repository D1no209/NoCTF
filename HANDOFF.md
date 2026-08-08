# NoCTF 数据模型重构交接

## 当前状态

- 已用 EF CLI 重建 `InitialBaseline`，业务 schema 为计划中的 17 张表。
- API、测试项目当前均可编译；EF model snapshot 与当前模型无 pending changes（2026-08-08 再次验证）。
- OpenAPI 已重新导出，Nuxt 生成客户端已更新。
- 根 `AGENTS.md`、`CONTEXT.md` 与数据库/API/消息/计分/存储文档已同步。
- 当前完整测试基线已清零：最后一次运行结果为 759 通过、2 跳过、0 失败；PostgreSQL、Redis、Docker Container/Compose 等真实依赖场景已强制执行，只有未配置实集群/镜像的 Kubernetes 与 Libvirt 外部集成按设计跳过。Build、OpenAPI、Nuxt 与 EF drift 检查通过。

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
