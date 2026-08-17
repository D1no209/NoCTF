#!/bin/sh
set -eu

exp_succeeded=false
exp_timed_out=false
if timeout "$TARGET_READY_TIMEOUT_SECONDS" /exploit.sh "$TARGET_HOST"; then
    exp_succeeded=true
else
    exp_status=$?
    [ "$exp_status" -ne 124 ] || exp_timed_out=true
fi

# EXP 的失败或崩溃不能跳过正常服务验证；服务异常拥有最高优先级。
health="$(wget -qO- -T 2 "http://${TARGET_HOST}:8080/health" 2>/dev/null || true)"
if [ "$exp_timed_out" = true ] || [ "$health" != 'ok' ]; then
    outcome='ServiceAbnormal'
elif [ "$exp_succeeded" = true ]; then
    outcome='ExploitSucceeded'
else
    outcome='DefenseSucceeded'
fi

wget -qO /dev/null -T 5 \
    --header "Authorization: Bearer $NOCTF_CALLBACK_TOKEN" \
    --header 'Content-Type: application/json' \
    --post-data "{\"outcome\":\"$outcome\"}" \
    "$NOCTF_CALLBACK_URL"
