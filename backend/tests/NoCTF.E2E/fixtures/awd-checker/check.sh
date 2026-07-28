#!/bin/sh
set -eu

if curl --fail --silent --show-error --max-time 3 "$NOCTF_TARGET_URL" >/dev/null; then
    state='Up'
else
    state='Down'
fi

curl --fail --silent --show-error --max-time 3 \
    -H "Authorization: Bearer $NOCTF_CALLBACK_TOKEN" \
    -H 'Content-Type: application/json' \
    --data "{\"state\":\"$state\"}" \
    "$NOCTF_CALLBACK_URL" >/dev/null
