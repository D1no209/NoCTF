# NoCTF Kubernetes Deployment Guide

## Prerequisites

- Kubernetes cluster (v1.25+) with Cilium `1.19.6`
- `kubectl` configured to point at your cluster
- Helm configured with the Cilium chart repository
- NGINX Ingress Controller installed
- `/etc/hosts` entry: `<ingress-ip> noctf.local minio.noctf.local`

Kubernetes Runner Pools require Cilium with policy enforcement enabled for every
endpoint, including initializing endpoints. For k3s, disable Flannel and its
built-in NetworkPolicy controller when creating the cluster, then install the
pinned Cilium version:

```bash
# k3s server flags:
# --flannel-backend=none --disable-network-policy

helm repo add cilium https://helm.cilium.io/
helm upgrade --install cilium cilium/cilium \
  --version 1.19.6 \
  --namespace kube-system \
  --values ../cilium/values.yaml
kubectl apply -f ../cilium/non-runtime-allow.yaml
```

The checked-in values use k3s's default `10.42.0.0/16` Pod CIDR. Override that
value for clusters with a different Pool CIDR. They retain k3s kube-proxy; if the
cluster disables kube-proxy, configure and validate Cilium's replacement mode
separately. Do not start the NoCTF Runner until Cilium reports Ready.
The clusterwide policy preserves existing network behavior for endpoints with a
resolved Namespace identity outside Namespaces labeled
`noctf.io/purpose=challenge-runtime`; it deliberately does not select
`reserved:init` or any Runtime Pool endpoint.

## Creating Real Secrets

**Never commit real secrets.** `secret.yaml` uses `stringData` placeholders for local smoke tests. For real clusters, create the Secret directly:

```bash
kubectl create secret generic noctf-secrets \
  --namespace noctf \
  --from-literal=jwt-secret='your-jwt-secret-at-least-32-chars' \
  --from-literal=db-password='your-db-password' \
  --from-literal=seed-admin-password='your-initial-admin-password' \
  --from-literal=runner-scoring-key='your-runner-scoring-jwt-key-at-least-32-chars' \
  --from-literal=email-verification-encryption-key='base64-encoded-32-byte-key' \
  --from-literal=minio-access-key='your-minio-access-key' \
  --from-literal=minio-secret-key='your-minio-secret-key'
```

Apply generated secret output with `--dry-run=client -o yaml | kubectl apply -f -`
when the empty Secret manifest has already been installed. Create the TLS secret
before exposing the Ingress:

```bash
kubectl create secret tls noctf-tls --namespace noctf --cert=tls.crt --key=tls.key
```

## Building Images

```bash
# From repo root
docker build -f backend/Dockerfile --target api -t noctf-backend:latest .
docker build -f backend/Dockerfile --target worker -t noctf-worker:latest .
docker build -f backend/Dockerfile --target runner -t noctf-runner:latest .
docker build -f backend/Dockerfile --target host -t noctf-host:latest .
```

All third-party build bases and stateful-service images in the checked-in
manifests use an exact version tag plus the multi-architecture manifest digest.
Kompose is downloaded at the fixed version and verified against the official
release SHA-256 for the BuildKit target architecture before it becomes
executable. To update one of these dependencies, obtain the digest from its
official registry, update the readable version tag and digest atomically, and
run `DeploymentTopologyTests`; never move the tag without its digest or replace
it with `latest`.

The `noctf-*:latest` names below are local build outputs, not third-party pulls.
Production delivery must retag those outputs with the platform pre-release
version and immutable registry digest, then replace the three application image
references in the environment-specific manifest before rollout.

For local clusters (kind/minikube), load images:

```bash
# kind
kind load docker-image noctf-backend:latest
kind load docker-image noctf-worker:latest
kind load docker-image noctf-runner:latest

# minikube
minikube image load noctf-backend:latest
minikube image load noctf-worker:latest
minikube image load noctf-runner:latest
```

## Apply Order

Apply manifests in this order to satisfy dependencies:

```bash
# 1. Namespaces and the Runtime baseline policy first
kubectl apply -f namespace.yaml
kubectl apply -f 00-runtime-namespace.yaml
kubectl apply -f 01-runtime-networkpolicy.yaml

# 2. Enforce the platform Namespace default-deny and exact allow rules before
#    creating any workload Pod
kubectl apply -f networkpolicy.yaml

# 3. Config and secrets
kubectl apply -f secret.yaml
kubectl apply -f configmap.yaml

# 4. Storage
kubectl apply -f postgres-pvc.yaml
kubectl apply -f redis-pvc.yaml
kubectl apply -f minio-pvc.yaml

# 5. Stateful services
kubectl apply -f postgres-deployment.yaml
kubectl apply -f postgres-service.yaml
kubectl apply -f redis-deployment.yaml
kubectl apply -f redis-service.yaml
kubectl apply -f minio-deployment.yaml
kubectl apply -f minio-service.yaml
kubectl apply -f minio-init-job.yaml

# 6. Database migration
kubectl apply -f migration-job.yaml
kubectl wait --for=condition=complete job/noctf-db-migrate -n noctf --timeout=300s

# 7. The distributed application-role topology
kubectl apply -f backend-deployment.yaml
kubectl apply -f backend-service.yaml
kubectl apply -f worker-deployment.yaml
kubectl apply -f runner-rbac.yaml
kubectl apply -f runner-service.yaml
kubectl apply -f runner-deployment.yaml

# 8. Public ingress (the internal policies are already active)
kubectl apply -f ingress.yaml
```

