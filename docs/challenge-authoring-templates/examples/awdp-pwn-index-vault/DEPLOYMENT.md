# index-vault 在 NoCTF 中的部署与验收

本文只描述测试环境部署，不保存登录凭据、Registry 密码或比赛 Flag。

## 1. 构建与推送镜像

```sh
docker build -t noctf-awdp-index-vault-target:local ./target
docker build -t noctf-awdp-index-vault-checker:local ./checker
./scripts/build-fix-packages.sh
./tests/invalid-archives.sh
./tests/smoke.sh

docker tag noctf-awdp-index-vault-target:local REGISTRY/noctf-awdp-index-vault-target:test
docker tag noctf-awdp-index-vault-checker:local REGISTRY/noctf-awdp-index-vault-checker:test
docker push REGISTRY/noctf-awdp-index-vault-target:test
docker push REGISTRY/noctf-awdp-index-vault-checker:test
```

NoCTF 接受可信的 tag 或 digest，不强制固定 digest。正式 Break 流程不使用
`deploy/docker-compose.attack.yml.example`；该文件只能做作者离线 smoke。

## 2. 创建 AWDP 题库模板

在题库中新建 `AWDP / PWN` 模板，并按结构化表单配置：

| 字段 | 值 |
| --- | --- |
| Runtime | `Container` |
| Allocation | `PerTeam` |
| FlagSource | `PerTeam` |
| Image | `REGISTRY/noctf-awdp-index-vault-target:test` |
| FlagEnvironmentVariableName | `FLAG` |
| Flag 容量 | 靶机和 EXP 完整支持 1～4096 UTF-8 字节 |
| PortMappings | 容器 `31337`，主机 `0` |
| InternalPorts | `[31337]` |
| URL | `tcp://{HOST}:{PORT}`，`OwnerOnly` |
| MemoryBytes | `268435456` |
| NanoCpus | `500000000` |
| PidsLimit | `128` |
| TtlSeconds | `180` |
| OperationTimeoutSeconds | `90` |
| EgressPolicy | `Isolated` |
| Security | NNP=`true`，Readonly=`false`，NonRoot=`true` |
| CapDrop / CapAdd | `[]` / `[]` |
| PatchEntrypoint | `fix.sh` |
| PatchCommand | `["/bin/sh","{entrypoint}"]` |
| PatchTimeoutSeconds | `60` |
| ReadyTimeoutSeconds | `20` |
| MaximumPatchUploadBytes | `268435456` |
| Checker image | `REGISTRY/noctf-awdp-index-vault-checker:test` |
| Checker timeout | `30` |

不在题库创建静态 Break Flag。平台会在 Player Runtime 创建事务中为比赛题目、队伍和
Runtime UUID 生成精确 Flag，并在 Provider 创建前覆盖注入 `FLAG`。

## 3. 添加到比赛

1. 创建 AWDP 比赛并设置逻辑轮次与默认 Break/Fix 规则。
2. 添加 `Index Vault`，可自定义本场显示名和顺序；Break/Fix 分值在比赛题目规则中配置。
3. 在比赛题目规则中配置本场 Flag 模板与 `RequireBreakBeforeFix`。
4. 确认 Worker/Runner 可以拉取两个镜像，再发布并开始比赛。

公开端口和 URL 只用于 Player attack Runtime。Runner 为 Fix 自动建立
`Purpose=AwdpTarget` 的一次性实例。申请时只绑定队伍；首次成功上传 Patch 时才原子创建并绑定 Fix GameplayFact。Target 忽略公开端口和 URL，Checker 只访问内部
`31337`，验证结束后清理全部资源。

## 4. 双队闭环

使用两支可丢弃队伍执行：

1. 开赛前尝试 Start，确认被拒绝。
2. 开赛后分别创建 Player Runtime，确认 URL 可连接且宿主端口不同。
3. 分别运行 `python tools/exploit.py HOST PORT`，确认得到的动态 Flag 不同。
4. 提交本队 Flag，确认 `BreakAttempt=Correct`；提交外队 Flag，确认不得分。
5. Reset 一队 Runtime，确认新 Flag 生效且旧 Flag 返回 `FlagExpired`。
6. 上传六个 `artifacts/fixes/*.tar.gz`，核对 DefenseSucceeded、ExploitSucceeded、
   ServiceAbnormal、PatchFailed 和 PatchTimeout。
7. 上传九类非法归档，确认在上传或安全解包边界被拒绝且不留下 target。
8. 验证按轮动态分值在暂停时冻结、恢复后继续、结束后冻结。
9. 检查 GameplayFact、排行榜、CompetitionEvent 与 Runtime 状态一致。

## 5. 故障定位

- Player `InvalidConfiguration`：检查 PerTeam、`FLAG`、31337 端口、OwnerOnly URL 以及资源正数。
- Fix 长时间停留 Processing：检查 Wolverine 死信、一次性 target、Patch 退出码和 Checker callback。
- `ServiceAbnormal`：确认服务监听 `0.0.0.0:31337`，Fix 未删除或停止服务，且 `READ 0` 仍返回 `VALUE:training-service-online`。
- Checker 非零：按平台失败处理；日志只能记录阶段和稳定错误，不得打印回调地址或敏感值。
