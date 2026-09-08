"""Run one immutable publication; loss of its short-lived lease kills the data plane.

This process is PID 1 in the isolated FRP container. It has no Docker/API credentials,
no listening management socket, and never chooses or changes the FRP upstream.
"""
import json
import os
from pathlib import Path
import signal
import subprocess
import time
import uuid

MAX_LEASE_SECONDS = 10.0
TICK_SECONDS = 0.1


def read_lease(path):
    with path.open("rb") as stream:
        raw = stream.read(1025)
    if len(raw) > 1024:
        raise ValueError("lease_too_large")
    return raw


def lease_deadline(path, publication_id, wall_time, monotonic_time):
    """Convert a bounded UTC lease into a monotonic deadline; reject stale identities."""
    raw = read_lease(path)
    data = json.loads(raw)
    if data.get("publicationId") != publication_id:
        raise ValueError("lease_identity_mismatch")
    expires = data.get("expiresAtUnixMs")
    if isinstance(expires, bool) or not isinstance(expires, int):
        raise ValueError("lease_expiry_invalid")
    remaining = expires / 1000 - wall_time
    if not 0 < remaining <= MAX_LEASE_SECONDS:
        raise ValueError("lease_expired_or_unbounded")
    return monotonic_time + remaining


def reap_child(child):
    """SIGKILL then bounded reap. PID 1 must exit rather than wait indefinitely."""
    if child is None:
        return True
    try:
        if child.poll() is None:
            try:
                child.kill()
            except ProcessLookupError:
                pass  # Exited between poll and kill; still reap it.
        child.wait(timeout=1)
        return True
    except (subprocess.TimeoutExpired, OSError):
        # Do not print subprocess args, credentials, or claim successful cleanup.
        print("gateway child reap failed; exiting lease guard", flush=True)
        return False


def main():
    publication_id = str(uuid.UUID(os.environ["NOCTF_PUBLICATION_ID"]))
    path = Path("/run/noctf-gateway/lease.json")
    child = None
    stopping = False

    def stop(_signal, _frame):
        nonlocal stopping
        stopping = True

    signal.signal(signal.SIGTERM, stop)
    signal.signal(signal.SIGINT, stop)
    try:
        deadline = lease_deadline(path, publication_id, time.time(), time.monotonic())
        last_lease = read_lease(path)
        child = subprocess.Popen(
            ["/usr/local/bin/frpc", "-c", "/run/noctf-gateway/frpc.toml"],
            stdin=subprocess.DEVNULL,
        )
        while not stopping and child.poll() is None:
            now = time.monotonic()
            if now >= deadline:
                raise ValueError("lease_expired")
            current = read_lease(path)
            if current != last_lease:
                deadline = lease_deadline(path, publication_id, time.time(), now)
                last_lease = current
            time.sleep(min(TICK_SECONDS, max(0, deadline - now)))
    except (OSError, ValueError, KeyError):
        # No lease contents, keys or credentials in diagnostics.
        print("gateway lease invalid; revoking publication", flush=True)
    finally:
        reaped = reap_child(child)
    return 0 if reaped else 1


if __name__ == "__main__":
    raise SystemExit(main())
