#!/bin/sh
set -eu

root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
output="$root/private/artifacts/fixes"
mkdir -p "$output"

for source in "$root"/private/examples/fixes/*; do
  test -d "$source"
  name=$(basename "$source")
  (
    cd "$source"
    find . -type f -printf '%P\0' | LC_ALL=C sort -z |
      tar --format=posix --mtime='UTC 2020-01-01' --owner=0 --group=0 --numeric-owner --mode=0644 \
        --pax-option=exthdr.name=%d/PaxHeaders/%f,delete=atime,delete=ctime \
        --null --no-recursion -T - -czf "$output/$name.tar.gz"
  )
done

printf '内部 Fix 验收包已生成到 %s，不得作为选手附件发布。\n' "$output"
