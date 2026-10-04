# 配置项与存储

本页解释首次安装和运维最常接触的配置。完整字段以对应 revision 的 `deploy/` 模板和配置向导为准；不要将开发 `appsettings.json` 中的示例密码用于生产。

## 配置文件的职责

| 文件 | 用途 |
| --- | --- |
| 安装根 `.env` | Compose 镜像、部署变量、模板插值和密钥 |
| `env/noctf/.env` | Host 环境变量，使用 `__` 表示层级 |
| `env/postgres/.env` | 数据库首次初始化变量 |
| `env/registry/.env` | Registry 存储与 bcrypt 认证设置 |
| `installation.json` | 向导参数和恢复配置来源，包含密钥 |
| Kubernetes `noctf-config` / `noctf-secrets` | 非敏感配置 / 敏感值 |

向导重新生成时可能根据安装状态重写生成文件。手工改动应记录到受控运维配置流程，重新运行向导前核对是否会覆盖。使用 `check` 验证，不仅凭 `.env` 编辑成功判断有效。

## 关键环境变量

| 字段 | 作用与核对方法 |
| --- | --- |
| `Hosting__Roles__0/1/2` | Api、Worker、Runner，至少一个；改动重启生效 |
| `ConnectionStrings__PostgreSql` | 所有角色连接同一业务数据库 |
| `ConnectionStrings__Redis` | 缓存/backplane，不能替代数据库 |
| `ConnectionStrings__Nats` | 生产 JetStream 连接地址 |
| `Authentication__SigningKey` | 共享认证签名密钥，使用随机值并外部保管 |
| `RunnerScoring__SigningKey` | 内部评分回调及受保护日志用途，各角色保持一致 |
| `EmailVerification__EncryptionKey` | 32 字节 Base64，保护 SMTP/验证码 secret |
| `SeedAdmin__UserName/Email/Password` | 首次管理员；已有账号密码不会因修改 seed 重设 |
| `Database__AutoMigrate` | 部署模板为 true，Host 就绪前执行迁移 |
| `Database__StartupTimeoutSeconds` | 数据库启动与迁移等待预算，模板为 180 秒 |
| `Authentication__RefreshAllowedOrigins__0` | 精确刷新/退出来源，生产 HTTPS Origin |
| `ForwardedHeaders__KnownNetworks__0` | 实际可信代理 CIDR |
| `Runtime__Provider/RunnerPool` | 平台统一执行提供方与池 |
| `Runner__Provider/Pool` | 必须与 Runtime placement 匹配 |
| `Runtime__Execution__ProcessesPerService` | 部署控制每服务 PID 上限，模板为 256 |
| `Runtime__Docker__PublicHost` | 题目随机端口对外连接域名/IP |
| `Observability__Enabled` | 指标/日志采集开关，基础 Docker 默认关闭 |

密钥不是“改了重启就无损”的普通配置。JWT key 变化影响会话，邮件加密 key 变化影响已保存 secret，RunnerScoring key 变化还影响历史日志中 UserId 的解密。升级时保持稳定，轮换前制定独立流程。

## 本地文件存储

默认 Docker 配置：

```dotenv
Storage__Provider=Local
Storage__LocalRoot=/app/uploads
Storage__PublicBaseUrl=https://noctf.example.com
```

安装目录 `data/uploads` bind mount 到 `/app/uploads`。数据库 File 记录保存不可变对象元数据，业务记录引用 FileId。替换附件会创建新文件，旧无引用文件由清理流程回收。

保持目录可写，并将它与 PostgreSQL、停写后的 NATS 一起备份。仅保存数据库不能恢复附件、修复包、头像和题解。

## 可选 RustFS / S3

首次向导选 `s3` 时生成对应参数，启用 `compose.rustfs.yml`。助手部署会先启动 RustFS，再运行同一个 Host 镜像的 `storage-init` 初始化 bucket，成功后启动平台。

手工执行的顺序为：

```bash
cd /opt/noctf
docker --context YOUR_CONTEXT compose --env-file .env -f docker-compose.yml -f compose.rustfs.yml config --quiet
docker --context YOUR_CONTEXT compose --env-file .env -f docker-compose.yml -f compose.rustfs.yml up -d postgres redis nats registry rustfs
docker --context YOUR_CONTEXT compose --env-file .env -f docker-compose.yml -f compose.rustfs.yml run --rm --no-deps storage-init
docker --context YOUR_CONTEXT compose --env-file .env -f docker-compose.yml -f compose.rustfs.yml up -d noctf
```

RustFS 的 `data/rustfs` 与 `data/rustfs-logs` 需要让 10001:10001 可写，控制台保持私有。文件 HTTPS Origin 要按实际代理设置；不要只修改 bucket 名而忽略公开地址和权限。

已有 Local 安装不能直接重跑向导切成 S3。需要停写、复制对象、保留 FileId/ObjectKey、验证内容/长度/元数据，再切换配置和验收。S3 切回 Local 同样需要迁移计划。

## Runtime 平台设置

Docker 使用部署级 challenge bridge 和内部 callback bridge，单服务/Checker 复用网络，多服务实例才创建独占 bridge。Kubernetes 在共享 Runtime namespace 中为每服务创建 Pod，多服务才使用 headless discovery。

Provider 与 Pool 由部署选定，题库模板仅描述服务、镜像、资源和访问入口。角色、Provider、Pool 变化后重启对应 Host；不能靠编辑比赛切换正在运行的执行域。

## 改配置后的操作

1. 备份受限配置并记录修改范围。
2. 校验 Compose/Kustomize 和 secret 格式。
3. 评估是否涉及存储、Provider、签名密钥或不兼容 schema。
4. 对普通 Docker 环境变量变更执行受控 `up -d noctf` 重新创建容器；单纯 `restart` 不会加载修改后的容器环境。
5. Kubernetes 检查配置 checksum rollout；Secret 变更安排受控 rollout。
6. 验证 readiness、登录、文件、Runtime 和评测。
