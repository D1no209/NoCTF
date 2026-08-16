# AWDP 题目交付单: index-vault

> 本包不包含真实比赛 Token、平台登录凭据、私有仓库密码或生产 Flag。

## 1. 基本信息

| 项目 | 值 |
| --- | --- |
| 题目名称 | `index-vault` |
| 游戏模式 | `AWDP` |
| 方向 | `PWN` |
| 漏洞类型 | 越界读 |
| 推荐题目显示名 | `Index Vault` |
| 推荐内部标识 | `awdp-pwn-index-vault` |

## 2. 题面

服务实现了一个 TCP note vault。合法用户只能读取 0 到 3 号 note：

```text
PING
READ 0
READ 1
READ 2
READ 3
```

漏洞点：原始版本错误允许 `READ 4`，会读到紧跟在 note 数组后的 `FLAG`
环境变量。选手需要通过 Break 提交该 Flag，再上传 Fix 包修复边界检查。

## 3. Break Flag

- 类型：精确匹配。
- 建议测试值：`flag{awdp-index-vault-test}`
- 放置方式：可选外部 Break 靶机以 `FLAG` 环境变量启动。
- 注意：正式比赛请在 NoCTF 题库 Flag 管理中配置，不要把真实 Flag 写进公开附件或日志。

## 4. Runtime 与 Checker

| 项目 | 值 |
| --- | --- |
| Runtime 类型 | Container |
| 目标镜像 | 构建并推送 `target/` |
| 内部端口 | `31337` |
| 对外端口 | 不配置 |
| URL | 不配置 |
| Runtime TTL | `180` 秒 |
| 操作超时 | `90` 秒 |
| Patch 入口 | `fix.sh` |
| Patch 命令 | `["/bin/sh", "{entrypoint}"]` |
| Patch 超时 | `60` 秒 |
| Ready Timeout | `20` 秒 |
| 最大 Fix 上传 | `256 MiB` |
| Checker 镜像 | 构建并推送 `checker/` |
| Checker 超时 | `30` 秒 |

目标容器以非 root 用户 `10001:10001` 运行，`/opt/challenge/bin` 对该用户可写，
因此 Fix 脚本可以替换 `/opt/challenge/bin/pwn-note`。

## 5. Fix 样例

| 名称 | 归档 | 预期 |
| --- | --- | --- |
| 合法修复 | `artifacts/fixes/fixed.tar.gz` | `Fixed` |
| 未修复漏洞 | `artifacts/fixes/still-vulnerable.tar.gz` | `StillVulnerable` |
| 破坏规则 | `artifacts/fixes/rule-violation.tar.gz` | `RuleViolation` |
| 服务不可用 | `artifacts/fixes/service-unavailable.tar.gz` | `ServiceUnavailable` |
| 非零退出 | `artifacts/fixes/nonzero.tar.gz` | `AwdpPatchFailed` |
| 超时 | `artifacts/fixes/timeout.tar.gz` | `AwdpPatchTimeout` |

## 6. 计分建议

| 字段 | 建议值 |
| --- | --- |
| 轮次长度 | `300` 秒 |
| Break 结算 | `Milestone` |
| Break 分值 | `50` |
| Fix 结算 | `Milestone` |
| Fix 分值 | `50` |
| 先 Break 后 Fix | 开启 |
| 最大 Break 次数 | `10` |
| 最大 Fix 次数 | `10` |
| Break 错误罚分 | `0` |
| Fix 失败罚分 | `0` |
| 规则违规罚分 | `100` |
| 服务不可用罚分 | `50` |
| 评测派发 | `Automatic` |

## 7. 作者自测

- [ ] `./scripts/build-fix-packages.sh` 成功生成 6 个 Fix 包。
- [ ] `./tests/smoke.sh` 通过。
- [ ] `fixed.tar.gz` 判定为 `Fixed`。
- [ ] `still-vulnerable.tar.gz` 判定为 `StillVulnerable`。
- [ ] `rule-violation.tar.gz` 判定为 `RuleViolation`。
- [ ] `service-unavailable.tar.gz` 判定为 `ServiceUnavailable`。
- [ ] `nonzero.tar.gz` 以非零退出失败。
- [ ] `timeout.tar.gz` 在平台 Patch 超时内失败。
- [ ] Checker 不输出 Flag、Token 或平台回调地址。
