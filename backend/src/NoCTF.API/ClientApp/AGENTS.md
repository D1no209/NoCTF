# NoCTF ClientApp 前端约定

Nuxt 4 SPA(`ssr: false`),Bun 管理依赖,TypeScript strict,Vue 用 `<script setup lang="ts">` Composition API。

## 命令

- `bun run dev`:开发(端口 3000,vite proxy 把 `/api`、`/hubs`、`/health` 转发到 `http://localhost:5080`)。
- `bun run typecheck`:全量类型检查,改动后必须通过。
- `bun run api:gen`:从 `../wwwroot/openapi/v1.json` 重新生成 API SDK 到 `app/api/`(@hey-api/openapi-ts,配置在 `openapi-ts.config.ts`)。后端接口变更后先重新导出 OpenAPI(`dotnet run -- --export-openapi`)再跑此命令;生成产物提交入库。
- shadcn-vue 组件用 `bunx --bun shadcn-vue@latest add <name>` 添加(CLI 一律走 bunx)。

## 结构(app/ 下)

- `api/`:hey-api 生成产物(sdk.gen.ts / types.gen.ts / client),不要手改。
- `components/ui/`:shadcn-vue 组件,自动导入无前缀(`<Button>` 等);`components/<领域>/` 放业务组件,组件名 = 文件名(nuxt.config 已设 `pathPrefix: false`,**全局禁止重名**)。
- `composables/`:`useAuth`(会话/角色)、`usePlatform`(品牌)、`useCursorPagination`(keyset「加载更多」)、`usePolling`(202+statusUrl 轮询)、`useCompetitionHub` / `usePlatformLogHub`(SignalR 实时失效)。
- `lib/`:`session.ts`(内存 access token + refresh 单飞,供拦截器使用,禁 localStorage)、`admin-competition.ts`(竞赛管理角色注入)。
- `utils/`:`api-error.ts`(ApiError/parseApiError,problem+json 解析)、`labels.ts`(枚举中文标签)、`admin-format.ts`、`download.ts`(带 Bearer 的 blob 下载)。
- `middleware/`:`auth` / `guest` / `platform-admin`,经 `definePageMeta` 使用。
- `pages/`:公开区(`/competitions/**`、首页)、选手区(竞赛工作区子路由含 my/*)、认证(`/auth/*`)、账户(`/account`)、竞赛管理(`/admin/competitions/**`)、题库(`/admin/challenges/**`)、平台管理(`/admin/platform/**`)。

## 约定

- API 调用一律走 `app/api` SDK:`const { data, error } = await xxxEndpoint({ path, query, body })`;错误统一 `parseApiError(error)` 取 message,toast 用 vue-sonner 的 `toast()`。
- 分页一律签名 keyset cursor +「加载更多」(useCursorPagination),不要页码;改筛选必须 `reset()`。
- 异步操作(提交/runtime/重判等)返回 202 时用 usePolling 轮询 statusUrl;SignalR 推送只做失效重取,REST 为事实源。
- 管理端写操作带 expectedRevision 乐观锁,409 提示刷新后重取。
- shadcn 规则:表单 FieldGroup+Field;Dialog/Sheet 必带 Title;语义色类(禁原始色值);gap-* 不用 space-*;size-*;条件类 `cn()`;空态 Empty、提示 Alert、骨架 Skeleton、状态 Badge;按钮加载态 Spinner+data-icon+disabled;图标用 `@lucide/vue`。
- 开发环境会话恢复依赖后端 `appsettings.Development.json` 的 `Authentication:RefreshAllowedOrigins` 包含前端源(已配 127.0.0.1/localhost:3000-3001);新增端口要同步加。
- 发布构建由 `NoCTF.API.csproj` 驱动(`bun install --frozen-lockfile` + `bun run generate`),产物在 `.output/public`。
- 改依赖后提交更新后的 `bun.lock`。
