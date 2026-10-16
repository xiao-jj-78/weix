"""Exercise the real local HTTP UI without launching automation workers."""
from pathlib import Path
from copy import deepcopy
import sys
import json

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'build' / 'console'))
from console_app import config
from console_app.tasks import TaskRunner
from console_app.remote_server import RemoteServer
from playwright.sync_api import sync_playwright, expect

OUTPUT = ROOT / 'build' / 'qa' / 'evidence'
OUTPUT.mkdir(parents=True, exist_ok=True)
saved = deepcopy(config.DEFAULT_CONFIG)

def commit(value):
    saved.clear()
    saved.update(value)

runner = TaskRunner(lambda: saved)
server = RemoteServer(runner, lambda: deepcopy(saved), commit)
ok, message = server.start(18899)
assert ok, message
errors = []
checks = []
try:
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        page = browser.new_page(viewport={'width': 1400, 'height': 1000}, device_scale_factor=1)
        page.on('pageerror', lambda e: errors.append(str(e)))
        page.goto('http://127.0.0.1:18899/', wait_until='networkidle')
        expect(page.locator('#connection')).to_have_text('服务已连接')
        expect(page.locator('.task')).to_have_count(5)
        expect(page.locator('#log')).to_contain_text('暂无日志')
        checks.append('real API status + 5 tasks + empty logs')
        page.screenshot(path=str(OUTPUT / 'web-desktop.png'), full_page=True)
        # A task failure must stay a failure even when the HTTP status is 200.
        page.route('**/api/task/start', lambda route: route.fulfill(
            status=200, content_type='application/json',
            body=json.dumps({'ok': False, 'message': '测试：设备未连接'}, ensure_ascii=False)))
        page.locator('[data-task="phone_like"]').click()
        expect(page.locator('#notice')).to_contain_text('设备未连接')
        expect(page.locator('#notice')).to_have_class('banner error')
        assert not runner.running_ids(), 'No real automation should run during UI QA'
        checks.append('HTTP 200 + ok:false shows error; no worker started')
        page.locator('#tab-settings').click()
        expect(page.locator('[name=round_like_count]')).to_have_value('30')
        page.locator('[name=round_like_count]').fill('47')
        page.locator('#save-settings').click()
        expect(page.locator('#settings-state')).to_contain_text('已保存')
        assert saved['round_like_count'] == 47
        page.locator('#settings-reload').click()
        expect(page.locator('[name=round_like_count]')).to_have_value('47')
        checks.append('config save/read-back uses real HTTP handler and committed snapshot')
        page.locator('#tab-sdk').click()
        expect(page.locator('#sdk-refresh')).to_be_enabled(timeout=20000)
        expect(page.locator('#sdk-state')).to_contain_text('SDK')
        page.screenshot(path=str(OUTPUT / 'web-sdk.png'), full_page=True)
        checks.append('SDK disconnected or real connected state is visible')
        page.set_viewport_size({'width': 390, 'height': 844})
        page.locator('#tab-tasks').click()
        expect(page.locator('.task')).to_have_count(5)
        assert page.evaluate('document.documentElement.scrollWidth <= innerWidth'), 'mobile horizontal overflow'
        page.screenshot(path=str(OUTPUT / 'web-mobile.png'), full_page=True)
        checks.append('390px mobile task layout without horizontal overflow')
        # Polling must disable controls on disconnect and recover on return.
        page.route('**/api/status', lambda route: route.abort())
        expect(page.locator('#connection')).to_have_text('连接已断开', timeout=16000)
        expect(page.locator('[data-task="phone_like"]')).to_be_disabled()
        page.screenshot(path=str(OUTPUT / 'web-offline.png'), full_page=True)
        page.unroute('**/api/status')
        expect(page.locator('#connection')).to_have_text('服务已连接', timeout=16000)
        expect(page.locator('[data-task="phone_like"]')).to_be_enabled()
        checks.append('disconnect disables actions; polling reconnect recovers controls')
        browser.close()
    assert not errors, errors
    print(json.dumps({'ok': True, 'checks': checks, 'page_errors': errors}, ensure_ascii=False, indent=2))
finally:
    runner.stop_all()
    server.stop()