Before applying `configmap.yaml`, replace `ForwardedHeaders__KnownNetworks__0`
with the exact ingress-controller Pod CIDR and replace
`ForwardedHeaders__AllowedHosts__0` with the public NoCTF host. The example CIDR
is deliberately not a production default. Do not use a trust-all forwarded-header
setting. TLS, HTTP-to-HTTPS redirects, HSTS and WebSocket upgrade remain owned by
the ingress; the backend Service and scoring callbacks stay HTTP-only inside the
cluster.

Email delivery is optional and remains fail-closed under the default-deny policy.
The stock DNS policy permits only `*.svc.cluster.local` lookups through cluster
DNS; every external dependency therefore needs its own exact-FQDN policy.
Before enabling email verification or password reset delivery, copy
`smtp-egress.example.yaml` outside the repository, replace both occurrences of
`smtp.example.com` and port `587` with the exact FQDN and TCP port configured in
Platform Settings, and apply that copy. Reapply the policy whenever the SMTP
endpoint changes. Add a
second exact `matchName` or port entry only when the configured mail service
actually requires it; do not use a wildcard FQDN, `0.0.0.0/0`, or commit the
production endpoint or credentials.

Human verification is also optional and fail-closed. Keep
`HumanVerification__Provider` as `None`, or set it to `Cap` or `Turnstile` and
add the selected provider's public settings to `configmap.yaml`:

- Cap: `HumanVerification__Cap__ServerUrl` and `HumanVerification__Cap__SiteKey`.
- Turnstile: `HumanVerification__Turnstile__SiteKey` and
  `HumanVerification__Turnstile__AllowedHostnames__0`.

Add only the corresponding `cap-secret` or `turnstile-secret` key to
`noctf-secrets`. Copy `human-verification-egress.example.yaml` outside the
repository, remove the unused provider policy, replace the Cap example FQDN
and port when applicable, then apply it before enabling the provider. The
Turnstile policy admits only `challenges.cloudflare.com:443`. Cap Standalone is
operated separately; enable its version-pinned asset server so the browser can
load the solver WASM from the same instance, and restrict its CORS list to the
actual NoCTF origins.

Do not replace the staged sequence with a single directory-wide apply. The
platform `networkpolicy.yaml` must be active before any `noctf` workload Pod is
created. When this cluster hosts Kubernetes Runner Pools, the `runtime`
Namespace and `noctf-runtime-baseline-deny` policy must likewise exist before
the Runner starts and before any challenge workload is created.

## Verify Deployment

```bash
kubectl get all -n noctf
kubectl get ingress -n noctf
```

## Kubernetes Runner

The default manifests run the Runner as a StatefulSet in `noctf`, use the stable Pod name as
`Runner__Id`, and grant its ServiceAccount a
namespace-scoped Role in the shared `runtime` namespace. Each Compose runtime gets
immutable `rt-*` resources, a headless DNS Service, a NetworkPolicy, Deployments,
and platform-owned dynamic NodePort Services. Challenge definitions cannot create
namespaces, Ingresses, NodePorts, or LoadBalancers.

The deployment-owned `noctf-runtime-baseline-deny` NetworkPolicy selects every Pod
in `runtime` and denies ingress and egress before per-Runtime allow policies are
added. A Kubernetes Runner validates this policy and the
`kube-system/cilium-config` `enable-policy=always` setting at startup. It also
checks that `Runtime__Kubernetes__ClusterDnsServiceAddress` matches the actual
`kube-system/kube-dns` ClusterIP. The Runner never creates or modifies any of
these deployment-owned resources. Cilium `always` enforcement is required so
initializing endpoints are fail-closed; the k3s built-in NetworkPolicy controller
is not a supported production security boundary for Runtime Pools.

Important ConfigMap values:

- `Runtime__Kubernetes__Namespace`: shared challenge namespace
- `Runtime__Kubernetes__PublicHost`: host/IP used in dynamic NodePort URLs
- `Runtime__Kubernetes__ClusterDomain`: actual cluster DNS domain
- `Runtime__Kubernetes__ClusterDnsServiceAddress`: exact IPv4 ClusterIP of
  `kube-system/kube-dns`; the checked-in `10.43.0.10` is the k3s default and must
  be changed for clusters that use another Service CIDR
- `RunnerScoring__CallbackBaseUrl`: full API Service FQDN; update its final DNS
  suffix when the cluster domain is not `cluster.local`
