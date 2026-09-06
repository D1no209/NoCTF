#!/usr/bin/env python3
"""只读取 Fix 输入或 Checker 重放副本的元数据，不执行内容、不输出原文。"""
import argparse
import hashlib
import json
import stat
import sys
from pathlib import Path, PurePosixPath

ROOTS = {"input": Path("/noctf/fix"), "replay": Path("/tmp/noctf-fix-work")}
MAX_BYTES = 16 * 1024 * 1024


def inspect_file(root: Path, relative: str, max_bytes: int = MAX_BYTES) -> dict:
    parts = relative.split("/")
    if not relative or "\\" in relative or PurePosixPath(relative).is_absolute() or any(
        part in ("", ".", "..") for part in parts
    ):
        raise ValueError("文件路径必须是无越界片段的相对路径")
    if max_bytes <= 0:
        raise ValueError("文件大小上限必须为正数")
    root = root.resolve(strict=True)
    selected = root
    for part in parts:
        selected = selected / part
        if selected.is_symlink():
            raise ValueError("不接受符号链接")
    selected = selected.resolve(strict=True)
    if not selected.is_relative_to(root) or not stat.S_ISREG(selected.stat().st_mode):
        raise ValueError("只允许读取指定目录内的普通文件")
    if selected.stat().st_size > max_bytes:
        raise ValueError("文件超过大小上限")
    digest = hashlib.sha256()
    size = 0
    with selected.open("rb") as source:
        while chunk := source.read(min(max_bytes + 1 - size, 64 * 1024)):
            size += len(chunk)
            if size > max_bytes:
                raise ValueError("文件读取过程中超过大小上限")
            digest.update(chunk)
    return {"path": relative, "sizeBytes": size, "sha256": digest.hexdigest()}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", choices=ROOTS, required=True, help="input 为上传包，replay 为重放副本")
    parser.add_argument("--path", required=True, help="包内或工作目录中的相对文件路径")
    args = parser.parse_args()
    try:
        evidence = inspect_file(ROOTS[args.source], args.path)
    except (OSError, ValueError) as error:
        print(f"文件检查失败：{error}", file=sys.stderr)
        return 1
    print(json.dumps({"source": args.source, **evidence}, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
