# NoCTF 四种赛制出题交付包

本包包含 CTF、AWD、AWDP、KoH 的完整作者模板、选手附件和 AWDP 内部修补样本。先解包所需模式的 `noctf-<mode>-authoring-kit.tar.gz`，进入该模式目录，阅读 README、后台填写表和部署交接单。

## 工作人员完整包

每个完整包内含：

- `statement.md`：可复制到后台的题面。
- `attachment/`：公开附件白名单。
- `private/runtime/`：真实漏洞服务与 Dockerfile。
- `private/checker/`：AWD / AWDP 私有检查器。
- `private/fixes/`：AWDP 内部正确修补及失败样本。
- `platform/CONFIGURATION.md`：后台字段、镜像、资源、端口和开关填写表。
- `private/DEPLOYMENT.md`、`private/HINTS.md`、`NOCTF-DELIVERY.md`：部署、提示与验收记录。
- `tools/`：跨平台打包与真实 Docker 检查工具。

需要 Python 3.12+、Docker CLI 和 Linux Docker Engine。例如解包 AWDP 后在 `awdp` 目录执行：

```sh
docker context show
python tools/verify.py --mode awdp
python tools/package.py --mode awdp
```

源码中的完整总规范位于 `specs/challenge-authoring.md`，四种赛制的详细契约位于 `specs/challenge-authoring-templates/`。独立模板中的配置表与交接文档包含本样例全部部署值。

## 选择赛制

| 模式 | 样例目标 | 必须核实 |
| --- | --- | --- |
| CTF | 按队文件读取服务 | FLAG 注入、精确字节、两队作用域、重置复用答案 |
| AWD | 按 Store / FlagId 保存记录 | 当前写入、历史读取、结构化回执、跨轮接收 |
| AWDP | 文件解析器漏洞与实际修补 | 新 Runtime Flag、独立 Fix 目标、正常业务和漏洞探测 |
| KoH | 共享控制记录 | 两队抢占、原文 Control Flag、内部控制端口和计分 |

镜像由作者构建并推送到 Runner 可访问的仓库。题库保存服务与技术定义，比赛及题目配置本场计分与 Flag 外观，部署统一选择 Provider、Runner Pool 和网络。

## 公开附件与内部 Fix

`noctf-<mode>-attachments.tar.gz` 仅包含已审核的公开目录。工作人员完整包包含漏洞源码、Checker 和标准修补，请限定工作人员访问。

`noctf-awdp-fix-*.tar.gz` 只用于内部验收。每个包根目录直接包含 `fix.sh`，对应防御成功、漏洞仍存在、业务损坏、Patch 非零和超时。公开附件里的 patch-template 保留待修补漏洞，供选手自行完成。

SHA256SUMS 列出所有 tar.gz 的 SHA-256。解包后复验附件内容和脚本权限，填写交付清单；在隔离比赛投放实际镜像，完成至少两队的平台权限、判定、计分、暂停恢复和回收验收后发布。

模板 Docker 检查只验证服务与检查器行为。它不签发平台 JWT，不验证平台数据库或正式赛事计分。失败、跳过与未验收项分别记录。
