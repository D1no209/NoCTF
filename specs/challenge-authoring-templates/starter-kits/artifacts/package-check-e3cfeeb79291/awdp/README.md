# AWDP 文件解析修补题模板

攻击目标从平台 `FLAG` 环境变量保存正式答案，路径穿越可读取它。Fix 在独立一次性目标替换 `resolver.py`，保持正常文件访问并阻断目录越界。Checker 通过实际 HTTP 检查正常文件、嵌套文件及多种穿越路径。

1. 编辑题面、公开附件、后台表和私有交接单。
2. 在 `starter-kits` 执行 `python tools/verify.py --mode awdp`。
3. 分别构建 `awdp/private/runtime` 和 `awdp/private/checker` 并推送。
4. 执行 `python tools/package.py --mode awdp`，生成选手附件骨架和五种内部 Fix 归档。
5. 在真实平台检查攻击 Runtime、新旧 Flag、独立防御目标、上传、回执、Fix 计分与回收。

独立解包后在 `awdp` 目录执行相同 `tools/` 命令。内部样本分别对应：修复成功、漏洞仍存在、业务损坏、Patch 非零和 Patch 超时。公开 `attachment/patch-template/` 中的解析器保留原漏洞，供选手自行修改。

Checker 可在打开 `checkerFixInput` 时读取 `/noctf/fix/fix.sh`，只做普通文件及大小检查；它始终通过目标真实业务判断结果，不执行选手输入。关闭开关仍可执行网络检查。

Fix 目标没有正式攻击 Flag，镜像在该场景生成独立随机内容。私有探测文件用于证明文件越界读取，正常文件必须继续可读。样例 Checker 只覆盖本题声明的漏洞；换题时必须同时替换正向业务和漏洞探测。
