# Mock 站组件缺失与卡顿排查

日期：2026-09-09。检查对象：原前端 `ClientApp/app`、3001 Mock 站及其独立 Nuxt 生成配置。

## 结论与修复

| 现象 | 归属与证据 | 修复 |
| --- | --- | --- |
| 卡片、菜单、徽标等大面积失效 | Mock layer 的根应用没有显式声明组件扫描约定，默认扫描整个 components，生成 `UiCard`、`UiBadge` 和 Views 前缀；实际视图调用 `Card`、`Badge`，浏览器持续输出 Failed to resolve component | Mock 根配置显式声明与生产一致的 UI 目录、无前缀、仅 Vue 文件，并在 components:dirs 中去重 |
| 明显卡顿、日志剧增 | Vite 8 浏览器 console 转发调用 JSON.stringify 展开 Vue 警告参数中的响应式依赖图；记录文件达到 3,919,897,723 字节，尾部包含巨量 `dep/subs/prevSub/[Circular]` 数据 | 修复解析警告的源头；只在 Mock 中关闭 forwardConsole，原始浏览器警告仍可见；清理自行产生的异常大日志 |
| Mock 重启后 Nitro 模块路径错误 | Windows 上直接用 Bun 执行 Nuxt CLI 的 Worker 入口解析失败，文件实际存在 | Mock 启动器和原 `bun run dev` 一样通过 Node 执行 Nuxt CLI；Bun 继续运行 Mock API |
| 提交区域 pt-5 间距消失 | 原前端 FlagSubmitView 同时输出 section 与 Dialog，属于多根视图，Vue 无法自动继承 class | `inheritAttrs: false`，将 `$attrs` 显式绑定到实际 section |
| 新增实时订阅时已有页面重复刷新 | 原前端 ensureStarted 在已连接状态下调用 rejoinAll，后者再次通知全部 onReconnected | 健康连接直接复用，由新增订阅者加入自己的组；真实重连仍重新加入并刷新。新增两项行为回归测试 |

这里的日志异常是**序列化放大**；没有证据证明浏览器与服务端日志互相回传形成闭环，也没有把它误判为 Vue 无限渲染循环。

## 修复后验证

- 原站与 Mock 的生成清单均有 **215 个共享 UI 原语**，缺失、额外及重复注册均为 0。
- API、身份初始化、i18n、自定义滚动条四个生产插件在 Mock 中各注册一次。
- 首页真实渲染 9 张卡片：20px 圆角、12px backdrop blur、alpha 0.6；未解析原语标签为 0。
- 首页空闲观察约 **66 秒**：新增长任务 0；首页只挂载 1 次，5 个竞赛卡片视图没有额外更新；比赛与品牌接口均只请求一次。倒计时有正常显示更新。
- 题目页干净加载后观察约 **91 秒**：长任务 0；页面、工作区、题目详情、FlagSubmit 均只挂载一次；FlagSubmitView 更新 0 次；间距恢复为 20px。
- 题目页 Hub negotiate 1 次，之后为正常长轮询与心跳；通知接口为正常 20 秒轮询。
- 仍有有限的首屏重复读取：榜单 4 次、部分题目/事件/历史接口 2 次，来源是父页面与工作区各自加载以及首次 Hub 同步。计数不会持续异常增长，本轮没有用缓存掩盖这些一致性读取。
- 管理题库页完整渲染 24 行真实共享表格；登录演示管理员、Flag 正确提交、记录更新、榜单增加 100 分均在浏览器中验证。
- 全量测试 **435 通过**，生产与 Mock 前端类型检查通过，架构审计 0 违规。

上述计数由 Mock 专用、可关闭的诊断插件采样，测量的是页面初始化后的观察窗口，并非完整冷启动 CPU/GPU profile 或所有路由的长期压测。

## 仍需区分的事项

Markdown 路径仍有 sanitize-html → postcss 引入 Node `path/url/source-map-js` 的浏览器兼容性警告。这是既有前端依赖问题，不是本次原语注册失败的原因；本轮没有替换 HTML 安全过滤实现。当前演示 Markdown 能正常渲染。

Mock 的辅助管理页面可以使用符合现有 OpenAPI 结构的空数据；未支持的写操作返回明确的 501。演示环境不表示所有真实后端业务已经实现或经过验证。

所有 Mock 数据、协议实现、启动配置、诊断插件和此报告都保留在 `mock/`。生产修改仅包含上表两项通用前端修复及相应回归测试，不包含任何 Mock 数据或条件分支。
