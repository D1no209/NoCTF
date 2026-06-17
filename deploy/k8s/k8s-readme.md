# NoCTF Kubernetes Deployment Guide

## Prerequisites

- Kubernetes cluster (v1.25+)
- `kubectl` configured to point at your cluster
- NGINX Ingress Controller installed
- Metrics Server installed (required for HPA)
- `/etc/hosts` entry: `<ingress-ip> noctf.local`

## Creating Real Secrets

**Never commit real secrets.** Before deploying, replace the placeholder values in `secret.yaml`:

```bash
# Generate base64-encoded values
echo -n 'your-jwt-secret-at-least-32-chars' | base64
echo -n 'your-db-password' | base64
echo -n 'your-minio-access-key' | base64
echo -n 'your-minio-secret-key' | base64
```

Then edit `secret.yaml` and replace each `REPLACE_ME_BASE64` with the actual base64 output.

Alternatively, use `kubectl create secret` directly (recommended):

```bash
kubectl create secret generic noctf-secrets \
  --namespace noctf \
  --from-literal=jwt-secret='your-jwt-secret-at-least-32-chars' \
  --from-literal=db-password='your-db-password' \
  --from-literal=minio-access-key='your-minio-access-key' \
  --from-literal=minio-secret-key='your-minio-secret-key'
```

## Building Images

```bash
# From repo root
docker build -t noctf-backend:latest ./backend
docker build -t noctf-frontend:latest ./frontend
```

For local clusters (kind/minikube), load images:

```bash
# kind
kind load docker-image noctf-backend:latest
kind load docker-image noctf-frontend:latest

# minikube
minikube image load noctf-backend:latest
minikube image load noctf-frontend:latest
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

# 4. Stateful services
kubectl apply -f postgres-deployment.yaml
kubectl apply -f postgres-service.yaml
kubectl apply -f redis-deployment.yaml
kubectl apply -f redis-service.yaml

# 5. Application
kubectl apply -f backend-deployment.yaml
kubectl apply -f backend-service.yaml
kubectl apply -f backend-hpa.yaml
kubectl apply -f frontend-deployment.yaml
kubectl apply -f frontend-service.yaml

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
kubectl get hpa -n noctf
```

## AWD Challenge Mode (Docker-in-Docker)

AWD game mode requires Docker socket access for spawning challenge containers. This is **disabled by default** for security.

To enable on a privileged node, uncomment the `volumeMounts` and `volumes` sections in `backend-deployment.yaml`:

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

The backend pod will also need a privileged security context or appropriate RBAC. Consider using a dedicated node pool with taints for AWD workloads.

## Network Policy Notes

The `networkpolicy.yaml` enforces a default-deny posture:
- All ingress/egress is denied by default
- Backend can reach postgres (5432) and redis (6379)
- Frontend accepts traffic from anywhere on port 80
- Backend accepts traffic only from the ingress-nginx namespace
- AWD challenge pods accept traffic only from pods labeled `app=noctf-checker`
- DNS (port 53) egress is allowed for all pods

The ingress-nginx namespace label `kubernetes.io/metadata.name: ingress-nginx` must exist. Verify with:

```bash
kubectl get namespace ingress-nginx --show-labels
```
