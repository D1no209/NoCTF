# 常见问题

## 安装

### 生产服务器需要 Bun 或 .NET SDK 吗？

使用完整预构建 Host 镜像时不需要。Bun 用于源码前端和此文档站；平台前端已经是 Host 内的静态文件。见 [安装方式](../installation/index.md)。

### 安装平台需要克隆 Git 仓库或运行 docker build 吗？

不需要。下载成功 CI 的部署配置包，读取 `deployment.json` 中的完整镜像地址，再配置和 pull/up 即可。详见 [获取发布产物](../installation/images.md)。

### Compose 起来了，为什么不能访问服务器 8080？

生产平台没有宿主发布端口。把容器化反代接入 `noctf-proxy`，通过 `noctf-web:8080` 上游和自己的 HTTPS 域名访问。见 [反向代理](../installation/reverse-proxy.md)。

### 需要手动创建数据库表吗？

部署模板启用 Host 自动迁移，完成前不开放 HTTP/消费者。不要手工改 EF 迁移或快照；旧版本不兼容迁移需要单独审查，而不是猜表结构。

### 首次管理员密码修改 seed 后为什么没变化？

SeedAdmin 只初始化，已有管理员不会被 seed 重置。通过正常修改/重置密码或受控管理员流程处理。

### 能把 Local 存储直接改成 S3 吗？

已有数据时不能只改变量。需要停写迁移，保留对象键/业务引用并验证内容和元数据。见 [配置与存储](../installation/configuration.md)。

## 参赛

### 队伍已经建立，为何不能读题？

新队为 Unregistered，需队长提交报名后 Approved。也检查题目发布、比赛状态、封禁和 CTF 闯关。见 [队伍流程](../player/teams.md)。

### 邀请队员后为什么需要重报？

成员变化是组织变化，会退回 Unregistered；重新校验当前赛道全体成员要求。轮换队伍邀请码不会改变状态。

### 队伍邀请码有期限或人工接受步骤吗？

没有独立邀请实体。当前唯一 32 字符 token 允许持有者直接加入；轮换立即使旧值失效。

### 暂停会延长 CTF 容器 TTL 吗？

不会，TTL 依真实 UTC 计时。暂停冻结的是有效竞技时间，EndAt 仍是绝对终点。

### AWDP Fix 是否提交一个特殊 Flag？

不是。Break 由正确 Flag 得到，Fix 是独立 archive 修复验证，可能要求先 Break。见 [模式指南](../player/modes.md)。

### KoH 需要点“提交 Flag”宣示控制吗？

不用普通提交。让共享 Hill 的控制端点返回本队精确 Control Flag，由平台轮询判定。

### 赛后练习会增加正式分数吗？

不会。CTF Practice 使用独立 Runtime，提交保留结果但在正式窗口外不产生正式得分或血奖。

## 管理与运维

### AtSolve 是否意味着历史分数永远不变？

后续普通解题不改变之前的解题价；配置、资格、赛道、重判和人工调整仍可改变历史投影。见 [计分配置](../competition/settings/scoring.md)。

### 勋章会加分吗？

不会。它与当前闯关图和成员快照关联，失活可以撤销；后来加入的成员不自动补发。

### Bot 能使用密码登录和 Refresh Cookie 吗？

不能。管理员为 Active Bot 签发普通 Access JWT，过期前按管理流程更新。

### 能单独吊销一个 JWT 或一个设备吗？

当前没有逐 token/设备会话模型。普通退出清 Cookie，改密码/管理撤销通过 TokenVersion 全量失效。

### Docker 单服务题目之间严格网络隔离吗？

单服务复用部署级 challenge bridge，同网实例可能互通。需要严格隔离时使用经过真实 NetworkPolicy 验收的 Kubernetes，见 [Runtime 定义](../challenge-bank/runtime-checkers.md)。

### 只备份 PostgreSQL 够吗？

不够。需要文件/S3、停写 JetStream 和密钥组成同一恢复点，见 [备份与恢复](../operations/backup.md)。

### 界面受理成功，榜单没有立即变化怎么办？

先核对最终事实、资格与可见性，再检查投影。异步刷新延迟时不重复提交或补分。见 [排障](../operations/troubleshooting.md)。
