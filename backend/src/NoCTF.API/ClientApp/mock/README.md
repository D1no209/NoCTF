# NoCTF 独立 Mock 站

复用现有前端页面和 UI 原语，在本地运行独立 Mock API。生产代码、SDK、后端与现有 3000 端口开发服务均不需要改动。

## 启动

在 `ClientApp` 中运行：

```powershell
bun mock/dev.ts
```

- 前端：http://127.0.0.1:3001
- Mock API：http://127.0.0.1:5081
- 按 Ctrl+C 同时停止两项服务。端口已占用时启动失败，不抢占已有服务。
- Mock Nuxt 配置、缓存和输出位于此目录，引用上一级 Nuxt layer 的页面、原语和功能层。
- Bun 运行 Mock API，Nuxt CLI 与原站一样使用 Node 运行，避免 Windows 下 Bun/Nitro Worker 的模块路径错误。
- 所有模拟数据和操作结果只存于 Mock API 进程内存，刷新页面保留，重启服务重置。它们在同一服务的多个浏览器窗口之间共享。

## 演示身份

首次打开默认恢复为管理员演示身份。可从现有用户菜单退出，再通过现有登录页切换账号。

| 用户名 | 密码 | 身份 |
| --- | --- | --- |
| admin | Mock123! | 平台管理员 |
| organizer | Mock123! | 组织者 |
| player | Mock123! | 选手 |

所有身份均为虚构；Mock access token 只被本地 Mock 服务接受。Mock Cookie 使用独立名称 `noctf_mock_session`。

## 数据与交互

- 四种赛制、6 场比赛、56 个题库模板、84 道比赛题目、56 个模板附件、48 支队伍、排行榜、趋势与公告。每场比赛按 Misc、Web、Crypto、Pwn、Reverse、Penetration、Forensics、OSINT、AI、Mobile、IoT、Hardware、Cloud、Blockchain 各提供一道题。
- 默认账号在各比赛的 Aurora 队伍中；选手是队长。
- 支持资料/学校身份编辑、平台品牌编辑、比赛和题库创建编辑、添加比赛题目、队伍创建/加入/退出/编辑、生命周期切换、公告及提问创建。每道种子题提供一个可实际下载的纯文本演示附件。
- Flag 演示：`flag{mock_success}` 为正确答案，其他内容为错误；重复正确提交不重复加分。提交结果写入演示记录并更新榜单。
- CTF 第一题预置为本队已解出；AWDP 前三题依次预置攻击成功、防御成功、攻防均成功，用于验证四种独立 SVG 状态标记。
- CTF 血榜按每道题实际存在的解出队伍连续分配一血、二血、三血，不会因未解出的种子队伍产生名次空缺；题目列表的血标悬停提示显示获奖队伍名。
- 14 个方向的种子题均预置运行中的模拟环境，并按题目分配 `tcp://challenge.mock.invalid:31xxx` 虚构套接字；Runtime 启动/停止/重置/续期只改变虚构内存状态，不监听端口、不运行容器、不提供真实靶机连接。
- SignalR 使用本地 LongPolling 协议响应握手、订阅、心跳和榜单失效消息；无需真正的 Hub 后端。
- 未单独填充的辅助只读页面使用现有 OpenAPI 响应结构，展示空列表/默认数据。未实现的写操作明确返回 501，不伪造成功。
- API 不包含任何向上游服务器发送请求的代码；全部前端 `/api`、`/hubs` 和 `/health` 流量固定指向 5081。

## 目录

- `data/fixtures.ts`：虚构内容与账号种子。
- `data/competition-poster.png`：用户提供的本地测试海报，经 Mock 海报接口返回；前端默认等比 cover 填充，不进入生产素材目录。
- `api.ts`：本地状态与接口行为。
- `schema.ts`：只读使用现有 OpenAPI 文档，生成完整响应结构，避免复制或修改生产 SDK。
- `leaderboard.ts`：虚构榜单与提交后的得分计算。
- `realtime.ts`：演示 SignalR 传输。
- `nuxt.config.ts` / `dev.ts` / `server.ts`：独立站点配置和启动入口。

## 验证

```powershell
bun test mock
bun mock/check-frontend.ts
node node_modules/@nuxt/cli/bin/nuxi.mjs typecheck mock
```

这不是后端业务规则或安全机制的替代实现，真实部署不引用此目录，也不使用这些演示账号。

## 可选性能诊断

启动前设置 `$env:NOCTF_MOCK_DIAGNOSTICS = '1'`，Mock 专用插件会记录组件挂载/更新/卸载次数、长任务和请求计数，结果写入页面根节点的 `data-mock-diagnostics` 属性。默认不加载该插件；它不进入生产前端。

Mock 显式沿用生产的原语注册路径及无前缀约定，禁止回退到 Nuxt 默认扫描整个 `components/`。`check-frontend.ts` 比较生成的原语清单并检查业务插件只注册一次。

Mock 的 `vite.server.forwardConsole` 关闭浏览器日志向终端的转发，浏览器控制台仍正常显示原始错误。避免将 Vue 警告中完整的响应式对象序列化成巨量文本。
