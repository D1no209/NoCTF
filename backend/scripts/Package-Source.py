#!/usr/bin/env python3
"""Archive the tracked files actually used by the build, including translated catalogs."""

import argparse
import gzip
import hashlib
import io
import json
from pathlib import Path
import subprocess
import tarfile


def package(root: Path, output: Path, base_image: str | None = None) -> None:
    root = root.resolve()
    revision = subprocess.check_output(
        ["git", "-C", str(root), "rev-parse", "HEAD"], text=True
    ).strip()
    entries = subprocess.check_output(
        ["git", "-C", str(root), "ls-files", "--stage", "-z"]
    ).split(b"\0")
    files = []
    for entry in entries:
        if not entry:
            continue
        metadata, encoded_name = entry.split(b"\t", 1)
        mode, _, stage = metadata.decode("ascii").split()
        name = encoded_name.decode("utf-8")
        path = root / name
        if stage != "0":
            raise ValueError(f"Resolve the merge before packaging: {name}")
        if mode not in ("100644", "100755") or not path.is_file() or path.is_symlink():
            raise ValueError(f"Source packaging requires a tracked regular file: {name}")
        if not path.resolve().is_relative_to(root):
            raise ValueError(f"Source path leaves the repository: {name}")
        if path.resolve() == output.resolve() or name == ".source-manifest.json":
            raise ValueError(f"Source path conflicts with package output: {name}")
        files.append((name, 0o755 if mode == "100755" else 0o644, path.read_bytes()))
    if "LICENSE" not in {name for name, _, _ in files}:
        raise ValueError("Commit LICENSE before packaging a source distribution.")

    manifest = {
        "revision": revision,
        "license": "AGPL-3.0-only",
        "files": {
            name: hashlib.sha256(data).hexdigest() for name, _, data in sorted(files)
        },
    }
    if base_image is not None:
        manifest["frontendBaseImage"] = base_image
    files.append((".source-manifest.json", 0o644,
                  (json.dumps(manifest, indent=2) + "\n").encode("utf-8")))
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("wb") as stream:
        with gzip.GzipFile(fileobj=stream, mode="wb", filename="", mtime=0) as compressed:
            with tarfile.open(fileobj=compressed, mode="w") as archive:
                for name, mode, data in sorted(files):
                    info = tarfile.TarInfo("noctf-source/" + name)
                    info.size = len(data)
                    info.mode = mode
                    archive.addfile(info, io.BytesIO(data))
    digest = hashlib.sha256(output.read_bytes()).hexdigest()
    output.with_name(output.name + ".sha256").write_text(
        f"{digest}  {output.name}\n", encoding="ascii", newline="\n"
    )
    print(f"Packaged {len(files) - 1} tracked source files at revision {revision}.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--frontend-base-image", help="Immutable image whose frontend a backend-only build retains.")
    arguments = parser.parse_args()
    package(arguments.root, arguments.output, arguments.frontend_base_image)
