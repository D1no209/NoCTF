#!/usr/bin/env bash
set -euo pipefail

proxy_root=${NOCTF_PROXY_ROOT:-/opt/noctf-proxy}
docker --context "${DOCKER_CONTEXT:?Set DOCKER_CONTEXT}" run --rm \
  -v "$proxy_root/acme:/var/www/acme" \
  -v "$proxy_root/letsencrypt:/etc/letsencrypt" \
  certbot/certbot:v5.8.0@sha256:f70ad0adbb7e117f0fe42a63c553f28ea451edabc0148757b6efcd9735acaa20 \
  renew --non-interactive --webroot -w /var/www/acme
docker --context "${DOCKER_CONTEXT:?Set DOCKER_CONTEXT}" exec "${NOCTF_PROXY_CONTAINER:?Set NOCTF_PROXY_CONTAINER}" nginx -t
docker --context "${DOCKER_CONTEXT:?Set DOCKER_CONTEXT}" exec "${NOCTF_PROXY_CONTAINER:?Set NOCTF_PROXY_CONTAINER}" nginx -s reload
