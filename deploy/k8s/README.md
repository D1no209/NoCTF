# NoCTF Kubernetes

`base/` is the shared Kustomize topology. `overlays/kind` and
`overlays/production` select the environment. All Host roles automatically migrate before HTTP/consumers start. `init/` contains only the first-install S3 bucket Job; no database migration Job is required.

One Host image runs API and Worker Deployments and a single Runner StatefulSet,
each with explicit `Hosting__Roles__0`. One Runner owns the cluster resource
domain through a NATS KV fencing lease. **Adding Runner replicas does not add
capacity.** Add attested Runtime nodes and tune bounded startup/checker concurrency.

## Docker Desktop / Windows

Prerequisites: PowerShell 7, Docker Desktop Linux containers, kubectl, .NET 10;
at least 8 CPUs and 16 GiB assigned to Docker Desktop. Verified kind 0.33.0 and
Helm 3.19.0 are downloaded to ignored `.codex/k8s/tools`, not installed globally.
Bootstrap preserves `docker-desktop` and the previously selected context.

```powershell
pwsh -File deploy/k8s/scripts/Initialize-Kind.ps1
# If Windows cannot reach the Docker Hub chart registry:
pwsh -File deploy/k8s/scripts/Initialize-Kind.ps1 `
  -EnvoyChartRepository oci://docker.m.daocloud.io/envoyproxy/gateway-helm
pwsh -File deploy/k8s/scripts/Deploy.ps1 `
  -Context kind-noctf-dev -Environment kind -Build -Initialize
```

The dedicated `noctf-dev` cluster has a control plane, platform worker and Runtime
worker. It disables kindnet, retains kube-proxy, and installs Cilium 1.20.2
(`policyEnforcementMode=always`), Metrics Server 0.9.0 and Envoy Gateway 1.9.2.
Kubernetes 1.36.4 is pinned by digest. Pod CIDR is `10.245.0.0/16`, Service CIDR
`10.97.0.0/16`, DNS `10.97.0.10`. See `../shared/dependencies.lock.json`.

Bootstrap verifies the actual kubelet `podPidsLimit=256` before adding the Runtime
node's PID attestation label. Worker kubeadm component patches do not configure
the live kubelet; bootstrap writes and verifies the dedicated kind node config.
The platform worker gets at most 35% of VM CPU/memory. The sole attested Runtime
node reports its own NodeMetrics; a namespace requests quota caps Runtime
workloads at 50% of the VM, leaving 15% shared overhead. Multiple
kind nodes share one VM and do not multiply physical capacity. The insecure
kubelet TLS exception for Metrics Server is confined to local bootstrap.

Add `127.0.0.1 noctf.local files.noctf.local` to your hosts file and visit
`https://noctf.local:8443`. The development certificate is self-signed. CLI checks
use `curl.exe --insecure --resolve noctf.local:8443:127.0.0.1` instead of hosts edits.
Credentials/TLS keys are generated once under `.codex/k8s/noctf-dev`, restricted
to the current Windows account. Login is `admin`; read
`secrets.json` → `stringData.seed-admin-password` locally. Never commit these files.

RustFS 1.0.0 uses a dedicated persistent volume, non-default Secret credentials,
private console and S3 API. Bucket initialization runs the Host AWS SDK command.

NodePorts are `30000–30127`: Envoy owns 30000/30001 (host 8080/8443), leaving 126
dynamic challenge ports. Every mapping binds to 127.0.0.1. Windows cannot directly
reach kind node IPs. WSRX targets use internal Pod addresses. Push local fixtures
to `localhost:5001`; containerd maps that registry to `noctf-dev-registry`.

```powershell
# Update: preserve Secrets/PVCs; startup auto-migration; no pruning.
pwsh -File deploy/k8s/scripts/Deploy.ps1 -Context kind-noctf-dev -Environment kind -Build
pwsh -File deploy/k8s/scripts/Verify.ps1 -Context kind-noctf-dev
pwsh -File deploy/k8s/scripts/Diagnose.ps1 -Context kind-noctf-dev
kubectl --context kind-noctf-dev -n noctf port-forward service/grafana 3001:3000
```

Policies precede workload creation. `-Initialize` keeps application roles at zero,
initializes the bucket, waits for completion, then restores replicas.
Do not replace the staged sequence with a single directory-wide apply.
Diagnose excludes secrets, environment, logs and raw event messages.

## Production

Linux/WSL can run `bash deploy/configure.sh` against an existing explicit context; the wizard does not replace CNI or configure kubelets.

Install the pinned Cilium, Metrics Server and Envoy dependencies before deploying
NoCTF. Apply `deploy/k8s/platform/cilium/non-runtime-allow.yaml`; it excludes both platform and
challenge-runtime namespaces so it cannot override their default-deny rules.
Wait for Cilium and Metrics API readiness. Production kubelets require valid
serving certificates; do not copy the local insecure-TLS exception.

Label platform nodes `noctf.io/node-role=platform`. Set Runtime kubelets'
`podPidsLimit: 256`, verify configz/cgroup enforcement, then label only those nodes
`noctf.io/pod-pids-limit=256`. A label is an attestation, not a configuration change.

