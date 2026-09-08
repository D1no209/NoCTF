# 轻量实例端点：隔离原型

状态：本地隔离原型，**未切换 Runner 的实际协调器，未推送或部署**。共享 SSH 数据面和 C# 端口控制组件已接入隔离测试；平台已区分公网与直连端口，但完整生命周期尚未接通，不能据此替换测试／生产部署。网站固定隧道继续使用 FRP，与本原型独立。

## 结构

每个 Runner 共用一个 SSH 客户端，跳板共用一个专用 sshd。每个 Runtime 有一个固定绑定原目标容器 ID 的网络命名空间的轻量程序，将独有 Unix Socket 转发至该命名空间的 `127.0.0.1:容器端口`。一个端点可服务多个容器端口，不是每个端口增加一个容器。它不查询宿主随机端口，不处理 HTTP，不持有隧道证书、平台凭据、Flag 或 Docker socket。

端点挂载自己的 socket 目录（可写）和独立授权目录（只读），不能自行续租。共享客户端只读挂载端点根目录。目录随发布 UUID 新建、不复用；进程单次启动，旧发布即使收到新的租约，也不能通过手动重启恢复。最多十秒租约丢失后关闭监听及所有已有连接。原 Docker `hostPort=0` 随机端口直连保持不变。

命令为 `/relay run /endpoint /lease <发布UUID> <最大连接数> <容器端口...>`；健康检查为 `/relay health /endpoint /lease`。平台原子替换 `/lease/lease.json` 中的 `publicationId` 和 `expiresAtUnixMs`（UTC Unix 毫秒期限），不得让题目、端点或共享隧道客户端写入授权。

当前程序使用 Go 标准库，限制并发连接数和每方向复制缓冲；半关闭使用独立方向的 `CloseWrite`。部署 Dockerfile 使用固定 Go 1.27.1 镜像构建静态二进制，最终镜像为 scratch。没有 Python 守护／周期性 Python 探针。

## 2026-09-08 本地实测

使用真实 Docker、两个题目实例、五个转发端口，分别验证官方 FRP 0.68.0 和 OpenSSH 10.3p1。FRP 使用双向 TLS，SSH 使用专用配对密钥和严格主机公钥检查。测试自动清理了创建的容器、网络及临时 Linux 文件系统卷；实际部署仍要求目录 bind mount。

| 项目 | 结果 |
| --- | --- |
| 普通 HTTP | 通过 |
| 1 MiB 长度明确的原始数据及特殊 HTTP 字节 | 字节完全一致 |
| 直连 TCP 半关闭 | 发送 1,048,671 字节，收到相同字节数 |
| 单独轻量 Unix 端点半关闭 | 1 MiB 字节完全一致 |
| 原生 FRP 半关闭负对照 | **发送 1,048,671 字节，收到 0 字节** |
| 共享 SSH 半关闭 | 发送和接收均为 1,048,671 字节 |
| 题目自身 HTTPS | 端到端证书验证通过，隧道不终止题目 TLS |
| WebSocket 二进制消息和关闭握手 | 通过 |
| SSE | 第二条延迟事件和连接结束前即可收到第一条 |
| SSH 错误主机密钥、未授权监听端口、远程命令、SFTP、本地转发 | 拒绝 |
| 动态撤销／增加 B 的转发 | A 不受影响 |
| 端点尝试写租约目录 | 被只读挂载拒绝 |
| 租约为 FIFO／符号链接／超长文件／目录，或身份、期限、JSON 无效 | 发布前拒绝，不阻塞 |
| 256 个授权端口的 SSH 配置 | 在 64 MiB／0.25 核限额下解析通过 |
| 删除 A 后 C 复用 A 宿主端口 | 旧 A 入口不能到达 C，B 保持正常 |
| 共享客户端／服务端重启 | SSH 不自动恢复旧转发；重新批准 B 后恢复，旧 A 不能到达 C |
| 延迟清理 A | 不影响 B |
| B 租约停止续租 | 公网入口失效，B 原 Docker 直连仍正常 |
| 旧端点重新启动（含新租约） | 被单次启动标记拒绝 |

