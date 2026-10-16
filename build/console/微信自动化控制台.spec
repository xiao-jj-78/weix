# -*- mode: python ; coding: utf-8 -*-
from PyInstaller.utils.hooks import collect_all

datas = [('C:/Users/upwar/WorkBuddy/2026-10-15-12-40-20/build/console/core', 'core'), ('C:/Users/upwar/WorkBuddy/2026-10-15-12-40-20/build/console/engines', 'engines'), ('C:/Users/upwar/WorkBuddy/2026-10-15-12-40-20/build/console/bin', 'bin'), ('C:/Users/upwar/WorkBuddy/2026-10-15-12-40-20/build/console/wechatauto', 'wechatauto')]
binaries = []
hiddenimports = ['console_app', 'console_app.config', 'console_app.tasks', 'console_app.gui', 'console_app.remote_server', 'console_app.sdk_bridge', 'console_app.web_dashboard', 'console_app.web_features', 'console_app.web_clients', 'console_app.ocr_shim', 'cv2', 'numpy', 'mss', 'psutil', 'pyautogui', 'pywinauto', 'pytweening', 'pymsgbox', 'pyperclip', 'pyscreeze', 'pygetwindow', 'pyrect', 'mouseinfo', 'win32api', 'win32con', 'win32gui', 'win32process', 'win32com', 'pythoncom', 'comtypes', 'comtypes.client', 'PIL', 'PIL.Image', 'wechatauto', 'uiautomation', 'colorama', 'cryptography', 'cryptography.hazmat.primitives.ciphers', 'zstandard', 'imageio_ffmpeg']
tmp_ret = collect_all('wechatauto')
datas += tmp_ret[0]; binaries += tmp_ret[1]; hiddenimports += tmp_ret[2]
tmp_ret = collect_all('rapidocr_onnxruntime')
datas += tmp_ret[0]; binaries += tmp_ret[1]; hiddenimports += tmp_ret[2]
tmp_ret = collect_all('onnxruntime')
datas += tmp_ret[0]; binaries += tmp_ret[1]; hiddenimports += tmp_ret[2]


a = Analysis(
    ['C:/Users/upwar/WorkBuddy/2026-10-15-12-40-20/build/console/main.py'],
    pathex=['C:/Users/upwar/WorkBuddy/2026-10-15-12-40-20/build/console'],
    binaries=binaries,
    datas=datas,
    hiddenimports=hiddenimports,
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[],
    noarchive=False,
    optimize=0,
)
pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.datas,
    [],
    name='微信自动化控制台',
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    upx_exclude=[],
    runtime_tmpdir=None,
    console=False,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
    icon=['C:/Users/upwar/WorkBuddy/2026-10-15-12-40-20/build/console/app.ico'],
)
