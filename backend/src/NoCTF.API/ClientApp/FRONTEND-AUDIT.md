# ClientApp 前端审计报告（2026-02 轮次）

审计对象：`backend/src/NoCTF.API/ClientApp/app`（1048 个 .vue/.ts/.css，排除 `app/api/` 生成代码）。
方法：`bun run audit:architecture` + 自建 AST/资产/重复度扫描（`@vue/compiler-sfc` + TypeScript 编译器 API），逐条人工复核。

## 0. 基线：已有的护栏

仓库已有一层实际生效的强制隔离，本轮审计不以重复其结论为目标：

| 层级 | 文件数 | 行数 | 护栏状态 |
|---|---|---|---|
| `components/ui`（唯一原语层，自动导入） | 223 vue + 57 ts | 7,504 | 禁止 import features/api/composables |
| `components/views`（纯渲染） | 159 vue | 15,030 | setup 只能 `defineProps/defineOptions/toRefs`；运行时 import 仅允许 `vue`（实测 158/158 通过） |
| `features`（控制器 + 组合入口） | 158 vue + 223 ts | 28,516 | 组合入口禁止渲染非 `*View` 元素、禁止自带样式 |
| `pages` / `layouts` | 96 / 1 | 577 | 只允许 `definePageMeta` |

`bun run audit:architecture` 当前 **0 violation**，且 `tests/architecture.test.ts` 对规则本身有行为测试。因此下面列出的问题都是自动检查覆盖不到、但违反 AGENTS.md / ARCHITECTURE.md 契约的部分。

## 1. 结论摘要

| 要求 | 结论 | 主要问题 |
|---|---|---|
| 逻辑层与 UI/UX 渲染层物理隔离 | **基本达成，8 处语义越界** | 4 个控制器直接 import View；2 个 composable 反向依赖 features；颜色/单位换算等业务处理漏在渲染层 |
| 静态资源/SVG/动效/颜色 token/文案集中管理 | **资源与文案达标；颜色、动效严重不达标** | 两个大屏页面私有整套色板 + 内联 29KB CSS；52 个 keyframes 写在视图里；品牌色等颜色字面量跨 4 层复制 |
| 同类型渲染/功能重复实现 | **存在 7 组实质重复** | SignalR 生命周期 ×3、分页 ×3、配置编辑器 ×2、大屏控制器 ×2、runtime 面板 ×2、平台日志/审计视图 46% 同构、死原语 9 个文件 |

---

## 2. P0：违反集中化契约、污染全局或高重复

### P0-1 AWDP 大屏页把整套 HUD 主题写进 4 个非 scoped `<style>`，并注入 `:root`

`components/views/page/competitions/[id]/CompetitionsByIdAwdpLivePageView.vue`

- L163 / L167 / L173 / L204 共 4 个 `<style>`（全部没有 scoped），合计 10,488 字符；
- L164 `:root{--awdp-display…}` 直接写全局根变量；同文件定义 31 个 `--awdp-*` 私有 token（L204-L224 区段）；
- L169 起定义 `@keyframes spin` / `@keyframes pulse` / `awdp-energy-orbit` 等 7 个全局关键帧；
- 文件内用全局选择器 `.awdp-control-shell *{animation-duration:.001ms!important}` 承担减少动态效果职责。

影响：任何渲染该页的时刻都会向 `document` 注入 4 份全局样式表，覆盖同名 `spin`/`pulse` 关键帧；SPA 内多保留一份隐藏实例就会互相覆盖。控件色板本应由 `assets/css/main.css` 单一持有（AGENTS.md：「颜色、阴影、滚动条皮肤集中在 main.css」「禁止业务组件重新定义原始颜色或私有阴影」）。

### P0-2 私有色板定义在视图里，却被共享 `lib` 与子视图消费（跨层隐式契约）

- 定义方：`CompetitionsByIdLivePageView.vue:207-220` 的 `--live-city-*`（10 个原始 `oklch()` 值）、`:226-227` 派生 token；该文件 `<style scoped>` 共 18,859 字符。
- 消费方：`lib/live-city-3d.ts:236-245` 用 `themeColor('--live-city-…')` 读取这 10 个 token 生成 WebGL 材质。
  → 共享 3D 库的可用性取决于某个业务视图是否挂载。
