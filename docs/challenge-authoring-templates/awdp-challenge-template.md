# AWDP 题目交付模板

> 复制本文件后填写。Break 是 Flag 提交，Fix 是独立 `tar.gz` 归档；不得把 Fix 建模为 Flag。

## 1. 题目元信息

| 项目 | 内容 |
| --- | --- |
| 题目名称 | 待填写 |
| 题目方向 | Pwn / Web / Misc / 其他：待填写 |
| 题目 Owner | 待填写 |
| 题目 Manager | 待填写 |
| 模板可见性 | Private / Shared |
| 目标内部端口 | 待填写，只能有一个 |
| 预计目标内存 | 待填写 MiB |
| 预计目标 CPU | 待填写 |
| 预计目标 PID | 待填写 |
| 目标镜像 | 待填写，可用 tag 或 digest |
| Checker 镜像 | 待填写，可用 tag 或 digest |
| Player URL | 待填写，例如 `tcp://{HOST}:{PORT}` |
| Flag 环境变量 | `FLAG` 或题目实际采用名称 |

## 2. 漏洞、Break 与修复判据

### 2.1 漏洞说明

待填写：漏洞位置、触发条件、利用步骤和正确 Break Flag 的来源。

### 2.2 Break Flag

| 项目 | 内容 |
| --- | --- |
| 匹配方式 | 精确匹配 |
| Flag 管理位置 | 平台按比赛题目、队伍和 Runtime UUID 自动生成 |
| 比赛专属模板 | 在 CompetitionChallenge 规则中配置前缀、正文与 Leet 选项 |
| 注入位置 | 题库 Runtime 定义中的环境变量名，不填写 Flag 明文 |
| 动态 Flag 容量 | 目标服务、漏洞读取路径与作者 EXP 必须完整支持 1～4096 UTF-8 字节，不能假定 `flag{GUID}` 的固定长度 |
| 是否包含在附件/镜像公开层 | 必须为否 |
| 正确性验证 | 待填写 |

### 2.3 修复成功判据

待填写：漏洞路径必须失效，同时哪些正常业务行为必须继续可用。

### 2.4 服务异常与禁止绕过判据

待填写：哪些正常业务交互失败、超时、崩溃、固定返回、删除功能、阻断 Checker、伪造状态或使用题目明确禁止的方法时，应统一视为 `ServiceAbnormal`。

## 3. Player Runtime 与一次性 Fix Target

- [ ] Runtime 为 Container，不是 Compose 或 OVA。
- [ ] 分配语义为 `PerTeam`，Flag 来源为 `PerTeam`。
- [ ] Player 使用 host port `0` 和 OwnerOnly URL。
- [ ] Player 有 TeamId，Purpose 为 `Player`。
- [ ] Fix Target 绑定申请队伍、Purpose 为 `AwdpTarget`；申请时没有 GameplayFact，首次成功上传 Patch 时才原子绑定唯一 Fix GameplayFact。
- [ ] Fix Target 忽略 Player 的公网端口和 URL。
- [ ] `InternalPorts` 恰好包含一个真实监听端口。
- [ ] 服务监听 `0.0.0.0`，Checker 可以从隔离网络访问。
- [ ] 服务读取、漏洞泄露与作者 EXP 完整保留平台注入的 1～4096 UTF-8 字节 Flag，不截断、不固定缓冲长度。
- [ ] 每次在全新容器中都能稳定复现漏洞。
- [ ] Fix 需要写入的路径对运行用户可写。
- [ ] 不依赖宿主路径、Docker Socket、其他队伍环境或外网。

配置摘要：

| 项目 | 内容 |
| --- | --- |
| 目标镜像 | 待填写 |
| 启动命令 argv | 待填写或“镜像默认入口” |
| 内部端口 | 待填写 |
| Player PortMapping | 容器端口：待填写；HostPort：`0` |
| Player URL / Exposure | 待填写 / `OwnerOnly` |
| FlagSource / 环境变量 | `PerTeam` / 待填写 |
| 内存 / CPU / PID | 待填写 |
| Runtime TTL | 待填写，必须覆盖补丁、就绪和 Checker 总耗时 |
| 操作超时 | 待填写 |
| no-new-privileges | 开启 / 关闭，原因：待填写 |
| 只读根文件系统 | 通常关闭；采用值与原因：待填写 |
| 非 root | 开启 / 关闭，镜像 USER：待填写 |
| CapDrop / CapAdd | 待填写 |

## 4. Fix 合约

| 项目 | 默认值 | 采用值 |
| --- | --- | --- |
| 补丁入口 | `fix.sh` | 待填写 |
| 补丁命令 | `["/bin/sh", "{entrypoint}"]` | 待填写 |
| 补丁超时 | 60 秒 | 待填写 |
| 就绪等待 | 30 秒 | 待填写 |
| 最大上传 | 256 MiB | 待填写，不能超过 1 GiB |

Fix 包要求：

- [ ] gzip 压缩的 tar（GNU Tar、USTAR、PAX、V7 均可），无需指定 `--format=ustar`。
- [ ] 入口文件位于归档中精确配置的位置。
- [ ] 没有绝对路径、`..`、符号链接、硬链接、设备文件或重复路径。
- [ ] 没有依赖平台地址、Token、真实 Flag 或外网下载。
- [ ] 修复脚本幂等，重复在全新目标上执行结果一致。
- [ ] 退出码 0 只表示脚本执行完成，最终结果仍由 Checker 决定。

