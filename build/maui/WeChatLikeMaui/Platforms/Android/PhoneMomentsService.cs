using Android.AccessibilityServices;
using Android.App;
using Android.Content;
using Rect = Android.Graphics.Rect;
using Android.OS;
using Android.Views.Accessibility;
using WeChatLikeMaui.Services;

namespace WeChatLikeMaui.Platforms.Android;

[Service(Name="com.wechatlike.maui.PhoneMomentsService",Label="微信工作台 · 朋友圈辅助",Permission="android.permission.BIND_ACCESSIBILITY_SERVICE",Exported=true)]
[IntentFilter(new[]{"android.accessibilityservice.AccessibilityService"})]
[MetaData("android.accessibilityservice",Resource="@xml/phone_accessibility")]
public sealed partial class PhoneMomentsService : AccessibilityService
{
    private static PhoneMomentsService? _instance;
    private string? _author;
    private long _deadline;
    private long _menuAt;
    private int _window;
    private Rect? _authorBounds;
    private bool _working;
    protected override void OnServiceConnected(){base.OnServiceConnected();_instance=this;_author=null;ResumeRemembered();}
    public override void OnDestroy(){SuspendWatch();_author=null;_instance=null;base.OnDestroy();}
    public override void OnInterrupt(){SuspendWatch();_author=null;PhoneWorkspace.SetStatus("朋友圈辅助已中断。");}
    public static bool IsReady=>_instance!=null;
    public static void Arm(string author){var instance=_instance??throw new InvalidOperationException("请先在系统中启用朋友圈辅助服务。");if(string.IsNullOrWhiteSpace(author)||author.Length>100||PhoneFriends.IsAmbiguous(author))throw new InvalidOperationException("作者名称不正确。");instance.SuspendWatch();instance._autoId=null;instance._autoBounds=null;instance._author=author;instance._deadline=SystemClock.ElapsedRealtime()+60_000;instance._menuAt=0;var deadline=instance._deadline;new Handler(Looper.MainLooper!).PostDelayed(()=>{if(instance._deadline==deadline)instance.Scan();},400);PhoneWorkspace.SetStatus("本轮已准备，请打开朋友圈并显示该作者的一条动态；60 秒后失效。");}
    public static void Cancel(){_instance?.SuspendWatch();}
    private void Stop(string status){_autoId=null;_autoBounds=null;_author=null;_menuAt=0;PhoneWorkspace.SetStatus(status);}
    private static List<AccessibilityNodeInfo> Nodes(AccessibilityNodeInfo root){var result=new List<AccessibilityNodeInfo>();var queue=new Queue<AccessibilityNodeInfo>();queue.Enqueue(root);int examined=0;while(queue.Count>0&&examined++<600){var node=queue.Dequeue();if(node.VisibleToUser)result.Add(node);for(int i=0;i<Math.Min(node.ChildCount,80);i++){var child=node.GetChild(i);if(child!=null)queue.Enqueue(child);}}return result;}
    private static string Label(AccessibilityNodeInfo node)=>node.Text?.ToString()?.Trim()??"";
    private static bool Menu(AccessibilityNodeInfo node)=>node.ContentDescription?.ToString()?.Trim() is "评论" or "Comment";
    private static AccessibilityNodeInfo? Clickable(AccessibilityNodeInfo node){var current=node;for(int i=0;i<3&&current!=null;i++,current=current.Parent){if(current.Clickable&&current.Enabled)return current;}return null;}
    public override void OnAccessibilityEvent(AccessibilityEvent? e){if(e?.PackageName?.ToString()=="com.tencent.mm"){Scan();ConsiderWatchEvent();}}
    private void Scan(){
        if(_working||_author==null)return;
        if(SystemClock.ElapsedRealtime()>_deadline){Stop("本轮已到期，没有继续操作。");return;}
        _working=true;
        try{
            var root=RootInActiveWindow;if(root?.PackageName?.ToString()!="com.tencent.mm")return;
            var nodes=Nodes(root);
            if(!nodes.Any(n=>Label(n) is "朋友圈" or "Moments"))return;
            var authors=nodes.Where(n=>Label(n)==_author&&(_autoBounds==null||Bounds(n).Equals(_autoBounds))).ToArray();
            if(authors.Length==0)return;
            if(authors.Length!=1){Stop("当前显示了多个同名候选，请调整到一条目标动态后重新准备。");return;}
            var author=authors[0];var bounds=new Rect();author.GetBoundsInScreen(bounds);
            if(bounds.IsEmpty)return;
            if(_autoId!=null&&Describe(author,root)?.Post.Id!=_autoId){Stop("目标动态已变化，本轮不再操作。");return;}
            if(_menuAt>0){
                if(SystemClock.ElapsedRealtime()-_menuAt>2500){Stop("未识别到明确的点赞菜单，本轮已停止。");return;}
                if(root.WindowId!=_window||_authorBounds==null||!bounds.Equals(_authorBounds)){Stop("页面已改变，本轮已停止。");return;}
                if(nodes.Any(n=>Label(n) is "取消" or "取消赞" or "Unlike")){Stop("检测到可能已点赞，本轮未重复点击。");return;}
                var likes=nodes.Where(LikeAction).Select(Clickable).Where(n=>n!=null).ToArray();
                if(likes.Length!=1)return;
                var target=likes[0]!;
                // 先取出本轮预留的唯一标识与作者，再清 _author，用于点赞落地后推进护栏计数。
                var likedId=_autoId;var likedAuthor=_author;_author=null;
                var clicked=target.PerformAction(global::Android.Views.Accessibility.Action.Click);
                // 只在微信真正接受了点击时推进冷却/上限；未确认的点击既不重试也不计数。
                if(clicked&&likedId!=null&&likedAuthor!=null)_watchPolicy.MarkLiked(new WatchedMoment(likedId,likedAuthor,true));
                Stop(clicked?"已提交一次点赞点击，请在官方微信确认结果。":"微信未接受点赞点击，本轮已停止。");return;
            }
            var screen=new Rect();root.GetBoundsInScreen(screen);
            var container=author.Parent;AccessibilityNodeInfo? menu=null;
            for(int depth=0;depth<5&&container!=null;depth++,container=container.Parent){
                var rect=new Rect();container.GetBoundsInScreen(rect);
                if(rect.Height()>screen.Height()*0.8)break;
                var entries=Nodes(container).Where(Menu).ToArray();
                if(entries.Length==1){menu=Clickable(entries[0]);break;}
                if(entries.Length>1)break;
            }
            if(menu==null){PhoneWorkspace.SetStatus("目标动态未暴露明确的评论菜单，请调整页面；不会使用坐标盲点。");return;}
            _likeMenuBounds=Bounds(menu);_window=root.WindowId;_authorBounds=bounds;_menuAt=SystemClock.ElapsedRealtime();
            if(!menu.PerformAction(global::Android.Views.Accessibility.Action.Click)){Stop("微信未接受菜单点击，本轮已停止。");return;}
            new Handler(Looper.MainLooper!).PostDelayed(()=>Scan(),700);
        }
        catch(Exception){Stop("当前微信界面不兼容，本轮已停止。");}
        finally{_working=false;}
    }
}
