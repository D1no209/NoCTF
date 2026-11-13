# CTF 部署交接

在 `starter-kits` 构建：

```sh
docker build -t YOUR_REGISTRY/noctf-ctf:VERSION ctf/private/runtime
docker push YOUR_REGISTRY/noctf-ctf:VERSION
python tools/verify.py --mode ctf
python tools/package.py --mode ctf
```

独立工作人员包解压后，构建目录改用 `private/runtime`。本地命令中的仓库与版本须替换为实际值。

- 正式镜像引用、推送后的 digest、源码修订、构建平台：待填写。
- 镜像默认用户 10001:10001，监听 8080。
- 必填环境变量 `FLAG`，平台自动注入；未传入时进程立即失败。
- Flag 写入 `/opt/challenge/private/flag`，公开根目录 `/opt/challenge/public`。
- 原始 Flag 不打印；私有 `exploit.py` 按原字节输出答案，只在受控验收中重定向到受限文件。
- 无启动安装、外部数据库、额外挂载或持久卷需求。
- 目标为按队临时容器，停止后本地文件随目标清理；CTF Flag 由平台继续保存。

模板测试后，在隔离比赛引用本镜像，验证两队不同答案、Wrong / Correct、重置复用原值和计分。记录实际资源 ID、结果、资源回收情况。公开内容只使用附件包。
