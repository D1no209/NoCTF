# 测试与验收

## 框架

统一 TUnit；NSubstitute 可用于纯单元测试。关系约束、事务、锁、Wolverine、Redis 不能用 Substitute/EF InMemory 证明，使用 Testcontainers PostgreSQL/Redis 与真实 Wolverine persistence。

## 单元测试

必须覆盖：

- Competition 状态机、EffectiveRunningTime、AWD/AWDP Round；
- Flag window、scope、SpecificationId、Round Guid 编码；
- Flag template、TEAMHASH、SafeLeetV1 固定表/强制变化/碰撞；
- CTF DynamicExpresso 默认/覆盖/边界/异常、assignment 禁用、Reflection 不可达、未知 identifier/额外类型拒绝、decimal rounding/overflow；
- 四模式投影、血奖、罚分、所有 tie-break；
- Submission 次数、预占释放、重判版本栅栏；
- 权限矩阵、Team/Competition UUID 数组不变量；
- URL template 与 Runtime 配置验证、Queued/Reset replacement/ProcessingVersion、CheckerSequence callback hash 幂等；
- Problem code/result mapping。

## PostgreSQL 集成

验证真实：部分唯一索引、XOR/check、uuid[]/GIN、jsonb、软删除 query filter、advisory lock 并发、Submission 次数竞争、Manual/rejudge 500 条 SKIP LOCKED drain、重判事件原子替换、Hint 并发扣分、RandomOne 固化、Wolverine Inbox/Outbox/DLQ。

不得使用 EF InMemory 替代这些测试。

## API 测试

使用 FastEndpoints Testing/Factory：

- 每个 Endpoint ExecuteAsync/集成请求的 Typed Result 类型、body/status/content-type；
- Validator 400、401/403/404 隔离、409 revision/state、422 archive、429 Retry-After；
- OpenAPI 声明与实际 union；
- multipart Patch/Attachment、Refresh Cookie 精确 `/api/v1/auth` Path/SameSite/Origin、cursor 签名、应用不产生 413；
- 玩家不能读取其他队/原始正确 Flag；管理权限矩阵。

## Runner Contract

- Docker/Kubernetes Container lifecycle/labels/security/resources/URL mapping；
- Compose YAML 拒绝项、Docker Compose 与 Kompose manifests 后置验证；
- Libvirt Provider OVA SHA-256、tar traversal、OVF 多 VM/资源总预算、routed subnet、
  Guest Agent、VmId URL expansion、幂等 cleanup、Pool inventory dispatch 与
  数据库 assignment 精确 orphan reconciliation；
- 网络隔离与 orphan reaper；
- AWDP target/checker callback、timeout、清理；
- AWD raw command injection 与轮次截止重试。

Provider 测试可按环境标记，但 Release 流水线必须至少在受控 Runner 环境执行。

Docker Integration 默认在 Docker 不可用时明确 skipped；发布门禁必须设置
`NOCTF_REQUIRE_DOCKER_INTEGRATION=true`，使不可用直接失败。

Kubernetes Compose 真实集成要求一个启用 CoreDNS 与 NetworkPolicy enforcement 的测试
集群，并设置：

```text
NOCTF_KUBERNETES_INTEGRATION=true
NOCTF_KOMPOSE_PATH=/usr/local/bin/kompose
```

测试会创建唯一临时 Namespace，真实执行 Kompose 与 Kubernetes API，验证 Compose
短名 DNS、`publishNotReadyAddresses`、同 Runtime 互通、跨 Runtime 拒绝、动态 NodePort
和删除收敛，然后删除该 Namespace。若 API endpoint 是本机地址，还必须将它加入
`NO_PROXY`，避免 kubectl/.NET Kubernetes client 经 HTTP proxy 访问。

## 端到端

每种模式至少一条真实依赖流程：

- CTF：注册/队伍/附件或 Runtime/Flag/血奖/Hint/重判；
- AWD：加固/轮换/批量攻击/重复/服务 Up-Down/轮末投影；
- AWDP：Break/Upload/Trigger/Patch/Checker/重判；
- KoH：共享 Runtime/Control Flag/四种观测/暂停恢复。

## CI 门槛

Release 必过：build、analyzer、format、TUnit、Testcontainers integration、OpenAPI drift、migration drift/空库 apply、无 Penetration/RuntimeOperation/旧 API contract 架构测试。
