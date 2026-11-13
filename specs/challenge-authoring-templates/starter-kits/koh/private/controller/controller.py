import datetime
import json
import os
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
    with http.open(f"http://{os.environ['NOCTF_TARGET_HOST']}:8081/control", timeout=3) as response:
        body = response.read(16385)
    identifier = body.decode("utf-8", errors="strict") if body else None
    if identifier is not None and (len(identifier) > 4096 or "\0" in identifier):
        identifier = None
        failure = "InvalidEvidence"
    else:
        result = "Controlled" if identifier else "Uncontrolled"
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
request = urllib.request.Request(os.environ["NOCTF_CALLBACK_URL"], data=json.dumps(payload).encode("utf-8"),
                                 headers={"Authorization": "Bearer " + os.environ["NOCTF_CALLBACK_TOKEN"],
                                          "Content-Type": "application/json"})
with http.open(request, timeout=3) as response:
    if response.status != 200:
        raise RuntimeError("The Control callback was not accepted.")
