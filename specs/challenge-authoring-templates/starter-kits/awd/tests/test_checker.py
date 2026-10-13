import contextlib
import datetime as dt
import importlib.util
import io
import json
import os
from pathlib import Path
import socket
import tempfile
import threading
import unittest
from unittest.mock import patch
import uuid
from http.server import ThreadingHTTPServer

ROOT = Path(__file__).resolve().parents[1]


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


checker = load("checker", ROOT / "checker/checker.py")
runtime_root = tempfile.TemporaryDirectory()
with patch.dict(os.environ, {"FLAG_ROOT": runtime_root.name}):
    runtime = load("runtime", ROOT / "runtime/server.py")


class CheckerTests(unittest.TestCase):
    def setUp(self):
        self.fail_write = False
        self.fail_function = False
        owner = self

        class Handler(runtime.Handler):
            def do_PUT(self):
                if owner.fail_write:
                    self.respond(503, b"write unavailable")
                else:
                    super().do_PUT()

            def do_GET(self):
                if owner.fail_function and self.path == "/health":
                    self.respond(503, b"function unavailable")
                else:
                    super().do_GET()

        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.current = self.flag(historical=False, write=True)
        self.historical = self.flag(historical=True, write=False)
        self.task = {"flags": [self.current, self.historical],
                     "deadline": (dt.datetime.now(dt.timezone.utc) + dt.timedelta(seconds=20)).isoformat()}
        self.save(self.historical)

    def tearDown(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join()

    @staticmethod
    def flag(historical, write):
        return {"flagId": str(uuid.uuid4()), "storeId": str(uuid.uuid4()), "storeName": "sample",
                "targetServiceName": "main", "flag": "flag{UTF8-测试} \n", "historical": historical,
                "requiresWrite": write, "privateReadContext": None, "publicLocator": None}

    def save(self, flag):
        path = Path(runtime_root.name) / uuid.UUID(flag["storeId"]).hex / uuid.UUID(flag["flagId"]).hex
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(flag["flag"].encode("utf-8"))
        return path

    def run_check(self):
        return checker.evaluate(self.task, {"main": "127.0.0.1"}, "127.0.0.1", self.server.server_port)

    def test_current_write_and_historical_read_preserve_exact_bytes(self):
        result = self.run_check()
        self.assertEqual(result["connection"]["outcome"], "Passed")
        self.assertEqual(result["functionality"]["outcome"], "Passed")
        self.assertEqual([x["read"]["outcome"] for x in result["flags"]], ["Passed", "Passed"])
        self.assertEqual(result["flags"][1]["write"]["outcome"], "NotRequired")
        self.assertNotIn(self.current["flag"], json.dumps(result, ensure_ascii=False))

    def test_history_loss_reports_recovery_evidence_without_rewriting_history(self):
        path = self.save(self.historical)
        path.unlink()
        result = self.run_check()
        self.assertEqual(result["flags"][0]["read"]["outcome"], "Passed")
        self.assertEqual(result["flags"][1]["read"], checker.probe("TargetFailure", "HistoricalFlagMissing"))
        self.assertFalse(path.exists())

    def test_failed_current_write_does_not_skip_historical_read(self):
        self.fail_write = True
        result = self.run_check()
        self.assertEqual(result["flags"][0]["write"], checker.probe("TargetFailure", "FlagStoreFailed"))
        self.assertEqual(result["flags"][1]["read"]["outcome"], "Passed")

    def test_failed_functionality_is_separate_from_flag_readability(self):
        self.fail_function = True
        result = self.run_check()
        self.assertEqual(result["functionality"], checker.probe("TargetFailure", "FunctionalCheckFailed"))
        self.assertTrue(all(x["read"]["outcome"] == "Passed" for x in result["flags"]))

    def test_unreachable_target_is_connection_evidence(self):
        with socket.socket() as unused:
            unused.bind(("127.0.0.1", 0))
            result = checker.evaluate(self.task, {"main": "127.0.0.1"}, "127.0.0.1", unused.getsockname()[1])
        self.assertEqual(result["connection"]["outcome"], "TargetFailure")
        self.assertIn(result["connection"]["reason"], ["ConnectionRefused", "ConnectTimeout"])
        self.assertEqual(result["functionality"], checker.probe("Unknown"))
        self.assertEqual(len(result["flags"]), 2)

    def test_expired_budget_keeps_manifest_and_never_fabricates_success(self):
        self.task["deadline"] = (dt.datetime.now(dt.timezone.utc) - dt.timedelta(seconds=1)).isoformat()
        result = self.run_check()
        self.assertEqual(result["connection"], checker.probe("Unknown", "CheckerTimedOut"))
        self.assertTrue(all(x["read"]["outcome"] == "Unknown" for x in result["flags"]))
        self.assertEqual(len(result["flags"]), 2)

    def test_duplicate_manifest_and_missing_service_are_rejected(self):
        with self.assertRaises(ValueError):
            checker.evaluate(self.task, {}, "127.0.0.1")
        self.task["flags"].append(self.current)
        with self.assertRaises(ValueError):
            self.run_check()

    def test_task_and_callback_failures_do_not_log_secrets(self):
        secret = "secret-task-or-token"
        env = {"NOCTF_CALLBACK_TOKEN": secret, "NOCTF_AWD_TASK_URL": "http://invalid/task",
               "NOCTF_CALLBACK_URL": "http://invalid/result", "NOCTF_TARGET_HOST": "target"}
        for task_fails in [True, False]:
            with self.subTest(task_fails=task_fails), patch.dict(os.environ, env), \
                 patch.object(checker, "request", side_effect=ValueError(secret) if task_fails else None,
                              return_value=json.dumps(self.task).encode()), \
                 patch.object(checker, "evaluate", return_value={}), \
                 patch.object(checker.HTTP, "open", side_effect=ValueError(secret)):
                output = io.StringIO()
                with contextlib.redirect_stderr(output):
                    self.assertEqual(checker.main(), 1)
                self.assertNotIn(secret, output.getvalue())


if __name__ == "__main__":
    unittest.main()