随题目交付但不公开给选手的测试包：

| 样本 | 文件位置 | 预期结果 |
| --- | --- | --- |
| 合法修复 | 待填写 | DefenseSucceeded |
| 漏洞仍存在 | 待填写 | ExploitSucceeded |
| 服务异常或禁止绕过 | 待填写 | ServiceAbnormal |
| 脚本非零退出 | 待填写 | AwdpPatchFailed |
| 脚本超时 | 待填写 | AwdpPatchTimeout |
| 非法归档 | 待填写 | 上传阶段拒绝 |

## 5. Checker

| 项目 | 内容 |
| --- | --- |
| Checker 镜像 | 待填写 |
| 启动命令 argv | 待填写或“镜像默认入口” |
| 目标内部端口 | 待填写 |
| Checker 超时 | 待填写 |
| EXP 利用成功判据 | 待填写 |
| 防御成功判据 | 待填写 |
| 服务异常判据 | 待填写 |

Checker 必须完成：

- [ ] 使用 `TARGET_HOST` 和内部端口访问一次性目标。
- [ ] 在 `TARGET_READY_TIMEOUT_SECONDS` 内有界等待。
- [ ] 把 EXP 作为子进程执行并捕获失败/崩溃，不让 EXP 崩溃直接结束 Checker 主进程。
- [ ] EXP 后继续验证正常业务路径；服务异常优先级高于 EXP 结果。
- [ ] 使用 Bearer `NOCTF_CALLBACK_TOKEN` 回调 `NOCTF_CALLBACK_URL`。
- [ ] 只回调 `ExploitSucceeded`、`DefenseSucceeded` 或 `ServiceAbnormal`。
- [ ] 回调成功后以 0 退出；异常时返回非零。
- [ ] 日志不包含 Flag、Token、补丁正文或敏感响应。

## 6. 比赛与本题规则

### 6.1 比赛默认值

| 项目 | 建议初值 | 采用值 |
| --- | ---: | ---: |
| 轮次长度 | 300 秒 | 待填写 |
| Break 分值曲线 | 默认曲线 | 待填写 |
| Fix 分值曲线 | 默认曲线 | 待填写 |
| Flag 错误罚分 | 0 | 待填写 |
| EXP 利用成功罚分 | 0 | 待填写 |
| 服务异常罚分 | 0 | 待填写 |
| 先 Break 后 Fix | 开启 | 待填写 |
| 最大 Break 次数 | 10 | 待填写 |
| 最大 Fix 次数 | 10 | 待填写 |
| 评测派发 | Automatic | 待填写 |

### 6.2 本题覆盖

| 字段 | 继承比赛 / 覆盖 | 覆盖值与原因 |
| --- | --- | --- |
| Break / Fix 分值曲线 | 待填写 | 待填写 |
| 先 Break 后 Fix | 待填写 | 待填写 |
| Break / Fix 次数 | 待填写 | 待填写 |
| 各类罚分 | 待填写 | 待填写 |
| 评测派发 | 待填写 | 待填写 |

## 7. 作者测试记录

| 场景 | 结果 | 证据或备注 |
| --- | --- | --- |
| 正确、错误、重复 Break | 待测试 | 待填写 |
| Break 次数上限 | 待测试 | 待填写 |
| 先 Break 后 Fix 门禁 | 待测试 | 待填写 |
| 合法 Fix 完整闭环 | 待测试 | 待填写 |
| 漏洞仍存在 | 待测试 | 待填写 |
| EXP 仍可利用 | 待测试 | 待填写 |
| 服务异常 | 待测试 | 待填写 |
| 补丁非零退出与超时 | 待测试 | 待填写 |
| 非法和恶意归档拒绝 | 待测试 | 待填写 |
| 按轮动态分值 | 待测试 | 待填写 |
| 暂停、恢复、结束 | 待测试 | 待填写 |
| Worker/Runner 重投幂等 | 待测试 | 待填写 |
| 一次性资源和容量回收 | 待测试 | 待填写 |

完整 E2E：

```powershell
dotnet run --file backend/tests/e2e.cs -- --mode awdp --suite full
```

执行日期、基线提交、结果和日志位置：待填写。

## 8. 交付复核

- [ ] Break 与 Fix 使用不同接口和事实类型。
- [ ] 题库模板没有比赛 ID、队伍 ID、Runner Pool 或平台凭据。
- [ ] Player Runtime 使用随机公网端口和 OwnerOnly URL；Fix Target 没有公网入口。
- [ ] 动态 Flag 只由平台生成，镜像不烘焙真实或测试 Flag。
- [ ] 合法 Fix、失败 Fix、违规 Fix、服务破坏和恶意归档均有测试资产。
- [ ] Checker 同时验证漏洞和业务功能，回调及日志已脱敏。
- [ ] Patch、Checker、目标镜像和题面版本相互对应。
- [ ] 第二名工作人员已复核完整 E2E 结果。

作者签名：待填写  
复核人签名：待填写  
交付日期：待填写
