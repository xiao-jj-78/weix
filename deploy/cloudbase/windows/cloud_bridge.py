"""Windows outbound bridge. Standard library only; never opens an inbound port."""
from __future__ import annotations

import argparse
import base64
import ctypes
import datetime as dt
import getpass
import hashlib
import json
import os
import re
import signal
import sqlite3
import threading
import time
from concurrent.futures import ThreadPoolExecutor
from contextlib import contextmanager
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.parse import parse_qsl, urlencode, urlsplit
from urllib.request import HTTPRedirectHandler, ProxyHandler, Request, build_opener

MAX_BYTES = 1024 * 1024
DEFAULT_IDLE_POLL_SECONDS = 3
TASKS = frozenset(("phone_like", "switch_account", "pc_like", "multi_like", "replica_like"))
PLUGINS = frozenset(("sdk-health", "session-snapshot", "sdk-session", "sdk-contacts"))
READ_PATHS = frozenset(("/api/sdk/clients", "/api/status", "/api/config", "/api/devices", "/api/sdk/status",
    "/api/sdk/plugins", "/api/sdk/contacts", "/api/sdk/ai/config", "/api/sdk/ai/status",
    "/api/sdk/ai/profiles/export"))
WRITE_PATHS = frozenset(("/api/sdk/clients/open", "/api/sdk/clients/focus", "/api/sdk/clients/alias", "/api/sdk/clients/verify", "/api/config", "/api/task/start", "/api/task/stop", "/api/sdk/start",
    "/api/sdk/stop", "/api/sdk/login", "/api/sdk/plugins/run", "/api/sdk/ai/config",
    "/api/sdk/ai/preview", "/api/sdk/ai/start", "/api/sdk/ai/stop", "/api/sdk/ai/profiles/import"))


def state_dir():
    root = os.environ.get("LOCALAPPDATA")
    if not root:
        raise RuntimeError("LOCALAPPDATA is unavailable")
    return Path(root) / "WeChatLikeConsole" / "CloudBridge"


class _Blob(ctypes.Structure):
    _fields_ = [("cbData", ctypes.c_ulong), ("pbData", ctypes.POINTER(ctypes.c_ubyte))]


class Dpapi:
    def _crypt(self, data, decrypt=False):
        if os.name != "nt":
            raise RuntimeError("DPAPI requires Windows")
        buffer = ctypes.create_string_buffer(data)
        source = _Blob(len(data), ctypes.cast(buffer, ctypes.POINTER(ctypes.c_ubyte)))
        result = _Blob()
        crypt = ctypes.windll.crypt32
        if decrypt:
            ok = crypt.CryptUnprotectData(ctypes.byref(source), None, None, None, None, 1, ctypes.byref(result))
        else:
            ok = crypt.CryptProtectData(ctypes.byref(source), "WeChat Cloud Bridge", None, None, None, 1, ctypes.byref(result))
        if not ok:
            raise ctypes.WinError()
        try:
            return ctypes.string_at(result.pbData, result.cbData)
        finally:
            ctypes.windll.kernel32.LocalFree(result.pbData)

    def protect(self, data):
        return base64.b64encode(self._crypt(data)).decode("ascii")

    def unprotect(self, value):
        return self._crypt(base64.b64decode(value, validate=True), True)


def validate_cloud_url(value, allow_test_http=False):
    if not isinstance(value, str) or len(value) > 2048 or any(c in value for c in ("\\", "%", "\r", "\n")):
        raise ValueError("Cloud URL is invalid")
    parsed = urlsplit(value)
    local_test = allow_test_http and parsed.scheme == "http" and parsed.hostname == "127.0.0.1"
    if (parsed.scheme != "https" and not local_test) or not parsed.hostname or parsed.username or parsed.password:
        raise ValueError("Cloud URL must use HTTPS without embedded credentials")
    if parsed.query or parsed.fragment or (not local_test and parsed.port not in (None, 443)):
        raise ValueError("Cloud URL must not contain a query, fragment or custom port")
    if any(part in (".", "..") for part in parsed.path.split("/")):
        raise ValueError("Cloud URL path is invalid")
    return value.rstrip("/")


def atomic_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    with temporary.open("w", encoding="utf-8", newline="\n") as stream:
        json.dump(value, stream, ensure_ascii=False, indent=2)
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(temporary, path)


