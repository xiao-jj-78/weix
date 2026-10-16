# -*- coding: utf-8 -*-
"""路径与配置管理。

设计约束：
- 核心脚本（core/ 下）只通过环境变量读取配置，不改动其原有函数接口；
- 同时支持源码运行（python main.py）与 PyInstaller 冻结运行（--onefile）。
"""
from __future__ import annotations

import json
import os
import sys
import tempfile
import threading
from copy import deepcopy
from pathlib import Path

APP_NAME = "微信自动化控制台"
APP_VERSION = "1.1.0"
_CONFIG_LOCK = threading.RLock()

DEFAULT_CONFIG = {
    # 点赞参数
    "round_like_count": 30,          # 每轮点赞数量
    "round_rest_minutes": 30,        # 每轮结束后休息分钟数
    "run_start_hour": 8,             # 每日运行开始小时（0-23）
    "run_end_hour": 0,               # 每日运行结束小时（0-23，0 表示跨天到 24 点）
    "wechat_count": 2,               # 多开点赞的微信数量
    # 手机 / ADB
    "adb_dir": "",                   # 空 = 使用内置 adb
    "adb_port": "5038",              # ADB 端口（MuMu 模拟器用户保持 5038，纯真机可改 5037）
    # OCR
    "tesseract_path": "",            # 空 = 使用内置 OCR（rapidocr 兼容层）
    # 电脑微信单账号窗口位置（多屏用户按需修改）
    "main_window": [1960, -760, 1080, 780],
    "moments_window": [2260, -80, 576, 558],
    # 定点点赞（replica 引擎）
    "replica_author": "",
    "replica_authors": [],          # explicit multi-select; empty retains single-author compatibility
    "replica_keyword": "",
    "replica_watch": False,          # 持续监听模式
    "replica_interval": 30,          # 监听模式扫描间隔（分钟）
    # 手机遥控
    "remote_port": 8765,
    "remote_autostart": False,
}


class ConfigError(ValueError):
    """A configuration value cannot be safely passed to a worker."""


_INT_FIELDS = {
    "round_like_count": (1, 10000),
    "round_rest_minutes": (0, 10080),
    "run_start_hour": (0, 23),
    "run_end_hour": (0, 23),
    "wechat_count": (1, 20),
    "adb_port": (1, 65535),
    "replica_interval": (1, 10080),
    "remote_port": (1, 65535),
}
_BOOL_FIELDS = {"replica_watch", "remote_autostart"}
_RECT_FIELDS = {"main_window", "moments_window"}


def _checked_value(key, value):
    if key == "replica_authors":
        if not isinstance(value, list) or len(value) > 100:
            raise ConfigError("replica_authors 必须是最多 100 位用户的列表")
        names = []
        for item in value:
            if not isinstance(item, str) or not item.strip() or len(item.strip()) > 200 or "\x00" in item:
                raise ConfigError("所选用户昵称必须是 1–200 字的有效文本")
            if item.strip() not in names:
                names.append(item.strip())
        return names
    if key in _INT_FIELDS:
        low, high = _INT_FIELDS[key]
        if isinstance(value, bool) or not isinstance(value, (str, int)):
            raise ConfigError("%s 必须是 %d–%d 的整数" % (key, low, high))
        try:
            number = int(value)
        except (ValueError, TypeError):
            raise ConfigError("%s 必须是 %d–%d 的整数" % (key, low, high)) from None
        if not low <= number <= high:
            raise ConfigError("%s 必须是 %d–%d 的整数" % (key, low, high))
        return str(number) if key == "adb_port" else number
    if key in _BOOL_FIELDS:
        if not isinstance(value, bool):
            raise ConfigError("%s 必须是布尔值" % key)
        return value
    if key in _RECT_FIELDS:
        if not isinstance(value, (list, tuple)) or len(value) != 4:
            raise ConfigError("%s 必须是 [x, y, 宽, 高]" % key)
        if any(isinstance(v, bool) or not isinstance(v, int) for v in value):
            raise ConfigError("%s 的坐标与尺寸必须是整数" % key)
        if not all(-100000 <= v <= 100000 for v in value[:2]) or not all(1 <= v <= 32768 for v in value[2:]):
            raise ConfigError("%s 的坐标或尺寸超出范围" % key)
        return list(value)
    if not isinstance(value, str) or len(value) > 4096 or "\x00" in value:
        raise ConfigError("%s 必须是有效文本（最多 4096 字）" % key)
    return value.strip()


def validate_config(cfg, *, partial=False):
    """Validate before committing a snapshot; never mutate the supplied dictionary."""
    if not isinstance(cfg, dict):
        raise ConfigError("配置必须是 JSON 对象")
    unknown = set(cfg) - set(DEFAULT_CONFIG)
    if unknown:
        raise ConfigError("未知配置项: %s" % ", ".join(sorted(str(k) for k in unknown)))
    result = {} if partial else deepcopy(DEFAULT_CONFIG)
    for key, value in cfg.items():
        result[key] = _checked_value(key, value)
    return result


