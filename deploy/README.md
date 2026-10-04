# NoCTF 部署入口

普通安装使用 GitHub Actions 已编译的完整 Host 镜像。下载同次 CI 运行的
`noctf-deploy-完整提交SHA` artifact，校验并解压配置包，再在解压目录执行下方命令。
镜像 digest 见运行摘要或包内 `deployment.json`。安装服务器无需 Git、应用源码或编译工具。
逐步操作见[镜像与配置包获取](https://github.com/D1no209/NoCTF/blob/HEAD/docs/installation/images.md)。

```bash
bash deploy/configure.sh
```

交互式向导支持 Linux/WSL Docker 与已有 Linux Kubernetes。默认只生成并校验，明确选择后才部署。
Windows Docker Desktop 的独立 kind 环境使用 K8s PowerShell 入口。

| 目录 | 用途 |
|---|---|
| [docker](docker/README.md) | 单机统一 Host、本地文件默认、可选 RustFS/监控、独立 Cap、反代示例 |
| [k8s](k8s/README.md) | 公共 base、kind/production overlays、平台依赖和验收 |
| [shared/recovery](shared/recovery/README.md) | PostgreSQL、文件/RustFS、停写 JetStream 的一致恢复点 |
| shared/observability | 两种部署共用规则、日志配置和仪表盘，采集配置分别维护 |
| shared/dependencies.lock.json | 已核验版本、镜像和 Chart 校验信息 |

Docker 和 K8s 都在 Host 启动时自动迁移，完成前不开 HTTP 或后台消费者。
每个执行资源域仅由一个 Runner 持 fencing 租约。已有生产、数据、密钥、外部反代和网络不会自动迁移或替换。
填充的 env、Secret、私钥、Docker auth、恢复点和安装状态文件限制权限、放在仓库外。
旧根 Docker 初始化入口仅提示新路径；已有安装目录不需要因为仓库整理而迁移数据。
