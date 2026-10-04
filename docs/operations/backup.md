# 备份与恢复

NoCTF 的完整恢复点由 PostgreSQL、文件/对象内容及元数据、**停止后的** NATS JetStream 和外部密钥集组成。Redis 可从关系事实重建，不代替其他备份。仓库恢复工具 v2 使用 age 加密、Minisign 签名，是停写离散恢复点，不是在线 PITR。

## 备份范围

| 内容 | 保存目的 |
| --- | --- |
| PostgreSQL public schema / EF history / 数据 | 当前业务事实与迁移基线 |
| Local uploads 或 S3 bucket 对象 | 附件、头像、题解、Fix 等文件 |
| 离线 JetStream 目录及状态清单 | streams、consumer、KV 和待处理消息 |
| 部署配置与 Secret 集 | 签名/加密、数据库、S3、Registry 等恢复依赖 |
| 恢复点对应 Host digest | 先恢复相同版本再考虑升级 |
| age 私钥/Minisign 验签公钥 | 解密与验证，不与所有备份唯一同处一台机器 |

Registry 和题目自有数据、外部 Cap 也要按各自生命周期制定备份。题目可执行镜像至少保留审核 digest 和可重新拉取的 Registry 内容。

## 1. 准备恢复工具与凭据

平台应用已经由 CI 编译，备份维护使用单独的恢复工具镜像。优先使用运维已准备的 `noctf-recovery:reviewed`；需要准备该工具时，配置包已经包含它的 Dockerfile 和脚本，从解压目录执行，不下载应用源码：

```bash
cd /opt/noctf-release
docker --context YOUR_CONTEXT build -t noctf-recovery:reviewed deploy/shared/recovery
```

在受限目录 `/run/noctf-recovery` 准备独立文件：数据库密码、age recipients、Minisign 私钥，S3 时还包括访问密钥。创建外部备份目录，并检查磁盘余量。

```bash
sudo install -d -m 0700 /run/noctf-recovery /var/backups/noctf
```

凭据内容通过安全的本地编辑/secret 管理写入，不放命令行参数或 Docker 镜像。创建 age/Minisign 密钥后测试能加密、签名、解密、验签，妥善外部保管；工具容器内包含运行依赖，但密钥由运维提供。

## 2. 停止全部写入

Docker 单 Host 场景，在维护窗口停止平台：

```bash
cd /opt/noctf
docker --context YOUR_CONTEXT compose --env-file .env -f docker-compose.yml stop noctf
```

拆分角色时停止所有 Api/Worker/Runner 和维护命令，等待 Provider、Checker、事务与消息执行结束。停止其他文件/S3 写入者；PG 保持可访问，工具发现其他 PG client 会拒绝。

在 NATS 仍运行时捕获 stream/consumer/KV/pending 状态清单，再停止 NATS：

```bash
docker --context YOUR_CONTEXT compose --env-file .env -f docker-compose.yml stop nats
docker --context YOUR_CONTEXT compose --env-file .env -f docker-compose.yml ps --all
```

`--jetstream-stopped` 是运维声明，不会替你停 NATS。确认其容器停止后再挂载目录；不复制运行中的数据库/NATS 文件作为完整备份。

Kubernetes 同样先将全部应用角色停写并确认，在 NATS 停止后使用离线 PVC 维护挂载或一致快照，不能猜测宿主 volume 路径。

## 3. 制作加密签名备份

本地文件示例，替换网络、备份账号、镜像与 secret-set-id：

```bash
docker --context YOUR_CONTEXT run --rm --network noctf-network \
  --mount type=bind,source=/run/noctf-recovery,target=/keys,readonly \
  --mount type=bind,source=/opt/noctf/data/uploads,target=/files,readonly \
  --mount type=bind,source=/opt/noctf/data/nats,target=/jetstream,readonly \
  --mount type=bind,source=/var/backups/noctf,target=/backup \
  noctf-recovery:reviewed backup \
  --postgres-host postgres --postgres-database noctf --postgres-user YOUR_BACKUP_USER \
  --postgres-password-file /keys/postgres-password --postgres-ssl-mode disable \
  --storage-kind local --local-root /files --jetstream-dir /jetstream --jetstream-stopped \
  --host-image YOUR_REGISTRY/noctf@sha256:YOUR_DIGEST --secret-set-id YOUR_SECRET_SET_ID \
  --age-recipients-file /keys/age-recipients --minisign-secret-key-file /keys/minisign.key \
  --output /backup/noctf-point.tar.gz.age
```

`noctf-network` 是默认项目的业务网络，项目名不同时使用实际名称；数据库备份账号需具备 dump/restore 需要的权限。示例 `disable` 仅适用于模板中私有 Docker 网络的无 TLS PostgreSQL，受 TLS 保护的 PostgreSQL 使用 `require` 等真实策略，不盲目关闭生产数据库 TLS。

S3 改用 `--storage-kind s3`、endpoint、bucket、access-key-file 和 secret-key-file，不挂 Local root。对象内容、Content-Type 与 metadata 都进入校验。可用 `--jetstream-state-file` 附上停止前清单。

将 `.age`、同名 `.minisig` 和所需恢复资料复制到独立故障域，外部保管 secret 集和解密私钥。没有隔离恢复演练前，不把“备份文件存在”当作可恢复证明。

## 4. 恢复到空隔离目标

目标停止全部角色与 NATS，准备匹配 PG major、数据库名、存储类型、bucket、Secret 集和恢复点 Host digest。数据库、文件根/bucket、JetStream 目录都必须为空；工具拒绝覆盖或合并。

恢复命令使用同样的连接/存储参数，改为 `restore`，并将目标挂载设为可写。关键参数：

```text
restore
--input /backup/noctf-point.tar.gz.age
--age-identity-file /keys/age-identity
--minisign-public-key-file /keys/minisign.pub
--host-image 与备份完全相同的repository@sha256
--secret-set-id 与备份相同的Secret集合标识
```

源备份只读挂载，目标用新的独立目录/卷。工具验签解密、核对 schema、逐表行数、对象哈希/元数据和 JetStream 清单；部分失败时保持停写，不能启动 Host 自动建表掩盖空目标。

## 5. 验证并切换

1. 恢复成功后先启动 NATS，检查 live stream/consumer/KV/pending 与 ack。
2. 等待旧短租约到期，检查 Runner fencing 和资源域所有权。
3. 用恢复点相同 Host digest 启动各角色和迁移检查。
4. 验收登录/刷新、文件、比赛、提交投递、榜单和新 Runtime。
5. 确认源端停写，仅目标接受正式业务，再切 DNS/代理入口。

## 独立演练

```bash
bash deploy/shared/recovery/rehearse.sh --context YOUR_CONTEXT \
  --host-image YOUR_REGISTRY/noctf@sha256:YOUR_DIGEST \
  --tool-image noctf-recovery:reviewed \
  --report /YOUR_TEST_DIRECTORY/recovery-results.json
```

只对专用隔离环境执行。演练创建唯一临时资源，验证 Local/S3、consumer/KV/pending、篡改与非空目标拒绝，并只清理自己的资源。完整 CLI 见 [恢复工具说明](https://github.com/D1no209/NoCTF/blob/HEAD/deploy/shared/recovery/README.md)。
