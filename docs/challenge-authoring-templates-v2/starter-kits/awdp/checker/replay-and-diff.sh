#!/bin/sh
set -eu

work=/tmp/noctf-fix-work
before=/tmp/noctf-fix-before
diff_file=/tmp/noctf-fix.diff
rm -rf "$work" "$before"
mkdir -p "$work" "$before"
cp -R /opt/noctf-baseline/. "$work/"
cp -R /opt/noctf-baseline/. "$before/"
chmod -R u+w "$work" "$before"

# Extension point: execute the following command inside an additional seccomp,
# Landlock, gVisor, or other challenge-specific sandbox when required.
NOCTF_TARGET_ROOT="$work" /bin/sh /noctf/fix/fix.sh

diff -ruN "$before" "$work" > "$diff_file" || true
test -f "$diff_file"
NOCTF_TARGET_ROOT="$work" python3 "$work/server.py" --self-test
