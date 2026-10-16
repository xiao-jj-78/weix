# -*- coding: utf-8 -*-
"""pytesseract 兼容层。

使用 rapidocr-onnxruntime（本地 ONNX 模型，随程序内置）实现与
pytesseract.image_to_data(..., output_type=Output.DICT) 相同的数据结构，
使 switch_wechat_account.py 等依赖 OCR 的脚本在**不安装 Tesseract** 的
电脑上也能运行。

仅在未检测到真实 tesseract 可执行文件时，由 worker 调度器注入
sys.modules["pytesseract"]。被驱动脚本的调用方式完全不变。
"""
from __future__ import annotations

import threading
from enum import Enum


class Output(Enum):
    DICT = 0
    BYTES = 1
    STRING = 2


class _Cmd:
    """兼容 pytesseract.pytesseract.tesseract_cmd 赋值（脚本会设置它）。"""

    tesseract_cmd = ""


pytesseract = _Cmd()

_lock = threading.Lock()
_engine = None


def _get_engine():
    global _engine
    with _lock:
        if _engine is None:
            from rapidocr_onnxruntime import RapidOCR

            _engine = RapidOCR()
        return _engine


def _to_bgr(image):
    import numpy as np

    img = np.asarray(image)
    if img.ndim == 2:
        try:
            import cv2

            img = cv2.cvtColor(img, cv2.COLOR_GRAY2BGR)
        except Exception:
            img = np.stack([img, img, img], axis=-1)
    return img


def _run_ocr(img):
    engine = _get_engine()
    try:
        result, _elapse = engine(img)
    except Exception:
        result = None
    return result or []


def _group_lines(dets):
    """把检测框按纵向重叠聚成“行”，模拟 tesseract 的 line 分组。"""
    boxes = []
    for det in dets:
        box, text, score = det[0], det[1], det[2]
        xs = [p[0] for p in box]
        ys = [p[1] for p in box]
        boxes.append(
            {
                "x1": min(xs),
                "y1": min(ys),
                "x2": max(xs),
                "y2": max(ys),
                "text": str(text).strip(),
                "score": float(score),
            }
        )
    boxes.sort(key=lambda b: (b["y1"] + b["y2"]) / 2.0)
    lines = []
    for b in boxes:
        placed = False
        cy = (b["y1"] + b["y2"]) / 2.0
        for line in lines:
            lcy = (line["y1"] + line["y2"]) / 2.0
            lh = max(1.0, line["y2"] - line["y1"])
            if abs(cy - lcy) <= lh * 0.6:
                line["y1"] = min(line["y1"], b["y1"])
                line["y2"] = max(line["y2"], b["y2"])
                line["items"].append(b)
                placed = True
                break
        if not placed:
            lines.append({"y1": b["y1"], "y2": b["y2"], "items": [b]})
    for line in lines:
        line["items"].sort(key=lambda b: b["x1"])
    return lines


def image_to_data(image, lang=None, config=None, output_type=Output.DICT, nice=0, timeout=0):
    """返回与 pytesseract Output.DICT 等价的字典。"""
    keys = (
        "level",
        "page_num",
        "block_num",
        "par_num",
        "line_num",
        "word_num",
        "left",
        "top",
        "width",
        "height",
        "conf",
        "text",
    )
    data = {k: [] for k in keys}
    img = _to_bgr(image)
    dets = _run_ocr(img)
    lines = _group_lines(dets)
    for line_idx, line in enumerate(lines):
        for word_idx, item in enumerate(line["items"]):
            data["level"].append(5)
            data["page_num"].append(1)
            data["block_num"].append(1)
            data["par_num"].append(1)
            data["line_num"].append(line_idx + 1)
            data["word_num"].append(word_idx + 1)
            data["left"].append(int(item["x1"]))
            data["top"].append(int(item["y1"]))
            data["width"].append(int(item["x2"] - item["x1"]))
            data["height"].append(int(item["y2"] - item["y1"]))
            data["conf"].append(round(item["score"] * 100.0, 2))
            data["text"].append(item["text"])
    if output_type == Output.DICT:
        return data
    if output_type == Output.BYTES:
        import pickle

        return pickle.dumps(data)
    return "\n".join(t for t in data["text"] if t)


def image_to_string(image, lang=None, config=None, nice=0, output_type=Output.STRING, timeout=0):
    img = _to_bgr(image)
    dets = _run_ocr(img)
    lines = _group_lines(dets)
    return "\n".join("".join(i["text"] for i in line["items"]) for line in lines)


def get_tesseract_version():
    return "rapidocr-shim-1.0"
