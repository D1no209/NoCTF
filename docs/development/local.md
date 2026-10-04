# 本地开发与源码构建

本页仅供修改平台源码的开发者使用。平台用户从 [发布镜像与部署配置包](../installation/images.md)安装；Windows 完整消息与网络验证使用 [kind](../installation/kubernetes.md#本地-kind-验证环境)。

## 1. 安装开发工具

准备 Git、Docker、.NET SDK、Bun 和 Node.js。仓库 `global.json` / `backend/global.json` 是 SDK 选择依据；当前基线为 .NET 10。本地检查：

```sh
dotnet --info
bun --version
node --version
docker version
```

Bun 的安装步骤以 [官方安装说明](https://bun.sh/docs/installation)为准。Linux/macOS 可执行官方安装器，Windows 在 PowerShell 中执行：

```powershell
powershell -c "irm bun.sh/install.ps1 | iex"
```

安装后重新打开终端确认 `bun --version`。Node.js 用于 Nuxt/Vite 工具链；建议使用满足 ClientApp 当前依赖要求的受支持 LTS，不将前端工具链版本与平台 .NET Runtime 混为一谈。

## 2. 获取源码与准备依赖

```sh
git clone https://github.com/D1no209/NoCTF.git
cd NoCTF
dotnet restore backend/NoCTF.slnx
```

本地调试需要 PostgreSQL 和 Redis。下面创建隔离开发依赖；示例密码只用于本机回环环境，不能用于生产：

```sh
docker run -d --name noctf-dev-postgres -p 127.0.0.1:5432:5432 -e POSTGRES_DB=noctf -e POSTGRES_USER=postgres -e POSTGRES_PASSWORD=postgres -v noctf-dev-postgres:/var/lib/postgresql/data postgres:16.14
docker run -d --name noctf-dev-redis -p 127.0.0.1:6379:6379 redis:7.4.9
```

已有同名容器/端口时复用已验证的依赖或更改连接串，不重复覆盖。生产依赖使用部署锁定 digest，本页标签仅方便隔离开发。

## 3. 安装并启动前端

从仓库根进入 ClientApp：

```sh
cd backend/src/NoCTF.API/ClientApp
bun install --frozen-lockfile
bun run dev
```

默认前端地址 `http://127.0.0.1:3000`，API dev proxy 连接后端 5080。前端开发和修改须遵循 `ClientApp/AGENTS.md`；本手册文档站独立安装在 `docs`，两者不是同一项目。

## 4. 启动唯一 Host

另开 PowerShell 终端，从仓库根启动：

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://localhost:5080'
dotnet run --project backend/src/NoCTF.Host/NoCTF.Host.csproj --no-launch-profile
```

Host 会读取 API 项目的 `appsettings.json` 与 `appsettings.Development.json`，开发启动自动应用迁移。前端打开 3000，后端 API 在 5080。不要对作为 class library 的 NoCTF.API/Worker/Runner 执行 `dotnet run`。

当前开发 seed 用户名为 `dev-admin`，密码为 `dev-admin-change-me`。它们仅是隔离开发默认值，已有管理员不会被 seed 重置。进入正式环境必须替换为自己的管理员与密钥。

开发 HTTP 下的 Secure Refresh Cookie 不能视为生产会话验收。需要验证刷新行为时使用 HTTPS 开发入口，或仅在隔离本机环境通过配置调整 cookie secure 设置；公开环境保持 Secure。前端新增端口时同步添加精确 Refresh Origin。

Development 会 stub 外部 Wolverine transports。验证 NATS 持久投递、资源域租约、恢复和完整题目生命周期时，使用 Production 配置的隔离 Docker/kind 部署，不把源码 dev server 作为证据。

## 5. 生成前后端契约

修改 API 后从仓库根导出 OpenAPI，再生成前端 SDK：

```powershell
dotnet run --project backend/src/NoCTF.Host/NoCTF.Host.csproj -- --export-openapi
Push-Location backend/src/NoCTF.API/ClientApp
bun run api:gen
Pop-Location
```

Handler 签名/依赖变化还需按仓库规范重新生成 Wolverine 静态 Handler。不要手工编辑 EF 迁移、快照或生成的 API SDK。

## 6. 构建完整镜像

Dockerfile 从仓库根取 build context：

```sh
docker build -f backend/Dockerfile --target host -t noctf-host:local .
```

构建包含 Bun 前端静态产物与 Host，不需要在运行镜像中再启动 Node/Bun。要对正式环境发布，走仓库 CI 镜像发布和 digest 审核流程；本机 `:local` 用于验证。

前端修改后执行它自己的 architecture audit、typecheck 和相应测试。仅修改 `docs` 时运行 [文档构建](../contributing.md)，不需要触发平台数据库迁移或部署。
