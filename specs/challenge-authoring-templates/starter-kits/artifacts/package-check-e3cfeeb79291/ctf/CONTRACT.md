# CTF 出题契约

部署与文件规则见 [通用规范](../challenge-authoring.md)。[可运行模板](starter-kits/ctf/README.md) 演示路径穿越读取平台注入 Flag。

## 选择交互

- 静态题：不配置 Runtime，在模板或本场比赛题目添加精确或正则 Flag。选手附件必须实际下载复验。
- 动态容器题：`Container / PerTeam / PerTeam`，选定服务的 `flagEnvironmentVariableName=FLAG`；公开入口 OwnerOnly。服务必须读取平台传入值。
- 多附件随机题：使用 `RandomOnePerTeam`，给每份附件配置正确答案；首次实际下载固定队伍分配，后续不重抽。
- OVA：交付 OVA 与 SHA-256，使用预置静态答案；平台不注入容器环境变量。
- CTF PatchVerification 属于独立实验交互，开启条件、无 Flag 单服务目标和 Fix 协议见 [实验规范](../ctf-patch-verification-experiment.md)。本套 CTF 模板使用普通 FlagSubmission。

## Flag 生命周期

CTF 动态 Flag 按比赛题目及队伍生成，容器启动通过选定变量注入。首次生成后保存，Stop / Reset 不轮换这个值；每次 Reset 仍创建新的 Runtime UUID。Flag 前缀和正文模板在比赛或比赛题目配置，优先使用单题覆盖，然后比赛默认。

提交为单个 `flag`。原文支持 1–4096 UTF-8 字节、无 NUL。相同队伍解题后仍能提交并得到 Correct / Wrong，计分仅取当前有效的首次正确事实。`MaxFlagAttempts=null` 表示无限，配置时为正整数。

## 模板配置

`main` 服务监听 8080，0.1 核、64 MiB，入口 `http://{HOST}:{PORT}/`，OwnerOnly，TTL 3600 秒、操作超时 120 秒。镜像默认用户 10001；启动时从 `FLAG` 写入 `/opt/challenge/private/flag`，移除进程环境中的原变量。缺少变量立即退出。

示例正常接口 `/file?name=welcome.txt`；漏洞利用读取 `../private/flag`。选手附件只包含协议说明；源码、EXP 和 Flag 文件留在私有目录。

## 发布验收

- [ ] 两队获得不同 Flag；本队正确，错误值和另一队值无法完成解题。
- [ ] Stop / Reset 后原 Flag 仍有效，新 Runtime 地址重新读取。
- [ ] 1 字节、4096 字节、Unicode、空格和换行经过服务与 EXP 后字节完全一致。
- [ ] 超限、空值、NUL 被拒绝，运行日志不泄露 Flag。
- [ ] 静态题、随机附件分别验证答案与下载作用域。
- [ ] 分数衰减、血奖、错误罚分、提示、题目时间和闯关按本场规则验收。
- [ ] TTL、续期、停止、结束和启动失败均释放所属资源。
