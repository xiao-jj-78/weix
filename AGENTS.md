# AGENTS.md — 微信自动化控制台（三开源项目整合）

> 本文件写给 Codex / 其他编码代理：仓库根目录的 `AGENTS.md` 会在启动时被自动读取。
> 先读「1. 硬约束」再动手；命令行示例默认 **Git Bash**（Windows），路径用正斜杠或双引号包裹。

---

## 0. 项目一句话

把三个开源微信自动化项目整合成：

1. **电脑版** `微信自动化控制台.exe`（单文件，PyInstaller）— 自动化引擎 + Tkinter 界面 + 局域网遥控 HTTP 服务；
2. **手机版** 两条路线 — Kotlin 原生 apk（轻量）/ .NET MAUI apk + Windows exe（跨平台示范）；
3. **文档** 项目解析报告 + 使用说明书 + MAUI 构建指南。

原始三项目（只读参考，不要改）：

| 目录 | 是什么 |
|---|---|
| `analysis/WeChat-Auto-Like-System-main/` | Python 脚本集：ADB 手机点赞、电脑微信截图匹配点赞、多开管理、广播助手 |
| `analysis/WeChatAuto.SDK-master/` | C# .NET SDK：3.9.12 纯 UIA / 4.x UIA+ONNX OCR，含 WebSocket Server、MCP |
| `analysis/wechatauto-replica-main/` | Python 库（wxauto 复刻）：UIA+坐标发送、SQLCipher 读库、`Moment.LikeMoment` |

---

## 1. 硬约束（必须遵守）

1. **不得修改被驱动项目的公开接口。** 允许的改动只有三类：
   - `build/console/core/*.py` 里带 `# [console-patch]` 注释的最小补丁（已存在的补丁见第 8 节）；
   - 通过环境变量 `WLC_*` 注入配置（见第 6.2）；
   - 新增文件（新模块/新引擎/新平台工程）。
   `core/` 下脚本的函数签名、参数、调用顺序一律不动。
2. **HTTP API 契约冻结**（三端共用，见第 6.3）。要加功能就加新端点，不要改已有端点的路径与字段名。
3. **replica 库（`build/console/wechatauto/`）只作为调用方使用**，不要 patch 库内部；需要新行为就在 `build/console/engines/` 写新驱动。
4. 不要删除 `.workbuddy/`（项目记忆），不要 `rm -rf` 清 `analysis/` 与 `deliverables/`。
5. Windows 上写 `AppData\Local\Android\Sdk` 会被沙箱拦截，需要用户授权后再写。

---

## 2. 环境事实（本机已就绪，别再重复安装）

