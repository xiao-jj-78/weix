namespace WeChatLikeMaui.Services;

public static class PhonePlatform
{
    public static bool HasNotificationAccess {
        get {
#if ANDROID
            var context = global::Android.App.Application.Context;
            var enabled = global::Android.Provider.Settings.Secure.GetString(context.ContentResolver,"enabled_notification_listeners") ?? "";
            return enabled.Split(':').Any(x => x == "com.wechatlike.maui/com.wechatlike.maui.PhoneNotificationService");
#else
            return false;
#endif
        }
    }
    public static void OpenNotificationSettings() {
#if ANDROID
        Open(new global::Android.Content.Intent("android.settings.ACTION_NOTIFICATION_LISTENER_SETTINGS"));
#else
        throw new InvalidOperationException("通知接入仅在安卓手机可用。");
#endif
    }
    public static void OpenWeChat() {
#if ANDROID
        var context = global::Android.App.Application.Context;
        var intent = context.PackageManager?.GetLaunchIntentForPackage("com.tencent.mm") ?? throw new InvalidOperationException("未找到手机官方微信，请先安装微信。");
        Open(intent);
#else
        throw new InvalidOperationException("此入口用于打开安卓手机微信。");
#endif
    }
    public static void OpenAppSettings() {
#if ANDROID
        var context = global::Android.App.Application.Context;
        Open(new global::Android.Content.Intent("android.settings.APPLICATION_DETAILS_SETTINGS", global::Android.Net.Uri.Parse("package:"+context.PackageName)));
#endif
    }
    public static Task ReplyAsync(PhoneMessage message, string text) {
#if ANDROID
        return Platforms.Android.PhoneNotificationService.ReplyAsync(message,text);
#else
        throw new InvalidOperationException("通知回复需要在安卓手机上使用。");
#endif
    }
#if ANDROID
    private static void Open(global::Android.Content.Intent intent) {
        intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
        global::Android.App.Application.Context.StartActivity(intent);
    }
#endif
}
