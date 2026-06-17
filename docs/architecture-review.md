# NoCTF 架构审查与优化建议

本文基于当前项目架构分析和 @oracle 架构审查结果整理，重点覆盖多租户、权限、插件隔离、容器安全、后台任务、数据一致性、部署与可维护性。

## 总体结论

当前项目方向正确，整体架构具备清晰的前后端分离、后端分层、插件化比赛模式、实时通信和容器编排能力。

但当前实现更接近 MVP。真正用于公网或真实比赛前，需要优先补齐以下基础工程护栏：

1. 租户与权限模型
2. Docker / Runner 安全边界
3. 后台任务生命周期
4. 计分与提交并发一致性
5. 生产密钥和默认账号安全
6. 插件加载与插件权限边界

如果只优先做三件事：

1. **修租户和权限模型**：当前 Header 租户、`IgnoreQueryFilters()`、Organizer 全局权限组合风险较高。
2. **移除 API 对 Docker socket 的直接控制**：这是最大的安全边界问题。至少先加资源限制和容器安全参数。
3. **替换 fire-and-forget 后台任务**：AWDP Patch 验证当前不可靠，线上容易丢任务、卡任务、并发错乱。

---

## 1. 高优先级问题与风险

### 1.1 多租户中间件顺序疑似错误

当前 `TenantResolutionMiddleware` 注册在 `UseFastEndpoints()` 之后，可能导致 Endpoint 执行时还没有设置租户上下文。

影响：

- EF Core 全局租户过滤不可靠
- 查询可能返回空
- 开发者被迫大量使用 `IgnoreQueryFilters()`
- 租户隔离从自动机制退化成手动约束

建议：

- 把 `TenantResolutionMiddleware` 放到 `UseAuthentication()` / `UseAuthorization()` 之后，`UseFastEndpoints()` 之前
- 所有比赛资源 Endpoint 统一走 Competition Scope / PreProcessor
- 限制业务代码直接使用 `IgnoreQueryFilters()`

---

### 1.2 `X-Competition-Id` 可被客户端伪造

`TenantResolutionMiddleware` 允许从 Header 中读取：

```text
X-Competition-Id
```

这意味着客户端可以尝试切换租户上下文。

风险：

- 横向越权
- Organizer 或普通用户访问其他比赛数据
- 如果某些 Endpoint 没有重复校验权限，问题会更严重

建议：

- Header 不能作为可信租户来源
- 租户应来自 route 参数、JWT scope 或服务端授权后的上下文
- 即使保留 Header，也必须验证用户对该 Competition 有权限

---

### 1.3 Organizer 权限过宽

不少 Admin Endpoint 只检查：

```csharp
Roles("Admin", "Organizer")
```

但没有绑定具体比赛权限。

风险：

- Organizer 可能管理任意比赛
- 多比赛之间权限隔离失效
- `CompetitionPermissionService` 和 `CompetitionManagePreProcessor` 没有被一致使用

建议：

- 系统级后台：只允许 `Admin`
- 比赛级后台：必须带 `competitionId`，并检查 Owner / Manager / Organizer scope
- 所有比赛写操作统一使用 `CanManageCompetitionAsync`

---

### 1.4 插件系统缺少安全边界

当前插件通过 DLL 加载，并可以执行：

```csharp
module.ConfigureServices(services)
```

这等于给插件完整的应用内代码执行能力。

风险：

- 插件可读取配置、数据库、JWT Secret
- 插件可覆盖服务注册
- 插件异常可能拖垮主应用
- 插件依赖冲突和不可卸载问题后续会变复杂

短期建议：

- 插件目录只读
- 插件 hash / 签名 allowlist
- 记录插件名称、版本、程序集信息
- 禁止第三方动态上传插件

中长期建议：

- 用受限注册 API 替代裸 `IServiceCollection`
- 增加 Plugin Manifest
- 不可信任务迁移到独立 Worker / Runner

---

### 1.5 Docker socket 直挂风险极高

部署中后端挂载了：

```yaml
/var/run/docker.sock:/var/run/docker.sock
```

这几乎等同于给后端容器宿主机 root 权限。

同时容器创建缺少安全限制：

