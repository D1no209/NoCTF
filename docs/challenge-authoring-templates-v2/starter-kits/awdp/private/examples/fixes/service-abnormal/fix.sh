#!/bin/sh
# 内部负例：故意破坏正常服务，不向选手提供。
set -eu
root=${NOCTF_TARGET_ROOT:-/opt/challenge}
cat /noctf/fix/policy.txt > "$root/policy.txt"
