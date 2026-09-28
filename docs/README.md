# NoCTF 权威设计规范

本目录的当前主题文档是 NoCTF 产品与技术规范。实现、测试、OpenAPI、EF 模型和部署配置必须
与它们一致；不得为了兼容旧实现恢复已删除概念。

## 阅读顺序

1. [产品与领域模型](product-domain.md)
2. [系统架构](architecture.md)
3. [进程、消息与并发](processes-messaging.md)
4. [关系数据模型与 provider 边界](database.md)
5. [认证与授权](authentication-authorization.md)、[通用 SSO](sso-authentication.md) 与 [CDUT Auth 接入](sso-cdut-auth-integration.md)
6. [API 通用规范](api-conventions.md) 与 [API 清单](api.md)
7. [GameplayFact 与判定/重判](gameplay-facts-adjudication.md)
8. [计分与排行榜投影](scoring-projection.md)
9. [Flag](flags.md)
10. [Runtime](runtime.md)
11. [附件与对象存储](storage-attachments.md)
12. 模式规范：[CTF](game-modes/ctf.md)、[CTF PatchVerification](ctf-patch-verification-experiment.md)、[AWD](game-modes/awd.md)、[AWDP](game-modes/awdp.md)、[KoH](game-modes/koh.md)
13. [实时与站内通知](realtime-notifications.md)
14. [赛事 Webhook](competition-webhooks.md) 与 [Webhook/BOT 对接手册](webhook-bot-integration.md)
15. [开发规范](development.md)、[测试规范](testing.md)、[部署边界](deployment.md)
16. [PostgreSQL、NATS JetStream 与对象存储备份恢复](backup-recovery.md)；本次生产切换另见[一次性关系模型迁移清单](one-time-relational-cutover.md)
17. [比赛题目仓库与 GitOps 管理设计](challenge-repository-gitops.md)
18. [AWD、AWDP 出题规范](challenge-authoring-awd-awdp.md) 与 [可复制出题模板](challenge-authoring-templates/README.md)

`data-model-wolverine-*`、`*-audit*`、`*-worklog*` 与 HANDOFF 文件只保存历史决策和验收记录，
不定义当前 persistence、messaging 或 deployment 契约。它们与上述当前文档冲突时，不得据此
恢复 JSON/数组持久化、PostgreSQL Wolverine、数据库锁或其他已删除架构。

## 强制边界

- 只支持 `Ctf`、`Awd`、`Awdp`、`Koh`。不存在 Penetration GameMode、多阶段题或静态容器群题型。
- `NoCTF.Host.dll` 是唯一进程入口；`Api`、`Worker`、`Runner` 是任意非空组合的角色。
- EF Core 关系模型是唯一持久化定义。公共模型 provider-neutral；Host 仅使用 PostgreSQL
  provider 与 migration assembly，隔离 SQLite 项目仅供模型测试。
- 多态聚合、事件、通知和 receipt 使用稳定 discriminator TPH；无身份值对象使用普通列 Complex
  Types；集合使用关系表。业务 JSON/数组列和旧 schema upgrader 禁止出现。
- Mutable aggregate 使用 `Guid ConcurrencyStamp`；集合不变量使用唯一约束与 Serializable bounded retry。
  不使用数据库方言锁、filtered index、check constraint 或业务 Raw SQL。
- Wolverine 只使用 NATS JetStream；PostgreSQL 不保存 Message Store、Inbox、Outbox 或 schedule。
  数据库提交后发布，关键 Pending 状态必须可重新派发，消费者必须按至少一次投递幂等。
- 周期调度和 Runner resource-domain 使用 NATS KV CAS 租约与 revision fencing。
- GameplayFact 不保存分值、分差或累计分。排行榜由事件驱动立即失效、500 ms 合并并从关系事实
  全量投影；不存在 Dirty 或 legacy cache 格式。
- Migration 与 Snapshot 只允许 `dotnet ef migrations ...` 生成，禁止手改。

## 已废弃内容

旧 Penetration、聊天 Bot 专用 Agent/群组同步协议、插件式 GameMode、进程内业务队列、
`runtime_operations`、`runtime_artifacts`、成员 UUID 数组、协作者 UUID 数组、JSON payload/config、
独立 API/Worker/Runner 可执行程序和旧前端路由均不属于当前架构。当前 TeamMember/TeamCaptain 与
CompetitionCollaborator 是关系模型的一部分。仓库历史或外部说明中出现旧设计时，不得恢复它们。