- 定义方：`CompetitionsByIdAwdpLivePageView.vue:204-224` 的 `--awdp-*`（31 个 token，含 8 处 `color-mix` 内描边/阴影 token）。
- 消费方：`components/views/awdp-control/{AwdpEventStageView,AwdpEventTickerView,AwdpAttack/Defense*AnimationView}.vue` 共 5 个子视图。

正确归属：两套大屏语义色板应作为独立 token 区块进 `main.css`（AGENTS.md 已为 CTF 3D 与 AWDP 定义语义映射），或抽到独立 `*.css` 由 `main.css` 统一导入；`lib/live-city-3d.ts` 只保留 token 名列表。

### P0-3 动效定义散落在视图：52 个 `@keyframes`、59 条动画声明、71 个局部 token

`motion/README.md` 明确定义：**`motion.css` 拥有 timing、keyframes 与 reduced-motion**；`motion/presets.ts` 是唯一预设入口。实际：

| 文件 | keyframes | 私有 token | 动画声明 |
|---|---|---|---|
| `components/views/awdp-control/AwdpAttackSuccessAnimationView.vue` | 11 | 1 | 10 |
| `components/views/awdp-control/AwdpAttackFailureAnimationView.vue` | 7 | 2 | 7 |
| `components/views/awdp-control/AwdpDefenseSuccessAnimationView.vue` | 8 | 2 | 8 |
| `components/views/awdp-control/AwdpDefenseFailureAnimationView.vue` | 8 | 2 | 8 |
| `components/views/page/competitions/[id]/CompetitionsByIdAwdpLivePageView.vue` | 7 | 36 | 9 |
| `components/views/page/competitions/[id]/CompetitionsByIdLivePageView.vue` | 7 | 24 | 11 |
| `components/views/awdp-control/AwdpEventStageView.vue` | 4 | 0 | 5 |

其中 **2,869–4,073 字符/文件（占文件体积 70–90%）是压缩过的 CSS**，模板与逻辑每文件仅 600–1,300 字符。

重复的关键帧名（同一定义被复制到多个文件）：

```text
spin         ×5 文件   result-in    ×4 文件
pulse        ×2 文件   cube-upload  ×2 文件   scan-stage ×2 文件
```

另外 5 个子视图自身没有 `prefers-reduced-motion` 分支，完全依赖父页面那条全局 `.awdp-control-shell *` 规则；一旦子视图被复用到别处，减少动态效果立即失效。

### P0-4 SignalR 连接生命周期被实现 3 次

`composables/useCompetitionHub.ts`(261) / `useNotificationHub.ts`(96) / `usePlatformLogHub.ts`(105)：

- `useNotificationHub` 与 `useCompetitionHub` **52 行逐字相同**（占前者 65%+）：`ensureConnection` 的 builder 配置（`Accept-Language`、`getRealtimeAccessToken`、`import.meta.dev` 传输降级、`withAutomaticReconnect`、`LogLevel.Warning`）、`ensureStarted` 的 `startPromise + AbortController + startRealtimeWithRetry` 三件套、`teardownIfIdle`、订阅 `Map<symbol, handlers>` 与 `notify*` 分发。
- `usePlatformLogHub` 又把同一份 builder 配置与 start/stop 三件套重写一遍（仅连接归属改为 scope 级）。
- `import.meta.dev` 传输降级这段「Vite ws 代理会拖垮 Nuxt」的注释与代码出现在 3 处。

建议：抽 `lib/signalr-hub.ts` 的 `createHubConnection({ url, events })` + `createHubLifecycle({ scope: 'module' | 'scope' })`，三个 hub 只声明事件名与 handler 类型。

### P0-5 两套配置编辑器重复 56 行核心引擎

`features/admin/useChallengeRulesEditor.ts`(181) 与 `features/admin/useCompetitionModeConfigEditor.ts`(125)：

