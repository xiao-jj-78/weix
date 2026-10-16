"""Multi-WeChat windows: passive discovery, explicit official-window actions."""
NAV = '<button role="tab" id="tab-clients" aria-controls="view-clients" aria-selected="false" data-view="clients"><span>07</span>多微信管理</button>'
PANEL = '''<section id="view-clients" role="tabpanel" aria-labelledby="tab-clients" hidden>
<div class="section-intro"><h2>多微信窗口与账号</h2><p class="sub">复用官方保留的登录状态。窗口存在不代表已登录；同一进程内的账号以 SDK 实际读取结果为准。</p></div>
<div class="panel"><div class="panel-head"><h2 id="clients-count">尚未读取窗口</h2><div class="actions"><button id="clients-refresh">刷新窗口</button><button id="clients-start">启动 SDK</button><button id="clients-open">打开官方微信</button><button id="clients-verify">读取已登录账号</button></div></div>
<div class="panel-body"><p id="clients-status" class="source-note" role="status">先连接电脑端。读取账号可能切换微信页面，登录和手机确认请在对应 Windows 桌面完成。</p><div id="clients-list" class="provider-grid"><div class="empty">尚未发现微信窗口。</div></div></div></div>
<p class="source-note">窗口备注仅标记本次进程，重启后请重新核对。打开官方微信会请求客户端启动，不能保证强制多开。Windows 原生版的“云端与多微信”还支持内置浏览器和本机伴随排列。</p>
<dialog id="client-alias-dialog" style="max-width:420px;width:90%;border:1px solid #dce5e2;border-radius:12px;color:#132c36"><form id="client-alias-form"><h2>窗口备注</h2><p id="client-alias-caption" class="sub"></p><label class="label" for="client-alias-name">备注 · 最多60字</label><input id="client-alias-name" maxlength="60" style="width:100%"><p id="client-alias-error" role="alert"></p><div class="actions"><button type="submit" class="primary" id="client-alias-save">保存备注</button><button type="button" id="client-alias-cancel">取消</button></div></form></dialog>
</section>'''
SCRIPT = r'''<script>(()=>{
let clientBusy=false,aliasId=null;const list=$('clients-list'),message=$('clients-status');
const controls=()=>['clients-refresh','clients-start','clients-open','clients-verify'].map(id=>$(id));
function renderClients(r){list.replaceChildren();$('clients-count').textContent=r.clients.length+' 个微信窗口';for(const c of r.clients){const card=el('div',undefined,'panel'),body=el('div',undefined,'panel-body');body.append(el('h3',c.alias||'微信 · PID '+c.pid),el('p',c.name+' '+(c.version||'')+' · PID '+c.pid,'sub'));body.append(el('p',c.account?'上次 SDK 读到：'+(c.account.nickname||c.account.name||'')+' · '+new Date(c.verifiedAt).toLocaleString('zh-CN'):'登录尚未验证','source-note'));const actions=el('div',undefined,'actions'),focus=el('button','显示窗口'),alias=el('button','备注');focus.onclick=()=>act(()=>api('/api/sdk/clients/focus',{instanceId:c.instanceId}));alias.onclick=()=>{aliasId=c.instanceId;$('client-alias-name').value=c.alias||'';$('client-alias-caption').textContent='PID '+c.pid+' · 仅标记本次进程';$('client-alias-error').textContent='';$('client-alias-dialog').showModal();$('client-alias-name').focus();};actions.append(focus,alias);body.append(actions);card.append(body);list.append(card);}if(!r.clients.length)list.append(el('div','尚未发现有主窗口的微信，可点击“打开官方微信”。','empty'));}
async function readClients(){const r=await api('/api/sdk/clients');renderClients(r);return r;}
async function act(work){if(clientBusy)return;clientBusy=true;controls().forEach(b=>b.disabled=true);message.textContent='正在处理…';try{const r=await work();message.textContent=r.message||'窗口列表已刷新。';await readClients();}catch(e){message.textContent='操作未完成：'+e.message;}finally{clientBusy=false;controls().forEach(b=>b.disabled=false);}}
$('clients-refresh').onclick=()=>act(()=>api('/api/sdk/clients'));$('clients-start').onclick=()=>act(()=>api('/api/sdk/start',{}));$('clients-open').onclick=()=>act(()=>api('/api/sdk/clients/open',{}));$('clients-verify').onclick=()=>act(()=>api('/api/sdk/clients/verify',{}));
$('tab-clients').addEventListener('click',()=>{document.querySelector('.top .sub').textContent='管理运行在 Windows 节点上的官方微信窗口。';act(()=>api('/api/sdk/clients'));});
$('client-alias-cancel').onclick=()=>$('client-alias-dialog').close();$('client-alias-form').onsubmit=async e=>{e.preventDefault();if(clientBusy||!aliasId)return;clientBusy=true;$('client-alias-save').disabled=true;try{const r=await api('/api/sdk/clients/alias',{instanceId:aliasId,alias:$('client-alias-name').value.trim()});message.textContent=r.message;$('client-alias-dialog').close();await readClients();}catch(error){$('client-alias-error').textContent=error.message;}finally{clientBusy=false;$('client-alias-save').disabled=false;}};
setInterval(()=>{if(currentView==='clients'&&connected&&!clientBusy&&!document.hidden&&!$('client-alias-dialog').open)act(()=>api('/api/sdk/clients'));},10000);
if(new URLSearchParams(location.search).get('view')==='clients'){if(window.WlcCloud)window.WlcCloud.onReady(()=>$('tab-clients').click());else $('tab-clients').click();}
})();</script>'''

def add_clients(page):
    style = '<style>@media(max-width:650px){#view-clients .panel-head{flex-direction:column;align-items:stretch;gap:14px}#clients-count{font-size:17px}#view-clients .panel-head .actions{justify-content:flex-start}}</style>'
    return (page.replace('</head>', style + '</head>', 1).replace('</nav>', NAV + '</nav>', 1)
            .replace('<footer class="footer">', PANEL + '<footer class="footer">', 1)
            .replace("path==='/api/sdk/plugins/run'?115000", "path==='/api/sdk/plugins/run'||path==='/api/sdk/clients/verify'?115000")
            .replace('</body>', SCRIPT + '</body>'))
