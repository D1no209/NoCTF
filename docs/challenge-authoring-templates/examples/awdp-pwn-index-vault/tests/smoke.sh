#!/bin/sh
set -eu

root="$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)"
suffix="$$"
network="noctf-awdp-pwn-${suffix}"
target="noctf-awdp-pwn-target-${suffix}"
callback="noctf-awdp-pwn-callback-${suffix}"
target_image="noctf-awdp-pwn-target:${suffix}"
checker_image="noctf-awdp-pwn-checker:${suffix}"
callback_image="noctf-awdp-pwn-callback:${suffix}"
token="smoke-token-${suffix}"
injected_value="TRAINING_DYNAMIC_SECRET_${suffix}"

cleanup() {
    docker rm -f "${target}" "${callback}" >/dev/null 2>&1 || true
    docker network rm "${network}" >/dev/null 2>&1 || true
    docker image rm "${target_image}" "${checker_image}" "${callback_image}" >/dev/null 2>&1 || true
}
trap cleanup EXIT INT TERM

wait_callback() {
    attempt=0
    until docker exec "${callback}" python -c \
        "from urllib.request import urlopen; urlopen('http://127.0.0.1:8080/health', timeout=1)" \
        >/dev/null 2>&1; do
        attempt=$((attempt + 1))
        [ "${attempt}" -lt 30 ] || {
            printf '%s\n' 'callback did not become ready' >&2
            exit 1
        }
        sleep 1
    done
}

last_outcome() {
    docker exec "${callback}" python -c \
        "from urllib.request import urlopen; import json; print(json.load(urlopen('http://127.0.0.1:8080/last', timeout=1))['outcome'])"
}

run_checker_expect() {
    expected="$1"
    docker run --rm --network "${network}" \
        -e TARGET_HOST=target \
        -e TARGET_READY_TIMEOUT_SECONDS=10 \
        -e NOCTF_CALLBACK_URL=http://callback:8080/result \
        -e NOCTF_CALLBACK_TOKEN="${token}" \
        "${checker_image}" >/dev/null
    actual="$(last_outcome)"
    [ "${actual}" = "${expected}" ] || {
        printf '%s\n' "expected ${expected}, got ${actual}" >&2
        exit 1
    }
}

restart_target() {
    docker rm -f "${target}" >/dev/null 2>&1 || true
    docker run -d --name "${target}" --network "${network}" --network-alias target \
        -e FLAG="${injected_value}" \
        "${target_image}" >/dev/null
}

apply_fix_archive() {
    archive="$1"
    docker exec "${target}" /bin/sh -c 'rm -rf /noctf/fix/*'
    docker cp "${archive}" "${target}:/tmp/fix.tar.gz"
    docker exec "${target}" tar -xzf /tmp/fix.tar.gz -C /noctf/fix
    docker exec "${target}" /bin/sh /noctf/fix/fix.sh
}

sh "${root}/scripts/build-fix-packages.sh" >/dev/null
docker build -q -t "${target_image}" "${root}/target" >/dev/null
docker build -q -t "${checker_image}" "${root}/checker" >/dev/null
docker build -q -t "${callback_image}" "${root}/tests/callback" >/dev/null
docker network create "${network}" >/dev/null
docker run -d --name "${callback}" --network "${network}" --network-alias callback \
    -e EXPECTED_TOKEN="${token}" "${callback_image}" >/dev/null
wait_callback

restart_target
run_checker_expect ExploitSucceeded
leak="$(docker exec "${target}" /bin/sh -c "printf 'READ 4\\n' | /opt/challenge/bin/pwn-note")"
[ "${leak}" = "VALUE:${injected_value}" ] || {
    printf '%s\n' 'vulnerable target did not expose the injected prefix-independent value' >&2
    exit 1
}

restart_target
apply_fix_archive "${root}/artifacts/fixes/defense-succeeded.tar.gz"
run_checker_expect DefenseSucceeded

restart_target
apply_fix_archive "${root}/artifacts/fixes/exploit-succeeded.tar.gz"
run_checker_expect ExploitSucceeded

restart_target
apply_fix_archive "${root}/artifacts/fixes/service-abnormal-bypass.tar.gz"
run_checker_expect ServiceAbnormal

restart_target
apply_fix_archive "${root}/artifacts/fixes/service-abnormal-down.tar.gz"
run_checker_expect ServiceAbnormal

restart_target
if apply_fix_archive "${root}/artifacts/fixes/nonzero.tar.gz"; then
    printf '%s\n' 'nonzero fix unexpectedly succeeded' >&2
    exit 1
fi

restart_target
docker exec "${target}" /bin/sh -c 'rm -rf /noctf/fix/*'
docker cp "${root}/artifacts/fixes/timeout.tar.gz" "${target}:/tmp/fix.tar.gz"
docker exec "${target}" tar -xzf /tmp/fix.tar.gz -C /noctf/fix
if docker exec "${target}" timeout 2 /bin/sh /noctf/fix/fix.sh; then
    printf '%s\n' 'timeout fix unexpectedly completed' >&2
    exit 1
fi

printf '%s\n' 'AWDP index-vault smoke test passed.'
