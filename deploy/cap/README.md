# NoCTF self-hosted Cap

This directory operates Cap Standalone separately from the NoCTF application stack. It publishes no host port. Only Cap joins `1panel-network`; its dedicated Valkey remains on the internal `noctf-cap-internal` network.

## Pinned software

- Cap Standalone `3.1.11`, upstream commit `e02138579482c711ee0bb79c7be3486fe0bf3e84`.
- Widget `0.1.57` and WASM `0.0.7`.
- Valkey `8.1.4-alpine` at the digest recorded in `.env.example`.

Clone the official Cap repository, check out the pinned commit, and run `build-image.sh`. The script refuses another commit. Push the image to the private registry and put its immutable `repository@sha256:...` reference in `/opt/noctf-cap/.env`.

## Install

1. Run `sh deploy/cap/init-layout.sh /opt/noctf-cap` and copy `compose.yml` there.
2. Set `CAP_IMAGE`, verify `VALKEY_IMAGE`, set the exact browser origin, and generate an `ADMIN_KEY` with at least 32 random bytes. Keep `.env` mode `0600`.
3. Run `docker compose --env-file .env -f compose.yml config --quiet`, then `docker compose --env-file .env -f compose.yml up -d`.
4. Insert `noctf-cap.nginx.conf` into the existing NoCTF TLS server block. Back up the live file, run `nginx -t`, and reload only after it succeeds.
5. Open `/cap/` with the administrator key, create a site key, and configure exact CORS plus `difficulty=4`, `challengeCount=80`, instrumentation enabled, obfuscation level `1`, automated-browser blocking disabled, and RSW disabled.
6. In the Cap dashboard, create a dedicated API key for NoCTF workload management. Configure NoCTF with browser server URL `https://noctf.example.com/cap`, API-only `HumanVerification__Cap__BackendServerUrl=http://noctf-cap:3000`, and API-only `HumanVerification__Cap__ManagementApiKey=<dedicated API key>`. The API must share `1panel-network` with Cap. Store the site verification secret through the platform administrator API; never add that secret to this stack or the NoCTF environment file. Never give NoCTF Cap's broader `ADMIN_KEY`.

The platform administrator page reads and updates the active Site Key through Cap's internal management API. It exposes Cap's native `difficulty` range 1–8 and `challengeCount` range 1–500, shows the expected average SHA-256 attempts, and keeps Cap's `saltSize` at its upstream-managed value of 32. Changes affect newly issued challenges; signed challenges already issued retain their original workload.

NoCTF validates CORS, the local WASM asset, and `siteverify` once before enabling Cap or replacing the secret of an enabled Cap configuration. Runtime availability is observed through real verification metrics and the independent Prometheus/Blackbox stack; Cap remains excluded from NoCTF readiness so an outage cannot restart the API, Worker, or Runner.

The observability stack probes Cap's private HTTP login page, scrapes the
dedicated Valkey through a standard read-only exporter, and samples Cap's
native daily statistics through the existing dedicated management API key.
NoCTF records `siteverify` latency separately. No Cap database or Valkey keys
are read by application telemetry, and no site key, secret, token or IP is a
Prometheus label. See `/d/noctf-cap` in Grafana.

## Backup and upgrade

Back up `/opt/noctf-cap/data/cap`, `/opt/noctf-cap/data/valkey`, the mode-`0600` environment file, and the Nginx configuration. Before an upgrade, stop writes, copy both data directories, build the exact reviewed upstream commit, and retain the previous image digest. Do not use `docker compose down -v`; this stack uses bind mounts, and their contents must be preserved.

## Rollback

Disable global human verification through an existing administrator session first. If no session is available, use the documented controlled database transaction that only clears `human_verification_enabled`. Restore the previous Cap image digest or Nginx backup independently. Stopping this Compose project must not stop or recreate any NoCTF application container.
