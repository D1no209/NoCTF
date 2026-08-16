#!/bin/sh
set -eu

remaining="${TARGET_READY_TIMEOUT_SECONDS}"
health=''
while [ "${remaining}" -gt 0 ]; do
    health="$(wget -qO- -T 2 "http://${TARGET_HOST}:8080/health" 2>/dev/null || true)"
    [ -n "${health}" ] && break
    remaining=$((remaining - 1))
    sleep 1
done

if [ -z "${health}" ]; then
    outcome='ServiceUnavailable'
elif [ "${health}" != 'ok' ]; then
    outcome='RuleViolation'
else
    state="$(wget -qO- -T 2 "http://${TARGET_HOST}:8080/status" 2>/dev/null || true)"
    case "${state}" in
        fixed) outcome='Fixed' ;;
        vulnerable) outcome='StillVulnerable' ;;
        '') outcome='ServiceUnavailable' ;;
        *) outcome='RuleViolation' ;;
    esac
fi

wget -qO /dev/null -T 5 \
    --header "Authorization: Bearer ${NOCTF_CALLBACK_TOKEN}" \
    --header 'Content-Type: application/json' \
    --post-data "{\"outcome\":\"${outcome}\"}" \
    "${NOCTF_CALLBACK_URL}"
