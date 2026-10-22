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

实现前核对 `backend/Directory.Packages.props` 中的 FastEndpoints 版本及其
[官方文档](https://fast-endpoints.com/docs/get-started)，不要按历史文档中的固定版本假定 API 可用。

## ProblemDetails

ProblemDetails 错误使用 RFC 9457 `application/problem+json`，按具体错误提供 type、title、status、
code、traceId、errors 及本地化资源信息。code 在应用内是 enum/value object，只在协议边界为文本。
Endpoint 声明的 `NotFound`、`UnauthorizedHttpResult`、`ForbidHttpResult` 可以没有正文；
成员加入等操作也可以返回自己的强类型 `Conflict<TFailure>`。调用方应按实际 result union 和
Content-Type 解析，空正文按 HTTP 状态提供反馈，不能假设所有 4xx 都带 ProblemDetails。

| 状态 | 用途 |
|---|---|
| 400 | DTO/字段/cursor/占位符语法错误 |
| 401 | 未认证 |
| 403 | 已认证但无权限 |
| 404 | 不存在或对调用者不可见 |
| 409 | 状态、额度、业务唯一约束或其他强类型业务冲突 |
| 422 | 请求形状正确但上传内容不可处理，如无效 tar.gz |
| 429 | 分布式限流，带 Retry-After |
| 500 | 未知平台错误；不返回异常细节 |
| 503 | 已知依赖/投影暂不可用；可返回 Retry-After，不伪装为业务 4xx |

## 分页

公共 `Pagination` 功能目录集中维护 `PaginationRequest`、`SearchRequest`、`ArrayResult<T>` 和
签名游标基础设施；筛选 DTO 与游标的业务作用域仍由具体 Endpoint 定义。

- 普通列表采用 `offset/limit/desc`，公共请求的 limit 默认 10、范围 1..200，响应为 items/total；
- 历史、日志和排行榜明细按各自契约采用 cursor，默认 limit 与响应字段以 Endpoint 为准；
- cursor 是签名 Base64url，绑定 Endpoint 及该接口定义的读取范围，无效或跨范围使用返回 400；
- 排行榜格子明细的作用域还包含调用者、比赛/队伍/题目、轮次窗口、结构修订、快照版本和数据截止时间。
  投影变化后旧 cursor 可失效，调用方重新开始该明细读取，不拼接不同快照的页面。

## 上传与下载

- Multipart Endpoint 用 DTO `IFormFile` 与 AllowFileUploads；不得直接读 HttpContext.Request.Body。
- 上传按用途执行显式业务上限；超过上限返回带稳定失败码的 413，且不得进入对象存储或创建业务记录。Kestrel/multipart 的全局上限仍保持开放；各上传 Endpoint 同时设置“文件上限 + 64 KiB multipart 开销”的传输硬上限，防止在绑定前无限落盘。单项 Flag 字节数和 Runner 解包安全限制分别执行各自的强类型边界；AWD Flag 批次数量与总字节数不设上限。
- 用户 FileName/MIME 不可信，ObjectKey 由服务端生成；流和 CancellationToken 正确传递。
- AWDP 防御申请与 Patch 上传是两个 API；上传成功即原子绑定唯一 FixAttempt 并触发一次验证，不存在第三个 Fix trigger，也不是预签名上传会话。
- 下载先授权；S3 返回 typed `RedirectHttpResult`（60 秒预签名 302），LocalFileSystem 返回 typed `FileStreamHttpResult`。二者必须出现在 Endpoint `Results<...>` 中，不以 SendStreamAsync/HttpResponse 手写。

## 并发

普通聚合 PATCH 不要求通用 `ExpectedRevision` 协议；省略 section 不修改，出现的 section
按该端点约定更新。数据库使用应用生成的 `Guid ConcurrencyStamp` 乐观并发令牌，集合不变量由
唯一约束和 Serializable 事务保护；可安全重放的事务进行有界重试，外部副作用不自动重放。

LiveSolo、单题题解等明确提供 expected stamp/版本的专用接口，调用方必须传入最新详情中的
对应值；题目时间变更的短时预览凭据也必须按专用流程读取和提交。不能把普通 PATCH 的规则推广到
所有管理操作。409 的业务含义以具体稳定失败码为准。

## 限流

分布式准入和配额由 EF Core 的请求窗口、租约及关系事务维护；Redis 只供 FusionCache 使用。
认证/SSO、提交、Patch、Runtime 等入口按 `ProtectedEntryMetadata` 和当前准入配置执行各自的
IP、用户、目标或并发额度。Flag 的业务受理准入位于 Application；Runtime 释放有独立入口额度，
避免资源释放被新的启动请求堵塞。429 不表示业务事实已经受理，调用方应遵守 Retry-After。

## 幂等

标记为 FlagSubmission、PatchUpload、RuntimeCommand、RuntimeRelease、ManualAdjustment 的入口
要求非空 UUID 格式的 `Idempotency-Key` 请求头；缺失或非法返回 400 `IdempotencyKeyRequired`。
同一操作作用域、请求键和负载重放已提交结果，同键不同负载返回 409 `IdempotencyPayloadMismatch`。
新的主动提交使用新键；网络重试复用原键，避免重复产生尝试、扣分或资源操作。

理论开卷与逐题保存使用强类型请求中的 RequestId 和关系化回执；整卷提交幂等返回同一结果，
每份正式试卷只有一个 TheoryPaperSubmission 事实。Runtime 重置创建新 UUID，消息消费者仍按
Runtime 身份、状态和消息作用域处理至少一次投递；HTTP 请求回执与消息幂等是不同边界。

内部消息通过 NATS JetStream 持久投递，数据库提交后发布并从关系化待处理状态恢复；不引入数据库 Inbox 或通用 Outbox。Competition Webhook 的专用事务 Outbox 保持现有例外。

## OpenAPI 验收

每个 Endpoint 必须有 Summary、Description、Tag、稳定 OperationId、Accepts/Produces、认证信息、所有 Typed Result 与字段 Description。提交前在本地生成 OpenAPI 并检查 drift（CI 仅构建）；集成测试覆盖序列化、授权、Validation 与每个结果分支。
