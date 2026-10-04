# 发布与比赛生命周期

## 状态流转

```text
Draft → Visible ↔ Published
Visible / Published → Running ↔ Paused
Visible / Published / Running / Paused → Finished
```

| 状态 | 使用方式 |
| --- | --- |
| Draft | 内部准备，比赛不公开，不自动开赛 |
| Visible / Published | 公开和报名阶段，具体入口按访问设置 |
| Running | 正式竞技，允许模式规定的提交和调度 |
| Paused | 暂停竞技调度和相应动作，保留部分执行资源 |
| Finished | 正式结束，不能恢复 Running |

Owner/Manager 可执行合法开始、暂停、恢复、结束。平台管理员按全平台权限操作；Judge/Observer 不管理生命周期。

## 发布前检查

检查标题、时间、配置、协作者、队伍审核、题目发布和资源容量。题目发布与比赛公开是不同操作，两者都需完成。

至少有一个已发布有效题目，模式与模板匹配，Flag/附件/运行定义组合完整。AWD/AWDP/KoH 还需对应 Runner、Checker、注入或 Control URL 就绪。

## Start Gate

比赛概览有单独“开赛检查”按钮。它返回当前全部错误，检查成功不自动开始；界面的开始按钮通常在 Published 显示，后台允许的转换仍由实际授权接口决定。

手动开始和定时开始共用 Start Gate。失败保持原状态，不部分启动比赛；返回错误包含稳定 code、题目/队伍 ID、配置路径和说明。

| 常见错误 | 修复步骤 |
| --- | --- |
| NoPublishedChallenge | 引用有效题目并发布 |
| FlagMissing | 按题目交互配置正确作用域 Flag |
| TemplateUnavailable / 模式不匹配 | 恢复合法模板引用，检查模式 |
| RuntimeDefinitionInvalid | 修正服务、入口和分配方式 |
| CheckerDefinitionInvalid | 补齐 Checker / Patch / 超时预算 |
| RunnerPoolUnavailable | 检查部署 Provider/Pool 与在线 Runner |
| RuntimeQuotaInsufficient | 增加必要队伍额度和真实执行资源 |
| ObjectStorageUnavailable | 修复存储凭据、bucket 和网络 |
| TeamInvariantInvalid | 核对成员、队长和获批队伍状态 |

一次修复完整错误数组，再重试；不要绕开校验直接修改数据库状态。

## 暂停与恢复

暂停冻结有效运行时间，因此 AWD 加固与 AWD/AWDP 轮次延后，KoH 停轮询。CTF Runtime TTL 使用真实 UTC，不暂停计时；暂停不意味着题目环境永久保留。

EndAt 是绝对终点，即使 Paused 也会 Finished。需要延期时先按允许的配置操作显式调整结束时间，不能只按下暂停当成无限延时。

恢复后检查当前轮次、Runtime、调度和榜单，再发布说明。暂停期间的缺失 KoH 观察不补跑计分。

## 结束与清理

Finish 前确认终局规则、榜单状态和题解截止。Finished 不能恢复正式比赛；CTF 练习是单独配置和 Runtime purpose，不是复活正式比赛。

结束后观察资源回收和待处理消息，按权限审核题解与申诉，再导出所需结果和保留恢复点。需要删除比赛时先满足状态与资源限制，并确认影响范围，不能用删除代替 Finish。