def save_config(cloud_url, agent_token, browser_token, path=None):
    """Call in-process with secrets; do not pass secrets through command-line args."""
    cloud_url = validate_cloud_url(cloud_url)
    for token in (agent_token, browser_token):
        if not isinstance(token, str) or not 32 <= len(token) <= 512 or re.search(r"\s", token):
            raise ValueError("Tokens must contain 32-512 non-whitespace characters")
    if agent_token == browser_token:
        raise ValueError("Agent and browser tokens must be distinct")
    protect = Dpapi()
    config = {"schemaVersion": 1, "cloudUrl": cloud_url,
        "environmentId": "jou-d4gq7a3cqf51e6caa", "agentId": "main-windows",
        "localBaseUrl": "http://127.0.0.1:8765",
        "agentTokenProtected": protect.protect(agent_token.encode("utf-8")),
        "browserTokenProtected": protect.protect(browser_token.encode("utf-8")),
        "agentTokenSha256": hashlib.sha256(agent_token.encode()).hexdigest(),
        "browserTokenSha256": hashlib.sha256(browser_token.encode()).hexdigest()}
    atomic_json(path or state_dir() / "config.json", config)
    return {key: config[key] for key in ("schemaVersion", "cloudUrl", "environmentId", "agentId")}


def load_config(path=None):
    config = json.loads(Path(path or state_dir() / "config.json").read_text(encoding="utf-8-sig"))
    if config.get("schemaVersion") != 1 or config.get("agentId") != "main-windows":
        raise ValueError("Unsupported bridge configuration")
    config["cloudUrl"] = validate_cloud_url(config.get("cloudUrl"))
    if config.get("localBaseUrl") != "http://127.0.0.1:8765":
        raise ValueError("Local target must be http://127.0.0.1:8765")
    config["agentToken"] = Dpapi().unprotect(config["agentTokenProtected"]).decode("utf-8")
    config["browserToken"] = Dpapi().unprotect(config["browserTokenProtected"]).decode("utf-8")
    for role in ("agent", "browser"):
        token = config[role + "Token"]
        if not 32 <= len(token) <= 512 or re.search(r"\s", token):
            raise ValueError("Invalid protected token")
        if hashlib.sha256(token.encode()).hexdigest() != config[role + "TokenSha256"]:
            raise ValueError("Protected token checksum mismatch")
    return config


def expires_at(value):
    if isinstance(value, bool):
        raise ValueError("Invalid command expiry")
    if isinstance(value, (int, float)):
        result = float(value)
        return result / 1000 if result > 10**11 else result
    if isinstance(value, str):
        parsed = dt.datetime.fromisoformat(value.replace("Z", "+00:00"))
        if parsed.tzinfo is None:
            raise ValueError("Command expiry must have a timezone")
        return parsed.timestamp()
    raise ValueError("Invalid command expiry")


