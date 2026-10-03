# 并发、HTTP 幂等与 CTF 练习判题审计

本次仅操作本地仓库、临时浏览器夹具及 Testcontainers PostgreSQL／Redis／NATS。不访问生产数据，不推送、不部署，不改变 `1panel-network`。

## 证据分级

### 已在隔离环境复现

1. **并发改密码**：两个独立 DbContext 先读取同一旧密码，再并发修改；旧实现两次均返回 Changed（期望 1，实际 2）。修复后只有一次成功，TokenVersion 为 1。
2. **反向锁等待**：重建旧 Runtime 的 competition FOR UPDATE 与提交方 team FOR UPDATE 顺序，实际 INSERT competition_events 触发外键锁，得到 SQLSTATE 40P01。此为可控交错复现，不是生产日志结论。
3. **Gameplay 发送端未持久化**：用项目实际 MessageRouting 配置及真实 Wolverine EF Outbox，SaveChanges + Commit 后业务收据存在，但 outgoing_envelopes 为 0。配置持久化发送后为 1，新 Host 可恢复投递。
   扩展到动态 Runner 地址后也复现缺口：两条消息只持久化一条。补齐精确 Runner 业务地址策略后，两条消息均持久化，重启恢复各消费一次。
4. **前端反馈窗口**：练习成功／失败弹窗约 3.2 秒自动关闭；原页面没有保留最近判定。挂起 HTTP 请求也没有请求级截止时间。新版以持久行内提示保留结果，对超时、无效响应和接口失败给出明确提示，并恢复提交按钮。

### 源码确认的条件性风险与防护

- 自动哈希升级曾直接写回读取的旧用户对象，可能覆盖同时更改的新密码；现在使用相同的条件替换协议。隔离 PostgreSQL 回归控制旧哈希升级暂停，在新密码提交后再恢复，确认不会恢复旧密码。
- HTTP 请求没有独立幂等收据；Wolverine Inbox 只能对消息去重。新版在业务事务内记录 HTTP 收据，重试返回已提交操作，新的主动操作使用新标识。
- 原限流含进程内“用户 + IP”分区；新共享入口额度独立约束账号与 IP，密码计算、Flag 请求和 Patch 上传有并发预算。Redis 故障不降级为无限放行。
- Patch 的对象存储在最终事务前进行。现在入口先做共享准入与 Target 归属核验，已消费 Target 在解析压缩包和对象写入前拒绝，保留最终数据库单次消费约束及孤儿文件延迟清理。

### 未能确认的事项

- **未复现普通 CTF 练习判题在后端不执行**。后续实现已移除专用 practice-flag 接口；CtfPanel 复用普通 Flag 提交与异步状态链路，静态题不要求 Runtime，有 Runtime 的题要求本队有效 Practice Runtime。练习提交创建普通 GameplayFact，但由正式时间窗口排除计分和血奖。
- CTF 动态 Flag 现行设计是“比赛题目 + 队伍”派生，实例注入该值，不是每次重启换一个随机 Flag。本次保留此设计，验证没有运行中的本队练习实例时不可判定动态 Flag，不接受静态模板 Flag 冒充注入值。
- 未进行生产攻击模拟、校园出口压测、真实代理拓扑验收或 Runner 上的真实 Patch 执行。真实依赖回归不能替代这些生产特定条件的核验。

## 修复后的业务约定

### 密码与令牌

`UserCredentialWrite` 为密码修改、重置和自动升级提供条件写：只有数据库中的哈希仍与本次观察相同才替换；TokenVersion 以数据库表达式递增。改密码同时作废未使用的重置令牌。平台角色、状态、邮箱验证和全局撤销的 TokenVersion 更新也使用原子递增，不再用过期内存值覆盖。

普通简介等资料继续 last-write-wins；不引入 refresh session、令牌轮换、持久化修订字段。

### 数据库锁

参赛写操作遵循 competition SHARE → team UPDATE → challenge/runtime/file 的顺序；共享比赛锁阻止生命周期修改穿越准入，同时兼容其他队伍的共享准入和外键 KEY SHARE。组队的比赛锁改为 NO KEY UPDATE，仍保留成员竞争保护，不将所有参赛操作全局串行化。

死锁、序列化冲突、锁等待预算超时返回 503 `TransactionBusy` 及 Retry-After；不自动重放带外部副作用的整段事务。客户端以原请求标识重试。

### 请求幂等

以下入口要求 UUID 格式的 `Idempotency-Key`：正式 Flag、手动调分、Patch 上传、一次性防御 Target 申请、选手 Runtime 操作、管理端队伍／共享 Runtime 操作及模板测试 Runtime 操作。

- 同一次网络重试复用同一标识；主动再次操作生成新标识。
- 标识绑定操作者、操作类型、比赛和资源范围；成功收据保留输入 SHA-256，已提交标识携带不同内容返回 409 `IdempotencyPayloadMismatch`。
- 收据复用私密 `notifications.HttpCommandReceipt`，与业务事实、Outbox 同事务保存，普通通知接口和线程不返回它。
- 不保存 Flag 请求明文；Runtime 收据只保存实例 ID，避免复制测试 Flag。Patch 比对包摘要、长度及文件元信息。
- 前端仅对结果不确定的网络请求保留重试标识；已完成的相同 Flag 是新操作，不按内容全局去重。
- 标识不能跨用户或业务范围用于读取别人的结果。提交前回滚不会保留成功收据。
- 客户端重试记录只在页面会话内存中；页面完全刷新或外部调用方丢失标识后，服务端无法推断两次请求是不是同一操作。外部客户端必须自行保存原标识。
- 终止／强制终止等入口保留原有状态幂等路径，不承诺缓存完全相同的 HTTP 响应；本次重点覆盖重复创建、重置、续期、扣次数和计分。

