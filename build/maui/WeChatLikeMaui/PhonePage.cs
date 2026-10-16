using WeChatLikeMaui.Services;
using Microsoft.Maui.Controls.Shapes;

namespace WeChatLikeMaui;

// Native phone workspace: no ConsoleApi, desktop IP, or cloud-agent dependency.
public sealed class PhonePage : ContentPage
{
    private static readonly Color Ink=Color.FromArgb("#132C36"), Teal=Color.FromArgb("#11856B"), Muted=Color.FromArgb("#5E756F");
    private readonly VerticalStackLayout _body=new(){Spacing=16,Padding=new Thickness(20,20,20,36)};
    private readonly Label _status=new(){FontSize=13,TextColor=Muted,LineBreakMode=LineBreakMode.WordWrap,AutomationId="PhoneStatus"};
    private ScrollView _tabStrip=new();
    private bool _phoneKeyConfigured;
    private readonly Label _setupSummary=new(){FontSize=14,TextColor=Ink};
    private readonly Label _permissions=new(){FontSize=14,TextColor=Ink};
    private readonly VerticalStackLayout _messages=new(){Spacing=12};
    private readonly Entry _url=new(){Placeholder="https://apihub.agnes-ai.com/v1",Keyboard=Keyboard.Url,AutomationId="PhoneAiUrl"};
    private readonly Entry _model=new(){Placeholder="模型名称",AutomationId="PhoneAiModel"};
    private readonly Entry _key=new(){Placeholder="填入密钥；留空保留已存密钥",IsPassword=true,AutomationId="PhoneAiKey"};
    private readonly Editor _prompt=new(){HeightRequest=110,AutoSize=EditorAutoSizeOption.TextChanges};
    private readonly Editor _input=new(){Placeholder="输入或粘贴需要回复的内容",HeightRequest=110,AutomationId="PhoneAiInput"};
    private readonly Editor _reply=new(){Placeholder="生成后可编辑；不会自动发送",HeightRequest=150,AutomationId="PhoneAiReply"};
    private readonly Editor _targets=new(){Placeholder="每行一个微信通知会话标题，完整匹配",HeightRequest=120};
    private readonly VerticalStackLayout _friendList=new(){Spacing=8,AutomationId="PhoneFriendList"};
    private readonly Label _friendSummary=new(){FontSize=13,TextColor=Muted,AutomationId="PhoneFriendSummary"};
    private readonly HashSet<string> _friendDraft=new(StringComparer.Ordinal);
    private int _friendPage;
    private readonly Label _replyTarget=new(){Text="当前为独立 AI 预览",FontSize=13,TextColor=Muted};
    private readonly Dictionary<string,View> _panels=new();
    private readonly Dictionary<string,Button> _tabs=new();
    private PhoneMessage? _selected;
    private bool _busy;
    private bool _previewVerified;
    private readonly CancellationTokenSource _lifetime=new();
    private readonly IDispatcherTimer _timer;
    private static Label Text(string value,int size=14) => new(){Text=value,FontSize=size,TextColor=Ink,LineBreakMode=LineBreakMode.WordWrap};
    private static Label Heading(string value,int size=22) {var label=Text(value,size);label.FontAttributes=FontAttributes.Bold;SemanticProperties.SetHeadingLevel(label,SemanticHeadingLevel.Level2);return label;}
    private Button Button(string title,string id,Func<Task> action,bool primary=false) {
        var b=new Button{Text=title,AutomationId=id,MinimumHeightRequest=48,FontSize=14,CornerRadius=10,Padding=new Thickness(14,10),BackgroundColor=primary?Teal:Color.FromArgb("#E6EFEB"),TextColor=primary?Colors.White:Ink};
        b.Clicked+=async(_,_)=>{if(_busy && !id.StartsWith("PhoneTab-",StringComparison.Ordinal) && id is not ("PhoneDisableAuto" or "PhoneCancelMoments" or "PhoneStopAssistant"))return;try{await action();}catch(OperationCanceledException){_status.Text="操作已取消或超时。";}catch(Exception ex){_status.Text=ex is InvalidOperationException?ex.Message:"操作未完成，请检查网络、权限和接口设置。";}};
        return b;
    }
    private static Border Card(params View[] children) => new(){Padding=16,Stroke=Color.FromArgb("#DDE7E3"),StrokeShape=new RoundRectangle{CornerRadius=14},BackgroundColor=Colors.White,Content=new VerticalStackLayout{Spacing=12,Children={}}.With(children)};
    private View Panel(string key,params View[] children) {var panel=new VerticalStackLayout{Spacing=16}.With(children);_panels[key]=panel;return panel;}
    public PhonePage() {
        SafeAreaEdges=Microsoft.Maui.SafeAreaEdges.All;
        Title="微信工作台 · 手机独立版";BackgroundColor=Color.FromArgb("#F3F6F5");
        var headline=new VerticalStackLayout{Padding=new Thickness(20,18),Spacing=5,BackgroundColor=Ink};
        headline.Add(new Label{Text="微信工作台",FontSize=26,FontAttributes=FontAttributes.Bold,TextColor=Colors.White});
        headline.Add(new Label{Text="手机独立版 · 无需连接电脑",FontSize=13,TextColor=Color.FromArgb("#A9D5C7")});
        var tabs=new HorizontalStackLayout{Spacing=8,Padding=new Thickness(20,12)};
        foreach(var(key,name)in new[]{("home","本机"),("ai","AI 答复"),("messages","微信消息"),("moments","朋友圈")}) {
            var capture=key;var b=Button(name,"PhoneTab-"+key,()=>{Select(capture);return Task.CompletedTask;});_tabs[key]=b;tabs.Add(b);
        }
        var home=Panel("home",Heading("微信旁边，点一下就用"),Text("首次按需配置，日常只需启动助手。官方微信保留原来的登录状态。"),
            Card(Heading("微信悬浮助手",18),_permissions,
                Button("启动微信助手","PhoneStartAssistant",StartAssistant,true),
                Text("打开官方微信，并显示可拖动工具球。点工具球使用 AI 回复、朋友圈单次辅助或自动巡检。"),
                Button("首次设置 / 权限","PhoneSetup",()=>{Select("setup");return Task.CompletedTask;}),
                Button("停止助手","PhoneStopAssistant",()=>{PhoneOverlay.Stop();return Task.CompletedTask;})),
            Card(Heading("日常流程",18),Text("启动助手 → 打开微信 → 点悬浮球 → 选择功能"),Text("AI 首次填写服务和密钥；通知与朋友圈权限按所需功能开启。没有快捷回复入口时，可复制回复到微信。")));
        var setup=Panel("setup",Heading("首次设置"),Text("只开启你需要的能力。返回手机后会刷新状态，日常不必重复配置。"),
            Card(Heading("接入状态",18),_setupSummary),
            Card(Heading("1 · AI 服务",18),Text("生成回复需要手机联网，以及你自己的接口和密钥。"),Button("配置 AI 服务","PhoneSetupAi",()=>{Select("ai");return Task.CompletedTask;})),
            Card(Heading("2 · 悬浮显示",18),Text("允许在微信旁边显示工具球，可随时关闭。"),Button("开启悬浮显示","PhoneSetupOverlay",async()=>{if(PhoneOverlay.HasPermission){_status.Text="悬浮显示已授权。";return;}if(await DisplayAlertAsync("悬浮显示权限","允许在其他应用上层显示可拖动工具球。此步骤仅授权，不会自动打开微信。","前往系统设置","取消"))PhoneOverlay.RequestPermissionAndContinue(false);})),
            Card(Heading("3 · 微信通知（按需）",18),Text("从新通知选择消息。不会读取聊天数据库；发送到 AI 前由你选择，自动回复另行启用。"),Button("开启通知使用权","PhoneNotifications",ConsentNotifications),Button("会话与自动回复设置","PhoneSetupMessages",()=>{Select("messages");return Task.CompletedTask;})),
            Card(Heading("4 · 朋友圈辅助（按需）",18),Button("开启朋友圈辅助权限","PhoneAccessibility",ConsentAccessibility),Button("选择微信好友","PhoneSetupMoments",()=>{Select("moments");return Task.CompletedTask;})),
            Button("小米后台设置指引","PhoneXiaomi",Xiaomi),Button("仅打开官方微信","PhoneOpenWechat",()=>{PhonePlatform.OpenWeChat();return Task.CompletedTask;}),
            Button("完成，返回首页","PhoneSetupDone",()=>{Select("home");return Task.CompletedTask;},true));
        var p=PhoneWorkspace.Profile;_url.Text=p.BaseUrl;_model.Text=p.Model;_prompt.Text=p.Prompt;
        var ai=Panel("ai",Heading("AI 智能答复"),Text("手机直接调用兼容接口。生成时，仅把当前内容和提示词发送到你配置的 AI 服务。"),
            Card(Heading("服务设置",18),Text("基础地址"),_url,Text("模型"),_model,Text("API 密钥 · 本机加密保存"),_key,Text("回复提示词"),_prompt,
                Button("保存 AI 设置","PhoneSaveAi",SaveAi,true),Button("删除已存密钥","PhoneDeleteKey",async()=>{SecureStorage.Remove("phone.ai.key");_phoneKeyConfigured=false;_key.Text="";_previewVerified=false;PhoneWorkspace.DisableAuto();await DisplayAlertAsync("已删除","手机上的 AI 密钥已删除。","知道了");})),
            Card(Heading("回复预览",18),_replyTarget,_input,Button("生成回复","PhoneGenerate",Generate,true),_reply,
                Button("确认并回复所选通知","PhoneSend",Send),Button("复制回复","PhoneCopy",async()=>{await Clipboard.SetTextAsync(_reply.Text??"");_status.Text="已复制，可切换微信手动粘贴。";}),
                Button("取消所选消息","PhoneUnselect",()=>{_selected=null;_replyTarget.Text="当前为独立 AI 预览";return Task.CompletedTask;})));
        _targets.Text=string.Join('\n',PhoneWorkspace.Targets);
        var messages=Panel("messages",Heading("微信消息"),Text("只接收授权后新出现的官方微信通知。通知标题不是唯一联系人 ID；同名或群聊请人工确认。"),
            Card(Heading("自动回复范围",18),_targets,Button("保存会话名单","PhoneSaveTargets",()=>{PhoneWorkspace.SaveTargets(_targets.Text??"");_status.Text="名单已保存，自动回复保持关闭。";return Task.CompletedTask;}),
                Button("启用自动回复","PhoneEnableAuto",EnableAuto),Button("立即关闭自动回复","PhoneDisableAuto",()=>{PhoneWorkspace.DisableAuto();return Task.CompletedTask;})),
            Button("清空本次消息","PhoneClearMessages",()=>{PhoneWorkspace.Clear();return Task.CompletedTask;}),_messages);
        _friendDraft.UnionWith(PhoneFriends.Selected);
        var moments=Panel("moments",Heading("按用户辅助点赞"),Text("手机本地处理。单次辅助需要先显示目标动态；自动巡检在悬浮球的“朋友圈辅助”中开启，可记住并随助手启动。"),
            Card(Heading("勾选微信好友",18),Text("不用输入名字。先打开微信通讯录或朋友圈，在悬浮球点“识别 / 选择好友”→“识别微信当前页”。收起工具球、手动翻页后可继续识别。"),
                Button("去微信识别好友","PhoneRecognizeFriends",StartAssistant,true),_friendSummary,_friendList,
                Button("保存所选好友","PhoneSaveMomentNames",()=>{PhoneFriends.Save(_friendDraft);_status.Text=$"已保存 {_friendDraft.Count} 位好友，自动巡检保持关闭。";return Task.CompletedTask;},true),
                Button("清空好友选择","PhoneClearFriends",()=>{_friendDraft.Clear();PhoneFriends.Save([]);RefreshFriends();return Task.CompletedTask;}),
                Text("只识别已显示的内容，不等于完整通讯录。显示名不是微信唯一 ID；遇到同名请先在微信设置不同备注。切换微信账号后请清空并重新选择。"),
                Button("开启朋友圈辅助权限","PhoneAccessibility",ConsentAccessibility),Button("准备本轮辅助点赞","PhoneArmMoments",ArmMoments),Button("取消本轮","PhoneCancelMoments",()=>{PhoneMoments.Cancel();return Task.CompletedTask;})),
            Card(Heading("执行边界",18),Text("仅在能确认朋友圈、完整作者名及对应评论菜单时操作。发现多个候选、已点赞、同名歧义或界面不兼容会停止；不使用固定坐标盲点。不支持无解锁桌面运行。")));
        foreach(var panel in new[]{home,setup,ai,messages,moments})_body.Add(panel);
        var scroll=new ScrollView{Content=_body};
        var layout=new Grid{RowDefinitions={new RowDefinition(GridLength.Auto),new RowDefinition(GridLength.Auto),new RowDefinition(GridLength.Auto),new RowDefinition(GridLength.Star)}};
        layout.Add(headline);_tabStrip=new ScrollView{Orientation=ScrollOrientation.Horizontal,Content=tabs};layout.Add(_tabStrip,0,1);
        _status.Margin=new Thickness(20,0,20,8);layout.Add(_status,0,2);layout.Add(scroll,0,3);Content=layout;
        _timer=Dispatcher.CreateTimer();_timer.Interval=TimeSpan.FromSeconds(2);_timer.Tick+=(_,_)=>RefreshStatus();_status.Text=PhoneWorkspace.Status;Select("home");
    }
    private void Select(string key){if(!_panels.ContainsKey(key))key="home";_tabStrip.IsVisible=key!="home";foreach(var(k,v)in _panels)v.IsVisible=k==key;foreach(var(k,b)in _tabs){b.BackgroundColor=k==key?Teal:Color.FromArgb("#E6EFEB");b.TextColor=k==key?Colors.White:Ink;}if(key=="messages")RefreshMessages();if(key=="moments")RefreshFriends();}
    protected override async void OnAppearing(){base.OnAppearing();PhoneWorkspace.Changed+=OnChanged;PhoneOverlay.SectionRequested+=OnSection;PhoneFriends.Changed+=OnFriendsChanged;_friendDraft.Clear();_friendDraft.UnionWith(PhoneFriends.Selected);RefreshFriends();var requested=PhoneOverlay.TakeSection();if(requested!=null)Select(requested);try{_phoneKeyConfigured=!string.IsNullOrWhiteSpace(await SecureStorage.GetAsync("phone.ai.key"));}catch{_phoneKeyConfigured=false;}_timer.Start();RefreshStatus();RefreshMessages();}
    protected override void OnDisappearing(){PhoneWorkspace.Changed-=OnChanged;PhoneOverlay.SectionRequested-=OnSection;PhoneFriends.Changed-=OnFriendsChanged;_timer.Stop();base.OnDisappearing();}
    private void OnSection(string section)=>MainThread.BeginInvokeOnMainThread(()=>{PhoneOverlay.TakeSection();Select(section);});
    private async Task StartAssistant(){if(!PhoneOverlay.HasPermission){if(await DisplayAlertAsync("开启微信悬浮助手","首次需要允许悬浮显示。授权返回后会启动工具球并打开官方微信；你可以随时暂停或关闭。","前往授权","取消"))PhoneOverlay.RequestPermissionAndContinue();return;}await PhoneOverlay.StartAndOpenAsync();}
    private void OnChanged()=>MainThread.BeginInvokeOnMainThread(()=>{if(!_busy)_status.Text=PhoneWorkspace.Status;RefreshStatus();RefreshMessages();});
    private void RefreshStatus(){_permissions.Text="工具球："+(PhoneOverlay.IsRunning?"运行中":"未启动")+"\nAI 服务："+(_phoneKeyConfigured?"已配置":"待首次配置");_setupSummary.Text="悬浮显示："+(PhoneOverlay.HasPermission?"已授权":"未授权")+"\n通知使用权："+(PhonePlatform.HasNotificationAccess?"已授权":"未授权")+"\n通知服务："+(PhoneWorkspace.ListenerConnected?"已连接":"等待接入")+"\n自动回复："+(Preferences.Get("phone.auto",false)?"已启用所选会话":"关闭");}
    private void RefreshMessages(){_messages.Clear();var list=PhoneWorkspace.Messages;if(list.Count==0){_messages.Add(Card(Heading("暂无新消息",18),Text("开启通知使用权后，请让微信收到一条新消息。不会读取聊天数据库或补发旧消息。")));return;}foreach(var m in list){var message=m;_messages.Add(Card(Heading(m.Title,17),Text(m.Text),Text(m.CanReply?"此通知提供系统快捷回复":"此通知没有快捷回复入口，可生成后复制"),Button("选择并生成回复","PhoneMessage-"+m.Id,async()=>{_selected=message;_replyTarget.Text="回复通知："+message.Title;_input.Text=message.Text;_reply.Text="";Select("ai");await Generate();})));}}
    private void OnFriendsChanged()=>MainThread.BeginInvokeOnMainThread(RefreshFriends);
    private void RefreshFriends(){
        var candidates=PhoneFriends.Candidates;var pages=Math.Max(1,(candidates.Count+19)/20);_friendPage=Math.Clamp(_friendPage,0,pages-1);
        _friendSummary.Text=$"已勾选 {_friendDraft.Count} / 20 · 本次识别及已保存 {candidates.Count} 人 · 第 {_friendPage+1} / {pages} 页";
        _friendList.Clear();
        if(candidates.Count==0)_friendList.Add(Text("尚未识别好友。先到微信打开通讯录，再用悬浮球识别当前页。"));
        foreach(var candidate in candidates.Skip(_friendPage*20).Take(20)){
            var item=candidate;var check=new CheckBox{IsChecked=_friendDraft.Contains(item.Name),IsEnabled=!item.Ambiguous,Color=Teal,MinimumHeightRequest=48,MinimumWidthRequest=48};
            SemanticProperties.SetDescription(check,item.Name+(item.Ambiguous?"，同名，无法选择":"，选择此好友"));
            check.CheckedChanged+=(_,e)=>{if(e.Value){if(_friendDraft.Count>=20){check.IsChecked=false;_status.Text="最多选择 20 位好友。";return;}_friendDraft.Add(item.Name);}else _friendDraft.Remove(item.Name);_friendSummary.Text=$"已勾选 {_friendDraft.Count} / 20 · 修改后请保存";};
            var labels=new VerticalStackLayout{Spacing=2,VerticalOptions=LayoutOptions.Center}.With([Text(item.Name),Text(item.Ambiguous?"同名无法区分，请在微信修改备注":item.Source,12)]);
            var row=new Grid{ColumnDefinitions={new ColumnDefinition(GridLength.Auto),new ColumnDefinition(GridLength.Star)},Padding=new Thickness(0,4)};row.Add(check);row.Add(labels,1);_friendList.Add(row);
        }
        var navigation=new HorizontalStackLayout{Spacing=8};
        var previous=Button("上一页","PhoneFriendsPrevious",()=>{_friendPage--;RefreshFriends();return Task.CompletedTask;});previous.IsEnabled=_friendPage>0;
        var next=Button("下一页","PhoneFriendsNext",()=>{_friendPage++;RefreshFriends();return Task.CompletedTask;});next.IsEnabled=_friendPage<pages-1;
        navigation.Add(previous);navigation.Add(next);_friendList.Add(navigation);
    }
    private async Task SaveAi(){var profile=new PhoneAiProfile(_url.Text??"",_model.Text??"",_prompt.Text??"");PhoneWorkspace.ValidateProfile(profile);PhoneWorkspace.DisableAuto();if(!string.IsNullOrWhiteSpace(_key.Text))await SecureStorage.SetAsync("phone.ai.key",_key.Text.Trim());PhoneWorkspace.SaveProfile(profile);_key.Text="";_previewVerified=false;_phoneKeyConfigured=!string.IsNullOrWhiteSpace(await SecureStorage.GetAsync("phone.ai.key"));_status.Text="AI 设置已保存，自动回复保持关闭。";}
    private async Task Generate(){_busy=true;_status.Text="正在生成回复…";try{var value=await PhoneWorkspace.GenerateAsync(_input.Text??"",_lifetime.Token);_reply.Text=value;_previewVerified=true;_status.Text="已生成，请核对；尚未发送微信。";}finally{_busy=false;}}
    private async Task Send(){var message=_selected??throw new InvalidOperationException("请先在微信消息中选择一条通知。");if(!message.CanReply)throw new InvalidOperationException("此通知没有系统快捷回复，请复制后在微信粘贴。");if(await DisplayAlertAsync("确认回复",$"将回复通知会话“{message.Title}”：\n\n{_reply.Text}","发送","取消")){await PhonePlatform.ReplyAsync(message,_reply.Text??"");_selected=null;_replyTarget.Text="当前为独立 AI 预览";_status.Text="已提交系统通知回复，送达状态请在微信查看。";}}
    private async Task ConsentNotifications(){if(await DisplayAlertAsync("微信通知使用权","授权后，程序只处理官方微信的新通知，消息暂存在手机内存中。生成或自动回复时，当前消息会发送到你配置的 AI 服务。可随时在系统设置撤销。","前往系统授权","取消"))PhonePlatform.OpenNotificationSettings();}
    private async Task EnableAuto(){var saved=PhoneWorkspace.Targets;var edited=(_targets.Text??"").Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).ToArray();if(!saved.SequenceEqual(edited,StringComparer.Ordinal))throw new InvalidOperationException("名单有未保存的修改，请先保存会话名单。");if(!_previewVerified)throw new InvalidOperationException("请先成功生成一次回复预览，再启用自动回复。");if(!PhonePlatform.HasNotificationAccess||!PhoneWorkspace.ListenerConnected)throw new InvalidOperationException("请先授权并接通通知服务。");if(PhoneWorkspace.Targets.Length==0)throw new InvalidOperationException("请先保存会话名单。");if(string.IsNullOrWhiteSpace(await SecureStorage.GetAsync("phone.ai.key")))throw new InvalidOperationException("请先配置 AI 密钥并预览回复。");if(await DisplayAlertAsync("启用所选会话自动回复","以下已保存会话的新通知将发送到 AI 并自动提交回复：\n"+string.Join("\n",saved)+"\n\n标题可能同名，请核对。服务重连、配置变更或异常后自动关闭。","启用","取消")){Preferences.Set("phone.auto",true);PhoneWorkspace.SetStatus("所选会话自动回复已启用，可随时立即关闭。");}}
    private async Task Xiaomi(){await DisplayAlertAsync("小米 / HyperOS 设置","在系统设置中允许微信显示通知内容，并给“微信工作台”通知使用权。如后台频繁断开，可在应用信息中检查自启动及省电限制；菜单名称随 MIUI / HyperOS 版本不同。无需电脑。","打开应用信息");PhonePlatform.OpenAppSettings();}
    private async Task ConsentAccessibility(){if(await DisplayAlertAsync("朋友圈辅助权限","只操作官方微信：你点识别时读取可见通讯录或朋友圈中的显示名，用于勾选好友；不读取聊天数据库。准备单次辅助或明确开启巡检后，按所选好友辅助点赞，并按需下拉刷新。不读取密码、不上传屏幕。可随时停止。","前往系统授权","取消"))PhoneMoments.OpenSettings();}
    private async Task ArmMoments(){var names=Preferences.Get("phone.moments.names","").Split('\n',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);if(names.Length==0)throw new InvalidOperationException("请先识别并勾选好友。");var name=await DisplayActionSheetAsync("选择本轮作者","取消",null,names);if(name==null||name=="取消")return;PhoneMoments.Arm(name);PhonePlatform.OpenWeChat();}
}

internal static class PhoneLayoutExtensions {
    public static VerticalStackLayout With(this VerticalStackLayout layout,IEnumerable<View> views){foreach(var view in views)layout.Add(view);return layout;}
}
