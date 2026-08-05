#!/usr/bin/env bash
set -euo pipefail

readonly recovery_root=/usr/local/lib/noctf-recovery

case "${1:-}" in
    backup|restore|verify)
        command_name="$1"
        shift
        exec "$recovery_root/$command_name.sh" "$@"
        ;;
    shell)
        shift
        exec /bin/bash "$@"
        ;;
    *)
        echo "Usage: noctf-recovery {backup|restore|verify|shell} [options]" >&2
        exit 64
        ;;
esac