```text
values / parseFailed / watch 解析 readConfigValues
updateField（含 awdEditor.updateAwdField 短路）
canonicalize（递归排序 + 过滤 null）      <- 两份逐字相同
normalize / serialized / dirty（normalize 比较）
save（emit('save', serialized, changeReason)）
effectiveValues / awdEditor 构造 / AWD preset baseline 合并
```

两者差异只在 `fields` 来源（`challengeRuleFields` vs `competitionConfigFields`）与 `overridden`/`setOverride`（rules 侧独有）。应抽 `useConfigEditorCore(fields, valuesIn, { rules: boolean })`，两个 hook 变薄。

### P0-6 分页被实现 3 套（含 1 套仅 1 个页面使用）

| 逻辑 | UI | 消费者 |
|---|---|---|
| `composables/useOffsetPagination.ts` | `OffsetPagination` | 17 个页面（AGENTS 指定方案） |
| `composables/useCursorPagination.ts` | `AdminLoadMore` | 4 处 load-more |
| `composables/useCursorPagePagination.ts` | `CursorPagination` | **仅** `AdminPlatformLogsPageView` |

问题点：

1. `useCursorPagePagination` 与 `useCursorPagination` 有 26/59 行相同（generation 守卫、loadingGeneration、error、reset、hasNext 语义）；
2. `CursorPagination.vue` 名为游标、实为页码分页（props：`page/count/limit/hasPrevious/hasNext`），且与 `OffsetPagination.vue` 的「limit 选择器 + 上一页/下一页按钮」14/64 行同构；
3. AGENTS.md 明确「Redis 平台日志保留签名/opaque cursor +『加载更多』」，实现却是页码翻页 —— 文档与实现不一致；
4. 平台审计页用 `AdminLoadMore`、平台日志页用 `CursorPagination`，同类页面两种交互。

### P0-7 两个大屏控制器 + 平台日志/审计视图高重复

- `features/routes/competitions/[id]/useCompetitionsByIdLivePage.ts`(354) 与 `…ByIdAwdpLivePage.ts`(459)：**96 行逐字相同**（全屏切换、`fullscreenchange` 监听、15s 轮询 + SignalR `watchCompetition` 失效重取、时钟、`projectionTimer`/`celebrationTimer` 清理、游标翻页加载等）。
- `components/views/page/admin/platform/AdminPlatformLogsPageView.vue` 与 `AdminPlatformAuditPageView.vue`：**58/125 行完全相同**（筛选器、表格骨架、游标分页区）。
- `features/admin/useChallengeTestRuntimePanel.ts`(294) 与 `features/challenges/useRuntimeCard.ts`(275)：**72 行相同**（runtime 状态机、操作确认、轮询）。
- `features/routes/admin/competitions/[id]/useAdminCompetitionsById{Runtimes,Submissions}Page.ts`：**81 行相同**（筛选 + `useOffsetPagination` + 详情抽屉）。

---

## 3. P1：隔离/集中化的语义越界

### P1-1 控制器直接 import 渲染层 View（架构回退）

| 位置 | 导入 |
|---|---|
| `features/authentication/mfa/useMfaAccountSecurity.ts:6` | `components/views/authentication/MfaStepUpView.vue` |
| `features/authentication/mfa/useMfaPlatformSettings.ts:8` | 同上 |
| `features/authentication/mfa/useMfaUserManagement.ts:7` | 同上 |
| `features/authentication/passkeys/usePasskeyAccountSecurity.ts:9` | 同上 |
| `features/notifications/showNotificationNotice.ts:5` | `components/views/layout/NotificationNoticeContent.vue` |

按约定，控制器只应提供 `features/**/<Name>.vue` 组合入口（它再 import View）。修法：为 MFA step-up 建 `features/authentication/mfa/MfaStepUp.vue`；通知内容改由 `features/notifications/NotificationNotice.vue` 组合 `NoticeToast` 原语。

### P1-2 反向/逆向分层依赖

