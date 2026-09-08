import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("lease_guard", Path(__file__).with_name("lease_guard.py"))
guard = importlib.util.module_from_spec(spec)
spec.loader.exec_module(guard)


class LeaseGuardTests(unittest.TestCase):
    def lease(self, data):
        directory = tempfile.TemporaryDirectory(prefix="noctf-gateway-lease-test-")
        self.addCleanup(directory.cleanup)
        path = Path(directory.name) / "lease.json"
        path.write_text(json.dumps(data), encoding="utf-8")
        return path

    def test_bounded_expiry_uses_monotonic_deadline(self):
        path = self.lease({"publicationId": "A", "expiresAtUnixMs": 105000})
        self.assertEqual(guard.lease_deadline(path, "A", 100, 20), 25)

    def test_stale_publication_cannot_reuse_lease(self):
        path = self.lease({"publicationId": "A", "expiresAtUnixMs": 105000})
        with self.assertRaises(ValueError):
            guard.lease_deadline(path, "B", 100, 20)

    def test_expired_or_unbounded_lease_fails_closed(self):
        for expiry in [0, 100000, 110001, True, "105000", None]:
            with self.subTest(expiry=expiry):
                path = self.lease({"publicationId": "A", "expiresAtUnixMs": expiry})
                with self.assertRaises(ValueError):
                    guard.lease_deadline(path, "A", 100, 20)

    def test_oversized_input_fails_closed(self):
        path = self.lease({"publicationId": "A" * 1024})
        with self.assertRaises(ValueError):
            guard.lease_deadline(path, "A", 100, 20)


if __name__ == "__main__":
    unittest.main()
