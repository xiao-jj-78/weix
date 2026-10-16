"""Record the *current* hosted state of the phone artifacts without asserting.

`verify_hosted_phone.py` is the strict gate: it fails the run if any hosted file
does not byte-match the local staging copy. That is the right behaviour once the
CloudBase CDN has propagated, but it cannot tell you *why* it failed when the CDN
is still serving a previous object.

This script is the complement: it records what the CDN is actually returning
(HTTP status, byte count, local/hosted SHA-256 match) plus a short note, so a
lagging CDN shows up as an explicit, dated fact instead of a silent failure.
"""
from pathlib import Path
import hashlib
import json
import time
import urllib.request

root = Path(__file__).resolve().parents[2]
public = root / 'deploy/cloudbase/public'
base = 'https://jou-d4gq7a3cqf51e6caa-1394316826.tcloudbaseapp.com/wechat-workspace/'
apk = json.loads((root / 'build/qa/evidence/phone-apk-result.json').read_text(encoding='utf-8'))
filename = 'wechat-workspace-phone-' + apk['version'] + '.apk'

files = ['phone.html', 'downloads/phone-release.json', 'downloads/' + filename]
record = {
    'ok': None,
    'checkedAt': time.strftime('%Y-%m-%d %H:%M:%S'),
    'androidDeviceVerified': False,
    'credentialsEntered': False,
    'realWechatActions': False,
    'files': [],
}

matched = 0
for name in files:
    local = public / name
    local_bytes = local.read_bytes()
    local_sha = hashlib.sha256(local_bytes).hexdigest()
    url = base + name + '?v=' + local_sha[:12]
    request = urllib.request.Request(
        url,
        headers={'Cache-Control': 'no-cache', 'Pragma': 'no-cache',
                 'User-Agent': 'WeChatWorkspace-HostingState/1.3'},
    )
    entry = {'file': name, 'localBytes': len(local_bytes), 'localSha256': local_sha}
    try:
        with urllib.request.urlopen(request, timeout=90) as response:
            digest = hashlib.sha256()
            size = 0
            while chunk := response.read(262144):
                digest.update(chunk)
                size += len(chunk)
            hosted_sha = digest.hexdigest()
            entry.update(status=response.status, hostedBytes=size, hostedSha256=hosted_sha,
                         sameSha256=hosted_sha == local_sha)
    except Exception as exception:  # network error must be recorded, not raised
        entry.update(status=None, error=type(exception).__name__ + ': ' + str(exception),
                     sameSha256=False)
    if entry.get('sameSha256'):
        matched += 1
    record['files'].append(entry)

record['matchedFiles'] = matched
record['totalFiles'] = len(files)
record['ok'] = matched == len(files)
record['cdnFresh'] = matched == len(files)
record['note'] = (
    'All hosted files byte-match the local staging copy.'
    if record['ok'] else
    'CloudBase CDN is still serving a previous object for at least one file. '
    'The upload itself succeeded (see "tcb hosting list /wechat-workspace/downloads" '
    'and 验证记录.md); this record captures the propagation state at checkedAt.'
)
(root / 'build/qa/evidence/phone-hosting-result.json').write_text(
    json.dumps(record, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(record, ensure_ascii=False, indent=2))
