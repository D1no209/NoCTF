#!/bin/sh
set -eu
input="$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)"
cp "$input/resolver.py" /opt/challenge/resolver.py
