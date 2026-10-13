"""AWD evidence checker example. Never log task material, tokens or response bodies."""
import datetime as dt
import errno
import json
import os
import socket
import sys
import time
import urllib.error
import urllib.request
import uuid


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


HTTP = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())


def probe(outcome, reason="None"):
    return {"outcome": outcome, "reason": reason}


def request(url, timeout, token=None, body=None, method=None):
    headers = {}
    if token is not None:
        headers["Authorization"] = "Bearer " + token
    if body is not None:
        headers["Content-Type"] = "application/octet-stream"
    with HTTP.open(urllib.request.Request(url, body, headers, method=method), timeout=timeout) as response:
        data = response.read(1024 * 1024 + 1)
        if len(data) > 1024 * 1024:
            raise ValueError("response too large")
        return data


def failure(error, missing_reason):
    if isinstance(error, urllib.error.HTTPError):
        error.close()
        return probe("TargetFailure", missing_reason)
    cause = error.reason if isinstance(error, urllib.error.URLError) else error
    if isinstance(cause, (TimeoutError, socket.timeout)):
        return probe("TargetFailure", "RequestTimeout")
    if isinstance(cause, ConnectionRefusedError) or getattr(cause, "errno", None) == errno.ECONNREFUSED:
        return probe("TargetFailure", "ConnectionRefused")
    return probe("PlatformFailure", "InfrastructureFailure")


def combine(observations):
    for outcome in ("PlatformFailure", "Unknown", "TargetFailure"):
        for item in observations:
            if item["outcome"] == outcome:
                return item
    return probe("Passed")


def evaluate(task, targets, fallback, port=8080):
    deadline = dt.datetime.fromisoformat(task["deadline"].replace("Z", "+00:00"))
    if deadline.tzinfo is None:
        raise ValueError("deadline must have an offset")
    stop = time.monotonic() + (deadline - dt.datetime.now(dt.timezone.utc)).total_seconds()
    flags = task["flags"]
    if not flags or len({flag["flagId"] for flag in flags}) != len(flags):
        raise ValueError("invalid task manifest")
    for flag in flags:
        uuid.UUID(flag["flagId"])
        uuid.UUID(flag["storeId"])
        if not isinstance(flag["flag"], str) or type(flag["requiresWrite"]) is not bool or type(flag["historical"]) is not bool:
            raise ValueError("invalid task material")

    def timeout():
        remaining = stop - time.monotonic()
        if remaining <= 0:
            raise TimeoutError("checker budget exhausted")
        return min(2.0, remaining)

    def host(flag):
        name = flag.get("targetServiceName")
        value = targets.get(name) if name else fallback
        if not value or any(char in value for char in "/?#@"):
            raise ValueError("missing or invalid target")
        return value

    hosts = list(dict.fromkeys(host(flag) for flag in flags))
    connection, functionality = [], []
    for target in hosts:
        if time.monotonic() >= stop:
            connection.append(probe("Unknown", "CheckerTimedOut"))
            functionality.append(probe("Unknown", "CheckerTimedOut"))
            continue
        try:
            with socket.create_connection((target, port), timeout=timeout()):
                pass
            connection.append(probe("Passed"))
        except OSError as error:
            result = failure(error, "ConnectionRefused")
            if result["reason"] == "RequestTimeout":
                result["reason"] = "ConnectTimeout"
            connection.append(result)
            functionality.append(probe("Unknown"))
            continue
        try:
            body = request(f"http://{target}:{port}/health", timeout())
            functionality.append(probe("Passed") if body == b"ok" else probe("TargetFailure", "FunctionalCheckFailed"))
        except (OSError, ValueError) as error:
            functionality.append(failure(error, "FunctionalCheckFailed"))

    results = []
    for flag in flags:
        path = f"/flags/{uuid.UUID(flag['storeId']).hex}/{uuid.UUID(flag['flagId']).hex}"
        url = f"http://{host(flag)}:{port}{path}"
        write = probe("NotRequired")
        if flag["requiresWrite"]:
            if time.monotonic() >= stop:
                write = probe("Unknown", "CheckerTimedOut")
            else:
                try:
                    request(url, timeout(), body=flag["flag"].encode("utf-8"), method="PUT")
                    write = probe("Passed")
                except (OSError, ValueError) as error:
                    write = failure(error, "FlagStoreFailed")
        if time.monotonic() >= stop:
            read = probe("Unknown", "CheckerTimedOut")
        else:
            reason = "HistoricalFlagMissing" if flag["historical"] else "CurrentFlagMissing"
            try:
                # Read every assigned Flag, even after a write or functionality failure.
                actual = request(url, timeout())
                read = probe("Passed") if actual == flag["flag"].encode("utf-8") else probe("TargetFailure", reason)
            except (OSError, ValueError) as error:
                read = failure(error, reason)
        results.append({"flagId": flag["flagId"], "write": write, "read": read,
                        "privateReadContext": None, "publicLocator": path})
    return {"connection": combine(connection), "functionality": combine(functionality), "flags": results,
            "observedAt": dt.datetime.now(dt.timezone.utc).isoformat()}


def main():
    try:
        token = os.environ["NOCTF_CALLBACK_TOKEN"]
        task = json.loads(request(os.environ["NOCTF_AWD_TASK_URL"], 5, token=token))
        result = evaluate(task, json.loads(os.environ.get("NOCTF_AWD_TARGETS", "{}")), os.environ["NOCTF_TARGET_HOST"])
        # One immutable receipt; transport failure never changes evidence to success.
        data = json.dumps(result, separators=(",", ":")).encode("utf-8")
        req = urllib.request.Request(os.environ["NOCTF_CALLBACK_URL"], data,
                                     {"Authorization": "Bearer " + token, "Content-Type": "application/json"})
        with HTTP.open(req, timeout=5) as response:
            response.read(4096)
        return 0
    except Exception:
        print("AWD checker could not complete its task or deliver evidence.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
