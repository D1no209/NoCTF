from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


flag_path = Path("/dev/shm/flag")
down_path = Path("/dev/shm/service-down")
flag_path.write_text("awaiting-round", encoding="ascii")


class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path == "/cgi-bin/flag":
            self.respond(200, flag_path.read_text(encoding="ascii"))
        elif self.path == "/cgi-bin/health":
            is_down = down_path.exists()
            self.respond(503 if is_down else 200, "down" if is_down else "up")
        elif self.path == "/cgi-bin/down":
            down_path.touch()
            self.respond(200, "down")
        elif self.path == "/cgi-bin/up":
            down_path.unlink(missing_ok=True)
            self.respond(200, "up")
        else:
            self.respond(404, "not found")

    def respond(self, status, body):
        encoded = body.encode("ascii")
        self.send_response(status)
        self.send_header("Content-Type", "text/plain")
        self.send_header("Content-Length", str(len(encoded)))
        self.end_headers()
        self.wfile.write(encoded)

    def log_message(self, format, *args):
        return


ThreadingHTTPServer(("0.0.0.0", 8080), Handler).serve_forever()
