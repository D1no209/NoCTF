#!/usr/bin/env python3

import json
import os
import socket
import subprocess
import sys
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


def run_exploit(host: str, timeout_seconds: int) -> tuple[bool, bool]:
    try:
        result = subprocess.run(
            [sys.executable, "/checker/exploit.py", host, str(TARGET_PORT)],
            stdin=subprocess.DEVNULL,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            check=False,
            timeout=max(1, timeout_seconds),
        )
        return result.returncode == 0, False
    except subprocess.TimeoutExpired:
        return False, True
    except OSError:
        # EXP 子进程自身崩溃不等于 Checker 主进程失败，仍须检查正常服务。
        return False, False


def service_is_healthy(host: str, ready_timeout: int) -> bool:
    if not wait_until_ready(host, ready_timeout):
        return False
    try:
        return exchange(host, "READ 0") == "VALUE:training-service-online"
    except (OSError, TimeoutError):
        return False


def evaluate(host: str, ready_timeout: int) -> str:
    exp_succeeded, exp_timed_out = run_exploit(host, ready_timeout)
    service_healthy = service_is_healthy(host, ready_timeout)
    if exp_timed_out or not service_healthy:
        return "ServiceAbnormal"
    return "ExploitSucceeded" if exp_succeeded else "DefenseSucceeded"


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
