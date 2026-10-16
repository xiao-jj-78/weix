"""Test new client APIs and native/web management with synthetic windows only."""
from pathlib import Path
from copy import deepcopy
import json
import subprocess
import sys
import time
import urllib.request
from urllib.error import HTTPError
from playwright.sync_api import sync_playwright, expect
try:
    from pywinauto import Desktop
except ImportError:
    import site
    site.addsitedir(str(Path.home()/'.workbuddy/binaries/python/envs/wechatbuild311/Lib/site-packages'))
    from pywinauto import Desktop

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT/'build/qa/evidence'
OUT.mkdir(exist_ok=True, parents=True)
sys.path.insert(0, str(ROOT/'build/console'))
from console_app import config
from console_app.tasks import TaskRunner
from console_app.remote_server import RemoteServer

def http(base, path, body=None):
    req = urllib.request.Request(base+path, data=None if body is None else json.dumps(body).encode(), headers={'Content-Type':'application/json'})
    try: response=urllib.request.urlopen(req, timeout=8)
    except HTTPError as error: response=error
    with response: return response.status, json.load(response)

record={'realWechatActions':False,'fixtureClientWindows':True,'checks':[]}
sdkexe=ROOT/'build/sdk/publish/WeChatSdkHost.exe'
sdk=subprocess.Popen([str(sdkexe),'--port','18778'], cwd=sdkexe.parent, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, creationflags=subprocess.CREATE_NO_WINDOW)
try:
    deadline=time.monotonic()+20
    while True:
        try: code,clients=http('http://127.0.0.1:18778','/api/sdk/clients'); break
        except OSError:
            if time.monotonic()>deadline: raise
            time.sleep(.2)
    assert code==200 and clients['ok'] and all('instanceId' in c and c['account'] is None for c in clients['clients'])
    for path,body in [('/api/sdk/clients/focus',{'instanceId':f'{sdk.pid}:638960000000000000'}),('/api/sdk/clients/open',{'executable':'cmd.exe'}),('/api/sdk/clients/verify',{'pid':123})]:
        code,value=http('http://127.0.0.1:18778',path,body); assert code==409 and value['ok'] is False
    record['checks'].append('Real new C# API passively discovers windows; stale/foreign targets and arbitrary launch/read arguments rejected')
finally:
    try: http('http://127.0.0.1:18778','/api/sdk/host/stop',{})
    finally:
        try:sdk.wait(timeout=15)
        except subprocess.TimeoutExpired:sdk.terminate();sdk.wait(timeout=10)

saved=deepcopy(config.DEFAULT_CONFIG)
fixture_ready=ROOT/'build/qa/obj/companion-ready.json'
fixture_ready.unlink(missing_ok=True)
fixture_exe=ROOT/'build/qa/obj/companion-fixture/Weixin.exe'
companion_process=subprocess.Popen([str(fixture_exe),str(fixture_ready)],cwd=fixture_exe.parent)
deadline=time.monotonic()+20
while not fixture_ready.exists():
    if time.monotonic()>deadline:raise AssertionError('Companion fixture did not open')
    time.sleep(.2)
fixture_identity=json.loads(fixture_ready.read_text(encoding='utf-8'))
server=RemoteServer(TaskRunner(lambda:saved),lambda:deepcopy(saved),lambda values:saved.update(values))
clients=[{'instanceId':fixture_identity['instanceId'],'pid':fixture_identity['pid'],'name':'Weixin','version':'fixture','alias':'测试窗口 A','state':'login-unverified','account':None,'verifiedAt':None},
         {'instanceId':'434343:638960000000000001','pid':434343,'name':'Weixin','version':'fixture','alias':'测试窗口 B','state':'login-unverified','account':None,'verifiedAt':None}]
calls=[]
def fixture(method,path,body=None):
    calls.append((method,path,body))
    if path=='/api/sdk/clients':return {'ok':True,'clients':deepcopy(clients),'count':2},200
    if path=='/api/sdk/clients/alias':
        next(c for c in clients if c['instanceId']==body['instanceId'])['alias']=body['alias']
        return {'ok':True,'message':'测试备注已保存'},200
    if path=='/api/sdk/clients/focus':return {'ok':True,'foreground':True,'message':'测试窗口切换已记录，未操作真实微信'},200
    return {'ok':False,'message':'fixture: unsupported'},404
