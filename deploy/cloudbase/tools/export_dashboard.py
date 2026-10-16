"""Export the shared dashboard with cloud-only access and node status controls.

Only imports the offline HTML module. It does not start a Windows service, read
local provider settings, connect to WeChat, or contact a cloud account.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
PUBLIC = Path(__file__).resolve().parents[1] / "public"


def export(output: Path) -> dict:
    sys.path.insert(0, str(ROOT / "build" / "console"))
    from console_app.web_dashboard import MOBILE_PAGE

    page = MOBILE_PAGE
    page = page.replace("<title>微信工作台 · 可视化控制</title>",
                        "<title>微信工作台 · 云端控制台</title>")
    page = page.replace("</head>", '<link rel="stylesheet" href="./cloud.css">'
                        '<script src="./cloud-config.js"></script></head>', 1)
    page = page.replace('<body><div class="shell">',
                        '<body><a class="cloud-skip" href="#page-title">跳到工作区</a>'
                        '<div class="shell" id="cloud-workspace" inert aria-hidden="true">', 1)
    page = page.replace("LOCAL WORKSPACE / 网页版", "CLOUD WORKSPACE / 云端控制台")
    old_rail = ('<strong>电脑 · 手机 · 同一工作区</strong>手机与电脑连接同一局域网，即可使用相同的任务、参数和日志。'
                '<br><br>官方微信扫码登录<br>独立 C# SDK 与插件宿主')
    page = page.replace(old_rail, '<strong>云端控制台 + Windows 微信节点</strong>'
                        '通过腾讯云连接电脑上的任务与 SDK。微信在 Windows 节点运行，由你在官方窗口扫码。'
                        '<br><br>云访问码只用于进入工作台<br>与微信登录、AI 密钥相互独立')
    page = page.replace('<div id="notice"', '''<section class="cloud-connection" aria-label="云端与节点连接状态">
<div><span class="cloud-link-label">腾讯云控制台</span><strong id="cloud-account-state">尚未验证访问码</strong></div>
<div><span class="cloud-link-label">Windows 微信节点</span><strong id="cloud-node-state" role="status">等待连接</strong></div>
<button id="cloud-logout" type="button">退出云端</button></section>
<div id="cloud-node-banner" class="banner" role="status" hidden></div>
<div id="notice"''', 1)
    page = page.replace('<span>本机服务 · <span id="footer-url"></span></span>',
                        '<span>云端控制台 · <span id="footer-url"></span></span>')
    page = page.replace('本机扩展宿主', 'Windows 节点扩展宿主')
    page = page.replace('后台宿主运行 SDK 和扩展；登录仍在官方微信窗口中由你扫码完成。',
                        'SDK 和扩展在 Windows 微信节点运行；请在该电脑的官方微信窗口扫码，云容器不承载微信桌面。')
    page = page.replace('请保留官方微信桌面窗口。UI 自动化可能需要可见窗口及交互桌面，不能保证锁屏后继续操作。停止宿主不会退出官方微信。',
                        '请在 Windows 节点保留官方微信窗口和可用的交互桌面。锁屏、远程桌面断开或节点离线可能中断自动化。停止宿主不会退出官方微信。')
    page = page.replace('<button id="sdk-login">打开微信登录</button>',
                        '<button id="sdk-login">在 Windows 节点打开微信</button>')
    # The original API paths and fields remain frozen. The cloud layer adds
    # authentication, finite waits, and idempotency without changing those calls.
    start = page.index('async function api(path,body){')
    end = page.index('\nfunction el(', start)
    page = page[:start] + '''async function api(path,body){const timeout=path==='/api/sdk/contacts'||path==='/api/sdk/ai/preview'||path==='/api/sdk/plugins/run'||path==='/api/sdk/clients/verify'?115000:38000;return window.WlcCloud.api(path,body,timeout);}''' + page[end:]
    page = page.replace('if(refreshing||document.hidden)return;',
                        'if(!window.WlcCloud.isReady()||refreshing||document.hidden)return;', 1)
    page = page.replace("$('connection').textContent=ok?'服务已连接':'连接已断开';",
                        "$('connection').textContent=ok?'Windows 节点已连接':'Windows 节点未连接';", 1)
    page = page.replace("'无法连接电脑端：'+e.message+'。服务恢复后会自动重试。'",
                        "'Windows 节点暂不可用：'+e.message+'。仅状态查询会自动重试。'", 1)
    startup = "document.addEventListener('visibilitychange',()=>{if(!document.hidden)refresh();});const requestedView=new URLSearchParams(location.search).get('view');if(['sdk','devices','settings','targets','ai'].includes(requestedView))document.querySelector('[data-view=\"'+requestedView+'\"]').click();refresh();setInterval(refresh,3000);"
    if startup not in page:
        raise RuntimeError("Shared dashboard startup changed; review cloud export before publishing")
    page = page.replace(startup, "document.addEventListener('visibilitychange',()=>{if(!document.hidden)refresh();});window.WlcCloud.onReady(()=>{const requestedView=new URLSearchParams(location.search).get('view');if(['sdk','devices','settings','targets','ai'].includes(requestedView))document.querySelector('[data-view=\"'+requestedView+'\"]').click();refresh();});setInterval(refresh,3000);", 1)
    # The login exists before any shared dashboard script can start polling.
    overlay = '''<div class="cloud-login" id="cloud-login">
<section class="cloud-login-card" role="dialog" aria-modal="true" aria-labelledby="cloud-login-title" aria-describedby="cloud-login-help">
<div class="eyebrow">WECHAT WORKSPACE · TENCENT CLOUD</div><h1 id="cloud-login-title">连接你的微信工作台</h1>
<p id="cloud-login-help" class="sub">云端提供控制台，Windows 节点运行微信与 SDK。输入云访问码后，再查看节点是否在线。</p>
<form id="cloud-login-form"><label class="label" for="cloud-access-code">云访问码</label>
<input id="cloud-access-code" type="password" required maxlength="4096" autocomplete="current-password" aria-describedby="cloud-login-error cloud-login-note" placeholder="填写部署时设置的浏览器访问码">
<p id="cloud-login-error" class="cloud-login-error" role="alert" hidden></p>
<button class="primary" id="cloud-login-submit" type="submit">进入工作台</button></form>
<p class="source-note" id="cloud-login-note">访问码只保存在本次浏览器会话中。点击“退出云端”会清除；请勿填写腾讯云密码、微信密码或 AI 密钥。</p>
<div class="cloud-login-foot"><span>官方微信扫码登录在 Windows 节点完成</span><span>仅供学习研究</span></div>
</section></div><script src="./cloud-bootstrap.js"></script>'''
    page = page.replace('<script>\n\'use strict\';', overlay + '<script>\n\'use strict\';', 1)
    output.mkdir(parents=True, exist_ok=True)
    # A configured public backend URL is deployment data. Keep it unchanged
    # when rebuilding index.html and copy it as-is for an alternate output.
    if output != PUBLIC.resolve():
        for asset in ("cloud-config.js", "cloud-bootstrap.js", "cloud.css"):
            shutil.copy2(PUBLIC / asset, output / asset)
    target = output / "index.html"
    target.write_text(page, encoding="utf-8", newline="\n")
    meta = {"source": "build/console/console_app/web_dashboard.py:MOBILE_PAGE",
            "includes": ["sdk", "ai-profiles", "moments-user-selection"],
            "authentication": "Bearer browser access code; sessionStorage only",
            "deployment": "prepared locally; no cloud deployment performed by exporter",
            "indexSha256": hashlib.sha256(target.read_bytes()).hexdigest()}
    (output / "export-info.json").write_text(json.dumps(meta, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return meta


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=PUBLIC)
    args = parser.parse_args()
    print(json.dumps(export(args.output.resolve()), ensure_ascii=False))


if __name__ == "__main__":
    main()
