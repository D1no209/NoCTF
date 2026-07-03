#!/bin/sh
set -eu

if [ -n "${PATCH_URL:-}" ]; then
  echo "[awdp-template] applying patch from PATCH_URL"
  rm -rf /tmp/noctf-patch
  mkdir -p /tmp/noctf-patch
  curl -fsSL "$PATCH_URL" -o /tmp/noctf-patch/patch.tar.gz
  tar -xzf /tmp/noctf-patch/patch.tar.gz -C /tmp/noctf-patch
  if [ ! -f /tmp/noctf-patch/patch.sh ]; then
    echo "[awdp-template] patch.sh missing" >&2
    exit 42
  fi
  cp /tmp/noctf-patch/patch.sh /app/patch.sh
  cd /app
  sh /app/patch.sh
fi

exec python /app/service.py
