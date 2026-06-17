# NoCTF 部署与测试指南

## 当前环境状态

| 服务 | 地址 | 状态 |
|------|------|------|
| 后端 API | http://localhost:5000 | 运行中 |
| 前端 (Vite Dev) | http://localhost:5173 | 运行中 |
| PostgreSQL | localhost:5432 | Docker 运行中 |
| Redis | localhost:6379 | Docker 运行中 |
| MinIO | - | 未启动（可选） |

---

## 方式一：本地开发模式（当前已就绪）

由于当前环境的 Docker 无法拉取 `node`/`nginx` 镜像，我们采用本地开发模式运行。此模式功能完整，可直接体验所有特性。

### 已启动的服务

- **后端**：`dotnet run --urls http://localhost:5000`
- **前端**：`pnpm dev` → http://localhost:5173

### 如何登录并测试

#### 1. 打开浏览器访问前端
```
http://localhost:5173
```

#### 2. 注册账号
- 点击 **Register**
- 填写邮箱、用户名、密码（至少 8 位）
- 注册后系统会自动分配 **User** 角色

#### 3. 创建 Admin / Organizer 账号（用于管理后台）

目前前端注册只能创建普通用户。要体验管理后台，需要通过 API 直接注册一个管理员账号：

```bash
curl -X POST http://localhost:5000/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@noctf.local","userName":"admin","password":"Admin123!","role":"Admin"}'
```

> 如果注册端点不支持带 `role` 的注册，可直接用刚刚注册的第一个账号登录，然后通过数据库修改角色，或者通过 Swagger 调用管理员相关的 API。

#### 4. 登录
- 使用邮箱 + 密码登录
- 登录后会自动跳转到 **赛事大厅** (`/competitions`)

#### 5. 创建赛事并添加题目（Admin）

以 `admin@noctf.local` / `Admin123!` 登录后：

1. 进入 **管理后台**：http://localhost:5173/admin/competitions
2. 点击 **Create**，填写赛事名称、赛制类型（CTF / AWD / AWDP / KoH）、起止时间
3. 进入 **/admin/challenges**，选择对应赛事，创建题目
4. 进入 **/admin/users** 或 **/admin/teams**，管理参赛队伍

#### 6. 参赛测试（User）

1. 用普通用户账号登录
2. 进入赛事大厅，选择刚创建的赛事
3. 如果是 **CTF**：
   - 进入题目列表 → 点击题目 → 输入 Flag 提交
   - 查看实时排行榜 (`/scoreboard`)
4. 如果是 **AWD**：
   - 进入 AWD Dashboard，查看轮次计时器、服务状态、攻击日志
5. 如果是 **KoH**：
   - 进入 KoH Dashboard，查看 Agent 轮询状态和占领计时

#### 7. 测试 Admin 实时日志流

1. 以 Admin 登录
2. 访问 http://localhost:5173/admin/logs
3. 页面会通过 SignalR 实时接收后端日志（如提交记录、容器事件等）

---

## 方式二：Docker Compose 一键部署（推荐用于生产/演示）

在你的机器（Docker Hub 可访问）上执行：

### 1. 准备环境变量

```bash
cd /path/to/NoCTF
cp .env.example .env
```

编辑 `.env`，确保设置强密码：
```env
POSTGRES_PASSWORD=YourStrongPostgresPassword
JWT_SECRET=YourSuperSecretKeyAtLeast32CharsLong!!!
MINIO_ROOT_PASSWORD=YourStrongMinioPassword
```

### 2. 启动全部服务

```bash
cd deploy
docker compose up --build -d
```

这会自动构建并启动：
- `postgres:16`
- `redis:7`
- `minio/minio`（对象存储）
- `backend`（.NET 8，端口 8080）
- `frontend`（Nginx，端口 80）

### 3. 验证启动状态

```bash
# 查看容器状态
docker compose ps

# 测试 API 健康检查
curl http://localhost:8080/api/health

# 测试前端
curl http://localhost/
```

### 4. 访问应用

```
前端首页：http://localhost
Swagger API 文档：http://localhost:8080/swagger
MinIO 控制台：http://localhost:9001
```

---

## 方式三：本地开发启动（开发者）

如果你需要修改代码并调试：

### 启动后端
```bash
cd backend/src/NoCTF.API
dotnet run --urls "http://localhost:5000"
```

### 启动前端
```bash
cd frontend
pnpm install
pnpm dev
```

浏览器打开 http://localhost:5173 即可。

> 由于 `spa.proxy.json` 已配置，也可以只启动后端，`dotnet run` 会自动代理前端请求到 `localhost:5173`（需先启动前端 dev server）。

---

## 测试账号快速创建

如果你不想通过前端注册，可以直接 curl 创建测试账号：

### 创建管理员
```bash
curl -X POST http://localhost:5000/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@noctf.local","userName":"admin","password":"Admin123!"}'
```

然后登录获取 JWT：
```bash
curl -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@noctf.local","password":"Admin123!"}'
```

返回的 `accessToken` 可用于后续 API 调用：
```bash
curl -H "Authorization: Bearer <accessToken>" http://localhost:5000/api/admin/users
```

---

## 常见问题

### Q: Docker Compose 构建时拉取 node/nginx 失败？
A: 说明你当前环境的 Docker Hub 访问受限。可尝试：
1. 配置 Docker 镜像加速器（如阿里云、DaoCloud）
2. 或先手动下载镜像：`docker pull node:20-slim` / `docker pull nginx:alpine`
3. 或改用本地开发模式运行

### Q: 后端启动报错 "Unable to resolve IContainerManager"？
A: 已在最新代码中修复。`DockerProvider` 和 `DockerManager` 会在 `Program.cs` 中正确注册。

### Q: 如何切换语言？
A: 页面右上角（登录页/导航栏）有一个下拉框，可切换 **EN / 中文**。

### Q: SignalR 实时更新不工作？
A: 确保后端和前端的域名/端口一致，且浏览器控制台没有 CORS 错误。Docker Compose 模式下 Nginx 已配置 WebSocket 反向代理。

---

## 相关文档

- [项目 README](../README.md)
- [架构设计](../docs/architecture.md)
- [部署指南](../docs/deployment.md)
- [开发指南](../docs/development.md)
- [赛制规则](../docs/game-modes.md)
- [API 参考](../docs/api.md)
