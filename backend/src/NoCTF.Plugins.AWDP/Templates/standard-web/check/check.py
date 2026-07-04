from pathlib import PurePosixPath
from urllib.parse import quote
from urllib.request import urlopen
import json
import os
import sys
import tarfile
import tempfile
import zipfile


TARGET_HOST = os.getenv("TARGET_HOST", "127.0.0.1")
TARGET_PORT = os.getenv("TARGET_PORT", "80")
TEAM_ID = os.getenv("TEAM_ID", "-")
PATCH_URL = os.getenv("PATCH_URL", "")
PATCH_FILE_NAME = os.getenv("PATCH_FILE_NAME", "fix.tar.gz")
FIX_ENTRY = os.getenv("FIX_ENTRY", "fix.sh")
BASE_URL = f"http://{TARGET_HOST}:{TARGET_PORT}"


def main():
    if PATCH_URL and not inspect_patch_archive():
        sys.exit(2)

    if not service_is_healthy():
        sys.exit(3)

    if not business_behaviour_works():
        sys.exit(3)

    if exploit_still_works():
        sys.exit(1)

    sys.exit(0)


def inspect_patch_archive():
    try:
        with tempfile.NamedTemporaryFile(delete=False) as tmp:
            with urlopen(PATCH_URL, timeout=8) as response:
                tmp.write(response.read())
            archive_path = tmp.name

        names = archive_names(archive_path, PATCH_FILE_NAME)
        normalized = [normalize_name(name) for name in names]
        normalized_set = set(normalized)

        if normalize_name(FIX_ENTRY) not in normalized_set:
            print(f"bad patch: missing {FIX_ENTRY}", file=sys.stderr)
            return False

        for name in normalized:
            if not name or name.startswith("../") or "/../" in name:
                print(f"bad patch: unsafe archive path {name}", file=sys.stderr)
                return False
            if name.startswith("/"):
                print(f"bad patch: absolute archive path {name}", file=sys.stderr)
                return False

        return True
    except Exception as exc:
        print(f"patch inspection failed: {exc}", file=sys.stderr)
        return False


def archive_names(path, file_name):
    lower = file_name.lower()
    if lower.endswith(".zip"):
        with zipfile.ZipFile(path) as archive:
            return archive.namelist()

    if lower.endswith(".tar.gz") or lower.endswith(".tgz"):
        with tarfile.open(path, "r:gz") as archive:
            unsafe_members = [m.name for m in archive.getmembers() if m.issym() or m.islnk()]
            if unsafe_members:
                raise ValueError(f"links are not allowed: {unsafe_members[0]}")
            return archive.getnames()

    raise ValueError(f"unsupported patch archive: {file_name}")


def normalize_name(name):
    return str(PurePosixPath(name.replace("\\", "/")))


def service_is_healthy():
    try:
        body = http_text("/health")
        if body.strip() == "ok":
            print(f"healthy team={TEAM_ID}")
            return True
        print(f"unexpected health body: {body!r}", file=sys.stderr)
    except Exception as exc:
        print(f"health check failed: {exc}", file=sys.stderr)
    return False


def business_behaviour_works():
    try:
        profile = json.loads(http_text("/api/profile?user=guest"))
        if profile.get("user") != "guest" or profile.get("role") != "reader":
            print(f"profile behaviour changed: {profile}", file=sys.stderr)
            return False

        body = http_text("/api/read?file=welcome.txt")
        if "NoCTF AWDP standard template" not in body:
            print("normal file read behaviour changed", file=sys.stderr)
            return False

        return True
    except Exception as exc:
        print(f"business check failed: {exc}", file=sys.stderr)
        return False


def exploit_still_works():
    probes = [
        "/api/read?file=" + quote("/flag/flag.txt", safe=""),
        "/api/read?file=" + quote("../../flag/flag.txt", safe=""),
    ]

    for path in probes:
        try:
            body = http_text(path).lower()
            if "flag{" in body:
                print(f"exploit succeeded through {path}", file=sys.stderr)
                return True
        except Exception:
            continue

    return False


def http_text(path):
    with urlopen(BASE_URL + path, timeout=5) as response:
        if response.status < 200 or response.status >= 300:
            raise RuntimeError(f"{path} returned HTTP {response.status}")
        return response.read().decode("utf-8", errors="replace")


if __name__ == "__main__":
    main()
