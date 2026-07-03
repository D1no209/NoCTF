from urllib.request import urlopen
import os
import sys


host = os.getenv("TARGET_HOST", "127.0.0.1")
port = os.getenv("TARGET_PORT", "80")
url = f"http://{host}:{port}/health"

try:
    with urlopen(url, timeout=5) as response:
        body = response.read().decode(errors="replace").strip()
        if response.status == 200 and body == "ok":
            print(f"healthy team={os.getenv('TEAM_ID', '-')}")
            sys.exit(0)
        print(f"unexpected health response: status={response.status} body={body!r}", file=sys.stderr)
except Exception as exc:
    print(f"checker failed for {url}: {exc}", file=sys.stderr)

sys.exit(1)
