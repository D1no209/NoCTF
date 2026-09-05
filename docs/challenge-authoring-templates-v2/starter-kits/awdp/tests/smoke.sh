#!/bin/sh
set -eu

root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
state="$root/.smoke"
network="noctf-awdp-v2-smoke"
target_image="noctf-awdp-v2-target:local"
checker_image="noctf-awdp-v2-checker:local"

cleanup() {
  docker rm -f noctf-v2-target noctf-v2-checker noctf-v2-callback >/dev/null 2>&1 || true
  docker network rm "$network" >/dev/null 2>&1 || true
  rm -rf "$state"
  rm -rf "$root/artifacts"
}
trap cleanup EXIT
cleanup
mkdir -p "$state/results"

docker build -t "$target_image" "$root/target"
docker build --build-arg "TARGET_IMAGE=$target_image" -t "$checker_image" "$root/checker"
sh "$root/scripts/build-fix-packages.sh"
docker network create "$network" >/dev/null

run_case() {
  case_name=$1
  expected=$2
  package="$root/artifacts/fixes/$case_name.tar.gz"
  case_root="$state/$case_name"
  mkdir -p "$case_root/noctf/fix"
  tar -xzf "$package" -C "$case_root/noctf/fix"
  test -f "$case_root/noctf/fix/fix.sh"
  tar -cf "$case_root/canonical.tar" -C "$case_root" noctf

  docker rm -f noctf-v2-target noctf-v2-checker noctf-v2-callback >/dev/null 2>&1 || true
  docker run -d --name noctf-v2-callback --network "$network" \
    -e "CASE_NAME=$case_name" \
    -v "$state/results:/results" \
    -v "$root/tests/callback.py:/callback.py:ro" \
    python:3.13-alpine python3 /callback.py >/dev/null
  docker run -d --name noctf-v2-target --network "$network" "$target_image" >/dev/null
  docker cp "$case_root/noctf" noctf-v2-target:/
  docker exec noctf-v2-target /bin/sh /noctf/fix/fix.sh

  docker create --name noctf-v2-checker --network "$network" \
    -e TARGET_HOST=noctf-v2-target \
    -e TARGET_READY_TIMEOUT_SECONDS=10 \
    -e NOCTF_CALLBACK_URL=http://noctf-v2-callback:8081/result \
    -e NOCTF_CALLBACK_TOKEN=smoke-token \
    "$checker_image" >/dev/null
  docker cp "$case_root/noctf" noctf-v2-checker:/
  docker start -a noctf-v2-checker

  actual=$(cat "$state/results/$case_name")
  test "$actual" = "$expected"
}

run_case defense-succeeded DefenseSucceeded
run_case exploit-succeeded ExploitSucceeded
run_case service-abnormal ServiceAbnormal

echo "AWDP V2 smoke test passed."