| 位置 | 依赖 | 说明 |
|---|---|---|
| `composables/useAdminCompetitionResource.ts:2` | `features/admin/admin-competition-resource` | 通用 composable 依赖业务单例 |
| `composables/useThemePalette.ts:1` | `features/theme/palette` | 主题调色板契约放在 features，却被 composable 与 UI 原语共同需要 |
| `features/theme/palette.ts:1` | `components/ui/color-picker/color` | 业务控制器的颜色解析依赖原语内部实现 |
| `lib/spectator-scene.ts:9` | type `features/spectator/spectator-service-state` | 共享 3D 库反向引用业务类型 |
| `utils/directions.ts:18` | type `components/ui/icons/technical-icons` | 可接受（仅类型），但方向映射依赖原语内部枚举 |

其中 `palette.ts` 是颜色契约放错层的根因：它定义 `ThemePalette`、`paletteStorageKey`、`normalizeBlur` 等共享契约，应迁至 `lib/theme-palette.ts`，让 `features/theme`、`composables/useThemePalette`、`components/ui/color-picker` 都向下依赖它。

### P1-3 颜色字面量跨层复制

| 位置 | 内容 | 应有归属 |
|---|---|---|
| `app/assets/css/main.css:164` | `#39FF14`（深色品牌色） | 唯一来源 |
| `components/ui/color-picker/ColorPicker.vue:10` | `'#39FF14'` 默认值 | 读 token / 共享常量 |
| `features/theme/useThemePalettePanel.ts:12,39` | `'#39FF14'` ×2 + 8 个 preset hex（`#0066FF`…） | 独立 `theme-presets.ts` |
| `components/ui/color-picker/color.ts:31` | `#101820` / `#F8FAFC` 对比色 | main.css token |
| `utils/theme-color.ts:39` | 兜底 `#1b2433` | main.css token |
| `features/leaderboard/useScoreboardTeamDetailDialog.ts:151-152` | ECharts `rgba(148,163,184,.28/.34)` 网格线 | 图表主题 token（AGENTS：Canvas/WebGL 色值走 themeColor） |
| `components/views/account/AvatarCropDialogView.vue:47` | `shadow-[0_0_0_999px_oklch(0_0_0/0.3)]` | 共享遮罩 token |
| `components/views/competition/CompetitionWorkspaceNavigationView.vue:30` | `shadow-[0_0_14px_color-mix(…)]` | 共享 `--focus-shadow` |

### P1-4 死原语 / 死资源（9 个组件文件 + 1 个 SVG）

| 文件 | 外部引用 |
|---|---|
| ~~`components/ui/number-field/`~~ | **更正：不是死代码。** `NumberInput.vue` 通过自动导入组合 `NumberField*`，扫描未识别到标签式用法，已保留 |
| `components/ui/scroll-area/ScrollArea.vue` | 0（只是 `ScrollSurface` 的 8 行别名） |
| `components/ui/table/TableEmpty.vue` | 0（仅 barrel 导出） |
| `components/ui/markdown/MarkdownPreview.vue` | 0（worker `lib/markdown-preview-worker.client.ts` 仍被 `markdown-editor/live-preview.ts` 使用） |
| `assets/svg/brand/noctf-wordmark-white.svg` | 0 |

同时 `components/ui/CapVerificationStatus.vue` 游离在原语目录根部（其余均按用途分目录），属目录规范噪声。

### P1-5 i18n 逃逸（少量但真实）

- 英文兜底错误被当作 UI 文案抛出：`useAdminChallengesIndexPage.ts:93`、`useAdminCompetitionsByIdTeamsPage.ts:126`、`useAdminPlatformUsersPage.ts:86`（`new Error('Failed to load …')`），以及 `useCompetitionsIndexPage.ts:16`、`useCompetitionsByIdChallengesIndexPage.ts:10` 的 `statusMessage: 'Page not found'`。
- `features/account/avatar-crop.ts:196` 英文异常；
- `features/live-solo/media/{screen-video-policy,useLiveSoloScreen}.ts` 6 条英文 codec/policy 错误串（若经 `$message` 上屏即为漏译）。

### P1-6 渲染层残留业务转换

