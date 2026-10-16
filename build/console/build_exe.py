# -*- coding: utf-8 -*-
"""PyInstaller 打包脚本：生成单文件 微信自动化控制台.exe。

用法（在 console/ 目录下）：
    <venv>/Scripts/python.exe build_exe.py
"""
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent

HIDDEN = [
    # 控制台自身
    "console_app",
    "console_app.config",
    "console_app.tasks",
    "console_app.gui",
    "console_app.remote_server",
    "console_app.sdk_bridge",
    "console_app.web_dashboard",
    "console_app.web_features",
    "console_app.web_clients",
    "console_app.ocr_shim",
    # 核心脚本依赖（脚本以数据文件运行，PyInstaller 不会自动分析其 import）
    "cv2",
    "numpy",
    "mss",
    "psutil",
    "pyautogui",
    "pywinauto",
    "pytweening",
    "pymsgbox",
    "pyperclip",
    "pyscreeze",
    "pygetwindow",
    "pyrect",
    "mouseinfo",
    "win32api",
    "win32con",
    "win32gui",
    "win32process",
    "win32com",
    "pythoncom",
    "comtypes",
    "comtypes.client",
    "PIL",
    "PIL.Image",
    # replica 引擎（wechatauto-replica，动态 import）
    "wechatauto",
    "uiautomation",
    "colorama",
    "cryptography",
    "cryptography.hazmat.primitives.ciphers",
    "zstandard",
    "imageio_ffmpeg",
]

COLLECT_ALL = [
    "wechatauto",          # 含 assets 数据文件与全部子模块
    "rapidocr_onnxruntime",  # 含 ONNX 模型
    "onnxruntime",
]


def main():
    cmd = [
        sys.executable, "-m", "PyInstaller",
        "--noconfirm", "--clean",
        "--onefile", "--windowed",
        "--name", "微信自动化控制台",
        "--icon", str(ROOT / "app.ico"),
        "--paths", str(ROOT),
        "--add-data", "%s;core" % (ROOT / "core"),
        "--add-data", "%s;engines" % (ROOT / "engines"),
        "--add-data", "%s;bin" % (ROOT / "bin"),
        "--add-data", "%s;wechatauto" % (ROOT / "wechatauto"),
    ]
    for mod in HIDDEN:
        cmd += ["--hidden-import", mod]
    for pkg in COLLECT_ALL:
        cmd += ["--collect-all", pkg]
    cmd += [str(ROOT / "main.py")]

    print(" ".join(cmd))
    rc = subprocess.call(cmd, cwd=str(ROOT))
    return rc


if __name__ == "__main__":
    raise SystemExit(main())
