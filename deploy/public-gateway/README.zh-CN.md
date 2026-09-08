# 可选公网访问部署

这是独立部署，不合并进默认平台 Compose。实际启用前必须完成配对、安全与站点验收。

## 组件与端口

- 跳板网站 FRPS：60998/TCP 控制入口，双向 TLS；只允许将网站映射至回环 9999。
- 跳板题目 FRPS：60999/TCP 控制入口，独立 CA 和令牌；允许题目端口 32768–60997，明确排除已占用的 36632。两个控制端口不得用于题目映射；Docker 若分配到保留端口，直连不变，公网发布应明确拒绝。
- 测试平台网站 FRPC：固定连接本机 `127.0.0.1:8080`，不能由平台表单指定任意上游。
- 动态题目 FRPC：由配对 Runner 管理，每个实例一个，无 Docker socket，不共享目标的文件系统或 PID；仅共享原目标的网络命名空间，连接该命名空间内的服务端口。

内网仍使用 Docker 自身以端口 0 分配的宿主端口。公网端口与该宿主端口同号，但公网数据面不再追随宿主端口，防止原目标删除后串到新容器。

公网 TCP 端口不是队伍身份认证；知道地址的人可能建立连接。普通 `nc` 的公网段也不会因隧道内部启用 TLS 而自动端到端加密。不得把平台地址查看权限等同于网络访问控制。

本次跳板 OpenResty 使用 host 网络，所以独立 FRPS 也采用 host 网络，网站端口只绑定回环。不创建、重建或修改 `1panel-network`。若部署到其他拓扑，必须重新确认上游可达性，不直接照抄。

## 目录与秘密

建议专属目录 `/opt/noctf-public-gateway`，子目录 `website-server`、`challenge-server`、`website-client`、`runner-credentials`。卷全部使用目录绑定。不要把这些内容放进 Git、网站目录或构建上下文。

网站与题目通道使用不同 CA、客户端/服务端证书和随机令牌；不能复用 1Panel 网站私钥。服务端证书分别使用 SAN `noctf-website-gateway`、`noctf-challenge-gateway`。

将对应 `.toml.example` 复制为组件目录下的 `frps.toml`／`frpc.toml`。每个组件独立的 `.env` 文件保存 `NOCTF_FRP_TOKEN` 和以下健康检查变量：

```ini
# website-server.env
NOCTF_GATEWAY_COMPONENT=server
FRP_BIND_PORT=60998
# challenge-server.env 使用 FRP_BIND_PORT=60999
# website-client.env 使用 NOCTF_GATEWAY_COMPONENT=website-client
```

容器运行 UID/GID 为 65532；只赋予其读取本组件配置和证书的权限，私钥权限应为 0600。Runner 的配对文件只挂载给 Runner，不挂载给选手容器。

使用 CI 产出的 digest 写入部署目录 `.env` 的 `NOCTF_GATEWAY_IMAGE`。不在服务器上构建镜像，不使用 latest。

## 平台启动能力配置

在独立网关配置文件中提供：

```ini
PublicGateway__ConnectorId=noctf-test-gateway
PublicGateway__RunnerId=填写已确认的RunnerID
PublicGateway__ApprovedOrigins__0=https://challenge.fa1lsnow.com
PublicGateway__FirstPort=32768
PublicGateway__LastPort=60997
PublicGateway__ReservedPorts__0=36632
PublicGateway__MaximumPorts=8
PublicGateway__NamespaceIsolationAvailable=true
```

配对 Runner 额外提供：

```ini
PublicGateway__HelperImage=CI产出的不可变镜像引用
PublicGateway__ServerHost=8.156.82.215
PublicGateway__ServerPort=60999
PublicGateway__ServerName=noctf-challenge-gateway
PublicGateway__CaFile=/run/noctf-gateway-credentials/ca.crt
PublicGateway__CertificateFile=/run/noctf-gateway-credentials/client.crt
PublicGateway__KeyFile=/run/noctf-gateway-credentials/client.key
PublicGateway__TokenFile=/run/noctf-gateway-credentials/token
```

