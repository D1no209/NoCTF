#!/bin/sh
set -eu

root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
docker_cli=${NOCTF_DOCKER_CLI:-docker}
docker() { "$docker_cli" "$@"; }
host_path() {
  case "$docker_cli" in
    *.exe) wslpath -w "$1" ;;
    *) printf '%s\n' "$1" ;;
  esac
}
state=$(mktemp -d "$root/.smoke.XXXXXXXX")
suffix=${state##*.smoke.}
network="noctf-awdp-v2-$suffix"
callback_network="$network-callback"
target_container="$network-target"
checker_container="$network-checker"
callback_container="$network-callback"
target_image="noctf-awdp-v2-target:local"
checker_image="noctf-awdp-v2-checker:local"

cleanup() {
  docker rm -f "$target_container" "$checker_container" "$callback_container" >/dev/null 2>&1 || true
  docker network rm "$network" >/dev/null 2>&1 || true
  docker network rm "$callback_network" >/dev/null 2>&1 || true
  case "$state" in "$root"/.smoke.*) rm -rf "$state" ;; *) exit 1 ;; esac
}
trap cleanup EXIT
mkdir -p "$state/results"

docker build -t "$target_image" "$(host_path "$root/private/target")"
docker build -t "$checker_image" "$(host_path "$root/private/checker")"
sh "$root/scripts/build-fix-packages.sh"
docker network create "$network" >/dev/null
docker network create --internal "$callback_network" >/dev/null

run_case() {
  case_name=$1
  expected=$2
  token=$(python3 -c 'import secrets; print(secrets.token_urlsafe(32))')
  package="$root/private/artifacts/fixes/$case_name.tar.gz"
  if [ "$case_name" = defense-without-checker-input ]; then
    package="$root/private/artifacts/fixes/defense-succeeded.tar.gz"
  fi
  case_root="$state/$case_name"
  mkdir -p "$case_root/noctf/fix"
  if [ "$case_name" = callback-forgery ]; then
    cp "$root/tests/fixtures/callback-forgery/fix.sh" "$case_root/noctf/fix/fix.sh"
  else
    tar -xzf "$package" -C "$case_root/noctf/fix"
  fi
  test -f "$case_root/noctf/fix/fix.sh"
  tar -cf "$case_root/canonical.tar" -C "$case_root" noctf

  docker rm -f "$target_container" "$checker_container" "$callback_container" >/dev/null 2>&1 || true
  docker run -d --name "$callback_container" --network "$callback_network" \
    -e "CASE_NAME=$case_name" \
    -e "EXPECTED_TOKEN=$token" \
    -v "$(host_path "$state/results"):/results" \
    -v "$(host_path "$root/tests/callback.py"):/callback.py:ro" \
    python:3.13-alpine python3 /callback.py >/dev/null
  docker run -d --name "$target_container" --network "$network" \
    --cap-drop ALL --security-opt no-new-privileges:true "$target_image" >/dev/null
  docker cp "$(host_path "$case_root/noctf")" "$target_container":/
  docker exec -e CALLBACK_PROBE_HOST="$callback_container" \
    "$target_container" /bin/sh /noctf/fix/fix.sh > "$case_root/target-output"
  if [ "$case_name" = callback-forgery ]; then
    grep -qx callback-credentials-absent "$case_root/target-output"
    grep -qx callback-network-isolated "$case_root/target-output"
  fi

  docker create --name "$checker_container" --network "$network" \
    --cap-drop ALL --security-opt no-new-privileges:true \
    -e TARGET_HOST="$target_container" \
    -e TARGET_READY_TIMEOUT_SECONDS=10 \
    -e NOCTF_CALLBACK_URL="http://$callback_container:8081/result" \
    -e "NOCTF_CALLBACK_TOKEN=$token" \
    "$checker_image" >/dev/null
  if [ "$case_name" != defense-without-checker-input ]; then
    docker cp "$(host_path "$case_root/noctf")" "$checker_container":/
  fi
  docker network connect "$callback_network" "$checker_container"
  docker start -a "$checker_container"

  actual=$(cat "$state/results/$case_name")
  test "$actual" = "$expected"
  test "$(wc -l < "$state/results/$case_name.jsonl")" -eq 1
}

run_case defense-succeeded DefenseSucceeded
run_case exploit-succeeded ExploitSucceeded
run_case service-abnormal ServiceAbnormal
run_case callback-forgery ExploitSucceeded
run_case defense-without-checker-input DefenseSucceeded

printf '%s\n' 'AWDP 业务判定与回调凭证隔离的容器验收通过。'
