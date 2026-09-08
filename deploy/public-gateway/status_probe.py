"""Read-only FRP status probe, invoked locally inside its private container filesystem."""
import base64
import json
from pathlib import Path
import ssl
import urllib.request

root = Path("/run/noctf-gateway")
credentials = json.loads((root / "admin.json").read_text(encoding="utf-8"))
context = ssl.create_default_context(cafile=str(root / "admin.crt"))
request = urllib.request.Request(f"https://127.0.0.1:{credentials['port']}/api/status")
value = base64.b64encode(f"noctf:{credentials['password']}".encode()).decode()
request.add_header("Authorization", "Basic " + value)
opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), urllib.request.HTTPSHandler(context=context))
try:
    with opener.open(request, timeout=0.6) as response:
        payload = response.read(65537)
    if len(payload) > 65536:
        raise ValueError("status_too_large")
    status = json.loads(payload)
    print(json.dumps([{"name": item["name"], "state": item.get("status", "")} for item in status.get("tcp", [])]))
except (OSError, ValueError, KeyError):
    print("[]")
