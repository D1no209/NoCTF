# Deployment Guide

This guide covers deploying NoCTF with Docker Compose (recommended for single-node setups) or Kubernetes (recommended for production).

## Docker Compose

The fastest way to get NoCTF running is with the provided `deploy/docker-compose.yml`.

### Services

| Service | Image | Purpose |
|---------|-------|---------|
| `postgres` | `postgres:16` | Primary database |
| `redis` | `redis:7` | SignalR backplane and leaderboard cache |
| `minio` | `minio/minio:latest` | S3-compatible object storage |
| `backend` | Built from `backend/Dockerfile` | NoCTF API and Vue SPA static files |
| `worker` | Built from `backend/Dockerfile` | Durable background task processor |
| `runner` | Built from `backend/Dockerfile` | Isolated Docker/K8s execution boundary |

### Steps

1. Copy the example environment file:

```bash
cp .env.example .env
```

2. Edit `.env` and set strong values for the required secrets:

```bash
POSTGRES_PASSWORD=change_me_strong_password
JWT_SECRET=change_me_at_least_32_chars_long_secret_key
MINIO_ROOT_PASSWORD=change_me_minio_password
RUNNER_API_KEY=change_me_runner_internal_api_key
```

3. Start the stack:

If Docker image pulls must use your local proxy on Windows PowerShell, set the proxy variables in the same terminal first:

```powershell
$env:HTTP_PROXY='http://127.0.0.1:7897'
$env:HTTPS_PROXY='http://127.0.0.1:7897'
$env:ALL_PROXY='http://127.0.0.1:7897'
```

```bash
cd deploy && docker compose up --build -d
```

4. Verify health:

```bash
curl http://localhost/api/health
```

5. Open the app at `http://localhost`.

### Notes

- The backend does not mount `/var/run/docker.sock`. Container access is isolated in the `runner` service; the worker calls runner over the internal Compose network.
- The Docker Compose API image builds the Vue SPA with Bun and serves the built `dist` from ASP.NET Core `wwwroot`, so no separate Nginx frontend container is required.
- Uploaded files are stored in the `backend_uploads` volume by default. If you prefer S3, change the storage provider configuration.
- Penetration Challenge ranges can run through the Docker Runner or the Kubernetes Runner. Set `NOCTF_PUBLIC_HOST` / `InstanceAccess:PublicHost` for Docker NodePort-style entry URLs, or `K8s:PublicEntry` / `K8s:IngressBaseDomain` for Kubernetes entries.

## Kubernetes

NoCTF includes a full set of K8s manifests under `deploy/k8s/`. These manifests deploy the platform services plus a Kubernetes-backed Runner that creates per-instance namespaces for dynamic challenge workloads.

### Prerequisites

- Kubernetes cluster (v1.25+)
- `kubectl` configured for your cluster
- NGINX Ingress Controller installed
- Metrics Server installed (required for HPA)

### Creating Secrets

Use `kubectl create secret` instead of editing `secret.yaml` to avoid committing credentials:

```bash
kubectl create namespace noctf

kubectl create secret generic noctf-secrets \
  --namespace noctf \
  --from-literal=jwt-secret='your-jwt-secret-at-least-32-chars' \
  --from-literal=db-password='your-db-password' \
  --from-literal=seed-admin-password='your-initial-admin-password' \
  --from-literal=runner-api-key='your-runner-internal-api-key' \
  --from-literal=minio-access-key='your-minio-access-key' \
  --from-literal=minio-secret-key='your-minio-secret-key'
```

### Build and Load Images

From the repo root:

```bash
docker build -f backend/Dockerfile --target api -t noctf-backend:latest .
docker build -f backend/Dockerfile --target worker -t noctf-worker:latest .
docker build -f backend/Dockerfile --target runner -t noctf-runner:latest .
```

For local clusters, load the images:

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

### Apply Manifests

Apply in order to satisfy dependencies, or apply the entire directory at once:

