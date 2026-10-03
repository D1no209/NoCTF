# 一次性关系模型生产切换

此文档只适用于旧模型到当前 EF Core 基线的一次性停机迁移。运行时不提供旧模型双读、升级器或混合版本窗口。生产业务数据、对象文件和 Data Protection Key 必须保留；任何数量、ID、内容或引用校验失败都应停止切换，保留旧环境供回滚。

1. 确认停机窗口，记录当前镜像、Host 角色、活动 Runtime、对象存储与 NATS 状态。停止旧 Api、Worker、Runner 进程并冻结旧 NATS subject；等待正在执行的业务事务和 Provider 操作结束。不要删除旧 Runtime 容器或对象存储卷。
2. 对旧 PostgreSQL、对象存储、Data Protection Key/外部 Secret 和旧镜像制作独立可恢复备份。旧数据库在验收期间只读保留。Redis/Runner Claim 与旧 NATS 消息不导入新协议，但不能把它们误当作业务数据库的替代备份。
3. 用隔离的 `LegacyExport` 通过旧 EF 模型导出加密包。清单必须恰好包含旧基线的 16 类记录；对每类数量与 SHA-256 进行验证。未知 schema、枚举、损坏 Payload 或非 Identity V3 哈希必须终止，不能跳过记录。
4. 准备新的空 PostgreSQL 数据库/volume，只用当前 EF CLI 或 `NoCTF.Host --migrate-only` 应用唯一 `InitialBaseline`。用隔离的 `CurrentImport` 在事务内导入；它再次验证加密包、主记录 ID、共享标量、用户密码哈希与 TokenVersion、文件元数据、Data Protection Key、SSO Provider、关系完整性及排行榜重建。失败则回滚目标事务，不修改旧库。
5. 在启用新 Host 前，对照对象存储验证全部 `files.ObjectKey` 可访问，并抽查认证、附件、竞赛配置、历史事件、通知、GameplayFact、Runtime/Receipt 与排行榜。保留活动 Runtime 的身份和容器，不创建替代 UUID；新 Runner 从数据库事实重建容量和调度状态。
6. 仅部署当前 `NoCTF.Host.dll` 镜像，按 `Hosting:Roles` 分配 Api、Worker、Runner；启用新的 JetStream subject/stream/consumer 命名空间。不得让旧 Host 或旧 Runner 与新版本同时写入。验证健康检查、关键业务流程及对象引用后再开放流量。
7. 验收失败时停止新 Host，恢复旧镜像、旧数据库连接和冻结的旧 NATS 命名空间。验收通过并得到确认后，才清理旧数据库 volume、旧 NATS 命名空间及 `LegacyExport`/`CurrentImport` 临时工具。不得将导入逻辑留在运行时项目。

所有数据库读写与迁移均通过 EF Core；不要用人工 PostgreSQL SQL 修补导入结果。正式迁移必须重新生成停机时的包，本地演练包只能证明工具链，不能代替最终清单。
