"""Check the frozen release and loopback cloud mode without WeChat actions."""
import datetime
import json
import os
from pathlib import Path
import subprocess
import time
import urllib.request

root = Path(__file__).resolve().parent.parent.parent
exe = root / 'build/console/dist/微信自动化控制台.exe'
env = dict(os.environ, WLC_BIND_HOST='127.0.0.1')
result = {'checked_at': datetime.datetime.now().astimezone().isoformat(), 'real_wechat_actions': False}
check = subprocess.run([str(exe), '--selftest'], cwd=exe.parent, env=env, timeout=120, creationflags=subprocess.CREATE_NO_WINDOW)
result['frozen_selftest_exit_code'] = check.returncode
base = 'http://127.0.0.1:18896'
process = subprocess.Popen([str(exe), '--serve', '18896'], cwd=exe.parent, env=env, creationflags=subprocess.CREATE_NO_WINDOW)
try:
    deadline = time.monotonic() + 50
    while True:
        try:
            with urllib.request.urlopen(base + '/api/status', timeout=3) as response:
                status = json.load(response)
            break
        except Exception:
            if time.monotonic() > deadline or process.poll() is not None:
                raise
            time.sleep(.5)
    result['version'] = status['version']
    result['bind_host'] = status['remote']['bind_host']
    result['tasks'] = len(status['tasks'])
    request = urllib.request.Request(base + '/api/service/stop', data=b'{}', headers={'Content-Type': 'application/json'}, method='POST')
    with urllib.request.urlopen(request, timeout=5) as response:
        result['graceful_stop'] = json.load(response)['ok']
    result['service_exit_code'] = process.wait(timeout=20)
finally:
    if process.poll() is None:
        process.terminate()
        process.wait(timeout=20)
result['ok'] = (check.returncode == 0 and result['version'] == '1.1.0' and result['bind_host'] == '127.0.0.1' and result['tasks'] == 5 and result['graceful_stop'] and result['service_exit_code'] == 0)
(root / 'build/qa/evidence/release-runtime-result.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(result, ensure_ascii=False, indent=2))
raise SystemExit(0 if result['ok'] else 1)
