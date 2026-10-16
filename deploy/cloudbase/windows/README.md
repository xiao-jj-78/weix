# Windows 云端连接

此程序由电脑主动连接腾讯云 CloudBase。它不向公网开放本机端口，也不向云端上传微信登录凭据。电脑仍需开机，微信界面自动化仍需已登录、可交互的 Windows 桌面。

1. 先启动电脑版微信工作台，使本机 `http://127.0.0.1:8765` 可用。
2. 由部署工具调用 `cloud_bridge.save_config(cloud_url, agent_token, browser_token)` 完成初始化；也可运行 `python cloud_bridge.py init` 并按隐藏输入提示填写。不要把令牌作为命令行参数。
3. 运行 `Start-CloudBridge.ps1`。无需新增第三方 Python 依赖。
4. 运行 `Copy-CloudBrowserToken.ps1`，在云端网页登录框中粘贴登录口令。复制窗口会在关闭或 60 秒后清理自己设置的剪贴板内容。
5. 运行 `Stop-CloudBridge.ps1` 仅停止本机云连接。它会等待已发出的请求收尾，不会强制退出微信或工作台。

配置存于 `%LOCALAPPDATA%\WeChatLikeConsole\CloudBridge\config.json`。`agentTokenProtected` 和 `browserTokenProtected` 使用当前 Windows 用户的 DPAPI 加密；SHA-256 仅用于校验。`journal.sqlite3` 保留命令执行标记和 DPAPI 加密结果，不保存命令明文或 AI 密钥。请勿删掉执行记录后重新连接旧队列。

协议：`POST /bridge/poll` 正文 `{agentId:"main-windows"}`；`POST /bridge/result` 正文 `{id,agentId,status,body,claimToken?}`。均使用独立 agent 令牌。每个进程最多同时转发三个读取，写入串行执行；本机请求最多等待 115 秒。只允许固定工作台接口、固定任务和插件，日志参数按字段校验。不会转发任意 URL、任意查询参数、服务退出接口或附加任务启动参数。

命令执行前先持久写入标记，再请求本机接口。断线只重传结果，绝不自动重做本机操作；重启时未完成命令显示“状态不确定”，需先查看工作台再决定下一步。已确认的命令 ID 也会保留，避免重放。云端令牌应通过云端密钥配置管理，不应上传本机配置文件。

验证：`python -m unittest discover -s tests -v` 使用独立的模拟云端与本机服务，不执行微信操作。

本工程整合自 GitHub 开源项目（MIT / Apache-2.0），**仅供学习研究**。通过界面自动化操作微信违反微信用户协议，频繁使用可能触发风控甚至封号；请勿用于骚扰、批量营销等违法违规用途。使用后果由使用者自行承担。
