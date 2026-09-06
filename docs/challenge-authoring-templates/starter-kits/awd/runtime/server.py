from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


FLAG_PATH = Path("/dev/shm/flag")
FLAG_PATH.write_text("awaiting-first-round", encoding="utf-8")


class Handler(BaseHTTPRequestHandler):
    def do_GET(self) -> None:
        if self.path == "/health":
            self.respond(200, "ok")
            return
        if self.path == "/flag":
            self.respond(200, FLAG_PATH.read_text(encoding="utf-8"))
            return
        self.respond(404, "not found")

    def respond(self, status: int, body: str) -> None:
        encoded = body.encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "text/plain; charset=utf-8")
        self.send_header("Content-Length", str(len(encoded)))
        self.end_headers()
        self.wfile.write(encoded)

    def log_message(self, format: str, *args: object) -> None:
        return


ThreadingHTTPServer(("0.0.0.0", 8080), Handler).serve_forever()
