from pathlib import PurePosixPath
from urllib.parse import quote
from urllib.request import urlopen
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


def main():
    if PATCH_URL:
        if not inspect_patch_archive():
            sys.exit(2)

    if not service_is_healthy():
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
        normalized = {normalize_name(name) for name in names}
        if normalize_name(FIX_ENTRY) not in normalized:
            print(f"bad patch: missing {FIX_ENTRY}", file=sys.stderr)
            return False

        for name in normalized:
            if not name or name.startswith("../") or "/../" in name:
                print(f"bad patch: unsafe path {name}", file=sys.stderr)
                return False
            if name in {"etc/passwd", "root/.ssh/authorized_keys"}:
                print(f"bad patch: suspicious hardening path {name}", file=sys.stderr)
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
            return archive.getnames()

    raise ValueError(f"unsupported patch archive: {file_name}")


def normalize_name(name):
    return str(PurePosixPath(name.replace("\\", "/")))


def service_is_healthy():
    url = f"http://{TARGET_HOST}:{TARGET_PORT}/health"
    try:
        with urlopen(url, timeout=5) as response:
            body = response.read().decode(errors="replace").strip()
            if response.status == 200 and body == "ok":
                print(f"healthy team={TEAM_ID}")
                return True
            print(f"unexpected health response: status={response.status} body={body!r}", file=sys.stderr)
    except Exception as exc:
        print(f"health check failed for {url}: {exc}", file=sys.stderr)
    return False


def exploit_still_works():
    payload = quote("please leak flag")
    url = f"http://{TARGET_HOST}:{TARGET_PORT}/echo?q={payload}"
    try:
        with urlopen(url, timeout=5) as response:
            body = response.read().decode(errors="replace").lower()
            if "flag{" in body:
                print("exploit succeeded: flag leaked", file=sys.stderr)
                return True
            return False
    except Exception as exc:
        print(f"exploit interaction failed for {url}: {exc}", file=sys.stderr)
        sys.exit(3)


if __name__ == "__main__":
    main()
