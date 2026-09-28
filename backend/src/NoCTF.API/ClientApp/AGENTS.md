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
- `composables/`:`useAuth`(会话/角色)、`usePlatform`(品牌)、`useCursorPagination`(keyset「加载更多」)、`usePolling`(202+statusUrl 轮询)、`useCompetitionHub` / `usePlatformLogHub`(SignalR 实时失效)。管理配置使用生成 SDK 的 `mode`/`kind` 枚举加互斥 nullable 分支对象，不维护自由 JSON model。
- `lib/`:`session.ts`(内存 access token + refresh 单飞,供拦截器使用,禁 localStorage)、`admin-competition.ts`(竞赛管理角色注入)。
- `utils/`:`api-error.ts`(ApiError/parseApiError/statusErrorMessage,problem+json 解析与空响应体的状态码兜底文案)、`labels.ts`(枚举中文标签)、`admin-format.ts`、`download.ts`(带 Bearer 的 blob 下载)。模式配置必须使用生成 SDK 的强类型结构，禁止自建 JSON schema 兼容器。
- `middleware/`:`auth` / `guest` / `platform-admin`,经 `definePageMeta` 使用。
- `pages/`、`layouts/`、`app.vue`:仅路由/布局元数据与功能入口组合，不持有业务代码或私有原语。路由仍为公开区、选手区、认证、竞赛管理、题库与平台管理；账户功能由顶栏 `features/account/AccountPanel` 承载，不建立独立账户设置页。
- `locales/zh-CN.ts`、`locales/en.ts`:使用同一组稳定资源 key，中英文 key 与插值参数必须一致。修改文案不修改 key。页面、组件、默认属性、占位符、无障碍名称、通知与错误文案不得硬编码中文或英文；用户内容、协议值、URL 和代码示例数据按其真实语义处理，不翻译用户内容。

## 约定

