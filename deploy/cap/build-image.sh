#!/bin/sh
set -eu

expected_commit=e02138579482c711ee0bb79c7be3486fe0bf3e84
source_root=${1:?usage: build-image.sh CAP_SOURCE_ROOT IMAGE_TAG}
image_tag=${2:?usage: build-image.sh CAP_SOURCE_ROOT IMAGE_TAG}

actual_commit=$(git -C "$source_root" rev-parse HEAD)
if [ "$actual_commit" != "$expected_commit" ]; then
  echo "Refusing to build Cap from $actual_commit; expected $expected_commit." >&2
  exit 1
fi

docker build \
  --pull=false \
  --label org.opencontainers.image.revision="$expected_commit" \
  --label org.opencontainers.image.version=3.1.11 \
  --file "$source_root/standalone/Dockerfile" \
  --tag "$image_tag" \
  "$source_root"

docker image inspect "$image_tag" --format '{{index .RepoDigests 0}}'
