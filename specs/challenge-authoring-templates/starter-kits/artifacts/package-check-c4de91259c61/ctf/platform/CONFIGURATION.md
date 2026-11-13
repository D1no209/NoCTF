# CTF 后台填写表

新建 CTF 私有模板，填写题名、Web 方向和 `statement.md`。保存后分别编辑运行环境、模式定义和附件。

| 位置 / 字段 | 样例填写值 |
| --- | --- |
| 模式交互 | FlagSubmission |
| Runtime kind / allocation / flagSource | Container / PerTeam / PerTeam |
| 命名服务 | main |
| 服务镜像 | 将构建的 `private/runtime` 推送后填写完整引用 |
| 服务 CPU / 内存 | 0.1 核 / 64 MiB |
| command / arguments / 普通环境变量 | 留空，保留镜像入口 |
| Flag 环境变量名 | FLAG |
| 内部端口 | 8080 |
| 访问入口 serviceName / containerPort | main / 8080 |
| 访问入口模板 / exposure | `http://{HOST}:{PORT}/` / OwnerOnly |
| TTL / 操作超时 | 3600 秒 / 120 秒 |
| egressPolicy | Isolated，网络能力以部署验收为准 |
| Checker | 普通 Flag 题不配置 |
| Flags 列表 | 动态容器无需手工新增答案 |
| 附件策略 | All，上传审核过的附件包 |

服务列表只声明内部端口，公开入口在 Runtime 级单独添加；无需选择宿主端口。

在 CTF 比赛引用模板，设置标题、顺序、时间、发布状态、提示和可选规则覆盖。Flag 模板、题值曲线、结算方式、尝试次数与罚分由赛事负责人审核。先保持未发布，完成实际选手验收后发布。

静态题：关闭 Runtime，维护精确或正则 Flag，上传题目附件。模板静态答案与本场专属答案分别使用正确作用域。随机附件使用 RandomOnePerTeam 并为每个候选配置关联答案。
