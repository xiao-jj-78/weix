"""Inspect the shared "微信数据" page in the Windows renderer against a fixture gateway.

Proves the page is reachable from the shell, that its controls render, and that its
three read-only actions address the documented endpoints (login / contacts / chat?who=),
plus that the local CSV export reports a local path.
No real WeChat, no device, no SDK host, no credentials: the gateway is a fixture whose
sdk.proxy is intercepted, and every asserted call is recorded for the evidence file.
"""
from copy import deepcopy
from pathlib import Path
import json
import site
import subprocess
import sys
import time

site.addsitedir(str(Path.home() / '.workbuddy/binaries/python/envs/wechatbuild311/Lib/site-packages'))
from pywinauto import Desktop

root = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(root / 'build/console'))
from console_app import config
from console_app.remote_server import RemoteServer
from console_app.tasks import TaskRunner

out = root / 'build/qa/evidence'
out.mkdir(exist_ok=True, parents=True)

saved = config.load_config()
calls = []
server = RemoteServer(TaskRunner(lambda: saved), lambda: deepcopy(saved), lambda values: None)

CONTACTS = {'ok': True, 'operation': 'contacts', 'accounts': [{'account': 'fixture'}],
            'data': [{'account': 'fixture', 'count': 2, 'names': ['测试好友甲', '测试好友乙'], 'truncated': False}]}
CHAT = {'ok': True, 'operation': 'chat', 'accounts': [{'account': 'fixture'}],
        'data': [{'account': 'fixture', 'count': 1,
                  'messages': [{'who': 'fixture', 'message': '隔离测试消息', 'time': '2026-10-16T00:00:00'}]}]}


def proxy(method, path, body=None, query=''):
    calls.append({'method': method, 'path': path, 'body': body, 'query': query})
    if path == '/api/sdk/contacts':
        return deepcopy(CONTACTS), 200
    if path == '/api/sdk/chat':
        return deepcopy(CHAT), 200
    return {'ok': True, 'launched': False, 'message': '隔离测试服务已记录请求'}, 200


server.sdk.start = lambda: ({'ok': True, 'running': True, 'message': '隔离测试服务'}, 200)
server.sdk.proxy = proxy
server.sdk.status = lambda: {'ok': True, 'running': True, 'message': '隔离测试服务',
                             'session': {'state': 'fixture', 'processes': [], 'accounts': []}, 'plugins': []}

ok, message = server.start(18921)
assert ok, message

exe = root / 'build/maui/WeChatLikeMaui/bin/Release/net10.0-windows10.0.19041.0/win-x64/WeChatLikeMaui.exe'
p = subprocess.Popen([str(exe)], cwd=exe.parent)
result = {'ok': False, 'renderer': 'Windows native MAUI shell (shared page)', 'androidDeviceVerified': False,
          'realWechatActions': False, 'credentialsEntered': False,
          'networkRequests': 'fixture gateway only; no real SDK host contacted', 'steps': [], 'diag': {}}


def win():
    return Desktop(backend='uia').window(process=p.pid, control_type='Window')


def alive():
    return p.poll() is None


def step(name, value):
    result[name] = value
    result['steps'].append({'step': name, 'ok': value is True})


def find(text, control_type=None, timeout=15.0, must_be_visible=True):
    deadline = time.time() + timeout
    while time.time() < deadline:
        if not alive():
            return None
        try:
            for control in win().descendants():
                try:
                    info = control.element_info
                    if (info.name or '').strip() != text:
                        continue
                    if control_type and info.control_type != control_type:
                        continue
                    if must_be_visible and not control.is_visible():
                        continue
                    return control
                except Exception:
                    continue
        except Exception:
            pass
        time.sleep(0.3)
    return None


def text_present(text, timeout=20.0):
    deadline = time.time() + timeout
    while time.time() < deadline:
        if not alive():
            return False
        try:
            for control in win().descendants():
                try:
                    if text in (control.element_info.name or ''):
                        return True
                except Exception:
                    continue
        except Exception:
            pass
        time.sleep(0.3)
    return False


def wait_call(predicate, timeout=25.0):
    deadline = time.time() + timeout
    while time.time() < deadline:
        if predicate():
            return True
        time.sleep(0.1)
    return False


def click(control):
    if control is None:
        return False
    try:
        control.invoke()
        return True
    except Exception:
        try:
            control.click_input()
            return True
        except Exception:
            return False


