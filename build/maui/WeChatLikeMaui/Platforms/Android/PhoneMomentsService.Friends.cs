using WeChatLikeMaui.Services;
using Android.Views.Accessibility;

namespace WeChatLikeMaui.Platforms.Android;

public sealed partial class PhoneMomentsService
{
    public static FriendFrame CaptureFriends()
    {
        var instance=_instance??throw new InvalidOperationException("请先在系统中开启朋友圈辅助权限，才能识别可见好友。");
        if(!Unlocked(instance))throw new InvalidOperationException("请先解锁手机。");
        // Choosing friends must never run alongside an already armed like operation.
        instance.SuspendWatch();
        var root=instance.RootInActiveWindow;
        if(root?.PackageName?.ToString()!="com.tencent.mm")root=instance.Windows?.Where(w=>w.IsFocused).Select(w=>w.Root).FirstOrDefault(n=>n?.PackageName?.ToString()=="com.tencent.mm");
        if(root?.PackageName?.ToString()!="com.tencent.mm")throw new InvalidOperationException("请收起工具球并打开微信通讯录或朋友圈，再点识别当前页。");
        var snapshots=new List<FriendNode>();
        void Visit(AccessibilityNodeInfo node,int parent,int depth){
            if(snapshots.Count>=600||depth>20)return;
            if(!node.VisibleToUser)return;
            var id=snapshots.Count;var bounds=Bounds(node);
            snapshots.Add(new(id,parent,Label(node),node.ContentDescription?.ToString()?.Trim()??"",node.ClassName?.ToString()??"",node.Clickable&&node.Enabled,bounds.Left,bounds.Top,bounds.Right,bounds.Bottom));
            for(int i=0;i<Math.Min(80,node.ChildCount);i++){var child=node.GetChild(i);if(child!=null)Visit(child,id,depth+1);}
        }
        Visit(root,-1,0);var screen=Bounds(root);
        return FriendPickerRules.Extract("com.tencent.mm",true,snapshots,screen.Width(),screen.Height(),instance.Resources?.DisplayMetrics?.Density??1);
    }
}
