import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import Mock, patch
import subprocess
import sys

spec = importlib.util.spec_from_file_location("lease_guard", Path(__file__).with_name("lease_guard.py"))
guard = importlib.util.module_from_spec(spec)
spec.loader.exec_module(guard)


class LeaseGuardTests(unittest.TestCase):
    def test_reap_timeout_is_bounded_and_does_not_disclose_arguments(self):
        child = Mock()
        child.poll.return_value = None
        child.wait.side_effect = subprocess.TimeoutExpired("secret-command", 1)
        with patch("builtins.print") as log:
            self.assertFalse(guard.reap_child(child))
        child.kill.assert_called_once_with()
        child.wait.assert_called_once_with(timeout=1)
        self.assertNotIn("secret-command", str(log.call_args_list))

    def test_exit_between_poll_and_kill_is_reaped(self):
        child = Mock()
        child.poll.return_value = None
        child.kill.side_effect = ProcessLookupError()
        self.assertTrue(guard.reap_child(child))
        child.wait.assert_called_once_with(timeout=1)

    def test_real_child_is_killed_and_reaped_and_repeated_cleanup_is_safe(self):
        child = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(60)"])
        try:
            self.assertTrue(guard.reap_child(child))
            self.assertIsNotNone(child.returncode)
            self.assertTrue(guard.reap_child(child))
            self.assertTrue(guard.reap_child(None))
        finally:
            if child.poll() is None:
                child.kill()
                child.wait(timeout=5)

    def test_main_reports_failure_instead_of_crashing_from_finally(self):
        child = Mock()
        child.poll.return_value = None
        child.wait.side_effect = subprocess.TimeoutExpired("secret-command", 1)
        with patch.dict(guard.os.environ, {"NOCTF_PUBLICATION_ID": "00000000-0000-4000-8000-000000000001"}), \
                patch.object(guard, "lease_deadline", return_value=guard.time.monotonic() + 5), \
                patch.object(guard, "read_lease", side_effect=[b"{}", OSError()]), \
                patch.object(guard.subprocess, "Popen", return_value=child), \
                patch.object(guard.signal, "signal"), patch("builtins.print"):
            self.assertEqual(guard.main(), 1)

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
