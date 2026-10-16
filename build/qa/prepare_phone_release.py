"""Stage the verified APK and public download metadata, with no credentials."""
from pathlib import Path
import hashlib
import json
import re
import shutil

root=Path(__file__).resolve().parents[2]
apk=root/'build/maui/WeChatLikeMaui/bin/Release/net10.0-android/com.wechatlike.maui-Signed.apk'
record=json.loads((root/'build/qa/evidence/phone-apk-result.json').read_text(encoding='utf-8'))
assert record['ok'] and record['sha256']==hashlib.sha256(apk.read_bytes()).hexdigest()
folder=root/'deploy/cloudbase/public/downloads'
folder.mkdir(exist_ok=True)
filename='wechat-workspace-phone-'+record['version']+'.apk'
shutil.copy2(apk,folder/filename)
record['downloadUrl']='https://jou-d4gq7a3cqf51e6caa-1394316826.tcloudbaseapp.com/wechat-workspace/downloads/'+filename
record['downloadUrl']+='?v='+record['sha256'][:12]
page=root/'deploy/cloudbase/public/phone.html'
html=page.read_text(encoding='utf-8')
html=re.sub(r'href="downloads/wechat-workspace-phone-[0-9.]+\.apk(?:\?v=[^"]+)?"','href="downloads/'+filename+'?v='+record['sha256'][:12]+'"',html)
page.write_text(html,encoding='utf-8')
(folder/'phone-release.json').write_text(json.dumps(record,ensure_ascii=False,indent=2),encoding='utf-8')
phone=root/'deliverables/手机独立版-小米'
phone.mkdir(exist_ok=True)
(phone/'SHA256.txt').write_text(record['sha256']+'  微信工作台-手机独立版.apk\n',encoding='utf-8')
p=root/'build/qa/验证记录.md'
s=p.read_text(encoding='utf-8')
heading='## 小米手机独立版 1.2（2026-10-16）'
section='''## 小米手机独立版 1.2（2026-10-16）

- 安卓默认直接进入 PhonePage，本地配置 AI、处理微信通知及按作者单轮辅助朋友圈；不创建 ConsoleApi 或要求电脑 IP。
- Android Release、Windows 共享页面构建均 0 警告、0 错误。安卓签名与 APK CRC 验证通过，版本 1.2.0 / versionCode 3，通知与辅助服务声明、备份和明文流量关闭已检查。
- 21 项独立规则与模拟兼容 API 检查通过，覆盖端点校验、请求认证、响应解析与大小限制、通知更新拒绝、精确名单匹配。未调用真实 AI 或发送微信。
- 共享原生页面已在 Windows MAUI 渲染器中检查四个页签、无电脑连接表单、密码框初始为空、无效地址拒绝及空消息状态；截图不是安卓真机证明。
- 未连接安卓设备。小米型号及 MIUI / HyperOS 版本未知，真实安装、通知快捷回复是否开放、朋友圈界面树兼容性与后台存活仍待本人手机验证。

'''
if heading not in s:s=s.replace('## 合规声明',section+'## 合规声明')
p.write_text(s,encoding='utf-8')
rules=json.loads((root/'build/qa/phone-rules-tests.log').read_text(encoding='utf-8-sig').strip().splitlines()[-1])
(root/'build/qa/evidence/phone-rules-result.json').write_text(json.dumps(rules,indent=2),encoding='utf-8')
print(json.dumps({'ok':True,'stagedBytes':record['bytes'],'version':record['version'],'credentialsIncluded':False}))
