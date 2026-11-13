import argparse
import hashlib
import subprocess
import sys
import tarfile
import uuid
from pathlib import Path

from package import MODES, ROOT, files, kit_root, package


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--run-docker", action="store_true")
    args = parser.parse_args()
    directory = ROOT / "artifacts" / ("package-check-" + uuid.uuid4().hex[:12])
    directory.mkdir(parents=True)
    for mode in MODES:
        produced = package(mode, directory)
        staff = next(path for path in produced if "-authoring-kit." in path.name)
        public = next(path for path in produced if "-attachments." in path.name)
        expected = {str(name).replace("\\", "/"): path.read_bytes() for path, name in files(kit_root(mode) / "attachment")}
        with tarfile.open(public) as archive:
            actual = {member.name: archive.extractfile(member).read() for member in archive.getmembers()}
        require(actual == expected, "Public attachment whitelist differs: " + mode)
        require(not any(Path(name).parts[0] == "private" for name in actual), "Private asset in attachment")
        with tarfile.open(staff) as archive:
            names = set(archive.getnames())
            required = {mode + "/tools/verify.py", mode + "/tools/package.py", mode + "/tools/gateway.py",
                        mode + "/private/runtime/Dockerfile", mode + "/platform/CONFIGURATION.md",
                        mode + "/statement.md", mode + "/NOCTF-DELIVERY.md"}
            require(required <= names, "Incomplete standalone kit: " + mode)
            require(not any("/artifacts/" in name or "/dist/" in name for name in names), "Generated content leaked into kit")
            archive.extractall(directory, filter="data")
        if mode == "awdp":
            for path in produced:
                if "-fix-" not in path.name:
                    continue
                with tarfile.open(path) as archive:
                    require("fix.sh" in archive.getnames(), "Nested Fix entrypoint")
                    require(archive.getmember("fix.sh").mode == 0o755, "Lost shell executable mode")
        if args.run_docker:
            subprocess.run([sys.executable, "tools/verify.py", "--mode", mode],
                           cwd=directory / mode, check=True, timeout=600)
        print(mode + ": standalone archive, public whitelist, input bytes and executable modes passed", flush=True)
    hashes = [hashlib.sha256(path.read_bytes()).hexdigest() + "  " + path.name
              for path in sorted(directory.glob("*.tar.gz"))]
    (directory / "SHA256SUMS").write_text("\n".join(hashes) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
