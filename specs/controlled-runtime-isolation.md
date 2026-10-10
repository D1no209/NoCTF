# Controlled Runtime isolation

This is a neutral Runtime capability keyed by opaque `ExecutionScopeId`; providers do not know Match or Round identifiers. Ordinary Runtime requests keep a null execution scope and retain their existing network defaults.

## Docker deployment requirements

Scoped Docker execution requires a distinct deployment-owned IPv4 bridge with inter-container connectivity disabled, and a running host-network NoCTF API gateway carrying the deployment label `noctf.io/execution-proxy-gateway=true`. It must serve the existing authenticated Runtime proxy/probe routes, using the same authentication signing and platform secret configuration. A plain TCP proxy or an unauthenticated HTTP forwarding service is insufficient.

Create the dedicated bridge:

```sh
docker network create --driver bridge --opt com.docker.network.bridge.enable_icc=false noctf-executions
```

Configure the Runner:

```text
Runtime:Docker:ExecutionNetwork=noctf-executions
Runtime:Docker:ExecutionProxyContainer=noctf-execution-gateway
```

The gateway must use Docker host networking on the same Docker daemon. Route Runtime proxy/probe HTTP requests and WebSocket upgrades to this gateway at the deployment ingress, retaining the original public origin, headers and authentication cookies. Its host port must be private to the ingress; this does not publish challenge service ports. Ordinary API/Worker/Runner roles, deployment authentication and TLS configuration still apply. These are prepared deployment requirements, not evidence of a production deployment.

Single-service scoped environments share the dedicated bridge. Multi-service environments keep a Runtime-owned bridge and service aliases; their services can communicate internally. Scoped services publish no host ports and drop NET_RAW. The ordinary challenge and callback bridges are not repurposed.

The adapter checks the bridge, gateway, scope labels, network membership, capabilities and port mappings. It also launches short-lived, resource-limited trusted probes: the host namespace must reach a probe server while a peer on the execution bridge must fail to reach it. This detects an engine that reports bridge options but does not enforce them. Probe results are cached for at most five seconds; infrastructure-owned probes have a bounded process lifetime and are removed using an independent cleanup budget.

Runner qualification is advertised with schema-3 NATS registration and a current resource-domain fencing token. Scoped admission requires that capability; a Running Runtime additionally needs a provider receipt matching its Runtime UUID and execution scope with Verified isolation and no published ports. A stale/failed qualification, missing receipt or mismatched scope cannot supply Ready. Unconfigured isolation does not make ordinary-mode Runner availability fail.

## Verification and remaining providers

Real Docker tests cover host-gateway access, peer rejection, single-service bridge reuse, multi-service discovery, cross-bridge rejection, no host publication, idempotent replay and deployment-bridge preservation after cleanup. Real PostgreSQL/NATS tests cover receipt qualification, current ownership fencing and rejected unverified provision results. The old ordinary Docker networking and Runtime writeback regressions also passed.

Kubernetes and VM controlled isolation still require their own enforced policies and actual provider tests. The base named-container adapter rejects scoped execution without provider verification; an interface, access-mode setting or configuration flag cannot certify those providers. Full ingress/gateway browser verification is part of the subsequent end-to-end deployment acceptance.

Official references: [Docker bridge driver](https://docs.docker.com/engine/network/drivers/bridge/), [Docker host networking](https://docs.docker.com/engine/network/drivers/host/), [Docker firewall rules](https://docs.docker.com/engine/network/firewall-iptables/).
