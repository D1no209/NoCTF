# NoCTF 当前待办

本文只追踪“数据模型与 Wolverine 调度简化”迁移。历史 TODO 已失效，不得据此恢复旧协议。

## 权威来源

1. [`docs/data-model-wolverine-simplification.md`](docs/data-model-wolverine-simplification.md)
2. [`AGENTS.md`](AGENTS.md)
3. [`docs/README.md`](docs/README.md)
4. [`PLAN.md`](PLAN.md)
5. [`NoCTF-backend-handoff-2026-07-24.md`](NoCTF-backend-handoff-2026-07-24.md)

## 当前阶段：阶段 5

- [x] 建立独立分支并记录 main 基线。
- [x] 完整阅读项目规范、权威规范、现有 schema、OpenAPI、SDK 和 Wolverine 拓扑。
- [x] 阅读 Wolverine 官方文档与 `llms.txt`。
- [x] 编写并通过 Wolverine 6.29.2 真实 PostgreSQL Spike。
- [x] 同步根 AGENTS、ClientApp AGENTS 与核心专题文档目标语义。
- [x] 完成 Stage 0 基线、Spike 和切换/回滚文档评审。
- [x] 运行 Release build、现有完整测试和静态门禁。
- [x] 更新 HANDOFF 并创建阶段 0 独立提交。

## 阶段 1：删除 Revision 协议闭环

- [x] 建立阶段 1 字段、类型、接口、消息和测试基线。
- [x] 删除领域实体和 EF 映射中的伪 Revision/ConcurrencyToken 字段。
- [x] 删除 Application command/query/store 的 ExpectedRevision 协议。
- [x] 删除 API/OpenAPI/生成 SDK/前端中的 Revision 冲突协议。
- [x] 保留业务唯一冲突和真正的 Token/Schema/Payload/外部协议版本。
- [x] 通过 LWW、唯一冲突、真实 PostgreSQL 并发和契约门禁。
- [x] 更新 HANDOFF 并创建阶段 1 独立提交。

## 阶段 2：核心实体和隐私模型

- [x] 建立 User、Competition、Team、Challenge 字段和隐私边界基线。
- [x] 完成核心实体最小模型收敛，不新增业务表。
- [x] 以真实 PostgreSQL 验证唯一约束和隐私读取边界。
- [x] 更新 HANDOFF 并创建阶段 2 独立提交。

## 阶段 3：Notifications/Questions 线程化

- [x] 使用 `ThreadRootId` 作为稳定线程成员边界。
- [x] 保留 `ReplyToId` 作为可选回复上下文，不参与线程归属。
- [x] 验证并发 append、稳定排序、状态计算、隐私和 append-only 根保护。
- [x] 更新契约、SDK、前端、HANDOFF 并创建阶段 3 独立提交。

## 阶段 4：同步流式导出并删除 DataExport

- [x] 删除 DataExport Domain/Application/Infrastructure/Worker/API 和消息链。
- [x] 保留并实现两个同步、可取消、有界 ZIP 流端点。
- [x] 验证权限、受保护 Flag、审计、NotFound、取消和四类资源上限。
- [x] 删除前端任务轮询并通过生成 SDK 直接下载。
- [x] 更新 OpenAPI、SDK、文档、HANDOFF 并创建阶段 4 独立提交。

## 阶段 5：Runtime 最小模型与节点直投

- [ ] 建立 Runtime 字段、消息、Runner 路由和测试基线。
- [ ] 删除冗余 Runtime 调度状态并把 Reset 改为新 UUID。
- [ ] 按具体 Runner Sticky PostgreSQL endpoint 直投并移除 pool queue 消费。
- [ ] 验证 URL 读取过滤、receipt Checker 目标、幂等和清理。
- [ ] 更新契约、SDK、前端、HANDOFF 并创建阶段 5 独立提交。

## 后续阶段

严格按 [`PLAN.md`](PLAN.md) 的阶段 1 至阶段 11 顺序推进。每阶段只有在权威规范第 12 节退出门禁全部通过后才能勾选完成。

## 当前禁止事项

- 不推送、不部署、不操作生产数据库、生产 Wolverine 队列或对象存储。
- 不手改 EF migration/snapshot、OpenAPI artifact 或生成 SDK。
- 不新增业务表，不创建已废弃协议的兼容层。
