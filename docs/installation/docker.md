# Docker 单机安装

目标：在 Linux 上直接运行 GitHub Actions 已构建的完整 Host 镜像，以及 PostgreSQL、Redis、NATS JetStream 和认证 Registry，完成管理员登录和测试题目运行。平台程序和前端均由 CI 编译，用户只需要发布配置包和容器镜像。

::: tip 执行位置
先按 [获取发布镜像与部署配置包](./images.md)下载同次 Action 的产物。向导命令在配置包解压目录 `/opt/noctf-release` 执行；Compose 命令在真实配置与数据目录 `/opt/noctf` 执行。
:::

## 1. 完成安装前检查

先完成 [准备服务器与依赖](./prerequisites.md)，取得 Host 镜像 digest，并确认当前 Docker context 指向预期宿主。

```bash
cd /opt/noctf-release
docker context ls
docker --context YOUR_CONTEXT info
docker --context YOUR_CONTEXT compose version
docker --context YOUR_CONTEXT network inspect noctf-proxy
```

不存在 `noctf-proxy` 时，仅在目标宿主没有同名网络且你准备自行部署容器化反代的情况下创建：

```bash
docker --context YOUR_CONTEXT network create --driver bridge noctf-proxy
docker --context YOUR_CONTEXT network inspect noctf-proxy --format '{{json .IPAM.Config}}'
```

记录真实代理所在 subnet，例如输出的 `172.20.0.0/16`。示例不是固定值，向导的可信代理 CIDR 必须填写实际网络。context 指向远程 Docker 时，bind mount 路径在远程宿主解析；应在目标 Linux 宿主运行向导和安装目录操作，不能把本地文件生成误认为远程文件已经存在。

## 2. 运行配置向导

```bash
cd /opt/noctf-release
bash deploy/configure.sh
```

向导需要交互终端。需要在 `/opt` 创建目录和设置首次数据目录属主时，使用有权限的运维终端；例如默认本机 root 管理场景可执行 `sudo bash deploy/configure.sh`。sudo 后 Docker context 与登录凭据可能属于 root，必须再次核对，不要无意切换目标。

推荐首次按以下表填写：

| 提示 | 填写方法 |
| --- | --- |
| 部署方式 | `1`，Docker |
| 安装目录 | `/opt/noctf`，不在仓库中 |
| Docker context | 已核对的 context，例如本机 `default` |
| 项目名称 | `noctf`；安装后保持一致，网络名称依赖它 |
| 统一 Host 镜像 | 发布流程提供的完整 `repository@sha256:...` |
| 平台公开域名 | `noctf.example.com`，填写你自己的域名 |
| 平台 HTTPS Origin | `https://noctf.example.com`，只含 scheme、host 和必要端口 |
| 题目公开连接域名或 IPv4 | 实际题目宿主的 DNS/IP，不填 `http://` 或路径 |
| 可信代理实际 CIDR | 上一步记录的代理网络 CIDR |
| 管理员用户名/邮箱 | 首次管理员身份；邮箱需真实可用 |
| 管理员密码 | 输入自己的强密码，终端不回显；留空会生成 |
| 各类部署密钥 | 新安装可留空自动生成；已有安装回车保留 |
| 文件存储 | 首次可选 `local`；`s3` 会启用 RustFS overlay |
| Registry HTTPS 主机名 | 宿主 Docker 能访问的 Registry 域名 |
| Registry 用户名/密码 | 为出题镜像专门创建的凭据 |
| 独立监控 | 先选 `no`，或按 [监控安装](../operations/monitoring.md)准备 |
| Grafana 管理密码 | 按向导填写；即使监控未启用也可能询问 |

向导自动生成 JWT、Runner 回调、数据库、Registry 等密钥。`EmailVerification` 加密密钥采用 32 字节 Base64；其他生成密钥采用随机十六进制。现有安装重跑时保留密钥和数据，不能借此直接变更已有存储类型。

摘要不显示密钥。首次选择“生成并校验”，最后的部署询问先回车，仅保存配置。这样可以先完成 DNS/TLS 和代理配置。

## 3. 核对生成内容

