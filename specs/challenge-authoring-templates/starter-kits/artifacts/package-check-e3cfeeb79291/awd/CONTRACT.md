# AWD 出题契约

通用部署见 [出题规范](../challenge-authoring.md)，赛事语义见 [AWD 完整规则](../awd-rules-2026.zh-CN.md)。[模板](starter-kits/awd/README.md) 提供持久于当前容器的业务存储与真实联网 Checker。

## Runtime 与 Store

Runtime 使用 `Container / PerTeam / AwdRotation`，至少一个 Participants 公开入口。每个有序 Store 选择目标命名服务、投放方式和历史保存能力。Store ID 由后台生成并保持稳定，公开攻击定位信息通过 `publicLocator` 提供。

`BusinessChecker` 使用题目真实业务写入当前清单中 `requiresWrite=true` 的 Flag，并用私有上下文读取。`RunnerCommand` 使用 Runner 注入；Checker 只读这些材料。命令在指定服务执行，使用已导出变量：

```sh
mkdir -p "/dev/shm/flags/${STORE_ID}"
printf '%s' "${FLAG}" > "/dev/shm/flags/${STORE_ID}/${FLAG_ID}"
```

样例 HTTP 服务使用相同的不带连字符的小写 UUID 路径。命令幂等，同一 Flag 重投不新建身份；按 StoreId / FlagId 保存，历史材料不得在每轮覆盖或在 Checker 中补写。Runtime 重置导致真实历史丢失时如实上报。

## Checker 输入

| 变量 | 含义 |
| --- | --- |
| `NOCTF_TARGET_HOST` | 默认目标内部主机 |
| `NOCTF_AWD_TARGETS` | JSON 服务名到内部主机的映射 |
| `NOCTF_AWD_TASK_URL` | 以 Bearer JWT GET 本次固定任务 |
| `NOCTF_AWD_DEADLINE` | 绝对执行期限 |
| `NOCTF_CALLBACK_URL` | POST 证据地址 |
| `NOCTF_CALLBACK_TOKEN` | 绑定本次任务的内部 JWT |

任务对象包含 `gameplayFactId`、`attemptId`、`deadline`、`flags`。每项包含 `flagId`、`storeId`、`storeName`、`targetServiceName`、`flag`、`historical`、`requiresWrite`、`privateReadContext`、`publicLocator`。只处理领取的清单；Flag 不从环境变量读取。

## 回执

```json
{
  "connection": { "outcome": "Passed", "reason": "None" },
  "functionality": { "outcome": "Passed", "reason": "None" },
  "flags": [{
    "flagId": "aaaaaaaa-0000-0000-0000-000000000001",
    "write": { "outcome": "Passed", "reason": "None" },
    "read": { "outcome": "Passed", "reason": "None" },
    "privateReadContext": null,
    "publicLocator": "/flags/store-id/flag-id"
  }],
  "observedAt": "2026-10-21T00:00:00Z"
}
```

逐项结果为 `Unknown`、`Passed`、`TargetFailure`、`PlatformFailure`、`NotRequired`；原因使用平台枚举，例如 `ConnectionRefused`、`ConnectTimeout`、`RequestTimeout`、`FunctionalCheckFailed`、`FlagStoreFailed`、`CurrentFlagMissing`、`HistoricalFlagMissing`、`InfrastructureFailure`、`CheckerTimedOut`。

回执恰好覆盖全部 FlagId，无重复；无需写入才可报告 NotRequired。当前写入失败仍检查历史。预算耗尽保留条目并报 Unknown。Checker 区分 TCP 建连、业务检查、当前数据和历史数据；不计算最终健康或分数。任务、Token、目标响应和私有上下文禁止写日志。

平台分类为 Ok、Mumble、Offline、Recovering、InternalError；攻击捕获独立记录。平台故障或证据不足进入同轮重检与待裁定，不自动当作选手故障。任务读取止于执行期限；执行窗口内的观察有 30 秒自动回执宽限，JWT 寿命不延长执行或 Flag 接收窗口。

## 赛事与验收

比赛配置轮长、加固、间隔、接收及历史窗口、每 Store 发行数量与 ATK / DEF / SLA 预算。题目仅覆盖需要差异的字段。默认轮长 180 秒、接收 5 轮，每 Store 每轮 1 Flag；具体分值以本场规则为准。

- [ ] 两队目标和攻击定位独立；跨轮有效 Flag 首次捕获成功，同队重复与自己 Flag 不计分。
- [ ] BusinessChecker 当前写入、RunnerCommand 只读清单、多 Store 和多 Flag 完整覆盖。
- [ ] 删除历史后仍报告 HistoricalFlagMissing，持续恢复状态不会伪造历史。
- [ ] 服务离线、业务错误、当前数据缺失、基础设施失败和超时分别产生正确证据。
- [ ] 暂停与轮间冻结发行及接收；已受理事实继续处理，结束保留合法未决结算。
- [ ] 正式平台验证至少两轮及一次接收窗口过期、重启接管、重复回执和最终资源回收。