- `Runtime__Kubernetes__CallbackNamespaceLabelKey/Value`: exact label selector
  for the Namespace containing the API callback Pod
- `Runtime__Kubernetes__CallbackPodLabelKey/Value`: exact label selector for
  the API callback Pod; it must match `backend-deployment.yaml`
- `Runtime__Kubernetes__PodPidsLimit`: must equal the kubelet Pool-wide value
- `Runtime__Kubernetes__NetworkPolicyRequired`: must be `true`
- `Runtime__Kubernetes__ProtectedCidrs__*`: every Pod, Service, node-management,
  platform-infrastructure, and other non-public IPv4 CIDR that challenge workloads
  must never reach

Before starting a Runner, verify the kubelet `PodPidsLimit` on every node that
may host challenge workloads, then attest the exact value with a node label. For
the checked-in value of `512`:

```bash
kubectl label node <runtime-node> noctf.io/pod-pids-limit=512 --overwrite
```

The Runner startup check requires at least one Ready, schedulable node carrying
the exact configured label, and every platform-created Runtime Pod has the same
label as a mandatory `nodeSelector`. A new or replacement node therefore cannot
host challenge workloads until an operator verifies its kubelet configuration
and labels it. The label is an explicit operator attestation; it does not change
the kubelet setting.

The Runner image contains Kompose `v1.38.0` at `/usr/local/bin/kompose`.
The cluster must use Cilium with `policyEnforcementMode=always`; the Runner checks
the resulting Cilium ConfigMap and kube-dns Service through read-only,
resource-name-scoped RBAC grants. Node PID attestation adds read-only `get/list`
access to Node metadata; the Runner cannot modify Nodes.
`NetworkPolicyRequired` remains an operator attestation rather than a dataplane
probe. `ProtectedCidrs` is required even though the runtime also blocks common
private and special-use IPv4 ranges; add all cluster-specific ranges that are not
covered by those built-ins. Each Runtime policy allows DNS through both the
CoreDNS Pod selector and the configured kube-dns ClusterIP `/32`; the explicit
Service address is required because Service NAT and NetworkPolicy evaluation
order is dataplane-dependent.

Scale a Pool with `kubectl scale statefulset/runner -n noctf --replicas=N`. Each ordinal has a
stable distinct RunnerId. Do not replace this with a Deployment whose replicas share a configured
RunnerId.

For a composable topology, deploy the `host` image and set indexed `Hosting__Roles__*` values to any
non-empty subset of `Api`, `Worker`, and `Runner`. The Pod ServiceAccount, network policy, secrets,
volumes, and egress must be the union required by its roles. A Host containing Runner should use the
Runner StatefulSet identity pattern; a Host without Runner can use an ordinary Deployment.

Smoke test:

```bash
kubectl get pods -n noctf
kubectl logs -n noctf deploy/worker
kubectl logs -n noctf deploy/runner
kubectl get deployment,service,networkpolicy -n runtime
```

Start and stop a Compose challenge from the UI, then verify that only resources
with the exact RuntimeInstanceId and Generation appear and are removed from
`runtime`.

## Docker Runner / AWD Privileged Node Mode

AWD game mode requires Docker socket access for spawning challenge containers. This is **disabled by default** for security.

To enable on a privileged node, mount the Docker socket only in a dedicated runner deployment:

```yaml
volumeMounts:
  - name: docker-sock
    mountPath: /var/run/docker.sock
volumes:
  - name: docker-sock
    hostPath:
      path: /var/run/docker.sock
      type: Socket
```

Do not mount the Docker socket into backend pods. Consider using a dedicated node pool with taints for Runner workloads.

## Network Policy Notes

The `networkpolicy.yaml` enforces a default-deny posture:
- All ingress/egress is denied by default
- Backend can reach PostgreSQL (5432), Redis (6379), and MinIO (9000)
- Worker can reach PostgreSQL (5432), Redis (6379), and MinIO (9000)
- Runner can reach PostgreSQL (5432), Redis (6379), and Cilium's exact
  `kube-apiserver` identity on 443/6443; those ports are no longer open to
  arbitrary Internet addresses
- Backend and Worker can reach an external SMTP server only after the operator
  applies an exact-FQDN/port `CiliumNetworkPolicy` derived from
  `smtp-egress.example.yaml`
- A composable Host needs the union of its selected role policies; do not grant Runner RBAC to an
  API/Worker-only Host
- Backend accepts traffic only from the ingress-nginx namespace and serves both API and SPA static files
- Backend accepts internal scoring callbacks only from the `runtime` Namespace
  and Pods whose purpose is `awd-checker` or `awdp-checker`, on TCP 8080
- Runtime workloads are isolated by the baseline and per-Runtime policies in the `runtime` namespace
- DNS (port 53) egress is allowed for all pods

The ingress-nginx namespace label `kubernetes.io/metadata.name: ingress-nginx` must exist. Verify with:

```bash
kubectl get namespace ingress-nginx --show-labels
```
