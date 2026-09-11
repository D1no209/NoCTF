# NoCTF 单机生产部署

只有一份 `docker-compose.yml`，包含 NoCTF（Api + Worker + Runner）、PostgreSQL、Redis、NATS 和 Registry。
所有镜像均直接拉取；NoCTF 使用 GitHub CI 产出的 digest。Compose 没有 ports/expose、build、healthcheck、
environment、命名卷、tmpfs、group_add、cap、security_opt 或 read_only。没有 migration/监控/exporter 容器。

## 运维反向代理接入

外部 `1panel-network` 必须已经存在，本项目不创建或修改它，也不修改反向代理。

| 服务 | 默认网络别名 | 1panel-network 别名 | 容器内 HTTP 上游 |
| --- | --- | --- | --- |
| NoCTF | noctf | noctf-web | http://noctf-web:8080 |
| Registry | registry、noctf-registry | noctf-registry | http://noctf-registry:5000 |

数据库、Redis 和 NATS 只加入默认网络，不接入 1panel-network，也不发布宿主机端口。
镜像自身的 EXPOSE 元数据不等于发布宿主机端口；Compose 不设置 ports/expose。
TLS、域名、WebSocket/SignalR 转发及镜像上传请求大小/超时，由运维配置。

## 目录布局

```text
/opt/noctf/
├── docker-compose.yml
├── .env                         # Compose 镜像引用、共享参数和密钥
├── env/
│   ├── noctf/.env               # 仅传给 NoCTF
│   ├── postgres/.env            # 仅传给 PostgreSQL
│   └── registry/.env            # 仅传给 Registry
├── config/
│   ├── docker/config.json      # NoCTF 拉取私有镜像的标准 docker login 配置
│   └── registry/auth/htpasswd  # Registry bcrypt 账户文件
└── data/
    ├── postgres/
    ├── redis/
    ├── nats/
    ├── registry/
    ├── uploads/
    └── backups/
```

业务数据全部使用目录 bind mount，缺失目录时拒绝隐式创建。Docker socket 是本地 Runner 的通信挂载，
不是持久化业务卷。包含 Runner 的 Host 镜像以 root 运行以访问 Docker socket；这是宿主机级权限，
只应运行可信平台镜像。本项目不会 chmod 宿主机 socket，也不动态修改宿主机用户/组。

## 首次安装

1. 从仓库执行 `bash deploy/init-layout.sh /opt/noctf`。已有文件不会覆盖，不会启动服务。
2. 填写安装目录的 `.env`。NoCTF 镜像填 CI 发布的 `repository@sha256:...`。数据库密码、JWT/Runner
   签名密钥和 Registry HTTP secret 使用独立随机值；数据库密码可使用 32 字节随机数的 hex 编码。
   邮箱加密密钥必须是 32 字节随机数的 Base64。`.env` 文件保持仅运维账户可读。
3. 填写真实公开域名、URL、题目连接地址和反向代理来源网段。可只读执行
   `docker network inspect 1panel-network` 核对网段；不要猜测，更不要自动改反代。
4. 由运维使用 bcrypt 创建 Registry 账户，例如在安装目录执行
   `htpasswd -Bc config/registry/auth/htpasswd challenge-publisher`，交互输入密码。
   此命令仅用于首次创建；文件已存在时不要使用 `-c`，否则会覆盖已有账户。
5. 进入安装目录，先执行 `docker compose config --quiet`，然后 `docker compose up -d`。
   第三方镜像不额外包装 healthcheck；NoCTF 的 `/health/ready` 探针定义在 CI 镜像 Dockerfile 内。
6. NoCTF 在启动 HTTP 和后台消费者前自动执行 EF migration 与管理员初始化，数据库短暂未就绪会
   有界重试。错误密码或非法迁移不会被忽略。已有管理员不会被重新设置密码。

## 人机验证

平台管理员在“邮件与人机验证”中选择 `None`、`Cap` 或 `Turnstile`，填写公开参数、单独更新 secret，
并启用验证。配置保存在数据库中，secret 使用 `EMAIL_VERIFICATION_ENCRYPTION_KEY` 加密且不会由 API
返回；升级时必须保留该加密密钥。运行时验证服务不可用时，敏感请求返回 503，不会自动放行。

`.env` 中的 `HUMAN_VERIFICATION_PROVIDER`、Provider 参数和 secret 只用于尚未保存数据库 Provider 的
首次启动回退，以保留旧部署行为。管理员首次保存人机验证设置后，数据库配置立即成为事实源，无需重启。
新安装可让 `HUMAN_VERIFICATION_PROVIDER=None` 并从后台完成配置。`None` 只关闭验证码，不关闭 Redis
限流、并发配额和幂等保护。

Cap 使用独立部署的官方 Cap Standalone。在后台填写 Server URL、site key 和 secret；用于首次启动回退时
可填写 `CAP_SERVER_URL`、`CAP_SITE_KEY`、`CAP_SECRET`。生产 URL
必须为 HTTPS。Cap 实例必须同时能被浏览器和 NoCTF 容器访问，并将 CORS 精确限制到 NoCTF 公开来源。
若 Cap 位于反向代理后，只向代理公开其应用端口，并按实际代理设置来源 IP 头；Cap 会直接信任其配置的
IP 头，因此不能绕过代理直接暴露源站。启用 Standalone 的 asset server，并固定
`WIDGET_VERSION=0.1.57` 与 `WASM_VERSION=0.0.7`；NoCTF 会从同一实例加载求解 WASM，避免运行时
依赖公共 CDN。NoCTF 不把 Cap 或其 Valkey 生命周期并入本 Compose 栈。

