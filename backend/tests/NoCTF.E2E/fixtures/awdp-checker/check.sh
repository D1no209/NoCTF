#!/bin/sh
set -eu

target="http://${TARGET_HOST}:8080/status"
remaining="$TARGET_READY_TIMEOUT_SECONDS"
state=''
while [ "$remaining" -gt 0 ]; do
    state="$(wget -qO- -T 2 "$target" 2>/dev/null || true)"
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

wget -qO /dev/null -T 5 \
    --header "Authorization: Bearer $NOCTF_CALLBACK_TOKEN" \
    --header 'Content-Type: application/json' \
    --post-data "{\"outcome\":\"$outcome\"}" \
    "$NOCTF_CALLBACK_URL"
