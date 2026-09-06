#!/bin/sh
set -eu

root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
state=$(mktemp -d "$root/.smoke.XXXXXXXX")
suffix=${state##*.smoke.}
network="noctf-awdp-v2-$suffix"
target_container="$network-target"
checker_container="$network-checker"
callback_container="$network-callback"
target_image="noctf-awdp-v2-target:local"
checker_image="noctf-awdp-v2-checker:local"

cleanup() {
  docker rm -f "$target_container" "$checker_container" "$callback_container" >/dev/null 2>&1 || true
  docker network rm "$network" >/dev/null 2>&1 || true
  rm -rf "$state"
}
trap cleanup EXIT
mkdir -p "$state/results"

docker build -t "$target_image" "$root/private/target"
docker build --build-arg "TARGET_IMAGE=$target_image" -t "$checker_image" "$root/private/checker"
sh "$root/scripts/build-fix-packages.sh"
docker network create "$network" >/dev/null

run_case() {
  case_name=$1
  expected=$2
  package="$root/private/artifacts/fixes/$case_name.tar.gz"
  case_root="$state/$case_name"
  mkdir -p "$case_root/noctf/fix"
  tar -xzf "$package" -C "$case_root/noctf/fix"
  test -f "$case_root/noctf/fix/fix.sh"
  tar -cf "$case_root/canonical.tar" -C "$case_root" noctf

  docker rm -f "$target_container" "$checker_container" "$callback_container" >/dev/null 2>&1 || true
  docker run -d --name "$callback_container" --network "$network" \
    -e "CASE_NAME=$case_name" \
    -v "$state/results:/results" \
    -v "$root/tests/callback.py:/callback.py:ro" \
    python:3.13-alpine python3 /callback.py >/dev/null
  docker run -d --name "$target_container" --network "$network" \
    --cap-drop ALL --security-opt no-new-privileges:true "$target_image" >/dev/null
  docker cp "$case_root/noctf" "$target_container":/
  docker exec "$target_container" /bin/sh /noctf/fix/fix.sh

  docker create --name "$checker_container" --network "$network" \
    --cap-drop ALL --security-opt no-new-privileges:true \
    -e TARGET_HOST="$target_container" \
    -e TARGET_READY_TIMEOUT_SECONDS=10 \
    -e NOCTF_CALLBACK_URL="http://$callback_container:8081/result" \
    -e NOCTF_CALLBACK_TOKEN=smoke-token \
    "$checker_image" >/dev/null
  docker cp "$case_root/noctf" "$checker_container":/
  docker start -a "$checker_container"

  actual=$(cat "$state/results/$case_name")
  test "$actual" = "$expected"
}

run_case defense-succeeded DefenseSucceeded
run_case exploit-succeeded ExploitSucceeded
run_case service-abnormal ServiceAbnormal

printf '%s\n' 'AWDP V2 三种判定的容器验收通过。'
