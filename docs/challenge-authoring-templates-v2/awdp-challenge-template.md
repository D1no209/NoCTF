# AWDP V2 出题契约：向 Checker 提供 Fix 输入包

## 适用版本与开关

- 平台版本：NoCTF `0.1.0-alpha.142` 及更新版本。
- 前端“游戏模式”选择 AWDP；普通出题人按 [后台逐项填写表](starter-kits/awdp/platform/CONFIGURATION.md) 操作，不需要填写格式版本号。
- 在题库模板的“模式定义 → Fix 一次性验证 Checker”中打开“向 Checker 提供 Fix 包”，不是比赛计分页的开关。
- 该开关关闭时，旧 Checker 的镜像、启动命令、环境变量、只读根文件系统、网络和回调
  行为保持不变。只为能够处理不可信选手输入的 Checker 开启此功能。

## 私有资料与公开附件

每个题目模板使用以下两个目录划分交付内容：

- `private/`：完整题目源码、靶机与 Checker 构建文件、PoC/EXP、标准修补和内部验收数据。
- `attachment/`：经过审核的选手附件、公开源码副本以及待选手填写的 Patch 包模板。

参考模板把内部修补样例保存在 `private/examples/fixes/`，选手 Patch 模板保存在
`attachment/patch-template/`。正确修补样例不得自动复制到公开目录。只上传
`scripts/build-attachments.sh` 生成并经过检查的发布文件，不上传整个题目仓库。

## 固定输入路径与执行顺序

开启开关后，平台在 Checker 入口启动之前准备好 `/noctf/fix`，其中是选手上传包的规范化内容：

```text
选手包内路径                 Checker 内路径
fix.sh                       /noctf/fix/fix.sh
src/server.py                /noctf/fix/src/server.py
```

入口文件名在前端“补丁入口”中填写，例如 `fix.sh`；此时对应完整路径为
`/noctf/fix/fix.sh`。`/noctf/fix` 是平台固定目录，不能通过题目设置改为其他路径。
选手归档必须直接包含入口文件，不要再套一层 `patch-template/` 目录。

正常顺序为：准备并在防御靶机执行 Patch → 将同一规范化输入的副本注入 Checker → 启动 Checker。
Checker 不需要自行下载或解压原始 `.tar.gz`，也不要自行假定当前工作目录就是 `/noctf/fix`。

这里不是防御靶机修补后的文件系统快照。靶机与 Checker 是两个独立容器；修改 Checker 内的文件
不会修改真实靶机。平台也不会因为提供输入包就自动在 Checker 内执行一遍 Patch。

## 独立基线重放与安全边界

如果需要比较真实文件差异，Checker 镜像应包含与靶机一致的独立基线。参考流程为：

1. 将基线复制到 `/tmp/noctf-fix-work`，保留修补前的副本或文件清单。
2. 在受控隔离环境中，把 `/noctf/fix/{patchEntrypoint}` 应用于这个副本。
3. 比较修补前后的文件差异，并对副本中的程序进行功能检查。
4. 仍通过平台隔离网络检查真实防御靶机的正常业务和漏洞利用结果。

这类流程会让 Patch 在靶机和 Checker 的独立副本上各执行一次，应避免依赖不可控外网、当前时间
或随机状态。Checker 可从对应 Target 镜像复制基线，也可在构建时嵌入完全一致的基线。

Fix 包始终是不可信输入。教学样例中的关键词扫描不是安全沙箱，不能阻止任意代码读取环境变量或
同容器文件。真实比赛若要重放选手脚本，必须另行实现明确的隔离边界，防止访问 Checker 源码、
基线保护区和回调凭证；不要把未经加固的教学脚本直接用于不可信参赛者。

不得把 Checker 的基线、差异文件、回调令牌或判定结果传给靶机；不得新增靶机检查端口，也不得
让 Checker 监听靶机可访问的服务端口。

## 运行环境约定

- `TARGET_HOST` 是真实靶机的内部地址，服务端口应与题目实际监听端口一致。
- `TARGET_READY_TIMEOUT_SECONDS` 是就绪等待预算；不要无限重试。
- `NOCTF_CALLBACK_URL` 与 `NOCTF_CALLBACK_TOKEN` 仅供 Checker 上报结果，不得打印或交给选手输入。
- 不要假定 Checker 中存在动态 `FLAG` 环境变量；靶机的 Flag 注入与 Checker 输入是不同契约。
- 镜像必须包含脚本用到的 `wget`、Python 等依赖，不能把命令缺失、超时或写文件失败当作“漏洞已修复”。
- 镜像应声明平台可验证的非零数字用户，例如 `USER 10001:10001`，并预先设置工作目录权限。
- 未开启 Fix 输入时 Checker 根文件系统只读；开启后也仍受非 root、能力限制和网络隔离约束，
  不得假设可以随意写入 `/`。中间文件应放在已确认可写的工作目录。

## 判定与回调

只使用既有回调结果，优先级如下：

1. `ServiceAbnormal`：正常业务、基线重放完整性或明确的反绕过规则不满足。
2. `ExploitSucceeded`：业务正常，但真实靶机仍可被成功利用。
3. `DefenseSucceeded`：正常业务通过，并且漏洞利用确实失败。

基础设施、依赖或回调异常不等于防御成功；应保留非零退出，让平台识别故障。
只有成功投递回调之后才以退出码 `0` 结束。提供者或输入准备故障属于平台失败，不应伪装成选手的
`PatchFailed`。完整配置见 [平台配置说明](starter-kits/awdp/platform/CONFIGURATION.md)。
