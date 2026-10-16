using Android.App;
using Android.Content.PM;
using Android.OS;

namespace WeChatLikeMaui;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnNewIntent(global::Android.Content.Intent? intent)
    {
        base.OnNewIntent(intent);Intent=intent;ReadAssistantSection();
    }
    private void ReadAssistantSection()
    {
        var section=Intent?.GetStringExtra("phone.section");
        if(section is not ("home" or "setup" or "ai" or "moments" or "messages"))return;
        Intent!.RemoveExtra("phone.section");Services.PhoneOverlay.RequestSection(section);
    }
    protected override void OnResume()
    {
        base.OnResume();ReadAssistantSection();
        MainThread.BeginInvokeOnMainThread(async()=>{await Task.Delay(250);await Services.PhoneOverlay.ResumeRequestedLaunchAsync();});
    }
}
