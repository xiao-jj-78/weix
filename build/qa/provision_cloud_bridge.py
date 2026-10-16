"""Create/reuse local DPAPI bridge credentials; stage only SHA-256 role hashes."""
import importlib.util
import json
from pathlib import Path
import secrets
import sys

root = Path(__file__).resolve().parent.parent.parent
source = root / 'deploy/cloudbase/windows/cloud_bridge.py'
spec = importlib.util.spec_from_file_location('cloud_bridge', source)
bridge = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = bridge
spec.loader.exec_module(bridge)
url = sys.argv[1] if len(sys.argv) > 1 else 'https://jou-d4gq7a3cqf51e6caa.service.tcloudbase.com/wechat-workspace-api'
path = bridge.state_dir() / 'config.json'
if path.exists():
    config = bridge.load_config()
    agent_token, browser_token = config['agentToken'], config['browserToken']
else:
    agent_token, browser_token = secrets.token_urlsafe(32), secrets.token_urlsafe(32)
metadata = bridge.save_config(url, agent_token, browser_token)
config = bridge.load_config()
policy = {'browserHash': config['browserTokenSha256'], 'agentHash': config['agentTokenSha256']}
target = root / 'build/cloudbase-stage/server/access-policy.json'
target.parent.mkdir(parents=True, exist_ok=True)
target.write_text(json.dumps(policy, indent=2), encoding='utf-8')
print(json.dumps({'ok': True, **metadata, 'protection': 'Windows DPAPI CurrentUser', 'plaintext_written': False}, ensure_ascii=False))