```bash
sudo stat -c '%a %n' /opt/noctf/.env /opt/noctf/installation.json
sudo test -s /opt/noctf/config/registry/auth/htpasswd
sudo test -s /opt/noctf/config/docker/config.json
```

配置文件限制权限，不在工单或日志中贴出内容。向导配置 Registry bcrypt 认证和 Docker inline auth；不要复制桌面的 credential helper 配置，它在 Linux Host 中可能不可用。

重点核对 `/opt/noctf/env/noctf/.env` 中的公开 Origin、Refresh Origin、CORS、可信代理、Runtime Provider/Pool 和文件路径。向导会生成精确的 `Authentication__RefreshAllowedOrigins__0` 和 `Cors__AllowedOrigins__0`，手工安装时这两项也必须填写。

使用配置助手重新校验，它根据安装状态自动选择存储和监控 overlay：

```bash
cd /opt/noctf-release
sudo python3 deploy/shared/configuration.py check /opt/noctf
```

若用 root 生成配置，后续同样使用能访问这些文件的账号执行。不要通过打印完整 `docker compose config` 输出检查密钥，使用 `config --quiet`。

## 4. 配置反向代理

按 [域名、HTTPS 与反向代理](./reverse-proxy.md)接入平台和 Registry。平台上游为 `noctf-web:8080`，Registry 为 `noctf-registry:5000`。端口不直接发布给宿主，浏览器从 HTTPS 域名访问。

先准备代理文件、证书和域名，下一步启动 core 服务后再检查并启动代理；Nginx 解析 upstream alias 时需要对应容器已经存在。

Registry TLS 和 DNS 需可被宿主 Docker 访问；错误的 Registry HTTPS 会导致题目启动拉取失败，即使平台首页已经能打开。

## 5. 启动平台

推荐用助手执行部署：

```bash
cd /opt/noctf-release
sudo python3 deploy/shared/configuration.py deploy /opt/noctf
```

它校验外部网络，启动依赖，等待数据库，按选择初始化 S3 桶/监控，再启动 Host 并等待 readiness。首次 Host 自动执行 EF 迁移和管理员初始化，完成前不会开放 HTTP 或业务消费者。无需另行手写 SQL 建表或启动 `NoCTF.API` 可执行程序。

若使用 `local` 且没有监控，也可以手工执行：

```bash
cd /opt/noctf
docker --context YOUR_CONTEXT compose --env-file .env -f docker-compose.yml config --quiet
docker --context YOUR_CONTEXT compose --env-file .env -f docker-compose.yml up -d
docker --context YOUR_CONTEXT compose --env-file .env -f docker-compose.yml ps
docker --context YOUR_CONTEXT compose --env-file .env -f docker-compose.yml exec -T noctf /usr/local/bin/noctf-healthcheck
```

健康检查退出码应为 0。启动失败先查看目标服务的有限日志，不反复删库重装：

```bash
docker --context YOUR_CONTEXT compose --env-file .env -f docker-compose.yml logs --tail=100 noctf
```

## 6. 登录并验收

打开 `https://你的平台域名`，使用向导配置的管理员登录。若密码自动生成，只在权限受限的本机编辑器中查看安装配置的 `SEED_ADMIN_PASSWORD`，不要将配置复制到聊天或 Git。

完成 [首次启动与验收](./verify.md)：检查刷新会话、附件、题目环境公网访问、Flag 评测、排行榜和通知。题目随机端口由 Docker 实际分配，不是平台 443 反代。

## 7. 手工准备目录的替代方式

`init-layout.sh` 只复制缺少的模板和创建目录，不生成完整可用配置，不启动服务，不覆盖已有文件：

```bash
cd /opt/noctf-release
sudo bash deploy/docker/init-layout.sh /opt/noctf
```

之后必须手工填 `.env`、三个 `env/*/.env`，追加 Refresh/CORS Origin，生成 Registry bcrypt 文件和受限 Docker auth，配置属主、代理和 TLS，再校验启动。首次安装优先使用向导，避免遗漏这些步骤。

## 安装后保存的资料

记录 Git revision、Host digest、context、项目名、Provider/Pool、域名与可信代理范围。将部署密钥、Registry 凭据和数据库/文件/NATS 一致恢复点分开保管。生产更新阅读 [升级与回退](../operations/upgrade.md)。
