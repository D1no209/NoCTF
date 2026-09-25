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
    OTEL_SDK_DISABLED=false

Port 9464 is not published to the host. The middleware serves only the exact
/metrics path on that listener, while the public 8080 listener returns 404 for
/metrics.

The base NoCTF Compose enables the NATS monitoring listener on internal port
8222; it is not published to the host.

## Install

1. Copy .env.example to .env and set the PostgreSQL exporter user. Create
   `secrets/grafana-admin-password` with a unique random password and
   `secrets/postgres-exporter-password` with the existing database password.
   Keep the secrets directory root-only and make each mounted file readable
   by its container user. Neither password belongs in Compose or .env.
2. Create persistent directories:

       install -d -o 65534 -g 65534 -m 0750 /opt/noctf-observability/data/prometheus
       install -d -o 472 -g 472 -m 0750 /opt/noctf-observability/data/grafana
       install -d -o root -g root -m 0700 /opt/noctf-observability/secrets

3. Confirm that NOCTF_NETWORK_NAME matches the network created by the NoCTF
   core deployment.
4. Validate and start the independent stack:

       docker compose --env-file deploy/observability/.env \
         -f deploy/observability/compose.yml config --quiet
       docker compose --env-file deploy/observability/.env \
         -f deploy/observability/compose.yml up -d

Prometheus and Grafana bind only to 127.0.0.1 by default. Grafana also joins
the existing `1panel-network` as `noctf-grafana` so an independently configured
TLS reverse proxy can reach it. Require Grafana login; do not publish Prometheus,
exporters or the NoCTF metrics listener.

The production Grafana URL is `https://noctf.grafana.fa1lsnow.com/`. Its TLS
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

For slow requests, first select the route and check sample count, P95/P99 and
5xx rate. Compare the same time window with Npgsql command duration, Redis
latency, EF Core query-cache hit rate and ThreadPool queue length. For Flag
completion, compare intake stages, Wolverine execution/effective time and
JetStream pending. Correlation suggests where to investigate; aggregate
histograms alone do not identify an individual SQL statement or prove a cause.
Widen the window when natural traffic is sparse, and expect Flag panels to be
empty when no submissions completed in that window.

The lower panels separate `ChallengeAttemptStateRead` (the challenge page's
display-only read) from `AdmissionLoad` (an actual Flag intake read), and show
Runtime dispatch stages alongside their sample counts. A stage with no samples
does not mean it was fast: dispatch may have exited early, failed or retried.

## Configuration

- Prometheus keeps seven days and at most 2 GB by default.
- Recording and alert rules live under prometheus/rules.
- The PostgreSQL exporter performs the Runtime waiting aggregation that was
  removed from the application background services.
- Add public NoCTF or Cap URLs to prometheus/targets/blackbox.yml when external
  probing is required. Never put Cap secrets or a siteverify request body in
  monitoring configuration.
- Alert rules create Prometheus alerts. Configure Grafana Alerting or an
  external Alertmanager and its credentials outside this repository.

## Validation

Run Docker Compose config validation, then use the pinned Prometheus image from
.env to execute:

    promtool check config /etc/prometheus/prometheus.yml

After startup, confirm that the noctf, postgres, redis, nats, node,
blackbox-exporter and blackbox targets are up. Grafana should provision the
NoCTF Operations dashboard without requiring application API access.
