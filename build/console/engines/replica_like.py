# -*- coding: utf-8 -*-
"""定点点赞引擎驱动（wechatauto-replica 库的调用方）。

只调用库的公开 API（WeChat / Moment.LikeMoment / WeChatDB / MomentDB），
不对库本身做任何修改。库未安装或导入失败时给出明确错误。
"""
from __future__ import annotations

import argparse
import json
import sys
import threading
import time


def _utf8_stdout():
    for s in (sys.stdout, sys.stderr):
        try:
            s.reconfigure(encoding="utf-8", errors="replace")
        except Exception:
            pass


def _import_with_watchdog(module_name, timeout=45):
    """带超时的 import：库在导入时可能扫描/激活微信窗口而阻塞。"""
    box = {}

    def work():
        try:
            box["mod"] = __import__(module_name, fromlist=["*"])
        except Exception as exc:  # noqa: BLE001
            box["err"] = exc

    t = threading.Thread(target=work, daemon=True)
    t.start()
    t.join(timeout)
    if t.is_alive():
        return None, TimeoutError("导入 %s 超过 %d 秒（微信可能未启动）" % (module_name, timeout))
    if "err" in box:
        return None, box["err"]
    return box.get("mod"), None


class _UiOnlyIdentity:
    """Public WeChat db argument adapter; prevent eager default database initialization."""
    def get_self_info(self):
        return {}


def main():
    _utf8_stdout()
    ap = argparse.ArgumentParser(description="定点点赞（wechatauto-replica 引擎）")
    ap.add_argument("--author", default="", help="发布者昵称")
    ap.add_argument("--authors-json", default="", help="明确选择的多个发布者昵称 JSON 数组")
    ap.add_argument("--keyword", default="", help="正文关键词")
    ap.add_argument("--max-screens", type=int, default=100)
    ap.add_argument("--watch", action="store_true", help="持续监听模式")
    ap.add_argument("--interval", type=int, default=1800, help="监听模式扫描间隔（秒）")
    args = ap.parse_args()

    try:
        authors = selected_authors(args.author, args.authors_json)
    except ValueError as exc:
        print("× 用户选择无效：%s" % exc)
        return 1
    if not authors and not args.keyword:
        print("× 请至少提供 --author 或 --keyword")
        return 1

    lib, err = _import_with_watchdog("wechatauto")
    if err is not None:
        print("× 无法加载 wechatauto 库：%s: %s" % (type(err).__name__, err))
        print("  请确认电脑版微信已登录并保持在前台任务栏。")
        return 2

    print("[引擎] wechatauto v%s 加载成功" % getattr(lib, "__version__", "?"))

    try:
        wx = lib.WeChat(db=_UiOnlyIdentity())
    except Exception as exc:  # noqa: BLE001
        print("× 初始化微信客户端失败：%s: %s" % (type(exc).__name__, exc))
        print("  请确认：1) 微信已登录 2) 微信已固定到任务栏 3) 窗口未最小化")
        return 2

    m = wx.Moment
    print("[引擎] 使用 UI 定位；不读取微信数据库或登录凭据。")
    print("[引擎] 所选用户：%s" % ("、".join(authors) if authors else "按明确关键词定位"))

    rounds = 0
    while True:
        rounds += 1
        failures = run_selected_round(m, authors, args.keyword, args.max_screens, rounds)
        if not args.watch:
            return 1 if failures else 0
        print("[引擎] %.0f 分钟后进行下一轮扫描" % (args.interval / 60.0), flush=True)
        time.sleep(args.interval)


def selected_authors(author="", authors_json=""):
    """Exact names only; empty explicit selections never expand to all users."""
    names = json.loads(authors_json) if authors_json else ([author] if author else [])
    if not isinstance(names, list) or len(names) > 100:
        raise ValueError("请选择最多 100 位用户")
    result = []
    for name in names:
        if not isinstance(name, str) or not name.strip() or len(name.strip()) > 200 or "\x00" in name:
            raise ValueError("昵称必须是 1–200 字的有效文本")
        if name.strip() not in result:
            result.append(name.strip())
    return result


def run_selected_round(moment, authors, keyword, max_screens, round_number=1):
    """One matching post per selected author per round; an author failure cannot widen selection."""
    if not authors and not keyword:
        raise ValueError("必须先选择用户或填写明确关键词")
    failures = 0
    for author in authors or [None]:
        try:
            # Upstream LikeMoment accepts a substring fallback from raw control
            # text, and its author branch ignores keyword. Validate the public
            # located item before invoking the separate public Like operation.
            item = moment.find_moment(publisher=author, keyword=keyword or None, db=None, max_screens=max_screens)
            if item is None:
                raise ValueError("未找到符合条件的动态，已跳过")
            actual_author = str(getattr(item, "publisher", "") or "").strip()
            if author and actual_author != author:
                raise ValueError("动态作者与所选昵称不完全一致，已跳过")
            if keyword and keyword not in str(getattr(item, "text", "") or ""):
                raise ValueError("动态正文不包含指定关键词，已跳过")
            response = moment.Like(item)
            print("[第%d轮] 用户=%s 结果: %s %s" % (round_number, author or "关键词筛选", response.is_success, response["message"]), flush=True)
            if not response.is_success:
                failures += 1
        except Exception as exc:
            failures += 1
            print("[第%d轮] 用户=%s 执行失败: %s: %s" % (round_number, author, type(exc).__name__, exc), flush=True)
    return failures


if __name__ == "__main__":
    raise SystemExit(main())
