# -*- coding: utf-8 -*-
"""任务调度器：以子进程方式驱动 core/ 与 engines/ 下的脚本。

- 冻结运行时：exe --worker <脚本> [参数...]
- 源码运行时：python -X utf8 -u <脚本> [参数...]
脚本只通过环境变量接收配置（见 config.worker_env），原有接口不改。
"""
from __future__ import annotations

import os
import json
import subprocess
import sys
import threading
import time
from collections import deque
from datetime import datetime
from pathlib import Path

from . import config


class TaskDef:
    def __init__(self, tid, name, script, desc, args=None, needs=None):
        self.id = tid
        self.name = name
        self.script = script  # 相对 bundle_root 的路径
        self.desc = desc
        self.args = args or []  # 固定附加参数
        self.needs = needs or []  # 前置条件提示（GUI 展示用）


TASK_DEFS = [
    TaskDef(
        "phone_like",
        "手机朋友圈点赞",
        "core/comment_like.py",
        "ADB 驱动安卓手机/模拟器微信，自动浏览朋友圈并点赞（最多 15 条/轮）",
        needs=["手机_usb"],
    ),
    TaskDef(
        "switch_account",
        "切换微信账号",
        "core/switch_wechat_account.py",
        "识别当前微信号并切换到下一个未访问账号（需 OCR，已内置）",
        needs=["phone_usb"],
    ),
    TaskDef(
        "pc_like",
        "电脑朋友圈点赞",
        "core/wechat_pc_moments_image_test.py",
        "驱动电脑版微信自动点赞，后台点击不占用鼠标，每轮 30 个后休息",
        needs=["pc_wechat"],
    ),
    TaskDef(
        "multi_like",
        "电脑多开点赞",
        "core/launch_4_wechat.py",
        "自动排列多个微信窗口并同时点赞，异常自动重启",
        needs=["pc_wechat"],
    ),
    TaskDef(
        "replica_like",
        "定点点赞（库引擎）",
        "engines/replica_like.py",
        "按作者昵称/关键词定位朋友圈并点赞，支持持续监听模式",
        needs=["pc_wechat"],
    ),
]

TASK_MAP = {t.id: t for t in TASK_DEFS}


def _script_path(task: TaskDef) -> Path:
    root = config.bundle_root()
    return (root / task.script).resolve()


class _Proc:
    def __init__(self, task: TaskDef, proc: subprocess.Popen):
        self.task = task
        self.proc = proc
        self.started_at = time.time()
        self.ended_at = None
        self.lines = deque(maxlen=600)
        self.last_line = ""
        self.reader = None


