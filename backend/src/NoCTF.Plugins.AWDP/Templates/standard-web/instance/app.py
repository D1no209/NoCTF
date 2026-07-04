from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, urlparse
import json
import os


DATA_DIR = Path("/app/data")


class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        parsed = urlparse(self.path)

        if parsed.path == "/health":
            self.send_text("ok\n")
            return

        if parsed.path == "/":
            self.send_json({
                "service": "NoCTF AWDP standard instance",
                "routes": ["/health", "/api/profile?user=guest", "/api/read?file=welcome.txt"],
            })
            return

        if parsed.path == "/api/profile":
            user = parse_qs(parsed.query).get("user", ["guest"])[0]
            self.send_json({"user": user, "role": "reader"})
            return

        if parsed.path == "/api/read":
            requested = parse_qs(parsed.query).get("file", ["welcome.txt"])[0]
            try:
                # Intentional vulnerability: absolute paths and ../ traversal escape DATA_DIR.
                path = Path(os.path.join(DATA_DIR, requested))
                body = path.read_text(encoding="utf-8")
                self.send_text(body)
            except OSError as exc:
                self.send_json({"error": str(exc)}, status=404)
            return

        self.send_json({"error": "not found"}, status=404)

    def log_message(self, fmt, *args):
        return

    def send_text(self, body, status=200):
        payload = body.encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "text/plain; charset=utf-8")
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)

    def send_json(self, value, status=200):
        payload = json.dumps(value, separators=(",", ":")).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)


if __name__ == "__main__":
    ThreadingHTTPServer(("0.0.0.0", 80), Handler).serve_forever()
