# KoH 共享文件控制题模板

所有队伍访问同一个 Hill，通过 POST `/claim` 写入本队 Control Flag。平台通过内部 GET `/control` 精确读取当前字节。模板故意开放抢占接口，正式题目应将该写入动作放在真实漏洞利用之后。

1. 编辑题面、附件说明、后台填写表与私有交接单。
2. 在 `starter-kits` 执行 `python tools/verify.py --mode koh`。
3. 构建并推送 `koh/private/runtime`，填写正式镜像引用。
4. 执行 `python tools/package.py --mode koh`。
5. 使用两队在隔离比赛交替占领，验证轮询、当前控制者、暂停恢复、计分与回收。

独立解包后在 `koh` 目录执行 `python tools/verify.py --mode koh`。示例在 8080 提供业务和控制读取，另在 8081 仅提供控制读取；后台推荐使用未公开的 8081 控制入口。模板检查确认 8081 未发布宿主端口。部署网络隔离需另外验收。

Control Flag 由平台按队伍生成，通过玩家页面读取；Hill 不获取全部队伍名单，不接收每队动态 `FLAG` 环境变量。请求 `/claim` 时使用二进制原文，避免附加换行。
