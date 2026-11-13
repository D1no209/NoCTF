# CTF 文件读取题模板

目标是一个真实存在路径穿越的文件服务。选手从公开文件接口读取私有 Flag，Flag 由平台注入，源码与 EXP 放在 `private/`。

1. 编辑 `statement.md`、`attachment/README.md`、`platform/CONFIGURATION.md` 和私有交接单。
2. 在 `starter-kits` 执行 `python tools/verify.py --mode ctf`。
3. 构建 `docker build -t YOUR_REGISTRY/noctf-ctf:VERSION ctf/private/runtime`，推送后把正式引用填入配置表。
4. 执行 `python tools/package.py --mode ctf`，复验完整包并审核选手附件包。
5. 在隔离比赛使用至少两队完成实际提交、重置、计分与回收。

单独解包后的命令在 `ctf` 目录执行：`python tools/verify.py --mode ctf` 和 `python tools/package.py --mode ctf`。镜像构建路径使用 `private/runtime`。

静态题可保留交付文档并去掉 Runtime 配置，改用平台静态 Flag 与附件能力；完整分支要求见源码仓库 `specs/challenge-authoring-templates/ctf-challenge-template.md`。
