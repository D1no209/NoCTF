# AWDP V2 参考模板

## 出题人从这里开始填写

| 要交付的内容 | 在哪里写 | 运维怎么用 |
| --- | --- | --- |
| 题目名称、模式、方向、每个输入框和开关的值 | [后台逐项填写表](platform/CONFIGURATION.md) 的“本题填写内容”列 | 按后台同名输入框照填，不猜参数 |
| 题目描述 | [statement.md](statement.md) | 全文复制到“基本信息 → 题面” |
| 如何构建、部署和验收 | [部署交接单](private/DEPLOYMENT.md) | 按步骤准备镜像、上传附件、创建测试实例 |
| Hint（可选） | [Hint 填写单](private/HINTS.md) | 到“比赛管理 → 题目 → 提示”逐项填写 |
| 不明白输入框的意思 | [页面字段说明](platform/FIELDS.md) | 按页面名称查含义、单位和常见填错原因 |
| Checker 要读取 Fix 文件或生成文件 | [Checker 文件读取说明](private/CHECKER-FIX.md) | 确认前端开关和实际能读取的目录，不把输入包当成靶机快照 |

**不要求出题人编写配置代码。** 下面的源码和脚本供负责实现题目的 Agent／开发者使用；运维录入以填写表为准。

## 本模板是什么

这是面向 NoCTF `0.1.0-alpha.142+` 的最小教学服务，演示 Checker 在启动前读取 `/noctf/fix`、
静态检查提交内容、使用独立靶机基线重放修补、比较文件差异，以及验证真实靶机的功能和漏洞。
它不是可直接用于真实比赛的完整安全沙箱；使用前先阅读 [AWDP 出题契约](../../awdp-challenge-template.md)。

## 目录与发布边界

| 目录 | 内容 | 是否提供给选手 |
| --- | --- | --- |
| `private/target/` | 题目源码、原始基线和 Target Dockerfile | 否；公开源码需审核后另行复制 |
| `private/checker/` | Checker、PoC/EXP、基线重放脚本 | 否 |
| `private/examples/fixes/` | 正确修补、漏洞仍可利用、服务异常三组内部样例 | 否 |
| `private/artifacts/fixes/` | 内部样例构建产物 | 否 |
| `attachment/` | 公开附件说明、公开数据和 Patch 包模板 | 经审核后可发布 |
| `attachment/dist/` | 发布脚本生成的附件 | 是；上传前检查归档内容 |
| `platform/`、`scripts/`、`tests/` | 平台配置、构建和验收工具 | 否 |

`private/` 不会自动获得 Git 或平台访问保护。真实题目应放在受控仓库中；附件上传必须逐个选取，
不要对整个模板执行递归打包。

## 构建镜像

在本目录执行，先构建靶机，再构建依赖相同基线的 Checker：

```sh
docker build -t noctf-awdp-v2-target:local private/target
docker build --build-arg TARGET_IMAGE=noctf-awdp-v2-target:local \
  -t noctf-awdp-v2-checker:local private/checker
```

两个镜像均使用数字 UID/GID，与平台的非 root 校验一致。构建上下文分别限定在对应子目录，
不会把 Checker 或 PoC 复制到靶机镜像。

## 构建选手附件与内部验收包

```sh
# 仅生成公开附件与待填写的 Patch 模板，不包含正确修补。
sh scripts/build-attachments.sh

# 仅供出题人验收，产物保存在 private/artifacts/fixes。
sh scripts/build-fix-packages.sh
```

公开 `patch-template.tar.gz` 直接包含 `fix.sh` 与填写说明。空白模板会明确报错并以非零退出，
不能作为有效修补提交。三份包含预期判定的内部样例不会进入选手附件。

## 本地验收

在具有 GNU tar/find/sort 的 Linux 或 WSL 环境中执行：

```sh
# 不需要 Docker，检查公开与私有包的内容边界和可重复构建。
sh tests/packages.sh

# 不需要 Docker，检查输入文件与重放产物的路径、链接和大小边界。
python3 tests/input-contract.py

# 需要可用 Docker，仅在可丢弃的测试环境运行。
sh tests/smoke.sh
```

容器验收会构建本地教学镜像、启动独立测试容器与网络，覆盖 `DefenseSucceeded`、
`ExploitSucceeded`、`ServiceAbnormal`。脚本退出时清理本次测试资源，不使用生产地址或凭证，
不得在生产 Docker 主机执行。平台配置见 [配置说明](platform/CONFIGURATION.md)，
交付前完成 [检查清单](NOCTF-DELIVERY.md)。
