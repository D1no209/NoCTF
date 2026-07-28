#!/bin/sh
set -eu

target="http://${TARGET_HOST}:${TARGET_PORT}/status"
remaining="$TARGET_READY_TIMEOUT_SECONDS"
state=''
while [ "$remaining" -gt 0 ]; do
    state="$(curl --fail --silent --show-error --max-time 2 "$target" 2>/dev/null || true)"
    if [ -n "$state" ]; then
        break
    fi
    remaining=$((remaining - 1))
    sleep 1
done

if [ "$state" = 'fixed' ]; then
    outcome='Fixed'
elif [ "$state" = 'vulnerable' ]; then
    outcome='StillVulnerable'
else
    outcome='ServiceUnavailable'
fi

curl --fail --silent --show-error --max-time 5 \
    -H "Authorization: Bearer $NOCTF_CALLBACK_TOKEN" \
    -H 'Content-Type: application/json' \
    --data "{\"outcome\":\"$outcome\"}" \
    "$NOCTF_CALLBACK_URL" >/dev/null
