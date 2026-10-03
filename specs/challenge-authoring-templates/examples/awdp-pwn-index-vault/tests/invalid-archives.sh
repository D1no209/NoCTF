#!/bin/sh
set -eu

root="$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)"
output="${root}/artifacts/invalid-fixes-test"
maximum=1048576

cleanup() {
    rm -rf "${output}"
}
trap cleanup EXIT INT TERM

python3 "${root}/scripts/build-invalid-fix-packages.py" \
    --output "${output}" \
    --maximum-bytes "${maximum}" >/dev/null

for name in \
    empty \
    missing-fix-sh \
    absolute-path \
    path-traversal \
    symbolic-link \
    hard-link \
    duplicate-path \
    not-gzip \
    oversized; do
    [ -f "${output}/${name}.tar.gz" ] || {
        printf '%s\n' "missing generated invalid archive: ${name}" >&2
        exit 1
    }
done

[ "$(wc -c < "${output}/oversized.tar.gz")" -gt "${maximum}" ] || {
    printf '%s\n' 'oversized archive did not cross the requested boundary' >&2
    exit 1
}

if gzip -t "${output}/not-gzip.tar.gz" >/dev/null 2>&1; then
    printf '%s\n' 'non-gzip fixture unexpectedly passed gzip validation' >&2
    exit 1
fi

printf '%s\n' 'AWDP invalid archive fixtures generated successfully.'
