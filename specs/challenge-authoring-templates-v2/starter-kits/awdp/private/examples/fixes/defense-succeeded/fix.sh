#!/bin/sh
# 内部正确修补样例，不向选手提供。
set -eu
root=${NOCTF_TARGET_ROOT:-/opt/challenge}
cat /noctf/fix/policy.txt > "$root/policy.txt"
