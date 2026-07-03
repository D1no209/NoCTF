#!/bin/sh
set -eu

if [ -n "${PATCH_URL:-}" ]; then
  echo "[awdp-template] applying patch from PATCH_URL"
  rm -rf /tmp/noctf-patch
  mkdir -p /tmp/noctf-patch
  archive=/tmp/noctf-patch/fix.archive
  entry="${FIX_ENTRY:-fix.sh}"
  file_name="${PATCH_FILE_NAME:-fix.tar.gz}"
  curl -fsSL "$PATCH_URL" -o "$archive"
  case "$file_name" in
    *.zip|*.ZIP) unzip -q "$archive" -d /tmp/noctf-patch ;;
    *.tar.gz|*.tgz|*.TGZ) tar -xzf "$archive" -C /tmp/noctf-patch ;;
    *) echo "[awdp-template] unsupported patch archive: $file_name" >&2; exit 41 ;;
  esac
  if [ ! -f "/tmp/noctf-patch/$entry" ]; then
    echo "[awdp-template] $entry missing" >&2
    exit 42
  fi
  cd /app
  sh "/tmp/noctf-patch/$entry"
fi

exec python /app/service.py
