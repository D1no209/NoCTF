import datetime
import hashlib
import json
import os
import socket
import urllib.request


with open("/noctf/fix/fix.sh", "rb") as archive_input:
    assert hashlib.sha256(archive_input.read()).hexdigest() == os.environ["PATCH_SCRIPT_SHA256"]
assert not os.path.exists("/repair-state")
with socket.create_connection((os.environ["NOCTF_TARGET_HOST"], 8080), timeout=5) as target:
    state = target.recv(1024).decode()
probes = [
    {"kind": "Functionality", "result": "Passed" if state else "TargetFailure", "reason": "None" if state else "FunctionalCheckFailed"},
    {"kind": "Vulnerability", "result": "Passed" if state == "repaired" else "TargetFailure", "reason": "None" if state == "repaired" else "VulnerabilityPresent"},
]
body = json.dumps({"probes": probes, "observedAt": datetime.datetime.now(datetime.timezone.utc).isoformat()}).encode()
request = urllib.request.Request(os.environ["NOCTF_CALLBACK_URL"], data=body,
                                 headers={"Authorization": "Bearer " + os.environ["NOCTF_CALLBACK_TOKEN"], "Content-Type": "application/json"})
with urllib.request.urlopen(request, timeout=10) as response:
    assert response.status == 200
