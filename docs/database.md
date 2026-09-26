# NoCTF 关系数据模型

## Provider 边界

- EF Core 10 关系模型是唯一持久化定义。公共 `NoCTF.Infrastructure` 只引用 EF Core Relational；
  不引用 Npgsql，也不包含数据库方言 API。
- PostgreSQL 是唯一运行时 provider，注册、设计时工厂和唯一 `InitialBaseline` 位于
  `NoCTF.Persistence.PostgreSql`。隔离的 SQLite 项目仅供模型测试，Host 不引用它。
- 新增 SQL Server、MySQL 或其他关系 provider 时，必须增加独立 provider 与 migration assembly，
  不得修改领域模型来保存方言兼容分支。
- 未知 `Database:Provider` 在启动时失败。生产数据不由应用自动升级或猜测旧结构。

## 可移植映射规则

- Competition、Challenge、CompetitionChallenge、CompetitionEvent、Notification、CommandReceipt、
  GameplayFact、RuntimeInstance、RuntimeReceipt、ChallengeFlag、SsoProvider 等联合类型使用稳定字符串
  discriminator 的 TPH；每个 leaf 都是 sealed 类型。
- 无身份的 ScoreCurve、资源额度、安全策略等值对象使用 EF Core Complex Types 映射为普通列，
  不使用 `ToJson()`。
- Track、Hint、Webhook Target、Capacity Allocation、Access Endpoint、Published Port、环境变量、标签、
  端口映射、Compose Service Resource、SSO Host/Scope、成员和权限全部使用普通或 owned 子表；有顺序
  的集合显式保存 `Position`。
- 公共模型禁止 JSON/数组列、provider column type、filtered index、数据库 check constraint、collation、
  `FromSql*`、`ExecuteSql*` 以及手写业务 SQL。
- 大小写不敏感查找只查询应用生成的 `Normalized*` 字段。领域时间保留 `DateTimeOffset`，持久化使用
  UTC ticks `long`，不依赖 provider 时区列语义。
- 主键由应用生成 UUIDv7。外键、普通唯一索引、长度、必填和精度由 EF 模型表达；其余业务不变量
  由构造器和统一保存前验证器保证。

## 并发与关系不变量

- 所有可变聚合使用应用生成的 `Guid ConcurrencyStamp`，配置为 EF concurrency token；保存时自动轮换。
- 集合不变量使用普通唯一约束和 `IsolationLevel.Serializable`。可安全重放的幂等命令最多重试三次，
  使用短随机退避；包含外部副作用的非幂等操作不得自动重试。
- Competition collaborator 的 `(CompetitionId, UserId)` 唯一并携带 Role。Team member 的
  `(CompetitionId, UserId)` 唯一；TeamCaptain 以 `(TeamId, CaptainId)` 外键指向 TeamMember。
- 可选唯一邮箱、外部身份、活跃 Runtime 和可空一对一分别使用唯一实体或显式关联表，不依赖
  filtered unique index。
- 不使用 `FOR UPDATE`、`FOR SHARE`、`SKIP LOCKED`、PostgreSQL advisory lock 或长连接锁。

## 消息与缓存

PostgreSQL 保存业务事实，以及与公开比赛事件同事务写入的 Webhook 专用 Outbox 和逐目标投递状态；
不保存 Wolverine Message Store、Inbox 或 scheduled message。Wolverine 仍使用 NATS JetStream。
其他业务提交成功后通过
`IPostCommitMessagePublisher` 发布；关键 Pending 状态必须可由数据库事实重新派发，以覆盖提交后、
发布前崩溃。所有消费者按至少一次投递和业务幂等键设计。

Redis 仅承载 FusionCache 的可丢弃 L2 与失效 backplane。请求配额、SSO 流程和容量分配
使用 EF Core 关系记录；Runner 在线状态、短租约与排行榜发布版本使用 NATS KV。
排行榜正文与其他读模型缓存丢失后从关系事实重建。

## Migration

迁移和 snapshot 只能由 EF CLI 生成，禁止手改：

```powershell
dotnet ef migrations add InitialBaseline `
  --project backend/src/NoCTF.Persistence.PostgreSql/NoCTF.Persistence.PostgreSql.csproj `
  --output-dir Migrations
dotnet ef migrations has-pending-model-changes `
  --project backend/src/NoCTF.Persistence.PostgreSql/NoCTF.Persistence.PostgreSql.csproj
```

仓库只保留当前 PostgreSQL `InitialBaseline`。旧 schema、旧 JSON、旧数组、旧枚举编号和旧 migration
不属于运行时契约，也不得通过双读、fallback 或升级器重新引入。

生产旧库的一次性停机导入与验收见[关系模型切换清单](one-time-relational-cutover.md)。
