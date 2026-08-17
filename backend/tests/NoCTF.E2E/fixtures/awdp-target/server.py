from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import os
from pathlib import Path


fixed_path = Path("/dev/shm/fixed")
service_abnormal_bypass_path = Path("/dev/shm/service-abnormal-bypass")
service_down_path = Path("/dev/shm/service-down")


class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path == "/health":
            if service_down_path.exists():
                self.respond(503, "down")
            elif service_abnormal_bypass_path.exists():
                self.respond(200, "tampered")
            else:
                self.respond(200, "ok")
            return
        if self.path == "/flag":
            flag = os.environ.get("FLAG")
            if flag:
                self.respond(200, flag)
            else:
                self.respond(404, "not found")
            return
        if self.path != "/status":
            self.respond(404, "not found")
            return
        if service_down_path.exists():
            self.respond(503, "unavailable")
            return
        if service_abnormal_bypass_path.exists():
            self.respond(200, "broken")
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