```bash
# Ordered apply
kubectl apply -f deploy/k8s/namespace.yaml
kubectl apply -f deploy/k8s/secret.yaml
kubectl apply -f deploy/k8s/configmap.yaml
kubectl apply -f deploy/k8s/postgres-pvc.yaml
kubectl apply -f deploy/k8s/postgres-deployment.yaml
kubectl apply -f deploy/k8s/postgres-service.yaml
kubectl apply -f deploy/k8s/redis-deployment.yaml
kubectl apply -f deploy/k8s/redis-service.yaml
kubectl apply -f deploy/k8s/minio-pvc.yaml
kubectl apply -f deploy/k8s/minio-deployment.yaml
kubectl apply -f deploy/k8s/minio-service.yaml
kubectl apply -f deploy/k8s/minio-init-job.yaml
kubectl apply -f deploy/k8s/runner-rbac.yaml
kubectl apply -f deploy/k8s/runner-deployment.yaml
kubectl apply -f deploy/k8s/runner-service.yaml
kubectl apply -f deploy/k8s/backend-deployment.yaml
kubectl apply -f deploy/k8s/backend-service.yaml
kubectl apply -f deploy/k8s/worker-deployment.yaml
kubectl apply -f deploy/k8s/backend-hpa.yaml
kubectl apply -f deploy/k8s/ingress.yaml
kubectl apply -f deploy/k8s/networkpolicy.yaml
```

Or apply everything at once:

```bash
kubectl apply -f deploy/k8s/
```

### Ingress Setup

The ingress is configured for `noctf.local`. Add it to your hosts file:

```bash
# Replace <ingress-ip> with the actual external IP of your ingress controller
echo "<ingress-ip> noctf.local minio.noctf.local" | sudo tee -a /etc/hosts
```

Verify:

```bash
kubectl get ingress -n noctf
kubectl get all -n noctf
kubectl get hpa -n noctf
```

### Kubernetes Runner

The Kubernetes Runner is selected with `Runner__Provider=Kubernetes` and uses either in-cluster ServiceAccount credentials or `K8s__KubeConfigPath` for an external runner. The included `runner-rbac.yaml` grants the Runner permission to create and delete per-instance namespaces, Deployments, Services, Jobs, NetworkPolicies, ResourceQuotas, LimitRanges, Secrets, and Ingresses.

Useful settings:

| Setting | Purpose |
|---------|---------|
| `K8s__PublicEntry` | Hostname/IP shown with NodePort entries |
| `K8s__IngressBaseDomain` | Wildcard domain for Ingress entries, e.g. `challenges.example.com` |
| `K8s__DefaultExposure` | `NodePort`, `Ingress`, or `ClusterIP` |
| `K8s__NetworkMode` | `Isolated` by default; `Open` allows outbound traffic |
| `K8s__Registries__0__Registry/UserName/Password` | Optional private registry credentials materialized as image pull Secrets |

Optional kind/minikube smoke flow:

```bash
kubectl apply -f deploy/k8s/
kubectl get pods -n noctf
kubectl port-forward -n noctf svc/backend-service 8080:8080
curl http://localhost:8080/api/health
```

Then create a dynamic challenge, start an instance, verify that the entry URL points to either the configured NodePort host or wildcard Ingress host, stop the instance, and confirm that the generated `noctf-inst-*` namespace is removed.

### Docker Runner On Privileged Nodes

AWD mode requires access to a container runtime for spawning challenge containers. This should be isolated to a dedicated Runner deployment.

To enable Docker-backed Runner on a privileged node, mount the Docker socket only in a dedicated runner deployment:

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

Use a dedicated node pool with taints/tolerations for Runner workloads so privileged container access is isolated from API, Worker, and database pods.

### Network Policy Notes

`networkpolicy.yaml` enforces a default-deny posture:

- Backend can reach postgres (5432) and redis (6379)
- Backend accepts traffic only from the `ingress-nginx` namespace
- AWD challenge pods accept traffic only from pods labeled `app=noctf-checker`
- DNS egress (port 53) is allowed for all pods

Make sure the `ingress-nginx` namespace has the label `kubernetes.io/metadata.name: ingress-nginx`:

```bash
kubectl get namespace ingress-nginx --show-labels
```
