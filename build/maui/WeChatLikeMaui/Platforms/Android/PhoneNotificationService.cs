using Android.App;
using Android.Content;
using Android.OS;
using Android.Service.Notification;
using WeChatLikeMaui.Services;

namespace WeChatLikeMaui.Platforms.Android;

[Service(Name="com.wechatlike.maui.PhoneNotificationService", Label="微信工作台 · 手机消息", Permission="android.permission.BIND_NOTIFICATION_LISTENER_SERVICE", Exported=true)]
[IntentFilter(new[] { "android.service.notification.NotificationListenerService" })]
public sealed class PhoneNotificationService : NotificationListenerService
{
    private static PhoneNotificationService? _instance;
    private readonly HashSet<string> _attempted = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _serial = new(1,1);
    private long _connectedAt;
    private readonly CancellationTokenSource _lifetime = new();
    public override void OnListenerConnected() {
        base.OnListenerConnected(); _instance=this; _connectedAt=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        PhoneWorkspace.ListenerConnected=true;
        // A service restart never resumes unattended sending without the user.
        PhoneWorkspace.DisableAuto();
        PhoneWorkspace.SetStatus("通知已接入，等待新的微信消息。不会补发历史通知。");
    }
    public override void OnListenerDisconnected() {
        PhoneWorkspace.ListenerConnected=false; _instance=null; PhoneWorkspace.DisableAuto();
        if (OperatingSystem.IsAndroidVersionAtLeast(24)) base.OnListenerDisconnected();
    }
    public override void OnDestroy() {
        _lifetime.Cancel(); PhoneWorkspace.ListenerConnected=false; _instance=null; PhoneWorkspace.DisableAuto();
        base.OnDestroy();
    }
    private static PhoneMessage? Read(StatusBarNotification sbn) {
        if (sbn.PackageName != "com.tencent.mm") return null;
        var n=sbn.Notification;
        if (n == null || (n.Flags & NotificationFlags.GroupSummary) != 0) return null;
        var extras=n.Extras;
        var title=extras?.GetCharSequence(Notification.ExtraTitle)?.ToString()?.Trim() ?? "";
        var text=extras?.GetCharSequence(Notification.ExtraText)?.ToString()?.Trim() ?? "";
        if (title.Length==0 || text.Length==0 || title.Length>100 || text.Length>8000) return null;
        // Replies echoed by Android are not new incoming messages.
        if (OperatingSystem.IsAndroidVersionAtLeast(24) && extras?.GetCharSequenceArray(Notification.ExtraRemoteInputHistory)?.Length > 0) return null;
        bool reply = n.Actions?.Any(a => a.GetRemoteInputs()?.Any(r => r.AllowFreeFormInput) == true && a.ActionIntent != null) == true;
        var key=sbn.Key ?? "";
        return new(PhoneRules.Fingerprint(key,title,text,sbn.PostTime),key,title,text,sbn.PostTime,reply);
    }
    public override void OnNotificationPosted(StatusBarNotification? sbn) {
        if (sbn==null || sbn.PostTime<_connectedAt) return;
        var message=Read(sbn); if(message==null)return;
        PhoneWorkspace.Put(message);
        if (message.CanReply && Preferences.Get("phone.auto",false) && PhoneRules.Allowed(message.Title,PhoneWorkspace.Targets))
            _=AutoAsync(message);
    }
    public override void OnNotificationRemoved(StatusBarNotification? sbn) {
        if(sbn?.PackageName=="com.tencent.mm" && sbn.Key is { } key)PhoneWorkspace.Remove(key);
    }
    private async Task AutoAsync(PhoneMessage message) {
        // No queued automatic backlog: one in-flight generation at a time.
        if(!await _serial.WaitAsync(0)) { PhoneWorkspace.SetStatus("有新消息到达，当前自动回复忙碌；请在消息列表手动处理。"); return; }
        try {
            if(!Preferences.Get("phone.auto",false) || !PhoneRules.Allowed(message.Title,PhoneWorkspace.Targets))return;
            if(!_attempted.Add(message.Id))return;
            if(_attempted.Count>2048){ PhoneWorkspace.DisableAuto(); return; }
            var answer=await PhoneWorkspace.GenerateAsync(message.Text,_lifetime.Token);
            if(!Preferences.Get("phone.auto",false) || !PhoneRules.Allowed(message.Title,PhoneWorkspace.Targets))return;
            await ReplyAsync(message,answer);
            PhoneWorkspace.SetStatus("已提交一条通知回复，送达状态请在官方微信查看。");
        }
        catch(System.OperationCanceledException){PhoneWorkspace.SetStatus("自动回复已取消或超时。");}
        catch(Exception){ PhoneWorkspace.DisableAuto(); PhoneWorkspace.SetStatus("自动回复未完成，已关闭；请手动检查接口或通知状态。"); }
        finally{_serial.Release();}
    }
    private readonly object _sendGate = new();
    public static Task ReplyAsync(PhoneMessage expected,string text) {
        if(string.IsNullOrWhiteSpace(text)||text.Length>8000)throw new InvalidOperationException("回复应为 1–8000 字。");
        var service=_instance ?? throw new InvalidOperationException("通知服务已断开，请重新开启通知使用权。");
        lock(service._sendGate) {
            if(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-expected.PostedAt>600_000)throw new InvalidOperationException("通知超过 10 分钟，请回微信确认后回复。");
            var sbn=service.GetActiveNotifications()?.SingleOrDefault(n=>n.Key==expected.NotificationKey);
            var current=sbn==null?null:Read(sbn);
            if(current==null||!PhoneRules.Same(expected,current))throw new InvalidOperationException("通知已更新或消失，请重新选择最新消息生成回复。");
            var receipts=Preferences.Get("phone.reply.receipts","").Split('\n',StringSplitOptions.RemoveEmptyEntries).ToList();
            if(receipts.Contains(expected.Id,StringComparer.Ordinal))throw new InvalidOperationException("这条通知已处理，不会重复发送。");
            var action=sbn!.Notification!.Actions!.First(a=>a.GetRemoteInputs()?.Any(r=>r.AllowFreeFormInput)==true && a.ActionIntent!=null);
            var inputs=action.GetRemoteInputs()!.Where(r=>r.AllowFreeFormInput).ToArray();
            // Store only hashed receipts before invoking the remote action; never
            // retry an uncertain send, including across app/service restarts.
            receipts.Add(expected.Id); if(receipts.Count>512)receipts.RemoveRange(0,receipts.Count-512);
            Preferences.Set("phone.reply.receipts",string.Join('\n',receipts));
            using var intent=new Intent(); using var results=new Bundle();
            foreach(var input in inputs)results.PutCharSequence(input.ResultKey,text);
            RemoteInput.AddResultsToIntent(inputs,intent,results);
            action.ActionIntent!.Send(service,0,intent);
            PhoneWorkspace.Remove(expected.NotificationKey);
        }
        return Task.CompletedTask;
    }
}
