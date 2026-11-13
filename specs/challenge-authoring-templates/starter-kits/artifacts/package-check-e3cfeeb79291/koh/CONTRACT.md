# KoH 出题契约

通用部署见 [出题规范](../challenge-authoring.md)，规则见 [KoH](../game-modes/koh.md)。[模板](starter-kits/koh/README.md) 提供共享文件写入目标和原文控制端点。

## 共享目标

每个已发布比赛题目一个 Shared Hill，所有队攻击同一目标。使用 Container 或 OVA，至少一个 Participants 公开入口。Running 由平台启动，Paused 保持资源并停止控制轮询，Resume 恢复，Finished 回收。

模板 Runtime 为 `Container / Shared / Static`；`Static` 表示 Hill 不接收每队动态环境变量，Control Flag 仍由平台为每队生成。不要为 Hill 配置 `flagEnvironmentVariableName`。

## 公开入口和控制入口

公开入口为 `main:8080`、`http://{HOST}:{PORT}/`、Participants。另添加 `main:8080`、`http://{HOST}:{PORT}/control`、`isControlCheck=true` 的控制入口。平台按实际内部地址展开控制 URL，客户端不获取这个内部地址。

服务把当前控制者的完整 Flag 保存为 UTF-8 字节，GET `/control` 直接返回原文：

```sh
curl --fail --data-binary @control-flag.txt http://TARGET:PORT/claim
curl --fail http://TARGET:PORT/control
```

`control-flag.txt` 必须精确包含本队平台给出的 Control Flag，不附加编辑器换行。示例 `/claim` 故意允许直接抢占；正式题应将写入权限放在实际漏洞利用之后。

模板使用独立 8081 HTTP 管理监听，仅接受 `/control` 的 GET。可把控制入口改成 `main:8081`；该端口无需加入公开访问入口。只有分离监听端口并验收网络策略后，才能声称选手网络无法访问控制端点。

## 轮询返回要求

仅 HTTP 2xx 参与解析。正文使用 UTF-8 原文，1–4096 字节、无 NUL，不包装 JSON、不 Trim、不改大小写、不追加换行。不重定向，不把上一控制者自动当作当前结果。

- 唯一有效 Team Flag：该次获得控制分。
- 2xx 空值、格式错误或无匹配：无人控制。
- 非 2xx、连接失败或超时：记录平台不可用 / 超时事实，本次没有控制分。
- 不同 Team 存在歧义匹配：平台失败，需要处理数据问题。

比赛默认与题目覆盖配置 `PollIntervalSeconds` 和 `ControlPointsPerInterval`。周期必须大于 0，分值非负；显式 0 分有效。每题同时最多一个检查；超时为 `min(interval, 30s)`。暂停和延迟不补造观测，恢复后立即检查。每次成功观测独立计分，封禁队伍按投影规则排除。

KoH 不接受 Flag 提交或 Fix 上传，不配置最大提交次数、RoundDuration 或 Checker 镜像。目标提供被平台轮询的 HTTP 接口即可。

## 发布验收

- [ ] 两队看到相同 Hill 地址，各自只读取本队 Control Flag。
- [ ] 抢占 A → B 后按实际观察切换控制者，分数跟随当前有效 Flag。
- [ ] 空正文、错误值、额外换行、非 UTF-8、NUL、超限、503 和超时不获得控制分。
- [ ] 原文中的 Unicode、空格和合法换行保持字节一致。
- [ ] 暂停不新增观测，恢复继续；错过周期不补分，结束停止并回收。
- [ ] 控制入口使用内部地址，公开业务端口与私有端口按部署策略实际验证。
