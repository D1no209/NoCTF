# KoH 后台填写表

新建 KoH 私有模板，复制题面并配置共享 Hill。

| 位置 / 字段 | 样例填写值 |
| --- | --- |
| Runtime kind / allocation / flagSource | Container / Shared / Static |
| 服务 name / image | main / `private/runtime` 推送后的完整引用 |
| CPU / 内存 | 0.1 核 / 64 MiB |
| 内部端口 | 8080、8081 |
| Flag 环境变量名 | 留空 |
| command / arguments / 普通环境变量 | 留空 |
| 公开访问入口 | main / 8080 / `http://{HOST}:{PORT}/` / Participants |
| 控制检查入口 | main / 8081 / `http://{HOST}:{PORT}/control` / isControlCheck=true |
| 操作超时 / egressPolicy | 120 秒 / Isolated |
| TTL | 留空，生命周期由共享 Hill 调度管理 |
| Checker / Patch | 不配置 |
| Flags 列表 | 不手工新增所有队伍答案 |
| 附件策略 | All |

控制入口与公开入口分别配置。8081 只用于平台内部 GET，不添加同端口公开入口。HTTP 控制监听没有额外鉴权，网络访问限制由部署策略保证；公开 8080 也提供可读的控制状态，正式题按需要评审暴露内容。

比赛设置轮询周期和每次控制分，题目可单独覆盖。样例建议 10 秒、每次 10 分；正式数值由赛事负责人决定。配置字段是 PollIntervalSeconds / ControlPointsPerInterval。

正式比赛 Running 自动创建一个共享 Hill，队伍页面读取本队 Control Flag。使用两队轮换写入验证当前控制与计分；暂停应停止观测，结束应回收资源。
