"""Local browser verification using synthetic node responses, never real WeChat."""

from __future__ import annotations

import json
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlsplit

from playwright.sync_api import sync_playwright


PUBLIC = Path(__file__).resolve().parents[1] / "public"
EVIDENCE = Path(__file__).resolve().parent / "evidence"
ACCESS = "local-browser-fixture-access"
STATE = {"online": False, "expired": False, "requests": [], "unknown": False, "apiBase": "", "origin": ""}
CONFIG = {"round_like_count": 30, "round_rest_minutes": 60, "run_start_hour": 8,
          "run_end_hour": 0, "wechat_count": 2, "adb_port": "5038",
          "replica_authors": ["测试联系人甲"], "replica_author": "", "replica_keyword": ""}
AI = {"activeProfileId": "fixture", "profiles": [{"id": "fixture", "name": "本地验证方案",
      "protocol": "openai-compatible", "baseUrl": "https://fixture.invalid/v1", "model": "fixture-model",
      "hasApiKey": False}], "allowedContacts": [], "systemPrompt": "本地界面验证",
      "minReplySeconds": 15, "maxRepliesPerHour": 20, "sessionMinutes": 60, "account": ""}


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *_):
        pass

    def json(self, value, status=200):
        data = json.dumps(value, ensure_ascii=False).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        if self.headers.get("Origin") == STATE["origin"]:
            self.send_header("Access-Control-Allow-Origin", STATE["origin"])
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        path = urlsplit(self.path).path
        path = path.removeprefix("/wechat-workspace-api")
        if path.startswith("/api/"):
            self.api(path)
            return
        name = path.removeprefix("/console/")
        if name not in {"index.html", "cloud-config.js", "cloud-bootstrap.js", "cloud.css"}:
            self.send_error(404)
            return
        data = (PUBLIC / name).read_bytes()
        if name == "cloud-config.js" and STATE["apiBase"]:
            data = ("window.WLC_CLOUD_CONFIG=" + json.dumps({"API_BASE": STATE["apiBase"]}) + ";").encode()
        self.send_response(200)
        self.send_header("Content-Type", "text/html; charset=utf-8" if name.endswith("html") else
                         "text/css" if name.endswith("css") else "application/javascript")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_POST(self):
        self.api(urlsplit(self.path).path.removeprefix("/wechat-workspace-api"))

    def do_OPTIONS(self):
        self.send_response(204)
        if self.headers.get("Origin") == STATE["origin"]:
            self.send_header("Access-Control-Allow-Origin", STATE["origin"])
        self.send_header("Access-Control-Allow-Headers", "Authorization, Content-Type, X-Idempotency-Key")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        self.end_headers()

    def api(self, path):
        length = int(self.headers.get("Content-Length", "0"))
        body = json.loads(self.rfile.read(length) or b"{}") if length else None
        STATE["requests"].append({"method": self.command, "path": path,
                                  "authorized": self.headers.get("Authorization") == "Bearer " + ACCESS,
                                  "idempotency": self.headers.get("X-Idempotency-Key"), "body": body})
        if STATE["expired"] or self.headers.get("Authorization") != "Bearer " + ACCESS:
            self.json({"ok": False, "message": "云访问码不正确"}, 401)
        elif path == "/api/cloud/status":
            self.json({"ok": True, "nodeOnline": STATE["online"], "lastSeen": None, "agentId": None})
        elif not STATE["online"]:
            self.json({"ok": False, "message": "Windows 工作节点离线，请在电脑上启动云连接器", "code": "NODE_OFFLINE"}, 503)
        elif path == "/api/status":
            self.json({"ok": True, "version": "fixture-only", "uptime": 60,
                       "tasks": [{"id": "replica_like", "name": "定点点赞 · 本地验证", "desc": "合成数据，不连接微信",
                                  "running": False, "exit_code": None, "elapsed": 0}], "config": CONFIG})
        elif path == "/api/logs":
            self.json({"ok": True, "lines": ["本地界面验证，不连接真实微信。"]})
        elif path == "/api/config":
            if body:
                CONFIG.update(body)
            self.json({"ok": True, "config": CONFIG})
        elif path == "/api/sdk/ai/config":
            if body:
                AI.update(body)
                for profile in AI["profiles"]:
                    profile.pop("apiKey", None)
            self.json({"ok": True, "config": AI})
        elif path == "/api/sdk/ai/status":
            self.json({"ok": True, "running": False, "state": "fixture", "replyCount": 0})
        elif path == "/api/sdk/ai/preview":
            self.json({"ok": True, "reply": "本地合成预览，未调用 AI，未发送微信。", "sentToWechat": False})
        elif path == "/api/task/start" and STATE["unknown"]:
            self.json({"ok": False, "message": "等待节点响应超时", "code": "EXECUTION_UNCERTAIN"}, 504)
        elif path == "/api/task/start":
            self.json({"ok": True, "message": "本地验证请求已记录，未执行微信任务。"})
        else:
            self.json({"ok": True, "data": [], "plugins": [], "running": False})


