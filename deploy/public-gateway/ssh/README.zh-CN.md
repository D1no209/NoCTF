# 专用题目 SSH 隧道：接入与验收

**Runner 已实现可选 SharedSsh 生命周期，尚未在服务器部署。** 本目录提供独立隧道镜像、配对配置和可选平台叠加配置；不替换网站 FRP，不使用宿主 SSH 服务，不修改其他站点或 `1panel-network`。数据面测试见 [验收记录](../relay/README.zh-CN.md)。

## 权限边界

- 跳板一个专用 sshd 容器、Runner 一个共享客户端；不是每道题一个 sshd。
- 专用 `noctf` 账户只允许公钥认证和指定公网端口的反向 TCP 监听。
- `MaxSessions 0` 禁止会话／命令／子系统，不提供 Shell、SFTP、PTY、代理转发或本地 TCP 转发。
- `AllowUsers` 限定运维确认的 Runner 出口 IP 或网段。
- 客户端严格校验配对主机公钥；禁止 `StrictHostKeyChecking=no`。
- 服务端关闭密码和空密码登录。镜像中的账户解锁仅为公钥认证，不能省略上述配置。
- 不使用运维个人 SSH 密钥，不挂载 Docker socket。
- 服务端健康检查只是进程存活；客户端健康检查验证共享控制连接，不代表所有题目发布成功。

## 受限公网端口池

默认控制端口 `60999`，网站隧道 `60998` 独立保留；候选题目池 `40000–40255`，共 256 个 TCP 端口。脚本拒绝管理端口 `36632`、网站／控制端口及越界范围。

这不是 SSH 最多转发 256 个端口的协议限制，而是本方案对权限范围和配置解析资源的保守限制；实际容量仍取决于带宽、连接数、CPU、内存。

OpenSSH `PermitListen` 需要逐项授权。整个约 2.8 万端口区间展开为一行，在本地 64 MiB 限额测试中失败。因此不能为保持公网和 Docker 随机端口同号就使用超长列表，更不能放宽为任意端口。重复多行同名配置也不构成追加列表。

原 Docker 随机直连端口保持原样；后续控制器独立分配公网端口，页面分别展示。多个 Runner 必须使用互不重叠的公网池和独立凭据。

## 配对材料生成

`New-SshPairingBundle.ps1` 参数：

| 参数 | 填写内容 |
| --- | --- |
| `Destination` | 不存在的私密目录；拒绝覆盖已有材料 |
| `ServerHost` | 跳板 IPv4 地址或域名 |
| `RunnerSourceAddress` | Runner 实际出口 IP 或运维确认的 CIDR，不是容器内部地址 |
| `ServerImage` / `ClientImage` / `RelayImage` | CI 镜像完整 `@sha256:...` 引用 |
| `ControlPort` | 已放行且不冲突的高端口，默认 60999 |
| `FirstPublicPort` / `PublicPortCount` | 已放行且不含保留端口的连续池，最多 256 项 |

脚本仅生成 `server/`、`client/`、`.env`、`platform.env` 和两份 compose，不连接服务器。`.env` 中故意留空部署身份和 Linux 路径，未填时 compose 明确拒绝启动。`server/` 只交给跳板，`client/` 只交给对应 Runner。私钥不能进入 Git、日志或聊天记录。传输到 Linux 后核对权限，Runner 必须能读取专用客户端密钥。

服务端 compose 使用宿主网络监听动态题目端口，不加入或重建 1Panel 网络，不映射宿主 22 端口。它使用目录挂载、CI 镜像和 Dockerfile 健康检查。

不单独启动静态客户端 compose：共享客户端由 Runner 管理，每个所有权会话创建新的不可变容器 ID，避免旧操作误作用于替代客户端。`platform.compose.yml` 只给原 `deploy/docker-compose.yml` 的 `noctf` 服务增加 env 文件、状态目录和只读密钥目录，不增加网络、端口、数据库或迁移服务。测试服务器若采用独立 Api/Worker/Runner 服务，不能直接照搬服务名，需将对应目录挂载给现有 Runner。

