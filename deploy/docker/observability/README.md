# NoCTF external observability

This stack runs independently from the NoCTF product. NoCTF only produces
private metrics, traces, logs and health signals; it never queries Prometheus or
embeds Grafana.

## Prepare NoCTF

Metrics remain opt-in. Set the following values in the existing NoCTF
environment file, then restart NoCTF:

    ASPNETCORE_HTTP_PORTS=8080;9464
    Observability__Enabled=true
    Observability__MetricsPort=9464
    Observability__LokiBaseUrl=http://loki:3100/
    Observability__RequireLoki=true
    OTEL_SDK_DISABLED=false

Port 9464 is not published to the host. The middleware serves only the exact
/metrics path on that listener, while the public 8080 listener returns 404 for
/metrics.

The base NoCTF Compose enables the NATS monitoring listener on internal port
8222; it is not published to the host.

## Install

1. Copy .env.example to .env and set the PostgreSQL exporter user. Set GRAFANA_PUBLIC_HOST/GRAFANA_PUBLIC_URL instead of a hard-coded domain. Create
   `secrets/grafana-admin-password` with a unique random password and
   `secrets/postgres-exporter-password` with the dedicated read-only exporter password.
   Keep the secrets directory root-only and make each mounted file readable
   by its container user. Neither password belongs in Compose or .env.
2. Create persistent directories:

       install -d -o 65534 -g 65534 -m 0750 /opt/noctf-observability/data/prometheus
       install -d -o 472 -g 472 -m 0750 /opt/noctf-observability/data/grafana
       install -d -o 10001 -g 10001 -m 0750 /opt/noctf-observability/data/loki
       install -d -o root -g root -m 0700 /opt/noctf-observability/secrets

3. Apply the Redis host prerequisite and persist it across reboot:

       printf 'vm.overcommit_memory = 1\n' >/etc/sysctl.d/99-noctf-redis.conf
       sysctl --system

   This changes only the Linux memory-allocation policy. It does not modify
   Redis data and does not require a PostgreSQL restart.
4. Confirm that NOCTF_SHARED_ROOT points at the installed shared assets and NOCTF_NETWORK_NAME matches the network created by the NoCTF
   core deployment. Only if Cap is enabled, start its independent stack and add `-f deploy/docker/observability/compose.cap.yml`. The base stack does not require a Cap network. It does not mount Cap data or connect to Valkey from the
   NoCTF application.
5. Validate and start the independent stack:

       docker compose --env-file deploy/docker/observability/.env \
         -f deploy/docker/observability/compose.yml config --quiet
       docker compose --env-file deploy/docker/observability/.env \
         -f deploy/docker/observability/compose.yml up -d

Prometheus and Grafana bind only to 127.0.0.1 by default. Loki has no published
port and is reachable only on the private Compose networks. Grafana also joins
the existing `1panel-network` as `noctf-grafana` so an independently configured
TLS reverse proxy can reach it. Require Grafana login; do not publish Prometheus,
exporters or the NoCTF metrics listener.

The Grafana origin comes from `GRAFANA_PUBLIC_HOST`/`GRAFANA_PUBLIC_URL`. Its TLS
certificate is separate from the NoCTF application certificate. The proxy
serves HTTP-01 challenges from `/opt/noctf-proxy/acme` and mounts the complete
`/opt/noctf-proxy/letsencrypt` directory read-only. The root-only Grafana
password file is `secrets/grafana-admin-password`; do not copy it into the
repository or paste it into logs. `systemd/noctf-grafana-cert-renew.*` runs
`renew-grafana-cert.sh` daily and reloads Nginx after a successful check.

The provisioned `NoCTF · 性能分析` dashboard is the default Grafana home and can
also be opened at `/d/noctf-performance`. For a separate editable copy, choose
Dashboards → New → Import and upload `grafana/noctf-performance-import.json`;
it has no fixed UID and uses the provisioned Prometheus data source UID
`prometheus`. Pick a route and a 5m/15m/1h statistical window at the top.
Read the request sample count before interpreting P95/P99. The dashboard uses
the current `NOCTF_V2_*` JetStream namespace and never displays SQL text,
Flag values, tokens or user IDs. Production exports metrics; without an OTLP
receiver, distributed traces are not retained.

