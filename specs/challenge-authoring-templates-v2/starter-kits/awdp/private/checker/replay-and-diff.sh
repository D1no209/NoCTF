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

# 教学扩展点：真实比赛需将以下重放步骤放入额外的隔离边界，
# 例如适当配置的 seccomp、Landlock 或 gVisor，不能暴露 Checker 回调凭证。
NOCTF_TARGET_ROOT="$work" /bin/sh /noctf/fix/fix.sh

diff -ruN "$before" "$work" > "$diff_file" || true
test -f "$diff_file"
NOCTF_TARGET_ROOT="$work" python3 "$work/server.py" --self-test
