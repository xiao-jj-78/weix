"""Contract and lifecycle tests. No WeChat client, SDK login or automation is executed."""
import io
import json
import tempfile
import time
import unittest
from concurrent.futures import ThreadPoolExecutor
from copy import deepcopy
from pathlib import Path
from unittest.mock import Mock, patch
from urllib.error import HTTPError
from urllib.request import ProxyHandler, Request, build_opener

from console_app import config
from console_app.__main__ import _run_worker, _serve
from console_app.remote_server import RemoteServer
from console_app.sdk_bridge import SdkBridge
from console_app.tasks import TaskRunner


class ConfigTests(unittest.TestCase):
    def test_known_values_are_normalized_without_mutating_input(self):
        original = {"remote_port": "8765", "adb_port": 5038}
        value = config.validate_config(original)
        self.assertEqual(value["remote_port"], 8765)
        self.assertEqual(value["adb_port"], "5038")
        self.assertEqual(original["remote_port"], "8765")

    def test_invalid_fields_are_rejected(self):
        cases = [{"remote_port": 65536}, {"round_like_count": True},
                 {"replica_watch": "false"}, {"main_window": [0, 0, 0, 5]},
                 {"adb_dir": []}, {"unrecognized": "anything"}]
        for value in cases:
            with self.subTest(value=value), self.assertRaises(config.ConfigError):
                config.validate_config(value)

    def test_atomic_save_and_partial_recovery(self):
        with tempfile.TemporaryDirectory() as directory:
            target = Path(directory) / "config.json"
            with patch.object(config, "config_path", return_value=target):
                config.save_config({"round_like_count": 50})
                self.assertEqual(config.load_config()["round_like_count"], 50)
                saved = target.read_bytes()
                with self.assertRaises(config.ConfigError):
                    config.save_config({"remote_port": -1})
                self.assertEqual(target.read_bytes(), saved)
                target.write_text(json.dumps({"round_like_count": 60, "remote_port": []}))
                recovered = config.load_config()
                self.assertEqual(recovered["round_like_count"], 60)
                self.assertEqual(recovered["remote_port"], 8765)
                self.assertEqual(list(Path(directory).glob("*.tmp")), [])


