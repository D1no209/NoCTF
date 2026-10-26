#!/usr/bin/env python3
import json
import os
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from threading import Lock

OUTPUT = Path("/results") / os.environ["CASE_NAME"]
CALLBACKS = OUTPUT.with_suffix(".jsonl")
LOCK = Lock()


class Handler(BaseHTTPRequestHandler):
    def do_POST(self) -> None:
        if self.headers.get("Authorization") != "Bearer " + os.environ["EXPECTED_TOKEN"]:
            self.send_response(401)
            self.end_headers()
            return
        length = int(self.headers.get("Content-Length", "0"))
        outcome = json.loads(self.rfile.read(length))["outcome"]
        with LOCK:
            with CALLBACKS.open("a", encoding="utf-8") as output:
                output.write(json.dumps({"outcome": outcome}) + "\n")
            OUTPUT.write_text(outcome, encoding="utf-8")
        self.send_response(200)
        self.end_headers()

    def log_message(self, format: str, *args: object) -> None:
        return


ThreadingHTTPServer(("0.0.0.0", 8081), Handler).serve_forever()
