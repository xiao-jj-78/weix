using Android.App;
using Android.AccessibilityServices;
using Android.OS;
using Android.Views.Accessibility;
using System.Text.RegularExpressions;
using WeChatLikeMaui.Services;
using Rect=Android.Graphics.Rect;

namespace WeChatLikeMaui.Platforms.Android;

public sealed partial class PhoneMomentsService
{
    private bool _watchRunning,_inspecting;
    private long _watchRevision,_lastProbe,_lastAttempt;
    private MomentWatchPolicy _watchPolicy=new();
    private Rect? _autoBounds;
    private Rect? _likeMenuBounds;
    private string? _autoId;
    private sealed record MomentCard(WatchedMoment Post,Rect AuthorBounds);
    public static bool AutoRunning=>_instance?._watchRunning==true;
    public static void StartAuto()
    {
        var instance=_instance??throw new InvalidOperationException("请先开启朋友圈辅助权限。");
        if(!PhoneOverlay.IsRunning)throw new InvalidOperationException("请先从首页启动微信助手。");
        if(!OperatingSystem.IsAndroidVersionAtLeast(24))throw new InvalidOperationException("自动刷新需要安卓 7 或更新版本。");
        if(Names().Length==0||Names().Any(PhoneFriends.IsAmbiguous))throw new InvalidOperationException("请先保存作者名单。");
        Preferences.Set("phone.moments.watch",true);instance.SuspendWatch();
        instance._watchPolicy=new();
        // 护栏接线：每次显式启用都重置本轮计数，并把决策审计接到可见记录（冷却/上限真正生效）。
        instance._watchPolicy.ResetRunCounters();
        instance._watchPolicy.OnAudit=PhoneWorkspace.RecordWatchAudit;
        instance._watchRunning=true;
        var revision=++instance._watchRevision;
        instance.ScheduleWatch(revision,1200);
        PhoneWorkspace.SetStatus("自动巡检已启动：请解锁并停留在朋友圈顶部，每 15 秒检查；不会补赞初始可见动态。");
    }
    public static void ResumeRemembered()
    {
        if(Preferences.Get("phone.moments.watch",false)&&!AutoRunning&&IsReady&&PhoneOverlay.IsRunning){
            try{StartAuto();}catch(Exception){PhoneWorkspace.SetStatus("自动点赞等待辅助权限或作者名单，悬浮工具球仍可使用。");}
        }
    }
    public static void StopAuto(bool forget)
    {
        if(forget)Preferences.Set("phone.moments.watch",false);
        _instance?.SuspendWatch();
    }
    private static string[] Names()=>Preferences.Get("phone.moments.names","").Split('\n',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
    private void SuspendWatch(){_watchRunning=false;_watchRevision++;_watchPolicy.Suspend();_author=null;_menuAt=0;_autoId=null;_autoBounds=null;}
    private void ScheduleWatch(long revision,int milliseconds)
    {
        new Handler(Looper.MainLooper!).PostDelayed(()=>{
            if(!_watchRunning||revision!=_watchRevision)return;
            if(_author!=null&&SystemClock.ElapsedRealtime()>_deadline)Stop("本轮已超时，没有继续点击。");
            InspectWatch(true);ScheduleWatch(revision,15_000);
        },milliseconds);
    }
    private void ConsiderWatchEvent()
    {
        if(_watchRunning&&SystemClock.ElapsedRealtime()-_lastProbe>=1000)InspectWatch(false);
    }
    private static bool Unlocked(global::Android.Content.Context context)=>
        (context.GetSystemService(KeyguardService)as KeyguardManager)?.IsKeyguardLocked==false&&
        (context.GetSystemService(PowerService)as PowerManager)?.IsInteractive==true;
    private static Rect Bounds(AccessibilityNodeInfo node){var bounds=new Rect();node.GetBoundsInScreen(bounds);return bounds;}
    private bool LikeAction(AccessibilityNodeInfo node)
    {
        if(Label(node) is not ("赞" or "Like")||_likeMenuBounds==null)return false;
        var bounds=Bounds(node);var density=Resources?.DisplayMetrics?.Density??1;
        if(Math.Abs(bounds.CenterY()-_likeMenuBounds.CenterY())>80*density)return false;
        var target=Clickable(node);if(target==null||Nodes(target).Any(n=>Label(n) is "评论" or "Comment"))return false;
        var parent=node.Parent;
        for(int i=0;i<3&&parent!=null;i++,parent=parent.Parent){
            if(Bounds(parent).Height()>85*density)return false;
            if(Nodes(parent).Any(n=>Label(n) is "评论" or "Comment"))return true;
        }
        return false;
    }
    private static AccessibilityNodeInfo? Cover(IReadOnlyList<AccessibilityNodeInfo> nodes,Rect screen)=>nodes.FirstOrDefault(n=>
        n.ContentDescription?.ToString()?.Trim() is "封面" or "更换封面" or "更换相册封面" or "Change cover" or "Album cover" &&
        Bounds(n).Width()>screen.Width()*.65&&Bounds(n).Top<screen.Top+screen.Height()*.35);
    private MomentCard? Describe(AccessibilityNodeInfo author,AccessibilityNodeInfo root)
    {
        var authorName=Label(author);var container=author.Parent;var screen=Bounds(root);
        for(int depth=0;depth<5&&container!=null;depth++,container=container.Parent){
            if(Bounds(container).Height()>screen.Height()*.85)break;
            var contents=Nodes(container);var menus=contents.Where(Menu).ToArray();
            if(menus.Length>1)break;
            if(menus.Length!=1)continue;
            var menu=Clickable(menus[0]);if(menu==null)return null;
            var menuBounds=Bounds(menu);var bottom=menuBounds.Top+8;
            var recent=contents.Any(n=>(Label(n)=="刚刚"||Regex.IsMatch(Label(n),"^[0-8]?[0-9]秒前$"))&&Math.Abs(Bounds(n).CenterY()-menuBounds.CenterY())<=Math.Max(20,menuBounds.Height()));
            var texts=contents.Where(n=>n.ChildCount==0&&Bounds(n).Bottom<=bottom).Select(Label)
                .Where(t=>t.Length>0&&t!=authorName&&!Regex.IsMatch(t,@"^(刚刚|\d+\s*(秒|分钟|小时|天)前|昨天.*|\d{1,2}月\d{1,2}日.*|\d{4}年.*|赞|评论|删除|全文|收起)$"))
                .Distinct(StringComparer.Ordinal);
            var body=Regex.Replace(string.Join(" ",texts),@"\s+"," ").Trim();
            if(body.Length<2)return null; // Image-only/unknown cards are not safely identifiable.
            body=body[..Math.Min(80,body.Length)];
            return new(new(MomentWatchPolicy.Identity(authorName,body),authorName,recent),Bounds(author));
        }
        return null;
    }
    private void InspectWatch(bool refresh)
    {
        if(!_watchRunning||_inspecting||_author!=null)return;
        _inspecting=true;_lastProbe=SystemClock.ElapsedRealtime();
        try{
            var root=RootInActiveWindow;
            if(!PhoneOverlay.IsRunning||!Unlocked(this)||root?.PackageName?.ToString()!="com.tencent.mm"){
                _watchPolicy.Suspend();if(refresh)PhoneWorkspace.SetStatus("巡检等待中：请解锁手机并打开微信朋友圈。");return;
            }
            var nodes=Nodes(root);var screen=Bounds(root);var cover=Cover(nodes,screen);
            if(!nodes.Any(n=>Label(n) is "朋友圈" or "Moments")||cover==null){
                _watchPolicy.Suspend();if(refresh)PhoneWorkspace.SetStatus("巡检等待中：请回朋友圈顶部；当前版本未暴露封面控件时不会操作。");return;
            }
            if(nodes.Any(n=>Label(n) is "赞" or "取消赞" or "Unlike"))return;
            var names=Names();var cards=nodes.Where(n=>names.Contains(Label(n),StringComparer.Ordinal)).Select(n=>Describe(n,root)).Where(c=>c!=null).Select(c=>c!).DistinctBy(c=>c.Post.Id).OrderBy(c=>c.AuthorBounds.Top).ToArray();
            var context=root.WindowId+":"+string.Join("\n",names.Order(StringComparer.Ordinal));
            if(SystemClock.ElapsedRealtime()-_lastAttempt>=15_000){
                var post=_watchPolicy.Observe(cards.Select(c=>c.Post).ToArray(),true,context,names);
                if(post!=null){
                    var card=cards.Single(c=>c.Post.Id==post.Id);_lastAttempt=SystemClock.ElapsedRealtime();
                    _author=post.Author;_autoId=post.Id;_autoBounds=card.AuthorBounds;_menuAt=0;_deadline=SystemClock.ElapsedRealtime()+8000;
                    new Handler(Looper.MainLooper!).PostDelayed(()=>Scan(),250);PhoneWorkspace.SetStatus("发现所选作者的新近文字动态，正在核对点赞菜单…");return;
                }
            }
            if(refresh&&OperatingSystem.IsAndroidVersionAtLeast(24)){
                var start=Math.Max(screen.Top+screen.Height()*.18,Bounds(cover).Bottom+12);var end=Math.Min(screen.Bottom-60,start+screen.Height()*.24);
                if(end-start<60)return;
                var path=new global::Android.Graphics.Path();path.MoveTo(screen.CenterX(),(float)start);path.LineTo(screen.CenterX(),(float)end);
                var builder=new GestureDescription.Builder();builder.AddStroke(new GestureDescription.StrokeDescription(path,0,450));
                var gesture=builder.Build();if(gesture==null)return;
                if(!DispatchGesture(gesture,null,null))PhoneWorkspace.SetStatus("系统未接受朋友圈刷新手势，请检查辅助权限。");
                else{var revision=_watchRevision;new Handler(Looper.MainLooper!).PostDelayed(()=>{if(_watchRunning&&revision==_watchRevision)InspectWatch(false);},1400);}
            }
        }
        catch(Exception){_watchPolicy.Suspend();PhoneWorkspace.SetStatus("当前朋友圈界面无法确认，巡检已等待，不会盲点。");}
        finally{_inspecting=false;}
    }
}
