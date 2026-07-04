# Penetration Operations

This page covers operations and troubleshooting for Penetration Challenge instances.

## Runtime Boundary

Penetration Challenge builds a Compose document from the saved topology and starts it through the platform container manager. With `Runner__Provider=Docker`, the Runner executes Docker Compose. With `Runner__Provider=Kubernetes`, the Runner translates the supported Compose subset into per-instance Kubernetes namespaces, Deployments, Services, NetworkPolicies, and optional Ingresses.

Recommended production posture:

- keep Docker access inside the runner boundary
- avoid mounting the Docker socket into the API process
- use a dedicated runner host or node pool for challenge workloads
- set `NOCTF_PUBLIC_HOST` / `InstanceAccess:PublicHost` for Docker/NodePort entries, or `K8s__IngressBaseDomain` for wildcard Ingress entries
- use private registries or allow-listed image sources for challenge images

## Instance Lifecycle

Player operations:

- start: creates a team/challenge Compose project, generates dynamic flags, and publishes the entry port
- stop: brings the Compose project down and deactivates active dynamic flags
- reset: destroys the current Compose project, regenerates dynamic flags, and starts a fresh one
- destroy: brings the Compose project down, clears entry information, and deactivates dynamic flags

The default instance TTL is 7200 seconds. The default action cooldown is 5 seconds.

The worker runs Penetration instance maintenance from the plugin registration. Each sweep:

- destroys expired Compose projects through the runner/container manager
- marks expired records as `Expired`
- deactivates dynamic flags for stopped, destroyed, expired, or missing instances
- syncs running instance container IDs and entry port mappings from Compose labels
- marks missing Compose projects as `Failed` and stopped projects as `Stopped`
- marks long-stuck starting/stopping/resetting/destroying records as `Failed`

Cleanup uses both the Compose project name and NoCTF labels (`competitionId`, `challengeId`, `teamId`, `instanceId`) to avoid touching unrelated projects.

## Common Failures

### Image Pull Fails

Symptoms:

- instance status becomes `Failed`
- competition log contains `penetration.instance.start_failed`

Checks:

- runner can reach the registry
- registry credentials are configured for the Docker daemon
- image name in the topology is correct
- proxy settings are available to the runner if required

### Entry URL Is Empty

Checks:

- at least one node has `isEntry: true`
- the entry node has a container port in `ports`, or the challenge has an exposed port
- `NOCTF_PUBLIC_HOST` / `InstanceAccess:PublicHost` is configured for remote players
- Docker Compose published the entry service port, or the Kubernetes Runner reported a NodePort/Ingress entry

### Old Flag Still Appears In A Container

Reset regenerates dynamic flags and starts a new Compose project. If a container still shows an old flag:

- confirm the reset request completed successfully
- confirm the old Compose project was brought down
- inspect runner logs for Compose cleanup failures
- ensure the challenge image does not cache generated flags in a persistent volume

### Wrong Flag Logs Must Not Leak Secrets

Submissions and competition logs store hash/length metadata for wrong Penetration flags. Do not copy raw container logs containing flags into public incident notes or support tickets.

### Compose Down Leaves Networks, Containers, Or Namespaces

Checks:

- `ComposeProjectName` in the instance record
- runner Docker daemon availability, or Kubernetes Runner RBAC for namespace deletion
- manual `docker compose -p <project> ps` and `docker compose -p <project> down --remove-orphans` on the runner host
- for Kubernetes, confirm the instance namespace uses the configured `K8s__NamespacePrefix` and `app.kubernetes.io/managed-by=noctf-runner`

## Admin Monitoring

Management APIs can list, inspect, reset, and destroy Penetration instances under:

```text
/api/admin/competitions/{competitionId}/penetration/instances
```

All management endpoints require `CanManageCompetitionAsync` for the competition.

The competition detail page includes a dedicated Range instances section. It can filter by Penetration challenge and team, show status, entry URL, reset count, expiration, last update, and last error, and trigger instance reset or destroy. Node-level log streaming is still a future enhancement.

## Current Limits

- Penetration ranges use the safe Compose subset; unsupported directives are rejected by the Kubernetes Runner
- environment-variable dynamic flag injection only
- no node-level log streaming UI yet
- Kubernetes quotas, limit ranges, and default network isolation are applied only when `Runner__Provider=Kubernetes`
