"""Deliberately vulnerable storage example; replace its business logic for a real challenge."""
import os
import uuid
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

ROOT = Path(os.environ.get("FLAG_ROOT", "/dev/shm/flags"))
ROOT.mkdir(parents=True, exist_ok=True)


class Handler(BaseHTTPRequestHandler):
    def flag_path(self):
        parts = self.path.split("/")
        if len(parts) != 4 or parts[1] != "flags":
            return None
        try:
            return ROOT / uuid.UUID(parts[2]).hex / uuid.UUID(parts[3]).hex
        except ValueError:
            return None

    def do_PUT(self):
        path = self.flag_path()
        if path is None:
            self.respond(404, b"not found")
            return
        try:
            length = int(self.headers.get("Content-Length", "0"))
        except ValueError:
            length = 0
        if not 0 < length <= 8192:
            self.respond(400, b"invalid length")
            return
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(self.rfile.read(length))
        self.respond(204, b"")

    def do_GET(self):
        down = ROOT / "service-down"
        if self.path == "/cgi-bin/down":
            down.touch()
            self.respond(200, b"down")
            return
        if self.path == "/cgi-bin/up":
            down.unlink(missing_ok=True)
            self.respond(200, b"up")
            return
        if self.path == "/cgi-bin/flag":
            flags = sorted((path for path in ROOT.glob("*/*") if path.is_file()), key=lambda path: path.stat().st_mtime_ns)
            self.respond(200, flags[-1].read_bytes() if flags else b"awaiting-round")
            return
        if self.path in ("/health", "/cgi-bin/health"):
            if down.exists():
                self.respond(503, b"down")
                return
            self.respond(200, b"ok" if self.path == "/health" else b"up")
            return
        path = self.flag_path()
        if path is not None and path.is_file():
            self.respond(200, path.read_bytes())
            return
        self.respond(404, b"not found")

    def respond(self, status, body):
        self.send_response(status)
        self.send_header("Content-Type", "application/octet-stream")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, format, *args):
        pass


if __name__ == "__main__":
    ThreadingHTTPServer(("0.0.0.0", 8080), Handler).serve_forever()