def control(text, control_type, visible=True):
    """Fresh lookup: UIA references go stale when the layout reflows."""
    return find(text, control_type, timeout=15, must_be_visible=visible)


try:
    win().wait('visible', timeout=45)
    step('shellVisible', True)

    by_id = lambda auto_id, ctype: win().child_window(auto_id=auto_id, control_type=ctype)
    by_id('QuickConnectionBtn', 'Button').invoke()
    by_id('IpEntry', 'Edit').set_edit_text('127.0.0.1')
    by_id('PortEntry', 'Edit').set_edit_text('18921')
    by_id('ConnectBtn', 'Button').invoke()
    time.sleep(2.0)
    step('workspaceConnected', text_present('18921', timeout=20))

    click(find('打开导航', 'Button', timeout=15) or find('关闭导航', 'Button', timeout=5))
    time.sleep(1.5)
    nav = find('微信数据', 'ListItem', timeout=20)
    step('navItemPresent', nav is not None)
    click(nav)
    time.sleep(2.5)
    win().capture_as_image().save(out / 'wechat-data-page.png')

    step('pageReachable', True)
    step('pageConfiguredToGateway', text_present('已连接 http://127.0.0.1:18921', timeout=15))
    step('controlsPresent', all(control(t, c, visible) is not None for t, c, visible in (
        ('一键登录微信', 'Button', True), ('读取通讯录（用户表）', 'Button', True),
        ('读取聊天记录', 'Button', True), ('会话标识 who（如 filehelper / 好友备注）', 'Edit', True),
        ('导出通讯录与聊天为本地 CSV', 'Button', False))))

    before = len(calls)
    click(control('一键登录微信', 'Button'))
    step('loginEndpointHit', wait_call(lambda: any(c['path'] == '/api/sdk/login' for c in calls[before:])))
    step('loginReportsResult', text_present('隔离测试服务已记录请求', timeout=15))

    before = len(calls)
    click(control('读取通讯录（用户表）', 'Button'))
    step('contactsEndpointHit', wait_call(lambda: any(c['path'] == '/api/sdk/contacts' for c in calls[before:])))
    step('contactsRendered', text_present('测试好友甲', timeout=15))

    entry = control('会话标识 who（如 filehelper / 好友备注）', 'Edit')
    if entry is not None:
        entry.set_edit_text('filehelper')
        time.sleep(0.5)
    before = len(calls)
    click(control('读取聊天记录', 'Button'))
    step('chatEndpointHit', wait_call(lambda: any(c['path'] == '/api/sdk/chat' for c in calls[before:])))
    hit = next((c for c in calls[before:] if c['path'] == '/api/sdk/chat'), None)
    step('chatWhoForwarded', bool(hit and 'filehelper' in (hit.get('query') or '')))
    step('chatRendered', text_present('隔离测试消息', timeout=15))

    click(control('导出通讯录与聊天为本地 CSV', 'Button', visible=False))
    step('exportWritesLocalFile', text_present('已导出到本机文件', timeout=15))

    result['calls'] = calls
    result['diag'] = {'processAlive': alive(), 'processExitCode': p.poll()}
    result['ok'] = all(result.get(k) for k in (
        'shellVisible', 'workspaceConnected', 'navItemPresent', 'pageReachable',
        'pageConfiguredToGateway', 'controlsPresent', 'loginEndpointHit', 'loginReportsResult',
        'contactsEndpointHit', 'contactsRendered', 'chatEndpointHit', 'chatWhoForwarded',
        'chatRendered', 'exportWritesLocalFile'))
except Exception as error:
    result['error'] = f'{type(error).__name__}: {error}'
    result['diag'] = {'processAlive': alive(), 'processExitCode': p.poll()}
    result['calls'] = calls
finally:
    try:
        win().close()
        p.wait(timeout=15)
    except Exception:
        if p.poll() is None:
            p.terminate()
            p.wait(timeout=10)
    try:
        server.stop()
    except Exception:
        pass
    (out / 'wechat-data-page-result.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')

for key, value in result.items():
    if key in ('calls', 'steps', 'diag'):
        continue
    print(f"{'PASS' if value is True else ('FAIL' if value is False else 'INFO')}  {key} = {value}")
print(json.dumps({'ok': result['ok'], 'diag': result['diag'], 'calls': [c['path'] for c in result['calls']]}, ensure_ascii=False))
