#!/bin/sh
set -eu

mkdir -p /flag /app/data
printf '%s\n' "${NOCTF_FLAG:-flag{awdp_standard_template}}" > /flag/flag.txt

if [ -n "${PATCH_URL:-}" ]; then
  echo "[noctf-awdp] applying submitted patch"
  rm -rf /tmp/noctf-patch
  mkdir -p /tmp/noctf-patch
  archive=/tmp/noctf-patch/archive
  entry="${FIX_ENTRY:-fix.sh}"
  file_name="${PATCH_FILE_NAME:-fix.tar.gz}"

  curl -fsSL "$PATCH_URL" -o "$archive"
  case "$file_name" in
    *.zip|*.ZIP) unzip -q "$archive" -d /tmp/noctf-patch ;;
    *.tar.gz|*.tgz|*.TGZ) tar -xzf "$archive" -C /tmp/noctf-patch ;;
    *) echo "[noctf-awdp] unsupported patch archive: $file_name" >&2; exit 41 ;;
  esac

  if [ ! -f "/tmp/noctf-patch/$entry" ]; then
    echo "[noctf-awdp] missing patch entry: $entry" >&2
    exit 42
  fi

  cd /app
  sh "/tmp/noctf-patch/$entry"
fi

exec python /app/app.py
