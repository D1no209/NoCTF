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

1. Copy .env.example to .env and replace the Grafana password and PostgreSQL
   exporter DSN. URL-encode reserved password characters in the DSN.
2. Create persistent directories:

       install -d -m 0750 /opt/noctf-observability/data/prometheus
       install -d -m 0750 /opt/noctf-observability/data/grafana

3. Confirm that NOCTF_NETWORK_NAME matches the network created by the NoCTF
   core deployment.
4. Validate and start the independent stack:

       docker compose --env-file deploy/observability/.env \
         -f deploy/observability/compose.yml config --quiet
       docker compose --env-file deploy/observability/.env \
         -f deploy/observability/compose.yml up -d

Prometheus and Grafana bind only to 127.0.0.1 by default. Use an SSH tunnel or
an independently authenticated reverse proxy. Do not publish exporters or the
NoCTF metrics listener.

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
