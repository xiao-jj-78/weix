"""Check release hashes, archive integrity and absence of protected cloud tokens."""
from pathlib import Path
import hashlib
import importlib.util
import json
import re
import sys
import zipfile

root = Path(__file__).resolve().parents[2]
dest = root/'deliverables/升级版-微信工作台'
spec = importlib.util.spec_from_file_location('cloud_bridge', root/'deploy/cloudbase/windows/cloud_bridge.py')
bridge = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = bridge
spec.loader.exec_module(bridge)
config = bridge.load_config()
secrets = [config[k].encode() for k in ('agentToken','browserToken')]
pattern = re.compile(rb'sk-[A-Za-z0-9]{32,}')
def check(data):
    if any(s in data for s in secrets) or pattern.search(data):
        raise AssertionError('A release file contains a credential; content withheld')

manifest = json.loads((dest/'文件校验.json').read_text(encoding='utf-8'))
for name, meta in manifest.items():
    data = (dest/name).read_bytes()
    assert len(data) == meta['bytes']
    assert hashlib.sha256(data).hexdigest() == meta['sha256']
    check(data)
archives = {}
for name in ['微信工作台-三版本完整包.zip','微信工作台-升级工程源码.zip']:
    with zipfile.ZipFile(root/'deliverables'/name) as archive:
        assert archive.testzip() is None
        for entry in archive.infolist():
            assert not entry.filename.endswith('/access-policy.json')
            check(archive.read(entry))
        archives[name] = len(archive.infolist())
record = {'ok':True,'manifestFiles':len(manifest),'archives':archives,'archiveCrcChecked':True,'plaintextCredentialsIncluded':False}
(root/'build/qa/evidence/workspace-package-result.json').write_text(json.dumps(record,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(record,ensure_ascii=False))
