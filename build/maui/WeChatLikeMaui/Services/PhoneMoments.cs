namespace WeChatLikeMaui.Services;
public static class PhoneMoments
{
    public static void OpenSettings() {
#if ANDROID
        var intent=new global::Android.Content.Intent(global::Android.Provider.Settings.ActionAccessibilitySettings);
        intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
        global::Android.App.Application.Context.StartActivity(intent);
#else
        throw new InvalidOperationException("朋友圈本地辅助需要在安卓手机启用。");
#endif
    }
    public static void Arm(string author) {
#if ANDROID
        Platforms.Android.PhoneMomentsService.Arm(author);
#else
        throw new InvalidOperationException("朋友圈本地辅助需要在安卓手机使用。");
#endif
    }
    public static bool AutoRunning {
        get {
#if ANDROID
            return Platforms.Android.PhoneMomentsService.AutoRunning;
#else
            return false;
#endif
        }
    }
    public static bool RememberAuto=>Preferences.Get("phone.moments.watch",false);
    public static void StartAuto(){
#if ANDROID
        Platforms.Android.PhoneMomentsService.StartAuto();
#else
        throw new InvalidOperationException("自动巡检需要在安卓手机使用。");
#endif
    }
    public static void ResumeRemembered(){
#if ANDROID
        Platforms.Android.PhoneMomentsService.ResumeRemembered();
#endif
    }
    public static void StopAuto(bool forget=true){
        if(forget)Preferences.Set("phone.moments.watch",false);
#if ANDROID
        Platforms.Android.PhoneMomentsService.StopAuto(forget);
#endif
    }
    public static void Cancel() {
#if ANDROID
        Platforms.Android.PhoneMomentsService.Cancel();
#endif
        PhoneWorkspace.SetStatus("已取消本轮朋友圈辅助。");
    }
}
