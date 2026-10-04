# 获取发布镜像与部署配置包

NoCTF 的完整应用镜像由 GitHub Actions 自动构建并推送。普通安装只下载部署配置包并拉取镜像，服务器无需克隆应用源码、安装 Git、.NET SDK、Node.js 或 Bun，也无需执行 `docker build`。

## 1. 选择一次成功的 CI 发布

打开 [NoCTF 的 CI 工作流](https://github.com/D1no209/NoCTF/actions/workflows/ci.yml)，选择准备部署的成功运行。该工作流在 main 更新后发布完整 Host，包含前端静态文件，以及 Api/Worker/Runner 三种角色所需的程序。

运行摘要的 **Install NoCTF** 中记录不可变镜像地址。默认镜像仓库为：

```text
ghcr.io/d1no209/noctf
```

启用自定义 Registry 的部署还会列出同一镜像的自定义地址。镜像使用 `latest`、版本号和 `sha-完整提交SHA` 标签；安装时复制摘要中的 `repository@sha256:完整digest`，与配置包保持同一次运行。

不要从另一次运行取配置，或把本地随手编译的镜像用作正式发布版本。`Build backend only` 是以已有完整 Host 为基础保留旧前端的维护流程，首次安装选择这里的完整 CI 发布。

## 2. 下载部署配置包

在同一次运行页面底部的 **Artifacts** 下载 `noctf-deploy-完整提交SHA`。GitHub 下载 artifact 可能需要登录；也可以请发布人员转交这次运行的原始产物。

先解压 GitHub 提供的外层 ZIP，其中包含：

```text
noctf-deploy-完整提交SHA.tar.gz
noctf-deploy-完整提交SHA.tar.gz.sha256
```

将这两个文件传到目标 Linux 服务器，例如 `/tmp/noctf-download`。不要下载 GitHub 的应用源码 ZIP 来替代部署配置包。包内仅有当前发布的 `deploy/` 模板、脚本、共享资产和 `deployment.json`，不包含 backend 源码和真实安装密钥。

首次启用配置包发布前的旧 Action 不会有该 artifact；选择支持配置包的新成功运行，不猜测不存在的 Release 下载 URL。Artifact 有保留期限，正式上线后自行保存该包及对应镜像引用。

## 3. 校验并解压配置包

将下列文件名中的 `YOUR_COMMIT_SHA` 替换为刚下载的完整提交 SHA，在服务器上执行：

```bash
cd /tmp/noctf-download
sha256sum -c noctf-deploy-YOUR_COMMIT_SHA.tar.gz.sha256
sudo install -d -m 0755 /opt/noctf-release
sudo tar -xzf noctf-deploy-YOUR_COMMIT_SHA.tar.gz -C /opt/noctf-release
```

校验应显示 `OK`。`/opt/noctf-release` 只放发布配置，首次使用空目录；升级时将新包解压到新版本目录，保留旧包，避免混合不同版本的脚本。

解压后的布局：

```text
/opt/noctf-release/
  deployment.json       # revision、version、image、images
  deploy/
    configure.sh
    docker/
    k8s/
    shared/
```

配置向导、Compose 模板和代理示例都从这个目录读取。真正的配置与持久数据仍写入独立的 `/opt/noctf`。

## 4. 读取并拉取完整镜像

`deployment.json` 的 `image` 是默认 GHCR 不可变地址，`images` 包含本次发布的全部仓库地址：

```bash
python3 -c 'import json; print(json.load(open("/opt/noctf-release/deployment.json"))["image"])'
docker context ls
docker --context YOUR_CONTEXT pull ghcr.io/d1no209/noctf@sha256:YOUR_IMAGE_DIGEST
```

将上一步读取的完整地址原样用于 `pull` 和向导“统一 Host 镜像”。`YOUR_IMAGE_DIGEST` 是镜像 digest，不是 Git 提交 SHA，不能混用。

公开 package 可匿名 pull；需要认证时使用拥有该镜像读取权限的 Registry 账号，在目标 Docker context 对应的宿主执行 `docker login ghcr.io`。不要将 GitHub Actions 内部 `GITHUB_TOKEN` 复制到服务器。

然后继续 [Docker 单机安装](./docker.md)或 [Kubernetes 安装](./kubernetes.md)。
