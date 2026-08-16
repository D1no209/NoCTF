#!/bin/sh
set -eu

install -m 0555 /noctf/fix/payload/pwn-note /opt/challenge/bin/pwn-note.new
mv /opt/challenge/bin/pwn-note.new /opt/challenge/bin/pwn-note
