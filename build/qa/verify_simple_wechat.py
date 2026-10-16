"""Verify the desktop one-button entry against isolated synthetic endpoints."""
from copy import deepcopy
from pathlib import Path
import json
import site
import subprocess
import sys
import time
site.addsitedir(str(Path.home()/'.workbuddy/binaries/python/envs/wechatbuild311/Lib/site-packages'))
from pywinauto import Desktop

root=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(root/'build/console'))
from console_app import config
from console_app.remote_server import RemoteServer
from console_app.tasks import TaskRunner
saved=config.load_config()
clients=[{'instanceId':'22222:638960000000000001','pid':22222,'alias':'测试窗口 A','name':'Weixin','state':'login-unverified'}]
calls=[]
server=RemoteServer(TaskRunner(lambda:saved),lambda:deepcopy(saved),lambda values:None)
def start():calls.append(('start',{}));return {'ok':True,'running':True,'message':'测试服务已启动'},200
def proxy(method,path,body=None):
    if path=='/api/sdk/clients':return {'ok':True,'clients':deepcopy(clients),'count':len(clients)},200
    if path in {'/api/sdk/clients/open','/api/sdk/clients/focus'}:
        calls.append((path,body));return {'ok':True,'message':'测试请求已记录，未操作真实微信','foreground':True},200
    return {'ok':False,'message':'unsupported fixture route'},404
server.sdk.start=start
server.sdk.proxy=proxy
server.sdk.status=lambda:{'ok':True,'running':True,'message':'隔离测试服务','session':{'state':'fixture','processes':[],'accounts':[]},'plugins':[]}
ok,message=server.start(18906);assert ok,message
exe=root/'build/maui/WeChatLikeMaui/bin/Release/net10.0-windows10.0.19041.0/win-x64/WeChatLikeMaui.exe'
p=subprocess.Popen([str(exe)],cwd=exe.parent)
w=Desktop(backend='uia').window(process=p.pid,control_type='Window')
result={'ok':False,'realWechatActions':False,'cloudCredentialsEntered':False}
def wait_call(predicate):
    for _ in range(80):
        if predicate():return
        time.sleep(.1)
    raise AssertionError('Expected synthetic request was not received')
try:
    w.wait('visible',timeout=40)
    w.child_window(auto_id='QuickWechatBtn',control_type='Button').wait('visible',timeout=20)
    assert not w.child_window(auto_id='IpEntry',control_type='Edit').exists(timeout=.3)
    w.capture_as_image().save(root/'build/qa/evidence/desktop-simple-wechat.png')
    w.child_window(auto_id='QuickConnectionBtn',control_type='Button').invoke()
    ip=w.child_window(auto_id='IpEntry',control_type='Edit');port=w.child_window(auto_id='PortEntry',control_type='Edit')
    old_ip=ip.get_value();old_port=port.get_value()
    ip.set_edit_text('127.0.0.1');port.set_edit_text('18906')
    w.child_window(auto_id='ConnectBtn',control_type='Button').invoke()
    wait_call(lambda:'已连接' in w.child_window(auto_id='StatusLabel').window_text())
    w.child_window(auto_id='QuickWechatBtn',control_type='Button').invoke()
    wait_call(lambda:any(c[0]=='/api/sdk/clients/focus' for c in calls))
    assert next(c[1]['instanceId'] for c in calls if c[0]=='/api/sdk/clients/focus')==clients[0]['instanceId']
    assert not any(c[0]=='/api/sdk/clients/open' for c in calls)
    w.child_window(auto_id='QuickWechatBtn',control_type='Button').wait('enabled',timeout=10)
    clients.clear()
    w.child_window(auto_id='QuickWechatBtn',control_type='Button').invoke()
    wait_call(lambda:any(c[0]=='/api/sdk/clients/open' for c in calls))
    w.child_window(auto_id='QuickWechatBtn',control_type='Button').wait('enabled',timeout=10)
    assert any(c[0]=='start' for c in calls)
    clients.extend([{'instanceId':'22222:638960000000000001','pid':22222,'alias':'测试窗口 A'},
                    {'instanceId':'33333:638960000000000002','pid':33333,'alias':'测试窗口 B'}])
    before=len(calls)
    w.child_window(auto_id='QuickWechatBtn',control_type='Button').invoke()
    chosen=None
    for _ in range(50):
        for top in Desktop(backend='uia').windows(process=p.pid):
            for control in top.descendants():
                if control.element_info.name=='测试窗口 B · PID 33333' and control.is_visible():
                    chosen=control;break
            if chosen:break
        if chosen:break
        time.sleep(.1)
    assert chosen,'Synthetic window selection was not displayed'
    chosen.click_input()
    wait_call(lambda:any(c[0]=='/api/sdk/clients/focus' and c[1]['instanceId']==clients[1]['instanceId'] for c in calls[before:]))
    w.child_window(auto_id='QuickWechatBtn',control_type='Button').wait('enabled',timeout=10)
    result.update(ok=True,advancedSettingsInitiallyHidden=True,existingWindowReused=True,noWindowStartsOfficialClient=True,sdkStartedAutomatically=True,multipleWindowsRequireExplicitChoice=True)
    ip.set_edit_text(old_ip);port.set_edit_text(old_port)
    w.child_window(auto_id='ConnectBtn',control_type='Button').invoke();time.sleep(.5)
finally:
    try:w.close();p.wait(timeout=15)
    except Exception:
        if p.poll() is None:p.terminate();p.wait(timeout=10)
    server.stop()
    (root/'build/qa/evidence/simple-wechat-result.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(result,ensure_ascii=False))
