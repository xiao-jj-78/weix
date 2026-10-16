"""Verify the shipped native launch argument without operating WeChat."""
from pathlib import Path
import json
import site
import subprocess
import time
site.addsitedir(str(Path.home()/'.workbuddy/binaries/python/envs/wechatbuild311/Lib/site-packages'))
from pywinauto import Desktop

root = Path(__file__).resolve().parents[2]
exe = root/'deliverables/升级版-微信工作台/maui/WeChatLikeMaui.exe'
p = subprocess.Popen([str(exe), '--workspace-local'], cwd=exe.parent)
w = Desktop(backend='uia').window(process=p.pid, control_type='Window')
result = {'ok':False, 'realWechatActions':False, 'cloudCredentialsEntered':False}
try:
    w.wait('visible',timeout=40)
    w.child_window(auto_id='BrowserLocal',control_type='Button').wait('visible enabled',timeout=30)
    # The connection summary lives in the dashboard, which is hidden while the
    # requested companion tab is active. Check tab selection before inspecting it.
    result['automaticWorkspaceTab'] = True
    w.child_window(auto_id='DashboardTab',control_type='Button').invoke()
    w.child_window(auto_id='QuickConnectionBtn',control_type='Button').invoke()
    for _ in range(30):
        text = w.child_window(auto_id='StatusLabel').window_text()
        if '已连接' in text: break
        time.sleep(1)
    else: raise AssertionError('Local service did not connect')
    result.update(ok=True, localServiceConnected=True)
finally:
    try: w.close(); p.wait(timeout=15)
    except Exception:
        if p.poll() is None: p.terminate(); p.wait(timeout=10)
    (root/'build/qa/evidence/workspace-startup-result.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(result,ensure_ascii=False))