def is_frozen() -> bool:
    return getattr(sys, "frozen", False)


def bundle_root() -> Path:
    """资源根目录：冻结运行为 PyInstaller 解包目录，源码运行为 console/ 目录。"""
    if is_frozen():
        return Path(getattr(sys, "_MEIPASS", Path(sys.executable).resolve().parent))
    return Path(__file__).resolve().parent.parent


def app_dir() -> Path:
    """用户数据目录（config.json / 日志 / 调试截图）。"""
    if is_frozen():
        return Path(sys.executable).resolve().parent
    return bundle_root()


def bin_dir() -> Path:
    return bundle_root() / "bin"


def core_dir() -> Path:
    return bundle_root() / "core"


def engines_dir() -> Path:
    return bundle_root() / "engines"


def log_dir() -> Path:
    d = app_dir() / "logs"
    d.mkdir(parents=True, exist_ok=True)
    return d


def config_path() -> Path:
    return app_dir() / "config.json"


def load_config() -> dict:
    cfg = deepcopy(DEFAULT_CONFIG)
    p = config_path()
    if p.exists():
        try:
            data = json.loads(p.read_text(encoding="utf-8"))
            if isinstance(data, dict):
                # Keep valid saved values if an older or hand-edited file has one bad field.
                for key, value in data.items():
                    if key in DEFAULT_CONFIG:
                        try:
                            cfg[key] = _checked_value(key, value)
                        except ConfigError:
                            pass
        except Exception:
            pass
    return cfg


def save_config(cfg: dict) -> None:
    snapshot = validate_config(cfg)
    p = config_path()
    with _CONFIG_LOCK:
        p.parent.mkdir(parents=True, exist_ok=True)
        temp_name = None
        try:
            with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", dir=p.parent,
                                             prefix=".config-", suffix=".tmp", delete=False) as f:
                temp_name = f.name
                json.dump(snapshot, f, ensure_ascii=False, indent=2)
                f.flush()
                os.fsync(f.fileno())
            os.replace(temp_name, p)
        finally:
            if temp_name and os.path.exists(temp_name):
                os.unlink(temp_name)


def adb_exe(cfg: dict) -> str:
    d = cfg.get("adb_dir") or str(bin_dir())
    for name in ("adb.exe", "adb"):
        candidate = Path(d) / name
        if candidate.exists():
            return str(candidate)
    return str(Path(d) / "adb.exe")


def tesseract_exe(cfg: dict) -> str:
    p = cfg.get("tesseract_path") or str(bin_dir() / "tesseract" / "tesseract.exe")
    return str(p)


def worker_env(cfg: dict) -> dict:
    """子进程环境变量：核心脚本只读这里面的值，接口不变。"""
    cfg = validate_config(cfg)
    env = os.environ.copy()
    env["WLC_ROUND_LIKE_COUNT"] = str(int(cfg.get("round_like_count", 30)))
    env["WLC_ROUND_REST_SECONDS"] = str(int(cfg.get("round_rest_minutes", 30)) * 60)
    env["WLC_RUN_START_HOUR"] = str(int(cfg.get("run_start_hour", 8)))
    env["WLC_RUN_END_HOUR"] = str(int(cfg.get("run_end_hour", 0)))
    env["WLC_WECHAT_COUNT"] = str(int(cfg.get("wechat_count", 2)))
    env["WLC_ADB_DIR"] = cfg.get("adb_dir") or str(bin_dir())
    env["WLC_ADB_PORT"] = str(cfg.get("adb_port", "5038"))
    env["WLC_TESSERACT"] = tesseract_exe(cfg)
    mw = cfg.get("main_window") or [0, 0, 1080, 780]
    qw = cfg.get("moments_window") or [0, 0, 576, 558]
    env["WLC_MAIN_WINDOW"] = ",".join(str(int(v)) for v in mw)
    env["WLC_MOMENTS_WINDOW"] = ",".join(str(int(v)) for v in qw)
    env["WLC_DEBUG_DIR"] = str(app_dir() / "debug_pc_wechat")
    env["PYTHONUTF8"] = "1"
    env["PYTHONIOENCODING"] = "utf-8"
    return env


def lan_ips() -> list:
    """本机局域网 IPv4 列表（供手机遥控连接）。"""
    ips = []
    try:
        import socket

        hostname = socket.gethostname()
        for info in socket.getaddrinfo(hostname, None, socket.AF_INET):
            ip = info[4][0]
            if ip not in ips and not ip.startswith("127."):
                ips.append(ip)
    except Exception:
        pass
    if not ips:
        try:
            import socket

            s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            s.connect(("8.8.8.8", 80))
            ips.append(s.getsockname()[0])
            s.close()
        except Exception:
            ips.append("127.0.0.1")
    return ips
