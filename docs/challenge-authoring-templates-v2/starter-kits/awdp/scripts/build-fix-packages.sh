#!/bin/sh
set -eu

root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
output="$root/artifacts/fixes"
mkdir -p "$output"

for source in "$root"/examples/fixes/*; do
  test -d "$source"
  name=$(basename "$source")
  (
    cd "$source"
    find . -type f -printf '%P\0' | LC_ALL=C sort -z |
      tar --format=posix --mtime='UTC 2020-01-01' --owner=0 --group=0 --numeric-owner \
        --null --no-recursion -T - -czf "$output/$name.tar.gz"
  )
done

echo "Fix packages written to $output"
