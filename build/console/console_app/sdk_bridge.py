# -*- coding: utf-8 -*-
"""Own the optional C# SDK sidecar and proxy a fixed, local-only API surface."""
from __future__ import annotations

import json
import os
import re
import subprocess
import threading
import time
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.parse import unquote
from urllib.request import HTTPRedirectHandler, ProxyHandler, Request, build_opener

from . import config


class _NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


class SdkBridge:
    BASE_URL = "http://127.0.0.1:18766"
    ROUTES = {
        ("GET", "/api/sdk/clients"),
        ("POST", "/api/sdk/clients/open"),
        ("POST", "/api/sdk/clients/focus"),
        ("POST", "/api/sdk/clients/alias"),
        ("POST", "/api/sdk/clients/verify"),
        ("GET", "/api/sdk/status"),
        ("GET", "/api/sdk/plugins"),
        ("POST", "/api/sdk/login"),
        ("POST", "/api/sdk/plugins/run"),
        ("GET", "/api/sdk/contacts"),
        ("GET", "/api/sdk/chat"),
        ("GET", "/api/sdk/ai/config"),
        ("POST", "/api/sdk/ai/config"),
        ("GET", "/api/sdk/ai/status"),
        ("POST", "/api/sdk/ai/preview"),
        ("POST", "/api/sdk/ai/start"),
        ("POST", "/api/sdk/ai/stop"),
        ("POST", "/api/sdk/ai/profiles/import"),
        ("GET", "/api/sdk/ai/profiles/export"),
    }
    INTERNAL_ROUTES = {("POST", "/api/sdk/host/stop")}

    def __init__(self):
        self._lock = threading.RLock()
        self._process = None
        self._log = None
        # A system HTTP proxy must never receive requests to the local SDK host.
        self._opener = build_opener(ProxyHandler({}), _NoRedirect())

    @staticmethod
    def _host_candidates():
        roots = [config.app_dir() / "sdk", config.bundle_root() / "sdk",
                 config.bundle_root().parent / "sdk" / "publish"]
        for root in roots:
            for name in ("WeChatSdkHost.exe", "WeChatSdkHost.dll"):
                yield root / name

    def host_path(self):
        return next((p for p in self._host_candidates() if p.is_file()), None)

    def _managed(self):
        return bool(self._process is not None and self._process.poll() is None)

    def _request(self, method, path, body=None, timeout=4, query=""):
        if (method, path) not in self.ROUTES | self.INTERNAL_ROUTES:
            return {"ok": False, "message": "SDK 接口未开放"}, 404
        data = json.dumps(body or {}).encode("utf-8") if method == "POST" else None
        target = self.BASE_URL + path + (("?" + query) if query else "")
        req = Request(target, data=data, method=method,
                      headers={"Content-Type": "application/json; charset=utf-8"})
        try:
            try:
                response = self._opener.open(req, timeout=timeout)
            except HTTPError as exc:
                response = exc
            with response:
                raw = response.read(1024 * 1024 + 1)
                if len(raw) > 1024 * 1024:
                    raise ValueError("SDK 返回内容过大")
                payload = json.loads(raw.decode("utf-8"))
                if not isinstance(payload, dict) or not isinstance(payload.get("ok"), bool):
                    raise ValueError("SDK 返回格式无效")
                return payload, response.code
        except (OSError, URLError, ValueError, UnicodeError) as exc:
            return {"ok": False, "message": "C# SDK 服务未就绪: %s" % exc}, 503

    def status(self):
        payload, code = self._request("GET", "/api/sdk/status", timeout=1)
        if code == 200 and payload.get("ok") and isinstance(payload.get("session"), dict) and isinstance(payload.get("sdk"), dict):
            payload["running"] = True
            payload["managed"] = self._managed()
            payload["installed"] = self.host_path() is not None
            return payload
        message = payload.get("message", "C# SDK 服务未启动")
        return {
            "ok": True, "running": False, "installed": self.host_path() is not None,
            "managed": self._managed(), "message": message,
            "session": {"state": "offline", "message": message, "processes": [], "accounts": []},
            "plugins": [],
        }

    def start(self):
        with self._lock:
            if self.status()["running"]:
                return {"ok": True, "message": "C# SDK 服务已就绪", "running": True}, 200
            if self._managed():
                return {"ok": False, "message": "SDK 进程仍在启动，请稍后刷新或停止后重试"}, 409
            path = self.host_path()
            if path is None:
                return {"ok": False, "message": "未找到 C# SDK 服务。请保留交付目录中的 sdk 文件夹。"}, 503
            cmd = [str(path)] if path.suffix.lower() == ".exe" else ["dotnet", str(path)]
            cmd += ["--port", "18766"]
            try:
                self._close_log()
                self._log = (config.log_dir() / "sdk_host.log").open("ab")
                self._process = subprocess.Popen(
                    cmd, cwd=str(path.parent), stdin=subprocess.DEVNULL,
                    stdout=self._log, stderr=subprocess.STDOUT,
                    creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0) if os.name == "nt" else 0,
                )
            except (OSError, ValueError) as exc:
                self._close_log()
                return {"ok": False, "message": "启动 C# SDK 服务失败: %s" % exc}, 503
            deadline = time.monotonic() + 6
            while time.monotonic() < deadline and self._managed():
                if self.status()["running"]:
                    return {"ok": True, "running": True, "message": "C# SDK 服务已启动"}, 200
                time.sleep(0.2)
            if not self._managed():
                code = self._process.returncode
                self._close_log()
                return {"ok": False, "message": "SDK 服务已退出（代码 %s），请查看 logs/sdk_host.log" % code}, 503
            return {"ok": True, "running": False, "message": "SDK 进程已启动，正在初始化；请稍后刷新状态"}, 202

    def _close_log(self):
        if self._log is not None:
            self._log.close()
            self._log = None

    def stop(self):
        with self._lock:
            if not self._managed():
                self._close_log()
                if self.status()["running"]:
                    return {"ok": False, "message": "SDK 服务由其他程序启动，请在原程序中停止"}, 409
                return {"ok": True, "running": False, "message": "C# SDK 服务未运行"}, 200
            try:
                result, code = self._request("POST", "/api/sdk/host/stop", {}, timeout=3)
                if code == 200 and result.get("ok"):
                    try:
                        self._process.wait(timeout=15)
                    except subprocess.TimeoutExpired:
                        self._stop_helper_processes()
                else:
                    self._stop_helper_processes()
                self._process.wait(timeout=8)
            except (OSError, subprocess.TimeoutExpired) as exc:
                return {"ok": False, "message": "SDK 服务停止失败: %s" % exc}, 500
            finally:
                if not self._managed():
                    self._close_log()
            return {"ok": True, "running": False, "message": "C# SDK 服务已停止，微信客户端继续运行"}, 200

    def _stop_helper_processes(self):
        """Fallback stops SDK helpers only; official WeChat can be a descendant."""
        import psutil

        try:
            owned = psutil.Process(self._process.pid)
            for child in reversed(owned.children(recursive=True)):
                try:
                    name = child.name().lower()
                    if name == "wechatsdkhost.exe" or (name == "dotnet.exe" and
                            any("wechatsdkhost.dll" in value.lower() for value in child.cmdline())):
                        child.kill()
                except (psutil.NoSuchProcess, psutil.AccessDenied):
                    pass
            owned.kill()
        except psutil.NoSuchProcess:
            pass

    def close(self):
        # Only a process created by this console belongs to this console's lifetime.
        if self._managed():
            self.stop()
        self._close_log()

    def proxy(self, method, path, body=None, query=""):
        if (method, path) not in self.ROUTES:
            return {"ok": False, "message": "SDK 接口未开放"}, 404
        if method == "GET":
            if path == "/api/sdk/chat":
                # who 由用户从本人会话中选择；这里只做形状校验，拒绝路径与控制字符。
                if not query.startswith("who="):
                    return {"ok": False, "message": "读取聊天记录需要提供 who 会话标识。"}, 400
                who = unquote(query[4:])
                if (not who or len(who) > 96 or any(ord(ch) < 32 for ch in who)
                        or "/" in who or "\\" in who or ".." in who):
                    return {"ok": False, "message": "会话标识无效。"}, 400
            elif query:
                return {"ok": False, "message": "该接口不接受额外参数"}, 400
        if method == "POST":
            if not isinstance(body, dict):
                return {"ok": False, "message": "请求内容必须是 JSON 对象"}, 400
            if path == "/api/sdk/plugins/run":
                plugin_id = body.get("id")
                if set(body) != {"id"} or not isinstance(plugin_id, str) or not re.fullmatch(r"[A-Za-z0-9_.-]{1,80}", plugin_id):
                    return {"ok": False, "message": "请指定有效插件 id"}, 400
            elif path in ("/api/sdk/clients/focus", "/api/sdk/clients/alias"):
                fields = {"instanceId", "alias"} if path.endswith("/alias") else {"instanceId"}
                if set(body) != fields or not isinstance(body.get("instanceId"), str) or not re.fullmatch(r"[1-9][0-9]{0,9}:[1-9][0-9]{10,18}", body["instanceId"]):
                    return {"ok": False, "message": "微信窗口标识无效，请刷新列表。"}, 400
                if "alias" in fields and (not isinstance(body["alias"], str) or len(body["alias"]) > 60 or any(ord(ch) < 32 for ch in body["alias"])):
                    return {"ok": False, "message": "窗口备注最多 60 字，不能包含控制字符。"}, 400
            elif path.startswith("/api/sdk/ai/") and path not in ("/api/sdk/ai/start", "/api/sdk/ai/stop"):
                # Structured AI fields are validated by the fixed local C# endpoint.
                pass
            elif body:
                return {"ok": False, "message": "该接口不接受额外参数"}, 400
        timeout = 4
        if path in ("/api/sdk/contacts", "/api/sdk/chat", "/api/sdk/clients/verify") or (path == "/api/sdk/plugins/run" and (body or {}).get("id") in ("sdk-session", "sdk-contacts")):
            timeout = 105
        elif path in ("/api/sdk/ai/preview", "/api/sdk/ai/start"):
            timeout = 95
        return self._request(method, path, body, timeout=timeout, query=query)