| 项 | 值 / 说明 |
|---|---|
| 打包用 Python | `C:/Users/upwar/.workbuddy/binaries/python/envs/wechatbuild311/Scripts/python.exe`（**3.11**） |
| 系统 Python 3.11 | `C:/Users/upwar/AppData/Local/Programs/Python/Python311/python.exe` |
| 托管 Python 3.13 | **没有 tkinter**，不要用它打包 GUI（会 `ModuleNotFoundError: tkinter`） |
| .NET SDK | `10.0.402`，workload：`maui-windows` / `android` / `ios` / `maccatalyst` |
| JDK | `C:/Program Files/Microsoft/jdk-17.0.20.8-hotspot` |
| Android SDK | `C:/Users/upwar/AppData/Local/Android/Sdk`（platforms: 34/35/**36**，build-tools: 34.0.0/**36.0.0**） |
| Gradle | `build/tools/gradle-8.7/bin/gradle`（本地解压版；`gradlew` 会联网下载，代理不通时用前者） |
| 可用代理 | `http://127.0.0.1:51202`（curl/pip/dotnet 读 `http_proxy` 环境变量；**Java/Gradle/sdkmanager 不读，需 `-Dhttps.proxyHost=127.0.0.1 -Dhttps.proxyPort=51202`**） |
| 华为/其他代理 | `7892`（鹿语云）与 `7897`（Clash，内核常未启）——**不要假设它们可用**，实测 7892 曾 connection refused |

---

## 3. 目录地图

```text
2026-10-15-12-40-20/
├── AGENTS.md                  # 本文件
├── analysis/                  # 三个原始开源项目（只读参考）
├── deliverables/              # ★ 最终交付物（exe / apk / 文档 / 源码 zip）
└── build/                     # 全部构建工程与中间产物
    ├── console/               # ★ 电脑版工程（Python）
    │   ├── main.py            # 入口（调 console_app.__main__.main）
    │   ├── build_exe.py       # PyInstaller 打包脚本（含 hidden-import / collect-all 清单）
    │   ├── app.ico, app.png   # 图标
    │   ├── console_app/       # 控制台自身代码
    │   │   ├── __main__.py    # 分发器：默认 GUI / --worker / --selftest / --guitest / --serve
    │   │   ├── config.py      # 路径解析 + config.json + worker_env（WLC_* 注入）
    │   │   ├── gui.py         # Tkinter 界面
    │   │   ├── tasks.py       # 任务定义 + 子进程调度 + 日志环形缓冲 + adb 设备检测
    │   │   ├── remote_server.py # HTTP API + 手机网页遥控页（MOBILE_PAGE 常量）
    │   │   └── ocr_shim.py    # pytesseract 兼容层（rapidocr-onnxruntime 实现）
    │   ├── core/              # ★ 被驱动的脚本（含 [console-patch] 补丁）
    │   ├── engines/replica_like.py # 定点点赞驱动（调用 wechatauto 公开 API）
    │   ├── bin/               # 内置 adb.exe + AdbWinApi.dll + AdbWinUsbApi.dll
    │   ├── wechatauto/        # ★ vendor 进来的 replica 库源码（未走 pip）
    │   ├── dist/              # 出包结果：微信自动化控制台.exe
    │   └── guitest_result.txt # --guitest 的证据文件（自动生成）
    ├── android/WeChatLikeRemote/   # ★ 手机版工程（Kotlin，零第三方依赖）
    │   ├── app/src/main/java/com/wechatlike/remote/{ConnectActivity,ControlActivity,Api}.kt
    │   ├── app/src/main/AndroidManifest.xml   # usesCleartextTraffic=true（访问 http 局域网）
    │   └── app/build/outputs/apk/debug/app-debug.apk
    ├── maui/WeChatLikeMaui/   # ★ 手机版+电脑版工程（.NET MAUI）
    │   ├── MainPage.xaml(.cs) # 连接 + 任务卡 + 日志；Windows 端多「启动电脑端」按钮
    │   ├── Models/TaskItem.cs, Services/ConsoleApi.cs
    │   └── bin/Release/net10.0-android/com.wechatlike.maui-Signed.apk
    ├── replica_src/           # replica 库解包源码（vendor 来源）
    ├── tools/                 # gradle-8.7/、platform-36.zip、bt36/、repo.xml（SDK 补装用）
    └── *.log                  # 各次构建/校验日志（排查历史用）
```

---

## 4. 构建命令（照抄可用）

### 4.1 电脑版 exe

```bash
cd build/console
C:/Users/upwar/.workbuddy/binaries/python/envs/wechatbuild311/Scripts/python.exe build_exe.py
# 产物：build/console/dist/微信自动化控制台.exe（约 151 MB，onerun 单文件）
```

`build_exe.py` 里维护了三份清单，加新模块时同步更新：`HIDDEN`（hidden-import）、`COLLECT_ALL`（要连数据文件一起收的包）、以及 `--add-data` 的目录（core/engines/bin/wechatauto）。

### 4.2 手机版 apk（Kotlin）

```bash
cd build/android/WeChatLikeRemote
export ANDROID_HOME="C:\\Users\\upwar\\AppData\\Local\\Android\\Sdk"
../tools/gradle-8.7/bin/gradle assembleDebug --no-daemon \
  -Dhttps.proxyHost=127.0.0.1 -Dhttps.proxyPort=51202 \
  -Dhttp.proxyHost=127.0.0.1 -Dhttp.proxyPort=51202
# 产物：app/build/outputs/apk/debug/app-debug.apk（debug 签名，可直接安装）
```

### 4.3 MAUI（一份代码出 apk + exe）

```bash
cd build/maui/WeChatLikeMaui

# Windows
dotnet build -f net10.0-windows10.0.19041.0 -c Release
# → bin/Release/net10.0-windows10.0.19041.0/win-x64/WeChatLikeMaui.exe

# Android（注意必须传 SDK/JDK 路径；写 AppData 需用户授权）
dotnet build -f net10.0-android -c Release \
  -p:AndroidSdkDirectory="C:\Users\upwar\AppData\Local\Android\Sdk" \
  -p:JavaSdkDirectory="C:\Program Files\Microsoft\jdk-17.0.20.8-hotspot"
# → bin/Release/net10.0-android/com.wechatlike.maui-Signed.apk
```

### 4.4 重新汇总交付物（复制 + 打包源码 + 算 MD5）

交付目录的生成逻辑是一次性脚本，重做时按此顺序：复制 `dist/*.exe` 与两处 apk 到 `deliverables/` → 用 `zipfile` 打包三个工程源码（排除 `bin/obj/build/dist/__pycache__/.gradle`）→ 重新计算 MD5 并更新 `deliverables/02-使用说明书.html` 第 10 节的表格。

---

## 5. 验证命令（改完必须跑，别只看代码）

```bash
cd build/console/dist

# ① 依赖/内置组件/API 自检 —— 期望最后一行 SELFTEST_PASS
"./微信自动化控制台.exe" --selftest

# ② 界面自检 —— 真实建窗口，结果写 guitest_result.txt
"./微信自动化控制台.exe" --guitest
cat guitest_result.txt
# 期望：GUITEST_OK title='微信自动化控制台 v1.0.0' geometry=1060x680 tasks=5 tk=8.6

# ③ 端到端遥控链路（需用 run_in_background 保持服务存活）
"./微信自动化控制台.exe" --serve 18899   &
curl -s http://127.0.0.1:18899/api/ping
curl -s -X POST -H "Content-Type: application/json" \
     -d '{"task":"phone_like"}' http://127.0.0.1:18899/api/task/start
curl -s "http://127.0.0.1:18899/api/logs?task=phone_like&n=20"
curl -s -X POST -H "Content-Type: application/json" \
     -d '{"task":"phone_like"}' http://127.0.0.1:18899/api/task/stop
```

apk 校验：

```bash
"/c/Users/upwar/AppData/Local/Android/Sdk/build-tools/34.0.0/aapt.exe" dump badging \
  build/android/WeChatLikeRemote/app/build/outputs/apk/debug/app-debug.apk | head -6
# 期望：package name='com.wechatlike.remote' versionName='1.0.0' sdkVersion:'24' targetSdkVersion:'34'
```

注意：`--guitest`/`--selftest` 输出含中文，Git Bash 终端可能显示成乱码，**以结果文件与关键字（SELFTEST_PASS / GUITEST_OK）为准**。

---

## 6. 运行架构

### 6.1 子进程分发（关键机制）

`TaskRunner` 启动任务时按是否冻结分两条命令：

| 运行方式 | 命令 |
|---|---|
| 冻结（exe） | `<exe> --worker <脚本绝对路径> [脚本参数...]` |
| 源码 | `<python> -X utf8 -u <脚本绝对路径> [脚本参数...]` |

`--worker` 由 `console_app/__main__.py::_run_worker` 处理：把脚本目录插入 `sys.path` → 按需注入 OCR shim → `runpy.run_path(script, run_name="__main__")`。
`core/launch_4_wechat.py` 与 `core/wechat_multi_process_manager.py` 会再派生子 worker，已按同样规则打补丁（frozen 时用 `[exe, "--worker"]`）。

停止任务用 `TaskRunner._kill_tree`（psutil 递归杀子进程，兜底 `taskkill /T /F`）。

### 6.2 环境变量契约（被驱动脚本只读这些，接口不动）

| 变量 | 对应脚本常量 | 默认 |
|---|---|---|
| `WLC_ROUND_LIKE_COUNT` | `ROUND_LIKE_COUNT` | 30 |
| `WLC_ROUND_REST_SECONDS` | `ROUND_REST_SECONDS` | 3600 |
| `WLC_RUN_START_HOUR` / `WLC_RUN_END_HOUR` | 运行时段 | 8 / 0 |
| `WLC_WECHAT_COUNT` | `WECHAT_COUNT` | 2 |
| `WLC_ADB_DIR` | `SCRCPY_DIR`（adb 目录） | `<bundle>/bin` |
| `WLC_ADB_PORT` | `ADB_PORT` | 5038 |
| `WLC_TESSERACT` | `TESSERACT_EXE` | `<bundle>/bin/tesseract/tesseract.exe` |
| `WLC_MAIN_WINDOW` | `MAIN_WINDOW_X,Y,W,H` | 1960,-760,1080,780 |
| `WLC_MOMENTS_WINDOW` | `MOMENTS_WINDOW_X,Y,W,H` | 2260,-80,576,558 |
| `WLC_DEBUG_DIR` | `DEBUG_DIR`（模板/调试截图） | `<exe同级>/debug_pc_wechat` |

注入点是 `console_app/config.py::worker_env`。

### 6.3 HTTP API 契约（冻结）

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/api/ping` | `{ok:true}` |
| GET | `/api/status` | `{ok, app, version, uptime, remote:{port,lan_ips}, tasks:[{id,name,desc,running,pid,started_at,elapsed,exit_code,last_line}], config}` |
| GET | `/api/logs?task=<id>&n=<n>` | `{ok, task, lines:[...]}` |
| GET | `/api/devices` | `{ok, devices:[...], raw}`（adb devices -l） |
| GET/POST | `/api/config` | 读/写 `config.json` |
| POST | `/api/task/start` | body `{task:"<id>", args?:[...]}` |
| POST | `/api/task/stop` | body `{task:"<id>"}` |
| GET | `/` | 手机网页遥控页（`remote_server.MOBILE_PAGE`） |

任务 id 固定 5 个：`phone_like` / `switch_account` / `pc_like` / `multi_like` / `replica_like`（定义在 `console_app/tasks.py::TASK_DEFS`）。

### 6.4 OCR 兼容层

`comment_like.py`（手机点赞）不用 OCR；`switch_wechat_account.py` 依赖 `pytesseract.image_to_data(..., output_type=Output.DICT)`。
本机装不了 Tesseract（GitHub release 经代理 502），因此 `ocr_shim.py` 用 `rapidocr_onnxruntime` 实现同名接口（返回 `text/left/top/width/height/conf/block_num/par_num/line_num`，conf 为 0-100，按纵向重叠聚成行）。
注入条件（`_inject_ocr_shim`）：脚本名含 `switch` 或 `broadcast` **且** `WLC_TESSERACT` 指向的文件不存在。用户在 `config.json` 里填了真实 tesseract 路径时自动改用真 OCR。

---

## 7. 已验证 / 未验证（不要谎报）

**已验证（有证据）**

- `--selftest` → `SELFTEST_PASS`（12 项：cv2/numpy/mss/psutil/pyautogui/pywinauto/win32api/PIL、内置 adb、OCR 兼容层、6 个内置脚本、遥控 API）
- `--guitest` → `GUITEST_OK ... tasks=5 tk=8.6`（打包后的 exe 里 tkinter 可用）
- 端到端：`--serve 18899` → `POST /api/task/start {"task":"phone_like"}` 返回 `已启动 PID=...` → `/api/logs` 回传脚本输出（无手机时正确报「获取设备列表失败」）→ `/api/status` 状态正确
- APK：aapt 校验包名/版本/minSdk/targetSdk/图标；MAUI 工程 Windows 与 Android 目标均 0 error
- 构建主机信息：Windows 10.0.26200、adb 1.0.41 (37.0.1)

**未验证（缺环境，别当成已完成）**

- 真实点赞、账号切换、多开点赞、replica 定点点赞：本机没有登录态微信、没连安卓设备，全部未跑通实测
- MAUI/ Kotlin 遥控器与实际手机的联调（需手机与电脑同一 Wi-Fi）
- 计划任务（schtasks）定时运行

---

## 8. 已存在的补丁与踩坑记录

`core/` 内的 `[console-patch]`（共 10 处，改动都很小，对照 `analysis/` 原文即可 diff）：

| 文件 | 改了什么 |
|---|---|
| `comment_like.py` | `SCRCPY_DIR` 改读 `WLC_ADB_DIR`；`ADB_PORT` 改读 `WLC_ADB_PORT` |
| `switch_wechat_account.py` | 追加 `import os`；`TESSERACT_EXE` 改读 `WLC_TESSERACT` |
| `wechat_pc_moments_image_test.py` | 追加 `import os`；`DEBUG_DIR`/`ROUND_LIKE_COUNT`/`ROUND_REST_SECONDS`/`MAIN_WINDOW_*`/`MOMENTS_WINDOW_*` 改读环境变量 |
| `launch_4_wechat.py` | `WECHAT_COUNT`/`RUN_START_HOUR`/`RUN_END_HOUR` 改读环境变量；子进程命令改 `base_cmd = [PYTHON_EXE, "--worker"] if frozen else [PYTHON_EXE, "-X","utf8","-u"]` |
| `wechat_multi_process_manager.py` | `PYTHON_EXE` 改 `sys.executable`、`WORKDIR` 改脚本自身目录；子进程命令同上 |
| `wechat_like_worker_by_pid.py` | 模板目录改读 `WLC_DEBUG_DIR`（与主脚本一致，保证校准模板跨会话留存） |

踩过的坑（同类问题复发时直接照此解）：

1. 托管 Python 3.13 无 tkinter → 必须用 3.11 打包。
2. `pip install <本地目录>` 在本沙箱下被 SIGTERM 且无输出 → 改为 vendor 包目录（`build/console/wechatauto`）+ 依赖逐个从 PyPI 装。
3. `winsdk` 无 wheel、编译失败 → 它只在 `guia.py::ScreenOCR` 里惰性 import，**不影响 `import wechatauto`**，忽略即可。
4. 批量 pip 安装里有一个失败会导致整批回滚 → 关键依赖分开装。
5. GitHub release 下载经代理 502 → 换官方镜像或直连 `dl.google.com`；`platform-36_r02.zip`（**注意是 _r02**）与 `build-tools_r36_windows.zip` 解压后要分别落到 `platforms/android-36`、`build-tools/36.0.0`（build-tools 包内层目录名是 `android-16`，需要摊平）。
6. .NET 10 Android **只接受 API 36**（`net10.0-android35.0` 报 NETSDK1140）→ 装 android-36 平台，不要降 TFM。
7. sdkmanager 走系统代理（可能是未启的 7897）→ 502；用 curl 直下或给 Java 显式 `-Dhttps.proxyHost/-Dhttps.proxyPort`。
8. 写 `AppData\Local\Android\Sdk` 被沙箱拦截（SIGTERM / 无输出）→ 需要用户授权后再写。
9. 前端 Bash 后台进程在工具调用结束时会被回收 → 需要长活服务时用受管后台任务（run_in_background）。
10. `/tmp` 在 Git Bash 与 Windows Python 之间不互通 → 中间文件写到工作区内。

---

## 9. 待办 TODO（按优先级）

1. **P0 真机联调**：在用户环境跑通「手机点赞 / 电脑点赞 / 多开 / 定点点赞」，记录失败点并补日志。
2. **P0 窗口坐标自适应**：当前 `main_window`/`moments_window` 默认值针对「右侧竖屏」。可加启动时自动枚举显示器并按分辨率推算（参考 `core/launch_4_wechat.py::get_rightmost_screen` 的做法），或首次运行弹窗让用户选屏。
3. **P1 遥控端鉴权**：HTTP 服务目前无鉴权（仅局域网）。可加 `token` 字段（header 或 query），默认关闭以免破坏现有 API。
4. **P1 打包体积**：exe 151 MB，大头是 onnxruntime + OCR 模型。若用户装了系统 Tesseract 可考虑做「不带 OCR」的精简版；或用 `--onedir` 换启动速度。
5. **P2 图标/签名**：exe 未做代码签名（部分杀软误报）；apk 为 debug 签名。正式分发需生成自己的 keystore 与签名证书。
6. **P2 MAUI 端补齐**：MAUI 版目前是遥控端；可在 Windows 端加「托管 Python 控制台进程 + 退出时清理」。

---

## 10. 交付物清单（`deliverables/`）

| 文件 | 大小 | MD5 |
|---|---|---|
| `微信自动化控制台.exe` | 151,376,268 B | `1bc8a5c95d0dad94ca90020fbe9debbb` |
| `微信点赞遥控器-安卓版.apk` | 824,129 B | `3a64cc8335dbc8da4895c82a46b05265` |
| `微信点赞遥控器-MAUI版.apk` | 29,652,674 B | `8a6432910bd30c2b6f5491f700108ec3` |
| `微信点赞遥控器-MAUI版-Windows.zip` | 55,826,782 B | `3491127bfa934f49cfeaec4051f42ee1` |
| `MAUI工程源码.zip` | 250,698 B | `42ef9b9b3393598055fc43d9c0fb42b1` |
| `电脑版控制台工程源码.zip` | 4,544,553 B | `ecd0a179ba65b908ac37298152fb0de0` |
| `安卓遥控器工程源码.zip` | 61,797 B | `b387b611398a0743c66079b7cccd189b` |
| `01-项目解析报告.html` | 13,903 B | `4963ff3c8f6b0d81bf2e4bb793df6d1e` |
| `03-.NET-MAUI跨平台构建指南.html` | 9,190 B | `21408d57dce062c6ee71dde0c238d5cf` |

> `02-使用说明书.html` 内含第 10 节的同一张校验值表；改产物后两处都要更新。

---

## 11. 合规声明（写进任何新文档都要保留）

本工程整合自 GitHub 开源项目（MIT / Apache-2.0），**仅供学习研究**。通过界面自动化操作微信违反微信用户协议，频繁使用可能触发风控甚至封号；请勿用于骚扰、批量营销等违法违规用途。使用后果由使用者自行承担。
