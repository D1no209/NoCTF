import json
import os
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


TOKEN = os.environ["TOKEN"]
TASKS = {}
RESULTS = {}
LOCK = threading.Lock()


class Handler(BaseHTTPRequestHandler):
    def authorized(self):
        if self.headers.get("Authorization") == "Bearer " + TOKEN:
            return True
        self.respond(401)
        return False

    def do_GET(self):
        if self.path == "/health":
            self.respond(200)
            return
        if not self.authorized():
            return
        with LOCK:
            if self.path in TASKS:
                self.respond(200, TASKS[self.path])
            elif self.path in RESULTS:
                self.respond(200, RESULTS[self.path])
            else:
                self.respond(404)

    def do_PUT(self):
        if not self.authorized():
            return
        if not self.path.startswith("/task/"):
            self.respond(404)
            return
        body = self.read_json()
        with LOCK:
            TASKS[self.path] = body
        self.respond(204)

    def do_POST(self):
        if not self.authorized():
            return
        if self.path == "/reject":
            self.respond(503)
            return
        if not self.path.startswith("/result/"):
            self.respond(404)
            return
        body = self.read_json()
        with LOCK:
            RESULTS.setdefault(self.path, []).append(body)
        self.respond(200, {"disposition": "Applied"})

    def read_json(self):
        length = int(self.headers["Content-Length"])
        if not 0 < length <= 1024 * 1024:
            raise ValueError("Invalid request length")
        return json.loads(self.rfile.read(length))

    def respond(self, status, value=None):
        body = b"" if value is None else json.dumps(value).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, format, *args):
        pass


if __name__ == "__main__":
    ThreadingHTTPServer(("0.0.0.0", 8090), Handler).serve_forever()
