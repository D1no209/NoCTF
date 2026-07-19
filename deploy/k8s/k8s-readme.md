# NoCTF Kubernetes Deployment Guide

## Prerequisites

- Kubernetes cluster (v1.25+)
- `kubectl` configured to point at your cluster
- NGINX Ingress Controller installed
- `/etc/hosts` entry: `<ingress-ip> noctf.local minio.noctf.local`

## Creating Real Secrets

**Never commit real secrets.** `secret.yaml` uses `stringData` placeholders for local smoke tests. For real clusters, create the Secret directly:

```bash
kubectl create secret generic noctf-secrets \
  --namespace noctf \
  --from-literal=jwt-secret='your-jwt-secret-at-least-32-chars' \
  --from-literal=db-password='your-db-password' \
  --from-literal=seed-admin-password='your-initial-admin-password' \
  --from-literal=runner-api-key='your-runner-internal-api-key' \
  --from-literal=runner-scoring-key='your-runner-scoring-jwt-key-at-least-32-chars' \
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
docker build -f backend/Dockerfile --target runner -t noctf-runner:latest .
```

For local clusters (kind/minikube), load images:

```bash
# kind
kind load docker-image noctf-backend:latest
kind load docker-image noctf-runner:latest

# minikube
minikube image load noctf-backend:latest
minikube image load noctf-runner:latest
```

## Apply Order

Apply manifests in this order to satisfy dependencies:

```bash
# 1. Namespace first
kubectl apply -f namespace.yaml

# 2. Config and secrets
kubectl apply -f secret.yaml
kubectl apply -f configmap.yaml

# 3. Storage
kubectl apply -f postgres-pvc.yaml
kubectl apply -f redis-pvc.yaml
kubectl apply -f minio-pvc.yaml

# 4. Stateful services
kubectl apply -f postgres-deployment.yaml
kubectl apply -f postgres-service.yaml
kubectl apply -f redis-deployment.yaml
kubectl apply -f redis-service.yaml
kubectl apply -f minio-deployment.yaml
kubectl apply -f minio-service.yaml
kubectl apply -f minio-init-job.yaml
kubectl apply -f runner-rbac.yaml
kubectl apply -f runner-deployment.yaml
kubectl apply -f runner-service.yaml

# 5. Database migration and application
kubectl apply -f migration-job.yaml
kubectl wait --for=condition=complete job/noctf-db-migrate -n noctf --timeout=300s
kubectl apply -f backend-deployment.yaml
kubectl apply -f backend-service.yaml

# 6. Networking
kubectl apply -f ingress.yaml
kubectl apply -f networkpolicy.yaml
```

Or apply everything at once (order is handled by K8s):

```bash
kubectl apply -f deploy/k8s/
```

## Verify Deployment

```bash
kubectl get all -n noctf
kubectl get ingress -n noctf
```

## Kubernetes Runner

The default manifests run `Runner__Provider=Kubernetes`. The runner uses its ServiceAccount to create one namespace per dynamic challenge instance, then applies ResourceQuota, LimitRange, NetworkPolicy, Deployments/Jobs, Services, and optional Ingress objects.

Important ConfigMap values:

- `K8s__PublicEntry`: host/IP shown for NodePort entries
- `K8s__IngressBaseDomain`: wildcard domain for generated Ingress hosts
- `K8s__DefaultExposure`: `NodePort`, `Ingress`, or `ClusterIP`
- `K8s__NetworkMode`: `Isolated` or `Open`

For private registries, add `K8s__Registries__0__Registry`, `K8s__Registries__0__UserName`, and `K8s__Registries__0__Password` through a Secret or deployment environment override.

Smoke test:

```bash
kubectl get pods -n noctf
kubectl logs -n noctf deploy/runner
kubectl get ns | grep noctf-inst
```

Start and stop a dynamic challenge from the UI, then verify that a `noctf-inst-*` namespace appears and is deleted.

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
- Backend can reach postgres (5432), redis (6379), MinIO (9000), and runner (8080)
- Backend accepts traffic only from the ingress-nginx namespace and serves both API and SPA static files
- AWD challenge pods accept traffic only from pods labeled `app=noctf-checker`
- DNS (port 53) egress is allowed for all pods

The ingress-nginx namespace label `kubernetes.io/metadata.name: ingress-nginx` must exist. Verify with:

```bash
kubectl get namespace ingress-nginx --show-labels
```
