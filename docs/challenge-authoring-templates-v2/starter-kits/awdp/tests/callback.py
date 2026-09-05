#!/usr/bin/env python3
import json
import os
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

OUTPUT = Path("/results") / os.environ["CASE_NAME"]


class Handler(BaseHTTPRequestHandler):
    def do_POST(self) -> None:
        if self.headers.get("Authorization") != "Bearer smoke-token":
            self.send_response(401)
            self.end_headers()
            return
        length = int(self.headers.get("Content-Length", "0"))
        outcome = json.loads(self.rfile.read(length))["outcome"]
        OUTPUT.write_text(outcome, encoding="utf-8")
        self.send_response(200)
        self.end_headers()

    def log_message(self, format: str, *args: object) -> None:
        return


ThreadingHTTPServer(("0.0.0.0", 8081), Handler).serve_forever()
