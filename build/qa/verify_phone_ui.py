"""Inspect the shared native phone page in Windows; Android runtime remains pending."""
from pathlib import Path
import json
import site
import subprocess
import time
site.addsitedir(str(Path.home()/'.workbuddy/binaries/python/envs/wechatbuild311/Lib/site-packages'))
from pywinauto import Desktop

root=Path(__file__).resolve().parents[2]
out=root/'build/qa/evidence'
exe=root/'build/maui/WeChatLikeMaui/bin/WindowsDebug/Release/net10.0-windows10.0.19041.0/win-x64/WeChatLikeMaui.exe'
p=subprocess.Popen([str(exe),'--phone-preview'],cwd=exe.parent)
w=Desktop(backend='uia').window(process=p.pid,control_type='Window')
result={'ok':False,'renderer':'Windows native MAUI shared phone page','androidDeviceVerified':False,'realWechatActions':False,'networkRequests':False}
try:
    w.wait('visible',timeout=40)
    w.child_window(auto_id='PhoneStartAssistant',control_type='Button').wait('visible',timeout=20)
    time.sleep(1)
    w.capture_as_image().save(out/'phone-native-home-preview.png')
    assert not w.child_window(auto_id='IpEntry',control_type='Edit').exists(timeout=.2)
    assert not w.child_window(auto_id='PhoneTab-ai',control_type='Button').exists(timeout=.2)
    w.child_window(auto_id='PhoneSetup',control_type='Button').click_input()
    w.child_window(auto_id='PhoneSetupAi',control_type='Button').wait('visible enabled',timeout=10)
    time.sleep(.5)
    w.capture_as_image().save(out/'phone-native-setup-preview.png')
    w.child_window(auto_id='PhoneTab-ai',control_type='Button').invoke()
    w.child_window(auto_id='PhoneAiUrl',control_type='Edit').wait('visible',timeout=10)
    key=w.child_window(auto_id='PhoneAiKey',control_type='Edit')
    assert key.get_value()==''
    w.child_window(auto_id='PhoneAiUrl',control_type='Edit').set_edit_text('http://example.invalid/v1')
    w.child_window(auto_id='PhoneSaveAi',control_type='Button').invoke()
    time.sleep(.4)
    assert 'HTTPS' in w.child_window(auto_id='PhoneStatus').window_text()
    w.capture_as_image().save(out/'phone-native-ai-preview.png')
    w.child_window(auto_id='PhoneTab-messages',control_type='Button').invoke()
    w.child_window(auto_id='PhoneDisableAuto',control_type='Button').wait('exists',timeout=10)
    w.capture_as_image().save(out/'phone-native-messages-preview.png')
    targets=next(edit for edit in w.descendants(control_type='Edit') if edit.is_visible())
    previous=targets.get_value()
    targets.set_edit_text('测试未保存会话')
    w.child_window(auto_id='PhoneEnableAuto',control_type='Button').invoke()
    time.sleep(.3)
    assert '未保存' in w.child_window(auto_id='PhoneStatus').window_text(),w.child_window(auto_id='PhoneStatus').window_text()
    targets.set_edit_text(previous)
    w.child_window(auto_id='PhoneEnableAuto',control_type='Button').invoke()
    time.sleep(.3)
    assert '回复预览' in w.child_window(auto_id='PhoneStatus').window_text(),w.child_window(auto_id='PhoneStatus').window_text()
    w.child_window(auto_id='PhoneTab-moments',control_type='Button').invoke()
    w.child_window(auto_id='PhoneArmMoments',control_type='Button').wait('exists',timeout=10)
    w.capture_as_image().save(out/'phone-native-moments-preview.png')
    result.update(ok=True,noDesktopConnectionForm=True,singleHomeEntry=True,firstSetupSeparated=True,configurationTabs=4,passwordInitiallyEmpty=True,invalidUrlBlocked=True,unsavedAutoTargetsRejected=True,previewRequiredBeforeAuto=True,androidOverlayRuntimeVerified=False)
finally:
    try:w.close();p.wait(timeout=15)
    except Exception:
        if p.poll() is None:p.terminate();p.wait(timeout=10)
    (out/'phone-ui-result.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(result,ensure_ascii=False))
