# NoCTF V2 出题交付检查清单

## 前端填写资料

- [ ] [后台逐项填写表](platform/CONFIGURATION.md) 已填写本题的最终值，镜像名和端口不留给运维猜测。
- [ ] [statement.md](statement.md) 已写好最终题面，不含私有答案或部署凭证。
- [ ] [部署交接单](private/DEPLOYMENT.md) 已填写负责人、镜像交付方式、正常业务和验收结论。
- [ ] [Hint 填写单](private/HINTS.md) 明确填写“有”或“无”；有提示时正文、扣分和发布时间已审核。
- [ ] 内存、CPU 和上传上限均按前端显示单位填写，不要求换算为后台数值。

## 内容分类

- [ ] 题目源码、PoC/EXP、Checker、标准修补与内部验收样例均放在 `private/`。
- [ ] `attachment/` 仅包含审核后的公开附件和待填写的 Patch 包模板。
- [ ] 公开源码已显式复制并脱敏，不通过符号链接引用私有目录。
- [ ] 使用 `scripts/build-attachments.sh` 生成发布文件，并检查包内没有私有资料、凭证和正确修补。
- [ ] 没有把整个题目仓库、`private/artifacts/` 或 `platform/` 当作选手附件上传。
- [ ] 已运行 `tests/packages.sh`，确认公开包与内部样例包严格分离。

## 平台配置与运行

- [ ] “游戏模式”选择 AWDP；仅为支持输入包的 Checker 打开“向 Checker 提供 Fix 包”。
- [ ] “补丁入口”填 fix.sh；“补丁应用命令”按参数分行，并且恰好一行是 {entrypoint}。
- [ ] Patch 模板直接打包入口文件，不包含额外顶层目录。
- [ ] 先构建 Target，再构建 Checker，确保其中保存的是同版本的独立靶机基线。
- [ ] Checker 镜像默认声明非零数字 UID/GID；确需 root 时经审核在模式定义中打开“允许 Checker 以 root 运行”。实际监听端口、平台端口映射和 Checker 访问端口一致。
- [ ] Checker 所需的 Python、wget 等程序实际存在，工作目录权限与只读限制经过验证。
- [ ] Checker 启动时 `/noctf/fix/fix.sh` 已存在，归档由 Runner 下载并规范化一次后分别注入。
- [ ] 没有假定 Checker 自动继承靶机的动态 Flag 环境变量。
- [ ] 回调成功后才退出 `0`；依赖、网络、输入准备异常不能误报防御成功。

## 安全与验收

- [ ] 日志不包含 Fix 下载凭证、真实 Flag 或回调令牌。
- [ ] 没有 Docker socket、宿主机挂载、公开 Checker 端口或靶机检查端口。
- [ ] 不可信 Fix 的重放具有独立隔离边界，不能读取 Checker 凭证；关键词扫描不作为沙箱替代品。
- [ ] 保留资源限额、`no-new-privileges`、能力限制、隔离网络和有界超时。默认非 root；允许 root 不代表特权容器或宿主机权限。
- [ ] 已在可丢弃环境用内部样例验证 `DefenseSucceeded`、`ExploitSucceeded`、`ServiceAbnormal`。
- [ ] 正式出题前替换所有教学镜像名、教学数据与判定规则，不在生产运行模板验收脚本。
