using Microsoft.Extensions.DependencyInjection;

namespace WeChatLikeMaui;

public partial class App : Application
{
	public App()
	{
#if WINDOWS
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(FileSystem.AppDataDirectory, "CloudBrowser"));
#endif
		InitializeComponent();
		UserAppTheme = AppTheme.Light;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
#if ANDROID
        var window = new Window(new PhonePage()) { Title = "微信工作台 · 手机独立版" };
#else
		var phonePreview = Environment.GetCommandLineArgs().Contains("--phone-preview");
		var window = new Window(phonePreview ? new PhonePage() : new AppShell()) { Title = "微信自动化工作台", Width = phonePreview ? 440 : 1240, Height = 900, MinimumWidth = 360, MinimumHeight = 640 };
#endif
		window.Stopped += (_, _) => { if (window.Page is Shell { CurrentPage: MainPage page }) page.SuspendPolling(); };
		window.Resumed += (_, _) => { if (window.Page is Shell { CurrentPage: MainPage page }) page.ResumePolling(); };
		window.Destroying += (_, _) => { if (window.Page is Shell { CurrentPage: MainPage page }) page.RestoreCompanion(); Services.LocalConsoleHost.StopOwned(); };
		return window;
	}
}