Replace `.invalid` domains and the production overlay ConfigMap patch with real
platform/file HTTPS origins, Runtime DNS, CORS/refresh origins, Webhook origin,
gateway Pod CIDR, cluster DNS/domain and protected infrastructure CIDRs.
Create `noctf-secrets`, `noctf-tls` out of band and
`runtime/challenge-registry` (`kubernetes.io/dockerconfigjson`). ImagePullSecrets
are platform settings shared by Runtime and Checker Pods, not authoring fields.

Production uses static StorageClass `noctf-retain`. Provision a Retain PV with
platform-node affinity per PVC, or replace the class with your CSI provisioner
while keeping Retain semantics. See [operations](../../specs/kubernetes-operations.md).
Node-local volumes require their original node after failure; stateful-service
HA is outside this first release. API/Worker default to 2 replicas, Runner to 1.
PostgreSQL, Redis, NATS JetStream, RustFS and monitoring use persistent single
instances. Loki, Grafana, Prometheus, NATS monitoring and RustFS console stay private.
Prometheus scrapes the dedicated 9464 role listeners and the private NATS exporter.
Grafana provisions application and Kubernetes scheduling dashboards. The legacy
infrastructure saturation panels require separately provisioned PostgreSQL/node
exporters; their absence is not evidence of spare capacity.

```powershell
pwsh -File deploy/k8s/scripts/Deploy.ps1 -Context YOUR_CONTEXT `
  -Environment production -Image YOUR_REGISTRY/noctf@sha256:YOUR_DIGEST -Initialize
```

Supply a real immutable digest; ordinary updates omit `-Initialize`. Generated monitoring ConfigMaps use content hashes; platform configuration changes trigger a checksum rollout. Secret changes require controlled rollouts. Runtime Provider/Pool and roles
apply after restart. Production retains the standard NodePort range. Runtime DNS
must reach a node serving those ports; budget ports per direct access entry.
Gateway serves HTTPS/S3, streaming uploads and WebSockets. It has a one-hour
listener idle timeout and no whole-request timeout. Trust only actual gateway
sources rather than client-provided forwarded headers.

Platform egress is limited to declared dependencies. Add exact FQDN/port policies
for SMTP, SSO, external verification and authorized Webhook destinations; changing
a dependency domain requires matching DNS/network policy updates. Do not replace
them with unrestricted egress. Use the checked-in policy examples.

## Acceptance

Rebuild TUnit. `Test-Gameplay.ps1` runs the existing four full-boundary scenarios
with registry fixtures and pinned development TLS verification.
`Measure-Capacity.ps1` verifies real provider startup, direct HTTP, replay and
cleanup with a port-80 fixture using 32 MiB/50m CPU:

```powershell
pwsh -File deploy/k8s/scripts/Measure-Capacity.ps1 -Context kind-noctf-dev `
  -Image localhost:5001/noctf-capacity:dev -PublicHost 127.0.0.1
# PowerShell 7/.NET 10 on an adequately provisioned isolated Linux cluster:
pwsh -File deploy/k8s/scripts/Measure-Capacity.ps1 -Context capacity-cluster `
  -Image YOUR_TEST_IMAGE -PublicHost YOUR_NODE_DNS -Counts 100,500,1000 `
  -ImagePullSecrets challenge-registry
pwsh -File deploy/k8s/scripts/Report-Capacity.ps1 -Context capacity-cluster `
  -TargetPods 1000 -ServiceCpuMillicores 500 -ServiceMemoryMiB 128
pwsh -File deploy/k8s/scripts/Test-Recovery.ps1 -Context kind-noctf-dev
pwsh -File deploy/k8s/scripts/Test-Provider.ps1 -Context kind-noctf-dev
pwsh -File deploy/k8s/scripts/Test-PidLimit.ps1 -Context kind-noctf-dev
pwsh -File deploy/k8s/scripts/Test-Scaling.ps1 -Context kind-noctf-dev
```

Reports record target/started/reachable/failed counts, p50/p95 startup and cleanup
duration. Insufficient Pod slots create a not-run report with 20% headroom. Check
CPU, memory and NodePort budgets independently. The fixture is lifecycle evidence,
not arbitrary challenge sizing or gameplay throughput evidence. New gauges expose
allocatable slots, managed/Pending/Unschedulable/image-pull-blocked Pods and
Runtime NodePorts; EF remains the authoritative allocation ledger.
Record passed/failed/not-run phases explicitly. Local 50-Pod success does not
prove 1,000-Pod production capacity. Isolation/recovery and cutover are in the
[operations runbook](../../specs/kubernetes-operations.md).

`Report-Capacity.ps1` counts remaining requests, Pod slots and global NodePorts
with 20% headroom. Its service CPU/memory are planning inputs, not measured
workload needs; verify quotas, utilization and per-node placement separately.
`Test-Recovery.ps1` injects stale metrics, role/NATS restarts and Redis cache loss
only into the dedicated development cluster. Rerun gameplay afterward.
Kubernetes integration tests additionally verify service DNS, cross-instance and
platform isolation, and large/invalid/cancelled checker archive cleanup. The
CTF scenario checks refresh origins, administrator log access and a scoreboard
notification through the Gateway WebSocket listener.