def validate_command(command, now=None):
    if not isinstance(command, dict):
        raise ValueError("Command must be an object")
    if not isinstance(command.get("id"), str) or not re.fullmatch(r"[A-Za-z0-9_-]{1,128}", command["id"]):
        raise ValueError("Invalid command ID")
    expiry = expires_at(command.get("expiresAt"))
    current = time.time() if now is None else now
    if not current < expiry <= current + 600:
        raise ValueError("Command has expired or its expiry is too far in the future")
    method, path = command.get("method"), command.get("path")
    if method not in ("GET", "POST") or not isinstance(path, str) or len(path) > 1024:
        raise ValueError("Invalid command method/path")
    if any(ch in path for ch in ("%", "\\", "#", "\r", "\n")) or not path.startswith("/api/"):
        raise ValueError("Command path is not allowed")
    parsed = urlsplit(path)
    if parsed.scheme or parsed.netloc or parsed.fragment:
        raise ValueError("Command target is not local")
    if method == "GET" and parsed.path == "/api/logs":
        pairs = parse_qsl(parsed.query, keep_blank_values=True, strict_parsing=True)
        query = dict(pairs)
        if len(pairs) != 2 or set(query) != {"task", "n"} or query["task"] not in TASKS:
            raise ValueError("Invalid log query")
        if not re.fullmatch(r"[0-9]{1,3}", query["n"]) or not 1 <= int(query["n"]) <= 500:
            raise ValueError("Invalid log count")
        path = "/api/logs?" + urlencode({"task": query["task"], "n": int(query["n"])})
    else:
        if parsed.query or (method == "GET" and path not in READ_PATHS) or (method == "POST" and path not in WRITE_PATHS):
            raise ValueError("Command route is not allowed")
    body = command.get("body")
    if method == "GET":
        if body not in (None, {}):
            raise ValueError("GET commands cannot carry a body")
        body = None
    else:
        if not isinstance(body, dict):
            raise ValueError("POST body must be an object")
        if path.startswith("/api/sdk/clients/"):
            fields = {"instanceId", "alias"} if path.endswith("/alias") else {"instanceId"} if path.endswith("/focus") else set()
            if set(body) != fields or ("instanceId" in fields and (not isinstance(body.get("instanceId"), str) or not re.fullmatch(r"[1-9][0-9]{0,9}:[1-9][0-9]{10,18}", body["instanceId"]))):
                raise ValueError("Invalid WeChat window selection")
            if "alias" in fields and (not isinstance(body["alias"], str) or len(body["alias"]) > 60 or any(ord(c) < 32 or 127 <= ord(c) <= 159 for c in body["alias"])):
                raise ValueError("Invalid window alias")
        if path.startswith("/api/task/"):
            if set(body) - {"task", "args"} or body.get("task") not in TASKS or body.get("args", []) != []:
                raise ValueError("Task command only accepts a fixed task without additional arguments")
        if path == "/api/sdk/plugins/run" and (set(body) != {"id"} or body.get("id") not in PLUGINS):
            raise ValueError("Plugin command is not allowed")
        if path in {"/api/sdk/start", "/api/sdk/stop", "/api/sdk/login", "/api/sdk/ai/start", "/api/sdk/ai/stop"} and body:
            raise ValueError("Route does not accept additional parameters")
        if len(json.dumps(body, ensure_ascii=False).encode()) > 65536:
            raise ValueError("Command body exceeds 64 KB")
    return method, path, body, expiry


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def request_json(opener, url, method="POST", body=None, token=None, timeout=30):
    data = None if body is None else json.dumps(body, ensure_ascii=False).encode("utf-8")
    headers = {"Accept": "application/json", "Content-Type": "application/json; charset=utf-8"}
    if token:
        headers["Authorization"] = "Bearer " + token
    request = Request(url, data=data, headers=headers, method=method)
    try:
        response = opener.open(request, timeout=timeout)
    except HTTPError as error:
        response = error
    with response:
        data = response.read(MAX_BYTES + 1)
        if len(data) > MAX_BYTES:
            raise ValueError("Response exceeds 1 MB")
        try:
            payload = json.loads(data.decode("utf-8"))
        except (ValueError, UnicodeError):
            payload = {"ok": False, "message": "Endpoint did not return valid JSON"}
        return response.status, payload


class Journal:
    """Commit the execution marker before sending any local request; never repeat it."""
    def __init__(self, path, protector=None):
        self.path, self.protector = str(path), protector or Dpapi()
        self.lock = threading.RLock()
        Path(path).parent.mkdir(parents=True, exist_ok=True)
        with self.connect() as db:
            db.execute("PRAGMA journal_mode=WAL")
            db.execute("CREATE TABLE IF NOT EXISTS commands (id TEXT PRIMARY KEY, digest TEXT NOT NULL, state TEXT NOT NULL, result TEXT, updated REAL NOT NULL)")
            # A crash after this durable marker means the local effect cannot be known safely.
            for (identifier,) in db.execute("SELECT id FROM commands WHERE state='running'").fetchall():
                result = {"status": 409, "body": {"ok": False, "state": "uncertain", "message": "Bridge restarted after execution began; command will not be repeated"}}
                self._store(db, identifier, result)

    @contextmanager
    def connect(self):
        db = sqlite3.connect(self.path, timeout=30)
        try:
            db.execute("PRAGMA synchronous=FULL")
            with db:
                yield db
        finally:
            db.close()

    def _store(self, db, identifier, result):
        protected = self.protector.protect(json.dumps(result, ensure_ascii=False).encode("utf-8"))
        db.execute("UPDATE commands SET state='completed',result=?,updated=? WHERE id=?", (protected, time.time(), identifier))

    def reserve(self, command):
        serialized = json.dumps({key: command.get(key) for key in ("method", "path", "body", "expiresAt")}, sort_keys=True, ensure_ascii=False)
        digest = hashlib.sha256(serialized.encode()).hexdigest()
        with self.lock, self.connect() as db:
            db.execute("BEGIN IMMEDIATE")
            row = db.execute("SELECT digest,state,result FROM commands WHERE id=?", (command["id"],)).fetchone()
            if row:
                if row[0] != digest:
                    return "conflict", None
                result = json.loads(self.protector.unprotect(row[2])) if row[2] else None
                return row[1], result
            db.execute("INSERT INTO commands VALUES(?,?,'running',NULL,?)", (command["id"], digest, time.time()))
            return "new", None

    def complete(self, identifier, result):
        with self.lock, self.connect() as db:
            self._store(db, identifier, result)

    def pending(self):
        with self.lock, self.connect() as db:
            rows = db.execute("SELECT id,result FROM commands WHERE state='completed' ORDER BY updated LIMIT 100").fetchall()
        return [(identifier, json.loads(self.protector.unprotect(result))) for identifier, result in rows]

    def acknowledge(self, identifier):
        with self.lock, self.connect() as db:
            # Keep the ID/digest forever as a replay tombstone; remove only encrypted response data.
            db.execute("UPDATE commands SET state='acknowledged',result=NULL,updated=? WHERE id=?", (time.time(), identifier))


