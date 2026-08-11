# API 通用规范

## 路由与版本

- 公共/管理 API：`/api/v1/...`。
- 内部回调：`/api/internal/v1/...`。
- 不保留旧 `/api/...` 别名。
- OperationId 显式稳定命名；OpenAPI 只发布 v1。

## FastEndpoints StronglyTyped

每个 Endpoint：

- 继承 `Endpoint<TRequest,TResponse>` 或 `EndpointWithoutRequest<TResponse>`；
- 实现并返回 `ExecuteAsync`；禁止业务 Endpoint 使用 HandleAsync/Send*/WriteAsJsonAsync；
- 多结果声明 `Results<T1,...>`，用 `TypedResults` 创建；
- route、auth、rate limit、OpenAPI 在 Configure；业务规则放 Application；
- Request/Response/Validator 与 Endpoint 同文件；复用 Model 放在最接近概念所有权的 Endpoint 文件，不建公共 Models 文件；
- CancellationToken 传到 EF、Redis、对象存储和消息调用；
- Domain Entity 不直接序列化。

实现前核对仓库 FastEndpoints 8.2.0 和官方文档：[FastEndpoints documentation](https://fast-endpoints.com/docs/get-started)。

## ProblemDetails

所有错误使用 RFC 9457 `application/problem+json`：type、title、status、code、traceId、errors。code 在应用内是 enum/value object，只在协议边界为文本。

| 状态 | 用途 |
|---|---|
| 400 | DTO/字段/cursor/占位符语法错误 |
| 401 | 未认证 |
| 403 | 已认证但无权限 |
| 404 | 不存在或对调用者不可见 |
| 409 | revision、状态、额度、并发冲突 |
| 422 | 请求形状正确但上传内容不可处理，如无效 tar.gz |
| 429 | 分布式限流，带 Retry-After |
| 500 | 未知平台错误；不返回异常细节 |
| 503 | 已知依赖/投影暂不可用；可返回 Retry-After，不伪装为业务 4xx |

## 分页

公共 `Pagination` 功能小工具提供无业务字段的 `KeysetPageRequest`、`KeysetPage<T>` 和签名基础设施；这是“model 就近放 Endpoint”规则的唯一例外。每个 Endpoint 仍在自己的文件定义筛选 DTO 与 cursor payload，并调用公共 codec。

- limit 默认 50，范围 1..200；
- cursor 为签名 Base64url，绑定 Endpoint、排序、筛选；
- items、nextCursor、hasMore；默认不计算 total；
- 无效/跨筛选 cursor 返回 400；
- 排行榜 cursor 不绑定 revision，每页使用最新快照，响应携带 revision。

## 上传与下载

- Multipart Endpoint 用 DTO `IFormFile` 与 AllowFileUploads；不得直接读 HttpContext.Request.Body。
- 上传按用途执行显式业务上限；超过上限返回带稳定失败码的 413，且不得进入对象存储或创建业务记录。Kestrel/multipart 的全局上限仍保持开放；各上传 Endpoint 同时设置“文件上限 + 64 KiB multipart 开销”的传输硬上限，防止在绑定前无限落盘。单项 Flag 字节数、AWD 批次和 Runner 解包安全限制分别执行各自的强类型边界。
- 用户 FileName/MIME 不可信，ObjectKey 由服务端生成；流和 CancellationToken 正确传递。
- Patch 上传与触发是两个 API；不是预签名上传会话。
- 下载先授权；S3 返回 typed `RedirectHttpResult`（60 秒预签名 302），LocalFileSystem 返回 typed `FileStreamHttpResult`。二者必须出现在 Endpoint `Results<...>` 中，不以 SendStreamAsync/HttpResponse 手写。

## Revision 并发

CompetitionChallenge 聚合 update body 与 delete/restore lifecycle 请求必须带
expectedRevision；delete/restore 使用 required query 标量，成功后同样递增聚合 Revision。
陈旧 revision 与生命周期状态冲突返回强类型 409，不自动合并。Competition 普通元数据更新
使用当前状态作为并发栅栏，不引入通用 Revision。Competition 与 CompetitionChallenge 的模式
配置更新分别携带对应配置的 expectedRevision；数据库条件更新成功后对应 Revision+1。
Competition 权限数组使用独立 PermissionRevision，全量替换携带 expectedPermissionRevision，
Owner transfer 同样递增该 revision；它不得复用 ConfigurationRevision 或扩展成通用
Competition Revision。Attachment、Flag、Hint 没有独立 Revision；GitOps 通过稳定 UUID、
内容和删除状态收敛，子资源 Endpoint 不要求伪造一套 ExpectedRevision 协议。Hint 写仍属于
CompetitionChallenge 聚合变更：必须与管理写共享 Competition transaction lock，并原子递增
父聚合 Revision；排行榜只使用 `LeaderboardDirty`，不暴露 revision。

## 限流

Redis 分布式策略：认证按 IP；普通 API 按 User；Flag 按 Team+题；AWD 批量按一次 HTTP 请求；Patch 上传/触发分开；Runtime 按 Team+题；管理批量按 Actor+Competition。429 不写 GameplayFact、不引用 PatchUpload、不写事件。

## 幂等

不支持公开 Idempotency-Key。每个通过接入的 GameplayFact 都是新尝试；Patch 上传替换未引用对象；Runtime 由状态机处理重复；配置由 Revision 处理。GameplayFact 内部消息只携带事实 ID，最终结果覆盖同一行；Runtime 自身仍用 ProcessingVersion/Sequence 防迟到写回。

## OpenAPI 验收

每个 Endpoint 必须有 Summary、Description、Tag、稳定 OperationId、Accepts/Produces、认证信息、所有 Typed Result 与字段 Description。CI 生成 OpenAPI 并检查 drift；集成测试覆盖序列化、授权、Validation 与每个结果分支。
