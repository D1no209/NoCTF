# Docker 单机部署

默认统一 Host（Api、Worker、Runner）、PostgreSQL、Redis、NATS JetStream、认证 Registry。
本地文件默认，RustFS 与独立监控通过可选 overlay 启用。平台不发布宿主端口，运维通过已有
`noctf-proxy` 独立代理网络反代；题目服务直接由 Docker 分配随机宿主端口，不经过平台反代。

首次安装由运维创建该外部 bridge，并将容器化反向代理接入：

```bash
docker --context YOUR_CONTEXT network create --driver bridge noctf-proxy
docker --context YOUR_CONTEXT network inspect noctf-proxy --format '{{json .IPAM.Config}}'
```

已有同名网络时只检查，不重复创建。使用实际代理 CIDR 填写可信网络，平台、Registry、
可选 Grafana 与 Cap 共用该入口网络；数据库和题目网络保持各自边界。详细安装见
[平台手册](../../docs/installation/docker.md)，已有安装网络切换见
[升级说明](../../docs/operations/upgrade.md)。仓库模板修改不会自动迁移现有线上容器。

## 配置与启动

```bash
bash deploy/configure.sh
```

Linux/WSL 需要 Bash 4.3+、Python 3/PyYAML、OpenSSL、Docker/Compose v2、htpasswd。
向导收集显式 context、Host digest、域名、可信代理 CIDR、管理员、Registry、存储与监控参数。
新密钥生成、已有密钥复用，摘要不显示密码；默认只生成并校验，输入 `deploy` 才启动。
不能通过重跑向导直接切换已有存储类型，数据迁移必须独立停写执行。
手工准备可用 `bash deploy/docker/init-layout.sh /opt/noctf`，只复制缺失模板、不启动或覆盖服务。

安装目录包含 `.env`、`env/{noctf,postgres,registry}/.env`、受限权限的
`installation.json`、`config/docker/config.json`、`config/registry/auth/htpasswd`、Compose 和 data 目录。
配置修改前保存受限权限备份，这些文件包含密钥，不得入库。

```bash
cd /opt/noctf
docker --context YOUR_CONTEXT compose --env-file .env -f docker-compose.yml config --quiet
docker --context YOUR_CONTEXT compose --env-file .env -f docker-compose.yml up -d
docker --context YOUR_CONTEXT compose --env-file .env -f docker-compose.yml ps
```

Host 启动自动应用 EF 迁移和管理员初始化，完成前不开 HTTP/消费者；错误、超时阻止就绪。
EF/Npgsql 保护并发迁移，管理员唯一键竞争回读获胜账户，已有密码不重设。
普通更新保留目录和密钥，但重启可能应用迁移；升级前必须备份并审查 schema 兼容性。
非兼容更新需停写旧角色，不能混跑。镜像已有 healthcheck，检查 readiness 后验收实际业务。

## RustFS 可选

```bash
docker --context YOUR_CONTEXT compose --env-file .env -f docker-compose.yml -f compose.rustfs.yml config --quiet
docker --context YOUR_CONTEXT compose --env-file .env -f docker-compose.yml -f compose.rustfs.yml up -d postgres redis nats registry rustfs
docker --context YOUR_CONTEXT compose --env-file .env -f docker-compose.yml -f compose.rustfs.yml run --rm --no-deps storage-init
docker --context YOUR_CONTEXT compose --env-file .env -f docker-compose.yml -f compose.rustfs.yml up -d noctf
```

桶由同一 Host 的 S3 CLI 幂等初始化，控制台不发布。新 data/logs 目录须让 10001:10001 可写。
原 uploads 目录保留；Local→S3 需停写复制，保留 FileId/ObjectKey，校验内容、大小和元数据后启用。
只更改环境变量不等于迁移完成。

## 网络、权限及可选服务

- 外部 `noctf-proxy` 由运维管理。上游 `noctf-web:8080`、`noctf-registry:5000`；
  Nginx 示例不会自动安装或 reload。TLS、DNS、Registry HTTPS 由运维配置。
- 代理传递对称可信的 forwarded headers，允许 SignalR/WSRX Upgrade 和流式上传；
  `/api/v1/runtime-proxies/` 默认会话 30 分钟。上传上限仍由 Endpoint 执行。
- 单服务/Checker 复用部署级 challenges 和 internal callback bridge，多服务才创建独占网络。
  题目不接入平台数据网；PID 由平台限制为 256。
- Host 的 Docker socket 等同宿主权限，只运行可信镜像，不 chmod socket。
- Registry 使用 bcrypt 与受限 Docker inline auth；不复制桌面 credential helper，
  已有 htpasswd 不使用 `-c` 覆盖。镜像必须用宿主可解析的 HTTPS Registry 主机名。
- Redis 只用于缓存/backplane，maxmemory 默认 512 MiB、容器 1 GiB、allkeys-lru。
- 监控加入 `compose.monitoring.yml` 并运行[独立栈](observability/README.md)，
  私有 9464 及 Loki 只在启用时要求。基础监控不强制 Cap 网络。
- Cap 保持[独立部署](cap/README.md)，验证 secret 与管理 API key 用途隔离。

## 恢复与回退

见[一致工具](../shared/recovery/README.md)和[完整流程](../../specs/backup-recovery.md)。
停止写入后备份 PG、文件/RustFS、停止后的 JetStream，并外部保管密钥。
不从运行中的 PG/NATS 目录复制，不用空目录替代旧卷、不全局 prune。
schema/数据回退需要同一恢复点，不只是退镜像；生产只人工部署审查后的 CI digest，CI 不登录服务器。
