# NoCTF V2 出题模板

本目录面向 NoCTF `0.1.0-alpha.142` 及更新版本。目前 V2 提供一套 AWDP 参考模板，重点演示向
Checker 提供选手 Fix 输入包的能力；旧版 `specs/challenge-authoring-templates/` 保持独立，不受本目录调整影响。

## 阅读顺序

1. **先打开 [后台逐项填写表](starter-kits/awdp/platform/CONFIGURATION.md)**：出题人改好“本题填写内容”，运维按后台同名输入框照填。
2. 在 [题面文件](starter-kits/awdp/statement.md) 写给选手的说明；在 [部署交接单](starter-kits/awdp/private/DEPLOYMENT.md) 写交付信息。
3. 有 Hint 就填写 [Hint 填写单](starter-kits/awdp/private/HINTS.md)；不清楚某个框的意思时查 [页面字段说明](starter-kits/awdp/platform/FIELDS.md)。
4. Checker 开发者再读 [文件读取说明](starter-kits/awdp/private/CHECKER-FIX.md) 和 [AWDP 出题契约](awdp-challenge-template.md)。
5. 完成 [交付检查清单](starter-kits/awdp/NOCTF-DELIVERY.md)，再发布比赛题目。

普通出题交付不需要编写 JSON、YAML 或手工生成资源 ID。所有数值按前端单位填写：例如内存填 256 MiB，CPU 填 0.5 核。

## 模板目录约定

```text
starter-kits/awdp/
├── private/                  # 仅出题人与运维使用，不作为选手附件发布
│   ├── target/               # 题目源码、靶机镜像构建文件与运行基线
│   ├── checker/              # Checker、PoC/EXP 与独立基线重放逻辑
│   ├── DEPLOYMENT.md         # 部署交接单
│   ├── HINTS.md              # 私有 Hint 填写单
│   ├── CHECKER-FIX.md        # Checker 如何取得所需文件
│   └── examples/fixes/       # 内部验收样例，含正确修补和失败样例
├── attachment/               # 经出题人审核后可提供给选手的资料
│   ├── public.txt            # 示例公开附件
│   ├── patch-template/       # 选手填写的 Patch 包模板，不包含标准答案
│   └── dist/                 # 附件脚本生成的选手发布包
├── statement.md              # 题面正文，复制到后台“题面”
├── platform/                 # 逐项填写表、页面字段说明
├── scripts/                  # 构建内部验收包与选手附件的工具
└── tests/                    # 仅在可丢弃环境运行的验收工具
```

`private` 是内容分类，不是访问控制或 `.gitignore` 规则。真实题目的仓库必须使用适当的访问权限；
不要把整个模板目录压缩上传，也不要把 `private/`、`platform/`、Checker、PoC 或内部正确修补作为
选手附件。公开源码题只能由出题人显式挑选、脱敏后复制到 `attachment/`。

## Checker Fix 输入概述

在前端打开“向 Checker 提供 Fix 包”后，Runner 校验选手上传的 `.tar.gz` 并生成一份规范化归档。
该归档先用于防御靶机，再以独立副本注入 Checker；Checker 启动前，输入内容已位于固定目录
`/noctf/fix`。不需要为 Checker 新增下载接口、URL 或下载令牌。

该开关关闭时沿用旧 Checker 路径。当前教学模板不包含真实比赛的 Flag、生产回调令牌、
生产域名或实际比赛题目的解答；即便如此，目录边界和发布白名单仍应按真实出题流程执行。
