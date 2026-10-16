"""Native UI feature regression with a real Python gateway and an isolated SDK fixture.

The SDK fixture never touches WeChat or an external AI service. All names are synthetic.
"""
from pathlib import Path
from copy import deepcopy
import json
import subprocess
import sys
import time
from pywinauto import Desktop

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "build" / "console"))
from console_app import config
from console_app.tasks import TaskRunner
from console_app.remote_server import RemoteServer

OUTPUT = ROOT / "build" / "qa" / "evidence"
OUTPUT.mkdir(parents=True, exist_ok=True)
calls = []


class SdkFixture:
    def __init__(self):
        self.running = False
        self.configuration = {
            "activeProfileId": "qa", "profiles": [{"id": "qa", "name": "QA Provider", "protocol": "openai-compatible", "baseUrl": "http://127.0.0.1:19999/v1", "model": "qa-model", "apiKeyEnv": "", "hasApiKey": False}],
            "systemPrompt": "QA prompt", "allowedContacts": [], "account": "", "allowGroups": False,
            "minReplySeconds": 15, "maxRepliesPerHour": 20, "sessionMinutes": 60,
        }

    def status(self):
        return {"ok": True, "running": True, "version": "fixture", "message": "Isolated UI fixture; no WeChat operations", "plugins": []}

    def proxy(self, method, path, body=None):
        calls.append({"method": method, "path": path})
        if path == "/api/sdk/contacts":
            return {"ok": True, "data": [{"account": "QA account", "names": ["QA Alice", "QA Bob", "QA Carol"]}]}, 200
        if path == "/api/sdk/ai/config":
            if method == "POST":
                self.configuration.update(deepcopy(body))
                for profile in self.configuration["profiles"]:
                    if profile.pop("apiKey", ""):
                        profile["hasApiKey"] = True
            return {"ok": True, "config": deepcopy(self.configuration), "running": self.running}, 200
        if path == "/api/sdk/ai/status":
            return {"ok": True, "running": self.running, "state": "running" if self.running else "stopped", "replyCount": 0}, 200
        if path == "/api/sdk/ai/preview":
            return {"ok": True, "reply": "QA preview only: " + body["message"], "sentToWechat": False}, 200
        if path in ("/api/sdk/ai/start", "/api/sdk/ai/stop"):
            self.running = path.endswith("/start")
            return {"ok": True, "running": self.running}, 200
        return {"ok": False, "message": "Fixture does not implement this route"}, 404

    def close(self):
        pass


def wait_until(predicate, description, timeout=15):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        try:
            if predicate():
                return
        except Exception:
            pass
        time.sleep(.2)
    raise AssertionError(description)