- 没有 Memory 限制
- 没有 CPU 限制
- 没有 PIDs 限制
- 没有 `CapDrop`
- 没有 `ReadonlyRootfs`
- 没有 `no-new-privileges`
- 没有镜像来源白名单
- 没有网络隔离策略

建议：

- API 不应直接挂 Docker socket
- 拆出独立 Runner
- Runner 放在隔离节点
- challenge / checker / patch 容器默认资源限制和安全策略
- 禁止容器访问 Postgres / Redis / MinIO / backend 内网

---

### 1.6 AWDP Patch 后台任务生命周期错误

`SubmitPatchEndpoint` 使用了类似：

```csharp
_ = Task.Run(async () =>
{
    await patchService.ValidatePatchAsync(submissionId);
});
```

问题：

- 请求结束后 scoped service / DbContext 可能已释放
- 异常容易丢失
- 服务重启后 Pending 任务会卡住
- 多副本部署没有幂等、锁、重试
- 无法优雅停机

建议：

- 改为 `BackgroundService + Channel`
- 或使用 Hangfire / Quartz / 消息队列
- 后台任务用 `IServiceScopeFactory` 创建 scope
- Patch 状态持久化为 Pending / Running / Retry / Failed / Verified
- 启动时恢复未完成任务

---

### 1.7 Flag 提交与动态计分存在并发竞态

当前 CTF 流程大致是：

```text
查是否已解出
添加 submission
添加 score event
保存
重新计算动态分数
```

问题：

- 并发提交时可能重复得分
- 动态分数修正不是事务化的
- `SaveChanges` 和 `RecalculateDynamicScoresAsync` 分阶段执行，中途失败会导致不一致

建议：

- 增加数据库唯一约束：`CompetitionId + TeamId + ChallengeId` 正确提交只能有一次
- 正确提交、计分、动态分数修正放在事务内
- 长期把计分改为事件事实表 + Projection 模型

---

### 1.8 默认管理员密码是生产级风险

Seeder 中存在默认账号：

```text
admin@noctf.local
Admin@123456
```

风险：

- 部署者忘记改，直接暴露管理员
- 生产环境不可接受
- 如果已有同邮箱用户，还可能被自动提升为 Admin

建议：

- 生产环境禁止硬编码默认密码
- 首次启动要求通过环境变量配置 bootstrap admin
- 如果 Production 未配置 bootstrap admin，应启动失败
- 不要自动提升已有用户为 Admin

---

## 2. 中优先级技术债

### 2.1 `IgnoreQueryFilters()` 使用过多

这会削弱多租户隔离的可信度。

建议：

- 包装成受控 query service
- 所有 Ignore 必须显式带 `competitionId`
- 增加测试证明 Competition A 不能访问 Competition B

---

### 2.2 Core 领域模型开始变宽

`Competition` 和 `Challenge` 中包含大量不同模式的 nullable 字段，例如 AWD、AWDP、KoH 配置混在一起。

短期可以接受，但长期会变成：

```text
宽表 + nullable 地狱 + 模式耦合
```

建议：

- 稳定核心实体保留在 Core
- 模式专属配置迁移到 JSONB value object 或独立配置表
- 插件 owns 自己的 config schema

---

### 2.3 插件契约太粗

当前插件只暴露 `ConfigureServices(IServiceCollection)`，缺少：

- 插件名称
- 版本
- API 兼容版本
- 支持的 GameMode
- 配置 schema
- 资源需求
- 健康检查
- 权限需求

建议增加 `PluginManifest`。

---

### 2.4 Docker Command 使用字符串拆分不可靠

如果现在有类似：

```csharp
Command.Split(' ')
```

复杂命令、引号、shell 参数都会出问题。

建议：

- `Command` 改为 `string[] Cmd`
- 显式区分 `Entrypoint` 和 `Cmd`

---

### 2.5 Container TTL 没有真正强制执行

`Ttl` 只记录了预期停止时间，但没有强制超时 kill。

建议：

- `RunContainerAsync` 使用 `CancellationTokenSource.CancelAfter(Ttl)`
- 超时后 force remove
- 记录 timeout exit reason

---

### 2.6 前端 token 存 localStorage

前端认证状态使用 localStorage 保存 token。

风险：

- 一旦有 XSS，token 可直接被窃取

短期建议：