Turnstile 在后台填写 site key、secret 和不含 scheme/路径的允许 hostname；用于首次启动回退时可填写
`TURNSTILE_SITE_KEY`、`TURNSTILE_SECRET` 和 `TURNSTILE_ALLOWED_HOSTNAME`。测试、预发布与生产使用不同 widget；生产 hostname 不得使用 localhost。
后端固定调用 Cloudflare Siteverify，并将经过可信代理处理的来源 IP 作为验证上下文发送。站点隐私说明已
同步披露该行为。

## 题目镜像仓库

Registry 默认启用 htpasswd，外部 TLS 由运维反代终止。Docker 宿主机拉取镜像时不使用容器 DNS，
因此题目 Manifest 和镜像名必须使用运维配置的 HTTPS Registry 域名，不能把 `noctf-registry:5000`
当作宿主机可解析的地址。默认网络别名供容器间访问，不替代宿主机 DNS/TLS。

运维在安装目录执行：

```sh
docker --config ./config/docker login registry.example.com
docker tag challenge:test registry.example.com/challenges/example:test
docker --config ./config/docker push registry.example.com/challenges/example:test
```

NoCTF SDK 拉取单容器/Checker 与 Compose CLI 共用挂载的 `config/docker/config.json`。使用独立配置
目录中的 inline auths，不要复制引用桌面系统 credential helper 的配置。凭据只按精确 Registry
主机匹配，不能放进题目 Definition 或提交到 Git。

基础 htpasswd 不区分只读/写入角色；需要细粒度授权时由运维部署相应 token 服务。不要匿名开放写入。

## 已有生产实例迁移

**不能直接把旧命名卷替换为空目录后启动。** 先由运维安排 NoCTF 停机窗口并确认项目/容器/卷的精确身份：

- 备份数据库、上传文件和配置，记录用户/题目数量及当前镜像。
- 保留原数据库密码、JWT/Runner 签名密钥、邮箱加密密钥；PG 环境变量不会重设已有数据库密码。
- 保持正确的 COMPOSE_PROJECT_NAME，不要因项目名变化误把新建空项目当成旧项目升级。
- 停止本项目写入者，再停止本项目 PostgreSQL/Redis/NATS，将旧卷内容完整复制到对应目录，保留权限；
  不从运行中的 PostgreSQL 数据目录直接复制，不更换 PG 主版本。
- 旧的拆分角色与监控/exporter 容器由运维核实后退役，保留旧卷和备份直到验收，不做全局 prune。
- 启动新拓扑后检查登录、用户/题目数量、文件下载、消息队列及题目容器。其他项目服务不在操作范围内。

CI 只部署测试服务器。它读取现有应用容器记录的 Compose、环境文件与项目名，校验环境变量和数据挂载后，
仅通过额外的镜像覆盖文件更新 NoCTF 应用，保留现有运行用户。旧版拆分角色和新版统一 Host 都不会在发布时
被自动换成另一种拓扑。发布覆盖会把可安全等价转换的 wildcard `ASPNETCORE_URLS` 改写为
`ASPNETCORE_HTTP_PORTS`/`ASPNETCORE_HTTPS_PORTS`，完整保留 8080、9464 等实际监听端口并消除重复配置告警；
特定 IP 或非 URL 绑定不会自动改写。
安装用的 `docker-compose.yml` 不覆盖已有环境，目录化迁移必须另行安排。
发布前备份数据库与配置；发布后检查 readiness、业务记录数量及网络 ID。数据库、Redis、NATS、
Registry、监控与反代不重建，不清理数据卷或宿主机缓存。失败时仅在数据库结构未变化时回退应用镜像，
不会自动恢复数据库覆盖新数据。迁移仍由应用按现有 `Database:AutoMigrate` 配置在启动时执行，不创建迁移容器。

`/health/ready` 保持 fail-closed，但相同故障只在状态变化时写一次结构化日志。排查 503 时读取响应中的
`data`：每个依赖都有 `<dependency>.status`；账户邮件还提供
`account-notification-delivery.state`。`ConfigurationUnavailable` 表示邮件验证已启用，但 SMTP 配置缺失或
`EMAIL_VERIFICATION_ENCRYPTION_KEY` 无法解密既有密码；应修正配置或恢复原加密密钥，不能通过关闭
readiness 掩盖。

生产服务器禁止通过 CI 部署。人工部署使用 CI 发布的同一 digest，通过 SSH 显式调用
`update_image.py --manual-production`；该参数只接受已确认的生产主机，CI 包装脚本不传此参数且独立拒绝生产主机。
两个路径都只保留并校验既有网络（包括 `1panel-network`），不会创建、删除或重建网络。

默认关闭 OpenTelemetry 与 Prometheus exporter，监控页面的外部指标显示不可用，不影响普通业务与日志。
本文件只约束平台部署栈；题目运行时的 Docker 随机端口发布与沙箱规则不因此更改。
