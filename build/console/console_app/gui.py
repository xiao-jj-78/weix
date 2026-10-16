# -*- coding: utf-8 -*-
"""Tkinter 图形界面（电脑版控制台）。"""
from __future__ import annotations

import queue
import threading
import time
import tkinter as tk
from tkinter import ttk
from pathlib import Path

from . import config
from .tasks import TASK_DEFS, TaskRunner
from .remote_server import RemoteServer


class ConsoleGUI:
    def __init__(self, root: tk.Tk):
        self.root = root
        self.cfg = config.load_config()
        self.runner = TaskRunner(lambda: self.cfg)
        self.remote = RemoteServer(self.runner, lambda: self.cfg, self._save_cfg)
        self.events = queue.Queue()
        self.runner.add_listener(self._on_event)
        self.selected_task = TASK_DEFS[0].id
        self._build()

        self.root.protocol("WM_DELETE_WINDOW", self._on_close)
        self._refresh()
        if self.cfg.get("remote_autostart"):
            self._toggle_remote()
        if self.cfg.get("adb_dir") == "":
            self._auto_detect_adb()

    # ---------------- UI ----------------
    def _build(self):
        root = self.root
        root.title("%s v%s" % (config.APP_NAME, config.APP_VERSION))
        root.geometry("1180x820")
        root.minsize(1040, 760)
        root.configure(background="#F3F6F5")

        style = ttk.Style()
        try:
            style.theme_use("clam")
        except Exception:
            pass
        style.configure(".", font=("Microsoft YaHei UI", 10), background="#F3F6F5", foreground="#132C36")
        style.configure("TFrame", background="#F3F6F5")
        style.configure("TLabel", background="#F3F6F5")
        style.configure("TButton", padding=(12, 8), background="#FFFFFF", bordercolor="#DCE5E2")
        style.map("TButton", background=[("active", "#E6F2ED")], foreground=[("disabled", "#8A9B98")])
        style.configure("Accent.TButton", background="#11856B", foreground="#FFFFFF")
        style.map("Accent.TButton", background=[("active", "#0D7059")], foreground=[("disabled", "#94C7B9")])
        style.configure("TLabelframe", padding=10, bordercolor="#DCE5E2")
        style.configure("TLabelframe.Label", font=("Microsoft YaHei UI", 10, "bold"), foreground="#132C36")
        style.configure("TNotebook", background="#F3F6F5", borderwidth=0)
        style.configure("TNotebook.Tab", padding=(18, 10), background="#E6EDE9")
        style.map("TNotebook.Tab", background=[("selected", "#FFFFFF")], foreground=[("selected", "#11856B")])

        top = ttk.Frame(root, padding=(20, 20, 20, 16))
        top.pack(fill="x")
        ttk.Label(top, text="微信工作台", font=("Microsoft YaHei UI", 20, "bold")).pack(
            side="left"
        )
        ttk.Label(
            top,
            text="v%s  /  本机任务与跨端连接" % config.APP_VERSION,
            foreground="#61757B",
        ).pack(side="left", padx=12)
        ttk.Button(top, text="打开可视化工作台  ↗", style="Accent.TButton", command=self._open_workbench).pack(side="right")

        paned = ttk.Panedwindow(root, orient="horizontal")
        paned.pack(fill="both", expand=True, padx=20, pady=(0, 12))

        # 左侧任务卡
        left = ttk.Frame(paned, width=385, padding=(0, 0, 18, 0))
        paned.add(left, weight=0)
        ttk.Label(left, text="任务", font=("Microsoft YaHei", 11, "bold")).pack(anchor="w", pady=(0, 6))
        self.task_cards = {}
        for t in TASK_DEFS:
            card = ttk.LabelFrame(left, text=t.name, padding=8)
            card.pack(fill="x", pady=4)
            ttk.Label(card, text=t.desc, wraplength=320, foreground="#555").pack(anchor="w")
            bar = ttk.Frame(card)
            bar.pack(fill="x", pady=(6, 0))
            dot = tk.Label(bar, text="●", fg="#B8C8C4", bg="#F3F6F5", font=("", 12))
            dot.pack(side="left")
            btn_start = ttk.Button(
                bar, text="开始", width=8, style="Accent.TButton", command=lambda tid=t.id: self._start_task(tid)
            )
            btn_start.pack(side="right")
            btn_stop = ttk.Button(
                bar, text="停止", width=8, command=lambda tid=t.id: self.runner.stop(tid)
            )
            btn_stop.pack(side="right", padx=4)
            self.task_cards[t.id] = {"dot": dot, "start": btn_start, "stop": btn_stop}

        dev_bar = ttk.Frame(left)
        dev_bar.pack(fill="x", pady=(8, 0))
        ttk.Button(dev_bar, text="检测手机设备", command=self._detect_devices).pack(side="left")
        self.dev_label = ttk.Label(dev_bar, text="未检测", foreground="#888")
        self.dev_label.pack(side="left", padx=8)

        # 右侧 notebook
        right = ttk.Frame(paned)
        paned.add(right, weight=1)
        nb = ttk.Notebook(right)
        nb.pack(fill="both", expand=True)

        # 日志页
        log_tab = ttk.Frame(nb)
        nb.add(log_tab, text="运行日志")
        sel = ttk.Frame(log_tab)
        sel.pack(fill="x", pady=4)
        ttk.Label(sel, text="任务:").pack(side="left")
        self.log_task_var = tk.StringVar(value=TASK_DEFS[0].id)
        cb = ttk.Combobox(
            sel,
            textvariable=self.log_task_var,
            values=[t.id for t in TASK_DEFS],
            state="readonly",
            width=18,
        )
        cb.pack(side="left", padx=4)
        cb.bind("<<ComboboxSelected>>", lambda e: self._render_log())
        self.log_text = tk.Text(
            log_tab, wrap="word", bg="#142E37", fg="#C1E4D9", font=("Consolas", 10), state="disabled", padx=16, pady=14, relief="flat"
        )
        self.log_text.pack(fill="both", expand=True)
        self.log_text.tag_config("err", foreground="#fca5a5")

        # 设置页
        set_tab = ttk.Frame(nb, padding=10)
        nb.add(set_tab, text="设置")
        form = ttk.Frame(set_tab)
        form.pack(fill="x")
        self.vars = {}
        rows = [
            ("每轮点赞数量", "round_like_count", "int"),
            ("每轮休息(分钟)", "round_rest_minutes", "int"),
            ("每日开始(时)", "run_start_hour", "int"),
            ("每日结束(时)", "run_end_hour", "int"),
            ("多开微信数量", "wechat_count", "int"),
            ("ADB 端口", "adb_port", "str"),
            ("ADB 目录(空=内置)", "adb_dir", "str"),
            ("Tesseract 路径(空=内置OCR)", "tesseract_path", "str"),
            ("定点点赞-作者", "replica_author", "str"),
            ("定点点赞-关键词", "replica_keyword", "str"),
            ("遥控端口", "remote_port", "int"),
        ]
        for i, (label, key, kind) in enumerate(rows):
            ttk.Label(form, text=label).grid(row=i, column=0, sticky="w", pady=3, padx=(0, 8))
            var = tk.StringVar(value=str(self.cfg.get(key, "")))
            self.vars[key] = (var, kind)
            ttk.Entry(form, textvariable=var, width=34).grid(row=i, column=1, sticky="w", pady=3)
        ttk.Button(set_tab, text="保存设置", command=self._save_settings).pack(anchor="w", pady=8)
        ttk.Label(
            set_tab,
            text="窗口坐标（多屏用户）可在 config.json 中修改 main_window / moments_window。",
            foreground="#888",
        ).pack(anchor="w")

        # 遥控页
        rm_tab = ttk.Frame(nb, padding=10)
        nb.add(rm_tab, text="手机遥控")
        self.rm_info = ttk.Label(rm_tab, text="遥控服务未启动", font=("Microsoft YaHei", 11))
        self.rm_info.pack(anchor="w")
        ttk.Button(rm_tab, text="启动/停止遥控服务", command=self._toggle_remote).pack(
            anchor="w", pady=8
        )
        ttk.Label(
            rm_tab,
            text="启动后，手机与电脑连同一 Wi-Fi，浏览器访问上面的地址即可遥控；\n"
            "也可以安装随附的 Android APK（微信点赞遥控器）获得原生体验。\n"
            "首次启动 Windows 防火墙会提示允许专用网络访问，请点击允许。",
            wraplength=560,
            justify="left",
            foreground="#555",
        ).pack(anchor="w", pady=6)
        self.rm_ips = ttk.Label(rm_tab, text="", foreground="#888")
        self.rm_ips.pack(anchor="w")

        # 状态栏
        status = ttk.Frame(root, padding=(10, 4))
        status.pack(fill="x", side="bottom")
        self.status_label = ttk.Label(status, text="就绪", foreground="#555")
        self.status_label.pack(side="left")
        ttk.Label(status, text="仅供学习研究使用", foreground="#bbb").pack(side="right")

    # ---------------- 逻辑 ----------------
    def _open_workbench(self):
        import webbrowser

        if not self.remote.is_running():
            ok, message = self.remote.start()
            self.status_label.config(text=message)
            if not ok:
                return
        webbrowser.open("http://127.0.0.1:%d/" % self.remote.state.port)
        self._render_remote()

    def _save_cfg(self, cfg):
        normalized = config.validate_config(cfg)
        config.save_config(normalized)
        self.cfg = normalized

    def _save_settings(self):
        try:
            candidate = dict(self.cfg)
            for key, (var, kind) in self.vars.items():
                v = var.get().strip()
                if kind == "int":
                    candidate[key] = int(v) if v else 0
                else:
                    candidate[key] = v
            self._save_cfg(candidate)
            self.status_label.config(text="设置已保存")
        except Exception as exc:
            self.status_label.config(text="保存失败: %s" % exc)

    def _auto_detect_adb(self):
        def work():
            devices, _raw = self.runner.adb_devices()
            self.events.put(("devices", devices))

        threading.Thread(target=work, daemon=True).start()

    def _detect_devices(self):
        self.dev_label.config(text="检测中…", foreground="#888")
        self._auto_detect_adb()

    def _start_task(self, tid):
        ok, msg = self.runner.start(tid)
        self.status_label.config(text=msg)
        if not ok:
            self._append_log(tid, "[控制台] " + msg, err=True)

    def _toggle_remote(self):
        if self.remote.is_running():
            ok, msg = self.remote.stop()
        else:
            ok, msg = self.remote.start()
        self.status_label.config(text=msg)
        self._render_remote()

    def _on_event(self, event):
        self.events.put(("event", event))

    def _append_log(self, tid, line, err=False):
        self.log_text.config(state="normal")
        tag = "err" if err else None
        if tid == self.log_task_var.get():
            self.log_text.insert("end", line + "\n", tag)
            self.log_text.see("end")
        self.log_text.config(state="disabled")

    def _render_log(self):
        self.log_text.config(state="normal")
        self.log_text.delete("1.0", "end")
        for line in self.runner.log_lines(self.log_task_var.get(), 300):
            self.log_text.insert("end", line + "\n")
        self.log_text.see("end")
        self.log_text.config(state="disabled")

    def _render_remote(self):
        if self.remote.is_running():
            ips = config.lan_ips()
            urls = "  ".join("http://%s:%d" % (ip, self.remote.state.port) for ip in ips)
            self.rm_info.config(text="遥控服务运行中 ｜ %s" % urls)
            self.rm_ips.config(text="手机浏览器直接打开上面任一地址即可")
        else:
            self.rm_info.config(text="遥控服务未启动")
            self.rm_ips.config(text="")

    def _render_tasks(self):
        running = set(self.runner.running_ids())
        for t in TASK_DEFS:
            card = self.task_cards[t.id]
            active = t.id in running
            card["dot"].config(fg="#11856B" if active else "#B8C8C4")
            card["start"].config(state="disabled" if active else "normal")
            card["stop"].config(state="normal" if active else "disabled")
        n = len(running)
        self.status_label.config(
            text="就绪 ｜ 运行中任务 %d 个 ｜ %s" % (n, time.strftime("%H:%M:%S"))
            if not n
            else "运行中任务 %d 个 ｜ %s" % (n, time.strftime("%H:%M:%S"))
        )

    def _refresh(self):
        try:
            while True:
                kind, payload = self.events.get_nowait()
                if kind == "event":
                    ev = payload
                    if ev.get("type") == "log":
                        self._append_log(ev["task"], ev["line"])
                    elif ev.get("type") == "exited":
                        self._append_log(ev["task"], "[控制台] 任务退出 code=%s" % ev.get("code"))
                elif kind == "devices":
                    if payload:
                        self.dev_label.config(text=", ".join(payload), foreground="#07c160")
                    else:
                        self.dev_label.config(text="未发现设备", foreground="#e64340")
        except queue.Empty:
            pass
        if self.log_task_var.get() and not self.runner.is_running(self.log_task_var.get()):
            pass
        self._render_tasks()
        self._render_remote()
        self.root.after(500, self._refresh)

    def _on_close(self):
        try:
            self.runner.stop_all()
            self.remote.stop()
        finally:
            self.root.destroy()


def run_gui():
    root = tk.Tk()
    ConsoleGUI(root)
    root.mainloop()
