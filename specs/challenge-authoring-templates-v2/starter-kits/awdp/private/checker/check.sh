#!/bin/sh
# 内部教学 Checker；不要将本脚本作为选手附件发布。
set -eu

: "${TARGET_HOST:?缺少 TARGET_HOST}"
: "${TARGET_READY_TIMEOUT_SECONDS:?缺少 TARGET_READY_TIMEOUT_SECONDS}"
: "${NOCTF_CALLBACK_URL:?缺少 NOCTF_CALLBACK_URL}"
: "${NOCTF_CALLBACK_TOKEN:?缺少 NOCTF_CALLBACK_TOKEN}"
: "${NOCTF_CHECK_REQUIREMENTS:?缺少 NOCTF_CHECK_REQUIREMENTS}"

# Fix 已由 Runner 在独立靶机中执行；Checker 只读取可选输入并验证靶机。
if [ -d /noctf/fix ]; then
  python3 /checker/inspect-file.py --source input --path fix.sh >/dev/null
fi

connection='Passed'
functionality='Passed'
vulnerability='Passed'
target="http://${TARGET_HOST}:8080"
remaining="${TARGET_READY_TIMEOUT_SECONDS}"
while [ "$remaining" -gt 0 ]; do
  if wget -qO- -T 2 "${target}/health" 2>/dev/null | grep -qx ok; then
    break
  fi
  remaining=$((remaining - 1))
  sleep 1
done
if [ "$remaining" -eq 0 ]; then
  connection='TargetFailure'
fi
if ! wget -qO- -T 3 "${target}/file?name=public.txt" 2>/dev/null \
    | grep -qx training-public-data; then
  functionality='TargetFailure'
  vulnerability='TargetFailure'
elif /checker/exploit.sh "$target"; then
  vulnerability='TargetFailure'
fi

report=$(python3 /checker/report.py "$connection" "$functionality" "$vulnerability")
attempt=0
until wget -qO /dev/null -T 5 \
    --header "Authorization: Bearer ${NOCTF_CALLBACK_TOKEN}" \
    --header 'Content-Type: application/json' \
    --post-data "$report" \
    "${NOCTF_CALLBACK_URL}"; do
  attempt=$((attempt + 1))
  test "$attempt" -lt 5
  sleep 1
done
