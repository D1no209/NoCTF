#!/bin/sh
set -eu

root="$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)"
output="${1:-${root}/dist}"
mkdir -p "${output}"

for kit in awd awdp; do
    archive="${output}/noctf-${kit}-authoring-kit.tar.gz"
    tar --exclude="${kit}/artifacts" -czf "${archive}" -C "${root}" "${kit}"
    printf '%s\n' "${archive}"
done