### 共享限流与上传

配置节 `RequestAdmission`（环境变量以 `__` 分隔）：

| 配置 | 默认 |
| --- | ---: |
| AuthenticationIpPerMinute | 600 |
| AuthenticationAccountPerMinute | 15 |
| PasswordConcurrency | 8 |
| SubmissionPerUserPerMinute | 30 |
| SubmissionConcurrency | 16 |
| SubmissionPerUserConcurrency | 2 |
| PatchConcurrency | 2 |
| PatchPerUserConcurrency | 1 |

Patch 另有每 Target 1 个并发槽，UUID 规范化后作为限流身份。Redis Lua 原子检查账号、IP 与并发槽，多 API 实例共享；上传槽周期续租，失去租约会取消当前请求。429 提供说明和 Retry-After，依赖不可用为 503。仅显式单进程 Development 配置使用内存适配器，这不是生产 Redis 故障回退，也不作为分布式一致性证明。

登录／注册／改密请求体上限 16 KiB；正式 Flag 请求上限 4 MiB、每批最多 128 个。原 Patch 包大小、解压大小、路径和成员安全规则保留，不恢复 USTAR-only。入口准入发生在 multipart 绑定前，不开启数据库长事务等待客户端上传。

### 提交后的消息

命名业务队列及赛事事件发送端启用 durable Outbox。仅在消息与业务数据保存且 Commit 确认之后调用 `FlushCommittedMessagesAsync`；立即分发失败记录明确日志，并通过 `X-NoCTF-Outbox-Delivery: pending` 表达已持久化、待恢复投递，而不是伪装成业务未提交。开发环境非持久化适配器不会做这一保证。

本次保留现有传输拓扑，未迁移 NATS，也未改外部端口、网络或服务。动态 Runner 发送端按精确业务 subject 前缀启用持久化，排除监听端和回复端，不更改其他队列的全局超时／并行度。生产实际重启恢复时延仍需在后续部署验收中核验。

## 数据库与交付

主要修改文件与行为对应：

| 文件／目录 | 行为变化 |
| --- | --- |
| ClientApp/components/challenges/FlagSubmit.vue | 练习独立判题、持续结果、异常与截止时间、重复点击保护 |
| ClientApp/utils/command-attempt.ts、plugins/api.client.ts | 网络结果不确定时复用 nonce，成功后新操作不按内容去重 |
| Authentication/AuthenticationStore.cs、PasswordResetStore.cs、UserCredentialWrite.cs | 统一条件替换密码、原子令牌失效、重置令牌作废 |
| Administration/PlatformAdministrationStore.cs、UserAccountAdministrationStore.cs | 管理操作的 TokenVersion 不覆盖并发增量 |
| Competitions/Participation/CompetitionParticipationLock.cs、SubmissionAttemptLock.cs、TeamRuntimeQuota.cs、CompetitionTeamMutationCriticalSection.cs | 比赛共享锁、队伍配额／次数锁顺序 |
| Application/Admission、Infrastructure/Admission、API/Security/RequestAdmissionMiddleware.cs | 跨实例独立额度、可续租并发预算、上传前授权及限流 |
| Application/Commands/Idempotency、Infrastructure/Commands/Idempotency | 同事务 HTTP 成功收据、输入摘要冲突校验 |
| SubmissionIntakeStore.cs、PatchUploadStore.cs、RuntimeInstanceStore.cs、AdminRuntimeStore.cs、ChallengeTestRuntimeStore.cs、AwdpDefenseTargetStore.cs | 各类关键写操作接入收据及提交后分发语义 |
| Hosting/MessageRouting.cs、DurableRunnerCommandPolicy.cs、WolverineTransactionalMessageOutbox.cs | 命名队列和动态 Runner 发送端持久化、已提交但待分发状态 |
| API/Security/RequestSafetyExceptionHandler.cs | 锁竞争、幂等冲突、共享准入失败的明确可重试响应 |

不新增表、列或持久化版本字段，**无需 Migration**。HTTP 收据沿用现有通知表；不重建数据库、数据卷或网络。测试使用新建、可销毁的隔离数据库。

验收包括真实 PostgreSQL 密码竞争、真实 FK 死锁及新锁顺序、实际 Flag／调分／Runtime 请求收据、真实 Wolverine Outbox 的提交前回滚及新进程恢复、共享 Redis 额度与租约续期／失效、练习静态／动态与 HTTP 权限、前端正确／错误／异常／挂起／刷新交互。测试精确结果以本次交付说明为准。

本地已通过的隔离测试组：PasswordConcurrency 4、PasswordReset 2、UserAccountDeletion 1、PlatformUserAccountStatus 2、FileCleanup 2、CompetitionPracticeMode 4、ParticipationLockOrder 2、RuntimeQuota 4、TeamMutationConcurrency 1、AwdpDefenseTarget 4、RedisRequestAdmission 1、CommandReceiptOutbox 3。共 30 项；其中 Outbox 组使用实际 PostgreSQL／NATS／Wolverine，密码与配额组的替身不作为消息持久化证据。

最终全量非集成测试 1,102 项、前端测试 400 项、TypeScript 类型检查、后端构建和 Nuxt 生产生成通过。该结果不代表已运行全部 Docker/Runner 端到端套件，更不代表生产故障已经复现。
