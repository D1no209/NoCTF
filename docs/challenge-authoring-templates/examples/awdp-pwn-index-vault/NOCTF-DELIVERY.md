# AWDP 题目交付单：index-vault

> 本包不包含平台凭据、私有仓库密码、回调 Token 或真实比赛 Flag。

## 基本信息

| 项目 | 值 |
| --- | --- |
| 显示名 | `Index Vault` |
| 模式 / 方向 | `AWDP` / `PWN` |
| 漏洞 | 越界读 |
| Player 服务端口 | `31337/tcp` |
| Flag 注入 | `PerTeam`，环境变量 `FLAG` |
| Patch 入口 | `fix.sh` |

## 题面摘要

服务允许读取 0～3 号 note。原始版本错误允许 `READ 4`，从而泄露当前队伍、当前
Runtime generation 的动态 Flag。Break 必须从本队 Player 实例中取得；Fix 必须保留
`READ 0` 的业务行为并阻断越界读取。

## Runtime / Checker 配置

| 字段 | 值 |
| --- | --- |
| Runtime / Allocation / FlagSource | `Container` / `PerTeam` / `PerTeam` |
| Image | `REGISTRY/noctf-awdp-index-vault-target:test` |
| FlagEnvironmentVariableName | `FLAG` |
| PortMappings | `31337 -> 0` |
| InternalPorts | `[31337]` |
| UrlTemplate / Exposure | `tcp://{HOST}:{PORT}` / `OwnerOnly` |
| MemoryBytes / NanoCpus / PidsLimit | `268435456` / `500000000` / `128` |
| TtlSeconds / OperationTimeoutSeconds | `180` / `90` |
| EgressPolicy | `Isolated` |
| NoNewPrivileges / ReadonlyRootfs / RunAsNonRoot | `true` / `false` / `true` |
| CapDrop / CapAdd | `["ALL"]` / `[]` |
| PatchEntrypoint / PatchCommand | `fix.sh` / `["/bin/sh","{entrypoint}"]` |
| PatchTimeoutSeconds / ReadyTimeoutSeconds | `60` / `20` |
| MaximumPatchUploadBytes | `268435456` |
| Checker image / timeout | `REGISTRY/noctf-awdp-index-vault-checker:test` / `30` |

不要填写 RuntimeProvider、RunnerPool、TeamId、SpecificationId 或 Flag UUID。Player 使用
公开随机端口和 OwnerOnly URL；Fix target 自动忽略这些公开配置，只让 Checker 通过
隔离网络访问内部 `31337`。

## 规则建议

| 字段 | 建议 |
| --- | --- |
| 轮次 | `300` 秒 |
| Break / Fix | `PerRound 50` / `PerRound 50` |
| RequireBreakBeforeFix | 按比赛需求开启或关闭 |
| 最大 Break / Fix | `10` / `10` |
| 违规 / 服务不可用罚分 | `100` / `50` |
| 错误 Break / Fix 失败罚分 | `0` / `0` |
| 派发 | `Automatic` |

本场比赛的 Flag 头、正文模板和 Leet 选项在比赛题目规则中配置。题库只声明 `FLAG`
这个注入位置，不保存比赛专属前缀或具体 Flag。

## 交付验收

- [ ] 两支队伍的 Player Runtime、端口与动态 Flag 相互独立。
- [ ] Reset 后新 generation Flag 可用，旧 Flag 变为 `FlagExpired`。
- [ ] 外队 Flag 被拒绝并生成工作人员可见证据。
- [ ] 六类 Fix 结果与九类非法归档全部符合预期。
- [ ] Checker 只回调一次，回调失败或非零退出收敛为平台失败。
- [ ] Fix 完成后 target、checker、网络、工作目录、端口和容量已回收。
- [ ] 日志和响应不含 Flag、Token 或 callback URL。