class RunnerTests(unittest.TestCase):
    def setUp(self):
        self.cfg = deepcopy(config.DEFAULT_CONFIG)
        self.runner = TaskRunner(lambda: self.cfg)

    def _fake_proc(self):
        proc = Mock()
        proc.pid = 12345
        proc.poll.return_value = None
        proc.stdout = io.StringIO()
        return proc

    def test_replica_defaults_are_shared_by_http_callers(self):
        self.cfg.update(replica_author="作者", replica_watch=True, replica_interval=5)
        with patch("console_app.tasks.subprocess.Popen", return_value=self._fake_proc()) as popen, \
                patch.object(self.runner, "_read_loop"):
            ok, message = self.runner.start("replica_like")
        self.assertTrue(ok, message)
        cmd = popen.call_args.args[0]
        self.assertIn("--worker", cmd)
        self.assertIn(str(config.bundle_root() / "main.py"), cmd)
        self.assertEqual(cmd[cmd.index("--author") + 1], "作者")
        self.assertEqual(cmd[cmd.index("--interval") + 1], "300")
        self.assertIn("--watch", cmd)

    def test_replica_requires_target_before_process_creation(self):
        with patch("console_app.tasks.subprocess.Popen") as popen:
            ok, _ = self.runner.start("replica_like")
        self.assertFalse(ok)
        popen.assert_not_called()

    def test_invalid_args_cannot_become_characters_or_objects(self):
        with patch("console_app.tasks.subprocess.Popen") as popen:
            for args in ("--watch", [3], {"bad": "args"}):
                self.assertFalse(self.runner.start("phone_like", args)[0])
        popen.assert_not_called()

    def test_simultaneous_start_creates_one_process(self):
        def spawn(*args, **kwargs):
            time.sleep(0.04)
            return self._fake_proc()

        with patch("console_app.tasks.subprocess.Popen", side_effect=spawn) as popen, \
                patch.object(self.runner, "_read_loop"):
            with ThreadPoolExecutor(max_workers=6) as pool:
                outcomes = list(pool.map(lambda _: self.runner.start("phone_like")[0], range(6)))
        self.assertEqual(outcomes.count(True), 1)
        self.assertEqual(popen.call_count, 1)

    def test_worker_bootstrap_adds_vendor_root_and_preserves_arguments(self):
        import sys

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            engine = root / "engines" / "example.py"
            engine.parent.mkdir()
            engine.write_text("# test")
            old_path, old_args = list(sys.path), list(sys.argv)
            try:
                with patch.object(config, "bundle_root", return_value=root), \
                        patch("console_app.__main__._inject_ocr_shim"), \
                        patch("console_app.__main__.runpy.run_path") as run:
                    self.assertEqual(_run_worker(str(engine), ["--author", "作者"]), 0)
                    self.assertIn(str(root), sys.path)
                    self.assertEqual(sys.argv[1:], ["--author", "作者"])
                    run.assert_called_once_with(str(engine), run_name="__main__")
            finally:
                sys.path[:] = old_path
                sys.argv[:] = old_args

    def test_headless_config_commit_updates_the_running_snapshot(self):
        snapshot = deepcopy(config.DEFAULT_CONFIG)
        captured = {}

        class FakeServer:
            state = Mock(port=8765)
            shutdown_requested = Mock()
            shutdown_requested.wait.return_value = True

            def __init__(self, runner, getter, setter, allow_shutdown=False):
                captured.update(runner=runner, getter=getter, setter=setter)

            def start(self, port):
                candidate = deepcopy(snapshot)
                candidate["replica_author"] = "新作者"
                captured["setter"](candidate)
                captured["author"] = captured["runner"]._cfg_getter()["replica_author"]
                return True, "test server"

            def stop(self):
                return True, "stopped"

        with patch.object(config, "load_config", return_value=snapshot), \
                patch.object(config, "save_config") as save, \
                patch.object(config, "lan_ips", return_value=[]), \
                patch("console_app.remote_server.RemoteServer", FakeServer), \
                patch("builtins.print"):
            self.assertEqual(_serve(8765), 0)
        self.assertEqual(captured["author"], "新作者")
        save.assert_called_once()


