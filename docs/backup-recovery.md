# PostgreSQL、NATS JetStream 与对象存储备份恢复

## 恢复边界

NoCTF 的灾难恢复点由三部分组成：

- PostgreSQL 业务 schema；
- NATS JetStream stream/consumer 配置与持久卷；
- 当前 S3-compatible bucket 中的全部对象及其 `Content-Type`、`x-amz-meta-sha256` 元数据。

Wolverine durable queue、scheduled message 和 dead letter 位于 JetStream，必须与 PostgreSQL 业务事实
和对象存储一起纳入恢复流程。不得把 JetStream 队列当成 Redis 缓存。Redis 不进入备份：恢复后由
PostgreSQL 事实、在线 Runner 和正常流量重建排行榜缓存、限流、
SignalR backplane、心跳与容量。

本仓库提供外部运维工具，不新增应用端点、后台页面或业务表。工具创建离散的、应用停写的一致性
恢复点，不提供在线 PITR。当前参考目标是 RPO 不超过 24 小时、RTO 不超过 4 小时、恢复点保留
30 天；生产调度、保留删除、异地复制和告警由目标环境落地。若要求更小 RPO，必须另行设计
PostgreSQL WAL/PITR 与对象版本的一致性协议，不能把本工具描述为 PITR。

## 安全契约

- 备份文件必须使用 `age` recipient 加密并通过独立 Minisign key 签名；工具不会生成明文最终
  产物。恢复会先验证签名，不能把 age recipient 的加密完整性误当成来源认证。
- 自动化使用的 Minisign secret key 文件本身不设置交互式口令，必须由 Secret 管理系统加密保存、
  只读挂载并限制到备份身份；恢复环境只分发 public key。
- PostgreSQL、S3 与 age 私钥只通过只读文件挂载。工具不会把凭据写入 manifest 或日志。
- manifest 只记录非秘密的 `secretSetId`。恢复者必须提供完全相同的外部 Secret 集标识。
- JWT signing key、Runner scoring key、S3/数据库凭据、age identity、Minisign signing key 和
  `EmailVerification__EncryptionKey` 不在
  备份中，必须在独立 Secret 管理系统中备份。邮箱 SMTP 密码、SSO Client Secret 与共享
  Data Protection 密钥环在数据库中为密文；缺少原 `EmailVerification__EncryptionKey` 时无法解密。
- 加密备份必须复制到与运行集群不同的故障域。至少保留一份不可由 NoCTF 运行身份删除的副本。
- 恢复工具只接受空数据库和空 bucket，不支持覆盖、合并或原地恢复。

## 构建工具

从仓库根目录构建固定 PostgreSQL 16 与 MinIO Client digest 的工具镜像：

```bash
docker build -t noctf-recovery:local deploy/recovery
```

工具需要网络访问源或目标 PostgreSQL/S3。下面示例中的 `/run/noctf-recovery` 是只读 Secret
挂载，`/backup` 是加密产物目录；不要把真实凭据放进命令行、镜像或仓库。
源数据库身份至少需要 `CONNECT`、应用 schema 的 `USAGE`、表 `SELECT`、sequence
读取及查看其他连接的权限；源 S3 身份只需要目标 bucket 的 List/Get/Head。恢复身份必须能在空
database 创建 schema/table 并向空 bucket Put/Head/Get/List。生产环境应为两条路径配置不同的
最小权限身份。

## 创建恢复点

1. 停止所有承载 Api、Worker、Runner 角色的进程和 migration job，等待当前事务与 Provider 操作结束。
2. 确认没有人工客户端或其他写入者连接数据库。工具也会在备份前后检查其他 PostgreSQL client
   connection；发现任意连接即失败并删除未完成产物。
3. 运行备份：

