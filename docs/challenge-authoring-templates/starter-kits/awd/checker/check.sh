#!/bin/sh
set -eu

if wget -qO /dev/null -T 3 \
    "http://${NOCTF_TARGET_HOST}:8080/health"; then
    state='Up'
else
    state='Down'
fi

wget -qO /dev/null -T 5 \
    --header "Authorization: Bearer ${NOCTF_CALLBACK_TOKEN}" \
    --header 'Content-Type: application/json' \
    --post-data "{\"state\":\"${state}\"}" \
    "${NOCTF_CALLBACK_URL}"