- CSP
- 禁止危险 HTML
- Admin token 短 TTL

中期建议：

- HttpOnly SameSite Cookie
- CSRF 防护

---

### 2.7 OpenAPI client 可能漂移

前端 OpenAPI 配置依赖：

```text
http://localhost:5000/swagger/v1/swagger.json
```

风险：

- CI 环境不稳定
- generated client 可能过期
- 前后端契约漂移

建议：

- 后端 build 输出 swagger artifact
- 前端从 artifact 生成 client
- CI 检查生成代码是否最新

---

## 3. 推荐架构优化方向

### 3.1 拆出 API / Worker / Runner 三个边界

当前 API 承担太多职责：

- 用户请求
- 游戏模式逻辑
- 后台 Patch 验证
- Docker 编排
- 实时通知
- 监控

建议逐步拆成：

```text
NoCTF.API
  认证、授权、管理、查询、提交入口

NoCTF.Worker / GameOrchestrator
  AWD round
  KoH polling
  AWDP patch validation
  leaderboard projection
  容器 GC

NoCTF.Runner
  唯一接触 Docker / K8s 的组件
  部署在隔离节点
```

收益：

- API 不再拿 Docker socket
- 后台任务可恢复、可重试
- 多副本部署更清晰
- 安全边界更明确

---

### 3.2 建立 CompetitionScope

建议把租户和权限合并为统一入口：

```text
Route competitionId
   │
   ▼
CompetitionScope
   ├── 校验比赛存在
   ├── 校验用户权限
   ├── 设置 TenantContext
   └── 提供强类型 CompetitionId
```

之后 service / repository 方法尽量显式接收 `competitionId`。

---

### 3.3 计分改成事件 + Projection

推荐模型：

```text
Submission / Attack / PatchVerified / KohControlChanged
        │
        ▼
事实事件表
        │
        ▼
ScoreEvent / Leaderboard Projection
        │
        ▼
Redis Cache / SignalR Push
```

这样排行榜可以重建，分数漂移也能修复。

---

### 3.4 收敛插件能力

目前不要急着做“全能力第三方插件平台”。

更稳妥的做法：

- 官方模式 CTF / AWD / AWDP / KoH 可以先作为内部项目
- 插件机制只开放小型扩展点：
  - ChallengeType
  - Scoring strategy
  - Checker adapter
- 第三方插件等 Manifest、签名、版本和权限模型成熟后再开放

---

## 4. 安全性改进建议

### 4.1 认证与授权

- 修复 JWT role claim 映射：`RoleClaimType = "role"` 或使用 `ClaimTypes.Role`
- JWT Secret 启动强校验
- Admin 操作使用短 TTL，可选二次确认
- 登录 / 注册 / 提交 flag / patch 增加 rate limiting
- 所有 Organizer 权限必须绑定 competition scope
- 禁止 Header 切换租户绕过权限

---

### 4.2 密钥与敏感数据

- 移除硬编码默认管理员密码
- `FlagSecret` 默认不应在 Admin DTO 列表中返回
- 需要单独 “reveal secret” API，并记录审计日志
- 静态 flag 可存 hash / HMAC，而不是明文
- `Submission.FlagContent` 可考虑只保存摘要或脱敏保存错误提交
- K8s secret 使用 ExternalSecrets / SOPS / SealedSecrets

---

### 4.3 容器沙箱

最低要求：

- API 不挂 Docker socket
- Runner 单独节点
- 所有挑战容器非 root
- 默认无内网访问
- 阻断访问 Postgres / Redis / MinIO / backend service
- 资源限制强制执行
- 容器退出 / 超时清理有兜底 GC
- 镜像必须来自可信 registry，最好用 digest

---

### 4.4 文件上传与 Patch 安全

建议：

- 限制上传大小
- 验证 tar.gz 结构
- 防止 tar path traversal：
  - `../`
  - absolute path
  - symlink / hardlink
- Patch URL 不应长期公开
- 使用短期签名 URL
- Patch 解包应在隔离容器和只读基础层中执行

---

### 4.5 CORS / SignalR

建议：

- CORS origin 从配置读取，生产禁止 localhost 默认
- SignalR query string token 可能进入日志或代理记录，需要降低日志暴露
- MonitorHub / Admin logs 需要严格 Admin / Organizer scope 授权

