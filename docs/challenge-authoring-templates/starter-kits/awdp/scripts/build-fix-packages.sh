#!/bin/sh
set -eu

root="$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)"
output="${root}/artifacts"
mkdir -p "${output}"

for fixture in defense-succeeded exploit-succeeded service-abnormal-bypass service-abnormal-down nonzero timeout; do
    archive="${output}/${fixture}.tar.gz"
    tar --format=ustar -czf "${archive}" -C "${root}/examples/fixes/${fixture}" fix.sh
    entries="$(tar -tzf "${archive}")"
    [ "${entries}" = 'fix.sh' ] || {
        printf '%s\n' "unexpected archive entries for ${fixture}: ${entries}" >&2
        exit 1
    }
    printf '%s\n' "${archive}"
done