```bash
docker run --rm \
  --network <operations-network> \
  -v /run/noctf-recovery:/run/secrets:ro \
  -v /var/backups/noctf:/backup \
  noctf-recovery:local backup \
  --postgres-host <postgres-host> \
  --postgres-database noctf \
  --postgres-user <backup-user> \
  --postgres-password-file /run/secrets/postgres-password \
  --postgres-ssl-mode verify-full \
  --s3-endpoint https://<s3-endpoint> \
  --s3-bucket noctf \
  --s3-access-key-file /run/secrets/s3-access-key \
  --s3-secret-key-file /run/secrets/s3-secret-key \
  --age-recipients-file /run/secrets/age-recipients.txt \
  --minisign-secret-key-file /run/secrets/minisign.key \
  --secret-set-id <vault-snapshot-id> \
  --output /backup/noctf-<utc-timestamp>.tar.gz.age
```

4. 只在命令成功且加密文件及其 `.minisig` 签名已复制到独立故障域后恢复应用进程。
5. 调度器按目标环境实现 30 天保留和失败告警；不得让 NoCTF 应用身份拥有异地备份删除权限。

产物内含 `manifest.json`、PostgreSQL custom dump、逐表行数、对象内容、对象元数据索引和全部文件
SHA-256。manifest 不含数据库/S3 地址和凭据。

## 隔离恢复

1. 创建全新的 PostgreSQL 16 database 与全新的空 bucket。保持所有 Api、Worker、Runner 角色停止。
2. 恢复与 manifest 的 `secretSetId` 对应的外部 Secret 集。不要先运行 EF migration；目标数据库
   必须为空。
3. 运行恢复，目标 database 和 bucket 名必须与 manifest 一致：

```bash
docker run --rm \
  --network <isolated-recovery-network> \
  -v /run/noctf-recovery:/run/secrets:ro \
  -v /var/backups/noctf:/backup:ro \
  noctf-recovery:local restore \
  --input /backup/noctf-<utc-timestamp>.tar.gz.age \
  --age-identity-file /run/secrets/age-identity.txt \
  --minisign-public-key-file /run/secrets/minisign.pub \
  --secret-set-id <vault-snapshot-id> \
  --postgres-host <new-postgres-host> \
  --postgres-database noctf \
  --postgres-user <restore-owner> \
  --postgres-password-file /run/secrets/postgres-password \
  --postgres-ssl-mode verify-full \
  --s3-endpoint https://<new-s3-endpoint> \
  --s3-bucket noctf \
  --s3-access-key-file /run/secrets/s3-access-key \
  --s3-secret-key-file /run/secrets/s3-secret-key
```

恢复会先验证 age 完整性、安全归档路径、manifest、全部 SHA-256 与外部 Secret 集标识，然后拒绝
任何非空目标。写入完成后会再次比较所有受保护 schema 的逐表行数，并下载每个对象核验 key、
内容 SHA-256、`Content-Type` 与 SHA-256 元数据。

4. 人工核验比赛、用户、`competition_events`、待处理 GameplayFact、JetStream scheduled/dead-letter
   数量、consumer 状态和关键附件。记录备份时间、开始/完成时间、操作者和验证结果。
5. 先用备份时相同的应用版本验收，再按正常 migration 流程升级。验收通过前不得让新旧环境同时
   消费同一队列或操作同一 Runtime provider。
6. 先恢复 NATS stream/consumer/KV 并启动包含 Worker 的宿主，确认调度租约与 JetStream endpoint ready，
   再启动各 Runner 节点和 API；全合一部署只需启动 Host。所有外部副作用仍必须依赖业务幂等键、
   状态转换和唯一约束，不使用持久化 ProcessingVersion 栅栏。

## 自动恢复演练

以下命令只创建带唯一名称的临时 Docker network/container/image，退出时精确删除这些临时资源：

```bash
bash deploy/recovery/rehearse.sh
```

演练覆盖两个隔离 PostgreSQL、两个隔离 MinIO、JetStream/KV 恢复点、比赛永久事件、多个对象
及其元数据、快照后源数据变化、恢复后逐表/逐对象验证、加密文件篡改拒绝和非空目标拒绝。它不
连接开发或生产 NoCTF 服务。
