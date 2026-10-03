# Kubernetes acceptance, persistence and cutover

This deployment uses independent test data. Do not share a database, bucket or
JetStream state with the existing installation. All commands need explicit
contexts. Normal deployment does not prune, rotate Secrets, delete PVCs. Host startup automatically migrates before serving requests or consuming messages.

## Real acceptance

Infrastructure Verify proves dependency preflight and HTTPS readiness. It does
not prove gameplay. Run Test-Gameplay.ps1 for CTF/AWD/AWDP/KoH, upload/download,
Flag injection, reset and checker callbacks. Tests create independent competitions
and users. Development TLS verification pins the generated certificate hash.

Run `Test-Provider.ps1` for native DNS, same-instance communication, cross-instance/
RustFS/metadata probes and large/invalid/cancelled archive handling. Run
`Test-PidLimit.ps1` for node-side cgroup enforcement under bounded process pressure.
`Test-Recovery.ps1` tests metrics expiry, role/dependency restarts and cache loss
on `kind-noctf-dev`; its report distinguishes readiness/persistence evidence from
in-flight message redelivery and business reconciliation.

Check refresh-cookie origin protection and persisted administrator logs. With
two API replicas, connect SignalR to each Pod and trigger one score change; each
connection must receive it once. Confirm NATS relay subscriptions and projections.

For isolation, create two independent Runtime instances and assert:

- Published NodePorts work; replay preserves Pod/port identity. Multi-service
  aliases resolve within the instance; a single service has no discovery Service.
- Runtime A cannot connect to B's internal IP, PostgreSQL, Redis, NATS, RustFS,
  Kubernetes API, node addresses or `169.254.169.254`.
- Same-instance traffic/DNS work. Isolated egress fails; InternetOnly still blocks
  protected infrastructure. Ordinary Runtime Pods cannot use Checker callbacks.
- The Pod's node-side cgroup `pids.max` is 256. Some kind/containerd mounts expose
  an ancestor at `/sys/fs/cgroup`; inspect the Pod UID cgroup on its node rather
  than treating that ancestor value as the Pod limit. Test bounded process pressure;
  a node label alone is not enforcement evidence.
- Port exhaustion, scheduling timeout and pull errors remove owned Pods, Services,
  policies and allocations. Deployment-owned namespace policies remain.

For recovery, capture relational state/owned identities first. Delete only named
test or role Pods; never shared namespaces or PVCs:

- Restart Runner; verify stable identity, fenced lease reacquisition and current
  allocation reconciliation. Another Runner must not own the same resource domain.
- Stop Metrics Server; new admission stops after freshness expiry. Cleanup stays
  available. Restore metrics and verify healthy admission recovery.
- Restart Worker during processing; JetStream redelivery produces one current
  GameplayFact result and one scoring effect.
- Restart NATS with its PVC retained; verify streams, consumers, KV and pending work.
- Lose the isolated Redis cache; leaderboards rebuild from relational facts and
  notification delivery resumes.

Record each phase as passed, failed or not-run with environment/time. Reports may
include counts, timings, stable errors and resource identities; exclude protected
Flag/token/SQL payloads. Production 100/500/1,000-Pod runs need CPU, memory, slots
and port budgets plus 20% auxiliary headroom. Provider load tests are not gameplay
or arbitrary challenge throughput tests.

## Storage and availability

Provision one Retain volume per PVC. For static storage use `noctf-retain`,
ReadWriteOnce, `local.path: /var/lib/noctf/<service>` and affinity to the platform
node. Pre-create directories with each image's UID/GID (Loki 10001, Prometheus
65534, Grafana 472). For CSI substitute the actual class/provisioner while keeping
Retain and verified restore semantics. Local volumes require the original node
after failure; persistent single instances are intentional outage points.

## Production cutover

1. Verify target image/schema, policies, storage, Secrets/TLS and real gameplay
   using independent data.
2. Freeze source writes, producers, Worker and Runner. Keep source fenced during
   backup/restore; never run two installations against one database.
3. Take an encrypted recovery point of PostgreSQL, files/metadata and all
   JetStream data/configuration. Preserve signing/encryption keys. Follow the
   existing [backup/recovery procedure](backup-recovery.md). Redis is rebuildable.
4. Restore into the stopped target. Local-files-to-S3 migration must preserve
   immutable object keys/FileIds and verify checksums, size and metadata before
   enabling API.
5. Drain Docker Runtime instances and reconcile their allocations. Docker
   receipts cannot control Kubernetes Pods. Select one active Kubernetes Provider/
   Pool and start fresh Runtime UUIDs after cutover.
6. Account for pending JetStream work and Webhook outbox rows. Verify database/
   message-state consistency; rehearse migrations on a copy, then start the corresponding Host image so its automatic migration completes before enabling writes.
7. Start dependencies, Runner/Worker and API. Verify ownership, health, login/
   refresh, files, logs, callbacks and a fresh Runtime before switching entry DNS.
8. Enable target writes once. Retain the fenced source and encrypted recovery point.

## Rollback

Before target writes, restore the source entry and role processes. After target
writes, freeze the target and choose a verified recovery point; reverting its
database loses subsequent writes unless explicitly reconciled. An image rollback
is not a schema downgrade. Recover database, objects, keys and JetStream together
when data/schema rollback is needed; do not enable both environments for writes.

Monitor readiness, JetStream backlog/redelivery, Webhook outbox age, Runtime
Pending/pull counts, NodePorts, resource pressure and disk free space. Monitoring
is private. Keep existing log redaction/encrypted UserId as the Loki export boundary.
