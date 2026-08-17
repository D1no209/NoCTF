#!/bin/sh
set -eu

root="$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)"
output="${root}/artifacts/fixes"
staging="${root}/artifacts/.staging-fixes-$$"
image="noctf-awdp-index-vault-build:$$"
container="noctf-awdp-index-vault-build-$$"

cleanup() {
    docker rm -f "${container}" >/dev/null 2>&1 || true
    docker image rm "${image}" >/dev/null 2>&1 || true
    rm -rf "${staging}"
}
trap cleanup EXIT INT TERM

make_archive() {
    name="$1"
    src="$2"
    dest="${output}/${name}.tar.gz"
    entries="$(tar -tzf "${src}")"
    case "${entries}" in
        *'..'*|/*)
            printf '%s\n' "unsafe archive entries for ${name}: ${entries}" >&2
            exit 1
            ;;
    esac
    cp "${src}" "${dest}"
    tar -tzf "${dest}" >/dev/null
    printf '%s\n' "${dest}"
}

mkdir -p "${output}" "${staging}"

docker build --target builder -q -t "${image}" "${root}/target" >/dev/null
docker create --name "${container}" "${image}" >/dev/null
docker cp "${container}:/out/pwn-note-defense-succeeded" "${staging}/pwn-note-defense-succeeded"
docker cp "${container}:/out/pwn-note-service-abnormal-bypass" "${staging}/pwn-note-service-abnormal-bypass"

for fixture in defense-succeeded service-abnormal-bypass; do
    mkdir -p "${staging}/${fixture}/payload"
    cp "${root}/fixes/${fixture}/fix.sh" "${staging}/${fixture}/fix.sh"
    chmod 0755 "${staging}/${fixture}/fix.sh"
done
cp "${staging}/pwn-note-defense-succeeded" "${staging}/defense-succeeded/payload/pwn-note"
cp "${staging}/pwn-note-service-abnormal-bypass" "${staging}/service-abnormal-bypass/payload/pwn-note"
chmod 0555 "${staging}/defense-succeeded/payload/pwn-note" "${staging}/service-abnormal-bypass/payload/pwn-note"

for fixture in exploit-succeeded service-abnormal-down nonzero timeout; do
    mkdir -p "${staging}/${fixture}"
    cp "${root}/fixes/${fixture}/fix.sh" "${staging}/${fixture}/fix.sh"
    chmod 0755 "${staging}/${fixture}/fix.sh"
done

for fixture in defense-succeeded service-abnormal-bypass; do
    tar --format=ustar -czf "${staging}/${fixture}.tar.gz" \
        -C "${staging}/${fixture}" fix.sh payload/pwn-note
    make_archive "${fixture}" "${staging}/${fixture}.tar.gz"
done

for fixture in exploit-succeeded service-abnormal-down nonzero timeout; do
    tar --format=ustar -czf "${staging}/${fixture}.tar.gz" \
        -C "${staging}/${fixture}" fix.sh
    make_archive "${fixture}" "${staging}/${fixture}.tar.gz"
done
