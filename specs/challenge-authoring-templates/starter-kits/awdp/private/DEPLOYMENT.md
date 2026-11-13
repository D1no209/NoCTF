# AWDP 部署交接

在 `starter-kits` 执行：

```sh
docker build -t YOUR_REGISTRY/noctf-awdp-runtime:VERSION awdp/private/runtime
docker build -t YOUR_REGISTRY/noctf-awdp-checker:VERSION awdp/private/checker
docker push YOUR_REGISTRY/noctf-awdp-runtime:VERSION
docker push YOUR_REGISTRY/noctf-awdp-checker:VERSION
python tools/verify.py --mode awdp
python tools/package.py --mode awdp
```

独立工作人员包中使用 `private/runtime` 和 `private/checker`。填写正式镜像引用、digest、源码修订、构建平台与验收时间。

## 目标

默认用户 10001，服务 8080，公开目录 `/opt/challenge/public`。攻击目标把平台传入 `FLAG` 写入私有文件；Fix 目标未传入该变量时生成独立随机内容。Checker 使用同镜像中的私有漏洞探测文件验证越界读取。

Patch 可以替换 `/opt/challenge/resolver.py`。样例主进程每个文件请求重新加载解析器，所以无需重启服务。它不读取回调凭据。Patch 输入位于 `/noctf/fix`，脚本以目标默认用户执行。

## 检查与结果

Checker 先等待健康，再读两个正常文件，执行两个路径穿越探测，再检查业务。能读取探测内容为 ExploitSucceeded，正常业务损坏为 ServiceAbnormal，业务正常且穿越拒绝为 DefenseSucceeded。网络异常不会作为成功修补。

Checker Fix 输入开启后只检查普通 `fix.sh` 文件，不重放选手脚本。网络判定在开关关闭时同样工作。回调错误使 Checker 非零退出；不要重写成业务成功。

内部五种样本位于 `private/fixes`。打包工具生成根目录包含入口的 Fix tar.gz；选手骨架只从 `attachment/` 打包。正常 Fix 小于 1 MiB，Patch 超时 10 秒、就绪等待 5 秒、Checker 30 秒。

正式比赛验收使用当前上传协议创建防御目标并上传归档，不能把本地 Docker 修补直接记为平台 Fix 成功。记录 Break、Fix、处理状态与回收，确认长期攻击目标未被修补或误删。