saved = deepcopy(config.DEFAULT_CONFIG)
runner = TaskRunner(lambda: saved)
server = RemoteServer(runner, lambda: deepcopy(saved), lambda value: saved.update(value))
fixture = SdkFixture()
server.sdk = fixture
ok, message = server.start(18898)
assert ok, message
exe = ROOT / "build" / "maui" / "WeChatLikeMaui" / "bin" / "Release" / "net10.0-windows10.0.19041.0" / "win-x64" / "WeChatLikeMaui.exe"
process = None
checks = []
try:
    process = subprocess.Popen([str(exe)], cwd=exe.parent)
    window = Desktop(backend="uia").window(process=process.pid, control_type="Window")
    window.wait("visible", timeout=45)
    time.sleep(1)

    def control(identifier, kind=None):
        args = {"auto_id": identifier, "visible_only": False}
        if kind:
            args["control_type"] = kind
        return window.child_window(**args)

    def set_entry(identifier, text):
        control(identifier, "Edit").wrapper_object().iface_value.SetValue(text)

    def click(identifier):
        control(identifier, "Button").invoke()

    def add_contacts(prefix, names):
        entry = control(prefix + "AddEntry", "Edit")
        entry.wrapper_object().iface_value.SetValue(names)
        parent = entry.wrapper_object().parent()
        buttons = parent.children(control_type="Button")
        assert len(buttons) == 1, "Contact add action must be unambiguous"
        buttons[0].invoke()

    window.child_window(auto_id="QuickConnectionBtn",control_type="Button").invoke()
    set_entry("IpEntry", "127.0.0.1")
    set_entry("PortEntry", "18898")
    click("ConnectBtn")
    wait_until(lambda: control("ConnectBtn").is_enabled() and "已连接" in control("StatusLabel").window_text(), "Native did not finish connecting")
    click("SettingsTab")
    add_contacts("Moments", "QA Alice,QA Bob")
    wait_until(lambda: "已选 2 位" in control("MomentsSummary").window_text(), "Multiple Moments selections were not retained")
    control("MomentsSearch").child_window(auto_id="TextBox", control_type="Edit", visible_only=False).wrapper_object().iface_value.SetValue("Alice")
    click("SaveConfigBtn")
    wait_until(lambda: saved["replica_authors"] == ["QA Alice", "QA Bob"], "Moments selected users were not persisted")
    assert saved["replica_author"] == "QA Alice"
    checks.append("Moments multi-select, search preserves choices, real gateway save")
    control("MainScroll").wrapper_object().iface_scroll.SetScrollPercent(-1, 100)
    time.sleep(.5)
    window.capture_as_image().save(OUTPUT / "native-moments-selected.png")

    click("AiTab")
    wait_until(lambda: control("AiSaveBtn").is_enabled(), "AI configuration did not load")
    set_entry("AiProfileNameEntry", "Native QA Provider")
    set_entry("AiBaseUrlEntry", "http://127.0.0.1:19999/v1")
    set_entry("AiModelEntry", "native-qa-model")
    set_entry("AiKeyEntry", "synthetic-key-not-a-real-secret")
    add_contacts("AiContacts", "QA Alice,QA Bob")
    assert not control("AiStartBtn").is_enabled(), "Unsaved AI settings must block start"
    click("AiSaveBtn")
    wait_until(lambda: fixture.configuration["profiles"][0]["name"] == "Native QA Provider" and control("AiSaveBtn").is_enabled(), "AI profile was not committed")
    assert fixture.configuration["allowedContacts"] == ["QA Alice", "QA Bob"]
    assert control("AiKeyEntry", "Edit").get_value() == "", "Saved key must never be echoed"
    checks.append("AI profile + whitelist committed, password cleared, dirty state blocks start")
    set_entry("AiSampleEditor", "Native sample")
    click("AiPreviewBtn")
    wait_until(lambda: "QA preview only: Native sample" in control("AiPreviewLabel").window_text(), "AI preview text missing")
    assert not fixture.running
    checks.append("AI preview rendered using SDK fixture; no message sent")
    window.capture_as_image().save(OUTPUT / "native-ai-configured.png")
    click("AiStartBtn")
    wait_until(lambda: fixture.running and control("AiStopBtn").is_enabled(), "Start state missing")
    assert not control("AiSaveBtn").is_enabled()
    click("AiStopBtn")
    wait_until(lambda: not fixture.running and control("AiSaveBtn").is_enabled(), "Stop state missing")
    checks.append("Explicit start/stop UI state only against isolated fixture")
    assert not runner.running_ids()
    assert not any("task/start" in call["path"] for call in calls)
    report = {"ok": True, "checks": checks, "sdkFixture": True, "realWeChatOperations": False, "calls": calls, "momentsAuthors": saved["replica_authors"]}
    (OUTPUT / "native-features-result.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False))
finally:
    if process and process.poll() is None:
        try:
            window.capture_as_image().save(OUTPUT / "native-feature-final.png")
        except Exception:
            pass
        process.terminate()
        process.wait(timeout=10)
    runner.stop_all()
    server.stop()
