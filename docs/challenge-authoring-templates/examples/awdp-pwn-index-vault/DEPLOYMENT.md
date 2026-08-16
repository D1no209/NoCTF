# AWDP PWN 示例题部署说明

本文说明如何把 `index-vault` 部署到 NoCTF 测试环境中验证。文档不会保存平台登录凭据、生产 Secret、私有仓库密码或真实比赛 Flag。

## 1. 本地构建镜像

在本目录执行：

```sh
docker build -t noctf-awdp-index-vault-target:local ./target
docker build -t noctf-awdp-index-vault-checker:local ./checker
./scripts/build-fix-packages.sh
./tests/smoke.sh
```

如果要让测试环境 Runner 拉取镜像，需要把两个镜像推送到测试环境可访问的镜像仓库：

```sh
docker tag noctf-awdp-index-vault-target:local REGISTRY/noctf-awdp-index-vault-target:test
docker tag noctf-awdp-index-vault-checker:local REGISTRY/noctf-awdp-index-vault-checker:test
docker push REGISTRY/noctf-awdp-index-vault-target:test
docker push REGISTRY/noctf-awdp-index-vault-checker:test
```

NoCTF 当前允许普通镜像 tag，不要求固定 digest。镜像命名按测试环境已有规范执行。

## 2. 可选 Break 靶机

NoCTF 的 AWDP Fix 验证会创建内部一次性 target；该 target 不提供公网入口。若需要用选手账号完整测试 Break 流程，可以由工作人员在受控主机上额外启动一个攻击靶机。

```sh
export TARGET_IMAGE=REGISTRY/noctf-awdp-index-vault-target:test
export BREAK_FLAG='flag{awdp-index-vault-test}'
export HOST_PORT=33137
docker compose -f deploy/docker-compose.attack.yml.example up -d
```

在作者机器上验证漏洞：

```sh
python tools/exploit.py 127.0.0.1 33137 --expect 'flag{awdp-index-vault-test}'
```

然后在 NoCTF 中创建同值的静态精确 Break Flag：

```text
flag{awdp-index-vault-test}
```

这个可选 compose 文件只用于工作人员测试，不应作为选手附件公开。

## 3. 创建题库模板

在 NoCTF 管理后台中：

1. 打开 **题库管理**，新建题目模板。
2. 游戏模式选择 `AWDP`。
3. 题目标题填写 `Index Vault`。
4. 方向填写 `PWN`。
5. 题面可使用 `NOCTF-DELIVERY.md` 中的简短题面。
6. 添加一个精确匹配静态 Flag，值与可选 Break 靶机的 `BREAK_FLAG` 保持一致。

在 **题目定义** 中通过结构化界面配置，不要手写比赛 ID、队伍 ID、规格 ID 等内部字段：

| 字段 | 值 |
| --- | --- |
| Runtime 类型 | Container |
| Runtime 镜像 | `REGISTRY/noctf-awdp-index-vault-target:test` |
| 内部端口 | `31337` |
| 对外端口 / URL | 留空 |
| 分配方式 | 每队独立 |
| 网络策略 | 隔离 |
| Runtime TTL | `180` 秒 |
| 操作超时 | `90` 秒 |
| Patch 入口 | `fix.sh` |
| Patch 命令 | `["/bin/sh", "{entrypoint}"]` |
| Patch 超时 | `60` 秒 |
| Ready Timeout | `20` 秒 |
| 最大 Patch 上传 | `268435456` 字节 |
| Checker 镜像 | `REGISTRY/noctf-awdp-index-vault-checker:test` |
| Checker 命令 | 留空 |
| Checker 超时 | `30` 秒 |

target 和 checker 都不配置公网端口。Fix 验证时 Runner 会注入 `TARGET_HOST`，checker 会连接 `TARGET_HOST:31337`。

## 4. 添加到比赛

1. 创建或打开一个 AWDP 比赛。
2. 点击 **添加题目**，选择 `Index Vault`。
3. 如需本场比赛自定义显示名，可填写 `PWN: Index Vault`。
4. 建议基础分填写 `100`。
5. 配置 AWDP 规则：
   - Break：`Milestone`，`50` 分。
   - Fix：`Milestone`，`50` 分。
   - 先 Break 后 Fix：开启。
   - 最大 Break 次数：`10`。
   - 最大 Fix 次数：`10`。
   - 规则违规罚分：`100`。
   - 服务不可用罚分：`50`。
   - Break 错误罚分：`0`。
   - Fix 失败罚分：`0`。
   - 评测派发：`Automatic`。

确认 target 和 checker 镜像都能被 Runner 拉取后，再发布并启动测试比赛。

## 5. 平台测试矩阵

使用可丢弃的选手账号和队伍进行测试。

| 步骤 | 操作 | 预期结果 |
| --- | --- | --- |
| 错误 Break | 提交 `flag{wrong}` | Break 错误，不得分 |
| 正确 Break | 提交配置的精确 Flag | Break 正确 |
| 未 Break 先 Fix | 开启先 Break 后 Fix 时先触发 Fix | 拒绝创建 Fix fact |
| 合法修复 | 上传 `artifacts/fixes/fixed.tar.gz` | Fix 正确 |
| 未修复漏洞 | 上传 `artifacts/fixes/still-vulnerable.tar.gz` | Wrong / AwdpFixFailed |
| 规则违规 | 上传 `artifacts/fixes/rule-violation.tar.gz` | Rejected / AwdpViolation |
| 服务不可用 | 上传 `artifacts/fixes/service-unavailable.tar.gz` | Wrong / AwdpServiceDown |
| 非零退出 | 上传 `artifacts/fixes/nonzero.tar.gz` | Wrong / AwdpPatchFailed |
| 超时 | 上传 `artifacts/fixes/timeout.tar.gz` | Wrong / AwdpPatchTimeout |

每次测试后检查：

- Flag 提交生成 `BreakAttempt`，Fix 包生成 `FixAttempt`。
- Fix 不产生 Break 分数，也不产生血榜分数。
- 一次性 target runtime 在评测后停止并释放容量。
- Checker 日志不包含 Flag、回调 Token 或平台凭据。

## 6. 常见问题

如果 Fix 长时间停留在 `Processing`，优先检查 Worker/Runner 日志和 Wolverine 死信队列。常见原因包括镜像不可拉取、Patch 归档非法、Checker 回调失败，或 target 没有在 `31337` 端口变为可访问。

如果 Checker 返回 `ServiceUnavailable`，确认服务监听 `0.0.0.0:31337`，而不是只监听 `127.0.0.1`；同时确认 Fix 没有删除二进制或终止主服务。

如果看似合法的修复返回 `RuleViolation`，先运行本地烟测，并确认 `READ 0` 仍返回 `VALUE:training-service-online`。
