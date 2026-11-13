# 文档柜修补

本题包含攻击与防御两个任务。攻击阶段在本队目标利用文件读取缺陷，取得当前运行环境的 Flag 后提交。重置后请重新读取地址并获取新 Flag。

防御阶段上传 tar.gz，根目录直接包含 `fix.sh` 与替换文件。平台在独立目标执行修补并检查业务：

- `GET /health` 应返回 `ok`。
- `GET /file?name=welcome.txt` 应读取公开文档。
- `GET /file?name=docs/guide.txt` 应读取嵌套公开文档。
- 公开目录以外的文件不得通过接口读取。

附件的 `patch-template/` 提供脚本和待修补解析器。修改 `resolver.py` 后，在该目录执行 `tar -czf fix.tar.gz fix.sh resolver.py`。Patch 通过 `/bin/sh` 执行，用户为 10001，可写 `/opt/challenge/resolver.py`。保持服务可用，无需重启主进程。

上传上限、次数、先攻击后防御要求和计分以平台本场配置为准。长期攻击环境与一次性防御环境独立。攻击范围限定本题目标。
