/* Cloud access belongs to the browser session. No credentials ship in assets. */
(() => {
  'use strict';
  const nativeFetch = window.fetch.bind(window);
  const byId = id => document.getElementById(id);
  const shell = byId('cloud-workspace');
  const login = byId('cloud-login');
  const form = byId('cloud-login-form');
  const code = byId('cloud-access-code');
  const submit = byId('cloud-login-submit');
  const error = byId('cloud-login-error');
  const unknownWrite = '执行状态未知，请先查看任务、日志或智能回复状态，避免重复操作。';
  let base = location.origin, configError = '', token = '', ready = false;
  let nodeOnline = null, checking = false, initialStarted = false;
  const callbacks = [];
  try {
    const value = String(window.WLC_CLOUD_CONFIG?.API_BASE || '').trim();
    if (value) {
      const parsed = new URL(value);
      const local = ['localhost', '127.0.0.1', '[::1]'].includes(parsed.hostname);
      if ((parsed.protocol !== 'https:' && !(local && parsed.protocol === 'http:')) || parsed.username || parsed.password || parsed.search || parsed.hash) {
        throw new Error('后端地址需要使用 HTTPS，且不能包含密码、查询参数或片段。');
      }
      base = parsed.href.replace(/\/+$/, '');
    }
  } catch (e) { configError = '云端连接配置有误：' + e.message; }
  const storageKey = 'wechat-workspace.cloud.browser:' + base;
  const endpoint = path => base + path;
  function stored(value) {
    try { value === undefined ? sessionStorage.removeItem(storageKey) : sessionStorage.setItem(storageKey, value); }
    catch { /* A private browser may disable storage; memory still works. */ }
  }
  function loginError(message) { error.hidden = !message; error.textContent = message; }
  function closeAccess(message = '') {
    ready = false; token = ''; nodeOnline = null; stored();
    shell.inert = true; shell.setAttribute('aria-hidden', 'true'); login.hidden = false;
    code.value = ''; byId('ai-key').value = '';
    byId('cloud-account-state').textContent = '尚未验证访问码';
    loginError(message); code.focus();
  }
  function showNode(data) {
    nodeOnline = Boolean(data.nodeOnline);
    byId('cloud-account-state').textContent = '访问码已验证';
    byId('cloud-node-state').textContent = nodeOnline ? '节点在线' : '节点离线';
    const note = byId('cloud-node-banner');
    note.hidden = false; note.className = 'banner' + (nodeOnline ? '' : ' error');
    let last = '';
    if (!nodeOnline && Number.isFinite(data.lastSeen) && data.lastSeen > 0) {
      last = ' 最近连接：' + new Date(data.lastSeen).toLocaleString('zh-CN') + '。';
    }
    note.textContent = nodeOnline
      ? 'Windows 微信节点已连接。任务、联系人和 AI 设置在该节点执行；打开微信登录后，请在 Windows 电脑上扫码。'
      : '云端控制台可访问，但 Windows 微信节点离线。请在运行微信的 Windows 电脑上启动云连接器，并保持可用的交互桌面。' + last + ' 节点恢复后会自动读取状态，不会自动重试操作。';
    if (!nodeOnline) {
      if (typeof window.connection === 'function') window.connection(false);
      byId('total').textContent = '等待 Windows 节点上线';
      byId('version').textContent = '尚未读取节点版本';
      byId('running').textContent = '—'; byId('uptime').textContent = '—';
      byId('sync').textContent = '节点离线';
      byId('tasks').replaceChildren(Object.assign(document.createElement('div'), {
        className: 'empty', textContent: 'Windows 节点上线后显示可用任务。'
      }));
    }
  }
  async function validate(candidate) {
    const response = await nativeFetch(endpoint('/api/cloud/status'), {
      headers: {Authorization: 'Bearer ' + candidate},
      signal: AbortSignal.timeout(38000), credentials: 'omit', cache: 'no-store', redirect: 'error', referrerPolicy: 'no-referrer'
    });
    let data;
    try { data = await response.json(); }
    catch { throw new Error('云端响应无法读取（' + response.status + '）。请检查后端地址和发布状态。'); }
    if (!response.ok || data.ok !== true) {
      if ([401, 403].includes(response.status)) throw new Error('云访问码不正确或已失效，请填写浏览器访问码。');
      throw new Error(data.message || '暂时无法进入云端（' + response.status + '）。');
    }
    if (typeof data.nodeOnline !== 'boolean') throw new Error('后端没有返回节点状态，请检查云端版本。');
    return data;
  }
  async function enter(candidate) {
    if (checking) return;
    if (configError) { loginError(configError); return; }
    checking = true; submit.disabled = true; form.setAttribute('aria-busy', 'true');
    submit.textContent = '正在验证…'; loginError('');
    try {
      const data = await validate(candidate);
      token = candidate; ready = true; stored(candidate); code.value = '';
      login.hidden = true; shell.inert = false; shell.removeAttribute('aria-hidden');
      showNode(data);
      byId('page-title').tabIndex = -1; byId('page-title').focus({preventScroll: true});
      if (!initialStarted) { initialStarted = true; callbacks.forEach(callback => callback()); }
      else if (typeof window.refresh === 'function') { window.refresh(); }
    } catch (e) {
      stored(); loginError(e.name === 'TypeError'
        ? '无法连接云端，请检查网络、后端地址以及跨域允许来源配置。' : e.message);
    } finally {
      checking = false; submit.disabled = false; submit.textContent = '进入工作台'; form.removeAttribute('aria-busy');
    }
  }
  function apiPath(input) {
    const source = typeof input === 'string' || input instanceof URL ? String(input) : input.url;
    const url = new URL(source, location.href);
    if (url.origin === location.origin && url.pathname.startsWith('/api/')) return url.pathname + url.search;
    const prefix = base + '/api/';
    if (url.href.startsWith(prefix)) return url.href.slice(base.length);
    return null;
  }
  window.fetch = async (input, init = {}) => {
    const path = apiPath(input);
    if (path === null) return nativeFetch(input, init);
    if (!ready || !token) throw new Error('请先输入云访问码进入工作台。');
    const request = new Request(input, init), method = request.method.toUpperCase();
    const writing = !['GET', 'HEAD', 'OPTIONS'].includes(method);
    const headers = new Headers(request.headers);
    headers.set('Authorization', 'Bearer ' + token);
    if (writing) {
      if (!crypto.randomUUID) throw new Error('浏览器需要 HTTPS 才能提交操作，请使用已发布的安全网址。');
      headers.set('X-Idempotency-Key', crypto.randomUUID());
    }
    try {
      // Buffer the small dashboard JSON body so browsers send a regular request,
      // rather than a streamed body that fails under some HTTP gateways.
      const body = ['GET', 'HEAD'].includes(method) ? undefined : await request.arrayBuffer();
      const response = await nativeFetch(endpoint(path), {
        method, body, signal: request.signal, headers, credentials: 'omit', cache: 'no-store', redirect: 'error', referrerPolicy: 'no-referrer'
      });
      if ([401, 403].includes(response.status)) closeAccess('云访问码已失效，请重新输入。');
      return response;
    } catch (e) {
      if (!writing) throw new Error('云端请求未完成，请检查网络连接。');
      throw new Error('操作请求未完成。' + unknownWrite);
    }
  };
  async function api(path, body, timeout = 38000) {
    const writing = body !== undefined;
    const response = await fetch(path, {
      method: writing ? 'POST' : 'GET', headers: writing ? {'Content-Type': 'application/json'} : {},
      body: writing ? JSON.stringify(body) : undefined, signal: AbortSignal.timeout(timeout)
    });
    let data;
    try { data = await response.json(); }
    catch { throw new Error('服务响应无法读取（' + response.status + '）。' + (writing ? unknownWrite : '')); }
    if (!response.ok || data.ok !== true) {
      if (data.code === 'NODE_OFFLINE') showNode({nodeOnline: false});
      const uncertain = writing && data.code !== 'COMMAND_EXPIRED' && (
        [502, 504].includes(response.status) || ['REQUEST_TIMEOUT', 'RESULT_UNKNOWN', 'EXECUTION_UNCERTAIN', 'BRIDGE_UNAVAILABLE', 'RESULT_EXPIRED'].includes(data.code)
      );
      const expired = writing && data.code === 'COMMAND_EXPIRED' ? '。此请求排队过期，未执行；确认当前状态后可重新提交。' : '';
      throw new Error((data.message || '请求失败（' + response.status + '）') + (uncertain ? '。' + unknownWrite : expired));
    }
    if (path === '/api/status' && nodeOnline !== true) showNode({nodeOnline: true});
    return data;
  }
  window.WlcCloud = Object.freeze({api, isReady: () => ready, onReady: callback => {
    callbacks.push(callback); if (ready) callback();
  }});
  form.onsubmit = event => { event.preventDefault(); const value = code.value.trim(); if (value) enter(value); };
  login.addEventListener('keydown', event => {
    if (event.key !== 'Tab') return;
    const focusable = [...login.querySelectorAll('input,button')].filter(item => !item.disabled);
    const first = focusable[0], last = focusable[focusable.length - 1];
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
  });
  byId('cloud-logout').onclick = () => { closeAccess(); location.reload(); };
  setInterval(async () => {
    if (!ready || document.hidden || checking) return;
    checking = true;
    try { showNode(await api('/api/cloud/status')); }
    catch (e) {
      if (ready) {
        byId('cloud-account-state').textContent = '云端连接暂不可用';
        byId('cloud-node-state').textContent = '状态待确认';
        byId('cloud-node-banner').hidden = false;
        byId('cloud-node-banner').className = 'banner error';
        byId('cloud-node-banner').textContent = '暂时无法确认云端或 Windows 节点状态。' + e.message + ' 状态查询恢复后会自动更新。';
      }
    } finally { checking = false; }
  }, 5000);
  if (configError) loginError(configError);
  else {
    let previous = '';
    try { previous = sessionStorage.getItem(storageKey) || ''; } catch {}
    if (previous) enter(previous);
    else code.focus();
  }
})();
