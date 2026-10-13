# AWD Starter Kit

包含可构建的按 StoreId/FlagId 保存数据的 HTTP Runtime、Python 标准库 Checker、任务材料/回执模拟器和故障测试。默认演示 BusinessChecker 写入，也支持 RunnerCommand 的只读任务清单。

样例 `/flags/{storeId}/{flagId}` 无认证，故意直接暴露记录，只用于协议验证。正式题替换真实业务、漏洞与修复流程，保留历史读取语义和证据完整性。

1. 修改 Runtime 与 Checker，按 `platform/CONFIGURATION.md` 配置命名服务及 Store。
2. 执行下面的测试并构建、发布可由 Runner 拉取的镜像。
3. 填写 `NOCTF-DELIVERY.md`，完成至少两队真实平台 E2E 后交付。

本地 Python 测试不需要第三方包：

```sh
python -B tests/test_checker.py
```

Docker 冒烟需要 Docker Engine 和 POSIX shell：

```sh
sh tests/smoke.sh
```

冒烟依次验证当前 Flag 写入、下一轮读取历史、删除历史后的恢复证据；回执日志只记录验证阶段，不输出秘密。脚本只清理本次唯一后缀的容器、网络和测试镜像，不执行全局 prune。

Checker 必须领取当前尝试的固定清单，逐项上报；不会自行计算健康、历史责任或分数。业务失败与平台失败需按真实服务设计归因，样例 HTTP 判定不能直接套用于任意生产服务。
