"""Native MAUI interaction check against a temporary real Python service."""
from pathlib import Path
import subprocess
import sys
import time
import json
from copy import deepcopy
from pywinauto import Desktop

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'build' / 'console'))
from console_app import config
from console_app.tasks import TaskRunner
from console_app.remote_server import RemoteServer

OUTPUT = ROOT / 'build' / 'qa' / 'evidence'
OUTPUT.mkdir(parents=True, exist_ok=True)
exe = ROOT / 'build' / 'maui' / 'WeChatLikeMaui' / 'bin' / 'Release' / 'net10.0-windows10.0.19041.0' / 'win-x64' / 'WeChatLikeMaui.exe'
saved = deepcopy(config.DEFAULT_CONFIG)
trace = []
def commit(value):
    trace.append({'event': 'config_commit', 'value': deepcopy(value), 'time': time.time()})
    saved.clear()
    saved.update(value)
runner = TaskRunner(lambda: saved)
server = RemoteServer(runner, lambda: deepcopy(saved), commit)
ok, message = server.start(18899)
assert ok, message
handler = server.httpd.RequestHandlerClass
original_body = handler._body
def traced_body(request):
    body = original_body(request)
    trace.append({'event': 'http_post', 'path': request.path, 'body': deepcopy(body),
                  'host': request.headers.get('Host'), 'time': time.time()})
    return body
handler._body = traced_body
proc = None
try:
    proc = subprocess.Popen([str(exe)], cwd=exe.parent)
    window = Desktop(backend='uia').window(process=proc.pid, control_type='Window')
    window.wait('visible', timeout=45)
    time.sleep(2)
    window.capture_as_image().save(OUTPUT / 'native-disconnected.png')
    window.child_window(auto_id='QuickConnectionBtn', control_type='Button').invoke()
    window.child_window(auto_id='IpEntry', control_type='Edit').set_edit_text('127.0.0.1')
    window.child_window(auto_id='PortEntry', control_type='Edit').set_edit_text('18899')
    window.child_window(auto_id='ConnectBtn', control_type='Button').invoke()
    deadline = time.monotonic() + 25
    while time.monotonic() < deadline:
        status = window.child_window(auto_id='StatusLabel').window_text()
        if '已连接' in status:
            break
        time.sleep(.4)
    else:
        raise AssertionError('Native connection failed: ' + status)
    window.child_window(auto_id='ConnectBtn', control_type='Button').wait('enabled', timeout=30)
    window.capture_as_image().save(OUTPUT / 'native-connected.png')
    window.child_window(auto_id='SdkTab', control_type='Button').invoke()
    time.sleep(1)
    window.capture_as_image().save(OUTPUT / 'native-sdk.png')
    window.child_window(auto_id='SettingsTab', control_type='Button').invoke()
    number_field = window.child_window(auto_id='LikeCountEntry', control_type='Edit')
    number_field.wait('exists enabled', timeout=20)
    number_field.set_focus()
    number_field.type_keys('^a53{TAB}', set_foreground=False)
    time.sleep(1)
    window.child_window(auto_id='MainScroll').wrapper_object().iface_scroll.SetScrollPercent(-1, 100)
    time.sleep(.5)
    trace.append({'event': 'before_save', 'field_value': number_field.get_value(),
                  'saved_count': saved['round_like_count'], 'time': time.time()})
    # The native Settings form now contains contact choices; make its save action visible.
    save_button = window.child_window(auto_id='SaveConfigBtn', control_type='Button', visible_only=False)
    save_button.wait('enabled', timeout=20)
    save_button.invoke()
    deadline = time.monotonic() + 12
    while saved['round_like_count'] != 53 and time.monotonic() < deadline:
        time.sleep(.2)
    window.capture_as_image().save(OUTPUT / 'native-settings.png')
    assert saved['round_like_count'] == 53, 'Native config save failed: '+str(saved['round_like_count'])
    assert not runner.running_ids()
    print(json.dumps({'ok':True,'checks':['native MAUI loads','connects actual HTTP service','SDK tab reachable','configuration write committed','no automation task executed']},ensure_ascii=False))
finally:
    if proc and proc.poll() is None:
        try:
            window.capture_as_image().save(OUTPUT / 'native-final-state.png')
            (OUTPUT / 'native-uia.json').write_text(json.dumps([
                {'id': item.element_info.automation_id, 'name': item.window_text(),
                 'type': item.element_info.control_type, 'enabled': item.is_enabled()}
                for item in window.descendants()], ensure_ascii=False, indent=2), encoding='utf-8')
        except Exception:
            pass
    (OUTPUT / 'native-http-trace.json').write_text(json.dumps({'events': trace, 'saved': saved}, ensure_ascii=False, indent=2), encoding='utf-8')
    if proc and proc.poll() is None:
        proc.terminate()
        proc.wait(timeout=10)
    runner.stop_all()
    server.stop()
