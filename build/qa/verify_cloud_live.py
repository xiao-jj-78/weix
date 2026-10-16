"""Exercise the deployed cloud with local protected tokens, never print secrets."""
import importlib.util
import json
from pathlib import Path
import sys
import time
import urllib.request
from urllib.error import HTTPError
import uuid

root = Path(__file__).resolve().parent.parent.parent
source = root / 'deploy/cloudbase/windows/cloud_bridge.py'
spec = importlib.util.spec_from_file_location('cloud_bridge', source)
bridge = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = bridge
spec.loader.exec_module(bridge)
config = bridge.load_config()
base = config['cloudUrl']
origin = 'https://jou-d4gq7a3cqf51e6caa-1394316826.tcloudbaseapp.com'

def request(path, token=None, body=None, method=None, cross=False, key=None):
    headers = {'Content-Type': 'application/json'}
    if token: headers['Authorization'] = 'Bearer ' + token
    if cross: headers.update({'Origin': origin, 'Sec-Fetch-Site': 'cross-site'})
    if key: headers['X-Idempotency-Key'] = key
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(base + path, data=data, headers=headers, method=method)
    started = time.monotonic()
    try: response = urllib.request.urlopen(req, timeout=125)
    except HTTPError as error: response = error
    content = response.read(600000)
    try: payload = json.loads(content)
    except Exception: payload = {'ok': False, 'code': 'NON_JSON_RESPONSE'}
    return response.status, payload, response.headers, round(time.monotonic() - started, 2)

code, health, _, _ = request('/health')
record = {'health_http': code, 'health_ok': health.get('ok'), 'service': health.get('service'), 'real_wechat_actions': False}
code, body, _, _ = request('/api/cloud/status')
record['unauthenticated_http'] = code
code, body, headers, _ = request('/api/cloud/status', config['browserToken'], cross=True)
record.update({'browser_http': code, 'browser_ok': body.get('ok'), 'browser_code': body.get('code'), 'nodeOnline': body.get('nodeOnline'), 'cors_exact_origin': headers.get('Access-Control-Allow-Origin') == origin})
if '--connected' in sys.argv and body.get('nodeOnline'):
    code, status, _, elapsed = request('/api/status', config['browserToken'], cross=True)
    record['windows_status'] = {'http': code, 'ok': status.get('ok'), 'version': status.get('version'), 'tasks': len(status.get('tasks', [])), 'elapsedSeconds': elapsed}
    code, sdk, _, elapsed = request('/api/sdk/status', config['browserToken'], cross=True)
    record['sdk_status'] = {'http': code, 'ok': sdk.get('ok'), 'running': sdk.get('running'), 'elapsedSeconds': elapsed}
    code, clients, _, elapsed = request('/api/sdk/clients', config['browserToken'], cross=True)
    record['wechat_windows'] = {'http': code, 'ok': clients.get('ok'), 'count': len(clients.get('clients', [])), 'elapsedSeconds': elapsed, 'passiveOnly': True}
    key = str(uuid.uuid4())
    code, health, _, elapsed = request('/api/sdk/plugins/run', config['browserToken'], {'id': 'sdk-health'}, cross=True, key=key)
    record['sdk_health'] = {'http': code, 'ok': health.get('ok'), 'loaded': health.get('result', {}).get('loaded'), 'elapsedSeconds': elapsed}
    code, replay, _, _ = request('/api/sdk/plugins/run', config['browserToken'], {'id': 'sdk-health'}, cross=True, key=key)
    record['same_request_replay'] = {'http': code, 'same_result': replay == health}
record['ok'] = (record['health_http'] == 200 and record['health_ok'] and record['unauthenticated_http'] == 401 and record['browser_http'] == 200 and record['browser_ok'] and record['cors_exact_origin'])
if '--connected' in sys.argv:
    record['ok'] = bool(record['ok'] and record.get('nodeOnline') and record.get('windows_status', {}).get('version') == '1.1.0' and record.get('sdk_health', {}).get('loaded') and record.get('same_request_replay', {}).get('same_result') and record.get('wechat_windows', {}).get('ok'))
(root / 'build/qa/evidence/cloud-live-result.json').write_text(json.dumps(record, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(record, ensure_ascii=False, indent=2))
raise SystemExit(0 if record['ok'] else 1)
