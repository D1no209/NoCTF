import datetime
import json
import os
import sys
import time
import urllib.error
import urllib.request


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, response, code, message, headers, new_url):
        return None


http = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
result = "Indeterminate"
identifier = None
failure = None
try:
    target = os.environ["NOCTF_TARGET_HOST"]
    port = int(os.environ.get("CONTROL_TARGET_PORT", "8080"))
    with http.open(f"http://{target}:{port}/control", timeout=1) as response:
        body = response.read(4097)
    if len(body) > 4096 or b"\0" in body:
        failure = "InvalidEvidence"
    elif body:
        identifier = body.decode("utf-8", errors="strict")
        result = "Controlled"
    else:
        result = "Uncontrolled"
except UnicodeDecodeError:
    failure = "InvalidEvidence"
except TimeoutError:
    failure = "DeadlineExceeded"
except urllib.error.URLError as error:
    failure = "DeadlineExceeded" if isinstance(error.reason, TimeoutError) else "RuntimeUnavailable"

payload = {
    "executionState": "PlatformFailed" if failure else "Completed",
    "result": result,
    "controllerIdentifier": identifier,
    "executionFailure": failure,
    "observedAt": datetime.datetime.now(datetime.timezone.utc).isoformat(),
}
if os.environ.get("CONTROL_TEST_COMPLETION") == "missing-callback":
    sys.exit(0)
if os.environ.get("CONTROL_TEST_COMPLETION") == "wait-for-cancel":
    time.sleep(120)
request = urllib.request.Request(os.environ["NOCTF_CALLBACK_URL"], data=json.dumps(payload).encode("utf-8"),
                                 headers={"Authorization": "Bearer " + os.environ["NOCTF_CALLBACK_TOKEN"],
                                          "Content-Type": "application/json"})
with http.open(request, timeout=2) as response:
    if response.status != 200:
        raise RuntimeError("The Control callback was not accepted.")
if os.environ.get("CONTROL_TEST_COMPLETION") == "nonzero-after-callback":
    sys.exit(9)