- `components/views/theory/TheoryActivityCatalogPageView.vue:19` 模板内 `Math.round(durationSeconds / 60)` 秒→分钟换算，直接违反 ARCHITECTURE.md「单位转换处理器迁入功能控制器」。
- `components/views/page/competitions/[id]/CompetitionsByIdLivePageView.vue:187` 模板内 `new Date(solve.solvedAt).toLocaleTimeString(...)`。
- 另有 14 处轻量模板过滤/取整（`navItems.filter`、`Math.max` rowspan 等），可接受但应在 review 时区分「格式化」与「业务转换」。

---

## 4. P2：可收敛的重复与样板

1. **4 个 mode panel 组合入口完全同构**（`features/challenges/panels/{Ctf,Awd,Awdp,Koh}Panel.vue`，仅控制器与 View 名不同）；8 个 `features/admin/Definition*Section.vue` 亦为同一 `bindViewState + <View> + slot 透传` 样板。可提供 `defineFeatureView()` 宏或生成器，一次收敛约 110 个入口的重复样板。
2. `NullableNumberInput.vue`（45 行）是 `NumberInput` 的 nullable 语义包装，可与 `NumberInput` 合并为 `nullable` prop；当前 5 个页面使用、9 处调用。
3. `TagPicker` 与 `ChoicePicker` 各自重造「Popover + 搜索框 + ScrollSurface + 列表」骨架（模板标识重合 0.66）；建议抽 `SearchablePopoverList` 原语。
4. `main.css` 单文件 89KB/1,468 行：集中化达成，但无按域拆分（tokens / cards / topbar / markdown / spectator / awdp-live…），且有少量重复选择器（`:root`、`.dark`、`.spectator-*` 各 3 段）。建议拆为 `assets/css/{tokens,card,layout,live-city,awdp-hud}.css` 由 `main.css` 导入。
5. 分页尺寸常量分散：`OffsetPagination` `[10,20,50,100]`、`CursorPagination` `[20,50,100,200]`，应进共享常量。
6. 主题 preset 色（8 个 hex）未与 `main.css` 品牌 token 对齐（见 P1-3）。

---

## 5. 为什么自动检查没拦住

`scripts/check-architecture.ts` 的规则边界：

- 颜色规则（`theme-boundary`）只扫描 `components/views` 的 `<style>`，且正则是 `#hex|rgb(|hsl(` —— 因此：
  - 非 scoped / `:root` / `@keyframes` / 私有 token 完全没有规则；
  - `components/ui`、`features`、`utils`、`lib` 里的颜色字面量不检查；
  - `color-mix()` / `oklch()` / `rgba(var(--x), …)` 不在正则内（大屏页正是这样绕过）；
  - `shadow-[…]` 任意值不在检查内。
- SVG 规则（`svg-boundary`）只检查文件名后缀为 `.svg` 且不在 `assets/svg/` 的情况；不检查内联 path（当前确实为 0，属实施良好而非规则保证），也不检查未引用资源。
- 动效规则（`motion-boundary`）只检查 `.css` 中的 `view-transition-name`；不检查组件内 `@keyframes`/`animation`。
- 分层规则只覆盖 views/primitives/route-shell 三个方向；`composables → features`、`lib → features`、`controllers → components/views` 均无规则。
- 无「同功能只允许一个实现」类规则，也没有未使用原语/资源的检查。

## 6. 建议整改顺序

**第 1 批（全局污染 + 契约，收益最高）**

1. 把 `--live-city-*`、`--awdp-*` 两套色板迁到 `assets/css/`（新增 `live-city.css` / `awdp-hud.css`，由 `main.css` 引入），删掉 AWDP 页 4 个非 scoped `<style>` 中的 `:root` 与色板定义。
2. 把 CTF/AWDP 大屏与 5 个 awdp-control 视图的 `@keyframes` 全部迁入 `motion/live-city.css` + `motion/awdp-hud.css`，由 `motion/motion.css` 统一导入；同屏动效收敛为具名预设（`live-dot-pulse`、`awdp-scan`、`awdp-result-in` 等）。
3. 修 5 处 `controller → components/views/*.vue` 导入。

**第 2 批（去重，先建抽象再改调用点）**

