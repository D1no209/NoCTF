# Runtime 与 Checker 配置

容器 Runtime 使用 **Container 命名服务**，一个定义包含 1–64 个服务和一份 Runtime 级访问入口。出题人不填写 Provider/Runner Pool，这些由部署选择。

## 配置命名服务

每个服务使用唯一小写 DNS label，最长 63 字符，例如 `web`、`db`。填写镜像，按需设置 command、arguments、环境变量、内部端口和 Flag 环境变量。

| 字段 | 示例 | 含义 |
| --- | --- | --- |
| 服务名 | `web` | 实例内服务通信名称 |
| 镜像 | `registry.example.com/challenge/web:reviewed` | 执行宿主/节点可拉取的镜像 |
| CPU | `0.5` 核 | 不是 500 核；对应 500 millicores |
| 内存 | `512` MiB | 正整数，以 MiB 为单位 |
| Flag 环境变量 | `FLAG` | 只向指定服务注入每队 Flag |
| command / arguments | 镜像约定的入口与参数 | 未指定保留镜像默认 |

默认每服务 0.5 核、512 MiB，CPU 步长 0.001 核。容量预留为所有服务之和；PID 上限由部署统一控制，出题表单不提供安全策略或 CheckerAllowRoot。

多服务通过服务名通信，如 `web` 连接 `db:5432`。镜像自行处理依赖未就绪的重试，不接收 Compose YAML、任意 Kubernetes manifest、volumes、depends_on、replicas 或自定义容器名。

## 配置访问入口

在 Runtime 级列表添加服务名、容器端口、显示模板和可见范围。例如：

```text
serviceName: web
containerPort: 8080
urlTemplate: http://{HOST}:{PORT}/
exposure: OwnerOnly
```

TCP 题可使用 `nc {HOST} {PORT}`。显示模板只使用受支持的 `{HOST}`、`{PORT}`；同服务同端口的多个显示模板共享一次发布。没有第二份每服务公开端口列表，也不指定宿主固定端口。

Docker 直接发布题目服务，随机 host port；Kubernetes 为公开入口创建 NodePort。Control Check 入口解析实际内部服务地址，不因轮询而另加公网端口。

## 按模式选择定义

| 模式 | 分配与 Flag | Checker / 特殊要求 |
| --- | --- | --- |
| CTF Flag 题 | 通常 PerTeam，静态或 PerTeam Flag | 按题目需要，不把无 Checker 当作错误 |
| CTF Patch 验证 | 单服务，不注入 Flag | 内部端口、Patch 和 Checker |
| AWD | 每队目标、轮换 Flag | Checker targetServiceName 和注入 serviceName 必须存在 |
| AWDP | 单服务目标、每队 Flag | Break、独立 Fix、Patch 和 Checker |
| KoH | 共享 Hill | 独立 Control URL，返回精确队伍 Flag |

## 配置 Checker 与修复包

Checker 镜像、命令、目标服务、超时、就绪等待和修复包入口应按题目约定配置。修复包必须使用安全相对路径，执行命令中的入口占位符遵守当前表单校验。正常服务和漏洞修复必须分别验证，不能只检查“容器能启动”。

Fix 上传上限和 archive 展开上限不同：压缩文件可能小但展开很大。平台拒绝路径穿越、链接、特殊文件、冲突条目和超过展开预算的包。资源耗尽或存储故障应与修复失败区分。

AWD 注入命令需要正确引用生成 Flag，占位和转义以当前编辑器/Starter Kit 为准。Checker 使用受保护内部回调，不在题面公开内部 JWT 或回调 secret。

## 网络与隔离边界

Docker 单服务复用部署级题目 bridge，**共享网络中的不同实例可能互通**；这不是严格跨队网络隔离。多服务使用实例独占 bridge，Checker 使用独立内部 callback bridge，题目不接入 PostgreSQL/Redis/NATS 网络。Docker `Isolated` 也不承诺禁止公网出站。

需要严格跨实例及出网隔离时，使用实际执行 NetworkPolicy 的 Kubernetes 部署并验收。不要在 Docker 单机方案中写出并不存在的隔离承诺。

## 发布前清单

1. Runner 可拉取所有服务和 Checker 镜像。
2. CPU/MiB 总和、TTL 和团队额度可支撑预计并发。
3. 每个访问入口从选手网络可达，内部端口不意外公开。
4. 正确 Flag、错误 Flag、合法 Fix、坏包和失败结果都验证过。
5. 新建、重置、停止和失败回滚可回收资源。
6. 暂停/恢复/Finish 的模式行为符合赛事规则。

协议细节和示例见 [命名服务说明](https://github.com/D1no209/NoCTF/blob/HEAD/specs/runtime-services.md)。旧 Starter Kit 若仍描述 Compose 或 allow-root，应先按当前定义转换，不能直接导入。
