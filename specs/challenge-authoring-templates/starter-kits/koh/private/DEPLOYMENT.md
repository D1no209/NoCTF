# KoH 部署交接

在 `starter-kits` 执行：

```sh
docker build -t YOUR_REGISTRY/noctf-koh:VERSION koh/private/runtime
docker push YOUR_REGISTRY/noctf-koh:VERSION
python tools/verify.py --mode koh
python tools/package.py --mode koh
```

独立工作人员包中镜像目录为 `private/runtime`。正式引用、digest、源码修订、构建平台：待填写。

默认用户 10001，8080 提供公开业务，8081 提供内部只读控制。状态文件 `/opt/challenge/state/control` 保存当前完整值，线程锁保护读写。不设置 FLAG 环境变量，不内置队伍名单。新 Hill 初始正文为空；重建后重新争夺。

推荐公开入口只引用 8080，Control 容器按内部服务地址访问 8081 `/control`。构建并推送 `private/controller`，在 Control 配置填写完整镜像引用、目标服务 `main` 和执行超时。控制端口没有额外应用令牌；Docker 单服务共享网络和公网出站边界必须按部署能力评审。

两队使用平台提供的不同 Control Flag 交替写入，检查当前观测与分数。暂停保存资源并停止轮询，恢复后继续，结束回收。记录无效值、轮询失败、网络可达性与实际权限检查；不把容器检查替代正式计分验收。
