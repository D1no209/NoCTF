#!/bin/sh
set -eu

root="$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)"
suffix="$$"
network="noctf-awd-kit-${suffix}"
runtime="noctf-awd-kit-runtime-${suffix}"
callback="noctf-awd-kit-callback-${suffix}"
runtime_image="noctf-awd-kit-runtime:${suffix}"
checker_image="noctf-awd-kit-checker:${suffix}"
callback_image="noctf-awd-kit-callback:${suffix}"

cleanup() {
    docker rm -f "${runtime}" "${callback}" >/dev/null 2>&1 || true
    docker network rm "${network}" >/dev/null 2>&1 || true
    docker image rm "${runtime_image}" "${checker_image}" "${callback_image}" >/dev/null 2>&1 || true
}
trap cleanup EXIT INT TERM

docker build -q -t "${runtime_image}" "${root}/runtime" >/dev/null
docker build -q -t "${checker_image}" "${root}/checker" >/dev/null
docker build -q -t "${callback_image}" "${root}/tests/callback" >/dev/null
docker network create "${network}" >/dev/null
docker run -d --name "${runtime}" --network "${network}" --network-alias target "${runtime_image}" >/dev/null
docker run -d --name "${callback}" --network "${network}" --network-alias callback \
    -e EXPECTED_TOKEN=starter-kit-token "${callback_image}" >/dev/null

docker exec "${runtime}" sh -c "printf '%s' 'flag{starter-kit}' > /dev/shm/flag"

attempt=0
until docker exec "${callback}" python -c \
    "from urllib.request import urlopen; urlopen('http://127.0.0.1:8090/health', timeout=1)" \
    >/dev/null 2>&1; do
    attempt=$((attempt + 1))
    [ "${attempt}" -lt 20 ] || { printf '%s\n' 'callback did not become ready' >&2; exit 1; }
    sleep 1
done

actual_flag="$(docker exec "${runtime}" python -c \
    "from urllib.request import urlopen; print(urlopen('http://127.0.0.1:8080/flag').read().decode())")"
[ "${actual_flag}" = 'flag{starter-kit}' ]

docker run --rm --network "${network}" \
    -e NOCTF_TARGET_HOST=target \
    -e NOCTF_CALLBACK_URL=http://callback:8090/result \
    -e NOCTF_CALLBACK_TOKEN=starter-kit-token \
    "${checker_image}"

docker logs "${callback}" 2>&1 | grep -q '"state":"Up"'
printf '%s\n' 'AWD starter kit smoke test passed.'
