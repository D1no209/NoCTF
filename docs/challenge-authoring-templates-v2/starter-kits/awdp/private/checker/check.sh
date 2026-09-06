#!/bin/sh
# 内部教学 Checker；不要将本脚本作为选手附件发布。
set -eu

: "${TARGET_HOST:?缺少 TARGET_HOST}"
: "${TARGET_READY_TIMEOUT_SECONDS:?缺少 TARGET_READY_TIMEOUT_SECONDS}"
: "${NOCTF_CALLBACK_URL:?缺少 NOCTF_CALLBACK_URL}"
: "${NOCTF_CALLBACK_TOKEN:?缺少 NOCTF_CALLBACK_TOKEN}"

test -d /noctf/fix
test -f /noctf/fix/fix.sh

outcome='DefenseSucceeded'
if find /noctf/fix -type f -maxdepth 8 -print0 \
    | xargs -0 grep -IlE '(^|[^[:alnum:]_])(curl|wget|nc|socat)[[:space:]]|/var/run/docker.sock|/proc/[0-9]+/root' \
    | grep -q .; then
  outcome='ServiceAbnormal'
elif ! /checker/replay-and-diff.sh; then
  outcome='ServiceAbnormal'
else
  target="http://${TARGET_HOST}:8080"
  remaining="${TARGET_READY_TIMEOUT_SECONDS}"
  while [ "$remaining" -gt 0 ]; do
    if wget -qO- -T 2 "${target}/health" 2>/dev/null | grep -qx ok; then
      break
    fi
    remaining=$((remaining - 1))
    sleep 1
  done
  if ! wget -qO- -T 3 "${target}/file?name=public.txt" 2>/dev/null \
      | grep -qx training-public-data; then
    outcome='ServiceAbnormal'
  elif /checker/exploit.sh "$target"; then
    outcome='ExploitSucceeded'
  fi
fi

attempt=0
until wget -qO /dev/null -T 5 \
    --header "Authorization: Bearer ${NOCTF_CALLBACK_TOKEN}" \
    --header 'Content-Type: application/json' \
    --post-data "{\"outcome\":\"${outcome}\"}" \
    "${NOCTF_CALLBACK_URL}"; do
  attempt=$((attempt + 1))
  test "$attempt" -lt 5
  sleep 1
done