The provisioned `NoCTF · 日志诊断` dashboard is available at `/d/noctf-logs`.
Loki receives redacted OTLP logs from Host over the private Docker network and
retains them for 14 days. The admin log API queries the same Loki data; old
Redis Stream logs are not imported and must be exported to an offline read-only
archive before cutover. Verify that the Loki bind directory is owned by UID
10001 before starting the stack; the official image runs as that user.
UserId values in Loki documents are encrypted with a purpose-separated key
derived from the existing shared `RunnerScoring:SigningKey`; the admin reader
decrypts them for the unchanged UserId filter. All Host roles must use the same
signing key. Rotating that key makes retained log UserIds unreadable, so export
the required audit window before rotation. Database exception payloads and EF
command text are not exported to Loki.

For slow requests, first select the route and check sample count, P95/P99 and
5xx rate. Compare the same time window with Npgsql command duration, NATS
coordination latency, EF Core query-cache hit rate and ThreadPool queue length. For Flag
completion, compare intake stages, Wolverine execution/effective time and
JetStream pending. Correlation suggests where to investigate; aggregate
histograms alone do not identify an individual SQL statement or prove a cause.
The dashboard suppresses the slow-route P95 ranking below 20 requests per
selected window and compares REST, EF-query and Npgsql-command rates to reveal
idle background scans. The Webhook recovery-scan panel separates empty scans
from useful work; this is diagnostic only and does not change retry timing.

`NoCTF · Cap 验证` is provisioned at `/d/noctf-cap`. A Blackbox probe checks
Cap's private HTTP login page without issuing a challenge or submitting a
token; it does not measure the public TLS proxy path.
The pinned Cap Standalone image does not expose `/metrics`; the dashboard does
not pretend that a successful login-page probe proves challenge verification.
The separate standard Valkey exporter reads only server diagnostics on Cap's
private network. NoCTF samples Cap's native `today` statistics through its
existing dedicated management API key once per minute; site key, credentials,
tokens, IPs and response payloads never become metric labels or logs. Cap's
native `avgLatency` is the mean time from challenge issuance to successful
redeem (including browser work); the NoCTF `siteverify` histogram measures
server-to-server HTTP time. Neither is a browser P95 solve-time metric.
Daily Cap counters reset at Cap's UTC day boundary and are exposed as gauges,
not Prometheus counters. Check the telemetry-available flag and sample age
before reading them. A management-API outage affects observability only, not
verification or NoCTF readiness.
Per-container Cap CPU/memory is not scraped: this design does not mount the
Docker socket into an exporter. Use a bounded host-side diagnostic only when
those resources become a demonstrated bottleneck.
FusionCache panels show memory and distributed hit/miss rates by one of four fixed
cache profiles; cache keys are never metric labels. A memory miss followed by a
distributed hit is one read path, not two failed requests.
Widen the window when natural traffic is sparse, and expect Flag panels to be
empty when no submissions completed in that window.

The lower panels separate `ChallengeAttemptStateRead` (the challenge page's
display-only read) from `AdmissionLoad` (an actual Flag intake read), and show
Runtime dispatch stages alongside their sample counts. A stage with no samples
does not mean it was fast: dispatch may have exited early, failed or retried.

## Configuration

- Prometheus keeps seven days and at most 2 GB by default.
- Recording and alert rules live under prometheus/rules.
- The standard PostgreSQL and Redis exporters report infrastructure health only.
  Runtime waiting metrics are computed by the Host through EF Core.
- Add public NoCTF or Cap URLs to prometheus/targets/blackbox.yml when external
  probing is required. Never put Cap secrets or a siteverify request body in
  monitoring configuration.
- Alert rules create Prometheus alerts. Configure Grafana Alerting or an
  external Alertmanager and its credentials outside this repository.
- A one-time Loki `empty ring`, Redis exporter connection failure, Prometheus
  graceful shutdown, Grafana session-token rotation, or canceled data-source
  query during startup/navigation is not an incident if the corresponding
  target becomes healthy and no alert remains active.
- Grafana plugin de-duplication warnings are expected with the pinned Grafana
  13 image and an existing data volume. Do not delete plugin directories
  automatically; verify each duplicate against the pinned image first.

## Validation

Run Docker Compose config validation, then use the pinned Prometheus image from
.env to execute:

    promtool check config /etc/prometheus/prometheus.yml

After startup, confirm that the noctf, postgres, redis, cap-valkey, nats, node,
blackbox-exporter, blackbox and loki targets are up. Grafana should provision the
NoCTF performance and Cap dashboards without requiring application API access.

When an operator needs an interactive PostgreSQL shell, expand environment
variables inside the PostgreSQL container rather than passing Compose-style
defaults as literal user names:

    docker compose exec -T postgres sh -lc \
      'exec psql -U "$POSTGRES_USER" -d "$POSTGRES_DB"'