---

## 5. 可维护性、测试与 CI 改进建议

### 5.1 增加关键集成测试

建议增加：

1. **租户隔离测试**
   - Competition A 用户不能读 / 写 Competition B
   - Header 伪造不能切换
   - `IgnoreQueryFilters` 包装方法必须带 competitionId

2. **权限测试**
   - Organizer 是否只能管理被授权比赛
   - Collaborator Manager / Viewer 行为

3. **并发提交测试**
   - 同队伍并发正确提交只得一次分
   - 动态分数结果稳定

4. **后台任务测试**
   - Patch validation 进程重启后的恢复
   - 重复消费幂等

5. **容器策略测试**
   - 创建容器必须带资源限制、安全参数

6. **OpenAPI contract 测试**
   - 后端 swagger 与前端 generated client 一致

---

### 5.2 建立最小 CI

建议最小 CI 包括：

后端：

- `dotnet restore`
- `dotnet build`
- `dotnet test`
- format / analyzer

前端：

- install dependencies
- typecheck
- lint
- build

契约：

- 生成 swagger
- 生成 client
- 检查 diff

安全：

- dependency scan
- secret scan
- container image scan

---

### 5.3 审计与日志

建议：

- 不要记录 flag、JWT、password、patch URL
- Admin reveal flag、role change、container destroy、competition delete 必须审计
- 后台 Worker 操作也要审计
- 日志流不要向 Organizer 泄露其他比赛或系统 secret

---

### 5.4 降低复杂度 / YAGNI

建议暂缓：

- 第三方热插拔插件生态
- 多云容器 Provider 抽象过度扩展
- 复杂前端状态库叠加

优先做：

- 一个可靠 Docker / K8s Runner
- 一个清晰 CompetitionScope 权限模型
- 一个可重放计分系统
- 一个稳定 OpenAPI 契约链路

---

## 6. 短期 1-2 周可落地行动清单

按优先级：

1. 调整 `TenantResolutionMiddleware` 顺序。
2. 禁止直接信任 `X-Competition-Id`。
3. 所有比赛管理 Endpoint 改为带 `competitionId` 并检查比赛级权限。
4. Organizer 不再拥有全局管理权限。
5. 移除生产默认 admin 密码。
6. JWT Secret 启动强校验，并显式设置 RoleClaimType。
7. CORS origin 改为配置项。
8. Admin DTO 默认不返回 `FlagSecret`。
9. 替换 `Task.Run` fire-and-forget 后台任务。
10. Patch validation 增加 Pending / Running / Failed / Retry 状态。
11. 容器执行加 TTL 强制超时。
12. Docker HostConfig 增加 CPU / Memory / PIDs / cap drop / no-new-privileges。
13. 为正确提交增加唯一约束或事务保护。
14. 增加并发提交测试。
15. 增加租户隔离测试。

---

## 7. 中长期路线图

### 阶段 A：可靠性基线

目标：先防越权和配置事故。

- CompetitionScope 权限模型
- 清理 `IgnoreQueryFilters()`
- CI
- OpenAPI contract check
- 安全配置启动校验
- 审计规范

---

### 阶段 B：Worker / Runner 架构

目标：把比赛运行逻辑从 API 生命周期中解耦。

- 新增 Worker
- 新增 Runner
- API 不直接访问 Docker socket
- 后台任务可恢复、可重试
- Runner 节点网络隔离

---

### 阶段 C：计分稳定化

目标：长时间比赛和高并发下分数可解释、可修复。

- 事实事件表
- Leaderboard projection
- Redis 可重建
- 提供 rebuild leaderboard 管理命令
- 并发和幂等测试

---

### 阶段 D：插件系统安全化

目标：插件可维护、可升级、不会破坏主应用边界。

- Plugin Manifest
- API version
- 插件签名 / hash allowlist
- 受限注册 API
- 插件加载失败诊断

---

### 阶段 E：生产级部署

目标：可公网承载真实比赛。

- K8s `securityContext`
- ExternalSecrets / SOPS / SealedSecrets
- NetworkPolicy 隔离 challenge containers
- 镜像扫描
- SBOM
- 依赖漏洞扫描
- 备份恢复演练
- metrics / traces / structured logs