4. `lib/signalr-hub.ts` 工厂，替换 3 个 hub 的重复生命周期。
5. `useConfigEditorCore` 收敛两个编辑器。
6. 分页：明确 `useOffsetPagination`（页码）+ `useCursorPagination`（load-more）两条唯一路径，删 `useCursorPagePagination`/`CursorPagination` 或改造平台日志页；同步修正 AGENTS.md 的记载。
7. 大屏控制器共用 hook（全屏/轮询/SignalR/计时器），平台日志与审计页共用列表骨架组件。

**第 3 批（清理）**

8. 删除 9 个死组件文件 + 1 个未引用 SVG，补「未引用原语」检查。
9. `palette.ts` 下沉为 `lib/theme-palette.ts`；颜色字面量换 token。
10. 模板内单位换算下沉到控制器；i18n 英文兜底串改用 message key。

## 7. 建议新增的护栏规则（增量，不改现有规则）

```text
style-ownership   任意 .vue 出现 @keyframes / animation: / transition:  -> 必须位于 motion/ 或 assets/css/
token-ownership   .vue 内定义 --custom-* 或 :root{}                    -> 必须位于 assets/css/
color-literal     全 app（含 ui/features/utils/lib）出现 #hex / rgb() / oklch() / color-mix() 字面量
                  -> 仅允许 assets/css/、components/ui/color-picker/、theme preset 模块
layer-direction   composables->features、lib->features、utils->components 运行时依赖
view-import       任意 *.ts 控制器 import components/views/**           -> 禁止
unused-primitive  components/ui/** 无外部引用的 .vue                    -> 报告
```

验收命令：`bun run audit:architecture`、`bun test`、`bun run typecheck`。第 1、2 批建议每项独立提交，避免与 `tests/support/source-assert-baseline.json` 的源码断言债（1809 条）互相耦合。

---

## 8. 本轮整改结果（审计后已执行）

验证：`bun run audit:architecture` 0 violation、`bun run typecheck` 通过、`bun test` **1091 项全部通过**。大屏相关代码按要求未改动。

### 8.1 第一点：逻辑层 / 渲染层物理隔离

| 问题 | 处理 | 结果 |
|---|---|---|
| 4 个控制器 import `MfaStepUpView.vue` | 新增组合入口 `features/authentication/mfa/MfaStepUp.vue`，4 处改为提供该入口 | 控制器不再 import 渲染层 |
| `showNotificationNotice.ts` import `NotificationNoticeContent.vue` | 新增组合入口 `features/notifications/NotificationNotice.vue` | 同上 |
| `features/theme/palette.ts` → 原语 `color-picker/color.ts` | 纯色工具下沉 `lib/color.ts`；调色板契约迁 `lib/theme-palette.ts`；`color.ts` 仅 re-export | 业务层不再依赖原语实现 |
| `composables/useThemePalette.ts` → features | 改为依赖 `lib/theme-palette` | composable 不再反向依赖 features |
| `composables/useAdminCompetitionResource.ts` → features 单例 | 控制器迁入 `features/admin/useAdminCompetitionResource.ts`，middleware 与 2 个页面显式导入 | 消除 composable → feature |
| 视图内秒→分钟换算 | `useTheoryActivityCatalogPage` 增加 `summary` computed（分钟、分数、作答次数），模板只读 | 业务转换回到功能层 |

自建 AST 分层扫描复跑结果：**no layering violations**。

### 8.2 第三点：同类型重复与死原语

| 重复 | 处理 | 行数变化 |
|---|---|---|
| SignalR 生命周期 ×3 | 新增 `lib/signalr-hub.ts`（连接构建、单飞启动、退避重试、teardown），3 个 hub 只保留事件与业务失效 | `useCompetitionHub` 261→242、`useNotificationHub` 96→85、`usePlatformLogHub` 105→81（+111 共享） |
| 配置编辑器 ×2 | 新增 `features/admin/useConfigDraft.ts`（解析、序列化、canonicalize、dirty 单一实现） | `useChallengeRulesEditor` 181→136、`useCompetitionModeConfigEditor` 125→79（+83 共享） |
| 分页 ×3 | 平台日志页改用 `useCursorPagination` + `AdminLoadMore`（与 AGENTS.md 的「加载更多」一致）；删除第三套实现 | 删除 `useCursorPagePagination.ts`、`components/ui/cursor-pagination/`（2 文件）与 `tests/cursor-page-pagination.test.ts` |
| 死原语 | 删除 `ScrollArea.vue`（`ScrollSurface` 别名）、`TableEmpty.vue`、`MarkdownPreview.vue` 及其 barrel 导出 | -3 组件文件 |
| 死文案 | 删除只被 `CursorPagination` 使用的 `pagination.cursor.*`（en + zh-CN 各 3 条） | -6 key |

