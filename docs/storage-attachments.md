# 对象存储、附件与 Patch

## Provider

底层文件 I/O 直接使用 FluentStorage `IStore`，不再维护 NoCTF 自定义存储接口或 Provider adapter。首版启用 FluentStorage Disk（开发/测试/单节点）和 FluentStorage AWS S3（AWS S3、MinIO 与通用 S3-compatible）；Bucket/目录不公开。平台生成不可猜测 ObjectKey，原文件名只进入不可变 `files.file_name`。

`Storage:Provider` 只能是 `Local` 或 `S3`。Local 要求 `Storage:LocalRoot`；S3 要求 `Storage:S3:Bucket`，标准 AWS 还要求 Region，自定义 ServiceUrl 要求显式 AccessKey/SecretKey，并可配置 SessionToken 与 ForcePathStyle。`IStore` 为进程级 Singleton，由 DI 容器释放。

业务表不再保存 ObjectKey、文件名、MIME、长度或 SHA256；只保存 `FileId`，所有 FK 为 Restrict。Users/Teams Avatar、Competition Poster、Platform Logo、ChallengeAttachment 和 PatchUpload 引用 `files`。同步导出直接写响应流，不创建 File 或导出业务记录。

API 的 `Uploads` 配置按用途设置压缩前请求文件上限：`MaximumAvatarBytes`、`MaximumLogoBytes`、`MaximumPosterBytes` 默认均为 12 MiB，`MaximumAttachmentBytes` 默认 1 GiB；所有值必须位于 1 byte 至 1 GiB。超过上限返回 413/`UploadTooLarge`，且不会进入图片解析、对象存储或业务引用写入。AWDP Fix 包另由题目定义的 `MaximumPatchUploadBytes` 控制。

上传顺序固定为：受控临时文件计算长度/SHA256 → 创建 File 行 → 通过 `IStore.SetObject` 上传最终对象 → 在关系事务中建立业务引用。替换引用的数据库提交成功后发布 `CleanupFile(FileId)`；Worker 检查全部引用，确认无引用后通过 `IStore.DeleteObject` 先删对象、成功再硬删 File 行，失败按 JetStream 重投。Pending 清理状态必须可重新派发。File 元数据不可编辑，改名/MIME 必须上传新 File。

对象键：

```text
attachments/{attachmentId}
fix-uploads/{patchUploadId}
```

OVA 不属于对象存储，由 Runtime 配置外部 URL。

只有 `files` 保存 ObjectKey、显示文件名、内容类型、字节数、SHA-256。SHA 在流式上传中计算；业务表只保存 FileId。

## Attachment

Attachment 属于 Challenge 模板。文件内容、显示名称和 MIME 都不可原位修改；任何变化都上传新 File 并替换业务引用。存在 Flag Specification、RandomOne 选择或发布引用时禁止删除。

下载 API 先授权，再通过 `IStore.OpenRead` 由 API 返回文件流；不向客户端暴露 Bucket、ObjectKey 或 Provider URL。All 策略允许列出 AttachmentId/显示元数据并按 Id 下载；RandomOne 策略只暴露不带 Id 的单数下载路由，并在该请求中完成隐式选择。RandomOne 玩家不获得候选 AttachmentId/ObjectKey/列表，任何玩家都不获得 FlagId。

## AWDP PatchUpload

申请与上传分离：

1. 申请防御 API 先创建一个干净、无公开入口的 `AwdpTarget`；Queued、Provisioning 或 Running 时均可选择 Patch。
2. multipart 上传 API 在一个 PostgreSQL 临界区内把单文件、PatchUpload、Pending FixAttempt 与该 Target 原子绑定；Target Running 后再发布唯一一次 `RunAwdpFixVerification`。

一个 AwdpTarget 最多绑定一个 PatchUpload/FixAttempt。并发上传只有一个能消费 Target；消费后不可替换、删除或再次验证，随 Competition 最终硬删除。队伍再次尝试必须申请新的干净 Target。

Fix 文件继续以 `.tar.gz` 提交，内容须为 gzip 压缩的 tar；支持 GNU Tar、USTAR、PAX、V7，不再限制为 POSIX ustar/pax。压缩包上传上限来自 typed ChallengeDefinition 的 `MaximumPatchUploadBytes`，默认 256 MiB、最大 1 GiB；超过上限在解压校验与对象存储之前返回 413/`ArchiveTooLarge`。通过大小门禁后再验证 gzip header/trailer 与 tar block 结构并写新的 ObjectKey、计算 SHA-256；不是 tar.gz 则删除新对象、返回 422、且不创建 PatchUpload/FixAttempt，也不消费尚未绑定 Patch 的 Target。数据库 commit 失败也 best-effort 删除新对象，ObjectStorage bucket lifecycle 兜底清理由随机 Id 但无对应数据库行的临时对象。

Runner 在消费后再次验证 Hash/长度/格式并安全解包，拒绝绝对路径、`..`、符号/硬链接、设备文件、重复/大小写冲突路径、超过部署安全限额的条目/展开字节/单文件/压缩比。安全限额来自启动时验证的 `ArchiveExtractionOptions`（MaxEntries、MaxExpandedBytes、MaxSingleFileBytes、MaxCompressionRatio 均为正数），与 HTTP 压缩包上传上限相互独立。解包超限是 Rejected/FixArchiveLimitExceeded；Hash/长度/格式与已上传元数据不一致是 PlatformFailed/StorageUnavailable，不消耗尝试。

## 外部清理

数据库先写待清理事实；提交后发布消息，Worker 通过 FluentStorage 删除对象后完成硬删除。外部删除幂等；NotFound 视为成功；最终失败进入 JetStream DLQ。软删除不立即丢对象。

## FluentStorage 切换

当前版本没有旧存储格式双读、fallback 或 `.metadata` 旁车兼容逻辑。新安装直接从唯一
`InitialBaseline` 创建结构。已有部署若保留当前格式对象，必须在停服窗口由运维先完成数据库
规范化与迁移基线重置；否则应清空 PostgreSQL、NATS namespace、Redis 与对象存储后重建。
应用不会自动猜测或转换旧对象元数据。

平台进程不实现备份与恢复 API；外部运维使用统一加密恢复点备份 PostgreSQL、NATS JetStream
和对象内容/元数据，具体停写、校验与隔离恢复流程见 [备份恢复](backup-recovery.md)。
