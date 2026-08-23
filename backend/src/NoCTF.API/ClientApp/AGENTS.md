# NoCTF ClientApp 前端约定

Nuxt 4 SPA(`ssr: false`),Bun 管理依赖,TypeScript strict,Vue 用 `<script setup lang="ts">` Composition API。

## 命令

- `bun run dev`:开发(端口 3000,vite proxy 把 `/api`、`/hubs`、`/health` 转发到 `http://localhost:5080`)。
- `bun run typecheck`:全量类型检查,改动后必须通过。
- `bun run api:gen`:从 `../wwwroot/openapi/v1.json` 重新生成 API SDK 到 `app/api/`(@hey-api/openapi-ts,配置在 `openapi-ts.config.ts`)。后端接口变更后先重新导出 OpenAPI(`dotnet run -- --export-openapi`)再跑此命令;生成产物提交入库。
- shadcn-vue 组件用 `bunx --bun shadcn-vue@latest add <name>` 添加(CLI 一律走 bunx)。

## 结构(app/ 下)

- `api/`:hey-api 生成产物(sdk.gen.ts / types.gen.ts / client),不要手改。
- `components/ui/`:shadcn-vue 组件,自动导入无前缀(`<Button>` 等);`components/<领域>/` 放业务组件,组件名 = 文件名(nuxt.config 已设 `pathPrefix: false`,**全局禁止重名**)。`components/app/` 放应用级共享组件:`AppWorkspaceNav`(路由级多分区工作区的侧边栏导航,配置走 `workspace-nav.ts` 的 `WorkspaceNavGroup`)。
- `composables/`:`useAuth`(会话/角色)、`usePlatform`(品牌)、`useCursorPagination`(keyset「加载更多」)、`usePolling`(202+statusUrl 轮询)、`useCompetitionHub` / `usePlatformLogHub`(SignalR 实时失效)、`useDefinitionModel`(题目 definitionJson 的 parse/serialize/回灌去重,供 DefinitionEditor 与各切片组件共享一份 model)。
- `lib/`:`session.ts`(内存 access token + refresh 单飞,供拦截器使用,禁 localStorage)、`admin-competition.ts`(竞赛管理角色注入)。
- `utils/`:`api-error.ts`(ApiError/parseApiError/statusErrorMessage,problem+json 解析与空响应体的状态码兜底文案)、`labels.ts`(枚举中文标签)、`admin-format.ts`、`download.ts`(带 Bearer 的 blob 下载)、`game-config.ts`(游戏模式专属配置 JSON 的解析/序列化模型与字段描述)。
- `middleware/`:`auth` / `guest` / `platform-admin`,经 `definePageMeta` 使用。
- `pages/`:公开区(`/competitions/**`、首页)、选手区(竞赛工作区子路由含 my/*)、认证(`/auth/*`)、账户(`/account`)、竞赛管理(`/admin/competitions/**`)、题库(`/admin/challenges/**`)、平台管理(`/admin/platform/**`)。

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
- 新写中文 UI 文案必须在 `app/locales/en.ts` 补英文资源(tests/i18n.test.ts 强制),英文值不得含汉字。
- 题目方向(Web/Pwn/Crypto 等)的图标与颜色一律走 `utils/directions.ts` 映射表(`directionIcon`/`directionTextClass`/`directionBadgeClass`),禁止局部硬编码方向色;分数、排名、时间等数据用 `font-mono`(JetBrains Mono)+ `tabular-nums`。
- 发布构建由 `NoCTF.API.csproj` 驱动(`bun install --frozen-lockfile` + `bun run generate`),产物在 `.output/public`。
- 改依赖后提交更新后的 `bun.lock`。
