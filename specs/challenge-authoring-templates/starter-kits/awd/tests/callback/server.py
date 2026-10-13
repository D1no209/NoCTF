import datetime as dt
import json
import os
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

TOKEN = os.environ["EXPECTED_TOKEN"]
STORE = "dddddddd-0000-0000-0000-000000000001"
FIRST = "aaaaaaaa-0000-0000-0000-000000000001"
SECOND = "aaaaaaaa-0000-0000-0000-000000000002"


def flag(id, historical):
    return {"flagId": id, "storeId": STORE, "storeName": "sample", "targetServiceName": "main",
            "flag": "flag{starter-kit-测试-" + id + "} \n", "historical": historical,
            "requiresWrite": not historical, "privateReadContext": None, "publicLocator": None}


class Handler(BaseHTTPRequestHandler):
    def authorized(self):
        return self.headers.get("Authorization") == "Bearer " + TOKEN

    def do_GET(self):
        if self.path == "/health":
            self.respond(200)
            return
        if not self.authorized():
            self.respond(401)
            return
        if self.path not in ("/task/1", "/task/2", "/task/3"):
            self.respond(404)
            return
        flags = [flag(FIRST, False)] if self.path.endswith("/1") else [flag(SECOND, False), flag(FIRST, True)]
        self.respond(200, {"gameplayFactId": "bbbbbbbb-0000-0000-0000-000000000001",
                           "attemptId": "cccccccc-0000-0000-0000-000000000001",
                           "deadline": (dt.datetime.now(dt.timezone.utc) + dt.timedelta(seconds=20)).isoformat(), "flags": flags})

    def do_POST(self):
        if not self.authorized():
            self.respond(401)
            return
        if self.path not in ("/result/1", "/result/2", "/result/3"):
            self.respond(404)
            return
        try:
            payload = json.loads(self.rfile.read(int(self.headers.get("Content-Length", "0"))))
            phase = self.path[-1]
            flags = payload["flags"]
            assert payload["connection"]["outcome"] == "Passed"
            assert payload["functionality"]["outcome"] == "Passed"
            assert flags[0]["write"]["outcome"] == "Passed"
            assert flags[0]["read"]["outcome"] == "Passed"
            assert len(flags) == (1 if phase == "1" else 2)
            assert flags[0]["flagId"] == (FIRST if phase == "1" else SECOND)
            assert "state" not in payload and "health" not in payload
            if phase != "1":
                assert flags[1]["flagId"] == FIRST
                assert flags[1]["write"]["outcome"] == "NotRequired"
                assert flags[1]["read"]["outcome"] == ("TargetFailure" if phase == "3" else "Passed")
                if phase == "3":
                    assert flags[1]["read"]["reason"] == "HistoricalFlagMissing"
            dt.datetime.fromisoformat(payload["observedAt"])
        except (ValueError, KeyError, AssertionError, TypeError):
            self.respond(422)
            return
        print("validated phase " + phase, flush=True)
        self.respond(200, {"disposition": "Applied"})

    def respond(self, status, body=None):
        data = b"" if body is None else json.dumps(body).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def log_message(self, format, *args):
        pass


ThreadingHTTPServer(("0.0.0.0", 8090), Handler).serve_forever()
