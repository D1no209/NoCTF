# NoCTF 数据模型与 Wolverine 调度简化实施计划

> 权威语义：[`specs/data-model-wolverine-simplification.md`](specs/data-model-wolverine-simplification.md)。
> 本计划只记录迁移顺序和门禁，不重新定义产品语义。

## 当前状态

- 工作分支：`codex/data-model-wolverine-simplification`
- 基线提交：`8e366fbda160e24d5142d54f1151bf0024950dc1`
- 已完成并独立提交：阶段 0 至阶段 10
- 当前阶段：阶段 11（契约、文档与本地全量验证已完成，等待获准的部署/生产切换门禁）
- 推送、部署、生产数据库和生产队列操作：未授权

## 强制阶段顺序

每个阶段必须先记录范围，再完成最小闭环、定向测试、退出门禁、HANDOFF 和独立提交。退出门禁失败时不得进入下一阶段。

1. **阶段 0：治理、基线与 Wolverine Spike**
   - 同步 AGENTS 和权威专题文档。
   - 固化源码、数据库、OpenAPI、SDK 与测试基线。
   - 以真实 PostgreSQL 验证 Wolverine 6.29.2 的 Sticky、fallback、PostgreSQL endpoint、`IdAndDestination`、EF transactional outbox、durable inbox 和 Singular Agent。
   - 输出生产切换和回滚 Runbook；不修改业务语义。
2. **阶段 1：删除 Revision 协议闭环**
   - 删除实体、命令、Store、API、OpenAPI、SDK 和 UI 中的 Revision/ExpectedRevision/ConcurrencyToken。
   - 统一 last-write-wins，同时保留真正的业务唯一约束和幂等键。
3. **阶段 2：核心实体和隐私模型**
   - 按权威规范收敛 User、Competition、Team、Challenge 与隐私字段。
4. **阶段 3：Notifications/Questions 线程化**
   - 使用 `Notifications + ThreadRootId + ReplyToId` 承载咨询线程，不新增业务表。
5. **阶段 4：同步流式导出并删除 DataExport**
   - 改为鉴权后的同步流式下载，删除 `data_exports` 和后台导出消息。
6. **阶段 5：Runtime 最小模型与节点直投**
   - Reset 创建新 UUID；删除 generation、replacement、pool、checker deadline 等冗余状态。
   - Runner claim 后按节点 Sticky PostgreSQL endpoint 直投。
7. **阶段 6：GameplayFact 与 AWDP 结果**
   - 每次 AWD Checker 对应独立事实；AWDP Fix 使用强类型结果和版本化事件 payload。
8. **阶段 7：Wolverine competing consumers 与 fan-out**
   - 单消费者使用 competing consumers；多订阅者显式 fan-out。
   - `IdAndDestination`、durable inbox/outbox 与 Sticky endpoint fail-fast 完成生产闭环。
9. **阶段 8：Singular Agent 调度**
   - 唯一活动 Agent 从 PostgreSQL 事实重建内存计划，只派发 durable 消息；停机窗口不补跑。
10. **阶段 9：排行榜事件驱动投影**
    - 删除 Dirty 扫描，改为事件驱动失效、500ms 合并和 PostgreSQL 全量投影。
11. **阶段 10：单一 EF 初始基线**
    - 仅在可丢弃开发数据库验证迁移移除和新 InitialBaseline。
    - 最终业务表严格为 15 张；migration/snapshot 只由 EF 工具生成。
12. **阶段 11：契约、文档、全量验证与切换**
    - 导出 OpenAPI、重新生成 SDK、完成完整测试矩阵、Alpha 版本更新和切换演练。

## 全程不变量

- 不新增业务表；若无法满足必须先暂停并请求确认。
- 不为旧 Revision、旧 Runtime、`DataExport`、Dirty 扫描或旧 API 保留兼容层。
- Endpoint 保持强类型 FastEndpoints；业务规则留在 Application。
- 关系型、Wolverine 与并发行为使用真实 PostgreSQL/Testcontainers。
- 不手改 migration、snapshot、OpenAPI artifact 或生成 SDK。
- 未经明确授权不推送、不部署、不触碰生产数据。

## 恢复入口

- 当前证据与精确命令：[`specs/data-model-wolverine-stage0-baseline.md`](specs/data-model-wolverine-stage0-baseline.md)
- Wolverine Spike：[`specs/wolverine-6.29.2-spike.md`](specs/wolverine-6.29.2-spike.md)
- 切换与回滚：[`specs/data-model-wolverine-cutover.md`](specs/data-model-wolverine-cutover.md)
- 阶段 11 验证：[`specs/data-model-wolverine-stage11-validation.md`](specs/data-model-wolverine-stage11-validation.md)
