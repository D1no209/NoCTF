# AWDP 后台填写表

新建 AWDP 私有模板，填写题面与 Web 方向。分别保存运行环境和模式定义；镜像引用由作者构建推送后填写。

| 位置 / 字段 | 样例填写值 |
| --- | --- |
| Runtime kind / allocation / flagSource | Container / PerTeam / PerTeam |
| 命名服务 name / image | main / `private/runtime` 推送后的完整引用 |
| 服务 CPU / 内存 | 0.1 核 / 64 MiB |
| Flag 环境变量名 | FLAG |
| command / arguments / 普通环境变量 | 留空，保留镜像入口 |
| 内部端口 | 8080，恰好一项 |
| Runtime 级访问入口 | main / 8080 / `http://{HOST}:{PORT}/` / OwnerOnly |
| TTL / 操作超时 / egressPolicy | 3600 秒 / 120 秒 / Isolated |
| Patch 入口 | fix.sh |
| Patch 命令参数列表 | 第一个参数 `/bin/sh`，第二个参数 `{entrypoint}` |
| Patch 超时 / 就绪等待 | 10 秒 / 5 秒 |
| Fix 上传上限 | 1 MiB；如果界面使用字节则填 1048576 |
| Checker image | `private/checker` 推送后的完整引用 |
| Checker command / environment | 留空，平台注入目标地址与回调凭据 |
| Checker 超时 | 30 秒 |
| 向 Checker 提供 Fix 包 | 可以开启；本样例只检查 fix.sh 文件形状 |
| 附件 | All，仅附件包中的 README 和 patch-template |
| Flags 列表 | 不新增静态答案 |

AWDP 只配置一个服务。访问入口必须是 OwnerOnly，全部指向 main:8080。一次性 Fix 目标由平台根据本题定义创建，移除公开入口；无需作者创建另一套外部靶机。

比赛及单题填写 Break / Fix 曲线、次数、前置 Break、罚分、轮次与派发方式。Flag 前缀和正文模板也在本场规则配置。样例修补包小于 1 MiB；换题后依据实际体积和展开预算重新审核。

公开 `attachment/patch-template` 保留待修补漏洞。标准答案及失败包位于 `private/fixes`，只用于内部验收。完成实际两队攻击、Fix 和清理后发布。
