#!/bin/sh
set -eu
root=${NOCTF_TARGET_ROOT:-/opt/challenge}
cat /noctf/fix/policy.txt > "$root/policy.txt"
