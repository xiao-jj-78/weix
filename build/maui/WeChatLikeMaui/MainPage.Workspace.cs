using System.Net;
using System.Text.Json;
using WeChatLikeMaui.Services;

namespace WeChatLikeMaui;

public partial class MainPage
{
    private const string CloudHome = "https://jou-d4gq7a3cqf51e6caa-1394316826.tcloudbaseapp.com/wechat-workspace/index.html";
    private readonly CompanionWindows _companion = new();
    private readonly Grid _companionLayout = new() { ColumnSpacing = 16, Padding = 16 };
    private readonly VerticalStackLayout _clientCards = new() { Spacing = 12 };
    private readonly Label _clientsMessage = new() { Text = "连接电脑并启动 SDK 后，刷新微信窗口。", FontSize = 12, LineBreakMode = LineBreakMode.WordWrap };
    private readonly Label _browserMessage = new() { Text = "选择云端工作台或本机网页开始。", FontSize = 12 };
    private readonly WebView _cloudWeb = new() { AutomationId = "CloudBrowser" };
    private readonly Picker _workspaceMode = new() { Title = "显示区域", ItemsSource = new[] { "云端浏览器", "多微信管理" }, SelectedIndex = 0 };
    private ScrollView _clientPane = null!;
    private Grid _browserPane = null!;
    private bool _clientsBusy, _workspaceReady;
    private string _browserUrl = CloudHome;
    private Button WorkspaceButton(string title, string id, EventHandler handler)
    {
        var button = new Button { Text = title, AutomationId = id, FontSize = 12, Padding = new Thickness(12, 8), Margin = new Thickness(0, 0, 6, 6), MinimumHeightRequest = 44,
            Style = (Style)Application.Current!.Resources["SecondaryButton"] };
        button.Clicked += handler; return button;
    }
    private void InitializeWorkspace()
    {
        var actions = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Start };
        actions.Children.Add(WorkspaceButton("刷新窗口", "ClientsRefresh", async (_, _) => await RefreshClientsAsync()));
        actions.Children.Add(WorkspaceButton("启动 SDK", "ClientsStartSdk", async (_, _) => await ClientAction("start", new { })));
        actions.Children.Add(WorkspaceButton("打开 / 继续使用微信", "ClientsOpen", async (_, _) => { await OpenWechatSimplyAsync(); _clientsMessage.Text=QuickWechatMessage.Text; await RefreshClientsAsync(); }));
        actions.Children.Add(WorkspaceButton("读取已登录账号", "ClientsVerify", async (_, _) => await ClientAction("clients/verify", new { })));
        actions.Children.Add(WorkspaceButton("恢复窗口位置", "ClientsRestore", (_, _) => { _companion.Dispose(); _clientsMessage.Text = "已恢复本次伴随布局前的窗口位置。"; }));
        actions.Children.Add(WorkspaceButton("连接设置", "ClientsConnection", (_, e) => OnTabClicked(DashboardTab, e)));
        var clients = new VerticalStackLayout { Spacing = 14 };
        clients.Children.Add(new Label { Text = "多微信管理", FontSize = 21, FontAttributes = FontAttributes.Bold });
        clients.Children.Add(new Label { Text = "优先复用官方登录状态。只显示同一桌面中有主窗口的进程；进程数量不等于账号数量。", FontSize = 12, TextColor = Color.FromArgb("#61757B") });
        clients.Children.Add(actions); clients.Children.Add(_clientsMessage); clients.Children.Add(_clientCards);
        _clientPane = new ScrollView { Content = clients };
        var browserActions = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        browserActions.Children.Add(WorkspaceButton("云端工作台", "BrowserCloud", (_, _) => Browse(CloudHome)));
        browserActions.Children.Add(WorkspaceButton("本机网页", "BrowserLocal", (_, _) => { if (_connected) Browse(_api.BaseUrl + "/"); else _browserMessage.Text = "请先在连接设置中连接电脑端。"; }));
#if WINDOWS
        browserActions.Children.Add(WorkspaceButton("复制云访问码", "BrowserCopyCode", async (_, _) =>
        {
            try { await CloudAccessCode.CopyAsync(); _browserMessage.Text = "已复制云访问码，请在网页粘贴。60 秒后会清理本次剪贴板。"; }
            catch (Exception) { _browserMessage.Text = "无法读取本机云访问码，请先使用交付包的“连接腾讯云”完成配对。"; }
        }));
#endif
        browserActions.Children.Add(WorkspaceButton("后退", "BrowserBack", (_, _) => { if (_cloudWeb.CanGoBack) _cloudWeb.GoBack(); }));
        browserActions.Children.Add(WorkspaceButton("前进", "BrowserForward", (_, _) => { if (_cloudWeb.CanGoForward) _cloudWeb.GoForward(); }));
        browserActions.Children.Add(WorkspaceButton("刷新", "BrowserReload", (_, _) => { if (_cloudWeb.Source is not null) _cloudWeb.Reload(); }));
        browserActions.Children.Add(WorkspaceButton("恢复伴随", "BrowserRestore", (_, _) => { _companion.Dispose(); _browserMessage.Text = "已恢复本次伴随布局前的窗口位置。"; }));
        browserActions.Children.Add(WorkspaceButton("系统浏览器打开", "BrowserExternal", async (_, _) => { try { await Launcher.OpenAsync(_browserUrl); } catch (Exception ex) { _browserMessage.Text = ex.Message; } }));
        _browserPane = new Grid { RowDefinitions = new RowDefinitionCollection(new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)), RowSpacing = 10 };
        _browserPane.Children.Add(browserActions); _browserPane.Children.Add(_browserMessage); Grid.SetRow(_browserMessage, 1);
        _browserPane.Children.Add(_cloudWeb); Grid.SetRow(_cloudWeb, 2);
        _cloudWeb.Navigating += (_, e) =>
        {
            if (e.Url == "about:blank") return;
            if (!Uri.TryCreate(e.Url, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo) ||
                !(uri.Scheme == "https" || (uri.Scheme == "http" && _connected && Uri.TryCreate(_api.BaseUrl, UriKind.Absolute, out var local) && uri.Host == local.Host && uri.Port == local.Port)))
            { e.Cancel = true; _browserMessage.Text = "此页面需要在系统浏览器打开。内置浏览器仅加载 HTTPS 和已连接的本机服务。"; return; }
            _browserMessage.Text = "正在载入 " + uri.Host + "…";
        };
        _cloudWeb.Navigated += (_, e) => _browserMessage.Text = e.Result == WebNavigationResult.Success ? "这是远程管理网页，访问码用于保护控制台。微信在官方客户端登录；本机使用不需要云访问码。" : "页面未能载入，可刷新或在系统浏览器打开。";
        _workspaceMode.SelectedIndexChanged += (_, _) => LayoutCompanion();
        _companionLayout.Children.Add(_clientPane); _companionLayout.Children.Add(_browserPane);
        var container = new Grid { RowDefinitions = new RowDefinitionCollection(new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)) };
        container.Children.Add(_workspaceMode); container.Children.Add(_companionLayout); Grid.SetRow(_companionLayout, 1);
        CompanionView.Children.Add(container); _workspaceReady = true; LayoutCompanion();
    }
    private void LayoutCompanion()
    {
        if (!_workspaceReady) return;
        bool wide = Width >= 1050;
        _workspaceMode.IsVisible = !wide;
        _companionLayout.ColumnDefinitions.Clear(); _companionLayout.ColumnDefinitions.Add(new ColumnDefinition(wide ? new GridLength(340) : GridLength.Star));
        if (wide) _companionLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        Grid.SetColumn(_browserPane, wide ? 1 : 0);
        _clientPane.IsVisible = wide || _workspaceMode.SelectedIndex == 1;
        _browserPane.IsVisible = wide || _workspaceMode.SelectedIndex == 0;
    }
    private void Browse(string url)
    { _browserUrl = url; _cloudWeb.Source = url; }
    private async Task ClientAction(string action, object body)
    {
        if (_clientsBusy) return;
        if (!_connected) { _clientsMessage.Text = "请先打开连接设置，连接运行微信的电脑。"; return; }
        _clientsBusy = true; _clientsMessage.Text = action == "clients/verify" ? "正在通过 SDK 读取账号与可见会话，可能切换官方微信窗口…" : "正在处理…";
        try
        {
            var result = await _api.SdkActionAsync(action, body, Token);
            _clientsMessage.Text = Text(result, "message", "操作完成，正在刷新列表。");
            if (action == "start") await RefreshSdkAsync(Token);
            await ReadClientCards(Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _clientsMessage.Text = ex.Message; }
        finally { _clientsBusy = false; }
    }
    private async Task RefreshClientsAsync()
    {
        if (_clientsBusy) return;
        if (!_connected) { _clientsMessage.Text = "请先在连接设置中连接运行微信的电脑。"; return; }
        _clientsBusy = true;
        try { await ReadClientCards(Token); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _clientsMessage.Text = "窗口列表不可用：" + ex.Message; }
        finally { _clientsBusy = false; }
    }
    private async Task ReadClientCards(CancellationToken token)
    {
        var result = await _api.SdkReadAsync("clients", token);
        token.ThrowIfCancellationRequested(); _clientCards.Children.Clear();
        foreach (var client in result.GetProperty("clients").EnumerateArray())
        {
            var id = Text(client, "instanceId"); var alias = Text(client, "alias");
            var card = new VerticalStackLayout { Spacing = 8 };
            card.Children.Add(new Label { Text = alias.Length > 0 ? alias : "微信 · PID " + Text(client, "pid"), FontSize = 16, FontAttributes = FontAttributes.Bold });
            card.Children.Add(new Label { Text = Text(client, "name") + " " + Text(client, "version") + " · PID " + Text(client, "pid"), FontSize = 12 });
            var accountText = "登录尚未验证";
            if (client.TryGetProperty("account", out var account) && account.ValueKind == JsonValueKind.Object)
                accountText = "上次 SDK 读到：" + Text(account, "nickname", Text(account, "name")) + "\n时间：" + Text(client, "verifiedAt");
            card.Children.Add(new Label { Text = accountText, FontSize = 12, TextColor = Color.FromArgb("#61757B") });
            var buttons = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
            buttons.Children.Add(WorkspaceButton("显示窗口", "ClientFocus-" + Text(client, "pid"), async (_, _) => await ClientAction("clients/focus", new { instanceId = id })));
            buttons.Children.Add(WorkspaceButton("备注", "ClientAlias-" + Text(client, "pid"), async (_, _) =>
            {
                var value = await DisplayPromptAsync("窗口备注", "仅标记本次进程，避免混淆多个微信。", "保存", "取消", initialValue: alias, maxLength: 60);
                if (value is not null) await ClientAction("clients/alias", new { instanceId = id, alias = value });
            }));
#if WINDOWS
            buttons.Children.Add(WorkspaceButton("伴随排列", "ClientCompanion-" + Text(client, "pid"), (_, _) =>
            {
                try
                {
                    if (!(IPAddress.TryParse(_api.Host, out var ip) && IPAddress.IsLoopback(ip)) && !_api.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("远程微信只能在对应电脑显示窗口；伴随排列仅支持本机连接。");
                    _companion.Arrange(Window, id); _clientsMessage.Text = "工作台与官方微信已并排；恢复按钮或退出本程序可还原窗口位置。";
                }
                catch (Exception ex) { _clientsMessage.Text = ex.Message; }
            }));
#endif
            card.Children.Add(buttons); _clientCards.Children.Add(new Border { Style = (Style)Application.Current!.Resources["Panel"], Content = card });
        }
        if (_clientCards.Children.Count == 0) _clientCards.Children.Add(new Label { Text = "尚未发现有主窗口的微信。可点击“打开官方微信”；多开能力由客户端决定。", FontSize = 13 });
    }
    protected override bool OnBackButtonPressed()
    {
        if (_tab == "companion" && _cloudWeb.CanGoBack) { _cloudWeb.GoBack(); return true; }
        return base.OnBackButtonPressed();
    }
    public void RestoreCompanion() { _companion.Dispose(); CloudAccessCode.ClearIfOwned(); }
}
