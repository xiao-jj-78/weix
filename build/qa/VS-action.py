import json, pathlib, sys, time, pythoncom, win32com.client
base = pathlib.Path(__file__).resolve().parent
rot = pythoncom.GetRunningObjectTable()
ctx = pythoncom.CreateBindCtx(0)
enum = rot.EnumRunning()
dte = None
while (items := enum.Next()):
    if 'VisualStudio.DTE' in items[0].GetDisplayName(ctx,None):
        candidate = win32com.client.Dispatch(rot.GetObject(items[0]).QueryInterface(pythoncom.IID_IDispatch))
        if str(candidate.Solution.FullName).endswith('WeChatWorkspace.slnx'):
            dte = candidate
if dte is None: raise RuntimeError('WeChatWorkspace.slnx is not open in Visual Studio')
def projects(collection):
    for p in collection:
        if str(p.Kind)=='{66A26720-8FB5-11D2-AA7E-00C04F688DDE}':
            yield from projects([x.SubProject for x in p.ProjectItems if x.SubProject is not None])
        else: yield p
def safe(fn):
    try: return fn()
    except Exception as ex: return {'error':str(ex)}
def output():
    obj=dte.Windows.Item('{34E76E81-EE4A-11D0-AE2E-00A0C90FFFC3}').Object
    return [{'name':p.Name,'text':safe(lambda:p.TextDocument.StartPoint.CreateEditPoint().GetText(p.TextDocument.EndPoint))} for p in obj.OutputWindowPanes if p.Name in ('Build','Debug','生成','调试','.NET MAUI')]
action=sys.argv[1]
if action=='properties':
    p=next(p for p in projects(dte.Solution.Projects) if p.Name=='WeChatLikeMaui')
    result={ 'properties':safe(lambda:[{'name':x.Name,'value':safe(lambda:x.Value)} for x in p.Properties]),
      'configuration_properties':safe(lambda:[{'name':x.Name,'value':safe(lambda:x.Value)} for x in p.ConfigurationManager.ActiveConfiguration.Properties]),
      'solution_configurations':safe(lambda:[{'name':x.Name,'platform':x.PlatformName} for x in dte.Solution.SolutionBuild.SolutionConfigurations]),
      'output':safe(output)}
elif action=='build':
    p=next(p for p in projects(dte.Solution.Projects) if p.Name=='WeChatLikeMaui')
    dte.Solution.SolutionBuild.StartupProjects=[p.UniqueName]
    started=time.time()
    dte.Solution.SolutionBuild.BuildProject('Debug',p.UniqueName,True)
    result={'action':'BuildProject','unique_name':p.UniqueName,'duration_seconds':round(time.time()-started,2),'build_state':dte.Solution.SolutionBuild.BuildState,'last_build_info':dte.Solution.SolutionBuild.LastBuildInfo,'output':safe(output)}
elif action=='debug':
    started=time.time()
    dte.ExecuteCommand('Debug.Start')
    result={'action':'Debugger.Go','debug_mode':dte.Debugger.CurrentMode,'output':safe(output)}
elif action=='commands':
    names=['Build.BuildSolution','Build.Cancel','Build.BuildSelection','Build.BuildOnlyProject','Debug.Start','Debug.StartWithoutDebugging']
    result={'commands':[{ 'name':n, 'available':safe(lambda:dte.Commands.Item(n).IsAvailable)} for n in names]}
elif action=='cancel':
    dte.ExecuteCommand('Build.Cancel')
    result={'action':'Build.Cancel','build_state':dte.Solution.SolutionBuild.BuildState,'output':safe(output)}
elif action=='build-command':
    dte.ExecuteCommand('Build.BuildSolution')
    result={'action':'Build.BuildSolution','build_state':dte.Solution.SolutionBuild.BuildState,'output':safe(output)}
elif action=='stop':
    dte.Debugger.Stop(False)
    result={'action':'Debugger.Stop','debug_mode':dte.Debugger.CurrentMode,'output':safe(output)}
elif action=='review':
    source=str(base.parent/'maui'/'WeChatLikeMaui'/'MainPage.xaml.cs')
    doc=dte.ItemOperations.OpenFile(source)
    explorer=dte.Windows.Item('{3AE79031-E1BC-11D0-8F78-00A0C9110057}')
    explorer.Visible=True
    result={'action':'OpenFile','document':dte.ActiveDocument.FullName,'solution_explorer_visible':explorer.Visible}
elif action=='output': result={'output':safe(output),'debug_mode':dte.Debugger.CurrentMode,'build_state':dte.Solution.SolutionBuild.BuildState,'last_build_info':safe(lambda:dte.Solution.SolutionBuild.LastBuildInfo)}
else: raise ValueError(action)
text=json.dumps(result,ensure_ascii=False,indent=2,default=str)
(base/f'VS-{action}.json').write_text(text,encoding='utf-8')
print(text)
