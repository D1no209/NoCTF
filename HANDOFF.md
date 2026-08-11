# NoCTF 数据模型重构交接

## 当前状态

- 已用 EF CLI 重建 `InitialBaseline`，业务 schema 已按 GameplayFact 收敛为 16 张表；旧
  `submissions`/`scoring_events` 已由单一 `gameplay_facts` 当前事实表取代。
- API、测试项目当前均可编译；EF model snapshot 与当前模型无 pending changes（2026-08-08 再次验证）。
- OpenAPI 已重新导出，Nuxt 生成客户端已更新。
- 根 `AGENTS.md`、`CONTEXT.md` 与数据库/API/消息/计分/存储文档已同步。
- 当前完整测试基线已清零：最后一次运行结果为 759 通过、2 跳过、0 失败；PostgreSQL、Redis、Docker Container/Compose 等真实依赖场景已强制执行，只有未配置实集群/镜像的 Kubernetes 与 Libvirt 外部集成按设计跳过。Build、OpenAPI、Nuxt 与 EF drift 检查通过。

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