server.sdk.proxy=fixture
server.sdk.status=lambda:{'ok':True,'running':False,'installed':True,'message':'隔离测试服务','session':{'state':'fixture','processes':[],'accounts':[]},'plugins':[]}
ok,message=server.start(18905)
assert ok,message
proc=None
try:
    with sync_playwright() as pw:
        browser=pw.chromium.launch(headless=True)
        page=browser.new_page(viewport={'width':1440,'height':1000})
        errors=[]; page.on('pageerror',lambda error:errors.append(str(error)))
        page.goto('http://127.0.0.1:18905');page.locator('#tab-clients').click()
        expect(page.locator('#clients-count')).to_have_text('2 个微信窗口')
        expect(page.locator('#clients-list')).to_contain_text('测试窗口 A')
        page.get_by_role('button',name='备注',exact=True).first.click()
        page.locator('#client-alias-name').fill('测试·工作账号')
        page.locator('#client-alias-save').click()
        expect(page.locator('#clients-list')).to_contain_text('测试·工作账号')
        page.get_by_role('button',name='显示窗口',exact=True).first.click()
        expect(page.locator('#clients-status')).to_contain_text('未操作真实微信')
        page.screenshot(path=str(OUT/'web-clients-desktop.png'),full_page=True)
        page.set_viewport_size({'width':390,'height':844});page.screenshot(path=str(OUT/'web-clients-mobile.png'),full_page=True)
        assert page.evaluate('document.documentElement.scrollWidth<=innerWidth'), 'mobile overflow'
        assert not errors, errors
        browser.close()
    record['checks'].append('Web multi-window list, alias dialog, selected focus body and 390px layout passed against fixture')
    exe=ROOT/'build/maui/WeChatLikeMaui/bin/Release/net10.0-windows10.0.19041.0/win-x64/WeChatLikeMaui.exe'
    proc=subprocess.Popen([str(exe)],cwd=exe.parent)
    window=Desktop(backend='uia').window(process=proc.pid,control_type='Window')
    window.wait('visible',timeout=40)
    window.child_window(auto_id='QuickConnectionBtn',control_type='Button').invoke()
    original_ip=window.child_window(auto_id='IpEntry',control_type='Edit').get_value()
    original_port=window.child_window(auto_id='PortEntry',control_type='Edit').get_value()
    window.child_window(auto_id='IpEntry',control_type='Edit').set_edit_text('127.0.0.1')
    window.child_window(auto_id='PortEntry',control_type='Edit').set_edit_text('18905')
    window.child_window(auto_id='ConnectBtn',control_type='Button').invoke()
    deadline=time.monotonic()+25
    while time.monotonic()<deadline:
        if '已连接' in window.child_window(auto_id='StatusLabel').window_text():break
        time.sleep(.3)
    else:raise AssertionError('Native fixture connection did not succeed')
    window.child_window(auto_id='CompanionTab',control_type='Button').invoke()
    window.child_window(auto_id='ClientFocus-'+str(fixture_identity['pid']),control_type='Button').wait('exists enabled',timeout=20)
    window.child_window(auto_id='ClientFocus-'+str(fixture_identity['pid']),control_type='Button').invoke()
    assert any(path=='/api/sdk/clients/focus' and body['instanceId']==fixture_identity['instanceId'] for _,path,body in calls if body)
    window.child_window(auto_id='BrowserLocal',control_type='Button').invoke()
    time.sleep(3)
    window.set_focus();time.sleep(.6)
    window.capture_as_image().save(OUT/'native-cloud-clients.png')
    tree=[{'name':node.window_text(),'id':node.element_info.automation_id,'type':node.element_info.control_type} for node in window.descendants()]
    assert any(node['id']=='CloudBrowser' for node in tree), 'Embedded browser missing'
    assert any(node['id']=='ClientCompanion-'+str(fixture_identity['pid']) for node in tree), 'Companion action missing'
    (OUT/'native-workspace-uia.json').write_text(json.dumps(tree,ensure_ascii=False,indent=2),encoding='utf-8')
    record['checks'].append('Actual MAUI workspace tab, synthetic client list, explicit focus forwarding and embedded WebView UI passed')
    import win32gui
    companion_window=Desktop(backend='uia').window(process=companion_process.pid,control_type='Window')
    companion_window.wait('visible',timeout=10)
    before=win32gui.GetWindowRect(companion_window.handle)
    window.child_window(auto_id='ClientCompanion-'+str(fixture_identity['pid']),control_type='Button').invoke()
    time.sleep(1)
    after=win32gui.GetWindowRect(companion_window.handle)
    assert after!=before, 'Companion fixture did not move'
    window.child_window(auto_id='BrowserRestore',control_type='Button').invoke()
    time.sleep(1)
    restored=win32gui.GetWindowRect(companion_window.handle)
    assert restored==before, (before,after,restored)
    record['checks'].append('Real Win32 companion layout moves only the synthetic window; explicit restore returns its exact original rectangle')
    window.child_window(auto_id='DashboardTab',control_type='Button').invoke()
    window.child_window(auto_id='IpEntry',control_type='Edit').set_edit_text(original_ip)
    window.child_window(auto_id='PortEntry',control_type='Edit').set_edit_text(original_port)
    window.child_window(auto_id='ConnectBtn',control_type='Button').invoke()
    time.sleep(1)
    record['ok']=True
finally:
    if proc and proc.poll() is None:
        try:window.close();proc.wait(timeout=10)
        except Exception:proc.terminate();proc.wait(timeout=10)
    server.stop()
    if companion_process.poll() is None:
        try:Desktop(backend='uia').window(process=companion_process.pid,control_type='Window').close();companion_process.wait(timeout=5)
        except Exception:companion_process.terminate();companion_process.wait(timeout=5)
    (OUT/'workspace-result.json').write_text(json.dumps(record,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(record,ensure_ascii=False,indent=2))
