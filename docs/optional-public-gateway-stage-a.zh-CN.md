# 可选公网网关：阶段 A 实测与待确认事项

日期：2026-09-08。分支：`codex/optional-public-gateway`。

## 当前结论

**动态题目发布安全门禁未通过，不能进入可启用／可上线状态。本文不表示公网网关功能已经完成。**

按设计稿先实施只读盘点和隔离原型，没有新增服务器服务、修改反代、证书、DNS、安全组、端口规则或容器网络。生产服务器 `192.0.2.30` 未访问。

已有 AWDP 修复 `6f0814e6` 已推送至 main，附加空提交 `1f734579` 用 `[skip ci]` 保持此前“不部署”的边界。推送后 GitHub Actions 查询没有该提交的运行记录。新网关分支未推送。

## 1. 环境核实

### 公网跳板 8.156.82.215

- SSH 只读连接成功，主机为 `iZ2vcc3wih64md70ar8jzmZ`。
- 2 核，约 1608 MiB 内存，盘点时 available 约 943 MiB，无 swap；不是经过负载验证的容量承诺。
- 已有 OpenResty 和两个业务容器，未停止或修改。
- OpenResty 容器使用 host 网络，因此回环上游具有可达性基础，但仍须在实际隧道部署后探测。
- 监听包括 22、80、443、36632。36632 落入常见 Docker 随机端口范围，后续必须明确保留，不能抢占。
- `command -v frpc/frps` 未找到可执行文件，容器列表也未发现 FRP；这不等于已扫描所有磁盘或所有进程。
- `1panel-network` ID 为 `2423359049c26e90357987a3156943f63a6829ba19193cd6d76a31b523f94a0c`，网段 `172.18.0.0/16`。两次只读检查 ID 与既有成员一致。

### 测试平台 192.0.2.10

- Windows SSH 只读连接成功，主机为 `VM-0-9-tencentos`。
- 当前 WSL 默认密钥不同，root／ubuntu 认证失败。没有复制私钥或修改密钥权限；无需把私钥贴到聊天中。
- 4 核，约 3655 MiB 内存，盘点时 available 约 1453 MiB，已有 swap。
- 平台 HTTP 绑定 `127.0.0.1:8080`。已有 API、Worker、Runner、PostgreSQL、Redis、NATS 及监控容器；均未改变。现有 exporter 不是本任务新增的。
- 没有名为 `1panel-network` 的网络，未擅自创建。
- `net.ipv4.ip_local_port_range` 为 `32768–60999`，不能把它当成跳板已批准的公开端口集合。

### 站点前提与设计稿不一致

- 本机 `Resolve-DnsName challenge.fa1lsnow.com -Type A` 返回 NXDOMAIN；跳板 `getent ahostsv4` 也未返回地址。尚未完成权威 DNS 全链路诊断。
- 对应 1Panel 站点配置仅监听 HTTP 80。
- 当前上游为 `http://127.0.0.1:9999`，只读监听清单未发现该端口。
- 从跳板本机携带该域名 SNI 访问 443，TLS 握手返回 `unrecognized name`，未取得该站点证书。没有关闭证书验证冒充成功，也没有读出任何私钥。
- 站点已有 Host、X-Forwarded-For/Proto/Port 与 WebSocket 升级相关配置；实际多层代理认证链未测试。

因此“仅纠正上游地址”不足以达到设计稿的 HTTPS 完成标准。DNS 与该站点 HTTPS 绑定需要运维补齐或新的明确操作授权；不改其他站点。

## 2. 固定测试版本与方法

使用 FRP 官方 `v0.68.0` Linux amd64 发布包。来源及能力说明：