`NamespaceIsolationAvailable` 只能在部署能力已验证后启用，不代表按钮点击即完成网络验证。默认缺少连接器配置时，不启动网关 Agent 或数据面。管理员在“平台管理 → 公网访问”保存日常发布开关、入口与显示主机，不需修改题目模板。

最大 8 个端口意味着最多 8 个转发容器，每个限制 32 MiB／0.1 核。需要为这部分预留独立宿主资源，不从“平台业务完全不经过转发进程”推断零资源影响。

## 安全与恢复

配置为连接器负责人的 Runner 注册关键 `public-gateway` readiness 依赖。代理未注册、初始化未完成、所有权冲突或 Redis 所有权查询失败时，`/health/ready` 返回 503；恢复所有权并完成初始化后恢复就绪。检查会读取真实所有权，不会因为 Redis 仍可连接就报告网关健康，也不会通过健康检查抢锁或续租。未配置连接器以及明确分配给其他 Runner 的配置不增加该依赖。这里只表达该 Runner 能否协调网关，不保证每道题的转发均已就绪；合并角色进程中该关键依赖会影响整个进程的 readiness，liveness 不受影响。

主机字段前后端使用相同规则：ASCII 域名（标签 1–63 字符、不能以连字符开头或结尾、允许末尾点）或四段十进制 IPv4（每段 0–255，不允许前导零），总长度不超过 253。IPv6、协议、路径和端口不属于该字段。先前被宽松校验接受的不规范值，后续保存会被拒绝；需改为符合新规则的实际主机名。

独立 FRP 的 Python 租约守护进程在撤销时先发送 SIGKILL，最多等待 1 秒回收。回收超时或系统错误会输出不含参数的诊断并以非零状态退出，不无限等待、不将失败伪装成成功。该行为用于其独立 PID 命名空间中的 PID 1，不适用于宿主机运行方式；当前 Shared SSH 的 Go relay 不使用这个 Python 守护程序。

每次发布有独立 ID 和短租约。租约绑定发布 ID，最长 10 秒，无续租时 PID 1 终止 FRPC；不自动启动旧磁盘配置。FRPC 不使用持久化动态 Store。Runner 用短时 Redis 所有权租约防止同一连接器多进程重复接管。

FRPC 的状态接口只在目标网络命名空间内的回环上开启，使用每次发布独立的 HTTPS 证书和随机认证；状态探针校验证书，不将该接口发布到宿主或公网。目标不能读取该转发容器的私钥和配置文件。

关闭题目公网不会停止目标或网站固定隧道。停止、过期、删除后的旧命名空间不会自动转向新容器；晚到删除按发布身份处理，不能按裸端口误删新发布。

如需回滚，只停止本目录 Compose 项目和本连接器持有的发布，恢复该站点原上游／本次增加的请求头。不要运行全局 prune、删除网络或数据库降级。新增数据库列保留，关闭功能即可。

## 验收

升级旧部署前先确认 API 的 `Database__AutoMigrate=true`。测试环境曾遗留 `false`，导致镜像升级后新增网关字段未应用；必须备份数据库后由平台启动流程执行 EF 迁移，不用手工建列或重建数据库。独立 Runner 也必须使用 `AddDbContextWithWolverineIntegration`，保证运行时状态与持久化赛事事件在同一事务提交；仅看到消息被标记为 Handled 不能证明业务状态已写回。

至少验证：网站 HTTPS/登录/刷新/上传/下载/SignalR、内外网地址选择、容器开始／停止／重置、错误身份与端口拒绝、租约失效、重启、端口复用、不支持的模式提示、关闭后内网仍可用，以及原有站点／服务／网络不变。`PublicGatewaySafetyPrototypeTests` 包含不安全方案的负对照，负对照测试通过不等于那些方案可上线。
