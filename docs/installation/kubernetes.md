# Kubernetes 安装

NoCTF 的 Kubernetes 部署将 Api、Worker、Runner 作为同一个 Host 镜像的不同角色运行。平台使用 `noctf` namespace，题目使用共享 `runtime` namespace，每服务一个 Pod，不为每个 Runtime 创建 namespace。

本页分为 Windows 本地 kind 验证和已有 Linux 生产集群。两条路线的 context、证书、持久化和网络条件不同。

## 本地 kind 验证环境

### 1. 准备 Windows 工具

安装 Docker Desktop 并选择 Linux containers，准备 PowerShell 7 和 kubectl。Docker Desktop 虚拟机至少分配 8 CPU / 16 GiB；否则 bootstrap 明确拒绝。按 [获取发布产物](./images.md)下载并解压部署配置包，节点需要能拉取本次 CI 的完整镜像。

在 PowerShell 7 中检查：

```powershell
$PSVersionTable.PSVersion
docker info --format '{{.NCPU}} CPUs / {{.MemTotal}} bytes'
kubectl version --client
```

进入包含 `deployment.json` 和 `deploy` 的配置包解压目录。确保本地端口 8080、8443、30002–30127 未被占用。脚本会下载并验证锁定的 Windows kind/Helm；不用手工配置另一个 `docker-desktop` 集群。

### 2. 初始化独立集群并部署

```powershell
pwsh -File deploy/k8s/scripts/Initialize-Kind.ps1
pwsh -File deploy/k8s/scripts/Deploy.ps1 -Context kind-noctf-dev -Environment kind -Image ghcr.io/d1no209/noctf@sha256:YOUR_IMAGE_DIGEST -Initialize
pwsh -File deploy/k8s/scripts/Verify.ps1 -Context kind-noctf-dev
```

bootstrap 创建专用 `noctf-dev`，安装锁定 Cilium、Metrics Server、Envoy Gateway，校验 PID 限制，保留现有 `docker-desktop` context。首次 `-Initialize` 会将应用角色保持为零，准备依赖、初始化 bucket，再启动各角色和迁移。

将镜像地址替换为配置包 `deployment.json` 中的完整 `image`。该命令由节点直接拉取预构建镜像，不加 `-Build`。依赖下载和调度需要时间；某步失败先检查对应错误和 [Diagnose](../operations/troubleshooting.md)，不要对集群全局 prune。

### 3. 访问与管理员登录

以管理员权限编辑 Windows hosts，添加：

```text
127.0.0.1 noctf.local files.noctf.local
```

访问 `https://noctf.local:8443`。本地开发证书为自签名，需要在隔离开发环境信任该证书或明确接受浏览器提示。生产不使用该证书。

管理员用户名为 `admin`；密码在配置包目录本地生成的 `.codex/k8s/noctf-dev/secrets.json` 的 `stringData.seed-admin-password` 中。文件受限于当前 Windows 账号，只在本地查看，不提交或复制到工单。

CLI 健康检查无需修改 hosts：

```powershell
curl.exe --insecure --resolve noctf.local:8443:127.0.0.1 https://noctf.local:8443/health/ready
```

`--insecure` 仅用于这里的自签名开发证书。Envoy 占用两个 NodePort，剩余 126 个本地动态题目端口；所有宿主映射绑定 127.0.0.1，因此不作为公网赛事服务。

### 4. 日常更新与诊断

```powershell
pwsh -File deploy/k8s/scripts/Deploy.ps1 -Context kind-noctf-dev -Environment kind -Image ghcr.io/d1no209/noctf@sha256:YOUR_NEW_IMAGE_DIGEST
pwsh -File deploy/k8s/scripts/Verify.ps1 -Context kind-noctf-dev
pwsh -File deploy/k8s/scripts/Diagnose.ps1 -Context kind-noctf-dev
kubectl --context kind-noctf-dev -n noctf port-forward service/grafana 3001:3000
```

普通更新省略 `-Initialize`，保留 Secret/PVC。题目测试镜像推送到 `localhost:5001`，集群 containerd 将它映射到独立的 kind Registry。正式功能仍需按 [验收](./verify.md)走完整比赛流程。

## 已有 Linux 生产集群

### 1. 明确集群前置条件

配置向导只部署 NoCTF，不安装 Kubernetes，不更换现有 CNI，不替你修改 kubelet。应由集群运维先准备以下条件：

| 条件 | 核对要求 |
| --- | --- |
| kubectl、Helm 与 kubeconfig | 使用显式生产 context，具备 namespace/RBAC/策略等权限 |
| Cilium | 与仓库锁定版本核对，`policyEnforcementMode=always` |
| Metrics Server | NodeMetrics API 可用，生产 kubelet 证书可信 |
| Envoy Gateway | 对应 Gateway API/Controller 就绪，公开 HTTPS 与 WebSocket 可用 |
| 平台节点 | `noctf.io/node-role=platform`，有平台服务与存储容量 |
| Runtime 节点 | 真实 kubelet `podPidsLimit: 256`，验证后再设置 attestation label |
| 持久存储 | 使用 Retain PV 或 Retain CSI StorageClass |
| 域名与 TLS | 平台/文件 Origin、Runtime DNS、有效证书及私钥 |
| Registry | Host 和题目镜像可拉取，私有仓库凭据可用 |

