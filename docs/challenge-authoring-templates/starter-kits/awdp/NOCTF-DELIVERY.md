# AWDP 题目交付单

> Break 是 Flag 提交，Fix 是独立 `tar.gz` 归档。不得记录真实比赛 Token、平台地址、Runner 凭据或私有仓库密码。

## 1. 元信息

| 项目 | 内容 |
| --- | --- |
| 题目名称 / 方向 | 待填写 |
| Owner / Manager | 待填写 |
| 目标镜像 | 待填写 |
| Checker 镜像 | 待填写 |
| 唯一内部端口 | 待填写 |
| 内存 / CPU / PID | 待填写 |

## 2. 漏洞、Break 与修复判据

- 漏洞位置、触发条件和利用步骤：待填写。
- 动态 Break Flag 的环境变量、漏洞泄露路径和精确匹配验证：待填写。
- Fix 成功后漏洞路径应如何失效：待填写。
- 必须继续可用的正常业务功能：待填写。
- RuleViolation 判据：待填写，例如删功能、固定返回、阻断 Checker 或伪造结果。

## 3. Player Runtime 与一次性 Fix target

- [ ] Container、`PerTeam`，不是 Compose 或 OVA。
- [ ] FlagSource 为 `PerTeam`，只填写注入环境变量名。
- [ ] Player 使用 host port `0` 和 OwnerOnly URL。
- [ ] Fix target 自动忽略公网端口和 URL，TeamId 为空。
- [ ] `InternalPorts` 恰好一个，服务监听 `0.0.0.0`。
- [ ] 全新容器稳定复现漏洞，Fix 目标路径可写。
- [ ] 不依赖宿主路径、Docker Socket、其他队伍、平台网络或外网。

| 项目 | 内容 |
| --- | --- |
| 启动命令 argv | 待填写或“镜像默认入口” |
| 内部端口 | 待填写 |
| Runtime TTL | 待填写，覆盖补丁、就绪和 Checker 总耗时 |
| 操作超时 | 待填写 |
| no-new-privileges | 开启 / 关闭，原因：待填写 |
| 只读根文件系统 | 通常关闭；采用值与原因：待填写 |
| 非 root | 开启 / 关闭，镜像 USER：待填写 |
| CapDrop / CapAdd | 待填写 |

## 4. Fix 合约

| 项目 | 默认值 | 采用值 |
| --- | --- | --- |
| 入口 | `fix.sh` | 待填写 |
| 命令 | `["/bin/sh", "{entrypoint}"]` | 待填写 |
| 补丁超时 | 60 秒 | 待填写 |
| 就绪等待 | 30 秒 | 待填写 |
| 最大上传 | 256 MiB | 待填写，不能超过 1 GiB |

- [ ] gzip 压缩的 POSIX ustar/pax tar，入口位于归档精确路径。
- [ ] 没有绝对路径、`..`、链接、设备文件、重复路径或大小写冲突。
- [ ] 不依赖平台地址、Token、真实 Flag 或外网下载。
- [ ] 修复脚本幂等；退出码 0 只表示脚本完成，最终结果由 Checker 决定。

| 工作人员样例 | 路径 | 预期结果 |
| --- | --- | --- |
| 合法修复 | `examples/fixes/valid/fix.sh` | Fixed |
| 仍有漏洞 | `examples/fixes/still-vulnerable/fix.sh` | StillVulnerable |
| 规则违规 | `examples/fixes/rule-violation/fix.sh` | RuleViolation |
| 服务破坏 | `examples/fixes/service-unavailable/fix.sh` | ServiceUnavailable |
| 非零退出 | `examples/fixes/nonzero/fix.sh` | AwdpPatchFailed |
| 超时 | `examples/fixes/timeout/fix.sh` | AwdpPatchTimeout |

## 5. Checker

- [ ] 使用 `TARGET_HOST` 和唯一内部端口。
- [ ] 在 `TARGET_READY_TIMEOUT_SECONDS` 内有界等待。
- [ ] 同时验证漏洞路径、正常业务路径和禁止绕过路径。
- [ ] 使用 Bearer `NOCTF_CALLBACK_TOKEN` 回调 `NOCTF_CALLBACK_URL`。
- [ ] 只提交 `Fixed`、`StillVulnerable`、`RuleViolation` 或 `ServiceUnavailable`。
- [ ] 成功回调后以 0 退出；不输出 Token、Flag、补丁正文或敏感响应。

## 6. 比赛与本题规则

| 项目 | 比赛默认 / 本题覆盖 | 采用值 |
| --- | --- | --- |
| 轮次长度 | 比赛默认 | 待填写 |
| Break 结算 / 分值 | 待填写 | 待填写 |
| Fix 结算 / 分值 | 待填写 | 待填写 |
| 规则违规 / 服务不可用罚分 | 待填写 | 待填写 |
| Break 错误 / Fix 失败罚分 | 待填写 | 待填写 |
| 先 Break 后 Fix | 待填写 | 待填写 |
| 最大 Break / Fix 次数 | 待填写 | 待填写 |
| 评测派发 | Automatic / ManualBatch | 待填写 |

## 7. 作者测试

- [ ] 正确、错误、重复 Break 和次数上限。
- [ ] 先 Break 后 Fix 门禁。
- [ ] Fixed、StillVulnerable、RuleViolation、ServiceUnavailable。
- [ ] 非零退出、超时、空包、非 gzip、穿越、链接、设备文件和超限包。
- [ ] Milestone / PerRound、暂停、恢复与结束。
- [ ] Worker/Runner 重投幂等，Fix 不产生 Break 或普通 Flag 分数。
- [ ] 一次性目标、Checker、网络和容量全部回收。

执行日期、NoCTF 基线、E2E 结果与日志位置：待填写。

作者：待填写
复核人：待填写
交付日期：待填写
