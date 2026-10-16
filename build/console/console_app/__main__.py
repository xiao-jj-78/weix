# -*- coding: utf-8 -*-
"""入口分发器。

用法：
  正常运行（图形界面）      微信自动化控制台.exe
  worker 模式（内部使用）   微信自动化控制台.exe --worker <脚本路径> [参数...]
  自检模式（无界面验证）     微信自动化控制台.exe --selftest
"""
from __future__ import annotations

import runpy
import os
import sys
from pathlib import Path

from . import config


def _inject_ocr_shim(script_path: Path) -> bool:
    """未安装真实 Tesseract 时，为 OCR 类脚本注入 rapidocr 兼容层。

    被驱动脚本的 import 方式与调用参数完全不变（pytesseract 接口保持兼容）。
    """
    name = script_path.name.lower()
    if not any(k in name for k in ("switch", "broadcast")):
        return False
    real = Path(os.environ.get("WLC_TESSERACT") or config.tesseract_exe(config.load_config()))
    if real.exists():
        return False
    try:
        from . import ocr_shim

        sys.modules["pytesseract"] = ocr_shim  # type: ignore[assignment]
        return True
    except Exception:
        return False


def _run_worker(script: str, args: list) -> int:
    script_path = Path(script)
    if not script_path.is_absolute():
        script_path = config.bundle_root() / script
    script_path = script_path.resolve()
    if not script_path.exists():
        print("[控制台] worker 脚本不存在: %s" % script_path, file=sys.stderr)
        return 2
    parent = str(script_path.parent)
    root = str(config.bundle_root())
    if root not in sys.path:
        sys.path.insert(0, root)
    if parent not in sys.path:
        sys.path.insert(0, parent)
    _inject_ocr_shim(script_path)
    sys.argv = [str(script_path)] + list(args)
    runpy.run_path(str(script_path), run_name="__main__")
    return 0


def _selftest() -> int:
    """无界面自检：验证依赖、内置组件、遥控 API。"""
    ok = True

    def check(name, fn):
        nonlocal ok
        try:
            msg = fn()
            print("[PASS] %-28s %s" % (name, msg or ""))
        except Exception as exc:
            ok = False
            print("[FAIL] %-28s %s" % (name, exc))

    import importlib

    def _dep(mod):
        def f():
            importlib.import_module(mod)
            return "ok"

        return f

    for mod in ("cv2", "numpy", "mss", "psutil", "pyautogui", "pywinauto", "win32api", "PIL"):
        check("import " + mod, _dep(mod))

    def _adb():
        cfg = config.load_config()
        p = Path(config.adb_exe(cfg))
        if not p.exists():
            raise FileNotFoundError(str(p))
        return str(p)

    check("内置 adb", _adb)

    def _ocr():
        from . import ocr_shim
        import numpy as np

        img = np.zeros((80, 400, 3), dtype=np.uint8)
        img[:] = 255
        data = ocr_shim.image_to_data(img, output_type=ocr_shim.Output.DICT)
        if not isinstance(data, dict) or "text" not in data:
            raise RuntimeError("image_to_data 返回结构异常")
        return "keys=%d" % len(data)

    check("OCR 兼容层", _ocr)

    def _scripts():
        for t in ("comment_like.py", "switch_wechat_account.py", "wechat_pc_moments_image_test.py",
                  "launch_4_wechat.py", "wechat_like_worker_by_pid.py"):
            p = config.core_dir() / t
            if not p.exists():
                raise FileNotFoundError(str(p))
        p = config.engines_dir() / "replica_like.py"
        if not p.exists():
            raise FileNotFoundError(str(p))
        return "core=5 engines=1"

    check("内置脚本", _scripts)

    def _remote():
        from .tasks import TaskRunner
        from .remote_server import RemoteServer

        runner = TaskRunner(config.load_config)
        srv = RemoteServer(runner, config.load_config, lambda c: None)
        okstart, msg = srv.start(18765)
        if not okstart:
            raise RuntimeError(msg)
        try:
            import json
            import urllib.request

            with urllib.request.urlopen("http://127.0.0.1:18765/api/status", timeout=5) as r:
                data = json.loads(r.read().decode("utf-8"))
            if not data.get("ok") or len(data.get("tasks", [])) != 5:
                raise RuntimeError("status 内容异常: %s" % list(data))
        finally:
            srv.stop()
        return "api ok, tasks=%d" % len(data["tasks"])

    check("遥控 API", _remote)

    print("SELFTEST_%s" % ("PASS" if ok else "FAIL"))
    return 0 if ok else 1


def _guitest() -> int:
    """GUI 冒烟测试：真实创建主窗口并构造全部控件，结果写入文件（无控制台也能核验）。"""
    out = config.app_dir() / "guitest_result.txt"
    try:
        import tkinter as tk

        from .gui import ConsoleGUI

        root = tk.Tk()
        gui = ConsoleGUI(root)
        root.update_idletasks()
        root.update()
        info = "GUITEST_OK title=%r geometry=%s tasks=%d remote_running=%s tk=%s" % (
            root.title(),
            root.winfo_geometry(),
            len(gui.task_cards),
            gui.remote.is_running(),
            tk.TkVersion,
        )
        root.after(1500, root.destroy)
        root.mainloop()
        out.write_text(info, encoding="utf-8")
        print(info)
        return 0
    except Exception as exc:
        import traceback

        msg = "GUITEST_FAIL %s: %s\n%s" % (type(exc).__name__, exc, traceback.format_exc())
        out.write_text(msg, encoding="utf-8")
        print(msg)
        return 1


def _serve(port=None) -> int:
    """无界面模式：仅启动遥控服务，供手机端连接（也可用于自动化/自检）。"""
    from .remote_server import RemoteServer
    from .tasks import TaskRunner

    cfg = config.load_config()
    runner = TaskRunner(lambda: cfg)
    def update_cfg(updated):
        config.save_config(updated)
        cfg.clear()
        cfg.update(updated)

    server = RemoteServer(runner, lambda: cfg, update_cfg, allow_shutdown=True)
    ok, msg = server.start(port)
    print(msg)
    if not ok:
        return 1
    for ip in config.lan_ips():
        print("手机遥控地址: http://%s:%d" % (ip, server.state.port))
    print("按 Ctrl+C 退出。")
    try:
        while not server.shutdown_requested.wait(1):
            pass
    except KeyboardInterrupt:
        pass
    finally:
        runner.stop_all()
        server.stop()
    return 0


def main(argv=None) -> int:
    argv = list(sys.argv[1:] if argv is None else argv)
    if argv and argv[0] == "--worker":
        if len(argv) < 2:
            print("--worker 需要脚本路径", file=sys.stderr)
            return 2
        return _run_worker(argv[1], argv[2:])
    if argv and argv[0] in ("--selftest", "-t"):
        return _selftest()
    if argv and argv[0] == "--guitest":
        return _guitest()
    if argv and argv[0] == "--serve":
        return _serve(argv[1] if len(argv) > 1 else None)
    if argv and argv[0] in ("--version", "-v"):
        print("%s v%s" % (config.APP_NAME, config.APP_VERSION))
        return 0
    from .gui import run_gui

    run_gui()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