class HttpContractTests(unittest.TestCase):
    def setUp(self):
        self.cfg = deepcopy(config.DEFAULT_CONFIG)
        self.runner = TaskRunner(lambda: self.cfg)
        self.commits = []

        def setter(snapshot):
            self.commits.append(snapshot)
            self.cfg = snapshot

        self.server = RemoteServer(self.runner, lambda: self.cfg, setter)
        self.server.sdk = Mock(spec=SdkBridge)
        self.server.sdk.status.return_value = {"ok": True, "running": False, "plugins": []}
        self.assertTrue(self.server.start(0)[0])
        self.url = "http://127.0.0.1:%d" % self.server.state.port
        self.opener = build_opener(ProxyHandler({}))

    def tearDown(self):
        self.server.stop()

    def request(self, path, body=None, raw=None):
        data = raw if raw is not None else json.dumps(body).encode() if body is not None else None
        request = Request(self.url + path, data=data,
                          headers={"Content-Type": "application/json"})
        try:
            response = self.opener.open(request, timeout=3)
        except HTTPError as exc:
            response = exc
        with response:
            return response.code, json.loads(response.read())

    def test_status_contract_retains_five_task_ids(self):
        code, payload = self.request("/api/status")
        self.assertEqual(code, 200)
        self.assertTrue(payload["ok"])
        self.assertEqual(len(payload["tasks"]), 5)
        self.assertEqual(payload["remote"]["port"], self.server.state.port)

    def test_valid_config_update_is_visible_without_restart(self):
        self.assertEqual(self.request("/api/config", {"round_like_count": 80})[0], 200)
        self.assertEqual(self.request("/api/status")[1]["config"]["round_like_count"], 80)

    def test_bad_json_and_invalid_config_do_not_mutate_shared_state(self):
        for raw in (b"broken", b"[]", b'{"remote_port":0}', b'{"unexpected":4}'):
            with self.subTest(raw=raw):
                code, payload = self.request("/api/config", raw=raw)
                self.assertEqual(code, 400)
                self.assertFalse(payload["ok"])
        self.assertEqual(self.commits, [])
        self.assertEqual(self.cfg["remote_port"], 8765)

    def test_failed_save_does_not_mutate_existing_configuration(self):
        def reject(snapshot):
            raise OSError("read only")

        self.server.cfg_setter = reject
        code, payload = self.request("/api/config", {"round_like_count": 90})
        self.assertEqual(code, 500)
        self.assertFalse(payload["ok"])
        self.assertEqual(self.cfg["round_like_count"], 30)

    def test_task_failure_is_a_failure_in_both_http_and_body(self):
        with patch("console_app.tasks.subprocess.Popen") as popen:
            code, payload = self.request("/api/task/start", {"task": "unknown"})
            self.assertEqual(code, 400)
            self.assertFalse(payload["ok"])
            code, payload = self.request("/api/task/start", {"task": ["phone_like"]})
            self.assertEqual(code, 400)
        popen.assert_not_called()

    def test_log_count_boundaries(self):
        for number in ("-1", "601", "abc"):
            self.assertEqual(self.request("/api/logs?task=phone_like&n=" + number)[0], 400)
        self.assertEqual(self.request("/api/logs?task=phone_like&n=0")[1]["lines"], [])

    def test_sdk_routes_are_fixed_and_payloads_are_not_arbitrary_commands(self):
        self.assertEqual(self.request("/api/sdk/status")[1]["running"], False)
        self.assertEqual(self.request("/api/sdk/start", {"command": "anything"})[0], 400)
        self.server.sdk.start.assert_not_called()
        self.server.sdk.start.return_value = ({"ok": True, "message": "started"}, 200)
        self.assertTrue(self.request("/api/sdk/start", {})[1]["ok"])
        self.assertEqual(self.request("/api/sdk/execute", {})[0], 404)
        self.server.sdk.proxy.assert_not_called()

    def test_only_headless_service_accepts_shutdown(self):
        self.assertEqual(self.request("/api/service/stop", {})[0], 409)
        self.assertFalse(self.server.shutdown_requested.is_set())
        self.server.allow_shutdown = True
        self.assertTrue(self.request("/api/service/stop", {})[1]["ok"])
        self.assertTrue(self.server.shutdown_requested.is_set())

    def test_foreign_browser_origins_cannot_mutate_local_service(self):
        request = Request(self.url + "/api/config", data=b'{"round_like_count":80}',
                          headers={"Content-Type": "application/json", "Origin": "https://other.example"})
        with self.assertRaises(HTTPError) as response:
            self.opener.open(request, timeout=3)
        self.assertEqual(response.exception.code, 403)
        self.assertEqual(self.commits, [])


class SdkProxyTests(unittest.TestCase):
    def test_proxy_never_forwards_non_allowlisted_routes(self):
        bridge = SdkBridge()
        with patch.object(bridge, "_request") as forward:
            self.assertEqual(bridge.proxy("POST", "http://example.com", {})[1], 404)
            self.assertEqual(bridge.proxy("POST", "/api/sdk/login", {"command": "calc"})[1], 400)
            self.assertEqual(bridge.proxy("POST", "/api/sdk/plugins/run", {"id": "../../bad"})[1], 400)
            self.assertEqual(bridge.proxy("POST", "/api/sdk/plugins/run", {"id": "ok", "url": "http://example.com"})[1], 400)
        forward.assert_not_called()

    def test_offline_status_is_explicit_and_does_not_launch_anything(self):
        bridge = SdkBridge()
        with patch.object(bridge, "_request", return_value=({"ok": False, "message": "offline"}, 503)), \
                patch.object(bridge, "host_path", return_value=None), \
                patch("console_app.sdk_bridge.subprocess.Popen") as popen:
            status = bridge.status()
        self.assertTrue(status["ok"])
        self.assertFalse(status["running"])
        self.assertFalse(status["installed"])
        self.assertEqual(status["session"]["state"], "offline")
        popen.assert_not_called()


if __name__ == "__main__":
    unittest.main()