class Bridge:
    def __init__(self, config, journal, stop=None, test_local_base=None):
        self.config, self.journal = config, journal
        self.stop = stop or threading.Event()
        self.write_lock = threading.Lock()
        self.local_base = test_local_base or "http://127.0.0.1:8765"
        # Neither loopback requests nor cloud credentials pass through a configured proxy.
        self.opener = build_opener(ProxyHandler({}), NoRedirect())
        self.upload_lock = threading.Lock()
        self.stop_file = None

    def stopped(self):
        if self.stop_file and self.stop_file.exists():
            self.stop.set()
        return self.stop.is_set()

    def cloud(self, suffix, body):
        return request_json(self.opener, self.config["cloudUrl"] + suffix, body=body,
            token=self.config["agentToken"], timeout=30)

    def upload(self, identifier, result, claim_token=None):
        payload = {"id": identifier, "agentId": self.config["agentId"], **result}
        if claim_token:
            payload["claimToken"] = claim_token
        try:
            code, response = self.cloud("/bridge/result", payload)
            if code == 200 and isinstance(response, dict) and response.get("ok"):
                self.journal.acknowledge(identifier)
                return True
        except (OSError, ValueError, URLError):
            pass
        return False

    def retry_results(self):
        if not self.upload_lock.acquire(blocking=False):
            return
        try:
            for identifier, result in self.journal.pending():
                if self.stopped() or not self.upload(identifier, result):
                    break
        finally:
            self.upload_lock.release()

    def execute(self, command):
        # Bad commands do not reach the localhost target under any circumstance.
        method, path, body, expiry = validate_command(command)
        state, previous = self.journal.reserve(command)
        if state != "new":
            if previous:
                self.upload(command["id"], previous, command.get("claimToken"))
            return state
        result = None
        try:
            if method == "POST":
                with self.write_lock:
                    if self.stopped() or time.time() >= expiry:
                        result = {"status": 409, "body": {"ok": False, "state": "not_executed", "message": "Command expired or bridge is stopping"}}
                    else:
                        result = self.local_request(method, path, body)
            elif self.stopped() or time.time() >= expiry:
                result = {"status": 409, "body": {"ok": False, "state": "not_executed", "message": "Command expired or bridge is stopping"}}
            else:
                result = self.local_request(method, path, body)
        except (OSError, ValueError, URLError):
            # Even a timeout can happen after a write was applied. Never retry localhost.
            result = {"status": 504, "body": {"ok": False, "state": "uncertain", "message": "Local request did not complete; inspect the workbench before issuing a new command"}}
        self.journal.complete(command["id"], result)
        self.upload(command["id"], result, command.get("claimToken"))
        return "executed"

    def local_request(self, method, path, body):
        code, payload = request_json(self.opener, self.local_base + path, method, body, timeout=115)
        if not isinstance(payload, dict) or not 200 <= code <= 599:
            return {"status": 502, "body": {"ok": False, "state": "uncertain", "message": "Local response is invalid; command will not be repeated"}}
        if len(json.dumps(payload, ensure_ascii=False).encode("utf-8")) > 512 * 1024:
            return {"status": 502, "body": {"ok": False, "state": "uncertain", "message": "Local response exceeds the cloud result limit; command will not be repeated"}}
        return {"status": code, "body": payload}

    def worker(self):
        while not self.stopped():
            try:
                self.retry_results()
                code, response = self.cloud("/bridge/poll", {"agentId": self.config["agentId"]})
                if code in (401, 403):
                    self.stop.set()
                    return
                if code != 200 or not isinstance(response, dict) or not response.get("ok"):
                    self.stop.wait(3)
                    continue
                command = response.get("command")
                if command is None:
                    self.stop.wait(DEFAULT_IDLE_POLL_SECONDS)
                    continue
                try:
                    self.execute(command)
                except (TypeError, ValueError, OverflowError):
                    # Report rejected commands when a syntactically safe ID is available.
                    if isinstance(command, dict) and isinstance(command.get("id"), str) and re.fullmatch(r"[A-Za-z0-9_-]{1,128}", command["id"]):
                        self.upload(command["id"], {"status": 400, "body": {"ok": False, "state": "not_executed", "message": "Rejected unsafe or expired command"}}, command.get("claimToken"))
            except (OSError, ValueError, URLError):
                self.stop.wait(3)

    def run(self):
        with ThreadPoolExecutor(max_workers=3, thread_name_prefix="cloud-bridge") as pool:
            futures = [pool.submit(self.worker) for _ in range(3)]
            while not self.stopped():
                if any(f.done() for f in futures):
                    self.stop.set()
                    break
                self.stop.wait(.5)
            for future in futures:
                future.result()


