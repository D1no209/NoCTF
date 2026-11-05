import datetime
import json
import os
import socket
import urllib.error
import urllib.request


with socket.create_connection((os.environ["NOCTF_TARGET_HOST"], 8080), timeout=5) as target:
    target.sendall(b"template-check")
    state = target.recv(1024).decode()
probes = [{"kind": "Functionality", "result": "Passed" if state == "template-check" else "TargetFailure",
           "reason": "None" if state == "template-check" else "FunctionalCheckFailed"}]
body = {"probes": probes, "observedAt": datetime.datetime.now(datetime.timezone.utc).isoformat()}


def submit(payload):
    request = urllib.request.Request(os.environ["NOCTF_CALLBACK_URL"], data=json.dumps(payload).encode(),
                                     headers={"Authorization": "Bearer " + os.environ["NOCTF_CALLBACK_TOKEN"], "Content-Type": "application/json"})
    return urllib.request.urlopen(request, timeout=10)


with submit(body) as response:
    assert json.load(response)["disposition"] == "Applied"
with submit(body) as response:
    assert json.load(response)["disposition"] == "Duplicate"
probes[0].update(result="TargetFailure", reason="FunctionalCheckFailed")
try:
    submit(body)
    raise AssertionError("Conflicting evidence was accepted")
except urllib.error.HTTPError as error:
    assert error.code == 409