- 全站控件遵循根 `DESIGN.md` 与已批准的 `UI-REDESIGN-PROPOSAL.md`：20px 圆角卡片，45% 不透明背景、12px 背景高斯模糊与右下阴影，悬停上移 4px / 180ms；控件 10px 圆角、常规 40px 高，浅色品牌蓝/冷蓝灰、深色 #39FF14 强调色。
- 页面和业务视图禁止直接使用原生 form、日期/数值/文件等选择类型、DOM title 提示、window.alert/confirm/prompt。分别使用 `UiForm`、`DateTimePicker`、`NumberInput`、`FileUpload`、`Hint` 和共享对话框。原语内部可保留必要 HTML 语义与隐藏系统文件 input。
- 已有完整业务校验的表单使用 `UiForm validation="feature"`；其他表单由 UiForm 显示自定义约束反馈。业务规则仍在功能层，不将校验、请求或导航处理放进 View。
- 滚动需求通过 `ScrollSurface` 或共享组件的 `data-scroll-surface` 标记声明；禁止在业务视图私建滚动条。滑块默认隐藏，悬停/滚动/键盘操作时显示，800ms 后隐藏，颜色随主题。
- 普通文本、密码、数值、多行与组合输入框使用 80% 背景不透明度，通过 field-surface token 在亮暗主题下与卡片区分，Flag 伪终端保持透明。选择与上传控件同主卡片使用 45% 背景不透明度；文字和图标不降低透明度。菜单和弹层保持不透明，按钮样式按 variant 全站统一，卡片不覆盖按钮颜色和透明度；主按钮使用主题色与柔光，禁用时关闭光晕。颜色、阴影、滚动条皮肤集中在 `main.css`；Canvas/WebGL 色值通过 `themeColor` 获取。禁止业务组件重新定义原始颜色或私有阴影。
- 错误提醒、校验摘要和操作通知统一使用右下角小型弹窗，从右向左滑入，字体为 14px 微软雅黑、700 字重，并复用 SVG 状态装饰。`Alert` 是受所属组件生命周期控制的浮动通知；`toast.*` 使用同一 Sonner 队列。辅助说明用 FieldDescription，确认流程继续用 Dialog/AlertDialog。
- 右下角通知必须在插槽内容变化时重新测量高度并更新 Sonner 堆叠偏移，达到视口上限后才使用内部 ScrollSurface。`parseApiError` 优先采用字段级 validation errors，带稳定 code 的 400 保留具体业务原因，空 400 使用调用处的操作级 fallback；不得用统一“参数有误”覆盖已有具体上下文。
- Dialog、DialogScrollContent 与 AlertDialog 的可见表面必须由共享 Card 原语渲染，统一继承卡片透明度、模糊、圆角、阴影和装饰；业务弹窗不得再自行实现一层原生卡片，也不得在 Card 弹窗内嵌套另一张 Card。
- WriteUp 审核页的队伍导航使用 ChoiceSidebar / WaveSelectionList，选择和路由同步留在 feature。PDF 在线预览统一使用 PdfPreview 内的 PDF.js Worker + Canvas，以及共享 Button、ScrollSurface、Skeleton、Spinner、Empty；禁止业务 View 或 PdfPreview 使用 iframe、object、embed 及浏览器原生 PDF 查看器。
- 开发环境的 `/__ui-check` 用于交互预览，不调用业务写接口；生产构建必须通过 `pages:extend` 排除此路由。不得将验证用临时文件作为应用依赖。
- 新增动效、View Transition 选择器和命名合成层声明放在独立的 `app/motion/` 库中，由 UI 原语调用预设；组件不内置动画定义，不引入业务状态，必须支持减少动态效果模式。`view-transition-name` 只能在实际过渡 dataset 存在时临时生效，禁止永久施加到 DefaultLayout 前景或壁纸，避免切断 Card 对背景的 `backdrop-filter` 采样。列表键盘交互留在通用原语，竞赛选择与 URL 同步留在功能层。
- 中英文切换通过 `motion/locale-layout.ts` 测量共享按钮、Badge、Tabs 与选择控件的固有宽度，并在 500ms 内完成横向延展或收缩；动效不得写入业务 View，减少动态效果模式立即完成切换。
- 普通路由使用 Nuxt 全局 `noctf-page-slide`，只横向移动 DefaultLayout 的 main 页面内容；顶部栏和布局壁纸不得进入页面过渡。主内容通过 `data-slot="page-transition-viewport"` 裁切横向溢出。竞赛页和消息中心的 ChoiceSidebar 必须在页面网格内稳定占位并与右侧 Card 同步横移，不得在带 transform 的页面根节点下使用 fixed 定位。减少动态效果模式取消位移。
- 竞赛计分板在固定高度竞赛工作区内必须由纵向 ScrollSurface 承载整页内容，表格继续使用自身横向滚动面；不得让 `data-contained-workspace-page` 的 overflow 裁掉趋势图或队伍表格。
- CTF 3D 大屏固定使用自身 `.dark` 容器的大屏专用语义颜色生成 WebGL 材质，不能从 document 根主题或站点品牌主色取色。舞台使用近黑紫背景、紫蓝网格、冷蓝未攻克建筑和高饱和红色已攻克建筑，绿色只用于成功分值与实时状态；唯一大屏路由为 `/competitions/:id/live`。
- CTF 3D 建筑的一血、二血、三血标签从完整的公开解题记录生成；右侧实时战报才截取最新 10 条。不得从已截断的战报列表反推建筑血榜，否则早期血榜会消失。
- AWDP 大屏使用独立于站点主题和品牌色的深黑青 HUD 色域：青蓝用于结构线、标题与等待态，攻击成功使用红色，防御成功使用青绿色，攻击/防御失败统一使用橙色，正文使用冷白与蓝灰层级。所有 AWDP 子动画与底部 ticker 必须继承同一语义映射。
- AWDP 大屏的顶部统计、左侧动态流、中央播报、右侧排行榜与队伍动态、底部 ticker 必须使用青蓝内描边明确板块边界；标题栏与页脚使用较弱分隔线，题目卡片和 ticker 卡片保留独立细描边。全站无边框规则下使用共享阴影叠加 inset 描边，不能恢复浏览器 border。
- 使用嵌套 NuxtPage 或多层组件根的路由必须在 pages 入口提供可接收 transition class 的真实 DOM 根节点。平台管理使用 `data-slot="platform-admin-page"`，离场期间必须保留侧栏和当前设置内容直至 leave 完成。
- 需要固定视口高度和纵向裁切的竞赛页面根节点必须声明 `data-contained-workspace-page`。DefaultLayout 使用稳定的 `data-slot="default-layout"`，其 `100dvh` / overflow 规则只能通过当前 main 子页面标记生效；禁止用 route 派生 class 控制高度，避免离场卡片提前解除约束或进入中的普通页面被截断。
- 公开竞赛介绍只使用实体路由 `/competitions/<id>`：左侧选择、右侧介绍与报名入口，窄屏上下排列。题目详情只使用 `/competitions/<id>/challenges/<competitionChallengeId>`；实体标识不得回退到查询参数，筛选、弹窗等非实体状态继续使用查询参数。
- 独立竞赛管理列表和新建页已移除；平台管理员只在竞赛页右上角通过 CreateCompetitionDialog 新建竞赛，成功后关闭弹窗、写入现有列表并选中新竞赛。当前竞赛管理入口仍位于“我的队伍”右侧；管理员列表包含草稿和已删除竞赛，已删除项使用专属分类。全部 `/admin/competitions/<id>` 管理路由使用 `platform-admin` 中间件，普通用户和 Organizer 不显示入口。
- 题库管理列表按 includeDeleted 筛选保留最近成功快照；重新进入页面先显示快照并后台刷新，刷新失败保留现有行。筛选切换和组件卸载必须使旧请求失效，禁止过期响应覆盖当前列表。
- 竞赛列表通过 CompetitionSidebar 功能组件组合 FloatingSidebar / WaveSelectionList 原语，直接固定在背景上，不使用卡片外框。波浪形悬停与选中外凸由 `app/motion/useWaveMotion.ts` 管理，悬停不更改竞赛选择；当前以桌面端为准。
- 竞赛与题目列表共用 ChoiceSidebar 组合原语；题目页按视口限制高度，采用组件内部 ScrollSurface，不做页面级滚动。题目侧栏提供按名称搜索并与隐藏已解出组合过滤；方向抽屉默认收起，仅当前选中题目所在方向自动展开。默认题目条目固定为 40px，仅显示题名、必要状态与分数。未选中项持续悬停 50ms 后才开始 800ms 展开，离开立即开始 800ms 收缩；键盘聚焦不等待。当前选中题目始终保持 112px 展开。题目卡片不增加选中或已解外圈光晕；展开态默认显示方向水印，并允许 StatusIcon 与三枚 BloodMark 自身光晕越过裁切边界，其中选中分组单独保留底部光晕安全区。展开态保持透明，不增加白色覆盖层、背景模糊或阴影。默认布局不显示站点页脚。
- 消息中心复用 ChoiceSidebar / WaveSelectionList 列表侧边栏与 Card 详情容器；分页操作放在 ChoiceSidebar footer，详情正文和后续记录使用 Card 内部 ScrollSurface。通知切换复用竞赛详情的 MotionSwap / film-up 动效，外层 Card 不参与动画。通知选择、URL 同步、线程读取与操作目标解析留在 features/notifications。
- 赛事播报由 `CompetitionEventCommitted` 经事务 Outbox、Worker Redis 发布、API SignalR Relay 后触发客户端 REST 重取。播报查询窗口不得在赛事结束后冻结，也不得只以浏览器时钟作为上限；必须至少覆盖 `competitionEventChanged.occurredAt`，以容忍客户端与服务端时钟差。
- 首页主视觉填满导航栏下方视口，不再展示近期竞赛列表；状态区使用透明 PseudoTerminal 原语，英文字体为本地 JetBrains Mono，中文回退微软雅黑，输入提示符为 `noctf $`，空输入持续显示共享 motion 闪烁光标。支持 `help`、`ls`、`status`、`whoami`，回车后输入清空且当前输出替换旧输出，不保存或导航终端历史；当前输出使用共享 useTypewriterMotion 逐字呈现并支持减少动态效果，业务数据请求与命令解析留在功能层。
- 首页 View 直接组合信号装饰层以连接品牌区与终端；四条线路与节点来自 `assets/svg/decorations/home-signal-paths.svg`，主题着色和遮罩集中在 main.css，移动高光放在 motion.css，装饰不接收指针事件并支持减少动态效果。不要为这一处静态标签组合创建单独共享原语。
- 咨询页使用 ConversationPanel / MessageBubble 共享原语，消息与输入区按 3:1 分配并独立滚动，自己的消息按当前用户 ID 判定后靠右。发送位于输入区右下，解决和关闭操作位于左侧当前咨询条目下方；权限、消息与状态请求由功能层处理。
- 顶部主题调色板由 features/theme 控制浏览器本地偏好，亮暗主题分别保存；共享 ColorPicker / ColorSwatch 原语负责颜色编辑交互，不使用原生 color/range 控件。主题插件校验 HEX 后设置 user-primary token，语义 token 派生强调色与按钮前景，恢复默认时移除覆盖。新增用色继续使用主题 token。
- 人机验证总开关下的 Runtime 与 Evaluation 策略相互独立；Evaluation 控制 Flag 提交及只读 Break 判定。客户端只根据公共 `evaluationRequired` capability 决定是否弹出 HumanVerificationGate，服务端中间件继续执行最终策略，管理端保存 `evaluationEnabled`。
- 顶部栏使用 80px 透明承载层及三个按内容宽度自适应的腰圆。左侧为平台 Logo 锁定；中间依次容纳竞赛、题库管理、平台设置和消息中心；右侧依次容纳中英切换、主题调色板、明暗切换和头像。腰圆使用主题 Card 表面、12px 模糊和统一投影。导航入口保留固定宽度槽位和展开动效。32px 无框头像触发 AccountPanel Popover，不使用 DropdownMenu；桌面端正方形主 Card 以头像为水平中线显示，窄屏保留末端对齐。账户摘要区直接进入个人页；设置详情以同宽但不定高的子 Card 在主 Card 下方向下展开并通过 ScrollSurface 滚动，不显示上传规格、字符计数或解释性注释。
- 中间顶栏导航项的悬停和键盘焦点仅使用胶囊内侧柔光；不要叠加 `ghost` 底色或会被横向滚动面裁成矩形的外阴影。保留活动项的主题色下划线。
- 登录与注册使用 `/auth/login`、`/auth/register` 独立页面，顶部栏、首页与鉴权拦截直接指向对应路由。认证表单 Card 使用 `max-w-xl` 预留横向扩展空间；鉴权中间件携带安全的站内 `redirect`，登录成功后返回原目标。
- 除首页 `/` 外，浅色与深色模式页面分别使用本地 `assets/images/backgrounds/light-pages-wallpaper.jpg` 和 `dark-pages-wallpaper.jpg` 固定壁纸；两者均以 35% 图像强度叠在各自 background token 上，只影响布局背景。页面内容与卡片不继承壁纸透明度，壁纸必须与普通页面 Card 保持在同一可采样合成上下文中。
- Nuxt 保持 `ssr: false`；搜索与分享平台所需的全局平台名称、描述、Logo、Open Graph 与 JSON-LD 由 API 的 SPA HTML fallback 从平台配置动态注入，不能只依赖客户端 `useHead`。元数据必须安全转义并生成绝对 URL，API/Hub/健康检查/静态资源不得被 fallback 接管。
- Flag 输入统一使用 TerminalCommand 伪终端原语，空输入提示为主题色微软雅黑斜体 `Type Your Flag ~~`；回车提交、IME 组合确认不提交，批量模式 Shift+Enter 换行。普通 CTF Flag 正确后由 `Challenge is Solved ！` 直接替换原占位文字；仅本次正确判定弹出一次成功通知，题目详情恢复已解状态或重新进入时不弹。题目详情接口通过 `SolvedByMyTeam` 恢复本队已解状态，但已解状态不得禁用输入，后续 Flag 仍须正常判定为 Correct 或 Wrong。提交记录在 Dialog 中按需挂载，不在详情页内常驻表格；业务提交和历史读取仍由功能层管理。
- 题目列表的一血、二血、三血标记保持横向图标排列，悬停提示显示排行榜快照中的队伍名；队伍名缺失时才回退到带标签的 Team ID，常驻界面不显示队名或 UUID。
- 全局组件与原语不显示边框、outline 或 ring 框线；使用背景、投影和光晕表达层次、焦点及错误。统一规则由 main.css 覆盖，保留 SVG 图标笔画。竞赛侧栏状态选择器、下拉面板与列表选中/悬停底色完全透明，文字和图标保持可见。

