from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


FIXED = Path("/dev/shm/fixed")
SERVICE_ABNORMAL_BYPASS = Path("/dev/shm/service-abnormal-bypass")
SERVICE_DOWN = Path("/dev/shm/service-down")


class Handler(BaseHTTPRequestHandler):
    def do_GET(self) -> None:
        if self.path == "/health":
            if SERVICE_DOWN.exists():
                self.respond(503, "down")
            elif SERVICE_ABNORMAL_BYPASS.exists():
                self.respond(200, "tampered")
            else:
                self.respond(200, "ok")
            return
        if self.path == "/status":
            self.respond(200, "fixed" if FIXED.exists() else "vulnerable")
            return
        self.respond(404, "not found")

    def respond(self, status: int, body: str) -> None:
        encoded = body.encode("ascii")
        self.send_response(status)
        self.send_header("Content-Type", "text/plain")
        self.send_header("Content-Length", str(len(encoded)))
        self.end_headers()
        self.wfile.write(encoded)

    def log_message(self, format: str, *args: object) -> None:
        return


ThreadingHTTPServer(("0.0.0.0", 8080), Handler).serve_forever()
