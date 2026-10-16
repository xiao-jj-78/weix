# -*- coding: utf-8 -*-
"""微信自动化控制台 - 电脑版。

基于三个开源项目整合：
  1. WeChat-Auto-Like-System（ADB 手机点赞 / 电脑微信模板匹配点赞 / 多开管理）
  2. wechatauto-replica（wxauto 复刻库：UIA+OCR 定点点赞）
  3. WeChatAuto.SDK 的引擎设计参考

本包只做调度与界面，不修改被驱动脚本/库的公开接口。
"""
from __future__ import annotations

APP_NAME = "微信自动化控制台"
APP_VERSION = "1.0.0"
