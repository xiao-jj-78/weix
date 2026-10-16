"""Prepare the isolated CloudBase function upload without local user data."""
from pathlib import Path
import json
import shutil

root = Path(__file__).resolve().parent.parent.parent
source = root / 'deploy/cloudbase'
stage = root / 'build/cloudbase-stage'
policy_path = stage / 'server/access-policy.json'
if not policy_path.is_file():
    raise RuntimeError('Initialize the Windows DPAPI bridge first')
policy = json.loads(policy_path.read_text(encoding='utf-8'))
if not all(len(policy[k]) == 64 for k in ('browserHash', 'agentHash')):
    raise RuntimeError('Invalid staged hash policy')
shutil.copytree(source / 'server', stage / 'server', dirs_exist_ok=True,
                ignore=shutil.ignore_patterns('node_modules', 'test', 'tests', '*.log', 'access-policy.json'))
shutil.copy2(source / 'cloudbaserc.json', stage / 'cloudbaserc.json')
print(json.dumps({'ok': True, 'stage': str(stage), 'runtime': 'Nodejs20.19', 'credentials': 'event-function temporary identity', 'plaintextTokensIncluded': False}))
