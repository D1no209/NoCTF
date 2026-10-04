# 术语表

| 术语 | 手册中的含义 |
| --- | --- |
| Human / Bot | 账号种类，和平台角色独立 |
| User / Organizer / Administrator | 平台普通用户、组织者、管理员角色 |
| Owner / Manager / Judge / Observer | 比赛所有者、管理者、裁判、观察者 |
| Competition | 一场固定模式比赛 |
| Team | 比赛中的队伍，一个账号同场只能属于一队 |
| Captain | 队长，必须是该队成员 |
| InvitationToken | 队伍唯一 32 字符加入凭据 |
| Unregistered / Pending / Approved / Rejected | 未报名、待审、获批、拒绝 |
| Track | 队伍赛道，控制准入、计分与公开竞争资格 |
| Direction / Tag | 题目内容组织方式，不是队伍赛道 |
| Challenge | 可复用全局题库模板 |
| CompetitionChallenge | 比赛内题目实例，有自己的 UUID、规则和发布状态 |
| RuntimeInstance | 一个具体执行环境，重置创建新 UUID |
| Practice | CTF 赛后练习 Runtime purpose |
| TemplateTest | 题库模板测试 Runtime purpose |
| Container service | Runtime 中命名服务，每服务一个容器或 Pod |
| Provider / Runner Pool | 部署选定的执行提供方与执行资源池 |
| Checker | 健康、修复等自动检查执行任务 |
| Flag | 答案、攻击或控制权验证内容 |
| Break / Fix | AWDP 正确 Flag 成就 / 独立修复包成就 |
| Control Flag / Hill | KoH 队伍控制凭据 / 共享目标环境 |
| GameplayFact | 比赛行为的当前权威记录与判定 |
| ManualAdjustment | 非零 signed Int32 人工调分事实 |
| DynamicRecalculation / AtSolve | CTF 动态重算 / 按解题位置结算 |
| Blood reward | 一二三血等首次完成顺序奖励 |
| Start Gate | 开始比赛前原子校验配置、队伍与执行条件 |
| EffectiveRunningTime | 排除暂停时间的竞技运行时长 |
| TTL | 执行环境按真实时间到期的生命周期 |
| Api / Worker / Runner | HTTP 身份入口 / 异步业务 / 外部执行角色 |
| Host | 唯一可执行程序，承载一个或多个角色 |
| JetStream | NATS 持久消息与协调基础设施 |
| FusionCache / backplane | 可替换缓存与多实例失效/实时通信 |
| ConcurrencyStamp | 应用生成的 Guid 乐观并发标记 |
| FileId / ObjectKey | 业务文件引用 / 存储对象标识 |
| Origin | scheme + host + port，不含 path |
| Digest | 不可变镜像内容标识，形式 `repository@sha256:...` |
| NodePort | Kubernetes 对外暴露服务的节点 TCP 端口 |
| Pod PID attestation | 已验证 kubelet/cgroup 限制后的节点声明标签 |
| Webhook / CloudEvent | 赛事公开事件外部投递 / 标准事件封装 |
| DeadLetter | 无法完成正常处理、需要查明原因的失败状态 |
| TokenVersion | 账号全量 Access/Refresh 令牌失效版本 |
| Recovery point | PG、对象、离线消息与密钥关联的同一可恢复点 |