- [官方发布](https://github.com/fatedier/frp/releases/tag/v0.68.0)
- [TLS 与身份验证](https://gofrp.org/en/docs/features/common/network/network-tls/)
- [Unix socket 客户端插件](https://gofrp.org/en/docs/examples/unix-domain-socket/)

归档 SHA-256：

```text
3cf934477f4fb1ee9e19e49c31fb33f5ffe3283300076f59afad8b8ccf1e1621
```

测试运行在本机 Docker Desktop 的独立 Testcontainers 网络中，使用固定 digest 的 Python Alpine 辅助镜像承载官方二进制及无害 HTTP 标识服务。测试生成短期独立 CA 和服务端／客户端证书，FRP 双向验证；没有使用 1Panel 证书或平台凭据。服务端只允许一个测试 TCP 端口，不启动 Dashboard、HTTP CONNECT 或 exporter。

A 的宿主端口由 Docker 随机分配；移除 A 后，仅让本测试创建的 B 使用刚释放的端口。通过公网端模拟端实际读取 `runtime-A`／`runtime-B`，不是仅断言控制记录发生变化。frps 的宿主端口同样由 Docker 随机分配，不使用真实跳板既有端口。

## 3. 实测结果

测试文件：`backend/tests/NoCTF.Tests/Integration/Runtime/PublicGatewaySafetyPrototypeTests.cs`。

| 场景 | 实际结果 | 安全含义 |
| --- | --- | --- |
| 普通 frpc → Docker HostPort，A 被平台外移除，B 复用端口 | 旧 A 入口返回 `runtime-B`；frpc 重新启动后仍如此 | 不满足 S07，也证明普通转发无法单独满足 S01 |
| frpc → A 专属 Unix socket → Docker HostPort，同样移除／复用 | 旧 A socket 仍拨向 B；frpc 重启后仍如此 | 唯一 socket 路径本身不是容器身份绑定 |
| 普通转发，先停止专属 frpc 再移除 A、启动 B | 旧入口无法取得响应，未读到 B | 支持受控撤销路线的基础假设，不代表完整生命周期已覆盖 |
| Unix socket 方案，先停止专属数据面再复用端口 | 旧入口无法取得响应，未读到 B | 同上 |

执行结果：4 项测试通过、0 跳过，约 47 秒。**前两个是负对照，测试通过表示成功复现了不安全路径，不表示网关安全验收通过。**

命令：

```powershell
$env:NOCTF_FRP_ARCHIVE = '<官方 frp_0.68.0_linux_amd64.tar.gz 的本地路径>'
dotnet run --project backend/tests/NoCTF.Tests -- --treenode-filter "/*/*/PublicGatewaySafetyPrototypeTests/*"
```

不指定 `NOCTF_FRP_ARCHIVE` 时从上述固定官方地址下载，始终校验 SHA-256。不使用漂移的 latest。测试创建的目标、FRP 容器和独立网络在退出时清理，不执行全局 prune，不删除用户容器。

## 4. 阶段 A 尚未通过的部分

没有宣称执行完成全部 S01–S10。尚未实施：迟到删除／创建通知、完整 Stop/Reset/TTL/比赛清理钩子、Docker daemon 重启与宿主重启、伪造 Connector、端口冲突协议、限额、启停对账、真实两机网站隧道和性能对比。

当前阶段没有创建管理员表单、Migration、运行态投影、后台队列或可启动部署清单，避免在安全机制未确定前交付看似可用的开关。默认部署与业务代码不变。

## 5. 需要确认的实施方向

设计稿第 10.4 节要求明确选择，不能由实现者默默缩小承诺：

1. **保持管理外销毁也安全的目标**：继续设计、验证与原始容器身份绑定且能自动失效的数据面机制。可能需要新增实例隔离组件，先确认组件能力、权限、目标连接方式和资源预算；普通 HostPort 或套一层 Unix socket 不足以实现。
2. **接受运维前置约束**：手工 `docker rm`、Docker daemon／宿主重启前必须先关闭此 Connector 的专属公网数据面，并禁止绕过。随后仍须实现全部正常生命周期的先撤销后释放、故障降级、幂等及 S01–S10 中适用测试，不能只靠操作手册替代平台的正常停止逻辑。

另需补齐 DNS 与站点 HTTPS 前提；只读盘点结果不能转化成修改 DNS、申请证书或重建站点的授权。

没有数据库 Migration、服务器部署或网络修改。生产服务器及 `1panel-network` 均未操作。
