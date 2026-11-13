import os
import runpy
import secrets
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, urlsplit


ROOT = Path("/opt/challenge")
if "FLAG" in os.environ:
    # 攻击 Runtime 必须完整承载平台注入内容。
    value = os.environ.pop("FLAG").encode("utf-8")
else:
    # 一次性 Fix 目标没有攻击 Flag，生成仅供本次目标使用的探测内容。
    value = secrets.token_urlsafe(32).encode("ascii")
if not 1 <= len(value) <= 4096 or b"\0" in value:
    raise ValueError("FLAG must contain 1 to 4096 UTF-8 bytes without NUL")
(ROOT / "private/flag").write_bytes(value)
(ROOT / "private/probe.txt").write_bytes(b"noctf-path-traversal-canary")
del value


class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        parsed = urlsplit(self.path)
        if parsed.path == "/health":
            self.respond(200, b"ok")
            return
        if parsed.path == "/file":
            name = parse_qs(parsed.query, keep_blank_values=True).get("name", [""])[0]
            # 每次请求读取修补后的解析器，修补无需重启主进程。
            resolver = runpy.run_path(str(ROOT / "resolver.py"))["resolve"]
            selected = resolver(ROOT / "public", name)
            if selected is not None:
                self.respond(200, selected.read_bytes())
                return
            self.respond(403, b"forbidden")
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
