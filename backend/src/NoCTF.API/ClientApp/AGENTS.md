# NoCTF ClientApp 前端约定

Nuxt 4 SPA(`ssr: false`),Bun 管理依赖,TypeScript strict,Vue 用 `<script setup lang="ts">` Composition API。

## 命令

- `bun run dev`:开发(端口 3000,vite proxy 把 `/api`、`/hubs`、`/health` 转发到 `http://localhost:5080`)。
- `bun run typecheck`:全量类型检查,改动后必须通过。
- `bun run audit:architecture`:检查 i18n、渲染层、路由壳与 UI 原语边界；改动后必须通过。`bun test` 包含同一架构检查及行为回归。
- `bun run api:gen`:从 `../wwwroot/openapi/v1.json` 重新生成 API SDK 到 `app/api/`(@hey-api/openapi-ts,配置在 `openapi-ts.config.ts`)。后端接口变更后先重新导出 OpenAPI(`dotnet run -- --export-openapi`)再跑此命令;生成产物提交入库。
- shadcn-vue 组件用 `bunx --bun shadcn-vue@latest add <name>` 添加(CLI 一律走 bunx)。

## 结构(app/ 下)

- `api/`:hey-api 生成产物(sdk.gen.ts / types.gen.ts / client),不要手改。
- `components/ui/`:唯一的共享 UI 原语目录，shadcn-vue 与项目通用控件均在此处。只有此目录自动导入，无前缀(`<Button>` 等)。新增通用交互控件必须集中放在这里，禁止页面或业务组件私建按钮、输入框、弹层、表格等原语。
- `components/views/`:纯渲染视图。setup 只声明 props、类型和 `toRefs` 绑定；不请求 API，不访问会话，不创建业务状态、watch、轮询或事件处理函数。模板允许布局 HTML、原语组合、条件渲染、列表、i18n、格式化、v-model 与命令转发；禁止事件中的赋值、控制流和业务转换。通过类型引用功能层契约可以，但禁止运行时导入功能层。
- `features/<领域>/`:功能逻辑与组合入口。`use<Name>.ts` 管理状态、校验、转换、权限判断、API、轮询、订阅和事件处理；`<Name>.vue` 只连接 props/emits/model、控制器和对应 View。子功能组件由控制器显式提供，视图通过 `<component :is>` 渲染，禁止隐式自动导入业务组件。`features/routes/` 对应路由功能，`features/shell/` 对应应用壳，`features/shared/view-state.ts` 统一连接功能状态与视图。所有 Vue 文件名保持唯一。
- `features/app/`:应用工作区组合与导航契约；`AppWorkspaceNav` 与 `workspace-nav.ts` 的 `WorkspaceNavGroup` 在这里。
- `composables/`:`useAuth`(会话/角色)、`usePlatform`(品牌)、`useCursorPagination`(keyset「加载更多」)、`usePolling`(202+statusUrl 轮询)、`useCompetitionHub` / `usePlatformLogHub`(SignalR 实时失效)、`useDefinitionModel`(题目 definitionJson 的 parse/serialize/回灌去重,供 DefinitionEditor 与各切片组件共享一份 model)。
- `lib/`:`session.ts`(内存 access token + refresh 单飞,供拦截器使用,禁 localStorage)、`admin-competition.ts`(竞赛管理角色注入)。
- `utils/`:`api-error.ts`(ApiError/parseApiError/statusErrorMessage,problem+json 解析与空响应体的状态码兜底文案)、`labels.ts`(枚举中文标签)、`admin-format.ts`、`download.ts`(带 Bearer 的 blob 下载)、`game-config.ts`(游戏模式专属配置 JSON 的解析/序列化模型与字段描述)。
- `middleware/`:`auth` / `guest` / `platform-admin`,经 `definePageMeta` 使用。
- `pages/`、`layouts/`、`app.vue`:仅路由/布局元数据与功能入口组合，不持有业务代码或私有原语。路由仍为公开区、选手区、认证、账户、竞赛管理、题库与平台管理。
- `locales/zh-CN.ts`、`locales/en.ts`:使用同一组稳定资源 key，中英文 key 与插值参数必须一致。修改文案不修改 key。页面、组件、默认属性、占位符、无障碍名称、通知与错误文案不得硬编码中文或英文；用户内容、协议值、URL 和代码示例数据按其真实语义处理，不翻译用户内容。

## 约定