- API 调用一律走 `app/api` SDK:`const { data, error } = await xxxEndpoint({ path, query, body })`;错误统一 `parseApiError(error)` 取 message,toast 用 vue-sonner 的 `toast()`。空响应体的错误(如登录 401)由 `plugins/api.client.ts` 的 error 拦截器按状态码合成文案,登录页 401 显示「用户名或密码错误」。
- 模式专属管理配置一律使用 OpenAPI 生成的枚举加互斥 nullable 分支对象请求/响应和结构化编辑器；`mode`/`kind` 与唯一非空分支必须匹配，JSON 属性顺序无关。禁止原始 JSON textarea、schemaVersion upgrader 或自由 JSON 解析。`DefinitionEditor`、`CompetitionModeConfigEditor`、`ChallengeRulesEditor` 只编辑当前分支。长表单内部分组用 `DefinitionSection`；常用组固定展开，高级组默认折叠且数据非空时自动展开。
- 竞赛管理与平台管理的路由级多分区导航使用 AppWorkspaceNav 组合 ChoiceSidebar / WaveSelectionList，不使用旧 SidebarProvider / SidebarInset；桌面端为网格内 sticky 侧栏，移动端位于内容上方。Tabs 仅用于单页内内容切换。
- 竞赛管理与平台管理默认共用固定视口的 `data-workspace-scroll-content` 内容区：页面本身不滚动，标题保持固定，子页面由纵向 ScrollSurface 独立滚动，并用 MotionSwap 执行路由内容切换。赛事题目列表及详情、队伍管理、闯关编排页例外，取消工作区固定高度与内部纵向滚动，使用 DefaultLayout 页面主滚动；桌面端左侧导航整列 sticky，不随右侧页面滚动，窄屏仍按正常页面流排列。Vue Flow 画布仍保留自身拖拽与缩放。赛事题目列表先完成搜索/方向/状态筛选，再用 OffsetPagination 对结果分页；队伍管理继续使用服务端分页。竞赛概览、配置、排行榜、导出与权限页把同一任务域的分区合并到一张连续 Card，以 Separator 划分。
- DefaultLayout 主滚动面与竞赛管理、平台管理的持久 ScrollSurface 必须以当前路由路径作为 `resetKey`，确保切换页面后复位到顶部；查询筛选、弹窗和普通局部滚动不使用该键。
- 公开个人资料页固定在导航栏下方视口内，不产生页面级滚动；上下两层等宽，上层高度为 0.618fr 的较短身份标签且头像位于左上角，下层高度为 1fr 的较高技术画像。下层在宽屏用三列分别展示赛制分布、擅长方向及近期赛事／当前有效的公开比赛勋章，窄屏在卡片内部滚动；三个原有分区标题不显示图标。方向雷达只以公开且已开赛（运行中、暂停或结束）赛事的已发布题目中实际出现过的方向为轴，按题目创建方向目录排序，暂无成绩的方向补 0；少于三个方向时展示数值列表。勋章默认只显示图片和名称，点击后显示详情。个人标签装饰图使用独立 ProfileCover 上传与公开读取接口，上传前复用共享裁切器按 4:1 宽幅比例自定义位置、缩放与旋转，不得复用或改变个人壁纸状态。
- 普通列表使用 offset/limit/total 页码分页（useOffsetPagination + OffsetPagination）；改筛选必须回到第一页。通知增量 feed 与 Redis 平台日志保留签名/opaque cursor +「加载更多」(useCursorPagination)。
- 异步操作(提交/runtime/重判等)返回 202 时用 usePolling 轮询 statusUrl;SignalR 推送只做失效重取,REST 为事实源。开发环境下两个 Hub 强制走 SSE/长轮询(`import.meta.dev` 分支):Vite ws 代理转发 SignalR WebSocket 会被重置并引发 Nuxt 崩溃重启循环,生产直连后端不受影响。
- 管理端写操作不携带持久化修订并发字段；可变记录采用 last-write-wins。409 只按生成 SDK 的强类型业务失败码展示，不得统一翻译为修订冲突。
- shadcn 规则:表单 FieldGroup+Field;Dialog/Sheet 必带 Title;语义色类(禁原始色值);gap-* 不用 space-*;size-*;条件类 `cn()`;空态 Empty、提示 Alert、骨架 Skeleton、状态 Badge;按钮加载态 Spinner+data-icon+disabled;图标用 `@lucide/vue`。
- 开发环境会话恢复依赖后端 `appsettings.Development.json` 的 `Authentication:RefreshAllowedOrigins` 包含前端源(已配 127.0.0.1/localhost:3000-3001);新增端口要同步加。
- 组件 style preset 为 `reka-vega`(components.json),全站文字微软雅黑;新增/更新组件统一 `bunx --bun shadcn-vue@latest add <name>`,apply/init preset 会重写 `main.css` 配色变量,之后需回合下方品牌色定制。
- 主题默认深色,切换走 `useTheme()`(vueuse `vueuse-color-scheme` 持久化,nuxt.config head 脚本防首帧闪烁);浅色品牌蓝、深色 #39FF14 token 与两套配色在 `assets/css/main.css`,背景刻意带蓝色调、不做纯白。
- 排版约定:标题用 `text-display` 工具类(main.css 定义,字重+字距),全局 h1-h3 已带 `tracking-tight`;终端光标闪烁用 `animate-blink`(如品牌 wordmark `> name _`,见 layouts/default.vue);数据用 `font-mono`（字体同样映射为微软雅黑）+ `tabular-nums`。
- 页面、分区与列表标题只显示标题和必要业务状态；不在标题下重复解释页面用途、内容范围或键盘操作。会影响决策的权限、风险、表单约束、错误与空状态反馈继续保留。
- 新 UI 文案在两个 locale 文件同时添加资源；静态文案用稳定 key 调用 `$t` / `t` / `translate`。配置标签保存 key，渲染时翻译，禁止在模块加载时固定当前语言。已解析的错误/反馈用 `$message` 渲染，已知中英文反馈随语言切换，未知服务端文本和用户内容原样保留。不得恢复中文原文作 key。
- 题目方向(Web/Pwn/Crypto 等)的图标与颜色一律走 `utils/directions.ts` 映射表(`directionIcon`/`directionTextClass`/`directionBadgeClass`),禁止局部硬编码方向色;分数、排名、时间等数据用 `font-mono`(JetBrains Mono)+ `tabular-nums`。
- 项目自绘的静态 SVG 资源统一放在 `app/assets/svg/` 下，方向图标放在 `directions/`、装饰图样放在 `decorations/`；Vue/TypeScript 只通过资源 URL 引用，不内联 path 数据。数据驱动的动态图表不作为静态资源处理。
- 题目列表详细态使用共享 IconWatermark 显示方向 SVG 水印，收缩态隐藏水印，方向文字只作为辅助技术文本；分组标题继续显示小图标和方向名称。Mock 为每个支持方向提供一题和一个可下载附件，数据与下载内容仅放在 mock/。
- 题目进度标记使用共享 StatusIcon 与 `app/assets/svg/status/`：CTF 已解出、AWDP 攻击成功、防御成功、攻防均成功必须使用四个不同 SVG，并提供非颜色依赖的可访问名称。状态判断留在功能层。
- 参赛题目详情中的 FlagSubmit 通过可选 dockTarget 挂载到详情卡片底部独立区域，不进入详情 ScrollSurface；CTF、AWD、AWDP 面板只转发布局目标，提交状态和业务命令仍由 FlagSubmit 功能层拥有。
- 参赛题目环境通过 RuntimeCard 的可选 dockTarget 挂载到描述后的资源行左侧，附件位于右侧；All 策略附件使用单一文件名按钮下载，不再拆分文件信息和通用下载按钮。Runtime 业务状态仍由各赛制功能层拥有。
- 剩余 Flag 次数使用 RemainingAttempts 原语；低于 5 时仅数字使用 `--critical-attempt` 纯红色。题目提示使用 MarkdownQuote 以 blockquote / `>` 语义渲染，继续经过共享 Markdown 安全清洗。
- Markdown 统一通过 `lib/markdown.ts` 解析与清洗，Fence 代码使用 `lib/markdown-highlighter.ts` 的 Highlight.js core 按需语言注册；未知语言保持转义纯文本。高亮的 `hljs-*` 类和 `data-language` 必须进入明确清洗白名单。标题、列表、引用、代码、表格、媒体、折叠块与分隔线的装饰集中在 main.css 的 `.markdown-content` 规则中，不在业务页面重复定义。
- 发布构建由 `NoCTF.API.csproj` 驱动(`bun install --frozen-lockfile` + `bun run generate`),产物在 `.output/public`。
- 改依赖后提交更新后的 `bun.lock`。
