#!/usr/bin/env python3
# 教学靶机源码，仅供出题人与内部验收使用。
import os
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, urlparse

ROOT = Path(os.environ.get("NOCTF_TARGET_ROOT", "/opt/challenge")).resolve()


def policy() -> str:
    return (ROOT / "policy.txt").read_text(encoding="utf-8").strip()


def resolve_file(name: str) -> Path | None:
    if policy() == "down":
        return None
    if policy() == "safe":
        return ROOT / "public.txt" if name == "public.txt" else None
    return (ROOT / name).resolve()


def self_test() -> int:
    if policy() == "down":
        return 1
    public = resolve_file("public.txt")
    return 0 if public and public.read_text(encoding="utf-8").strip() == "training-public-data" else 1


class Handler(BaseHTTPRequestHandler):
    def do_GET(self) -> None:
        parsed = urlparse(self.path)
        if parsed.path == "/health":
            healthy = self_test() == 0
            self.send_response(200 if healthy else 503)
            self.end_headers()
            self.wfile.write(b"ok" if healthy else b"down")
            return
        if parsed.path == "/file":
            name = parse_qs(parsed.query).get("name", [""])[0]
            selected = resolve_file(name)
            if selected is None or not selected.is_file():
                self.send_response(404)
                self.end_headers()
                return
            self.send_response(200)
            self.end_headers()
            self.wfile.write(selected.read_bytes())
            return
        self.send_response(404)
        self.end_headers()

    def log_message(self, format: str, *args: object) -> None:
        return


if __name__ == "__main__":
    if "--self-test" in sys.argv:
        raise SystemExit(self_test())
    ThreadingHTTPServer(("0.0.0.0", 8080), Handler).serve_forever()