- API 调用一律走 `app/api` SDK:`const { data, error } = await xxxEndpoint({ path, query, body })`;错误统一 `parseApiError(error)` 取 message,toast 用 vue-sonner 的 `toast()`。空响应体的错误(如登录 401)由 `plugins/api.client.ts` 的 error 拦截器按状态码合成文案,登录页 401 显示「用户名或密码错误」。
- 模式专属配置 JSON(题目模板 definitionJson、竞赛模式配置、题目规则 rulesJson)一律用结构化编辑器组件,禁止暴露原始 JSON textarea:`DefinitionEditor`(题目定义薄组合,按模式渲染切片;切片组件 `DefinitionRuntimeSection`/`DefinitionFlagInjectionSection`/`DefinitionCheckerSection`/`DefinitionFlagTemplateSection`/`DefinitionPatchSection` 共享 `useDefinitionModel` 的 model,题库编辑页按「基本信息/运行环境/模式定义」Tab 分别引用切片)、`CompetitionModeConfigEditor`(竞赛配置)、`ChallengeRulesEditor`(题目规则,字段可「覆盖/继承竞赛默认」)。解析/序列化与 schemaVersion、枚举整数编码全部走 `utils/game-config.ts`;字段或结构变化时先改该文件。长表单内部分组用 `DefinitionSection`(标题+可选 Accordion 折叠):常用组 `:collapsible="false"` 固定展开,高级组默认折叠且数据非空时自动展开(`:default-open`),不要平铺十几个无层级的全宽 Field。
- 路由级多分区导航(竞赛工作区、竞赛管理、平台管理)一律用 `AppWorkspaceNav` 图标侧边栏,禁止再用 Tabs/横排幽灵按钮做路由导航;Tabs 仅用于单页内的内容切换(如账户页、题目详情页)。
- 分页一律签名 keyset cursor +「加载更多」(useCursorPagination),不要页码;改筛选必须 `reset()`。
- 异步操作(提交/runtime/重判等)返回 202 时用 usePolling 轮询 statusUrl;SignalR 推送只做失效重取,REST 为事实源。开发环境下两个 Hub 强制走 SSE/长轮询(`import.meta.dev` 分支):Vite ws 代理转发 SignalR WebSocket 会被重置并引发 Nuxt 崩溃重启循环,生产直连后端不受影响。
- 管理端写操作不携带持久化修订并发字段；可变记录采用 last-write-wins。409 只按生成 SDK 的强类型业务失败码展示，不得统一翻译为修订冲突。
- shadcn 规则:表单 FieldGroup+Field;Dialog/Sheet 必带 Title;语义色类(禁原始色值);gap-* 不用 space-*;size-*;条件类 `cn()`;空态 Empty、提示 Alert、骨架 Skeleton、状态 Badge;按钮加载态 Spinner+data-icon+disabled;图标用 `@lucide/vue`。
- 开发环境会话恢复依赖后端 `appsettings.Development.json` 的 `Authentication:RefreshAllowedOrigins` 包含前端源(已配 127.0.0.1/localhost:3000-3001);新增端口要同步加。
- 组件 style preset 为 `reka-vega`(components.json),正文 Inter;新增/更新组件统一 `bunx --bun shadcn-vue@latest add <name>`,apply/init preset 会重写 `main.css` 配色变量,之后需回合下方品牌色定制。
- 主题默认深色,切换走 `useTheme()`(vueuse `vueuse-color-scheme` 持久化,nuxt.config head 脚本防首帧闪烁);品牌蓝(#2563eb)token 与浅/深两套配色在 `assets/css/main.css`,背景刻意带蓝色调、不做纯白。
- 排版约定:标题用 `text-display` 工具类(main.css 定义,字重+字距),全局 h1-h3 已带 `tracking-tight`;终端光标闪烁用 `animate-blink`(如品牌 wordmark `> name _`,见 layouts/default.vue);数据用 `font-mono`(JetBrains Mono)+ `tabular-nums`。
- 新 UI 文案在两个 locale 文件同时添加资源；静态文案用稳定 key 调用 `$t` / `t` / `translate`。配置标签保存 key，渲染时翻译，禁止在模块加载时固定当前语言。已解析的错误/反馈用 `$message` 渲染，已知中英文反馈随语言切换，未知服务端文本和用户内容原样保留。不得恢复中文原文作 key。
- 题目方向(Web/Pwn/Crypto 等)的图标与颜色一律走 `utils/directions.ts` 映射表(`directionIcon`/`directionTextClass`/`directionBadgeClass`),禁止局部硬编码方向色;分数、排名、时间等数据用 `font-mono`(JetBrains Mono)+ `tabular-nums`。
- 发布构建由 `NoCTF.API.csproj` 驱动(`bun install --frozen-lockfile` + `bun run generate`),产物在 `.output/public`。
- 改依赖后提交更新后的 `bun.lock`。