一次 SSH 测试的 cgroup 内存快照：端点约 4.9～5.2 MiB，共享客户端约 7.8 MiB，跳板专用 sshd 约 9.6 MiB。端点测试配额为 16 MiB／0.05 核，共享客户端和服务端分别 64 MiB／0.25 核。**快照不是满载峰值或容量承诺**，不能据此声称可同时支持某个数量的真实题目。

测试环境限制：Windows Docker Desktop 的直连 TCP 半关闭基线出现过 Socket 中止。现在直连／隧道半关闭均由同一隔离 Docker 网络内的 Linux 客户端验证，避免引入生产不存在的 Windows 端口转发层。HTTP、HTTPS、WebSocket 和长度明确的原始流仍经过宿主发布端口验证；正式验收仍须在测试服务器的 Linux Docker 上重跑。

半关闭失败已由源码交叉确认：FRP 0.68.0 的 Unix Socket 插件调用 `golib/io.Join`，它在一个复制方向结束时关闭两端。固定依赖为 `github.com/fatedier/golib v0.5.1`。

- [FRP Unix Socket 插件](https://github.com/fatedier/frp/blob/v0.68.0/pkg/plugin/client/unix_domain_socket.go)
- [固定版本 Join 实现](https://github.com/fatedier/golib/blob/v0.5.1/io/io.go)

`SharedTunnelRelayPrototypeTests` 将 FRP 明确命名为负对照，断言其未保留半关闭；SSH 正向验收必须返回完整响应。没有忽略 FRP 问题。这里的“透明”限定为已验证的字节和连接语义，不代表源 IP 不变、没有网络延迟，或所有 Web 应用都能无配置使用。题目若自行生成绝对 URL、校验 Host／Origin，仍需配置自己的公开地址。

## 复现

在仓库根目录使用 Go 编译 Linux 原型（本地现有 Go 1.25.4 可用于复现；发布只使用 Dockerfile 固定工具链）：

```powershell
$env:GOOS='linux'
$env:GOARCH='amd64'
$env:CGO_ENABLED='0'
$env:GOTOOLCHAIN='local'
$env:NOCTF_RELAY_BINARY="$env:TEMP/noctf-gateway-relay-prototype"
go build -trimpath -ldflags='-s -w' -o $env:NOCTF_RELAY_BINARY ./deploy/public-gateway/relay/main.go
docker build --target prototype -f deploy/public-gateway/ssh/Dockerfile -t noctf-gateway-ssh-prototype:local .
$env:NOCTF_REQUIRE_DOCKER_INTEGRATION='true'
# 可选：NOCTF_FRP_ARCHIVE 指向未经修改的官方 frp_0.68.0_linux_amd64.tar.gz，测试会校验 SHA-256。
dotnet run --no-restore --project backend/tests/NoCTF.Tests -- --treenode-filter '/*/*/SharedTunnelRelayPrototypeTests/*' --output Detailed
```

以上构建仅用于本地测试，服务器只使用 CI 镜像，不上传源码构建。

## 尚未完成的接入门禁

1. 将已验证的 `SharedSshPortController`／`DockerSshControl` 接入 Runner 的实际协调器和依赖注入。目前只由隔离测试调用，不是已上线的实现。
2. 使用已完成的独立 `PublicPort` 状态投影，把实际分配端口写入平台短期缓存。原 FRP 适配器明确填写同号端口，不通过空值回退猜测公网端口。
3. 旧发布延迟撤销不能取消同端口的新发布，租约续期不能被慢探测阻塞。
4. 真实 Linux 目录 bind mount 权限、平台重启恢复、Docker daemon 重启、多实例压力测试。
5. CI 镜像产出和测试服务器完整平台验收；目前没有在任何服务器替换题目数据面。

专用 SSH 权限、端口池和配对材料见 [SSH 说明](../ssh/README.zh-CN.md)。无需修改数据库、反向代理或 `1panel-network` 来运行本地原型。
