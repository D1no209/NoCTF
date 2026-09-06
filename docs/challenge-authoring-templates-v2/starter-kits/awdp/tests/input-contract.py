#!/usr/bin/env python3
"""不依赖 Docker 的输入文件与重放产物边界测试。"""
import hashlib
import importlib.util
import tempfile
import unittest
from pathlib import Path

KIT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("inspect_fix_file", KIT / "private/checker/inspect-file.py")
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class InputContractTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="noctf-v2-input-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)

    def test_input_and_replay_are_distinct_sources(self):
        for name, content in [("input", b"submitted"), ("replay", b"generated")]:
            folder = self.root / name
            folder.mkdir()
            (folder / "policy.txt").write_bytes(content)
            evidence = MODULE.inspect_file(folder, "policy.txt")
            self.assertEqual(evidence["sha256"], hashlib.sha256(content).hexdigest())
            self.assertEqual(evidence["sizeBytes"], len(content))
            self.assertNotIn("content", evidence)

    def test_invalid_paths_are_rejected(self):
        for path in ["", "/etc/passwd", "../private", "x/../private", "x//y", "x\\y", "./x"]:
            with self.subTest(path=path), self.assertRaises(ValueError):
                MODULE.inspect_file(self.root, path)

    def test_missing_files_and_directories_are_not_accepted(self):
        with self.assertRaises(FileNotFoundError):
            MODULE.inspect_file(self.root, "absent")
        (self.root / "directory").mkdir()
        with self.assertRaises(ValueError):
            MODULE.inspect_file(self.root, "directory")

    def test_oversized_input_is_rejected(self):
        (self.root / "large").write_bytes(b"12345")
        with self.assertRaises(ValueError):
            MODULE.inspect_file(self.root, "large", max_bytes=4)

    def test_symlink_cannot_redirect_to_private_files(self):
        secret = self.root / "private.txt"
        secret.write_text("private-test-sentinel", encoding="utf-8")
        link = self.root / "link"
        try:
            link.symlink_to(secret)
        except OSError:
            self.skipTest("当前系统未允许创建测试符号链接")
        with self.assertRaises(ValueError):
            MODULE.inspect_file(self.root, "link")


if __name__ == "__main__":
    unittest.main()
