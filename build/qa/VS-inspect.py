import json, pathlib, sys, pythoncom, win32com.client
base = pathlib.Path(__file__).resolve().parent
rot = pythoncom.GetRunningObjectTable()
ctx = pythoncom.CreateBindCtx(0)
enum = rot.EnumRunning()
dte = None
names = []
while (items := enum.Next()):
    name = items[0].GetDisplayName(ctx, None)
    if 'VisualStudio.DTE' in name:
        names.append(name)
        candidate = win32com.client.Dispatch(rot.GetObject(items[0]).QueryInterface(pythoncom.IID_IDispatch))
        if str(candidate.Solution.FullName).endswith('WeChatWorkspace.slnx'):
            dte = candidate
if dte is None:
    raise RuntimeError(f'Expected open WeChatWorkspace.slnx; ROT={names}')
def safe(fn):
    try: return fn()
    except Exception as exc: return {'error':str(exc)}
def project(p):
    return {'name':safe(lambda:p.Name), 'kind':safe(lambda:p.Kind),'unique':safe(lambda:p.UniqueName),'file':safe(lambda:p.FullName),
      'subprojects':safe(lambda:[project(x.SubProject) for x in p.ProjectItems if x.SubProject is not None])}
result = {
  'rot':names,'version':dte.Version,'solution':dte.Solution.FullName,
  'configuration':safe(lambda:dte.Solution.SolutionBuild.ActiveConfiguration.Name),
  'startup':safe(lambda:list(dte.Solution.SolutionBuild.StartupProjects)),
  'build_state':safe(lambda:dte.Solution.SolutionBuild.BuildState),
  'last_build_info':safe(lambda:dte.Solution.SolutionBuild.LastBuildInfo),
  'debug_mode':safe(lambda:dte.Debugger.CurrentMode),
  'debug_processes':safe(lambda:[{'name':p.Name,'pid':p.ProcessID} for p in dte.Debugger.DebuggedProcesses]),
  'projects':[project(p) for p in dte.Solution.Projects],
  'active_document':safe(lambda:dte.ActiveDocument.FullName),
  'panes':safe(lambda:[{'name':p.Name,'text':p.TextDocument.StartPoint.CreateEditPoint().GetText(p.TextDocument.EndPoint)} for p in dte.ToolWindows.OutputWindow.OutputWindowPanes]),
}
text = json.dumps(result,ensure_ascii=False,indent=2)
(base/'VS-inspect.json').write_text(text,encoding='utf-8')
print(text)