依赖版本和 digest 以 [dependencies.lock.json](https://github.com/D1no209/NoCTF/blob/HEAD/deploy/shared/dependencies.lock.json)为准，不用最新 Chart 无审核替换现有 CNI。首次部署到全新集群时，集群管理员按锁定版本安装组件；Cilium 参数可参考 [仓库 values](https://github.com/D1no209/NoCTF/blob/HEAD/deploy/k8s/platform/cilium/values.yaml)。

```bash
kubectl --context YOUR_CONTEXT get nodes
kubectl --context YOUR_CONTEXT -n kube-system get configmap cilium-config
kubectl --context YOUR_CONTEXT get --raw /apis/metrics.k8s.io/v1beta1/nodes
kubectl --context YOUR_CONTEXT get storageclass
```

为非 NoCTF namespace 应用仓库的 `non-runtime-allow.yaml` 前先由集群管理员审查现有策略。它有意排除平台和 runtime namespace，不能替代其默认拒绝策略。

### 2. 验证 PID 限制与节点标签

在 Runtime 节点实际 kubelet 配置中设置 `podPidsLimit: 256`，按集群维护流程重启/滚动处理 kubelet并验证 cgroup 执行。然后才为对应节点加标签：

```bash
kubectl --context YOUR_CONTEXT label node YOUR_PLATFORM_NODE noctf.io/node-role=platform
kubectl --context YOUR_CONTEXT get --raw /api/v1/nodes/YOUR_RUNTIME_NODE/proxy/configz
kubectl --context YOUR_CONTEXT label node YOUR_RUNTIME_NODE noctf.io/node-role=runtime noctf.io/pod-pids-limit=256
```

标签只是已经验证限制的声明，不能替代 kubelet 设置。向导会读取 configz，标签与真实配置不匹配时拒绝部署。

### 3. 准备持久卷与网络边界

生产默认 `noctf-retain` 为静态 StorageClass，需要预先按所有 PVC 容量创建 Retain PV，或使用自己的 Retain CSI class。节点本地 PV 还需要正确的 platform-node affinity；节点故障后不能假定卷可无损移到其他节点。

PostgreSQL、NATS、RustFS 和监控默认仍是单实例持久服务；Api/Worker 多副本不等于数据库或对象存储高可用。参考 [Kubernetes 运维契约](https://github.com/D1no209/NoCTF/blob/HEAD/specs/kubernetes-operations.md)制定恢复计划。

为 SMTP、SSO、人机验证、Webhook 等外部依赖准备精确 FQDN/port egress。平台内部数据网络不向题目开放；不要用 unrestricted egress 代替逐项策略。

### 4. 运行配置向导

按 [获取发布产物](./images.md)准备部署配置包。在目标 Linux/WSL 运维终端，从解压目录运行：

```bash
cd /opt/noctf-release
bash deploy/configure.sh
```

选择 `2`，已有 Linux Kubernetes。填写明确 context、Host digest、平台 Origin、Runtime 节点公网 DNS、Gateway Pod CIDR、管理员、cluster DNS IP/domain、受保护 CIDR、Retain StorageClass、覆盖平台/文件域名的 TLS 文件、私有镜像 `config.json` 和 S3 参数。

向导生成安装目录中的 Secret、配置 overlay 与渲染清单。已有部署会尝试从集群导入现有密钥，不能借升级重新生成全部密钥。先选择生成并校验，检查真实域名不再含 `.invalid`，所有公开 Origin 与 TLS 匹配。

```bash
python3 deploy/shared/configuration.py check /opt/noctf
python3 deploy/shared/configuration.py deploy /opt/noctf
```

助手按 namespace/策略/RBAC → Secret → 基础依赖 → 首次 bucket 初始化 → Host 的顺序部署。不要用一次 `kubectl apply -f deploy/k8s` 替代它。

### 5. 验证上线

```bash
kubectl --context YOUR_CONTEXT -n noctf get pods,pvc,services
kubectl --context YOUR_CONTEXT -n noctf rollout status deployment/backend
kubectl --context YOUR_CONTEXT -n noctf rollout status deployment/worker
kubectl --context YOUR_CONTEXT -n noctf rollout status statefulset/runner
curl -f https://noctf.example.com/health/ready
```

确认 PVC Bound、Pod Ready、Gateway HTTPS 与文件域名可用，再检查 Runtime namespace 的 Pod 调度、DNS、跨实例隔离与公网 NodePort。Runtime DNS 必须到达可提供对应 NodePort 的节点，NodePort 数量按每个直接访问入口预算。

需要脚本验收时可在具备 PowerShell 7 的运维环境运行 `Verify.ps1 -Context YOUR_CONTEXT -Environment production`。使用仓库 production overlay 的高级路径要先准备 Secret、TLS、Registry 与替换所有占位配置，再运行 `Deploy.ps1 -Environment production -Image ... -Initialize`；普通更新不加 `-Initialize`。不要将向导生成 overlay 与手工 overlay 混用。
