import datetime
import json
import os
import subprocess
import urllib.error
import urllib.request


host = os.environ["TARGET_HOST"]
try:
    exploit = subprocess.run(["/exploit.sh", host], check=False,
                             stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                             timeout=int(os.environ["TARGET_READY_TIMEOUT_SECONDS"]))
    vulnerability = {"kind": "Vulnerability", "result": "TargetFailure" if exploit.returncode == 0 else "Passed",
                     "reason": "VulnerabilityPresent" if exploit.returncode == 0 else "None"}
except subprocess.TimeoutExpired:
    vulnerability = {"kind": "Vulnerability", "result": "Unknown", "reason": "CheckerTimedOut"}
try:
    with urllib.request.urlopen(f"http://{host}:8080/health", timeout=2) as response:
        healthy = response.read(3) == b"ok"
except (OSError, urllib.error.URLError):
    healthy = False
functionality = {"kind": "Functionality", "result": "Passed" if healthy else "TargetFailure",
                 "reason": "None" if healthy else "FunctionalCheckFailed"}
body = json.dumps({"probes": [functionality, vulnerability], "flagContexts": [],
                   "observedAt": datetime.datetime.now(datetime.timezone.utc).isoformat()}, separators=(",", ":")).encode()
request = urllib.request.Request(os.environ["NOCTF_CALLBACK_URL"], data=body,
                                 headers={"Authorization": "Bearer " + os.environ["NOCTF_CALLBACK_TOKEN"], "Content-Type": "application/json"})
with urllib.request.urlopen(request, timeout=5) as response:
    assert 200 <= response.status < 300
