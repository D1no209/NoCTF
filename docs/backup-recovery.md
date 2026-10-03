# PostgreSQL、文件/RustFS 与 JetStream 一致恢复

[恢复工具 v2](../deploy/shared/recovery/README.md) 包含当前 EF 创建的 public schema、迁移历史、
本地文件或 S3 对象，以及停止后的 JetStream 文件/校验和。旧 Wolverine 数据库 schema 已移除，
旧 v1 不被描述为完整恢复点。age 加密、Minisign 签名、密钥外部保管；不是在线 PITR。

## 停写与备份

1. 核对项目/context、Host digest、PG major、存储和外部 Secret 集。
2. 停止所有 Api/Worker/Runner 和维护命令，等待 Provider/事务/消息结束；保持 PG/文件服务可读。
   工具发现其他 PG client 会拒绝，文件/S3 的其他写入者也必须停止。
3. 捕获 stream/consumer/KV/pending 状态后停止 NATS，确认容器/Pod 已停止；禁止复制运行目录。
   K8s 用已停写 PVC 的维护挂载或一致快照，不直接猜测节点路径。
4. 将 PG、文件/RustFS、离线 JetStream 合入一个加密签名产物；凭据仅只读文件，不出现在参数/日志。
5. 复制到独立故障域，外部保管平台、S3/PG、age、签名密钥。Redis 可重建，不纳入恢复。
   保留、异地复制、调度、RPO/RTO 由目标运维设定。

## 恢复、切换和回退

1. 停止目标所有角色和 NATS，避免启动自动迁移提前创建表。
2. 验证签名、格式、Secret 集、存储类型、PG major、对应 Host image。
   只允许空数据库、空文件根/bucket、空 JetStream 目录，不覆盖或合并。
3. 恢复后比较迁移历史、逐表行数、文件/对象 key、SHA、metadata/Content-Type、JetStream 文件清单。
   部分恢复失败时保持停写，不自动启动。
4. 启动恢复 NATS，核验 streams/consumers/KV/pending 与 ack，等待旧短租约到期并核验 fencing。
5. 用恢复点 image 启动自动迁移，先验证对应 schema，再考虑另行升级。
   验收登录/刷新、文件、业务记录、投递、排行榜、新 Runtime 后切换入口。
6. 只启用目标一次，源保持停写；不能两端同时接收业务。

schema/数据回退需冻结目标并恢复同一 PG、对象/文件、密钥、JetStream 恢复点。
退镜像不等于退 schema，新增写入的处理由运维明确决定。
`rehearse.sh` 用唯一临时资源、真实 EF schema、Local 与 RustFS、durable consumer/KV/pending，
校验篡改/非空目标拒绝并清理自身资源；不是生产迁移已完成的证明。
