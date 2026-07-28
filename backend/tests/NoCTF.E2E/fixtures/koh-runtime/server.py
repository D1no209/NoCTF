import http.server
import threading
import time
import urllib.parse


class State:
    lock = threading.Lock()
    mode = "wrong"
    value = "flag{uncontrolled}"


class Handler(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        parsed = urllib.parse.urlparse(self.path)
        if parsed.path == "/set":
            values = urllib.parse.parse_qs(parsed.query, keep_blank_values=True)
            mode = values.get("mode", [""])[0]
            if mode not in {"wrong", "flag", "unavailable", "timeout"}:
                self.send_error(400)
                return
            with State.lock:
                State.mode = mode
                State.value = values.get("value", ["flag{uncontrolled}"])[0]
            self.respond(200, b"ok")
            return
        if parsed.path == "/play":
            self.respond(200, b"shared hill")
            return
        if parsed.path != "/control":
            self.send_error(404)
            return

        with State.lock:
            mode = State.mode
            value = State.value
        if mode == "unavailable":
            self.respond(503, b"unavailable")
            return
        if mode == "timeout":
            time.sleep(5)
            self.respond(200, b"late")
            return
        self.respond(200, value.encode("utf-8"))

    def respond(self, status, body):
        self.send_response(status)
        self.send_header("Content-Type", "text/plain; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        try:
            self.wfile.write(body)
        except BrokenPipeError:
            pass

    def log_message(self, format, *args):
        pass


http.server.ThreadingHTTPServer(("0.0.0.0", 8080), Handler).serve_forever()
