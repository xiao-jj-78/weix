"""Verify the signed independent Android artifact without claiming device QA."""
from pathlib import Path
import hashlib
import json
import re
import subprocess
import zipfile

root=Path(__file__).resolve().parents[2]
apk=root/'build/maui/WeChatLikeMaui/bin/Release/net10.0-android/com.wechatlike.maui-Signed.apk'
sdk=Path.home()/'AppData/Local/Android/Sdk/build-tools/36.0.0'
java=Path('C:/Program Files/Microsoft/jdk-17.0.20.8-hotspot/bin/java.exe')
signature=subprocess.run([str(java),'-jar',str(sdk/'lib/apksigner.jar'),'verify',str(apk)],capture_output=True,text=True)
assert signature.returncode==0,'APK signature verification failed'
manifest=subprocess.run([str(sdk/'aapt2.exe'),'dump','xmltree','--file','AndroidManifest.xml',str(apk)],capture_output=True,text=True,encoding='utf-8',check=True).stdout
for expected in ['com.wechatlike.maui.PhoneNotificationService','com.wechatlike.maui.PhoneMomentsService','com.wechatlike.maui.PhoneOverlayService','android.permission.BIND_NOTIFICATION_LISTENER_SERVICE','android.permission.BIND_ACCESSIBILITY_SERVICE','android.permission.SYSTEM_ALERT_WINDOW','android.permission.FOREGROUND_SERVICE_SPECIAL_USE','android.app.PROPERTY_SPECIAL_USE_FGS_SUBTYPE','android.accessibilityservice','1.4.0']:
    assert expected in manifest,expected
assert re.search(r'allowBackup\([^\n]+\)=false',manifest)
assert re.search(r'usesCleartextTraffic\([^\n]+\)=false',manifest)
with zipfile.ZipFile(apk) as archive:
    assert archive.testzip() is None
    assert 'res/xml/phone_accessibility.xml' in archive.namelist()
certificate=subprocess.run([str(java),'-jar',str(sdk/'lib/apksigner.jar'),'verify','--print-certs',str(apk)],capture_output=True,text=True,check=True).stdout
old=root/'deploy/cloudbase/public/downloads/wechat-workspace-phone-1.2.0.apk'
same_signer=None
if old.is_file():
    previous=subprocess.run([str(java),'-jar',str(sdk/'lib/apksigner.jar'),'verify','--print-certs',str(old)],capture_output=True,text=True,check=True).stdout
    same_signer=re.search(r'Signer #1 certificate SHA-256 digest: (\w+)',certificate).group(1)==re.search(r'Signer #1 certificate SHA-256 digest: (\w+)',previous).group(1)
    assert same_signer,'Upgrade signing identity changed'
record={'ok':True,'version':'1.4.0','versionCode':5,'signatureVerified':True,'sameSignerAs12':same_signer,'notificationServiceDeclared':True,'accessibilityServiceDeclared':True,'overlayForegroundServiceDeclared':True,'androidDeviceVerified':False,'androidOverlayRuntimeVerified':False,'automaticMomentsRuntimeVerified':False,'friendRecognitionRuntimeVerified':False,'realWechatActions':False,'bytes':apk.stat().st_size,'sha256':hashlib.sha256(apk.read_bytes()).hexdigest()}
(root/'build/qa/evidence/phone-apk-result.json').write_text(json.dumps(record,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(record,ensure_ascii=False))
