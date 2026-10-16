"""Verify the console gateway forwards the read-only chat route to the local C# host.

Uses the real SdkBridge (only its outbound _request is stubbed), so route whitelist,
query validation and dispatch are all exercised. No WeChat, no device, no real host.
"""
from pathlib import Path
import json
import sys
import urllib.request
from urllib.error import HTTPError

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'build/qa/evidence'
OUT.mkdir(exist_ok=True, parents=True)
sys.path.insert(0, str(ROOT / 'build/console'))
from console_app import config
from console_app.tasks import TaskRunner
from console_app.remote_server import RemoteServer


def http(base, path):
    request = urllib.request.Request(base + path)
    try:
        response = urllib.request.urlopen(request, timeout=8)
    except HTTPError as error:
        response = error
    with response:
        return response.status, json.load(response)


def main():
    checks = []
    bridge_holder = {}

    def check(name, condition, detail=""):
        checks.append({"name": name, "ok": bool(condition), "detail": detail})

    server = RemoteServer(TaskRunner(config.load_config), config.load_config, lambda values: None)
    server.start(18917)
    base = "http://127.0.0.1:18917"
    try:
        bridge = server.sdk
        bridge_holder['bridge'] = bridge
        forwarded = []

        def fake_request(method, path, body=None, timeout=4, query=""):
            forwarded.append({"method": method, "path": path, "body": body, "query": query, "timeout": timeout})
            return {"ok": True, "echo": path, "who": query}, 200

        bridge._request = fake_request

        check("route whitelisted", ("GET", "/api/sdk/chat") in bridge.ROUTES)

        status, payload = http(base, "/api/sdk/chat?who=filehelper")
        check("valid who forwarded", status == 200 and forwarded and forwarded[-1]["query"] == "who=filehelper",
              f"status={status} forwarded={forwarded[-1:] }")
        check("chat timeout raised", forwarded and forwarded[-1]["timeout"] == 105,
              f"timeout={forwarded[-1]['timeout'] if forwarded else None}")

        before = len(forwarded)
        status, payload = http(base, "/api/sdk/chat")
        check("missing who rejected", status == 400 and len(forwarded) == before,
              f"status={status} message={payload.get('message')}")

        status, payload = http(base, "/api/sdk/chat?who=..%2Fetc")
        check("path traversal rejected", status == 400 and len(forwarded) == before,
              f"status={status} message={payload.get('message')}")

        status, payload = http(base, "/api/sdk/chat?who=" + "x" * 200)
        check("overlong who rejected", status == 400 and len(forwarded) == before, f"status={status}")

        status, payload = http(base, "/api/sdk/contacts?who=filehelper")
        check("unexpected query rejected", status == 400 and len(forwarded) == before, f"status={status}")

        status, payload = http(base, "/api/sdk/contacts")
        check("contacts still works", status == 200 and forwarded[-1]["path"] == "/api/sdk/contacts",
              f"status={status}")

        status, payload = http(base, "/api/sdk/nope")
        check("unknown route still 404", status == 404, f"status={status}")
    finally:
        try:
            server.stop()
        except Exception:
            pass

    result = {"ok": all(item["ok"] for item in checks), "checks": len(checks),
              "realWechatActions": False, "deviceRequired": False, "items": checks}
    (OUT / "console-chat-route-result.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    for item in checks:
        print(("PASS  " if item["ok"] else "FAIL  ") + item["name"] + ("" if item["ok"] else "  <- " + item["detail"]))
    print(json.dumps({"ok": result["ok"], "checks": result["checks"]}, ensure_ascii=False))
    return 0 if result["ok"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
