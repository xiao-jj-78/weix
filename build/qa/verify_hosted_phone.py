"""Verify public phone artifacts and inspect the exact download-page source."""
from pathlib import Path
import hashlib
import json
import urllib.request
from playwright.sync_api import sync_playwright

root=Path(__file__).resolve().parents[2]
base='https://jou-d4gq7a3cqf51e6caa-1394316826.tcloudbaseapp.com/wechat-workspace/'
record={'ok':False,'androidDeviceVerified':False,'credentialsEntered':False,'realWechatActions':False,'files':[]}
apk_record=json.loads((root/'build/qa/evidence/phone-apk-result.json').read_text(encoding='utf-8'))
filename='wechat-workspace-phone-'+apk_record['version']+'.apk'
for name in ['phone.html','downloads/phone-release.json','downloads/'+filename]:
    local=root/'deploy/cloudbase/public'/name
    request=urllib.request.Request(base+name+'?v='+hashlib.sha256(local.read_bytes()).hexdigest()[:12],headers={'Cache-Control':'no-cache','User-Agent':'WeChatWorkspace-ReleaseCheck/1.2'})
    with urllib.request.urlopen(request,timeout=90) as response:
        digest=hashlib.sha256();size=0
        while chunk:=response.read(262144):digest.update(chunk);size+=len(chunk)
        same=digest.hexdigest()==hashlib.sha256(local.read_bytes()).hexdigest()
        assert response.status==200 and same
        record['files'].append({'file':name,'http':response.status,'bytes':size,'sameSha256':same})
with sync_playwright() as p:
    browser=p.chromium.launch(channel='chrome',headless=True,args=['--no-proxy-server'])
    page=browser.new_page(viewport={'width':390,'height':844},device_scale_factor=1)
    page.goto((root/'deploy/cloudbase/public/phone.html').as_uri())
    apk_record=json.loads((root/'build/qa/evidence/phone-apk-result.json').read_text(encoding='utf-8'))
    assert page.get_by_role('link',name='下载安卓安装包 · '+apk_record['version']).get_attribute('href')=='downloads/'+filename+'?v='+apk_record['sha256'][:12]
    assert page.evaluate('document.documentElement.scrollWidth<=innerWidth')
    page.screenshot(path=str(root/'build/qa/evidence/phone-download-mobile.png'),full_page=True)
    browser.close()
record.update(ok=True,pagePreview='exact local source matching hosted SHA256')
(root/'build/qa/evidence/phone-hosting-result.json').write_text(json.dumps(record,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(record,ensure_ascii=False))
