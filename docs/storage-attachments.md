# 对象存储、附件与 Patch

## Provider

统一 `IObjectStorage`：S3Compatible（生产默认）与 LocalFileSystem（开发/测试/单节点）。Bucket/目录不公开。平台生成不可猜测 ObjectKey；原文件名只进入不可变 `files.file_name`。

业务表不再保存 ObjectKey、文件名、MIME、长度或 SHA256；只保存 `FileId`，所有 FK 为 Restrict。Users/Teams Avatar、Competition Poster、Platform Logo、ChallengeAttachment、PatchUpload、DataExport 都引用 `files`。

上传顺序固定为：受控临时文件计算长度/SHA256 → 创建 File 行 → 上传最终对象 → 事务锁定 File 并建立业务引用。替换引用时向 Wolverine Outbox 投递 `CleanupFile(FileId)`；Worker 检查全部引用，确认无引用后先删对象、成功再硬删 File 行，失败按 Wolverine 重试。File 元数据不可编辑，改名/MIME 必须上传新 File。

对象键：

```text
attachments/{attachmentId}
fix-uploads/{patchUploadId}
```

OVA 不属于对象存储，由 Runtime 配置外部 URL。

只有 `files` 保存 ObjectKey、显示文件名、内容类型、字节数、SHA-256。SHA 在流式上传中计算；业务表只保存 FileId。

## Attachment

Attachment 属于 Challenge 模板。文件内容、显示名称和 MIME 都不可原位修改；任何变化都上传新 File 并替换业务引用。存在 Flag Specification、RandomOne 选择或发布引用时禁止删除。

下载 API 先授权。All 策略允许列出 AttachmentId/显示元数据并按 Id 下载；RandomOne 策略只暴露不带 Id 的单数下载路由，并在该请求中完成隐式选择。S3Compatible 返回 60 秒预签名 GET 的 302；LocalFileSystem 由 API stream。RandomOne 玩家不获得候选 AttachmentId/ObjectKey/列表，任何玩家都不获得 FlagId。

## AWDP PatchUpload

上传与触发分离：

1. multipart 上传 API 把单文件写对象存储，创建 PatchUpload；不创建 Submission/尝试。
2. Fix trigger API 引用 PatchUploadId；次数预检通过后事务消费并创建 Submission/Outbox。

每队每题最多一个未消费 Upload。新上传成功后替换旧未消费记录，并以 Outbox 清理旧对象；消费后不可替换/删除，随 Competition 最终硬删除。

仅接受内容为 gzip 压缩 POSIX ustar/pax tar 的 tar.gz；不以 FileName/MIME 判定。上传流先写新的 ObjectKey并计算 SHA-256，同时验证 gzip header/trailer 与 tar block 结构；不是 tar.gz 则删除新对象、返回 422、且不创建 PatchUpload。数据库 commit 失败也 best-effort 删除新对象，ObjectStorage bucket lifecycle 兜底清理由随机 Id 但无对应数据库行的临时对象。

Runner 在消费后再次验证 Hash/长度/格式并安全解包，拒绝绝对路径、`..`、符号/硬链接、设备文件、重复/大小写冲突路径、超过部署安全限额的条目/展开字节/单文件/压缩比。安全限额来自启动时验证的 `ArchiveExtractionOptions`（MaxEntries、MaxExpandedBytes、MaxSingleFileBytes、MaxCompressionRatio 均为正数），只限制解包后的危险工作量，不限制 HTTP 压缩包字节数。超过限额是 Rejected/FixArchiveLimitExceeded，不是 HTTP 413；Hash/长度/格式与已上传元数据不一致是 PlatformFailed/StorageUnavailable，不消耗尝试。

## 外部清理

数据库先写待清理事实/Outbox，Worker 删除对象后完成硬删除。外部删除幂等；NotFound 视为成功；最终失败进入 DLQ。软删除不立即丢对象。

平台进程不实现备份与恢复 API；外部运维使用统一加密恢复点备份 PostgreSQL、Wolverine schema
和对象内容/元数据，具体停写、校验与隔离恢复流程见 [备份恢复](backup-recovery.md)。