## 控制器必要语义

`SharedSshPortController`（受限端口池、会话绑定、精确撤销）和 `DockerSshControl`（限时 OpenSSH 命令）已由 `DockerSharedSshGateway` 接入实际 `PublicGatewayAgent`。通过部署环境变量 `PublicGateway__Transport=SharedSsh` 选择；未填写保持 IsolatedFrp。非 Linux、未固定 digest、非法路径或越界端口池均不会注册共享 SSH 传输。

客户端配置刻意不写持久化 `RemoteForward`：重启先建立空连接，再由平台确认有效发布并补建。动态命令必须使用 `ssh -F none -S /control/master -O forward/cancel`，不能加载可能含多条 `RemoteForward` 的配置撤销单个实例。

控制器必须核对“共享连接身份＋端口＋发布 UUID”所有权：旧任务只能撤销自己的监听，不能在端口被 B 复用后撤销 B。重连后重新确认归属；撤销失败仍须停止旧端点，以短租约兜底。

已确认的外部协议陷阱：OpenSSH 10.3 的 `-O cancel` 即使收到 master 拒绝，也会打印错误后退出 0。因此退出 0 **且无诊断输出**才算撤销确认；精确的本地主连接“该发布不存在”响应也可用于幂等清理，其他结果仍保留占用。原始诊断不进入面向用户的错误或生产日志。依据见 [OpenSSH muxclient 源码](https://github.com/openssh/openssh-portable/blob/V_10_3_P1/mux.c)。

端口占用是当前协调器的内存安全账本，不是跨进程锁。后续 Runner 接入仍必须持有连接器所有权租约；启动／接管前须清理或重建旧 master，不能以空内存账本直接接管仍有旧转发的连接。已经释放的公开 URL 日后可以依法分配给新题目；这里保证的是旧控制命令／旧实例转发不误操作新题目，不承诺永久不复用公网端口。

脚本和数据面测试通过不等于完整控制链路实现。测试服务器迁移前还需平台集成验收，生产迁移另行授权。

## 本地接入进展（2026-09-08）

- `PublicEndpointStatus`／HTTP 响应新增独立 `PublicPort`；未知、保留、越界或相互矛盾的就绪状态不生成公网 URL。直连仍按 Docker `HostPort` 展示。
- 已重新导出 OpenAPI 并生成前端 SDK；管理页分别标明容器、直连、公网端口，不用内网端口填补缺失的公网值。
- C# 控制组件真实联调通过半关闭、HTTPS、SSE、WebSocket、共享连接重启和旧 A 撤销不影响端口新持有者 B 的测试。另有并发配额、确认丢失、撤销失败和会话变化单元测试。
- 本轮非集成测试 1131 项、共享隧道隔离测试 3 项、原 FRP 协调器真实 PostgreSQL 测试 1 项通过；前端类型检查和 408 项测试通过。前端验证为类型及代码契约测试，尚未做登录后的浏览器验收。
- 后续已完成实际 Runner 接入，并修复非 root SSH 用户条目缺失、旧 Ready 缓存残留、旧回调重复清理报错。没有新 Migration、推送或部署。

## 平台配置与目录

将生成的 `platform.env` 路径填入 `NOCTF_GATEWAY_ENV_FILE`；沿用原平台 compose 的 `.env`，补入生成文件中需要的网关变量，不覆盖原数据库、签名密钥或其他服务设置。

| 变量 | 运维填写内容 |
| --- | --- |
| `NOCTF_GATEWAY_CONNECTOR_ID` | 此配对连接器的固定标识，Api 与 Runner 一致 |
| `NOCTF_GATEWAY_RUNNER_ID` | 现有 `Runner__Id`，不能另造一个身份 |
| `NOCTF_GATEWAY_PUBLIC_ORIGIN` | 已批准的平台 HTTPS origin，不含路径 |
| `NOCTF_GATEWAY_STATE_DIRECTORY` | 宿主上的专用状态目录，Runner 可写，不能是符号链接或对组／其他用户可写 |
| `NOCTF_GATEWAY_CLIENT_DIRECTORY` | 宿主上的 `client/` 配对材料目录，Runner 可读，其他用户不可读 |
| `NOCTF_GATEWAY_SAFETY_APPROVED` | 完成本部署隔离验收后才能设为 `true` |
| `NOCTF_GATEWAY_MAXIMUM_PORTS` | 平台允许的实际发布配额，默认 8，当前上限 64；不是 SSH 的协议上限 |

状态目录在 Runner 内为 `/app/public-gateway-state`，宿主路径来自独立的 `HostStateDirectory`，二者必须指向同一个 bind mount。每个所有权会话及每个发布都使用新 UUID 目录。socket 子目录仅 endpoint 用户可写；租约子目录在 relay 内只读。root Runner 初始化为 UID 65532 的端点／客户端；非 root Runner 使用自身 UID/GID，以保持 bind 权限一致。为 OpenSSH 写入的本地用户资料仅在专用客户端容器内，不修改宿主用户或权限。

所有权每 2 秒续租，与端点续租、较慢的发布操作独立。只持有连接器所有权的协调器能发布；丢失所有权会取消其正在执行的操作。接管先清理旧客户端，再清理旧端点，然后重新核实数据库资格创建新的发布。关闭公网、删除端点或切换策略不会停止原题目容器。

## 本地完整生命周期复现

先按 relay 文档构建本地镜像，再构建独立客户端：

```powershell
docker build --target client -f deploy/public-gateway/ssh/Dockerfile -t noctf-gateway-ssh-client:local .
docker build -f deploy/public-gateway/relay/Dockerfile -t noctf-gateway-relay:local .
./deploy/public-gateway/ssh/Test-Lifecycle.ps1
./deploy/public-gateway/ssh/Test-Lifecycle.ps1 -NonRoot
```

脚本只启动可回收的本地 Linux 测试容器，使用真实 PostgreSQL、Redis、OpenSSH、Go relay 和实际 C# 协调器。临时测试卷不是部署命名卷。非 root 测试使用 Docker socket 的主组，不添加额外组、不改 socket 权限。脚本默认针对 Docker Desktop；其他 Linux Docker 环境需显式提供容器可达的宿主地址。

上线前仍须验证真实测试服务器负载、Docker daemon／整机重启及浏览器交互，不能把共享客户端重建测试当成整机重启测试。生产部署需另行授权。

浏览器安全验收还必须确认：不可信 Web 题目不要与平台登录共用主机名，不能仅靠不同端口做 Cookie 隔离。Cookie 不按端口隔离，见 [RFC 6265 §8.5](https://www.rfc-editor.org/rfc/rfc6265.html#section-8.5)。题目应使用独立主机名，并核对平台 Cookie 的 Domain／Path 范围。此项是部署前检查，本轮没有改动 DNS、证书或线上 Cookie 配置。

切换实现或回退前，先在平台关闭题目公网访问并确认应用，再更新对应 Runner 配置／镜像。新版可按保留标签清理两种实现的旧容器；回退到没有 SSH 清理逻辑的更早版本前，务必先完成旧 Runner 的关闭和资源清理。不要用 `docker compose down -v`、删除原数据目录或重建 `1panel-network` 实施切换。旧会话可能留下少量已失效的状态目录（不含配对密钥），不能因此递归删除整个平台数据根目录。

本轮最终验证：1138 项非集成测试；Linux root／非 root 的实际生命周期与目录安全测试；Linux 上的 6 项注册验证；固定 Dockerfile 工具链产物的 3 项协议测试；旧 FRP 协调器的真实 PostgreSQL 回归。均已通过。Compose 叠加配置和 PowerShell 配对脚本也通过了解析检查。实际生命周期测试包括 12 秒慢控制操作期间续租、封禁／解封、共享客户端被外部删除后的恢复、遗留客户端／端点接管清理及旧回调幂等清理。
