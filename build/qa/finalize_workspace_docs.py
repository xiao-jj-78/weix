"""Refresh release documentation and recorded, non-secret feature metadata."""
from pathlib import Path
import json

root = Path(__file__).resolve().parents[2]
p = root/'build/launchers/Start-Workspace.ps1'
s = p.read_text(encoding='utf-8-sig').replace('Login uses official WeChat QR confirmation.', 'Login follows the official WeChat prompt and saved authorization.')
p.write_text(s, encoding='utf-8-sig')  # Windows PowerShell 5.1 must recognize Chinese literals.
p = root/'DESIGN.md'
s = p.read_text(encoding='utf-8').replace('官方微信登录必须由用户扫码确认。', '官方微信登录由本人按官方提示确认，优先复用已有授权状态。')
p.write_text(s, encoding='utf-8')
p = root/'build/qa/验证记录.md'
s = p.read_text(encoding='utf-8').replace('20 项本地检查', '21 项本地检查').replace('19 项检查', '21 项检查')
addition = '''## 云端浏览器与多微信管理（2026-10-16）

- 新增多微信窗口 API、原生面板和网页面板；Python 31 项、云桥接 21 项、Windows 连接器 21 项检查通过。
- 实际 MAUI WebView 已显示本地测试服务页面；桌面及 390 像素手机面板、备注弹窗与指定窗口操作传递通过。
- 启动独立且明确标记“非微信”的测试窗口，实际验证 Win32 伴随排列与精确恢复原窗口位置；没有移动或登录真实微信。
- 真实 C# 宿主被动窗口列表与过期目标拒绝通过；账号读取、登录状态复用和多个真实账号兼容性仍待本人账号验证。
- Windows 独立浏览器配置用于保留网页状态；官方微信使用自己的状态，不复制微信令牌、Cookie 或账号文件。

'''
if '## 云端浏览器与多微信管理（2026-10-16）' not in s:
    s = s.replace('## 合规声明', addition+'## 合规声明')
p.write_text(s, encoding='utf-8')
p = root/'build/sdk/README.md'
s = p.read_text(encoding='utf-8')
addition = '''## 多微信窗口管理

新增 GET `/api/sdk/clients` 和 POST `/api/sdk/clients/open`、`/focus`、`/alias`、`/verify`，不更改既有接口。窗口列表被动发现同一 Windows 会话的官方客户端；`instanceId` 绑定 PID 与进程启动时间。focus 接收 instanceId，alias 接收 instanceId 和 alias（最多 60 字符）；open 与 verify 不接收参数。verify 通过现有 SDK 显式读取账号，返回上次验证时间，不据窗口存在断言已登录。open 请求官方启动器，不能强制多开。所有云端操作仍受固定路由、认证与防重复消费控制。

'''
if '## 多微信窗口管理' not in s:
    s = s.replace('## 合规声明', addition+'## 合规声明')
p.write_text(s, encoding='utf-8')
p = root/'deploy/cloudbase/deployment-status.json'
obj = json.loads(p.read_text(encoding='utf-8'))
obj['testCounts'].update(cloudServer=21, windowsConnector=21)
obj['features'] = ['embedded-cloud-browser', 'official-WeChat-companion-windows', 'CSharp-SDK', 'multi-WeChat-window-management']
p.write_text(json.dumps(obj, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
print('Release documentation refreshed; no credentials accessed.')
