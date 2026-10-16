"""Receive a key via non-echoing stdin; persist only using the local DPAPI vault."""
from pathlib import Path
from urllib.request import Request, build_opener, HTTPRedirectHandler, ProxyHandler
from urllib.error import HTTPError, URLError
import getpass
import json
import subprocess
import time

ROOT=Path(__file__).resolve().parents[2]
API='https://apihub.agnes-ai.com/v1'
HOST='http://127.0.0.1:18767'
class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self,*args):return None
local=build_opener(ProxyHandler({}),NoRedirect())
network=build_opener(NoRedirect())
def local_request(path,payload=None):
    request=Request(HOST+path,data=None if payload is None else json.dumps(payload,ensure_ascii=False).encode(),headers={'Content-Type':'application/json'})
    with local.open(request,timeout=5) as response:
        data=json.load(response)
    if not data.get('ok'):raise RuntimeError('Local SDK configuration was rejected')
    return data

key=getpass.getpass('Agnes API key (hidden): ').strip()
if not key or any(c.isspace() for c in key):raise SystemExit('Invalid key input; no key was stored')
result={'baseUrl':API,'modelsHttpStatus':None,'availableModels':[],'profileStored':False,'autoReplyEnabled':False,'previewSent':False,'credentialReturned':False}
try:
    request=Request(API+'/models',headers={'Authorization':'Bearer '+key})
    with network.open(request,timeout=20) as response:
        result['modelsHttpStatus']=response.status
        body=json.loads(response.read(1024*1024))
        result['availableModels']=[item['id'] for item in body.get('data',[]) if isinstance(item,dict) and isinstance(item.get('id'),str)][:100]
except HTTPError as error:result['modelsHttpStatus']=error.code
except (URLError,TimeoutError,OSError,ValueError):result['modelsHttpStatus']='connection-or-format-error'
available=result['availableModels']
model=next((name for name in ('agnes-3.0-flash','agnes-2.5-flash','agnes-2.0-flash','agnes-1.5-flash') if name in available),'')
result['configuredModel']=model
process=subprocess.Popen([str(ROOT/'build/sdk/publish/WeChatSdkHost.exe'),'--port','18767'],cwd=ROOT/'build/sdk/publish',stdin=subprocess.DEVNULL,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0))
try:
    for _ in range(40):
        try:
            current=local_request('/api/sdk/ai/config')['config']
            break
        except Exception:
            if process.poll() is not None:raise RuntimeError('SDK helper exited before configuration')
            time.sleep(.25)
    else:raise RuntimeError('SDK helper did not become ready')
    profiles=[{k:v for k,v in p.items() if k!='hasApiKey'} for p in current['profiles'] if p['id']!='agnes-ai']
    profiles.append({'id':'agnes-ai','name':'Agnes AI','protocol':'openai-compatible','baseUrl':API,'model':model,'apiKeyEnv':'WLC_AGNES_API_KEY','apiKey':key})
    saved=local_request('/api/sdk/ai/config',{'activeProfileId':'agnes-ai','profiles':profiles})
    key=''
    result['profileStored']=any(p['id']=='agnes-ai' and p['hasApiKey'] for p in saved['config']['profiles'])
    result['autoReplyEnabled']=local_request('/api/sdk/ai/status')['running']
finally:
    key=''
    try:local_request('/api/sdk/host/stop',{})
    except Exception:pass
    try:process.wait(timeout=10)
    except subprocess.TimeoutExpired:
        process.terminate()
        process.wait(timeout=5)
output=ROOT/'build/qa/evidence/agnes-connection-result.json'
output.parent.mkdir(parents=True,exist_ok=True)
output.write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(result,ensure_ascii=False))
