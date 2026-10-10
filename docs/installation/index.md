# 选择安装方式

安装 NoCTF 使用 GitHub Actions 已编译的完整容器镜像。下载同次发布的部署配置包，配置执行资源和网络，再启动平台及依赖、完成业务验收。安装服务器不需要应用源码或编译环境。

## 部署方式对比

| 方式 | 适合场景 | 准备条件 | 手册入口 |
| --- | --- | --- | --- |
| Linux Docker 单机 | 第一套部署、小型到中型赛事、独立服务器 | Linux Docker Engine、Compose v2、HTTPS 反代、题目直连端口 | [Docker 安装](./docker.md) |
| 已有 Kubernetes | 有集群运维能力，需要按节点调度执行资源 | Cilium、Metrics Server、Envoy Gateway、Retain 存储与节点隔离 | [Kubernetes 安装](./kubernetes.md) |
| Windows Docker Desktop + kind | 隔离验证平台与前四赛制，媒体能力另行验收 | Docker Desktop 分配至少 8 CPU / 16 GiB、PowerShell 7 | [本地 kind](./kubernetes.md#本地-kind-验证环境) |

首次生产安装可以从 Linux Docker 单机开始；Docker、Kubernetes 和本地 kind 都可以直接使用 CI 完整镜像。

LiveSolo 媒体是独立可选部署，不包含在普通平台安装的能力保证中；当前单机媒体 overlay 与正式启用条件见 [媒体部署](../live-solo/media-deployment.md)及 [验收](../live-solo/readiness.md)。

## 安装顺序

1. [准备服务器与依赖](./prerequisites.md)：软件、磁盘、域名、镜像和 context。
2. [获取发布产物](./images.md)：下载部署配置包、读取 digest、拉取完整镜像。
3. [Docker](./docker.md) 或 [Kubernetes](./kubernetes.md)：生成配置并部署基础服务与 Host。
4. [域名、HTTPS 与反向代理](./reverse-proxy.md)：平台访问、Registry、题目网络和 WebSocket。
5. [配置项与存储](./configuration.md)：核对签名密钥、管理员、存储和 Provider。
6. [首次启动与验收](./verify.md)：验证登录、文件、Runtime、提交与排行榜。
7. [备份与恢复](../operations/backup.md)：建立实际可恢复的恢复点后再开放正式比赛。

## 发布配置目录和安装目录

```text
/opt/noctf-release/          # Action 部署配置包，无应用源码
  deployment.json           # 发布 revision、version、不可变镜像地址
  deploy/                   # Compose/K8s 模板、向导与共享资产
/opt/noctf/                  # 安装目录；真实配置与持久数据
  .env                      # Compose 参数与部署密钥
  installation.json         # 配置向导状态；同样包含敏感内容
  docker-compose.yml
  env/noctf/.env            # Host 的 .NET 环境变量
  env/postgres/.env
  env/registry/.env
  config/docker/config.json # Runner 拉取题目镜像的认证
  config/registry/auth/htpasswd
  data/                     # PG、Redis、NATS、Registry、上传目录等
```

发布配置目录保存下载的模板、脚本和元数据。安装目录保存填好的配置、密钥和真实业务数据，两者分开。升级时下载匹配新镜像的新配置包，不移动持久数据；不要直接在解压的 `deploy/docker` 中创建正式平台数据。

## 关于 Bun

Bun 用于本仓库前端、文档站的依赖管理和构建。使用预构建的完整 Host 镜像安装平台时，服务器无需安装 Bun、Node.js 或 .NET SDK；前端已经打包到 Host 的 `wwwroot` 中，生产不另起 Nuxt 服务。

如仅想运行这份手册，阅读 [维护与发布文档站](../contributing.md)，无需先安装 NoCTF。
