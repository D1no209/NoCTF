#!/usr/bin/env python3

import json
import os
import socket
import time
import urllib.request


TARGET_PORT = 31337


def exchange(host: str, command: str) -> str:
    with socket.create_connection((host, TARGET_PORT), timeout=2) as connection:
        connection.sendall((command + "\n").encode("ascii"))
        connection.shutdown(socket.SHUT_WR)
        response = bytearray()
        while len(response) < 512:
            block = connection.recv(512 - len(response))
            if not block:
                break
            response.extend(block)
    return response.decode("utf-8", errors="replace").strip()


def wait_until_ready(host: str, timeout_seconds: int) -> bool:
    deadline = time.monotonic() + timeout_seconds
    while time.monotonic() < deadline:
        try:
            if exchange(host, "PING") == "PONG":
                return True
        except OSError:
            pass
        time.sleep(0.5)
    return False


def evaluate(host: str, ready_timeout: int) -> str:
    if not wait_until_ready(host, ready_timeout):
        return "ServiceUnavailable"

    try:
        legitimate = exchange(host, "READ 0")
        if legitimate != "VALUE:training-service-online":
            return "RuleViolation"

        exploit = exchange(host, "READ 4")
    except OSError:
        return "ServiceUnavailable"

    if exploit == "ERR range":
        return "Fixed"
    if exploit.startswith("VALUE:flag{") and exploit.endswith("}"):
        return "StillVulnerable"
    return "RuleViolation"


def publish(outcome: str) -> None:
    callback_url = os.environ["NOCTF_CALLBACK_URL"]
    callback_token = os.environ["NOCTF_CALLBACK_TOKEN"]
    body = json.dumps({"outcome": outcome}, separators=(",", ":")).encode("utf-8")
    request = urllib.request.Request(
        callback_url,
        data=body,
        method="POST",
        headers={
            "Authorization": f"Bearer {callback_token}",
            "Content-Type": "application/json",
        },
    )
    with urllib.request.urlopen(request, timeout=5) as response:
        if response.status < 200 or response.status >= 300:
            raise RuntimeError("callback rejected checker result")


def main() -> None:
    host = os.environ["TARGET_HOST"]
    timeout = int(os.environ["TARGET_READY_TIMEOUT_SECONDS"])
    publish(evaluate(host, timeout))


if __name__ == "__main__":
    main()
