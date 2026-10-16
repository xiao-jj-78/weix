namespace WeChatLikeMaui.Services;

public static class PhoneOverlay
{
    public static bool IsRunning { get; internal set; }
    public static bool HasPermission {
        get {
#if ANDROID
            return !OperatingSystem.IsAndroidVersionAtLeast(23)||global::Android.Provider.Settings.CanDrawOverlays(global::Android.App.Application.Context);
#else
            return false;
#endif
        }
    }
    public static event Action<string>? SectionRequested;
    private static string? _pendingSection;
    public static void RequestSection(string value){_pendingSection=value;SectionRequested?.Invoke(value);}
    public static string? TakeSection(){var value=_pendingSection;_pendingSection=null;return value;}
    private static bool _resumeLaunch;
    #if ANDROID
    private static bool _starting;
    #endif
    private static TaskCompletionSource<bool>? _ready=null;
    public static void MarkReady(bool success){_ready?.TrySetResult(success);}
    public static void RequestPermissionAndContinue(bool launchOnReturn=true)
    {
#if ANDROID
        if(!OperatingSystem.IsAndroidVersionAtLeast(23))return;
        _resumeLaunch=launchOnReturn;
        var context=global::Android.App.Application.Context;
        var intent=new global::Android.Content.Intent(global::Android.Provider.Settings.ActionManageOverlayPermission,global::Android.Net.Uri.Parse("package:"+context.PackageName));
        intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);context.StartActivity(intent);
#else
        throw new InvalidOperationException("悬浮工具球需要在安卓手机开启。");
#endif
    }
    public static async Task ResumeRequestedLaunchAsync()
    {
        if(!_resumeLaunch)return;
        _resumeLaunch=false;
        if(!HasPermission){PhoneWorkspace.SetStatus("未开启悬浮显示，可在首次设置中重试。");return;}
        try{await StartAndOpenAsync();}
        catch(Exception){PhoneWorkspace.SetStatus("未能启动悬浮助手，请检查小米系统的悬浮及后台权限。");}
    }
    public static async Task StartAndOpenAsync()
    {
#if ANDROID
        if(_starting)return;
        if(!HasPermission)throw new InvalidOperationException("请先允许显示在其他应用上层。");
        _starting=true;
        try{
            if(!IsRunning){
                // Request from the visible activity, never via a boot receiver.
                if(OperatingSystem.IsAndroidVersionAtLeast(33)&&!Preferences.Get("phone.overlay.notificationAsked",false)){await Permissions.RequestAsync<Permissions.PostNotifications>();Preferences.Set("phone.overlay.notificationAsked",true);}
                _ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
                var context=global::Android.App.Application.Context;
                var intent=new global::Android.Content.Intent(context,typeof(Platforms.Android.PhoneOverlayService));
                if(OperatingSystem.IsAndroidVersionAtLeast(26))context.StartForegroundService(intent);else context.StartService(intent);
                try{
                    if(!await _ready.Task.WaitAsync(TimeSpan.FromSeconds(12)))throw new InvalidOperationException("系统未允许悬浮工具球显示，请检查应用权限。");
                }catch{context.StopService(intent);throw;}
            }
            PhonePlatform.OpenWeChat();
            PhoneWorkspace.SetStatus("悬浮工具球已显示，点工具球使用 AI 或朋友圈辅助。");
        }finally{_starting=false;}
#else
        await Task.CompletedTask;throw new InvalidOperationException("悬浮工具球需要在安卓手机使用。");
#endif
    }
    public static void Stop()
    {
        _resumeLaunch=false;PhoneWorkspace.DisableAuto();PhoneMoments.Cancel();
#if ANDROID
        var context=global::Android.App.Application.Context;
        context.StopService(new global::Android.Content.Intent(context,typeof(Platforms.Android.PhoneOverlayService)));
#endif
        PhoneWorkspace.SetStatus("助手已停止，自动回复与本轮朋友圈辅助已取消。");
    }
}
