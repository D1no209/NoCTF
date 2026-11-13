# KoH 出题契约

通用部署见 [出题规范](../challenge-authoring.md)，规则见 [KoH](../game-modes/koh.md)。[模板](starter-kits/koh/README.md) 提供共享文件写入目标和原文控制端点。

## 共享目标

每个已发布比赛题目一个 Shared Hill，所有队攻击同一目标。使用 Container，至少一个 Participants 公开入口。Running 由平台启动，Paused 保持资源并停止控制轮询，Resume 恢复，Finished 回收。

模板 Runtime 为 `Container / Shared / Static`；`Static` 表示 Hill 不接收每队动态环境变量，Control Flag 仍由平台为每队生成。不要为 Hill 配置 `flagEnvironmentVariableName`。

## 公开入口和控制入口

公开入口为 `main:8080`、`http://{HOST}:{PORT}/`、Participants。在基本信息启用 Control，再配置 Control 镜像、命令、环境变量、目标服务 `main` 和执行超时。容器读取 `NOCTF_TARGET_HOST`，自行访问目标，不填写平台请求 URL。

服务把当前控制者的完整 Flag 保存为 UTF-8 字节，GET `/control` 直接返回原文：

```sh
curl --fail --data-binary @control-flag.txt http://TARGET:PORT/claim
curl --fail http://TARGET:PORT/control
```

`control-flag.txt` 必须精确包含本队平台给出的 Control Flag，不附加编辑器换行。示例 `/claim` 故意允许直接抢占；正式题应将写入权限放在实际漏洞利用之后。

模板使用独立 8081 HTTP 管理监听，仅接受 `/control` 的 GET。样例 Control 容器自行读取该端口，无需加入公开访问入口。只有分离监听端口并验收网络策略后，才能声称选手网络无法访问控制端点。

## 轮询返回要求

Control 容器向 `NOCTF_CALLBACK_URL` 发送 Bearer `NOCTF_CALLBACK_TOKEN` 授权的 JSON，包含 `executionState`、`result`、`controllerIdentifier`、`executionFailure` 和 `observedAt`。标识精确匹配，不 Trim、不改大小写、不追加换行。样例只读取目标 HTTP 2xx UTF-8 原文，不跟随重定向。

- 唯一有效 Team Flag：该次获得控制分。
- 明确空值：报告 `Uncontrolled`；未知标识由平台记为无人控制；非法编码或超长标识报告 `Indeterminate` 和稳定故障码。
- 非 2xx、连接失败或超时：记录平台不可用 / 超时事实，本次没有控制分。
- 不同 Team 存在歧义匹配：平台失败，需要处理数据问题。

比赛默认与题目覆盖配置 `PollIntervalSeconds` 和 `ControlPointsPerInterval`。周期必须大于 0，分值非负；显式 0 分有效。容器执行受配置超时和任务期限约束。暂停和延迟不补造观测，恢复后立即检查。每次成功观测独立计分，封禁队伍按投影规则排除。

KoH 不接受 Flag 提交或 Fix 上传，不配置最大提交次数、RoundDuration 或 Check 能力。Control 镜像由 Runner 执行；容器正常结束、签名回调和辅助资源清理全部完成后，本次观察才完成。

## 发布验收

- [ ] 两队看到相同 Hill 地址，各自只读取本队 Control Flag。
- [ ] 抢占 A → B 后按实际观察切换控制者，分数跟随当前有效 Flag。
- [ ] 空正文、错误值、额外换行、非 UTF-8、NUL、超限、503 和超时不获得控制分。
- [ ] 原文中的 Unicode、空格和合法换行保持字节一致。
- [ ] 暂停不新增观测，恢复继续；错过周期不补分，结束停止并回收。
- [ ] 控制入口使用内部地址，公开业务端口与私有端口按部署策略实际验证。
