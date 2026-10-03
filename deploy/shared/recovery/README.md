# 一致恢复工具 v2

仅制作应用与 NATS 停止写入的一致离散恢复点，非在线 PITR。包含 PG public/EF history、
Local 文件或 RustFS/S3、离线 JetStream。age 加密、Minisign 签名，密钥外部保管；旧 v1 不支持。

```bash
docker --context YOUR_CONTEXT build -t noctf-recovery:reviewed deploy/shared/recovery
```

基础 PG digest 沿用锁定版本；S3 SDK 及依赖由 requirements.lock 固定版本/SHA。
凭据仅 `--*-file` 和只读挂载，不能放进参数或镜像。源文件/NATS 只读挂载，目标读写挂载。

## CLI

backup/restore 均需 `--postgres-host`、`--postgres-database`、`--postgres-user`、
`--postgres-password-file`、`--postgres-ssl-mode`、`--storage-kind local|s3`、`--jetstream-dir`、
`--jetstream-stopped`、`--host-image repository@sha256:...`、`--secret-set-id`。

- Local：`--local-root`；S3：`--s3-endpoint`、`--s3-bucket`、`--s3-access-key-file`、`--s3-secret-key-file`。
- backup：`--age-recipients-file`、`--minisign-secret-key-file`、`--output`，可传停止前的 `--jetstream-state-file`。
- restore：`--input`、`--age-identity-file`、`--minisign-public-key-file`，核验同名 `.minisig`。

`--jetstream-stopped` 是运维确认，不是停止指令。工具不管理生产容器/Pod；PG 其他连接会被拒绝，
文件/S3 写入者也必须由运维停止。对象 Content-Type（含参数）、metadata 和内容由 SDK 保存并校验。
拒绝特殊文件、symlink、归档穿越、非空目标或 image/存储/PG major/Secret 集不匹配。

示例：

```bash
docker --context YOUR_CONTEXT run --rm --network YOUR_PRIVATE_NETWORK \
  --mount type=bind,source=/run/noctf-recovery,target=/keys,readonly \
  --mount type=bind,source=/opt/noctf/data/uploads,target=/files,readonly \
  --mount type=bind,source=/opt/noctf/data/nats,target=/jetstream,readonly \
  --mount type=bind,source=/var/backups/noctf,target=/backup \
  noctf-recovery:reviewed backup \
  --postgres-host postgres --postgres-database noctf --postgres-user BACKUP_USER \
  --postgres-password-file /keys/postgres-password --postgres-ssl-mode require \
  --storage-kind local --local-root /files --jetstream-dir /jetstream --jetstream-stopped \
  --host-image YOUR_HOST_IMAGE_AT_DIGEST --secret-set-id YOUR_SECRET_SET \
  --age-recipients-file /keys/age-recipients --minisign-secret-key-file /keys/minisign.key \
  --output /backup/noctf-point.tar.gz.age
```

恢复用 `restore` 与验签/解密参数，指向空隔离目标；不能改源挂载进行原地恢复。
恢复完成仍停写，启动 NATS 验证 live 状态，再验收对应 Host。
完整停写、切换、回退见[运行手册](../../../specs/backup-recovery.md)。

## 独立演练

```bash
bash deploy/shared/recovery/rehearse.sh --context YOUR_CONTEXT \
  --host-image YOUR_HOST_IMAGE_AT_DIGEST --tool-image noctf-recovery:reviewed \
  --report /YOUR_TEST_DIRECTORY/recovery-results.json
```

唯一名称的容器/卷/网络只在演练内创建和清理；真实 EF schema、Local/RustFS、
带参数的 Content-Type、durable consumer、KV、pending/ack 和篡改/非空目标拒绝均实测。
