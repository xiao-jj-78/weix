import base64
import importlib.util
import json
import os
import tempfile
import threading
import time
import unittest
from concurrent.futures import ThreadPoolExecutor
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

SPEC = importlib.util.spec_from_file_location("cloud_bridge", Path(__file__).parents[1] / "cloud_bridge.py")
bridge = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(bridge)


class TestProtector:
    """Test fixture only; production always uses DPAPI."""
    def protect(self, value):
        return base64.b64encode(value).decode()

    def unprotect(self, value):
        return base64.b64decode(value)


class MockServer:
    def __init__(self, callback):
        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *_):
                pass

            def handle_request(self):
                length = int(self.headers.get("Content-Length") or 0)
                body = json.loads(self.rfile.read(length)) if length else None
                code, payload = callback(self.command, self.path, body, self.headers)
                raw = json.dumps(payload).encode()
                self.send_response(code)
                if code == 302:
                    self.send_header("Location", payload["location"])
                self.send_header("Content-Length", str(len(raw)))
                self.send_header("Content-Type", "application/json")
                self.end_headers()
                self.wfile.write(raw)

            do_GET = handle_request
            do_POST = handle_request

        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.url = "http://127.0.0.1:" + str(self.server.server_port)

    def close(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join()


class BridgeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.local_calls, self.results = [], []
        self.result_fail = False
        self.active = self.max_active = 0
        self.call_lock = threading.Lock()
        self.local_delay = 0
        self.redirect = None
        self.poll_commands = []
        self.local_payload = None

        def local(method, path, body, headers):
            with self.call_lock:
                self.local_calls.append((method, path, body, headers.get("Authorization")))
                self.active += 1
                self.max_active = max(self.active, self.max_active)
            time.sleep(self.local_delay)
            with self.call_lock:
                self.active -= 1
            if self.redirect:
                return 302, {"location": self.redirect}
            return 200, self.local_payload if self.local_payload is not None else {"ok": True, "count": len(self.local_calls)}

        def cloud(method, path, body, headers):
            self.assertEqual(headers.get("Authorization"), "Bearer " + "a" * 48)
            if path == "/bridge/result":
                self.results.append(body)
                return (503, {"ok": False}) if self.result_fail else (200, {"ok": True})
            with self.call_lock:
                command = self.poll_commands.pop(0) if self.poll_commands else None
            return 200, {"ok": True, "command": command}

        self.local, self.cloud = MockServer(local), MockServer(cloud)
        self.journal = bridge.Journal(Path(self.temp.name) / "journal.db", TestProtector())
        self.agent = bridge.Bridge({"cloudUrl": self.cloud.url, "agentToken": "a" * 48, "agentId": "main-windows"}, self.journal, test_local_base=self.local.url)

    def tearDown(self):
        self.local.close()
        self.cloud.close()
        self.temp.cleanup()

    @staticmethod
    def command(identifier="one", method="GET", path="/api/status", body=None):
        return {"id": identifier, "method": method, "path": path, "body": body, "expiresAt": time.time() + 60, "claimToken": "fixture-claim"}

    def test_get_forwards_only_local_and_not_cloud_authorization(self):
        self.assertEqual(self.agent.execute(self.command()), "executed")
        self.assertEqual(self.local_calls, [("GET", "/api/status", None, None)])
        self.assertEqual(self.results[0]["claimToken"], "fixture-claim")
        self.assertEqual(self.results[0]["agentId"], "main-windows")

    def test_acknowledged_command_is_not_replayed(self):
        command = self.command(method="POST", path="/api/task/stop", body={"task": "replica_like"})
        self.agent.execute(command)
        self.assertEqual(self.agent.execute(command), "acknowledged")
        self.assertEqual(len(self.local_calls), 1)

    def test_failed_upload_retries_result_without_repeating_local_write(self):
        self.result_fail = True
        command = self.command(method="POST", path="/api/config", body={"replica_authors": ["Fixture"]})
        self.agent.execute(command)
        self.assertEqual(len(self.journal.pending()), 1)
        self.result_fail = False
        self.agent.retry_results()
        self.assertEqual(len(self.local_calls), 1)
        self.assertEqual(self.journal.pending(), [])
        self.assertEqual(self.results[0]["body"], self.results[1]["body"])

    def test_local_timeout_is_durable_and_never_retried(self):
        attempts = []
        def failed_request(*args):
            attempts.append(args)
            raise OSError("fixture timeout after sending")
        self.agent.local_request = failed_request
        command = self.command(method="POST", path="/api/config", body={})
        self.agent.execute(command)
        self.agent.execute(command)
        self.assertEqual(len(attempts), 1)
        self.assertEqual(self.results[-1]["body"]["state"], "uncertain")

    def test_worker_polls_mock_cloud_and_returns_result_without_replay(self):
        command = self.command()
        self.poll_commands = [command, command]
        runner = threading.Thread(target=self.agent.run)
        runner.start()
        deadline = time.time() + 5
        while time.time() < deadline and not self.results:
            time.sleep(.05)
        self.agent.stop.set()
        runner.join(5)
        self.assertFalse(runner.is_alive())
        self.assertTrue(self.results)
        self.assertEqual(len(self.local_calls), 1)

    def test_empty_poll_waits_three_seconds_before_polling_again(self):
        class StopAfterWait:
            stopped = False
            waits = []

            def is_set(self):
                return self.stopped

            def set(self):
                self.stopped = True

            def wait(self, seconds):
                self.waits.append(seconds)
                self.stopped = True
                return True

        stop = StopAfterWait()
        self.agent.stop = stop
        self.agent.worker()
        self.assertEqual(stop.waits, [3])
        self.assertEqual(self.local_calls, [])

    def test_restart_of_incomplete_command_reports_uncertain_never_executes(self):
        command = self.command(method="POST", path="/api/config", body={"round_like_count": 1})
        self.journal.reserve(command)
        restarted = bridge.Journal(Path(self.temp.name) / "journal.db", TestProtector())
        agent = bridge.Bridge(self.agent.config, restarted, test_local_base=self.local.url)
        self.assertEqual(agent.execute(command), "completed")
        self.assertEqual(self.local_calls, [])
        self.assertEqual(self.results[-1]["body"]["state"], "uncertain")

    def test_conflicting_replay_never_executes(self):
        command = self.command()
        self.agent.execute(command)
        changed = {**command, "path": "/api/config"}
        self.assertEqual(self.agent.execute(changed), "conflict")
        self.assertEqual(len(self.local_calls), 1)

    def test_rejects_unsafe_routes_queries_and_task_arguments(self):
        bad = [("GET", "https://evil.invalid/api/status", None),
            ("GET", "//evil.invalid/api/status", None),
            ("GET", "/api/status?url=https://evil.invalid", None),
            ("GET", "/api/%73tatus", None), ("GET", "/api/status#x", None),
            ("GET", "/api/status\\..\\other", None),
            ("GET", "/api/logs?task=replica_like&n=1&url=x", None),
            ("GET", "/api/logs?task=replica_like&n=1&n=2", None),
            ("GET", "/api/logs?task=other&n=1", None),
            ("GET", "/api/logs?task=replica_like&n=501", None),
            ("POST", "/api/service/stop", {}), ("POST", "/api/sdk/host/stop", {}),
            ("POST", "/api/task/start", {"task": "replica_like", "args": ["--anything"]}),
            ("POST", "/api/sdk/plugins/run", {"id": "arbitrary-dll"})]
        for number, (method, path, body) in enumerate(bad):
            with self.subTest(path=path), self.assertRaises(ValueError):
                self.agent.execute(self.command(str(number), method, path, body))
        self.assertEqual(self.local_calls, [])

    def test_expiry_checked_before_dispatch(self):
        command = self.command()
        command["expiresAt"] = time.time() - 1
        with self.assertRaises(ValueError):
            self.agent.execute(command)
        self.assertEqual(self.local_calls, [])

    def test_expiry_checked_after_waiting_for_write_lock(self):
        command = self.command(method="POST", path="/api/config", body={})
        command["expiresAt"] = time.time() + .15
        self.agent.write_lock.acquire()
        thread = threading.Thread(target=self.agent.execute, args=(command,))
        thread.start()
        time.sleep(.3)
        self.agent.write_lock.release()
        thread.join()
        self.assertEqual(self.local_calls, [])
        self.assertEqual(self.results[-1]["body"]["state"], "not_executed")

    def test_stopping_never_starts_local_work(self):
        self.agent.stop.set()
        self.agent.execute(self.command())
        self.assertEqual(self.local_calls, [])

    def test_three_reads_can_run_in_parallel(self):
        self.local_delay = .2
        with ThreadPoolExecutor(max_workers=3) as executor:
            list(executor.map(self.agent.execute, [self.command(str(n)) for n in range(3)]))
        self.assertEqual(self.max_active, 3)

    def test_writes_are_serialized(self):
        self.local_delay = .1
        with ThreadPoolExecutor(max_workers=3) as executor:
            list(executor.map(self.agent.execute, [self.command(str(n), "POST", "/api/config", {}) for n in range(3)]))
        self.assertEqual(self.max_active, 1)

    def test_redirect_never_reaches_second_server(self):
        self.redirect = self.cloud.url + "/stolen"
        self.agent.execute(self.command())
        self.assertEqual(len(self.local_calls), 1)
        self.assertEqual(self.results[0]["status"], 302)

    def test_large_result_returns_bounded_error_instead_of_upload_loop(self):
        self.local_payload = {"ok": True, "large": "x" * (512 * 1024)}
        self.agent.execute(self.command())
        self.assertEqual(self.results[0]["status"], 502)
        self.assertEqual(self.journal.pending(), [])
        self.assertEqual(len(self.local_calls), 1)

    def test_log_query_is_rebuilt_from_validated_fields(self):
        self.agent.execute(self.command(path="/api/logs?n=025&task=replica_like"))
        self.assertEqual(self.local_calls[0][1], "/api/logs?task=replica_like&n=25")

    @unittest.skipUnless(os.name == "nt", "DPAPI is Windows-only")
    def test_dpapi_config_and_journal_do_not_store_plain_secrets(self):
        config_path = Path(self.temp.name) / "config.json"
        first, second = "agent-test-" + "a" * 40, "browser-test-" + "b" * 40
        metadata = bridge.save_config("https://example.invalid", first, second, config_path)
        raw = config_path.read_text()
        self.assertNotIn(first, raw)
        self.assertNotIn(second, raw)
        self.assertNotIn("Token", json.dumps(metadata))
        self.assertEqual(bridge.load_config(config_path)["agentToken"], first)
        journal = bridge.Journal(Path(self.temp.name) / "protected.db")
        journal.reserve(self.command())
        journal.complete("one", {"status": 200, "body": {"fixtureSecret": second}})
        self.assertNotIn(second.encode(), (Path(self.temp.name) / "protected.db").read_bytes())
        self.assertEqual(journal.pending()[0][1]["body"]["fixtureSecret"], second)

    def test_cloud_config_rejects_non_https_credentials_and_queries(self):
        for value in ("http://example.invalid", "https://user:pass@example.invalid", "https://example.invalid?token=x", "https://example.invalid/#x", "https://example.invalid:8765", "https://example.invalid/%2e%2e"):
            with self.subTest(value=value), self.assertRaises(ValueError):
                bridge.validate_cloud_url(value)


if __name__ == "__main__":
    unittest.main()
