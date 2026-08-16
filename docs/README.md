# NoCTF 权威设计规范

本目录是 NoCTF 后续开发的唯一产品与技术规范。实现、测试、OpenAPI、数据库模型和部署配置必须与这里一致；代码现状与本文冲突时，以本文为目标架构，不能为了兼容旧实现而保留错误概念。

## 阅读顺序

1. [产品与领域模型](product-domain.md)
2. [系统架构](architecture.md)
3. [进程、消息与并发](processes-messaging.md)
4. [数据库](database.md)
5. [认证与授权](authentication-authorization.md)
6. [API 通用规范](api-conventions.md) 与 [API 清单](api.md)
7. [GameplayFact 与判定/重判](gameplay-facts-adjudication.md)
8. [计分与排行榜投影](scoring-projection.md)
9. [Flag](flags.md)
10. [Runtime](runtime.md)
11. [附件与对象存储](storage-attachments.md)
12. 模式规范：[CTF](game-modes/ctf.md)、[AWD](game-modes/awd.md)、[AWDP](game-modes/awdp.md)、[KoH](game-modes/koh.md)
13. [实时与站内通知](realtime-notifications.md)
14. [QQBOT JWT 接入](qqbot-jwt.md)
15. [开发规范](development.md)、[测试规范](testing.md)、[部署边界](deployment.md)
16. [PostgreSQL、对象存储与 Wolverine 备份恢复](backup-recovery.md)
17. [比赛题目仓库与 GitOps 管理设计](challenge-repository-gitops.md)
18. [AWD、AWDP 出题规范](challenge-authoring-awd-awdp.md) 与 [可复制出题模板](challenge-authoring-templates/README.md)

## 强制边界

- 只支持 `Ctf`、`Awd`、`Awdp`、`Koh`。不存在 `Penetration` GameMode、多阶段题或静态容器群题型。
- `Api`、`Worker`、`Runner` 是三个可组合角色；既可使用三个兼容独立入口，也可通过
  `NoCTF.Host` 以任意非空组合运行，缺省为单进程全合一。
- PostgreSQL 是业务事实源；Redis 是可丢失的缓存、限流、SignalR backplane 与 Runner 心跳存储。
- Wolverine PostgreSQL persistence 承载 Inbox、Outbox、Scheduled Message 与 Dead Letter。业务任务不得使用进程内 Channel。
- GameplayFact 不保存分值、分差或累计分，只保存当前结果。排行榜每 15 秒为脏比赛用当前配置全量投影。
- 所有配置可在任何生命周期状态修改；保存后设置排行榜 Dirty，但不会自动重判 GameplayFact。
- Migration 与 Snapshot 只允许 `dotnet ef migrations ...` 生成，禁止手改。

## 已废弃内容

旧 Penetration、QQBot 专用 Agent/群组同步协议、插件式 GameMode、进程内队列、`runtime_operations`、`runtime_artifacts`、TeamMember 顺序队长模型和 CompetitionCollaborator 子表不属于目标架构。QQBOT 只能作为普通 JWT 通知消费者接入。仓库历史、旧提交或外部说明中出现这些旧设计时，不得据此恢复它们。
