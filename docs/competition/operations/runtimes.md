# 比赛运行实例管理

入口：比赛管理 → 运行实例，`/admin/competitions/比赛ID/runtimes`。此页查看单场比赛；跨比赛与模板测试入口在 [平台 Runtime](../../platform/maintenance/runtimes.md)。

## 查询与详情

按题目、队伍、Runtime 状态和 kind 筛选，确认页码与当前实例 UUID。Shared 环境没有普通队伍归属；TemplateTest 属于模板作用域，不与参赛 UUID 混用。

| 状态 | 管理含义 |
| --- | --- |
| Queued | 等待执行/资源接收 |
| Provisioning | 拉取/创建/注入等准备中 |
| Running | 外部实例已进入运行状态，仍需验实际连接 |
| Stopping | 异步回收已受理 |
| Stopped | 停止完成 |
| Failed | 准备/执行失败，核对稳定错误代码 |

详情包括作用域、purpose、题目/队伍跳转、Provider/池、时间、访问入口、资源与 Flag 状态。公开地址之外的诊断字段不能复制给普通选手。

## 启动队伍或共享环境

1. 选择对应比赛题目。
2. 题目按模式需要队伍实例时选择目标队，共享题目使用共享操作。
3. 核对比赛状态、发布、资格和资源额度。
4. 提交启动，等待实例最终 Running。
5. 测试返回的实际入口并确认注入/Checker。

KoH 共享 Hill 不为每队创建一份；AWD 平台调度和手工运行不能无意产生重复活动实例。以服务器当前活动槽位校验为准。

## 续期

对允许续期的 Running 实例打开续期弹窗，填写时长，阅读页面“过早续期”等反馈。续期受 TTL、实例 purpose、资格和当前访问条件限制；不是所有模式、所有状态都有此操作。

页面输入单位与模板 TTL 秒数不同，按表单标签输入；核对新的 expiresAt，别把点击成功当作到期时间已经改变。

## 终止与强制处理

普通终止将实例交给 Runner 回收，页面先显示 Stopping，确认最终 Stopped 和外部资源清除。

强制终止用于普通回收无法完成的受控管理动作，需要理由和确认。它仍交由业务流程处理，不保证请求返回瞬间所有外部资源消失。保留 ID 和故障原因，核对容器/Pod、端口及 owned bridge。

不删除共享 challenge/callback bridge，不清空 runtime namespace，不用全局 prune 或手动改状态字段代替回收。

## 排障顺序

先查资格和额度，再查 Worker/JetStream/Runner，随后镜像认证、资源/PID/Pod slots，最后查公网端口/WSRX。重置创建新 UUID 和可能的新端口，旧链接失效。详见 [排障](../../operations/troubleshooting.md)。
