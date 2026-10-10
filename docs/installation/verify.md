# 首次启动与验收

验收从基础健康、账号会话、文件、题目执行和业务计分依次进行。正式赛事开放前，应在独立测试比赛中走完整闭环，而不是在真实比赛中写合成 Flag 或人工调分。

## 1. 基础服务

Docker 在安装目录执行：

```bash
docker --context YOUR_CONTEXT compose --env-file .env -f docker-compose.yml ps
docker --context YOUR_CONTEXT compose --env-file .env -f docker-compose.yml exec -T noctf /usr/local/bin/noctf-healthcheck
curl -f https://noctf.example.com/health/ready
```

如果启用了 S3/监控，诊断 Compose 命令应带上部署时同样的 overlay，或通过配置助手执行。Kubernetes 检查 Pod Ready、PVC Bound、各角色 rollout、Metrics API 和 Gateway 路由。

预期所有必要服务可用，Host readiness 为 200。数据库迁移失败时 Host 不应进入就绪，不用绕开迁移启动消费者。

## 2. 管理员与普通账号

1. 从 HTTPS 平台域名登录 seed 管理员。
2. 打开平台管理页面，确认具有 Administrator 权限。
3. 刷新浏览器页面，确认会话恢复。
4. 保持页面超过普通 Access Token 生命周期，再执行查询，确认 Refresh Cookie 流程正常。
5. 注册测试普通账号；若启用邮箱验证，完成邮件收取和验证。
6. 用普通账号登录，确认无法进入平台管理。

如果首次登录成功、刷新后掉线，检查 HTTPS、Secure Cookie、精确 Refresh Origin 和可信代理，而不是延长 Access Token 掩盖配置问题。

## 3. 文件存储

在测试模板上传合法附件，并在获批测试队伍的题目页下载：

- 文件长度、内容和名称正确。
- 未授权账号无法读取私有附件。
- 头像/比赛海报等合法上传正常。
- S3 部署的 bucket 已初始化，公开 Origin/TLS 与对象请求匹配。
- 使用接近实际赛事大小的文件验证代理上传和下载，不只测几字节文件。

## 4. 第一场静态 CTF

按 [举办第一场比赛](../competition/quick-start.md)创建隔离 CTF：设置合法时间、Flag、已发布题目、固定计分、有效队伍。

1. 普通账号建队并提交报名，管理员批准为 Approved。
2. 组织者发布比赛并开始，确认 Running。
3. 队伍可读取题面、附件和本队信息。
4. 提交错误 Flag，确认最终结果与规则一致。
5. 提交正确 Flag，等待最终结果，再检查本队得分和排行榜。
6. 重复正确提交，确认没有重复解题奖励。
7. 检查赛事动态/通知的可见性，断线重连后状态仍一致。

## 5. 容器题目闭环

使用经过审核的小型容器题目，镜像放在平台可访问的 Registry：

1. 配置服务名、镜像、CPU、内存和 Runtime 级访问入口。
2. 确认 Runner 在线，Provider/Pool 与部署一致。
3. 用 Approved 队伍启动 Runtime，等待就绪。
4. 从实际选手网络访问页面返回的随机端口/NodePort。
5. 如有动态 Flag，确认注入和 Checker 回调正常，不公开受保护内容。
6. 重置，确认出现新 Runtime UUID，并重新读取连接地址。
7. 停止，确认外部容器/Pod/端口最终回收。

Docker 不加题目 HAProxy；Kubernetes 检查实例间与平台数据网络隔离。页面就绪与实际可访问要分别记录。

## 6. 要使用的模式逐项验证

| 模式 | 额外验收 |
| --- | --- |
| AWD | 轮次 Flag 注入、健康 Checker、攻击提交、暂停/恢复、Finish 清理 |
| AWDP | Break Flag、合法 Fix、坏包/失败包、判题调度、轮次结果 |
| KoH | 共享 Hill、各队自己的 Control Flag、轮询计分、无控制与超时处理 |
| CTF 闯关 | 前驱解锁、重锁、勋章授予/撤销与分数保留 |
| LiveSolo | 名单/媒体/隔离、共同受理顺序、延迟节目、录像、纠正及恢复，使用 [独立验收清单](../operations/live-solo-readiness.md) |

只准备 CTF 比赛时不必运行不使用的模式，但报告不能把未运行项写为通过。

## 7. 开放正式使用前

| 检查项 | 应保留的记录 |
| --- | --- |
| 安装版本 | Git revision、完整 Host digest、依赖锁定文件 |
| 访问网络 | 平台/文件/Registry 域名、题目公网路径、代理与端口策略 |
| 密钥 | 外部保管的位置与恢复方式，不写入报告正文 |
| 功能闭环 | 登录、文件、Runtime、评测、榜单通过/失败/未运行 |
| 容量 | 实际题目资源与预计并发、Runner 配额、磁盘和端口预算 |
| 可恢复性 | 同一恢复点备份和隔离恢复演练结果 |
| 值守 | 故障负责人、监控入口、比赛暂停和沟通流程 |

验收完成后，停止测试比赛并回收测试 Runtime。保留配置和恢复点，继续按 [运维流程](../operations/upgrade.md)维护。
