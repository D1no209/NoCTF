# AWDP 出题契约

通用部署见 [出题规范](../challenge-authoring.md)，计分见 [AWDP](../game-modes/awdp.md)。[模板](starter-kits/awdp/README.md) 使用真实文件路径穿越、可替换解析器与独立网络 Checker。

## 攻击目标与 Flag

配置 `Container / PerTeam / PerTeam`，恰好一个服务、一个内部端口，并选择 `FLAG` 环境变量。至少一个 OwnerOnly 公开入口，全部入口都引用同一服务和内部端口。模板使用 `main:8080`。

平台为当前攻击 Runtime UUID 生成 Flag。服务按原文写入私有文件，攻击利用必须能读取完整值；Reset 后产生新 UUID 和新 Flag，旧值失效。Break 是提交 Flag 后正确判定产生的 BreakAttempt。

## Fix 目标与归档

Fix 是独立 tar.gz 上传，产生 FixAttempt。平台按已保存题目定义创建一次性防御目标，移除公开入口，注入规范化归档，执行 Patch，再运行 Checker，完成后回收。修补不会应用到选手长期攻击目标。是否要求先 Break 由比赛及单题 `RequireBreakBeforeFix` 决定。

归档根目录直接包含 `fix.sh` 和声明的替换文件。使用普通文件与目录，拒绝绝对路径、路径穿越、链接、设备等特殊文件、冲突条目及超出预算的包。默认上传上限为 256 MiB，硬上限 1 GiB；实际题目建议填写自己的合理上限，展开预算另行验证。

后台填写 `patchEntrypoint=fix.sh`，命令逐项填写 `/bin/sh`、`{entrypoint}`。平台把独立占位参数替换为 `/noctf/fix/fix.sh`。脚本应从自身目录定位随包文件：

```sh
set -eu
input="$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)"
cp "$input/resolver.py" /opt/challenge/resolver.py
```

模板用户 10001 对该路径有写权限。Patch 在目标默认用户下运行；不得要求额外容器能力或 root。Patch 超时、Checker 超时和就绪等待分别配置，ReadyTimeoutSeconds 不超过 Checker 超时。

一次性 Fix 目标不注入攻击 Flag。模板在缺少 `FLAG` 的 Fix 目标中生成随机探测内容，并在私有目录放置固定漏洞探测文件。攻击 Runtime 必须实际读取传入的 `FLAG`。Checker 无需知道正式 Flag。

## Checker

| 变量 | 含义 |
| --- | --- |
| `TARGET_HOST` | 一次性目标内部主机 |
| `TARGET_READY_TIMEOUT_SECONDS` | 目标就绪等待秒数 |
| `NOCTF_CALLBACK_URL` | 本次结果回调地址 |
| `NOCTF_CALLBACK_TOKEN` | 绑定 GameplayFact 与 Runtime 的内部 JWT |

```json
{"outcome":"DefenseSucceeded"}
```

以 `Authorization: Bearer <token>` POST JSON，合法结果为 DefenseSucceeded、ExploitSucceeded、ServiceAbnormal。不要添加 GameplayFactId 或 RuntimeId；身份来自 JWT。

先验证正常业务，再执行完整漏洞探测，探测后再次验证正常业务。漏洞仍能利用报告 ExploitSucceeded；正常业务损坏、目标不可用或执行超时报告 ServiceAbnormal；业务可用且全部漏洞探测被正确拒绝才报告 DefenseSucceeded。依赖、协议、输入准备或回调故障保持非零退出，平台收敛为 PlatformFailed。Checker 正常回调后退出 0。

开启 `checkerFixInput` 后，Checker 启动前可读取 `/noctf/fix/` 中的规范化选手输入副本。该目录不包含真实目标执行后的文件。只读取约定的普通文件，按大小和内容验证；不要在持有回调令牌的 Checker 内执行选手脚本。需要真实运行结果时访问 `TARGET_HOST` 的业务接口。样例以网络检查为主，开启开关时额外检查输入文件是否存在。

## 赛事与验收

比赛与单题配置 Break / Fix 曲线、次数、错误 Flag / EXP 成功 / 服务异常罚分、RequireBreakBeforeFix、派发方式。轮次是动态结算周期，具体当前规则以配置为准。

- [ ] 两队攻击 Flag 独立，Wrong、另一队 Flag、旧 Runtime Flag 不获得 Break。
- [ ] 标准修补保持正常业务并阻断路径穿越；原样包仍可利用。
- [ ] 全部文件拒绝、服务停止、Patch 非零、Patch 超时和 Checker 非零分别验收。
- [ ] 开启及关闭 Checker Fix 输入均能正确判定；读取输入不执行上传脚本。
- [ ] 归档根目录、嵌套入口、路径穿越、链接、特殊条目和超限包按真实平台校验。
- [ ] 正式平台上传、一次性目标、回执、Fix 状态、排行榜、暂停恢复与资源回收完成验收。
