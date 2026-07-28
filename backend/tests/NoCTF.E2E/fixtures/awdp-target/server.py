from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


fixed_path = Path("/dev/shm/fixed")


class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path != "/status":
            self.respond(404, "not found")
            return
        self.respond(200, "fixed" if fixed_path.exists() else "vulnerable")

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
