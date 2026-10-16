using Android.App;
using Android.Content;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using WeChatLikeMaui.Services;
using AColor=Android.Graphics.Color;
using AView=Android.Views.View;
using AButton=Android.Widget.Button;

namespace WeChatLikeMaui.Platforms.Android;

[Service(Name="com.wechatlike.maui.PhoneOverlayService",Exported=false)]
public sealed class PhoneOverlayService : Service
{
    private const int NoticeId=7301;
    private const string Channel="wechat-assistant-overlay";
    private IWindowManager? _windows;
    private WindowManagerLayoutParams? _position;
    private LinearLayout? _root;
    private global::Android.Widget.ScrollView? _surface;
    private bool _layoutFailed;
    private readonly OverlaySession _session=new();
    private PhoneMessage? _selected;
    private EditText? _input,_answer;
    private AButton? _generate;
    private TextView? _feedback;
    private string _draftInput="",_draftAnswer="";
    private bool _attached;
    private readonly HashSet<string> _friendDraft=new(StringComparer.Ordinal);
    private int _friendPage;
    private float _density=1;
    private static readonly AColor Ink=AColor.Rgb(19,44,54),Teal=AColor.Rgb(17,133,107),Muted=AColor.Rgb(94,117,111);
    public override IBinder? OnBind(Intent? intent)=>null;
    public override void OnCreate(){base.OnCreate();_density=Resources?.DisplayMetrics?.Density??1;_windows=GetSystemService(WindowService)?.JavaCast<IWindowManager>();}
    public override StartCommandResult OnStartCommand(Intent? intent,StartCommandFlags flags,int startId)
    {
        if(intent?.Action=="stop"){StopSelf();return StartCommandResult.NotSticky;}
        try{
            if(!PhoneOverlay.HasPermission)throw new InvalidOperationException("悬浮权限未开启。");
            var notice=BuildNotice();
            if(OperatingSystem.IsAndroidVersionAtLeast(34))StartForeground(NoticeId,notice,global::Android.Content.PM.ForegroundService.TypeSpecialUse);
            else StartForeground(NoticeId,notice);
            if(!_attached){
                _root=new LinearLayout(this){Orientation=Orientation.Vertical};
                _surface=new global::Android.Widget.ScrollView(this){FillViewport=false};
                _surface.AddView(_root,new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent,ViewGroup.LayoutParams.WrapContent));
                _position=new WindowManagerLayoutParams(ViewGroup.LayoutParams.WrapContent,ViewGroup.LayoutParams.WrapContent,
                    OperatingSystem.IsAndroidVersionAtLeast(26)?WindowManagerTypes.ApplicationOverlay:WindowManagerTypes.Phone,
                    WindowManagerFlags.NotFocusable|WindowManagerFlags.NotTouchModal,global::Android.Graphics.Format.Translucent){Gravity=GravityFlags.Top|GravityFlags.Left,X=Dp(12),Y=Dp(160)};
                _windows!.AddView(_surface,_position);_attached=true;ShowBall();
            }
            if(_layoutFailed)throw new InvalidOperationException("悬浮窗口布局未完成。");
            PhoneOverlay.IsRunning=true;PhoneMoments.ResumeRemembered();PhoneOverlay.MarkReady(true);
        }
        catch(Exception){PhoneOverlay.MarkReady(false);PhoneWorkspace.SetStatus("悬浮工具球启动失败，请检查悬浮权限和后台限制。");StopSelf();}
        return StartCommandResult.NotSticky;
    }
    private Notification BuildNotice()
    {
        var manager=(NotificationManager)GetSystemService(NotificationService)!;
        if(OperatingSystem.IsAndroidVersionAtLeast(26))manager.CreateNotificationChannel(new NotificationChannel(Channel,"微信悬浮助手",NotificationImportance.Low));
        var immutable=OperatingSystem.IsAndroidVersionAtLeast(23)?PendingIntentFlags.Immutable:(PendingIntentFlags)0;
        var home=SettingsIntent("setup");
        var open=PendingIntent.GetActivity(this,7302,home,PendingIntentFlags.UpdateCurrent|immutable);
        var stop=PendingIntent.GetService(this,7303,new Intent(this,typeof(PhoneOverlayService)).SetAction("stop"),PendingIntentFlags.UpdateCurrent|immutable);
        var stopAction=OperatingSystem.IsAndroidVersionAtLeast(23)?new Notification.Action.Builder(Icon.CreateWithResource(this,global::Android.Resource.Drawable.IcMenuCloseClearCancel),"停止助手",stop):new Notification.Action.Builder(global::Android.Resource.Drawable.IcMenuCloseClearCancel,"停止助手",stop);
        var builder=OperatingSystem.IsAndroidVersionAtLeast(26)?new Notification.Builder(this,Channel):new Notification.Builder(this);
        builder.SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo).SetContentTitle("微信助手 · 悬浮工具球")
            .SetContentText("点此进入设置；可随时停止助手").SetContentIntent(open).SetOngoing(true)
            .AddAction(stopAction.Build());
        return builder.Build();
    }
    private Intent SettingsIntent(string section)=>new Intent(this,typeof(MainActivity)).PutExtra("phone.section",section).AddFlags(ActivityFlags.NewTask|ActivityFlags.SingleTop|ActivityFlags.ClearTop);
    private void Settings(string section){ShowBall();StartActivity(SettingsIntent(section));}
    private int Dp(double value)=>(int)Math.Round(value*_density);
    private GradientDrawable Background(AColor color,int radius=18){var background=new GradientDrawable();background.SetColor(color);background.SetCornerRadius(Dp(radius));return background;}
    private TextView Text(string value,int size=14,AColor? color=null)=>new(this){Text=value,TextSize=size,ContentDescription=value,TextAlignment=global::Android.Views.TextAlignment.ViewStart};
    private void AddText(string value,int size=14,AColor? color=null){var text=Text(value,size);text.SetTextColor(color??Ink);_root!.AddView(text,new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent,ViewGroup.LayoutParams.WrapContent){BottomMargin=Dp(10)});}
    private AButton AddButton(string label,Action action,bool primary=false)
    {
        var button=new AButton(this){Text=label,TextSize=14,ContentDescription=label};button.SetAllCaps(false);button.SetMinHeight(Dp(48));button.SetTextColor(primary?AColor.White:Ink);
        button.Background=Background(primary?Teal:AColor.Rgb(230,239,235),10);button.SetPadding(Dp(12),Dp(8),Dp(12),Dp(8));
        button.Click+=(_,_)=>{try{action();}catch(Exception ex){Feedback(ex is InvalidOperationException?ex.Message:"操作未完成，请检查权限和设置。");}};
        _root!.AddView(button,new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent,ViewGroup.LayoutParams.WrapContent){BottomMargin=Dp(8)});return button;
    }
    private EditText AddEditor(string hint,string value,int height)
    {
        var editor=new EditText(this){Text=value,Hint=hint,TextSize=14,Gravity=GravityFlags.Top,InputType=global::Android.Text.InputTypes.ClassText|global::Android.Text.InputTypes.TextFlagMultiLine,ContentDescription=hint};
        editor.SetTextColor(Ink);editor.SetHintTextColor(Muted);editor.SetPadding(Dp(10),Dp(8),Dp(10),Dp(8));editor.Background=Background(AColor.Rgb(243,246,245),8);
        _root!.AddView(editor,new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent,Dp(height)){BottomMargin=Dp(10)});return editor;
    }
    private void Feedback(string value){if(_feedback!=null)_feedback.Text=value;PhoneWorkspace.SetStatus(value);}
    private void Frame(string title,bool input=false)
    {
        _session.Navigate();HideKeyboard();_root!.RemoveAllViews();_root.SetPadding(Dp(14),Dp(14),Dp(14),Dp(10));_root.Background=Background(AColor.White);
        _feedback=null;_input=null;_answer=null;
        _position!.Width=Math.Min(Dp(330),Math.Max(Dp(180),ScreenWidth()-Dp(24)));
        _position.Height=Math.Min(Dp(540),Math.Max(Dp(200),ScreenHeight()-Dp(110)));
        _position.Flags=input?WindowManagerFlags.NotTouchModal:WindowManagerFlags.NotFocusable|WindowManagerFlags.NotTouchModal;
        _position.SoftInputMode=SoftInput.AdjustResize;
        _surface!.ScrollTo(0,0);
        AddText(title,18);AddButton("收起工具球",ShowBall);
        _feedback=Text("",12);_feedback.SetTextColor(Muted);_root.AddView(_feedback);
        LayoutWindow();
    }
    private int ScreenWidth()=>Resources?.DisplayMetrics?.WidthPixels??Dp(360);
    private int ScreenHeight()=>Resources?.DisplayMetrics?.HeightPixels??Dp(700);
    private void LayoutWindow()
    {
        if(!_attached||_position==null||_root==null)return;
        int width=_position.Width>0?_position.Width:Dp(64),height=_position.Height>0?_position.Height:Dp(64);
        var point=OverlayGeometry.Clamp(_position.X,_position.Y,width,height,ScreenWidth(),ScreenHeight(),Dp(12));_position.X=point.X;_position.Y=point.Y;
        try{_windows!.UpdateViewLayout(_surface,_position);}catch(Exception){_layoutFailed=true;StopSelf();}
    }
    private void HideKeyboard(){if(_root?.WindowToken!=null)(GetSystemService(InputMethodService)as InputMethodManager)?.HideSoftInputFromWindow(_root.WindowToken,HideSoftInputFlags.None);}
    private void ShowBall()
    {
        _draftInput=_input?.Text??_draftInput;_draftAnswer=_answer?.Text??_draftAnswer;
        _session.Navigate();HideKeyboard();_root!.RemoveAllViews();_root.SetPadding(0,0,0,0);_root.Background=null;_surface!.ScrollTo(0,0);
        _input=null;_answer=null;_feedback=null;
        _position!.Width=Dp(64);_position.Height=Dp(64);_position.Flags=WindowManagerFlags.NotFocusable|WindowManagerFlags.NotTouchModal;
        var ball=new AButton(this){Text=_session.IsPaused?"已暂停":"微助手",TextSize=12,ContentDescription="微信助手悬浮工具球，点按展开，拖动调整位置"};
        ball.SetAllCaps(false);ball.SetTextColor(AColor.White);ball.Background=Background(_session.IsPaused?Muted:Teal,32);ball.SetPadding(0,0,0,0);
        ball.Click+=(_,_)=>Menu();ball.SetOnTouchListener(new Drag(this));_root.AddView(ball,new LinearLayout.LayoutParams(Dp(64),Dp(64)));LayoutWindow();
    }
    private void Menu()
    {
        Frame(_session.IsPaused?"微信助手 · 已暂停":"微信助手");
        AddText("朋友圈巡检："+(PhoneMoments.AutoRunning?"已启动，等待符合条件的页面":PhoneMoments.RememberAuto?"已记住开启，当前等待或暂停":"关闭"),12,Muted);
        var ai=AddButton("AI 回复",()=>{_selected=null;_draftInput="";_draftAnswer="";AiPanel();},true);ai.Enabled=!_session.IsPaused;
        var moments=AddButton("朋友圈辅助",Authors);moments.Enabled=!_session.IsPaused;
        var friends=AddButton("识别 / 选择好友",()=>{BeginFriends();});friends.Enabled=!_session.IsPaused;
        AddButton(_session.IsPaused?"继续使用助手":"暂停全部操作",()=>{
            if(_session.IsPaused){_session.Resume();PhoneMoments.ResumeRemembered();}else{_session.Pause();PhoneWorkspace.DisableAuto();PhoneMoments.Cancel();}
            Menu();Feedback(_session.IsPaused?"已取消生成、自动回复与本轮点赞。通知接收仍保留。":"已继续使用，自动回复保持关闭。");
        });
        AddButton("首次设置 / 权限",()=>Settings("setup"));AddButton("关闭助手",StopSelf);
    }
    private void AiPanel()
    {
        if(_session.IsPaused)return;
        Frame("AI 回复",true);
        AddButton("选择微信通知",ChooseNotification);
        AddText(_selected==null?"手动输入或粘贴；不会读取聊天窗口":"当前通知："+_selected.Title,12,Muted);
        _input=AddEditor("需要回复的内容",_draftInput,80);_answer=AddEditor("AI 回复，可修改后确认",_draftAnswer,110);
        _generate=AddButton("生成回复",()=>_ = GenerateAsync(),true);
        AddButton("复制回复",()=>{var value=_answer!.Text??"";if(string.IsNullOrWhiteSpace(value))throw new InvalidOperationException("请先生成或填写回复。");((global::Android.Content.ClipboardManager)GetSystemService(ClipboardService)!).PrimaryClip=ClipData.NewPlainText("AI 回复",value);Feedback("已复制，请在微信按需粘贴。");});
        AddButton("核对并回复通知",ConfirmReply);
        AddButton("返回菜单",Menu);
        Feedback("生成时会把当前内容发送到你已配置的 AI 服务。");
    }
    private void ChooseNotification()
    {
        _draftInput=_input?.Text??_draftInput;_draftAnswer=_answer?.Text??_draftAnswer;
        Frame("选择要回复的微信通知");
        var messages=PhoneWorkspace.Messages.Take(8).ToArray();
        if(messages.Length==0){AddText("暂无新通知。可手动粘贴内容，或在首次设置开启通知使用权。");AddButton("通知设置",()=>Settings("setup"));}
        foreach(var item in messages){var message=item;AddButton(message.Title+" · "+(message.Text.Length>28?message.Text[..28]+"…":message.Text),()=>{_selected=message;_draftInput=message.Text;_draftAnswer="";AiPanel();});}
        AddButton("返回 AI 回复",AiPanel);
    }
    private async Task GenerateAsync()
    {
        if(_session.IsPaused||_generate?.Enabled!=true)return;
        var input=_input?.Text??"";
        var generation=_session.BeginGeneration();var button=_generate;var editor=_input;var answerEditor=_answer;
        button.Enabled=false;if(editor!=null)editor.Enabled=false;if(answerEditor!=null){answerEditor.Enabled=false;answerEditor.Text="";}
        Feedback("正在生成，可收起或暂停以取消…");
        try{
            var answer=await PhoneWorkspace.GenerateAsync(input,generation.Token);
            MainThread.BeginInvokeOnMainThread(()=>{if(!_session.Accepts(generation.Revision)||_answer==null)return;_answer.Text=answer;_draftInput=input;_draftAnswer=answer;Feedback("已生成，尚未发送。请核对内容和目标会话。");});
        }
        catch(System.OperationCanceledException){MainThread.BeginInvokeOnMainThread(()=>{if(_session.Accepts(generation.Revision))Feedback("生成已取消或超时。");});}
        catch(Exception ex){MainThread.BeginInvokeOnMainThread(()=>{if(_session.Accepts(generation.Revision))Feedback(ex is InvalidOperationException?ex.Message:"AI 请求未完成，请在首次设置核对接口、密钥与网络。");});}
        finally{MainThread.BeginInvokeOnMainThread(()=>{if(!_session.Accepts(generation.Revision))return;button.Enabled=true;if(editor!=null)editor.Enabled=true;if(answerEditor!=null)answerEditor.Enabled=true;});}
    }
    private void ConfirmReply()
    {
        var message=_selected??throw new InvalidOperationException("请先选择一条微信通知，手动输入的内容请复制回复。");
        if(!message.CanReply)throw new InvalidOperationException("此通知没有快捷回复入口，请复制后在微信粘贴。");
        _draftInput=_input?.Text??"";_draftAnswer=_answer?.Text??"";
        if(string.IsNullOrWhiteSpace(_draftAnswer))throw new InvalidOperationException("请先生成或填写回复。");
        var text=_draftAnswer;Frame("核对后发送");AddText("目标通知："+message.Title,16);AddText(text);
        AddButton("确认发送这条回复",()=>{
            if(_session.IsPaused||_session.IsStopped)return;
            try{PhonePlatform.ReplyAsync(message,text).GetAwaiter().GetResult();_selected=null;_draftInput="";_draftAnswer="";Menu();Feedback("已提交通知回复，请在微信查看送达状态。");}
            catch(Exception ex){Feedback(ex is InvalidOperationException?ex.Message:"提交未完成，不会自动重发，请到微信确认。");}
        },true);
        AddButton("返回修改",AiPanel);
    }
    private void Authors()
    {
        if(_session.IsPaused)return;
        Frame("朋友圈辅助");AddText("自动巡检每 15 秒检查一次，仅在解锁并停留在朋友圈顶部时操作。不能保证别人一发就立即点赞。",12,Muted);
        AddButton(PhoneMoments.RememberAuto?"关闭自动巡检（已记住开启）":"开启自动巡检并记住",()=>{if(PhoneMoments.RememberAuto){PhoneMoments.StopAuto();Feedback("自动巡检已关闭。");}else ConfirmWatch();});
        if(PhoneMoments.RememberAuto&&!PhoneMoments.AutoRunning)AddButton("恢复已记住的自动巡检",()=>{PhoneMoments.StartAuto();ShowBall();});
        AddText("下面选择作者，可执行单次辅助：",12,Muted);
        var names=Preferences.Get("phone.moments.names","").Split('\n',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
        AddButton("识别 / 选择好友",BeginFriends);
        if(names.Length==0)AddText("尚未选择好友，请先识别并勾选。");
        foreach(var item in names){var name=item;AddButton(name,()=>{PhoneMoments.Arm(name);ShowBall();});}
        AddButton("取消本轮辅助",()=>{PhoneMoments.Cancel();ShowBall();});AddButton("返回菜单",Menu);
    }
    private void BeginFriends(){PhoneMoments.Cancel();_friendDraft.Clear();_friendDraft.UnionWith(PhoneFriends.Selected);_friendPage=0;FriendsPanel();}
    private void FriendsPanel(string? status=null)
    {
        if(_session.IsPaused)return;
        Frame("识别 / 选择微信好友");
        AddText("先在微信打开通讯录或朋友圈，再识别当前页。收起工具球、手动翻页后可以继续添加；不用输入名字。",12,Muted);
        AddButton("识别微信当前页",()=>{var message=PhoneFriends.Capture();FriendsPanel(message);},true);
        var candidates=PhoneFriends.Candidates;var pages=Math.Max(1,(candidates.Count+19)/20);_friendPage=Math.Clamp(_friendPage,0,pages-1);
        AddText($"已勾选 {_friendDraft.Count} / 20 · 第 {_friendPage+1} / {pages} 页",14);
        AddButton("保存所选好友",()=>{PhoneFriends.Save(_friendDraft);FriendsPanel("好友选择已保存，自动巡检保持关闭。");},true);
        AddButton("清空好友选择",()=>{_friendDraft.Clear();PhoneFriends.Save([]);FriendsPanel("已清空好友选择。");});
        if(candidates.Count==0)AddText("暂无识别结果。请先收起工具球，打开微信通讯录。不会读取聊天数据库或上传好友名单。",12,Muted);
        foreach(var candidate in candidates.Skip(_friendPage*20).Take(20)){
            var item=candidate;
            var check=new global::Android.Widget.CheckBox(this){Text=item.Name+"\n"+(item.Ambiguous?"同名无法区分，请先修改微信备注":item.Source),TextSize=14,Checked=_friendDraft.Contains(item.Name),Enabled=!item.Ambiguous,ContentDescription=item.Name+(item.Ambiguous?"，同名无法选择":"，勾选好友")};
            check.SetTextColor(Ink);check.SetMinHeight(Dp(52));
            check.CheckedChange+=(_,e)=>{if(e.IsChecked){if(_friendDraft.Count>=20){check.Checked=false;Feedback("最多选择 20 位好友。");return;}_friendDraft.Add(item.Name);}else _friendDraft.Remove(item.Name);Feedback($"已勾选 {_friendDraft.Count} / 20，修改后请保存。");};
            _root!.AddView(check,new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent,ViewGroup.LayoutParams.WrapContent));
        }
        if(_friendPage>0)AddButton("上一页",()=>{_friendPage--;FriendsPanel();});
        if(_friendPage<pages-1)AddButton("下一页",()=>{_friendPage++;FriendsPanel();});
        AddText("仅可见显示名，不是微信唯一 ID。同名请先设置不同备注；切换微信账号后请清空重选。识别结果暂存内存，只有勾选保存的名单留在手机。",12,Muted);
        AddButton("清除未保存的识别结果",()=>{PhoneFriends.ClearRecognized();_friendDraft.Clear();_friendDraft.UnionWith(PhoneFriends.Selected);FriendsPanel();});
        AddButton("返回朋友圈辅助",Authors);
        if(status!=null)Feedback(status);
    }
    private void ConfirmWatch()
    {
        Frame("开启自动巡检");
        var names=Preferences.Get("phone.moments.names","").Split('\n',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
        AddText(names.Length==0?"尚未配置作者名单。":"仅处理以下作者：\n"+string.Join("\n",names));
        AddText("记住后，每次启动助手会自动恢复巡检。初次可见动态作为基线；只尝试可识别的新近文字动态，每 15 秒最多一条。离开朋友圈、锁屏或控件不明确时等待。",12,Muted);
        AddButton("启用并记住设置",()=>{PhoneMoments.StartAuto();ShowBall();},true);
        AddButton("选择微信好友",BeginFriends);AddButton("返回",Authors);
    }
    public override void OnConfigurationChanged(global::Android.Content.Res.Configuration? newConfig){base.OnConfigurationChanged(newConfig);_density=Resources?.DisplayMetrics?.Density??1;if(_attached)ShowBall();}
    public override void OnDestroy()
    {
        _session.Dispose();PhoneOverlay.IsRunning=false;PhoneOverlay.MarkReady(false);PhoneWorkspace.DisableAuto();PhoneMoments.Cancel();HideKeyboard();
        if(_attached&&_surface!=null){try{_windows?.RemoveViewImmediate(_surface);}catch(Exception){} _attached=false;}
        if(OperatingSystem.IsAndroidVersionAtLeast(24))StopForeground(StopForegroundFlags.Remove);
        else {
#pragma warning disable CS0618
            StopForeground(true);
#pragma warning restore CS0618
        }
        base.OnDestroy();
    }
    private sealed class Drag(PhoneOverlayService owner):Java.Lang.Object,AView.IOnTouchListener
    {
        private float _x,_y;private int _left,_top;private bool _moved;
        public bool OnTouch(AView? view,MotionEvent? e)
        {
            if(e==null||owner._position==null)return false;
            switch(e.ActionMasked){
                case MotionEventActions.Down:view?.Parent?.RequestDisallowInterceptTouchEvent(true);_x=e.RawX;_y=e.RawY;_left=owner._position.X;_top=owner._position.Y;_moved=false;return true;
                case MotionEventActions.Move:
                    var dx=e.RawX-_x;var dy=e.RawY-_y;
                    if(Math.Abs(dx)+Math.Abs(dy)>owner.Dp(8))_moved=true;
                    if(_moved){owner._position.X=_left+(int)dx;owner._position.Y=_top+(int)dy;owner.LayoutWindow();}return true;
                case MotionEventActions.Up:view?.Parent?.RequestDisallowInterceptTouchEvent(false);if(!_moved)view?.PerformClick();return true;
                case MotionEventActions.Cancel:view?.Parent?.RequestDisallowInterceptTouchEvent(false);return true;
            }
            return false;
        }
    }
}
