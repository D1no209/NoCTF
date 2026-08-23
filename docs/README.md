# NoCTF 权威设计规范

本目录是 NoCTF 后续开发的唯一产品与技术规范。实现、测试、OpenAPI、数据库模型和部署配置必须与这里一致；代码现状与本文冲突时，以本文为目标架构，不能为了兼容旧实现而保留错误概念。

## 阅读顺序

1. [数据模型与 Wolverine 调度简化权威规范](data-model-wolverine-simplification.md)
   - [阶段 0 基线](data-model-wolverine-stage0-baseline.md)
   - [Wolverine 6.29.2 Spike](wolverine-6.29.2-spike.md)
   - [生产切换与回滚 Runbook](data-model-wolverine-cutover.md)
2. [产品与领域模型](product-domain.md)
3. [系统架构](architecture.md)
4. [进程、消息与并发](processes-messaging.md)
5. [数据库](database.md)
6. [认证与授权](authentication-authorization.md)
7. [API 通用规范](api-conventions.md) 与 [API 清单](api.md)
8. [GameplayFact 与判定/重判](gameplay-facts-adjudication.md)
9. [计分与排行榜投影](scoring-projection.md)
10. [Flag](flags.md)
11. [Runtime](runtime.md)
12. [附件与对象存储](storage-attachments.md)
13. 模式规范：[CTF](game-modes/ctf.md)、[AWD](game-modes/awd.md)、[AWDP](game-modes/awdp.md)、[KoH](game-modes/koh.md)
14. [实时与站内通知](realtime-notifications.md)
15. [QQBOT JWT 接入](qqbot-jwt.md)
16. [开发规范](development.md)、[测试规范](testing.md)、[部署边界](deployment.md)
17. [PostgreSQL、对象存储与 Wolverine 备份恢复](backup-recovery.md)
18. [比赛题目仓库与 GitOps 管理设计](challenge-repository-gitops.md)
19. [AWD、AWDP 出题规范](challenge-authoring-awd-awdp.md) 与 [可复制出题模板](challenge-authoring-templates/README.md)

当前代码和其余文档尚未完成简化迁移时，第一项文档定义新版本目标语义并优先于冲突内容；现状只能用于建立迁移清单。完成阶段 0 后，必须同步 `AGENTS.md` 及所有受影响专题文档，不能长期依赖优先级说明维持矛盾规范。

## 强制边界

- 只支持 `Ctf`、`Awd`、`Awdp`、`Koh`。不存在 `Penetration` GameMode、多阶段题或静态容器群题型。
- `Api`、`Worker`、`Runner` 是三个可组合角色；既可使用三个兼容独立入口，也可通过
  `NoCTF.Host` 以任意非空组合运行，缺省为单进程全合一。
- PostgreSQL 是业务事实源；Redis 是可丢失的缓存、限流、SignalR backplane 与 Runner 心跳存储。
- Wolverine PostgreSQL persistence 承载 Inbox、Outbox 与 Dead Letter。周期调度由集群 Singular Agent 从事实重建，禁止用业务列或 Wolverine Scheduled Message 保存下一次周期执行时间；业务任务不得使用进程内 Channel。
- GameplayFact 不保存分值、分差或累计分，只保存当前结果。排行榜由事件驱动立即失效、500ms 合并并用当前配置全量投影；不存在 Dirty 列或快照列。
- 所有配置可在任何生命周期状态修改；保存后发布影响投影的比赛事件并失效排行榜缓存，但不会自动重判 GameplayFact。
- Migration 与 Snapshot 只允许 `dotnet ef migrations ...` 生成，禁止手改。

## 已废弃内容

旧 Penetration、QQBot 专用 Agent/群组同步协议、插件式 GameMode、进程内队列、`runtime_operations`、`runtime_artifacts`、TeamMember 顺序队长模型和 CompetitionCollaborator 子表不属于目标架构。QQBOT 只能作为普通 JWT 通知消费者接入。仓库历史、旧提交或外部说明中出现这些旧设计时，不得据此恢复它们。
