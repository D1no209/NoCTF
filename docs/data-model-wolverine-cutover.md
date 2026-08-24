# 数据模型与 Wolverine 简化：生产切换与回滚 Runbook

## 当前授权状态

本任务未授权推送、部署或操作生产数据库、Wolverine 队列和对象存储。本文只定义切换方案；执行前必须由发布负责人逐项签字。

阶段 10 已在本机可丢弃 PostgreSQL 完成旧 migration 链备份恢复和新空库基线演练。开发证据及哈希
记录在 [阶段 10 单一 EF 基线](data-model-wolverine-stage10-ef-baseline.md)。该演练只证明工具链和恢复
流程可执行，不代表生产备份、数据转换、责任人、停机窗口或 Go/No-Go 门禁已经获批。

## 负责人

| 职责 | 负责人 | 权限 |
|---|---|---|
| 发布/回滚决策 | NoCTF 项目所有者（主线程用户） | 决定停机、切换、回滚 |
| 数据库与备份执行 | 当次指定的平台/数据库管理员 | 旧库冻结、备份、恢复、校验 |
| 应用与 Wolverine 执行 | 当次指定的发布工程师 | 镜像、队列、配置、ready 验证 |
| 事件协调 | 当次指定的值班负责人 | 记录时间线、影响和恢复点 |

任何执行人的真实姓名、联系方式和替补未在发布工单确认前，生产切换门禁视为未通过。

## 发布前准备

1. 冻结旧版本写流量并记录冻结时间。
2. 记录旧应用 commit、镜像 digest、配置版本和 Secret 引用；不得把 Secret 写入文档。
3. 对旧 PostgreSQL 做全量一致性备份并校验可恢复性。
4. 导出旧 schema、逐表行数、关键外键/唯一约束和业务校验摘要。
5. 记录 Wolverine persistence/transport schema：所有 queue、inbox、outbox、dead-letter 数量和最老消息年龄。
6. 生成对象存储不可变清单：对象键、大小、内容哈希和引用计数；不记录受保护 Flag 明文。
7. 确认旧 durable queues 已排空，或取得明确的丢弃审批和影响说明。
8. 在隔离环境恢复旧库备份，验证旧应用可以回退启动。
9. 在隔离的新数据库应用 EF 生成的单一 InitialBaseline，确认只有 15 张业务表。
10. 验证新 OpenAPI、SDK、前端和镜像均来自同一 commit。

## 数据转换策略

新版本不在生产旧 schema 上执行破坏性原位迁移。若需要保留数据，使用一次性、可审计的离线搬运程序：

1. 输入只读的冻结旧库备份；输出全新空数据库。
2. 按权威规范逐表转换；删除 Revision、Dirty、旧 Runtime 调度字段和 `data_exports`，不把兼容字段带入新库。
3. Notification 线程、Runtime、GameplayFact 等必须有显式映射和无法转换记录。
4. 每张表验证输入/输出计数、被丢弃行及原因、外键、唯一约束和稳定内容哈希。
5. 对象存储只复用经哈希验证且仍被新模型引用的对象。
6. 搬运程序和结果报告单独评审；未经批准不得针对生产备份运行。

无法无损转换的业务数据必须形成差异清单并暂停切换，不能用清库或静默丢弃替代审批。

## 停机切换

1. 发布负责人宣布停机窗口开始，确认入口只读/不可写。
2. 数据库管理员执行最终备份并记录恢复点。
3. 停止旧 Api/Worker/Runner，确认不再写业务库和旧 Wolverine schema。
4. 执行已评审的数据搬运，或连接经批准的全新空基线数据库。
5. 使用全新的 Wolverine persistence/transport schema；不得让新版本消费旧协议消息。
6. 启动新 Worker，验证所有 PostgreSQL queue、durable inbox/outbox、Sticky endpoint 和唯一 Singular Agent ready。
7. 启动 Runner，验证节点 endpoint 和容量事实；不创建真实比赛 Runtime。
8. 启动 API，验证健康、鉴权、OpenAPI commit、同步导出和只读核心查询。
9. 清空可丢失 Redis 后验证排行榜能从 PostgreSQL 重建。
10. 执行冒烟写入、消息投递、投影和回滚测试数据，再精确删除本次测试数据。
11. 发布负责人批准恢复流量并结束停机窗口。

## 成功门禁

- 15 张业务表且无 `data_exports`、Revision、Dirty 或旧 Runtime 调度列。
- 新 Wolverine schema 无未知旧消息；四类 queue、inbox/outbox/dead-letter 可观测。
- 多 Worker 只有一个调度 Agent，Sticky 缺失会阻止 ready。
- Redis 丢失后排行榜可重建；500ms 合并和乱序保护正常。
- OpenAPI、SDK、前端、镜像与数据库 baseline commit 一致。
- 备份恢复、数据计数、外键和内容哈希报告全部通过。

## 回滚

1. 发现门禁失败立即重新关闭写流量并停止新 Api/Worker/Runner。
2. 不让旧应用连接已经写入新 schema 的数据库。
3. 恢复旧应用、旧配置和切换前旧数据库备份；恢复旧 Wolverine schema 的一致备份状态。
4. 对象存储只做版本/清单恢复，不删除无法确认归属的对象。
5. 新版停机后产生的数据不在线反向转换；记录可能丢失窗口和所有已确认写入。
6. 验证旧版本核心读写、消息和 Runtime 控制后再恢复流量。
7. 保存失败现场、日志、trace、数据库差异和队列快照，重新评审后排期。

回滚演练未在隔离环境通过、负责人未签字或恢复时间目标不可满足时，禁止生产切换。