同步更新的测试：`markdown.test.ts`、`realtime-retry.test.ts`、`notification-realtime.test.ts`、`competition-hub-{dispatch,subscription}.behavior.test.ts`、`platform-logs-pagination.behavior.test.ts`、`theme-palette.test.ts`。质量棘轮（源码断言只降不升）保持通过。

**更正**：`components/ui/number-field/*` 不是死代码 —— `NumberInput.vue` 通过自动导入使用 `NumberField*` 组合，审计时按标签名扫描误判，已保留。

### 8.3 尚未处理（后续轮次）

- runtime 面板 / runtime 卡片控制器重复（72 行）
- 竞赛管理 Runtimes / Submissions 列表控制器重复（81 行）
- 平台日志 / 平台审计视图重复（58/125 行）
- P2 组合入口样板收敛（4 个 panel、8 个 definition section）
- 大屏（`live` / `awdp-control` / spectator）全部未动

---

## 9. 第二轮整改结果

验证：`bun run audit:architecture` 0 violation、`bun run typecheck` 通过、`bun test` **1091 项全部通过**。大屏未动。

### 9.1 P1-5 i18n 硬编码英文

| 位置 | 处理 |
|---|---|
| `useAdminChallengesIndexPage` / `useAdminCompetitionsByIdTeamsPage` / `useAdminPlatformUsersPage` 的 `new Error('Failed to load …')` | 改为 `new ApiError(describeMessage('…'))`，错误在显示时按当前语种渲染；新增 `administration.challenges.error.templatesLoadFailed`、`administration.competitionsBy.error.teamsLoadFailed`、`administration.platformUsers.error.usersLoadFailed`（en + zh-CN） |
| `createError({ statusMessage: 'Page not found' })` ×2 | 不处理：`features/shell/ApplicationError` 只按状态码渲染本地化文案，`statusMessage` 不会上屏 |

### 9.2 P1-3 颜色字面量

| 位置 | 处理 |
|---|---|
| `ColorPicker.vue`、`useThemePalettePanel` 的 `'#39FF14'` 兜底 | 改为 `themeColor('--primary')`，从 main.css token 读取 |
| 调色板 8 个 preset hex | 收进 `features/theme/palette-presets.ts`（单一设计数据模块） |
| `useScoreboardTeamDetailDialog` 雷达图 `rgba(148,163,184,…)` | 新增 `utils/chart-theme.ts` 的 `chartGuideColor(alpha)`，经 `themeColorAlpha('--border')` 解析 token（canvas 无法解析 CSS 变量） |
| `CompetitionWorkspaceNavigationView` 的 `shadow-[0_0_14px_color-mix(…)]` | 移到 `main.css` 的 `.nav-focus-glare:focus-visible` 共享类 |
| `AvatarCropDialogView` 的 `shadow-[0_0_0_999px_oklch(…)]` | 移到 `main.css` 的 `.crop-overlay-mask` 共享类 |

复扫结果：非大屏、非 `main.css`/token 模块的原始色值只剩 `lib/color.ts`（对比墨色常量）、`palette-presets.ts`（设计数据）、`motion/motion.css` 与 `auth-artwork.css` 的遮罩黑、`utils/theme-color.ts` 的无 DOM 兜底值——均为集中模块内的技术常量，不再出现在业务组件与视图中。

### 9.3 剩余

- 三组仍待处理的重复实现（runtime 面板、竞赛列表控制器、平台日志/审计视图）
- P2 组合入口样板与颜色技术常量进一步 token 化
- 大屏相关（按要求）

