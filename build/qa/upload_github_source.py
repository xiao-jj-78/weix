"""Upload a reviewed source tree to the authorized empty repository via gh API.

Uses the existing gh credential helper. No token is read or placed in files.
The ref is advanced only after every blob and the complete tree have been checked.
"""
from pathlib import Path
import base64
import json
import subprocess
import tempfile
import time

root=Path(__file__).resolve().parents[2]
checkout=root/'build/github/weix'
repo='xiao-jj-78/weix'
receipt_path=checkout/'.git/github-upload.json'

def git(*args):
    result=subprocess.run(['git',*args],cwd=checkout,capture_output=True,check=True)
    return result.stdout

def api(path,method='GET',body=None,allow_missing=False):
    for attempt in range(3):
        with tempfile.TemporaryDirectory(prefix='weix-api-') as folder:
            command=['gh','api',path,'--method',method]
            if body is not None:
                payload=Path(folder)/'payload.json';payload.write_text(json.dumps(body,ensure_ascii=False),encoding='utf-8')
                command+=['--input',str(payload)]
            result=subprocess.run(command,capture_output=True)
        if result.returncode==0:return json.loads(result.stdout)
        if allow_missing and (b'HTTP 404' in result.stderr or b'HTTP 409' in result.stderr):return None
        if attempt<2:time.sleep(2)
    raise RuntimeError('GitHub API request failed: '+path+'; response content withheld')

assert (checkout/'README.md').is_file() and (root/'build/qa/evidence/github-source-preflight.json').is_file()
if not (checkout/'.git').exists():
    subprocess.run(['git','init','-b','main',str(checkout)],capture_output=True,check=True)
    git('config','core.autocrlf','false')
    git('config','http.proxy','')
    git('config','credential.helper','!gh auth git-credential')
    git('remote','add','origin','https://github.com/'+repo+'.git')
    user=api('user')
    git('config','user.name',user['login'])
    git('config','user.email',str(user['id'])+'+'+user['login']+'@users.noreply.github.com')
    git('add','--all')
    git('commit','-m','Add WeChat workspace source, friend picker and pyquan-like review')

assert not git('status','--porcelain'),'Export was modified after its reviewed commit'
tree_sha=git('rev-parse','HEAD^{tree}').decode().strip()
entries=[]
for line in git('ls-tree','-r','-z','HEAD').split(b'\0'):
    if not line:continue
    metadata,path=line.split(b'\t',1);mode,kind,sha=metadata.decode().split(' ')
    assert kind=='blob','Submodules are not allowed in the export'
    name=path.decode('utf-8')
    assert not name.endswith(('.apk','.zip','.pfx','.keystore','.jks'))
    entries.append((name,mode,sha))

receipt=json.loads(receipt_path.read_text(encoding='utf-8')) if receipt_path.exists() else {}
current=api('repos/'+repo+'/git/ref/heads/main',allow_missing=True)
if current is not None:
    assert current['object']['sha'] in {receipt.get('initialCommit'),receipt.get('commit')},'Remote has unrelated work; no ref will be overwritten'
    if current['object']['sha']==receipt.get('commit'):
        commit=api('repos/'+repo+'/git/commits/'+receipt['commit'])
        assert commit['tree']['sha']==tree_sha
        print(json.dumps(receipt,ensure_ascii=False));raise SystemExit(0)
    initial=receipt['initialCommit']
else:
    assert api('repos/'+repo)['size']==0,'Repository is not empty'
    body={'message':'Initialize reviewed WeChat workspace source','content':base64.b64encode((checkout/'README.md').read_bytes()).decode(),'branch':'main'}
    initial=api('repos/'+repo+'/contents/README.md','PUT',body)['commit']['sha']
    receipt={'initialCommit':initial,'repo':repo,'sourceTree':tree_sha}
    receipt_path.write_text(json.dumps(receipt,indent=2),encoding='utf-8')

tree=[];binary_count=0
for name,mode,sha in entries:
    data=git('cat-file','blob',sha)
    try:
        content=data.decode('utf-8')
        if '\0' in content:raise UnicodeDecodeError('utf-8',data,0,1,'binary')
        tree.append({'path':name,'mode':mode,'type':'blob','content':content})
    except UnicodeDecodeError:
        blob=api('repos/'+repo+'/git/blobs','POST',{'content':base64.b64encode(data).decode(),'encoding':'base64'})
        assert blob['sha']==sha,'Binary hash mismatch'
        tree.append({'path':name,'mode':mode,'type':'blob','sha':sha});binary_count+=1

remote_tree=api('repos/'+repo+'/git/trees','POST',{'tree':tree})
assert remote_tree['sha']==tree_sha,'Remote tree differs from local reviewed source'
parent=api('repos/'+repo+'/git/ref/heads/main')['object']['sha']
assert parent==initial,'Remote changed while uploading; no ref will be overwritten'
commit=api('repos/'+repo+'/git/commits','POST',{'message':'Add MAUI workspace 1.4, visible friend picker and pyquan-like source review','tree':tree_sha,'parents':[initial]})
api('repos/'+repo+'/git/refs/heads/main','PATCH',{'sha':commit['sha'],'force':False})
receipt.update(commit=commit['sha'],url='https://github.com/'+repo,files=len(entries),binaryBlobs=binary_count,remoteTreeMatchesLocal=True)
receipt_path.write_text(json.dumps(receipt,ensure_ascii=False,indent=2),encoding='utf-8')

assert api('repos/'+repo+'/git/ref/heads/main')['object']['sha']==commit['sha']
remote_files=api('repos/'+repo+'/git/trees/'+tree_sha+'?recursive=1')
assert not remote_files.get('truncated')
actual={f['path']:f['sha'] for f in remote_files['tree'] if f['type']=='blob'}
assert actual=={name:sha for name,mode,sha in entries},'Published file list mismatch'
(root/'build/qa/evidence/github-upload-result.json').write_text(json.dumps(receipt,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(receipt,ensure_ascii=False))
