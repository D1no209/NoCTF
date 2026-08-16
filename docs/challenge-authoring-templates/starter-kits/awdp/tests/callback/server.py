import json
import os
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


EXPECTED_TOKEN = os.environ["EXPECTED_TOKEN"]
ALLOWED_OUTCOMES = {
    "Fixed",
    "StillVulnerable",
    "RuleViolation",
    "ServiceUnavailable",
}


class Handler(BaseHTTPRequestHandler):
    def do_GET(self) -> None:
        if self.path == "/health":
            self.respond(200)
            return
        self.respond(404)

    def do_POST(self) -> None:
        if self.path != "/result":
            self.respond(404)
            return
        if self.headers.get("Authorization") != f"Bearer {EXPECTED_TOKEN}":
            self.respond(401)
            return
        length = int(self.headers.get("Content-Length", "0"))
        payload = json.loads(self.rfile.read(length))
        if payload.get("outcome") not in ALLOWED_OUTCOMES or len(payload) != 1:
            self.respond(422)
            return
        print(json.dumps(payload, separators=(",", ":")), flush=True)
        self.respond(204)

    def respond(self, status: int) -> None:
        self.send_response(status)
        self.send_header("Content-Length", "0")
        self.end_headers()

    def log_message(self, format: str, *args: object) -> None:
        return


ThreadingHTTPServer(("0.0.0.0", 8090), Handler).serve_forever()
