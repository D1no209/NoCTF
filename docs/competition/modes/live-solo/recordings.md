# 录像预览、保留与恢复

入口：`/competitions/比赛ID/live-solo/recordings/对局ID`，选择录像后带 `/录像ID`。列表按页码读取；工作人员与公开回放使用不同读取范围。

## 录像与临时节目

录像可选，默认关闭；关闭录像仍可能产生供延迟节目的临时 HLS。启用时记录每位上场者屏幕，不只记录观众看到的主视图，并保留 UTC、Match/Round 关联。

录制归档、临时节目、Provider 原始暂存和最终输出是不同阶段。只有已 Completed、存在正数文件长度且授权通过的录像才提供有效预览。

## 预览与原生下载

选择成员/录像，核对状态、时间、大小和失败代码。工作人员取得短期预览授权，Range 请求仍检查当前 JWT/MFA/权限；切身份或失去资格后内容被清空。

原生下载前请求当前有效下载授权，等待实际文件传输并验证内容。过期或授权失败重读，不猜文件对象键或转发短期 URL。

## 保留和公开

| 动作 | 条件与结果 |
| --- | --- |
| Hold | 有裁判能力时设争议保留，防止按普通期限删除 |
| ReleaseHold | 解除当前保留，不代表立即删除 |
| Publish | 管理者在整个赛事结束后公开已完成回放 |
| Withdraw | 撤下公开回放，管理记录保留 |

填理由并确认，查看不可修改的操作历史。Deleting 状态不允许迟到的 Hold 重新抢占删除流程。公开前确认赛事、成员和证据保管政策，不把比赛进行中原始录像直接公开。

## 按失败代码恢复

CapacityUnavailable/SourceUnavailable 且尚未请求启动：先修复容量或确认同一来源仍 Sharing，再 RetryPendingStart。

StartUncertain 等未知结果：先 ReconcileExport，查询真实 Provider 输出，保留预留，不自动重复启动。

ArchiveCapacityUnavailable/ExportTooLarge：处理归档容量/大小后 RetryArchive。已终态 ExportFailed 需按允许流程 StartNewChunk，得到新的分段，不伪造此前缺失部分已经录好。

## 存储边界

预留覆盖暂存、最终原始输出、托管归档三份副本；原始清理成功后才释放适用预算。降低码率不缩减已存在预留。未知目录或文件阻止扩大删除范围，交由运维调查。

录像保留、原始清理和多节点恢复的完整部署验收仍需完成，参考 [媒体部署](../../../installation/live-solo-media.md)和 [就绪清单](../../../operations/live-solo-readiness.md)。
