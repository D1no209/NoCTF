from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse
import os


FLAG = os.getenv("NOCTF_FLAG", "flag{awdp_template_flag}")


class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        parsed = urlparse(self.path)
        if parsed.path == "/health":
            self.send_text("ok\n")
            return

        if parsed.path == "/":
            self.send_text("NoCTF AWDP basic web target\n")
            return

        if parsed.path == "/echo":
            query = parse_qs(parsed.query).get("q", [""])[0]
            # Intentional teaching vulnerability: leaking the flag keyword proves exploitability.
            if "flag" in query.lower():
                self.send_text(f"{FLAG}\n")
                return
            self.send_text(f"{query}\n")
            return

        self.send_response(404)
        self.end_headers()

    def log_message(self, fmt, *args):
        return

    def send_text(self, body):
        payload = body.encode()
        self.send_response(200)
        self.send_header("Content-Type", "text/plain; charset=utf-8")
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)


if __name__ == "__main__":
    ThreadingHTTPServer(("0.0.0.0", 80), Handler).serve_forever()
