"""Record the real Visual Studio debug session without changing its UI."""
import datetime
import json
from pathlib import Path
import pythoncom
import win32com.client

root = Path(__file__).resolve().parent.parent.parent
rot = pythoncom.GetRunningObjectTable()
ctx = pythoncom.CreateBindCtx(0)
results = []
for moniker in rot:
    name = moniker.GetDisplayName(ctx, None)
    if 'VisualStudio.DTE' not in name:
        continue
    dte = win32com.client.Dispatch(rot.GetObject(moniker).QueryInterface(pythoncom.IID_IDispatch))
    solution = str(dte.Solution.FullName)
    if Path(solution).name != 'WeChatWorkspace.Windows.slnx':
        continue
    results.append({
        'rot': name,
        'solution': solution,
        'version': str(dte.Version),
        'debug_mode': int(dte.Debugger.CurrentMode),
        'build_state': int(dte.Solution.SolutionBuild.BuildState),
        'last_build_info': int(dte.Solution.SolutionBuild.LastBuildInfo),
        'debug_processes': [{'name': str(p.Name), 'pid': int(p.ProcessID)} for p in dte.Debugger.DebuggedProcesses],
    })
passed = any(x['debug_mode'] == 3 and any(Path(p['name']).name == 'WeChatLikeMaui.exe' for p in x['debug_processes']) for x in results)
record = {'ok': passed, 'checked_at': datetime.datetime.now().astimezone().isoformat(), 'sessions': results}
target = root / 'build/qa/evidence/visual-studio-running.json'
target.write_text(json.dumps(record, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(record, ensure_ascii=False, indent=2))
raise SystemExit(0 if passed else 1)
