"""Real web→Python→C# flow with an isolated local AI stub; never calls WeChat."""
from copy import deepcopy
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from tempfile import TemporaryDirectory
import json
import os
import sys
import threading
import time
from playwright.sync_api import sync_playwright, expect

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT/'build/console'))
from console_app import config
from console_app.remote_server import RemoteServer
from console_app.tasks import TaskRunner

OUTPUT = ROOT/'build/qa/evidence'
OUTPUT.mkdir(parents=True,exist_ok=True)
saved=deepcopy(config.DEFAULT_CONFIG)
def commit(value):
    saved.clear()
    saved.update(value)
ai_requests=[]
class AiStub(BaseHTTPRequestHandler):
    def log_message(self,*args): pass
    def do_POST(self):
        payload=json.loads(self.rfile.read(int(self.headers['Content-Length'])))
        ai_requests.append({'path':self.path,'model':payload['model'],'messages':payload['messages']})
        body=json.dumps({'choices':[{'message':{'role':'assistant','content':'本地测试回复：已收到样例消息。'}}]},ensure_ascii=False).encode()
        self.send_response(200)
        self.send_header('Content-Type','application/json')
        self.send_header('Content-Length',str(len(body)))
        self.end_headers()
        self.wfile.write(body)

stub=ThreadingHTTPServer(('127.0.0.1',0),AiStub)
threading.Thread(target=stub.serve_forever,daemon=True).start()
runner=TaskRunner(lambda:saved)
server=RemoteServer(runner,lambda:deepcopy(saved),commit)
ok,message=server.start(0)
assert ok,message
checks=[]
errors=[]
original=os.environ.get('WLC_SDK_AI_CONFIG_DIR')
try:
    with TemporaryDirectory(prefix='wechat-ai-web-qa-') as sandbox:
        os.environ['WLC_SDK_AI_CONFIG_DIR']=sandbox
        response,code=server.sdk.start()
        assert response['ok'],response
        for _ in range(30):
            if server.sdk.status()['running']:break
            time.sleep(.3)
        else:raise AssertionError('SDK host did not start')
        with sync_playwright() as p:
            browser=p.chromium.launch(headless=True)
            page=browser.new_page(viewport={'width':1400,'height':1100})
            page.on('pageerror',lambda e:errors.append(str(e)))
            page.goto('http://127.0.0.1:%d/'%server.state.port,wait_until='networkidle')
            expect(page.locator('#connection')).to_have_text('服务已连接')
            page.locator('#tab-targets').click()
            page.locator('#target-manual').fill('测试用户甲\n测试用户乙\n测试用户甲')
            page.locator('#targets-add').click()
            expect(page.locator('#target-summary')).to_contain_text('2 位')
            page.locator('#target-search').fill('乙')
            expect(page.locator('#target-list .contact-choice')).to_have_count(1)
            page.locator('#target-search').fill('')
            page.locator('#targets-save').click()
            expect(page.locator('#targets-state')).to_contain_text('已保存 2 位')
            assert saved['replica_authors']==['测试用户甲','测试用户乙']
            assert saved['replica_author']==''
            page.screenshot(path=str(OUTPUT/'web-selected-users.png'),full_page=True)
            checks.append('exact multi-user selection, dedupe, search and real config commit; no like started')
            page.locator('#tab-ai').click()
            expect(page.locator('#ai-profile option')).to_have_count(1)
            page.locator('#ai-name').fill('本地测试服务 / QA')
            page.locator('#ai-url').fill('http://127.0.0.1:%d/v1'%stub.server_address[1])
            page.locator('#ai-model').fill('qa-local-model')
            page.locator('#ai-key').fill('qa-placeholder-key-never-shared')
            page.locator('#ai-contacts').fill('测试私聊联系人')
            page.locator('#ai-save').click()
            expect(page.locator('#ai-key')).to_have_value('')
            expect(page.locator('#ai-key-state')).to_contain_text('已保存密钥')
            public,_=server.sdk.proxy('GET','/api/sdk/ai/config')
            assert 'qa-placeholder-key-never-shared' not in json.dumps(public)
            assert public['config']['allowedContacts']==['测试私聊联系人']
            assert public['config']['profiles'][0]['hasApiKey'] is True
            checks.append('provider config/password stored through C#; GET never returns plaintext key')
            page.locator('#ai-sample').fill('这是一条本地 QA 样例，不来自真实微信。')
            page.locator('#ai-preview').click()
            expect(page.locator('#ai-result')).to_contain_text('本地测试回复',timeout=30000)
            assert len(ai_requests)==1 and ai_requests[0]['path']=='/v1/chat/completions'
            assert ai_requests[0]['model']=='qa-local-model'
            status,_=server.sdk.proxy('GET','/api/sdk/ai/status')
            assert status['running'] is False
            assert not runner.running_ids()
            checks.append('real transport to isolated localhost AI stub; preview never sends to WeChat; auto reply stays off')
            page.screenshot(path=str(OUTPUT/'web-ai-desktop.png'),full_page=True)
            with page.expect_download() as downloading:
                page.locator('#ai-export').click()
            output=Path(sandbox)/'sanitized-export.json'
            downloading.value.save_as(str(output))
            assert 'qa-placeholder-key-never-shared' not in output.read_text(encoding='utf-8')
            checks.append('JSON export excludes credentials')
            page.set_viewport_size({'width':390,'height':844})
            assert page.evaluate('document.documentElement.scrollWidth <= innerWidth'),'AI mobile horizontal overflow'
            page.screenshot(path=str(OUTPUT/'web-ai-mobile.png'),full_page=True)
            checks.append('AI mobile390px layout without horizontal overflow')
            browser.close()
        assert not errors,errors
finally:
    server.stop()
    runner.stop_all()
    stub.shutdown()
    stub.server_close()
    if original is None:os.environ.pop('WLC_SDK_AI_CONFIG_DIR',None)
    else:os.environ['WLC_SDK_AI_CONFIG_DIR']=original
report={'ok':True,'checks':checks,'page_errors':errors,'real_wechat_actions':False,'external_ai_calls':False}
(OUTPUT/'web-features-result.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(report,ensure_ascii=False,indent=2))