def copy_browser_token():
    """Interactive clipboard copy; no secret is ever printed or placed in argv."""
    import tkinter as tk
    from tkinter import ttk
    token = load_config()["browserToken"]
    root = tk.Tk()
    root.title("复制云端登录口令")
    root.geometry("420x170")
    root.resizable(False, False)
    root.clipboard_clear()
    root.clipboard_append(token)
    root.update()
    ttk.Label(root, text="已复制云端登录口令。\n请在云端工作台的登录框粘贴。\n窗口关闭或 60 秒后将清理本次剪贴板。", padding=20).pack()
    def close():
        try:
            if root.clipboard_get() == token:
                root.clipboard_clear()
                root.update()
        except tk.TclError:
            pass
        root.destroy()
    ttk.Button(root, text="已粘贴，清理并关闭", command=close).pack()
    root.protocol("WM_DELETE_WINDOW", close)
    root.after(60000, close)
    root.mainloop()


def run_agent():
    if os.name != "nt":
        raise RuntimeError("The bridge agent must run under the signed-in Windows user")
    kernel = ctypes.windll.kernel32
    kernel.CreateMutexW.restype = ctypes.c_void_p
    mutex = kernel.CreateMutexW(None, False, "Local\\WeChatLikeConsole.CloudBridge.main-windows")
    if not mutex or kernel.GetLastError() == 183:
        if mutex:
            kernel.CloseHandle(ctypes.c_void_p(mutex))
        raise RuntimeError("The bridge is already running in this Windows session")
    directory = state_dir()
    stop_file, runtime = directory / "stop.request", directory / "runtime.json"
    stop_file.unlink(missing_ok=True)
    try:
        bridge = Bridge(load_config(), Journal(directory / "journal.sqlite3"))
        bridge.stop_file = stop_file
        for name in ("SIGINT", "SIGTERM", "SIGBREAK"):
            if hasattr(signal, name):
                signal.signal(getattr(signal, name), lambda *_: bridge.stop.set())
        atomic_json(runtime, {"pid": os.getpid(), "startedAt": time.time(), "scriptPath": str(Path(__file__).resolve()), "state": "running"})
        bridge.run()
    finally:
        if runtime.exists():
            atomic_json(runtime, {"pid": os.getpid(), "scriptPath": str(Path(__file__).resolve()), "state": "stopped", "stoppedAt": time.time()})
        kernel.CloseHandle(ctypes.c_void_p(mutex))


def main():
    parser = argparse.ArgumentParser(description="Windows outbound CloudBase bridge")
    parser.add_argument("action", choices=("run", "init", "copy-browser-token", "check-config"))
    action = parser.parse_args().action
    try:
        if action == "init":
            url = input("Cloud HTTPS URL: ").strip()
            save_config(url, getpass.getpass("Agent token (hidden): "), getpass.getpass("Browser token (hidden): "))
            print("Encrypted configuration saved for this Windows user.")
        elif action == "copy-browser-token":
            copy_browser_token()
        elif action == "check-config":
            config = load_config()
            print(json.dumps({key: config[key] for key in ("schemaVersion", "cloudUrl", "environmentId", "agentId")}, ensure_ascii=False))
        else:
            run_agent()
    except Exception:
        # Exception details could include credentials from an upstream URL/body: do not echo them.
        print("Cloud bridge could not complete the operation. Check the non-secret configuration and Windows user session.")
        raise SystemExit(1)


if __name__ == "__main__":
    main()
