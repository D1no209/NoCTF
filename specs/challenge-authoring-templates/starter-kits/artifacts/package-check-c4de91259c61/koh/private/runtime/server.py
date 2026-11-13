import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


CONTROL = Path("/opt/challenge/state/control")
LOCK = threading.Lock()


class ControlHandler(BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path == "/health":
            self.respond(200, b"ok")
        elif self.path == "/control":
            with LOCK:
                body = CONTROL.read_bytes() if CONTROL.exists() else b""
            self.respond(200, body)
        else:
            self.respond(404, b"not found")

    def respond(self, status, body):
        self.send_response(status)
        self.send_header("Content-Type", "application/octet-stream")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, format, *args):
        pass


class PlayerHandler(ControlHandler):
    def do_POST(self):
        if self.path != "/claim":
            self.respond(404, b"not found")
            return
        length = int(self.headers.get("Content-Length", "0"))
        if not 1 <= length <= 4096:
            self.respond(400, b"invalid length")
            return
        body = self.rfile.read(length)
        if len(body) != length or b"\0" in body:
            self.respond(400, b"invalid body")
            return
        body.decode("utf-8", errors="strict")
        with LOCK:
            CONTROL.write_bytes(body)
        self.respond(204, b"")


if __name__ == "__main__":
    control = ThreadingHTTPServer(("0.0.0.0", 8081), ControlHandler)
    threading.Thread(target=control.serve_forever, daemon=True).start()
    ThreadingHTTPServer(("0.0.0.0", 8080), PlayerHandler).serve_forever()
