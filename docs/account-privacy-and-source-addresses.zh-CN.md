# 校级比赛私密资料与来源 IP

## 资料范围

账户设置分为公开资料、校级比赛信息、账户安全。姓名（最多 100 字符）、学号（最多 64 字符）均可选填和清空。学号为字符串，保留前导零、允许字母、不要求唯一。只去掉首尾空白，拒绝控制字符。资料为用户自行填写，不代表实名认证，不参与注册、组队、参赛资格或成绩判断。

姓名、学号不加入 CurrentUser/PublicUserProfile、排行榜、公开队伍或导出协议；实体序列化也显式忽略这两个字段。私密页面只在内存中持有响应，不保存到 localStorage，响应设置 `private, no-store`。

| 入口 | 权限 |
| --- | --- |
| 账户设置 → 校级比赛信息 | 本人读写自己的资料 |
| 平台管理 → 用户详情 | 平台管理员查看任意用户 |
| 比赛管理 → 队伍详情 → 队员私密详情 | 平台管理员、本场 Owner、Manager、Judge |

比赛侧同时核对比赛、队伍和当前成员关系；不接受其他比赛的队伍 ID，不允许 Observer、队友或普通用户读取。默认包括待审核报名成员，以便报名核验；不包括已拒绝或已删除队伍。比赛人员只能看到本场提交，不会收到用户全平台登录历史。匿名化用户时清空姓名、学号。

## IP 的来源和用途

采集注册成功、登录成功、登录失败及已受理的 Flag／Patch 提交。前两者关联已认证的用户；失败登录是独立的系统安全事件，**不记录输入的用户名、不归属该用户名对应用户**。Flag 和 Patch 的 IP 保存在其 GameplayFact 中，沿用提交自身的用户、比赛、时间及提交 ID；重复提交不会改写原 IP。未受理请求不会新增比赛提交，练习不新增计分事实。

不采集地理位置、设备指纹或用户代理。IP 只用于赛事核验与安全排查，共享校园网、宿舍网和 VPN 出口十分常见，**不能据相同 IP 自动认定作弊**。

认证事件复用 `notifications` 中独立的 `AuthenticationSecurityActivity` 类型，目标为平台管理员，不派发给普通通知流、线程、赛事广播或公开导出。其 JSON 包含 schemaVersion、Id、Kind、OccurredAt、IpAddress；用户关联为 SourceId。失败事件 SourceType=System、SourceId=null。当前管理界面展示用户成功认证和参赛活动；无归属的失败认证事件保留为独立安全事件，供授权运维排查，不挂到任意用户详情。

管理界面展示留存窗口内最近 50 条记录，不表示完整历史。未知来源显示“来源未知”，不推测 IP，不补造旧记录。

## 留存

`AccountPrivacy__IpRetentionDays` 默认 30，可配置为 1–365。Api 与 Worker（组合 Host 则为同一进程）须使用相同值。

现有集群 Singular Agent 在启动时和每小时通过后台持久化队列发送 `ExpireAccountSourceAddresses`：删除超期认证安全事件，清空超期 GameplayFact 的 SourceIpAddress，**保留比赛提交、成绩和其他通知**。操作幂等，清理失败可重投递。读取时也按相同窗口过滤，不等后台清理才隐藏过期内容。数据库备份须另按运维备份留存政策处理。

## 反向代理

只从 `UseForwardedHeaders` 之后的 `RemoteIpAddress` 采集，IPv4-mapped IPv6 规范化为 IPv4；不读取客户端 X-Forwarded-For 第一项。缺少地址时保留未知。

- `ForwardedHeaders__KnownProxies__0`：真实反向代理 IP。
- `ForwardedHeaders__KnownNetworks__0`：仅代理所在的受控网段，不应配置整个校园网或客户端网络。
- `ForwardedHeaders__ForwardLimit`：默认 1，多层代理可显式设为 1–10。每一层仍须通过可信代理检查。
- 两个可信列表都为空时，**明确禁用转发头处理**，不使用框架“空列表信任所有来源”的行为。
- 保留转发头对称性校验及 AllowedHosts 配置。无需修改端口、反向代理拓扑或 `1panel-network`。

## 数据库与验收

EF 工具生成 `PrivateSchoolIdentityAndSourceAddresses` 增量 Migration，仅新增 users.school_full_name、users.school_student_number、gameplay_facts.source_ip_address 三个可空列。旧用户默认留空，不重建数据库。

回归覆盖：旧 schema 带用户数据增量升级；选填、清空、前导零、重复学号、长度限制；本人／管理员／Owner／Manager／Judge／Observer／普通成员权限；跨比赛 ID 替换；比赛人员不见登录历史；公开 DTO 和实体序列化不泄露；登录失败不关联用户名；认证事件不进入通知流；真实 PostgreSQL Flag／Patch IP 持久化且不进入广播；过期清理幂等且保留比赛事实；实际 ForwardedHeaders 中间件的空配置、非可信来源、IPv4、IPv6、伪造头及多跳限制。浏览器检查手机和桌面、深浅主题、独立保存和明确错误反馈。

本次仅本地提交，不推送、不部署。上线前运维应核实可信代理列表、Api/Worker 留存配置及数据库备份；不要重建数据卷或网络。

本地验收结果（2026-09-08）：后端非集成测试 1,100 项、前端测试 395 项通过；AccountPrivacy HTTP/PostgreSQL 综合回归 1 项、AWDP Target/Flag/Patch 回归 4 项、资料持久化 2 项、邮箱验证持久化 3 项通过。TypeScript 类型检查、后端构建及 Nuxt 生产生成通过。390px 手机视口实测无横向溢出，验证了深浅主题、保存中／成功、清空保存和邮件操作失败提示。
