# 阶段 2：核心实体与隐私模型

## 基线

- 分支：`codex/data-model-wolverine-simplification`
- 起点提交：`e264d49f refactor(model): remove revision concurrency protocol`
- 权威规范：`docs/data-model-wolverine-simplification.md` 第 5.2—5.5、12 节阶段 2。
- 中间模型只用于源码和测试迁移；阶段 10 前不生成、应用或手改 EF migration/snapshot。

## 现有模型问题

| 能力 | 旧字段/行为 | 目标 |
|---|---|---|
| 用户邮箱 | `Email` + `NormalizedEmail` 双写，`IsEmailPublic` 允许公开协议返回邮箱 | 只保存 canonical `Email`；只有平台管理员和授权审计导出可见 |
| 队伍名称 | `NormalizedName` 与 `(CompetitionId, NormalizedName)` 唯一索引禁止重名 | 删除规范化字段/索引，所有寻址使用 TeamId；同屏重名显示 Guid 前 8 位 |
| 比赛配置时间 | `ConfigurationUpdatedAt`、`TrackConfigurationUpdatedAt` | 删除冗余更新时间 |
| 有效运行时间 | `RunningSince` + `AccumulatedRunningSeconds` 缓存 | 从 `CompetitionLifecycleChanged` 不可变事件区间派生 |
| 榜单可见性 | 枚举、开始/应用时间、冻结快照、Dirty 多份状态 | 只保存 `FrozenStartAt`、`HiddenStartAt`，按真值表派生 |
| 赛道邀请码 | PBKDF2 `InvitationCodeHash` 与兼容验证分支 | 配置 JSON 内明文 `InvitationCode`；固定时间比较；仅授权管理协议回显 |
| AWD 调度 | `LastScheduledAwdRound`、`AwdScheduleDueAt` | 删除；阶段 8 由 Singular Agent 从业务事实重建 |

## 本阶段字段与类型

### 删除

- `User.NormalizedEmail`
- `User.IsEmailPublic`
- `Team.NormalizedName`
- `Competition.ConfigurationUpdatedAt`
- `Competition.TrackConfigurationUpdatedAt`
- `Competition.RunningSince`
- `Competition.AccumulatedRunningSeconds`
- `Competition.LeaderboardDirty`
- `Competition.LeaderboardVisibility`
- `Competition.LeaderboardVisibilityStartsAt`
- `Competition.LeaderboardVisibilityAppliedAt`
- `Competition.FrozenLeaderboardSnapshotJson`
- `CompetitionChallenge.LastScheduledAwdRound`
- `CompetitionChallenge.AwdScheduleDueAt`
- `CompetitionTrackDefinition.InvitationCodeHash`

### 新增/替换

- `Competition.FrozenStartAt : DateTimeOffset?`
- `Competition.HiddenStartAt : DateTimeOffset?`
- `CompetitionTrackDefinition.InvitationCode : string?`
- 单一 `EmailCanonicalizer`（`Trim().ToLowerInvariant()`）供注册、管理员创建/修改、验证与找回查询使用。
- 从生命周期事件派生有效运行区间的应用能力。
- 由两个时间字段派生 `Normal/Frozen/Hidden` 的应用策略。

## 受影响接口与协议

- 身份：注册、登录、密码找回、管理员用户列表/详情、我的资料、公共资料。
- 队伍：创建/改名/列表、比赛内队伍投影、排行榜和管理列表显示名。
- 比赛：管理详情、公开详情、生命周期、榜单可见性读写、榜单查询。
- 赛道：管理端读写可回显邀请码；参赛者与公开读取只返回是否需要邀请码。
- 调度：AWD round 协调器和 KoH 运行时目标在阶段 8 前改为事件派生时钟，不保留下一次执行列。
- OpenAPI/TypeScript SDK：所有协议变更后由生成流程统一更新，禁止手改。

## 定向测试门禁

- canonical email 在注册、登录、找回、管理员写入路径一致；数据库唯一索引拒绝大小写/空白等价邮箱。
- 公共用户、成员、通知作者和普通导出协议不包含邮箱；平台管理员协议仍可见。
- 同比赛允许完全同名和仅大小写不同队伍；GIN 成员查询、Captain 约束与成员去重约束保持。
- 同屏重名队伍显示 `名称 · <Guid 前 8 位>`。
- 生命周期区间覆盖未发布、运行、暂停、恢复、结束及当前运行截止 `min(now, endAt)`。
- 榜单可见性真值表覆盖未来时间、同刻 Hidden 优先和恢复 Normal。
- 邀请码固定时间比较；Judge/Observer/参赛者/公开协议、日志与非管理导出均不泄露明文。
- 生产源码不再引用本阶段删除字段；Release build、Unit/Architecture、真实 PostgreSQL 定向测试、前端测试/typecheck/build 通过。

## 数据迁移影响

- 阶段 10 才通过 `dotnet ef migrations` 生成单一 InitialBaseline。
- 生产切换需要把 `normalized_email` 回填到 canonical `email` 并先检测冲突；发生 canonical 冲突时不能自动选择账户，必须进入人工转换清单。
- 赛道 PBKDF2 哈希不可逆，旧邀请码无法无损恢复；Alpha 破坏性切换时需要 Owner/Manager 重新设置受保护赛道邀请码，切换前导出“需重置赛道”清单，不导出明文。
- 旧冻结快照不迁移；以 `LeaderboardVisibilityChanged` 事件和当前配置时间转换为新时间列，切换后从 PostgreSQL 事实全量重建榜单。

## 回滚点

- 本阶段代码提交可整体 revert。
- 未生成或应用中间 migration，不改变共享/生产数据库。
- 若 canonical email 冲突、可见性事件缺失或邀请码重置责任人无法确定，阶段 10 生产转换计划必须停止并请求确认。

## 完成证据

- Release solution build：0 warnings，0 errors。
- Unit 定向测试：`27/27`，覆盖邮箱 canonicalization、公共资料隐私、同名队伍注册、
  邀请码校验与权限、可见性真值表和有效运行时间事件派生。
- Architecture：`52/52`，OpenAPI 可见性契约已改为 `frozenStartAt` / `hiddenStartAt`，
  不再要求旧枚举或 Revision 字段。
- 真实 PostgreSQL/Testcontainers：`5/5`，覆盖 canonical Email 唯一约束、可见性时间持久化、
  同比赛重复队名、赛道邀请码只向授权管理读取回显。
- Frontend：`bun test` 为 `274/274`，typecheck 与 production build 通过；构建只保留既有的
  chunk size、plugin timing 和第三方依赖提示。
- OpenAPI 导出和 TypeScript SDK 生成连续执行后，四个生成制品 SHA-256 不变。
- 生产源码扫描不再引用本阶段删除字段；公共用户资料不返回邮箱，邮件投递日志只记录 UserId。
- `git diff --check` 无 whitespace error。

完整 Integration 套件在阶段 10 前仍受旧 migration/model drift 阻塞，未将其误报为通过。
本阶段的关系型不变量改用 `EnsureCreated` 创建当前模型的可丢弃 Testcontainers PostgreSQL；
历史 migration 与 snapshot 保持原样，等待阶段 10 使用 EF 工具统一重建单一基线。

## 退出结论

阶段 2 的核心实体、隐私模型和最小协议闭环已完成，退出门禁通过。下一阶段为阶段 3
`Notifications/Questions` 线程化；继续禁止在阶段 10 前生成中间 migration。
