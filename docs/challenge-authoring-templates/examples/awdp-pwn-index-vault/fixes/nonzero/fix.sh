#!/bin/sh
set -eu

printf '%s\n' 'intentional nonzero patch exit for author verification' >&2
exit 17
