#!/usr/bin/env python3

import argparse
import gzip
import io
import os
from pathlib import Path
import tarfile


SCRIPT = b"#!/bin/sh\nexit 0\n"


def regular(name: str, content: bytes = SCRIPT) -> tarfile.TarInfo:
    entry = tarfile.TarInfo(name)
    entry.mode = 0o755
    entry.size = len(content)
    return entry


def write_gzip_tar(path: Path, entries: list[tuple[tarfile.TarInfo, bytes | None]]) -> None:
    with tarfile.open(path, "w:gz", format=tarfile.USTAR_FORMAT) as archive:
        for entry, content in entries:
            archive.addfile(entry, io.BytesIO(content) if content is not None else None)


def build_small_invalid_archives(output: Path) -> None:
    write_gzip_tar(output / "empty.tar.gz", [])
    write_gzip_tar(
        output / "missing-fix-sh.tar.gz",
        [(regular("payload/readme.txt", b"missing entrypoint\n"), b"missing entrypoint\n")],
    )
    write_gzip_tar(output / "absolute-path.tar.gz", [(regular("/fix.sh"), SCRIPT)])
    write_gzip_tar(output / "path-traversal.tar.gz", [(regular("../fix.sh"), SCRIPT)])

    symlink = tarfile.TarInfo("payload/symlink")
    symlink.type = tarfile.SYMTYPE
    symlink.linkname = "../fix.sh"
    write_gzip_tar(
        output / "symbolic-link.tar.gz",
        [(regular("fix.sh"), SCRIPT), (symlink, None)],
    )

    hardlink = tarfile.TarInfo("payload/hardlink")
    hardlink.type = tarfile.LNKTYPE
    hardlink.linkname = "fix.sh"
    write_gzip_tar(
        output / "hard-link.tar.gz",
        [(regular("fix.sh"), SCRIPT), (hardlink, None)],
    )

    write_gzip_tar(
        output / "duplicate-path.tar.gz",
        [(regular("fix.sh", b"one\n"), b"one\n"), (regular("fix.sh", b"two\n"), b"two\n")],
    )

    with tarfile.open(output / "not-gzip.tar.gz", "w", format=tarfile.USTAR_FORMAT) as archive:
        archive.addfile(regular("fix.sh"), io.BytesIO(SCRIPT))


def build_oversized_archive(path: Path, maximum_bytes: int) -> None:
    if maximum_bytes <= 0:
        raise ValueError("maximum bytes must be positive")
    with gzip.open(path, "wb", compresslevel=1) as stream:
        while path.stat().st_size <= maximum_bytes:
            stream.write(os.urandom(min(1024 * 1024, maximum_bytes + 1)))
            stream.flush()
    if path.stat().st_size <= maximum_bytes:
        raise RuntimeError("oversized archive did not exceed the configured upload boundary")


def main() -> None:
    parser = argparse.ArgumentParser(description="Build intentionally invalid AWDP Fix archives.")
    parser.add_argument(
        "--output",
        type=Path,
        default=Path(__file__).resolve().parent.parent / "artifacts" / "invalid-fixes",
    )
    parser.add_argument("--maximum-bytes", type=int, default=268_435_456)
    args = parser.parse_args()

    args.output.mkdir(parents=True, exist_ok=True)
    build_small_invalid_archives(args.output)
    build_oversized_archive(args.output / "oversized.tar.gz", args.maximum_bytes)
    for path in sorted(args.output.iterdir()):
        print(path)


if __name__ == "__main__":
    main()
