# 比赛强制删除：修复与本地验收记录

日期：2026-09-08。基于 `272590ae`，本次改动尚未提交、推送或部署。

## 边界

- 没有连接生产服务器，没有删除「DO TEST」，没有操作任何生产比赛、容器或文件。
- 没有修改数据库模型、Migration、CI、部署配置、版本号或 `1panel-network`。
- 所有写入和故障注入均使用本机 Testcontainers 创建的独立 PostgreSQL / NATS，测试结束后释放。
- 用户提供的生产日志用于确定排查方向；下面的新增结论来自本地代码检查和真实依赖测试，不声称重新核验了生产日志。

## 已修复

### 1. 外键依赖顺序

删除顺序为：已证明归属的通知 → PatchUpload → RuntimeInstance → GameplayFact → 比赛题目 Flag → CompetitionEvent → CompetitionChallenge → Team → Competition。

完整 AWDP 夹具中，PatchUpload.RuntimeInstanceId 和 RuntimeInstance.GameplayFactId 均非空，包含多条已停止实例。对夹具执行旧顺序的第一条 Runtime DELETE，真实 PostgreSQL 返回 `23503`；正常强制删除流程则成功。没有关闭或放宽任何外键。

同时收紧 Flag 删除条件：只按本比赛的 CompetitionChallenge 删除，不再仅凭 TeamId 扩大范围。若历史异常数据让全局模板 Flag 引用了本比赛队伍，外键将阻止删除并回滚，不能顺便删除模板 Flag。

### 2. 事务和提交边界

保留比赛行锁、管理员权限、精确标题确认、删除原因校验，以及 Running / Paused 和活动 Runtime 阻断规则。

先收集预览和文件 ID，再删除比赛作用域数据，加入平台审计和后台消息，SaveChanges 后提交。提交前异常会回滚业务删除、审计及 Outbox。文件对象不在这个删除事务内直接清理。

提交后的请求仅尝试分发已经持久化的消息；分发异常记录为“比赛已删除，Outbox 等待重试”，不会被当成业务删除失败重新抛给接口。

### 3. 实测确认的 Outbox 附加风险

仅增加 SaveChanges 不足以证明持久化。中断请求分发的测试中，原有 buffered NATS 发送配置下，独立数据库连接查不到相应 outgoing envelope。

已为 CleanupFile 和新增的 InvalidateDeletedCompetitionReadModels 所使用的 **background 发送端** 显式启用 JetStream 和 durable outbox。Wolverine 按端点共享配置，因此同一 background subject 的其他发送也采用此设置；没有全局改动其他业务队列。保持已有 MultiFlushMode.AllowMultiples，不重复配置。

故障注入测试使用真实 `ConfigureNoCtfMessageRouting`：提交后、请求分发前中断，独立连接可读取 5 个已提交的 envelope（海报和 3 个 Patch 文件清理，以及缓存失效）。丢弃原请求作用域并启动新的 Worker host 后恢复消费，不重新调用删除。

该测试模拟“提交完成但请求未分发”的崩溃窗口，并通过新 host 验证恢复；不是对生产进程断电，也不是一次实际 OS 强杀测试。

这一风险是本地另外确认的消息可靠性问题，**不是 DO TEST 日志中已证实的外键错误原因**。

实现核对参考：[Wolverine 6.30.3 EF Outbox](https://github.com/JasperFx/wolverine/blob/V6.30.3/src/Persistence/Wolverine.EntityFrameworkCore/Internals/EfCoreEnvelopeTransaction.cs)、[NATS 发送配置](https://github.com/JasperFx/wolverine/blob/V6.30.3/src/Transports/NATS/Wolverine.Nats/Configuration/NatsSubscriberConfiguration.cs)。

### 4. 通知归属

- 首先按比赛、队伍、比赛题目等明确关联找到通知与线程根。
- 只沿 ThreadRootId 收集线程成员；ReplyToId 不作为扩展删除集合的依据。
- 校验候选通知自身是否包含矛盾的其他比赛关联，以及集合内外的 ThreadRootId / ReplyToId 引用。
- 锁定待删除根及成员，防止新的外键引用穿过检查与删除之间的窗口。
- 平台强制删除审计始终排除；若它引用了待删除线程，也阻止删除，而不是吞掉审计。
- 无法证明归属时返回 `NotificationScopeConflict` 与冲突通知 ID；预览显示阻断原因，管理页面显示中文说明。

### 5. 文件清理及缓存重试

CleanupFile 消费者仍先加文件行锁并检查引用；增加对软删除比赛、软删除队伍的引用检查。仍被用户、模板附件、其他比赛等使用的文件不能删除。重复投递已清理文件无副作用；对象存储失败保留数据库记录，重试可完成清理。

比赛读模型缓存失效改由持久化后台消息执行。文件清理和该缓存任务具有按消息类型限定的异常重试策略，避免策略执行时尚无端点关联而漏配重试；重试耗尽进入持久化错误队列。测试注入一次缓存基础设施异常，确认第二次消费成功，删除结果和审计不受影响。

## 验收

- 后端构建：0 警告、0 错误。
- 全部非 Integration 后端测试：1073 项。
- 删除相关测试集合：22 项（包含接口测试，以及真实 PostgreSQL / NATS 测试）。
- 文件清理 PostgreSQL 集成测试：2 项，包括软删除引用、存储失败重试和重复清理。
- 前端：388 项测试，类型检查、静态生产构建通过。
- OpenAPI 与前端生成 SDK 已同步；没有手写修改 SDK。

覆盖：完整 AWDP FK 链、多实例多 Patch、仅 ThreadRootId 的成员、跨比赛/无归属回复引用、平台审计保护、活动比赛与资源、无权限、标题错误、无效原因、中途 SQL 异常回滚、SaveChanges 后提交前异常回滚、提交后分发中断恢复、缓存任务重试、其他比赛/全局模板/共享文件保护。

未做共享测试服务器的页面删除验收，也没有生产发布或实际删除。后续必须得到明确指令，先在测试服务器验收；生产发布后再预览 DO TEST 的真实范围，通过正常接口确认，不能用直接 SQL 绕过保护。
