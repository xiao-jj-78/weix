import ctypes, json, pathlib, sys, win32gui, win32process, win32ui
from PIL import Image
ctypes.windll.user32.SetProcessDPIAware()
base=pathlib.Path(__file__).resolve().parent
pid=int(sys.argv[1]); label=sys.argv[2]
w=[]
def enum(hwnd,arg):
    _,p=win32process.GetWindowThreadProcessId(hwnd)
    if p==pid and win32gui.IsWindowVisible(hwnd) and win32gui.GetWindowText(hwnd):
        w.append((hwnd,win32gui.GetWindowText(hwnd),win32gui.GetWindowRect(hwnd)))
win32gui.EnumWindows(enum,None)
result=[]
for idx,(hwnd,title,rect) in enumerate(w):
    width,height=rect[2]-rect[0],rect[3]-rect[1]
    if width<50 or height<50: continue
    dc=win32gui.GetWindowDC(hwnd); src=win32ui.CreateDCFromHandle(dc); dst=src.CreateCompatibleDC(); bmp=win32ui.CreateBitmap(); bmp.CreateCompatibleBitmap(src,width,height); dst.SelectObject(bmp)
    ok=ctypes.windll.user32.PrintWindow(hwnd,dst.GetSafeHdc(),2)
    bits=bmp.GetBitmapBits(True)
    img=Image.frombuffer('RGB',(width,height),bits,'raw','BGRX',0,1)
    path=base/f'VS-{label}-{idx}.png'; img.save(path)
    result.append({'title':title,'hwnd':hwnd,'rect':rect,'PrintWindow':ok,'path':str(path)})
    win32gui.DeleteObject(bmp.GetHandle()); dst.DeleteDC(); src.DeleteDC(); win32gui.ReleaseDC(hwnd,dc)
(base/f'VS-{label}-windows.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(result,ensure_ascii=False,indent=2))
