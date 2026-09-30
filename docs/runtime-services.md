# 容器 Runtime 与命名服务

容器环境统一使用 `Container` 类型。一个 Runtime 包含 1–64 个服务；一个服务就是单容器环境。服务名称使用唯一的小写 DNS label，最长 63 字符。服务之间按名称通信，例如 `web` 访问 `db:5432`。每项服务只需名称和镜像，命令、参数、环境变量、内部端口、Flag 环境变量和资源均可选。

以下是 CTF 模板定义中的 Runtime 配置。完整请求仍需包含题目模式和对应模式分支。

```json
{
  "kind": "Container",
  "allocation": "PerTeam",
  "flagSource": "PerTeam",
  "egressPolicy": "Isolated",
  "ttlSeconds": 3600,
  "operationTimeoutSeconds": 120,
  "container": {
    "services": [
      {
        "name": "web",
        "image": "registry.example/challenge:web",
        "cpuCores": 0.5,
        "memoryMiB": 512,
        "environment": { "DATABASE_HOST": "db" },
        "flagEnvironmentVariableName": "FLAG"
      },
      {
        "name": "db",
        "image": "registry.example/challenge:db",
        "cpuCores": 0.25,
        "memoryMiB": 256
      }
    ]
  },
  "urlBindings": [
    {
      "serviceName": "web",
      "containerPort": 8080,
      "urlTemplate": "http://{HOST}:{PORT}/",
      "exposure": "OwnerOnly",
      "isControlCheck": false
    }
  ]
}
```

`urlBindings` 是唯一的对外入口清单。每项入口引用一个服务和端口；服务本身不再配置公开端口或宿主端口。同一服务、同一端口的多个显示模板共享一次发布，不同服务可以监听同一个端口号。显示模板可使用 `{HOST}`、`{PORT}`，也可以是 `nc {HOST} {PORT}`。KoH 的 `isControlCheck=true` 入口使用实际内部服务地址，不会因为控制检查而增加公开端口。

每个服务默认 0.5 核、512 MiB。CPU 步长为 0.001 核，内存使用正整数 MiB。Runtime 的容量预留是服务资源之和；没有另一份容器总资源配置。`command` 和 `arguments` 分别覆盖镜像 entrypoint 和参数，未配置时保留镜像默认值。PID 上限由 `Runtime:Execution:ProcessesPerService` 统一配置，默认 256；Worker、Runner 和 Kubernetes kubelet 的实际 Pod PID 限制必须一致。Runner 注册携带该值，配置不一致的 Runner 不接收该请求。

AWD 的 `checker.targetServiceName` 和 `awd.flagInjection.serviceName` 必须引用已定义的服务。`PerTeam` Flag 至少选择一个服务的 `flagEnvironmentVariableName`，平台只向选择的服务注入。AWDP 和 CTF Patch 修复验证仍要求恰好一个服务，并声明一个供 Checker 使用的内部端口。

不接受 Compose YAML、任意 Kubernetes 清单、volumes、depends_on、replicas 或用户指定的容器名称。每个命名服务只有一个容器或 Pod，镜像应自行处理依赖服务尚未启动的情况。安全开关和 Checker 的 allow-root 配置已删除，运行身份遵循镜像默认用户。

## 部署与访问

- Docker 单服务复用部署级题目 bridge，不创建实例专属网络，也不注册重复的 `main` / `target` alias。共享题目网络内不同实例可以互通。
- Docker 多服务使用一个实例专属 bridge，服务名作为 network alias。容器直接发布自己的端口，宿主端口请求 `0`，由 Docker 随机分配。停止、失败回滚和重置只清理实例资源。
- 部署必须预先提供独立的普通题目 bridge 和固定 internal callback bridge。前者与数据库、Redis、NATS 所在业务网络分开；后者供 Checker 与 API 回调网关通信。平台部署清单会创建它们；Runner 不在每次执行时创建网络。停止单服务或 Checker 不删除这些共享资源。
- WSRX 和 Worker 的控制检查使用服务实际内部地址。Docker 网关在共享网络上保持连接，多服务实例按需连接专属网络；Kubernetes 使用平台网关标签和 NetworkPolicy 放行。
- Kubernetes 单服务使用一个 Pod，仅为公开入口创建 NodePort Service；无公开入口时不创建 Service。多服务额外创建一个 headless Service，通过 hostname、subdomain 和 DNS 搜索域解析实例内服务名。所有资源位于部署既有 Runtime namespace。
- Docker 仅支持 `Isolated`，它不保证禁止公网访问；Kubernetes 保留 `Isolated` 和 `InternetOnly` 的既有出网限制。

## 验证与版本切换

`NamedContainerRuntimeTests` 验证入口去重、WSRX、回滚和容量单位。`KubernetesNamedServicesTests` 验证 SDK 生成的 Pod、发现资源、NodePort、重放和清理；它不能证明真实集群 DNS 或 CNI 行为。

真实 Docker 测试使用 `DockerNamedServicesIntegrationTests`，需要可用 daemon。真实 Kubernetes 测试使用 `KubernetesNamedServicesIntegrationTests`，需要配置 kube context，并设置 `NOCTF_KUBERNETES_INTEGRATION=true`、`NOCTF_KUBERNETES_PUBLIC_HOST`、`NOCTF_KUBERNETES_POD_PIDS_LIMIT` 和 `NOCTF_KUBERNETES_CLUSTER_DNS`；集群须实际执行 NetworkPolicy，且节点 PID 限制标签与配置一致。

这次版本不支持旧容器定义、旧 Compose 类型、旧 Receipt 或旧消息。切换前使用旧版本停止并清理全部旧 Runtime，排空相关持久消息，再整体更新 Host、前端和定义。EF 迁移及快照由 `dotnet ef` 生成，迁移会删除旧容器配置字段。

已有单容器生产数据通过一次性部署工具 `backend/scripts/Migrate-SingleContainerRuntime.py` 转换：先备份数据库并导出旧定义、内部端口、命令、环境变量、实例和 Receipt；在数据库副本上执行新 EF 迁移及回填，核对数量和引用；停机后重复执行正式迁移。旧资源精确换算为核数和 MiB，定义转换为 `main` 服务，历史 Receipt 和端口保留。该工具不支持活动实例或多容器旧配置，也不增加长期 API 兼容层。部署备份和回填 SQL 必须使用仅管理员可读的权限，因为环境变量可能含有敏感内容。
