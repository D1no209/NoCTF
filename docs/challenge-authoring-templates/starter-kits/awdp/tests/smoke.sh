#!/bin/sh
set -eu

root="$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)"
suffix="$$"
network="noctf-awdp-kit-${suffix}"
target="noctf-awdp-kit-target-${suffix}"
callback="noctf-awdp-kit-callback-${suffix}"
target_image="noctf-awdp-kit-target:${suffix}"
checker_image="noctf-awdp-kit-checker:${suffix}"
callback_image="noctf-awdp-kit-callback:${suffix}"

cleanup() {
    docker rm -f "${target}" "${callback}" >/dev/null 2>&1 || true
    docker network rm "${network}" >/dev/null 2>&1 || true
    docker image rm "${target_image}" "${checker_image}" "${callback_image}" >/dev/null 2>&1 || true
}
trap cleanup EXIT INT TERM

sh "${root}/scripts/build-fix-packages.sh" >/dev/null
docker build -q -t "${target_image}" "${root}/target" >/dev/null
docker build -q -t "${checker_image}" "${root}/checker" >/dev/null
docker build -q -t "${callback_image}" "${root}/tests/callback" >/dev/null
docker network create "${network}" >/dev/null
docker run -d --name "${target}" --network "${network}" --network-alias target "${target_image}" >/dev/null
docker run -d --name "${callback}" --network "${network}" --network-alias callback \
    -e EXPECTED_TOKEN=starter-kit-token "${callback_image}" >/dev/null

attempt=0
until docker exec "${callback}" python -c \
    "from urllib.request import urlopen; urlopen('http://127.0.0.1:8090/health', timeout=1)" \
    >/dev/null 2>&1; do
    attempt=$((attempt + 1))
    [ "${attempt}" -lt 20 ] || { printf '%s\n' 'callback did not become ready' >&2; exit 1; }
    sleep 1
done

docker run --rm --network "${network}" \
    -e TARGET_HOST=target \
    -e TARGET_READY_TIMEOUT_SECONDS=10 \
    -e NOCTF_CALLBACK_URL=http://callback:8090/result \
    -e NOCTF_CALLBACK_TOKEN=starter-kit-token \
    "${checker_image}"
docker logs "${callback}" 2>&1 | grep -q '"outcome":"ExploitSucceeded"'

docker cp "${root}/examples/fixes/defense-succeeded/fix.sh" "${target}:/tmp/fix.sh"
docker exec "${target}" /bin/sh /tmp/fix.sh

docker run --rm --network "${network}" \
    -e TARGET_HOST=target \
    -e TARGET_READY_TIMEOUT_SECONDS=10 \
    -e NOCTF_CALLBACK_URL=http://callback:8090/result \
    -e NOCTF_CALLBACK_TOKEN=starter-kit-token \
    "${checker_image}"
docker logs "${callback}" 2>&1 | grep -q '"outcome":"DefenseSucceeded"'

printf '%s\n' 'AWDP starter kit smoke test passed.'
