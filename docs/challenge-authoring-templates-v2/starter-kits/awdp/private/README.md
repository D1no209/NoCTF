# 出题人私有资料

本目录仅供出题人、协作者和经授权的运维使用，不应进入选手附件：

- `target/`：题目源码、Target Dockerfile 和运行基线。
- `checker/`：Checker、`exploit.sh` PoC/EXP 和独立基线重放脚本。
- `examples/fixes/`：三种预期判定的内部样例，其中包含正确修补。
- `artifacts/fixes/`：`scripts/build-fix-packages.sh` 生成的内部归档。
- `DEPLOYMENT.md`：由出题人填写的部署交接单。
- `HINTS.md`：按前端名称填写的私有 Hint 草稿。
- `CHECKER-FIX.md`：Checker 读取输入文件与重放产物的说明。

目录名不会自动设置 Git 或文件访问权限。实际比赛源码应存放在受控仓库中，回调令牌、真实 Flag 和
生产凭证也不应写入此目录。公开源码题必须逐个审核文件，再显式复制到 `attachment/`；不要创建
指向本目录的公开符号链接，也不要直接对题目根目录打包。

构建和验收步骤见 [模板说明](../README.md)。
