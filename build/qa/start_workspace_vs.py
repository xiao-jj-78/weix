"""Start debugging only the requested workspace solution in Visual Studio."""
from pathlib import Path
import time
import pythoncom
import win32com.client

root = Path(__file__).resolve().parents[2]
for attempt in range(30):
    rot = pythoncom.GetRunningObjectTable()
    ctx = pythoncom.CreateBindCtx(0)
    found = None
    for moniker in rot:
        if 'VisualStudio.DTE' not in moniker.GetDisplayName(ctx,None): continue
        try:
            dte = win32com.client.Dispatch(rot.GetObject(moniker).QueryInterface(pythoncom.IID_IDispatch))
            if Path(str(dte.Solution.FullName)).resolve() == (root/'WeChatWorkspace.Windows.slnx').resolve():
                found = dte
                break
        except pythoncom.com_error: continue
    if found:
        if int(found.Debugger.CurrentMode) == 1:
            found.ExecuteCommand('Debug.Start')
        print('Visual Studio debug start requested for the workspace solution.')
        break
    time.sleep(2)
else: raise RuntimeError('The workspace solution is not ready in Visual Studio')
