import os
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, urlsplit


ROOT = Path("/opt/challenge")
flag = os.environ.pop("FLAG").encode("utf-8")
if not 1 <= len(flag) <= 4096 or b"\0" in flag:
    raise ValueError("FLAG must contain 1 to 4096 UTF-8 bytes without NUL")
(ROOT / "private/flag").write_bytes(flag)
del flag


class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        parsed = urlsplit(self.path)
        if parsed.path == "/health":
            self.respond(200, b"ok")
            return
        if parsed.path == "/file":
            name = parse_qs(parsed.query, keep_blank_values=True).get("name", [""])[0]
            # 教学漏洞：客户端路径可以越过公开目录。
            path = (ROOT / "public" / name).resolve()
            if path.is_file():
                self.respond(200, path.read_bytes())
                return
        self.respond(404, b"not found")

    def respond(self, status, body):
        self.send_response(status)
        self.send_header("Content-Type", "application/octet-stream")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, format, *args):
        pass


if __name__ == "__main__":
    ThreadingHTTPServer(("0.0.0.0", 8080), Handler).serve_forever()
