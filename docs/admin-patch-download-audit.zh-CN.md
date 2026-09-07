# 工作人员下载 Fix Patch：实现与验收

日期：2026-09-08。本次仅本地实现、测试及提交，不推送、不部署。

## 入口与权限

沿用“竞赛管理 → 提交记录”，没有新建页面。Fix 记录提供“下载 Patch”，原有详情侧栏显示文件名、大小、上传时间和 SHA-256。

下载及元数据访问均由 Application 用例调用当前数据库权限判断 `CanJudgeAsync`：允许平台管理员，以及本场 Owner、Manager、Judge；Observer、其他比赛的负责人、普通选手不允许使用此管理接口。没有新增普通选手自助下载接口。既有比赛软删除权限规则保持不变。

前端显式匹配允许的角色，但不是授权依据。每次下载重新检查后端权限，页面打开后被撤销 Manager 权限也不能继续下载。

## 接口与文件关联

新增 `GET /api/v1/admin/competitions/{competitionId}/gameplay-facts/{gameplayFactId}/patch`，使用 Bearer、强类型 FastEndpoints 请求和 `Results<FileStreamHttpResult, ProblemHttpResult>`。

依次校验：

1. 当前用户拥有本场下载权限。
2. GameplayFact 属于 URL 指定的比赛，且 Kind 为 FixAttempt。
3. ReferenceKind / ReferenceId 指向存在的 PatchUpload。
4. PatchUpload 的比赛、题目、队伍、提交人与该事实一致，题目和队伍本身也确实归属该比赛。
5. File 元数据和存储对象存在。

不按评测结果或运行环境状态限制。Pending、Processing、Completed/Correct、Completed/Wrong、Completed/Rejected、PlatformFailed 均可在满足上述条件后下载。

错误有明确的 HTTP 状态及稳定 code：Forbidden、SubmissionNotFound、NotFixSubmission、PatchNotFound、InvalidAssociation、FileNotFound、StorageUnavailable、AuditUnavailable。缺少 Patch 关联与文件对象丢失分别提示，不混成统一冲突。无效 UUID 由请求验证返回 ValidationProblemDetails。

现有提交详情返回安全的 `patch` 元数据、`patchFailure` 与 `canDownloadPatch`。后者表示权限，不保证文件在未来仍然存在；下载时会重新检查。Observer 不获得 Patch 元数据。

## 审计与传输安全

- 文件打开后、返回任何文件字节前，必须将 `GameplayFactPatchDownloaded` 写入不可变 competition_events。平台管理员没有豁免。
- 记录操作者、比赛、队伍、提交、文件 ID 和服务端时间；不记录包内容、Flag、对象路径或令牌。
- 此事件表示“已授权并打开文件，开始下发”，不声称客户端已接收完整文件；网络中断后的完整性仍由客户端处理。
- 审计保存失败返回 503，关闭已打开的流，不下发文件。未经授权或文件不存在的请求不会被记成一次已开始下载。
- 审计同步落库，不依赖消息队列或公开播报；现有平台审计页可查询此动作与相关 ID。既有 Flag 读取的审计规则没有改变。
- 服务器直接流式传输原始存储字节，不解压、规范化或执行。响应强制 `application/octet-stream`、`Content-Disposition: attachment`、`X-Content-Type-Options: nosniff`、`Cache-Control: private, no-store`。
- 不设置可绕过授权的永久链接或内部 JWT。Runner 内部下载接口及其令牌协议未改动。
- 附件建议文件名移除路径和控制字符，空名称使用安全默认值。前端优先使用 UTF-8 `filename*`，避免中文文件名被 ASCII fallback 替换。
- 不启用 Range / 条件缓存复用；每次完整下载请求都要重新授权和审计。

## 验证

- 后端构建 0 警告、0 错误；1092 项非 Integration 测试通过。
- 真实 PostgreSQL + 本地文件存储 + 实际 FastEndpoints HTTP 管线回归通过，覆盖 7 种角色、未登录、权限撤销、跨比赛路由替换、6 种伪造关联、非 Fix、缺失关联、缺失对象、各评测状态、存储故障和审计故障。
- HTTP 验证原始字节逐字节一致、中文文件名、附件响应、禁止缓存、nosniff，以及管理员/Owner/Manager/Judge 的审计均存在；错误响应不包含私有路径或包内容。
- 审计故障验证已打开文件流被关闭；元数据查询和失败请求没有虚假的下载成功审计。
- OpenAPI 声明二进制响应及错误响应，API 路由文档与生成 SDK 已同步。没有修改生成迁移文件。
- 前端 392 项测试、类型检查及静态构建通过；本地模拟 API 浏览器验收确认 Judge 的入口、加载状态、具体错误与元数据，以及 Observer 无入口、无 Patch 元数据。

未访问生产服务器、未运行任何选手 Patch。没有 Migration、配置变更、数据卷/网络变更，也没有新增在线预览或解压功能。
