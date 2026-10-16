# -*- coding: utf-8 -*-
"""手机遥控 HTTP 服务（局域网）。

API 契约（保持不变）：
  GET  /api/ping            -> {"ok": true}
  GET  /api/status          -> {ok, app, version, uptime, remote, tasks, config}
  POST /api/task/start      body {"task": "<id>", "args": [...]}  -> {ok, message}
  POST /api/task/stop       body {"task": "<id>"}                  -> {ok, message}
  GET  /api/logs?task=&n=   -> {ok, task, lines: [...]}
  GET  /api/devices         -> {ok, devices: [...], raw: "..."}
  GET  /api/config          -> {ok, config}
  POST /api/config          body {…} 保存配置                      -> {ok}
  GET  /                     -> 手机网页遥控界面（无需装 APK 也能用）
"""
from __future__ import annotations

import json
import os
import ipaddress
import threading
import time
from copy import deepcopy
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse, parse_qs

from . import config
from .sdk_bridge import SdkBridge

from .web_dashboard import MOBILE_PAGE


class _State:
    def __init__(self):
        self.started_at = time.time()
        self.port = 8765
        self.bind_host = "0.0.0.0"


class RemoteServer:
    def __init__(self, runner, cfg_getter, cfg_setter, allow_shutdown=False):
        self.runner = runner
        self.cfg_getter = cfg_getter
        self.cfg_setter = cfg_setter
        self.httpd = None
        self.thread = None
        self.state = _State()
        self.sdk = SdkBridge()
        self._config_lock = threading.RLock()
        self.allow_shutdown = allow_shutdown
        self.shutdown_requested = threading.Event()

    # ---------- 生命周期 ----------
    def is_running(self):
        return bool(self.httpd)

    def start(self, port=None):
        if self.httpd:
            return False, "遥控服务已在运行"
        cfg = self.cfg_getter()
        try:
            port = int(cfg.get("remote_port", 8765) if port is None else port)
            if not 0 <= port <= 65535:
                raise ValueError()
        except (TypeError, ValueError):
            return False, "遥控端口必须是 1–65535 的整数"
        runner = self.runner
        server = self

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *args):
                pass

            def _json(self, obj, code=200):
                body = json.dumps(obj, ensure_ascii=False).encode("utf-8")
                self.send_response(code)
                self.send_header("Content-Type", "application/json; charset=utf-8")
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)

            def _html(self, text):
                body = text.encode("utf-8")
                self.send_response(200)
                self.send_header("Content-Type", "text/html; charset=utf-8")
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)

            def _body(self):
                if self.headers.get("Transfer-Encoding"):
                    raise ValueError("请求必须提供 Content-Length；不支持流式正文")
                try:
                    length = int(self.headers.get("Content-Length") or 0)
                except (ValueError, TypeError):
                    raise ValueError("Content-Length 无效") from None
                if length < 0 or length > 65536:
                    raise ValueError("请求内容不得超过 64 KB")
                if not length:
                    return {}
                try:
                    body = json.loads(self.rfile.read(length).decode("utf-8"))
                except (ValueError, UnicodeError):
                    raise ValueError("请求内容必须是有效 JSON") from None
                if not isinstance(body, dict):
                    raise ValueError("请求内容必须是 JSON 对象")
                return body

            def do_GET(self):
                try:
                    u = urlparse(self.path)
                    q = parse_qs(u.query)
                    if u.path == "/api/ping":
                        return self._json({"ok": True})
                    if u.path == "/api/status":
                        with server._config_lock:
                            cfg = deepcopy(server.cfg_getter())
                        return self._json(
                            {
                                "ok": True,
                                "app": config.APP_NAME,
                                "version": config.APP_VERSION,
                                "uptime": int(time.time() - server.state.started_at),
                                "remote": {
                                    "running": True,
                                    "port": server.state.port,
                                    "bind_host": server.state.bind_host,
                                    "lan_ips": config.lan_ips(),
                                    "pid": os.getpid(),
                                    "parent_pid": os.getppid(),
                                    "shutdown_available": server.allow_shutdown,
                                },
                                "tasks": server.runner.status(),
                                "config": cfg,
                            }
                        )
                    if u.path == "/api/logs":
                        tid = (q.get("task") or [""])[0]
                        n = int((q.get("n") or ["200"])[0])
                        if not 0 <= n <= 600:
                            raise ValueError("日志条数 n 必须是 0–600 的整数")
                        return self._json({"ok": True, "task": tid, "lines": server.runner.log_lines(tid, n)})
                    if u.path == "/api/devices":
                        devices, raw = server.runner.adb_devices()
                        return self._json({"ok": True, "devices": devices, "raw": raw})
                    if u.path == "/api/config":
                        with server._config_lock:
                            return self._json({"ok": True, "config": deepcopy(server.cfg_getter())})
                    if u.path == "/api/sdk/status":
                        return self._json(server.sdk.status())
                    if ("GET", u.path) in SdkBridge.ROUTES:
                        payload, code = server.sdk.proxy("GET", u.path, query=u.query)
                        return self._json(payload, code)
                    if u.path in ("/", "/index.html", "/mobile"):
                        return self._html(MOBILE_PAGE)
                    return self._json({"ok": False, "message": "not found"}, 404)
                except (ValueError, TypeError) as exc:
                    return self._json({"ok": False, "message": str(exc)}, 400)
                except Exception as exc:
                    return self._json({"ok": False, "message": str(exc)}, 500)

            def do_POST(self):
                try:
                    origin = self.headers.get("Origin")
                    if origin and urlparse(origin).netloc.lower() != self.headers.get("Host", "").lower():
                        return self._json({"ok": False, "message": "不接受来自其他网站的控制请求"}, 403)
                    u = urlparse(self.path)
                    body = self._body()
                    if u.path == "/api/service/stop":
                        if body:
                            raise ValueError("该接口不接受额外参数")
                        if not server.allow_shutdown:
                            return self._json({"ok": False, "message": "请从桌面控制台窗口退出服务"}, 409)
                        self._json({"ok": True, "message": "工作台正在安全停止"})
                        server.shutdown_requested.set()
                        return
                    if u.path in ("/api/sdk/start", "/api/sdk/stop"):
                        if body:
                            raise ValueError("该接口不接受额外参数")
                        payload, code = server.sdk.start() if u.path.endswith("/start") else server.sdk.stop()
                        return self._json(payload, code)
                    if ("POST", u.path) in SdkBridge.ROUTES:
                        payload, code = server.sdk.proxy("POST", u.path, body)
                        return self._json(payload, code)
                    if u.path == "/api/task/start":
                        if not isinstance(body.get("task"), str):
                            raise ValueError("task 必须是任务 id")
                        ok, msg = server.runner.start(body.get("task", ""), body.get("args", []))
                        return self._json({"ok": ok, "message": msg}, 200 if ok else 400)
                    if u.path == "/api/task/stop":
                        if not isinstance(body.get("task"), str):
                            raise ValueError("task 必须是任务 id")
                        ok, msg = server.runner.stop(body.get("task", ""))
                        return self._json({"ok": ok, "message": msg}, 200 if ok else 400)
                    if u.path == "/api/config":
                        updates = config.validate_config(body, partial=True)
                        with server._config_lock:
                            cfg = deepcopy(server.cfg_getter())
                            cfg.update(updates)
                            cfg = config.validate_config(cfg)
                            server.cfg_setter(cfg)
                        return self._json({"ok": True})
                    return self._json({"ok": False, "message": "not found"}, 404)
                except (ValueError, TypeError) as exc:
                    return self._json({"ok": False, "message": str(exc)}, 400)
                except Exception as exc:
                    return self._json({"ok": False, "message": str(exc)}, 500)

        try:
            bind_host = os.environ.get("WLC_BIND_HOST", "0.0.0.0").strip()
            address = ipaddress.ip_address(bind_host)
            if address.version != 4 or address.is_multicast:
                raise ValueError("WLC_BIND_HOST 必须是本机有效 IPv4 地址")
            self.httpd = ThreadingHTTPServer((bind_host, port), Handler)
            self.state.bind_host = bind_host
        except Exception as exc:
            self.httpd = None
            return False, "端口 %d 被占用或无法监听: %s" % (port, exc)
        self.httpd.daemon_threads = True
        self.state.port = self.httpd.server_address[1]
        self.state.started_at = time.time()
        self.thread = threading.Thread(target=self.httpd.serve_forever, daemon=True)
        self.thread.start()
        return True, "遥控服务已启动: http://<本机IP>:%d" % self.state.port

    def stop(self):
        if not self.httpd:
            return False, "遥控服务未运行"
        try:
            self.httpd.shutdown()
            self.httpd.server_close()
        except Exception:
            pass
        self.httpd = None
        self.thread = None
        self.sdk.close()
        return True, "遥控服务已停止"