def no_overflow(page):
    assert page.evaluate("document.documentElement.scrollWidth <= innerWidth"), "Page has horizontal overflow"


def main():
    EVIDENCE.mkdir(exist_ok=True)
    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    api_server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    STATE["origin"] = f"http://127.0.0.1:{server.server_port}"
    # Always replace the production configuration with this local fixture;
    # verification must never contact the deployed cloud endpoint.
    STATE["apiBase"] = STATE["origin"]
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    threading.Thread(target=api_server.serve_forever, daemon=True).start()
    checks = []
    try:
        with sync_playwright() as p:
            browser = p.chromium.launch(headless=True)
            page = browser.new_page(viewport={"width": 1440, "height": 1000})
            errors = []
            page.on("pageerror", lambda e: errors.append(str(e)))
            page.goto(f"http://127.0.0.1:{server.server_port}/console/index.html")
            page.wait_for_load_state("networkidle")
            assert not STATE["requests"], "An API was called before authentication"
            assert page.locator("#cloud-access-code").get_attribute("type") == "password"
            assert page.locator("#cloud-workspace").get_attribute("inert") is not None
            checks.append("Unauthenticated polls are stopped and workspace is inert")
            no_overflow(page)
            page.screenshot(path=str(EVIDENCE / "cloud-login-desktop.png"), full_page=True)
            page.locator("#cloud-access-code").fill("wrong-fixture")
            page.locator("#cloud-login-submit").click()
            page.wait_for_function("document.getElementById('cloud-login-error').textContent.includes('不正确')")
            assert page.evaluate("sessionStorage.length") == 0
            checks.append("Invalid access code is rejected without storing it")
            page.locator("#cloud-access-code").fill(ACCESS)
            page.locator("#cloud-login-submit").click()
            page.locator("#cloud-login").wait_for(state="hidden")
            assert "节点离线" in page.locator("#cloud-node-state").inner_text()
            assert "Windows" in page.locator("#cloud-node-banner").inner_text()
            assert "云容器不承载微信桌面" in page.locator("#view-sdk").inner_text()
            checks.append("Cloud login succeeds while Windows node remains explicitly offline")
            no_overflow(page)
            page.screenshot(path=str(EVIDENCE / "cloud-node-offline-desktop.png"), full_page=True)
            STATE["online"] = True
            page.evaluate("refresh()")
            page.get_by_role("button", name="开始定点点赞 · 本地验证").wait_for()
            page.get_by_role("button", name="开始定点点赞 · 本地验证").click()
            page.wait_for_function("document.getElementById('notice').textContent.includes('本地验证请求已记录')", timeout=6000)
            writes = [r for r in STATE["requests"] if r["method"] == "POST"]
            assert len(writes) == 1 and len(writes[0]["idempotency"]) == 36 and writes[0]["authorized"]
            checks.append("Write requests include a fresh idempotency UUID and browser bearer")
            STATE["unknown"] = True
            page.get_by_role("button", name="开始定点点赞 · 本地验证").click()
            page.wait_for_function("document.getElementById('notice').textContent.includes('执行状态未知')")
            page.wait_for_timeout(500)
            assert len([r for r in STATE["requests"] if r["method"] == "POST"]) == 2
            checks.append("Timed out writes show unknown execution state and are never automatically retried")
            page.locator("#tab-targets").click()
            page.locator("#target-summary").filter(has_text="1").wait_for()
            page.locator("#target-manual").fill("测试联系人乙")
            page.locator("#targets-add").click()
            page.locator("#targets-save").click()
            page.wait_for_function("document.getElementById('targets-state').textContent.includes('已保存 2')")
            assert CONFIG["replica_authors"] == ["测试联系人甲", "测试联系人乙"]
            checks.append("Existing explicit Moments user selection saves through cloud wrapper")
            page.locator("#tab-ai").click()
            page.wait_for_function("document.getElementById('ai-name').value === '本地验证方案'")
            page.locator("#ai-sample").fill("测试样例")
            page.locator("#ai-preview").click()
            page.wait_for_function("document.getElementById('ai-result').textContent.includes('本地合成预览')")
            assert "未发送到微信" in page.locator("#notice").inner_text()
            checks.append("Existing AI profile and sample preview work against synthetic fixture")
            page.set_viewport_size({"width": 390, "height": 844})
            no_overflow(page)
            page.screenshot(path=str(EVIDENCE / "cloud-ai-mobile.png"), full_page=True)
            checks.append("Mobile viewport has no horizontal page overflow")
            STATE["expired"] = True
            page.evaluate("refresh()")
            page.locator("#cloud-login").wait_for(state="visible")
            page.wait_for_function("document.getElementById('cloud-login-error').textContent.includes('失效')")
            assert page.evaluate("sessionStorage.length") == 0
            assert page.locator("#ai-key").input_value() == ""
            checks.append("401 clears browser access and secret input and restores login overlay")
            no_overflow(page)
            page.screenshot(path=str(EVIDENCE / "cloud-login-mobile.png"), full_page=True)
            assert not errors, errors
            keys = [r["idempotency"] for r in STATE["requests"] if r["method"] == "POST"]
            assert len(keys) == len(set(keys))
            checks.append("All user writes have distinct UUIDs; no JavaScript errors")
            STATE["expired"] = False
            STATE["apiBase"] = f"http://127.0.0.1:{api_server.server_port}/wechat-workspace-api"
            cross = browser.new_page(viewport={"width": 390, "height": 844})
            cross.goto(f"{STATE['origin']}/console/index.html")
            cross.wait_for_load_state("networkidle")
            cross.locator("#cloud-access-code").fill(ACCESS)
            cross.locator("#cloud-login-submit").click()
            cross.locator("#cloud-login").wait_for(state="hidden")
            cross.get_by_role("button", name="开始定点点赞 · 本地验证").wait_for()
            no_overflow(cross)
            checks.append("Static assets work in a subdirectory with a separate-origin prefixed API and explicit CORS")
            cross.locator("#cloud-logout").click()
            cross.locator("#cloud-login").wait_for(state="visible")
            assert cross.evaluate("sessionStorage.length") == 0
            checks.append("Explicit logout clears browser session access")
            cross.close()
            browser.close()
        result = {"ok": True, "fixtureOnly": True, "realWechatOperations": False,
                  "cloudDeploymentVerified": False, "checks": checks}
        (EVIDENCE / "cloud-dashboard-result.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        print(json.dumps(result, ensure_ascii=False))
    finally:
        server.shutdown()
        server.server_close()
        api_server.shutdown()
        api_server.server_close()


if __name__ == "__main__":
    main()
