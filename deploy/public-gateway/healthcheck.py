"""Component-local liveness; never used as NoCTF's global readiness dependency."""
import json
import os
import socket
import time

try:
    role = os.environ.get("NOCTF_GATEWAY_COMPONENT", "publication")
    if role == "server":
        with socket.create_connection(("127.0.0.1", int(os.environ["FRP_BIND_PORT"])), timeout=1):
            pass
    elif role == "website-client":
        os.kill(1, 0)
    elif role == "publication":
        with open("/run/noctf-gateway/lease.json", "rb") as stream:
            lease = json.loads(stream.read(1025))
        if lease["publicationId"] != os.environ["NOCTF_PUBLICATION_ID"] or lease["expiresAtUnixMs"] <= time.time() * 1000:
            raise ValueError("invalid lease")
    else:
        raise ValueError("unknown component")
except (OSError, ValueError, KeyError):
    raise SystemExit(1)
