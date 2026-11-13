import datetime
import json
import os
import subprocess
import urllib.error
import urllib.request


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, response, code, message, headers, new_url):
        return None


http = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())


def functionality(target):
    try:
        with http.open(f"http://{target}:8080/health", timeout=2) as response:
            healthy = response.read(1024) == b"ok"
        return {"kind": "Functionality", "result": "Passed" if healthy else "TargetFailure",
                "reason": "None" if healthy else "FunctionalCheckFailed"}
    except (urllib.error.URLError, TimeoutError):
        return {"kind": "Functionality", "result": "TargetFailure", "reason": "FunctionalCheckFailed"}


def vulnerability(target):
    try:
        result = subprocess.run(["/exploit.sh", target], check=False,
                                stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                                timeout=int(os.environ["TARGET_READY_TIMEOUT_SECONDS"]))
    except subprocess.TimeoutExpired:
        return {"kind": "Vulnerability", "result": "Unknown", "reason": "CheckerTimedOut"}
    if result.returncode == 0:
        return {"kind": "Vulnerability", "result": "TargetFailure", "reason": "VulnerabilityPresent"}
    if result.returncode == 1:
        return {"kind": "Vulnerability", "result": "Passed", "reason": "None"}
    return {"kind": "Vulnerability", "result": "PlatformFailure", "reason": "CheckerCrashed"}


target = os.environ["NOCTF_TARGET_HOST"]
service = functionality(target)
if service["result"] == "Passed":
    exploit = vulnerability(target)
    service = functionality(target)
else:
    exploit = {"kind": "Vulnerability", "result": "NotExecuted", "reason": service["reason"]}
body = json.dumps({"probes": [service, exploit],
                   "observedAt": datetime.datetime.now(datetime.timezone.utc).isoformat()}).encode("utf-8")
request = urllib.request.Request(os.environ["NOCTF_CALLBACK_URL"], data=body,
                                 headers={"Authorization": "Bearer " + os.environ["NOCTF_CALLBACK_TOKEN"],
                                          "Content-Type": "application/json"})
with http.open(request, timeout=5) as response:
    if response.status != 200:
        raise RuntimeError("The Check callback was not accepted.")
