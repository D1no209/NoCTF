# 准备服务器与依赖

本页给出全新 Ubuntu 24.04 LTS 服务器的 Docker 安装准备示例。已有 Docker、面板、反代或集群时先核对现有部署，再只补齐缺少的依赖。其他发行版按 [Docker 官方安装文档](https://docs.docker.com/engine/install/)安装对应的软件包。

## 1. 规划资源

平台常驻 PostgreSQL、Redis、NATS、Registry、Host；题目和 Checker 还需要额外 CPU、内存和磁盘。没有一个适用于所有题目的固定容量数字。

可以用 4 vCPU / 8 GiB 作为单机功能试装的起点，这只是规划建议，不是生产容量保证。启用监控、RustFS、多服务题目或大量并发后，应基于实际镜像测量。Windows 完整 kind 验证环境的脚本则明确要求 Docker Desktop 至少分配 8 CPU / 16 GiB。

准备磁盘时分别预算数据库、上传文件、Registry、镜像缓存、NATS 消息、题目临时数据和异地备份。题目的内存以 MiB 配置，CPU 以核配置；不得把平台剩余内存全部许诺给题目。

## 2. 安装基础工具

以下命令在服务器上执行。使用有 sudo 权限的运维账号。

```bash
sudo apt-get update
sudo apt-get install -y ca-certificates curl openssl python3 python3-yaml apache2-utils
bash --version
python3 -c 'import yaml; print("PyYAML ready")'
openssl version
htpasswd -h
```

`htpasswd -h` 输出帮助并可能以非零状态退出，这是帮助行为。向导需要 Bash 4.3+、Python 3/PyYAML、OpenSSL 和 `htpasswd`；`apache2-utils` 提供 `htpasswd`。

## 3. 安装 Docker Engine 与 Compose v2

先确认没有需要保留的已有 Docker/containerd 工作负载。若存在冲突的软件包，按 [Docker Ubuntu 官方步骤](https://docs.docker.com/engine/install/ubuntu/)处理，不能直接卸载正在承载其他应用的 containerd。

在全新 Ubuntu 上添加 Docker 官方 APT 源：

```bash
sudo install -m 0755 -d /etc/apt/keyrings
sudo curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
sudo chmod a+r /etc/apt/keyrings/docker.asc

sudo tee /etc/apt/sources.list.d/docker.sources >/dev/null <<EOF
Types: deb
URIs: https://download.docker.com/linux/ubuntu
Suites: $(. /etc/os-release && echo "${UBUNTU_CODENAME:-$VERSION_CODENAME}")
Components: stable
Architectures: $(dpkg --print-architecture)
Signed-By: /etc/apt/keyrings/docker.asc
EOF

sudo apt-get update
sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
sudo systemctl enable --now docker
sudo docker version
sudo docker compose version
sudo docker run --rm hello-world
```

预期 Docker Client/Server 均可访问，Compose 显示 v2，测试容器正常退出。本手册后续命令按运维账号已能访问 Docker 编写；也可以在已授权的 root 运维终端执行。Docker socket 提供宿主级能力，不通过 `chmod 666 /var/run/docker.sock` 放开权限。

## 4. 获取 Action 发布产物

```bash
docker context ls
docker context show
```

按 [获取发布镜像与部署配置包](./images.md)从成功的 GitHub Actions CI 运行下载配置包，校验后解压到 `/opt/noctf-release`。该目录只包含部署模板、脚本和发布元数据，不需要 Git 或应用源码。

CI 已经编译完整 Host 和前端，默认发布到 `ghcr.io/d1no209/noctf`。从同次运行摘要或配置包的 `deployment.json` 读取 `repository@sha256:...`；如使用自定义 Registry，选择 `images` 中对应地址。安装服务器直接 pull，不执行本机编译。

```bash
# 替换为发布流程实际给出的镜像；digest 必须是完整的 64 位十六进制。
docker --context YOUR_CONTEXT pull YOUR_REGISTRY/noctf@sha256:YOUR_DIGEST
```

## 5. 准备网络与 DNS

| 名称 | 示例 | 用途 |
| --- | --- | --- |
| 平台域名 | `noctf.example.com` | 浏览器、API、SSO 回调、Webhook 公开链接 |
| Registry 域名 | `registry.example.com` | 出题镜像 push/pull，宿主 Docker 可解析 |
| 题目连接域名 | `challenges.example.com` | 指向执行题目的宿主或节点 |
| 文件域名（S3） | `files.example.com` | 按部署方案接入对象存储 HTTPS |

平台与 Registry 使用 HTTPS。外部代理接入已有的 `noctf-proxy`；没有该网络时，可由运维在目标 context 新建同名 bridge，再将容器化 Nginx 加入。平台 Compose 不发布宿主端口，代理必须能解析 `noctf-web` 和 `noctf-registry`。

确认 DNS 已解析到对应服务器，云安全组与宿主防火墙允许 80/443，以及经过规划的题目随机 TCP 端口。Docker 发布端口可能绕过 UFW 的常规规则，限制应结合云安全组、Docker 转发链和真实公网探测；参考 [Docker 防火墙说明](https://docs.docker.com/engine/install/ubuntu/#firewall-limitations)。

下一步进入 [Docker 单机安装](./docker.md)。
