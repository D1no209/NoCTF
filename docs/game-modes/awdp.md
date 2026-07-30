# AWDP

## 配置契约

Competition 必须配置 `RoundDurationSeconds > 0`，并提供以下比赛规则默认值；CompetitionChallenge.RulesJson 可用 nullable 字段逐项覆盖：

```text
BreakSettlement / FixSettlement: Milestone | PerRound
BreakPoints / FixPoints: bigint >= 0
BreakWrongPenalty / FixFailurePenalty / ViolationPenalty / ServiceDownPenalty: bigint >= 0
RequireBreakBeforeFix: bool
MaxBreakSubmissions / MaxFixSubmissions: int       // <=0 无限
EvaluationDispatchMode: Automatic | ManualBatch
```

覆盖数值 0 是显式 0，只有 null 表示继承。Runtime、Checker、Patch 和 ReadyTimeoutSeconds 只属于 Challenge.DefinitionJson，Competition 配置和 CompetitionChallenge.RulesJson 均不得声明或覆盖。复用 Challenge 模板即复用相同判题定义。所有 JSON 带 schemaVersion，未知版本拒绝。

## 基本模型

AWDP 是独立 Break/Fix 模式：

- Break：正确 Flag Submission；只接受单 flag。
- Fix：消费不可变 tar.gz PatchUpload 的 archive Submission；不是 Flag 动作。

两者分别配置 `AchievementSettlement.Milestone | PerRound` 与 Points。

- Milestone：同队同题整场首次 Correct 获得一次。
- PerRound：每逻辑轮同队同题首次 Correct 获得一次。
- 后续正确 Submission 仍保存 Correct，投影器不重复计成就。
- 首次由 ReceivedAt+SubmissionId 决定，重判可改变。

Round 使用 EffectiveRunningTime；没有加固期。对任意 Submission，用 Competition 生命周期审计积分得到 `EffectiveRunningTimeAt(ReceivedAt)`，再计算 `floor(effective / current RoundDurationSeconds)+1`。RoundDuration 修改会自动重分组全部历史 Achievement，不需重判。中途 Finish 不伪造 RoundEnd，结束前 Break/Fix 仍按 ReceivedAt 所属轮次结算。

## RequireBreakBeforeFix

Competition 默认、题目覆盖。为 true 时，Fix 触发前要求同队同 CompetitionChallenge 已有当前有效 Correct Break；否则 409、零 Submission、不消费 PatchUpload。Break 后续重判非 Correct 不删除 Fix，但投影不再给其成就分；恢复后重新参与。

## Upload 与次数

Patch multipart 上传和 Fix trigger 是两个 API，详见 [存储](../storage-attachments.md)。MaxBreakSubmissions/MaxFixSubmissions <=0 无限；API 预检、Pending 预占、Worker 二次验证。触发时超限不消费 Upload、不建 Submission。

管理重判精确使用原 archive 和当前 Runtime/Checker 配置，不复制 Submission，不消耗尝试。Archive 保留到 Competition 最终硬删除。

## 一次性判定网络

AWDP target 只支持单 Container，Provider Docker/Kubernetes；不支持 Compose/OVA。每个 Fix 创建全新目标与隔离网络：

- Docker user-defined bridge；Kubernetes 统一 Namespace+Operation labels+NetworkPolicy；
- target 只接收同 Operation Checker 的内部流量；不暴露公网。Runtime 定义声明一个内部端口用于平台网络策略，Checker 镜像自行知道使用哪个端口，平台不注入端口；
- Checker 只访问 target、DNS、内部 callback；
- target/Checker 禁止公网、平台私网、其他 Runtime；
- 完成/失败/超时清理资源，labels/reaper 兜底。

Fix 永不修改比赛长期 Runtime。

创建 disposable target 时会固化 Competition configuration revision、
CompetitionChallenge revision 和 Challenge definition revision。Fix 结果落库前再次核对
这三个 revision；任一变化都把本次 Fix 记为 PlatformFailed，停止并清理即时 target，
不计分、不消耗尝试，也不自动用新配置重跑。管理员需要显式 rejudge。

## Archive/Patch

只支持 tar.gz。安全解包见存储文档。Challenge.DefinitionJson 配置：

```text
PatchEntrypoint (default fix.sh, safe relative path)
PatchCommand (default ["/bin/sh", "{entrypoint}"])
PatchTimeoutSeconds
ReadyTimeoutSeconds
Runtime Container definition
Checker definition
```

不自动剥离顶层目录；入口必须在 archive root 下精确存在。Runner 只替换独立 argv 中的 `{entrypoint}` 为 `/noctf/fix/<path>`，不 Shell 拼接。目标 Provider Running 后复制 archive、执行 Patch；不做 Probe。Patch exit 0 启 Checker；非零/超时分别保存 AwdpPatchFailed/AwdpPatchTimeout，并归入 StillVulnerable。

## Checker callback

Runner 注入：TARGET_HOST、TARGET_READY_TIMEOUT_SECONDS、NOCTF_CALLBACK_URL、NOCTF_CALLBACK_TOKEN。题目不能覆盖这些保留变量；端口由 Checker 自身配置。Checker 不获得 archive、对象键/文件名、Flag、选手身份或长期 Runtime。

```text
POST /api/internal/v1/awdp/fix-results
outcome: Fixed | StillVulnerable | RuleViolation | ServiceUnavailable
```

映射：

| outcome | Result / Failure |
|---|---|
| Fixed | Correct |
| StillVulnerable | Wrong / AwdpFixFailed |
| RuleViolation | Rejected / AwdpViolation |
| ServiceUnavailable | Wrong / AwdpServiceDown |

无 callback/Runner/Provider/Storage/非法 callback 是 PlatformFailed，不消耗尝试且不替换重判前旧事件。Patch 与 Checker 只是一个 Submission 的内部阶段，最终只有一条 ScoringEvent；诊断在结构化日志/Wolverine。

## 罚分

Competition 默认、题目覆盖，均非负：

- BreakWrongPenalty：Break Wrong；
- FixFailurePenalty：Checker StillVulnerable、Patch nonzero、Patch timeout；
- ViolationPenalty：RuleViolation；
- ServiceDownPenalty：ServiceUnavailable。

PlatformFailed 不扣。Patch/业务结果消耗 Fix 尝试；平台故障不消耗。总分可负，配置动态重投影。

## 排名

1. 总分降序；
2. 有效 Fix 成就数降序；
3. 有效 Break 成就数降序；
4. 累计罚分绝对值升序；
5. 最后一次有效 Fix 时间升序；无 Fix 为无穷大；
6. Team.RegisteredAt 升序；
7. TeamId 升序。
