import json
import os
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


EXPECTED_TOKEN = os.environ["EXPECTED_TOKEN"]
last_evidence = {}


class Handler(BaseHTTPRequestHandler):
    def do_GET(self) -> None:
        if self.path == "/health":
            self.respond(200)
            return
        if self.path == "/last":
            body = json.dumps(last_evidence, separators=(",", ":")).encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
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
        if {probe.get("kind") for probe in payload.get("probes", [])} != {"Functionality", "Vulnerability"} or not payload.get("observedAt"):
            self.respond(422)
            return
        last_evidence.clear()
        last_evidence.update(payload)
        print(json.dumps(payload, separators=(",", ":")), flush=True)
        self.respond(204)

    def respond(self, status: int) -> None:
        self.send_response(status)
        self.send_header("Content-Length", "0")
        self.end_headers()

    def log_message(self, format: str, *args: object) -> None:
        return


ThreadingHTTPServer(("0.0.0.0", 8080), Handler).serve_forever()
