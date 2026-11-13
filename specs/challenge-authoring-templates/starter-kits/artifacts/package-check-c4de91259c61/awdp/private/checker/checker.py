import http.client
import json
import os
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


HTTP = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
HOST = os.environ["TARGET_HOST"]
if any(character in HOST for character in "/?#@"):
    raise ValueError("Invalid TARGET_HOST")
BASE = "http://" + ("[" + HOST + "]" if ":" in HOST else HOST) + ":8080"


def get(path):
    try:
        with HTTP.open(BASE + path, timeout=2) as response:
            return response.status, response.read(8193)
    except urllib.error.HTTPError as error:
        with error:
            return error.code, error.read(8193)


def business_passes():
    return all(get(path) == (200, expected) for path, expected in (
        ("/health", b"ok"),
        ("/file?name=welcome.txt", b"NoCTF public document.\n"),
        ("/file?name=docs%2Fguide.txt", b"Nested public document.\n"),
    ))


def evaluate():
    ready = int(os.environ["TARGET_READY_TIMEOUT_SECONDS"])
    if ready <= 0:
        raise ValueError("Invalid ready timeout")
    deadline = time.monotonic() + ready
    while True:
        try:
            if get("/health") == (200, b"ok"):
                break
        except (OSError, http.client.HTTPException):
            pass
        if time.monotonic() >= deadline:
            return "ServiceAbnormal"
        time.sleep(0.1)

    try:
        if not business_passes():
            return "ServiceAbnormal"
        exploited = False
        for name in ("../private/probe.txt", "../../challenge/private/probe.txt"):
            status, body = get("/file?" + urllib.parse.urlencode({"name": name}))
            if status == 200 and body == b"noctf-path-traversal-canary":
                exploited = True
            elif status not in (403, 404):
                return "ServiceAbnormal"
        if not business_passes():
            return "ServiceAbnormal"
        return "ExploitSucceeded" if exploited else "DefenseSucceeded"
    except (OSError, http.client.HTTPException):
        return "ServiceAbnormal"


def main():
    # 可选输入只做普通文件检查，绝不执行持有回调凭据的环境中的选手脚本。
    directory = Path("/noctf/fix")
    if directory.exists():
        entrypoint = directory / "fix.sh"
        if entrypoint.is_symlink() or not entrypoint.is_file() or entrypoint.stat().st_size > 65536:
            raise ValueError("Invalid Checker Fix input")
    outcome = evaluate()
    request = urllib.request.Request(
        os.environ["NOCTF_CALLBACK_URL"],
        json.dumps({"outcome": outcome}).encode("utf-8"),
        {"Authorization": "Bearer " + os.environ["NOCTF_CALLBACK_TOKEN"],
         "Content-Type": "application/json"},
    )
    with HTTP.open(request, timeout=5) as response:
        if response.status not in (200, 202):
            raise ValueError("Unexpected callback status")
        response.read(4096)


if __name__ == "__main__":
    main()
