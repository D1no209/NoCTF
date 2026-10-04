# 平台审计与归档

入口：平台设置 → 审计，`/admin/platform/audit`。审计把授权的账号、平台、比赛操作与关联证据组织为可追溯视图，不是原始应用日志页。

## 过滤与读取

按 kind、Actor UUID、Competition UUID 和时间范围筛选。可见种类包括 CompetitionLifecycle、UserAccountLifecycle、PlatformAdministration、CompetitionAdministration、CompetitionLeaderboardVisibility 和 CompetitionEvent。

1. 选择明确窗口和种类，输入需要的操作者/比赛 ID。
2. 应用筛选，从结果读取动作、发生时间、原因和资源。
3. 按需加载更多，使用服务器提供的 cursor。
4. 点击关联用户、比赛、队伍、题目或 Runtime 跳转。
5. 将授权记录与当前状态对照，区分历史动作和现行结论。

审计条目关联历史状态，不代表目前资源仍存在；永久删除后的平台记录仍可能保留目标标识。

## 归档导出

页面提供平台审计归档动作，先填写/核对时间和范围，再下载并检查实际 ZIP 内容。按服务端授权保管；它不是公开比赛结果包，也不是恢复数据库、文件和 JetStream 的一致恢复点。

导出记录也可进入审计，不包含 secret 原文或原始受保护正文。网络超时先确认是否产生导出/操作事实，不反复修改历史记录。

## 各种记录的边界

| 记录 | 用来回答 |
| --- | --- |
| 平台审计 | 谁对账号/平台/比赛执行了授权操作 |
| 比赛动态 | 比赛发生了哪些状态、内容和结果事件 |
| GameplayFact | 当前行为及最终评测结果 |
| Loki | 技术执行、错误与关联链路 |
| PCAPNG | 已捕获路径中有哪些网络报文 |

## 使用限制

先检查筛选、时区和权限，不把没有查到某行视为事件绝未发生。调分、封禁和重判结论要与对应权威记录核对；受保护 Flag 读取规则另看 [Flag 管理](../../competition/content/hints-flags.md)。
