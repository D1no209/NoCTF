# AWD 部署交接

在 `starter-kits` 执行：

```sh
docker build -t YOUR_REGISTRY/noctf-awd-runtime:VERSION awd/private/runtime
docker build -t YOUR_REGISTRY/noctf-awd-checker:VERSION awd/private/checker
docker push YOUR_REGISTRY/noctf-awd-runtime:VERSION
docker push YOUR_REGISTRY/noctf-awd-checker:VERSION
python tools/verify.py --mode awd
python tools/package.py --mode awd
```

独立工作人员包中使用 `private/runtime` 和 `private/checker`。正式仓库、版本、digest、源码修订与构建平台：待填写。

Runtime 用户 10001，监听 8080，数据按 StoreId / FlagId 保存于 `/dev/shm/flags`。当前容器内历史保留；停止或重置会丢失。样例容量由题目历史窗口和实际 Flag 数量决定，正式部署核对共享内存容量。

Checker 用户 10001，完整输入由平台保留的环境变量提供。它必须访问目标内部地址、任务材料接口与回调地址，不需要访问数据库或对象存储。任务和回执只在内存处理；stdout / stderr 不打印材料。

BusinessChecker 使用 PUT / GET；RunnerCommand 在指定服务使用后台表命令投放。当前写入失败仍读取历史；历史缺失不补写。

正式平台以两队实际镜像验证多轮发行、跨轮捕获、历史删除、服务与平台故障、暂停和结束。记录 Store、Runtime、GameplayFact、回执与最终状态的非敏感标识，验收后停止目标并确认资源回收。