class TaskRunner:
    def __init__(self, cfg_getter):
        self._cfg_getter = cfg_getter
        self._procs = {}
        self._lock = threading.RLock()
        self._listeners = []

    # ---------- 监听 ----------
    def add_listener(self, fn):
        self._listeners.append(fn)

    def _emit(self, event):
        for fn in list(self._listeners):
            try:
                fn(event)
            except Exception:
                pass

    # ---------- 查询 ----------
    def is_running(self, tid) -> bool:
        with self._lock:
            p = self._procs.get(tid)
            return bool(p and p.proc.poll() is None)

    def running_ids(self):
        with self._lock:
            return [tid for tid in self._procs if self.is_running(tid)]

    def status(self):
        with self._lock:
            return self._status_unlocked()

    def _status_unlocked(self):
        out = []
        for t in TASK_DEFS:
            p = self._procs.get(t.id)
            running = bool(p and p.proc.poll() is None)
            if p and not running and p.ended_at is None:
                p.ended_at = time.time()
            out.append(
                {
                    "id": t.id,
                    "name": t.name,
                    "desc": t.desc,
                    "running": running,
                    "pid": p.proc.pid if p else None,
                    "started_at": datetime.fromtimestamp(p.started_at).isoformat(
                        timespec="seconds"
                    )
                    if p
                    else None,
                    "elapsed": int((p.ended_at or time.time()) - p.started_at) if p else 0,
                    "exit_code": p.proc.returncode if p and p.proc.poll() is not None else None,
                    "last_line": p.last_line if p else "",
                }
            )
        return out

    def log_lines(self, tid, n=200):
        with self._lock:
            p = self._procs.get(tid)
            if not p or n <= 0:
                return []
            return list(p.lines)[-min(n, 600):]

    # ---------- 控制 ----------
    def start(self, tid, extra_args=None):
        # Serialize the check and process creation across GUI and HTTP requests.
        with self._lock:
            return self._start_unlocked(tid, extra_args)

    def _start_unlocked(self, tid, extra_args=None):
        t = TASK_MAP.get(tid)
        if not t:
            return False, "未知任务: %s" % tid
        if self.is_running(tid):
            return False, "任务已在运行"
        script = _script_path(t)
        if not script.exists():
            return False, "脚本不存在: %s" % script
        try:
            cfg = config.validate_config(self._cfg_getter())
        except config.ConfigError as exc:
            return False, "配置无效: %s" % exc
        if extra_args is None:
            extra_args = []
        if (not isinstance(extra_args, list) or len(extra_args) > 64 or
                any(not isinstance(a, str) or len(a) > 4096 or "\x00" in a for a in extra_args)):
            return False, "args 必须是文本数组（最多 64 项）"
        extra_args = list(extra_args)
        if tid == "replica_like":
            # These defaults must be shared by desktop, web and MAUI callers.
            # Explicit arguments retain precedence and the underlying script interface stays intact.
            def has(flag):
                return any(a == flag or a.startswith(flag + "=") for a in extra_args)

            defaults = []
            selected = cfg.get("replica_authors") or []
            if selected and not has("--author") and not has("--authors-json"):
                defaults += ["--authors-json", json.dumps(selected, ensure_ascii=False)]
            for key, flag in (("replica_author", "--author"), ("replica_keyword", "--keyword")):
                if key == "replica_author" and selected:
                    continue
                if cfg[key] and not has(flag):
                    defaults += [flag, cfg[key]]
            if not has("--author") and not has("--authors-json") and not has("--keyword") and not defaults:
                return False, "请先在设置中填写定点点赞的作者或关键词"
            if cfg["replica_watch"] and not has("--watch"):
                defaults.append("--watch")
            if not has("--interval"):
                defaults += ["--interval", str(cfg["replica_interval"] * 60)]
            extra_args = defaults + extra_args
        if config.is_frozen():
            cmd = [sys.executable, "--worker", str(script)] + list(t.args) + list(extra_args or [])
        else:
            # Use the same dispatcher as packaged builds so OCR and vendored imports agree.
            cmd = [sys.executable, "-X", "utf8", "-u", str(config.bundle_root() / "main.py"),
                   "--worker", str(script)] + list(t.args) + list(
                extra_args or []
            )
        env = config.worker_env(cfg)
        try:
            creationflags = 0
            if os.name == "nt":
                creationflags = getattr(subprocess, "CREATE_NO_WINDOW", 0)
            proc = subprocess.Popen(
                cmd,
                cwd=str(config.bundle_root()),
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,
                stdin=subprocess.DEVNULL,
                env=env,
                encoding="utf-8",
                errors="replace",
                bufsize=1,
                creationflags=creationflags,
            )
        except Exception as exc:
            return False, "启动失败: %s" % exc
        p = _Proc(t, proc)
        self._procs[tid] = p
        p.reader = threading.Thread(target=self._read_loop, args=(p,), daemon=True)
        p.reader.start()
        self._emit({"type": "started", "task": tid})
        return True, "已启动 PID=%d" % proc.pid

    def _read_loop(self, p: _Proc):
        try:
            for line in p.proc.stdout:
                line = line.rstrip("\n").rstrip("\r")
                if not line:
                    continue
                with self._lock:
                    p.lines.append(line)
                    p.last_line = line
                self._emit({"type": "log", "task": p.task.id, "line": line})
        except Exception:
            pass
        finally:
            p.proc.wait()
            if p.proc.stdout:
                p.proc.stdout.close()
            with self._lock:
                p.ended_at = time.time()
            self._emit({"type": "exited", "task": p.task.id, "code": p.proc.returncode})

    def stop(self, tid):
        with self._lock:
            p = self._procs.get(tid)
        if not p or p.proc.poll() is not None:
            return False, "任务未在运行"
        try:
            self._kill_tree(p.proc.pid)
        except Exception as exc:
            return False, "停止失败: %s" % exc
        return True, "已发送停止指令"

    def stop_all(self):
        with self._lock:
            ids = list(self._procs.keys())
        for tid in ids:
            if self.is_running(tid):
                self.stop(tid)

    @staticmethod
    def _kill_tree(pid: int):
        """结束进程及其子进程（多开点赞会派生 worker 子进程）。"""
        try:
            import psutil

            parent = psutil.Process(pid)
            children = parent.children(recursive=True)
            for c in children:
                try:
                    c.kill()
                except Exception:
                    pass
            try:
                parent.kill()
            except Exception:
                pass
        except Exception:
            if os.name == "nt":
                subprocess.run(
                    ["taskkill", "/T", "/F", "/PID", str(pid)],
                    capture_output=True,
                    creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
                )
            else:
                os.kill(pid, 15)

    # ---------- 设备检测 ----------
    def adb_devices(self):
        cfg = self._cfg_getter()
        adb = config.adb_exe(cfg)
        port = str(cfg.get("adb_port", "5038"))
        env = os.environ.copy()
        try:
            out = subprocess.run(
                [adb, "-P", port, "devices", "-l"],
                capture_output=True,
                text=True,
                timeout=15,
                env=env,
                creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0) if os.name == "nt" else 0,
            )
            text = (out.stdout or "") + (out.stderr or "")
        except Exception as exc:
            return [], "adb 执行失败: %s" % exc
        devices = []
        for line in text.splitlines():
            line = line.strip()
            if not line or line.startswith("List of"):
                continue
            parts = line.split()
            if len(parts) >= 2 and parts[1] == "device":
                devices.append(parts[0])
        return devices, text.strip()
