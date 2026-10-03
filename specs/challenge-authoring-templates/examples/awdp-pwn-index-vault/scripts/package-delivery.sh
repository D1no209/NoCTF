#!/bin/sh
set -eu

root="$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)"
dist="${root}/dist"
archive="${dist}/noctf-awdp-pwn-index-vault.tar.gz"

sh "${root}/scripts/build-fix-packages.sh" >/dev/null
mkdir -p "${dist}"
rm -f "${archive}"

tar --format=ustar -czf "${archive}" \
    -C "${root}/.." \
    --exclude='awdp-pwn-index-vault/dist' \
    --exclude='awdp-pwn-index-vault/artifacts/.staging-*' \
    --exclude='awdp-pwn-index-vault/**/__pycache__' \
    --exclude='awdp-pwn-index-vault/**/*.pyc' \
    awdp-pwn-index-vault

tar -tzf "${archive}" >/dev/null
printf '%s\n' "${archive}"
