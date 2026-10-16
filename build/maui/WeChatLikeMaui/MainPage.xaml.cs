using System.Collections.ObjectModel;
using System.Text.Json;
using WeChatLikeMaui.Models;
using WeChatLikeMaui.Services;

namespace WeChatLikeMaui;

public partial class MainPage : ContentPage
{
    private readonly ConsoleApi _api = new();
    private readonly ObservableCollection<TaskItem> _tasks = [];
    private readonly IDispatcherTimer _timer;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly SemaphoreSlim _deviceGate = new(1, 1);
    private readonly SemaphoreSlim _sdkGate = new(1, 1);
    private CancellationTokenSource _session = new();
    private string _logTask = "";
    private bool _connected, _busy, _sdkRunning, _sdkBusy;
    private int _cycle;
    private string _tab = "dashboard";

    public MainPage()
    {
        InitializeComponent();
        InitializeAi();
        InitializeWorkspace();
        MomentsContactsSelector.SetAutomationPrefix("Moments");
        BindableLayout.SetItemsSource(TaskList, _tasks);
        IpEntry.Text = Preferences.Get("ip", DeviceInfo.Platform == DevicePlatform.WinUI ? "127.0.0.1" : "");
        PortEntry.Text = Preferences.Get("port", "8765");
#if WINDOWS
        LocalBtn.IsVisible = true;
        QuickWechatPanel.IsVisible = true;
        ConnectionPanel.IsVisible = false;
#endif
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(2.5);
        _timer.Tick += async (_, _) => await RefreshAsync();
        SetConnected(false);
    }

    private CancellationToken Token => _session.Token;
    private void NewSession()
    {
        _session.Cancel();
        _session.Dispose();
        _session = new CancellationTokenSource();
    }
    private void Notice(string message, bool error = true)
    {
        NoticeLabel.Text = message;
        NoticeLabel.TextColor = Color.FromArgb(error ? "#914138" : "#14725D");
        NoticeLabel.IsVisible = !string.IsNullOrWhiteSpace(message);
    }
    private void SetBusy(bool value)
    {
        _busy = value;
        ConnectionBusy.IsVisible = value;
        ConnectionBusy.IsRunning = value;
        ConnectBtn.IsEnabled = LocalBtn.IsEnabled = IpEntry.IsEnabled = PortEntry.IsEnabled = !value;
        DisconnectBtn.IsEnabled = !value;
        UpdateAvailability();
    }
    private void SetConnected(bool connected)
    {
        _connected = connected;
        ConnectionBadge.Text = connected ? "已连接" : "未连接";
        ConnectionBadge.TextColor = Color.FromArgb(connected ? "#9CE0BF" : "#E0EBE7");
        ServerState.Text = connected ? "在线" : "离线";
        ConnectBtn.Text = connected ? "重新连接" : "连接电脑";
        DisconnectBtn.IsVisible = connected;
        foreach (var item in _tasks) item.SetAvailable(connected && !_busy);
        if (!connected)
        {
            RunningCount.Text = DeviceCount.Text = "—";
            DeviceSummary.Text = "等待连接";
            TaskHint.Text = "离线";
            _sdkRunning = false;
            SdkBadge.Text = "未连接";
            _aiLoaded = false;
            _aiRunning = false;
            AiBadge.Text = "未连接";
        }
        UpdateAvailability();
    }
    private void UpdateAvailability()
    {
        bool available = _connected && !_busy;
        LogTaskBtn.IsEnabled = available && _tasks.Count > 0;
        DevicesBtn.IsEnabled = SaveConfigBtn.IsEnabled = ReloadConfigBtn.IsEnabled = available;
        SdkRefreshBtn.IsEnabled = available && !_sdkBusy;
        SdkStartBtn.IsEnabled = available && !_sdkBusy && !_sdkRunning;
        SdkStopBtn.IsEnabled = available && !_sdkBusy && _sdkRunning;
        SdkLoginBtn.IsEnabled = !_busy && !_sdkBusy && !_quickWechatBusy;
        PluginList.IsEnabled = available && !_sdkBusy && _sdkRunning;
        MomentsContactsSelector.IsEnabled = available;
        MomentsContactsBtn.IsEnabled = available && _sdkRunning && !_contactsBusy;
        UpdateAiAvailability();
        foreach (var item in _tasks) item.SetAvailable(available);
    }

    private async void OnConnectClicked(object? sender, EventArgs e) => await ConnectAsync(false);
    private async void OnLaunchLocalClicked(object? sender, EventArgs e) => await ConnectAsync(true);

    private async Task ConnectAsync(bool launch)
    {
        if (_busy) return;
        _timer.Stop();
        NewSession();
        SetConnected(false);
        SetBusy(true);
        Notice("");
        var token = Token;
        try
        {
            if (launch) IpEntry.Text = "127.0.0.1";
            _api.Configure(IpEntry.Text ?? "", PortEntry.Text ?? "8765");
            StatusLabel.Text = launch ? "正在启动本机服务，首次启动可能需要几十秒…" : "正在连接电脑端…";
            if (launch) await LocalConsoleHost.EnsureStartedAsync(int.Parse(_api.Port), token);
            await _api.PingAsync(token);
            token.ThrowIfCancellationRequested();
            SetConnected(true);
            Preferences.Set("ip", _api.Host);
            Preferences.Set("port", _api.Port);
            EndpointLabel.Text = _api.BaseUrl;
            await RefreshAsync(force: true);
            if (!_connected) return;
            await LoadConfigAsync(token);
            await RefreshDevicesAsync(token);
            await RefreshSdkAsync(token);
            if (_sdkRunning) await LoadAiAsync(token);
            token.ThrowIfCancellationRequested();
            _timer.Start();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { SetConnected(false); StatusLabel.Text = "连接失败"; Notice(ex.Message); }
        finally { SetBusy(false); }
    }

    private void OnDisconnectClicked(object? sender, EventArgs e)
    {
        _timer.Stop();
        NewSession();
        SetConnected(false);
        StatusLabel.Text = "已断开。电脑端服务仍可由其他设备使用。";
        DeviceLabel.Text = "连接已断开，请重新连接后检测设备。";
        SdkStateLabel.Text = "连接已断开，当前 SDK 状态未知。";
        Notice("");
    }

    private async Task RefreshAsync(bool force = false)
    {
        if (!_connected || (_busy && !force)) return;
        var token = Token;
        if (force) await _refreshGate.WaitAsync(token);
        else if (!_refreshGate.Wait(0)) return;
        try
        {
            var status = await _api.StatusAsync(token);
            token.ThrowIfCancellationRequested();
            var ids = status.Tasks.Select(t => t.Id).ToHashSet();
            foreach (var old in _tasks.Where(t => !ids.Contains(t.Id)).ToList()) _tasks.Remove(old);
            foreach (var dto in status.Tasks)
            {
                var item = _tasks.FirstOrDefault(t => t.Id == dto.Id);
                if (item is null) { item = new TaskItem { Id = dto.Id }; _tasks.Add(item); }
                item.Apply(dto, _connected && !_busy);
            }
            RunningCount.Text = status.Tasks.Count(t => t.Running).ToString();
            TaskCount.Text = $"共 {status.Tasks.Count} 个任务";
            TaskEmpty.IsVisible = _tasks.Count == 0;
            TaskHint.Text = "每 2.5 秒同步";
            RefreshTime.Text = DateTime.Now.ToString("HH:mm:ss");
            StatusLabel.Text = $"已连接 {_api.BaseUrl} · 控制台 v{status.Version} · 运行 {TimeSpan.FromSeconds(status.Uptime):hh\\:mm\\:ss}";
            LanLabel.Text = status.Remote is null ? "" : "局域网地址：" + string.Join(" / ", status.Remote.LanIps.Select(ip => $"{ip}:{status.Remote.Port}"));
            LogTaskBtn.IsEnabled = !_busy && _tasks.Count > 0;
            if (!_tasks.Any(t => t.Id == _logTask)) _logTask = _tasks.FirstOrDefault()?.Id ?? "";
            await RefreshLogsAsync(token);
            if (++_cycle % 6 == 0) await RefreshDevicesAsync(token);
            if (_tab == "sdk" && _cycle % 2 == 0 && !_sdkBusy) await RefreshSdkAsync(token);
            if (_tab == "companion" && _cycle % 2 == 0) await RefreshClientsAsync();
            if (_tab == "ai" && _cycle % 2 == 0 && !_aiBusy && _sdkRunning) await RefreshAiStatusAsync(token);
        }
        catch (OperationCanceledException) { }
        catch (ApiException ex)
        {
            if (token.IsCancellationRequested) return;
            _timer.Stop(); SetConnected(false);
            StatusLabel.Text = "电脑端连接中断，请重新连接。";
            Notice(ex.Message);
        }
        catch (Exception ex) { Notice("同步失败：" + ex.Message); }
        finally { _refreshGate.Release(); }
    }

    private async Task RefreshLogsAsync(CancellationToken token)
    {
        if (_logTask.Length == 0) { LogLabel.Text = "电脑端尚无任务。"; return; }
        var id = _logTask;
        var lines = await _api.LogsAsync(id, token);
        if (id != _logTask || token.IsCancellationRequested) return;
        LogTaskLabel.Text = _tasks.FirstOrDefault(t => t.Id == id)?.Name ?? id;
        LogLabel.Text = lines.Count == 0 ? "此任务暂无日志。启动后将在此显示真实输出。" : string.Join("\n", lines);
    }
    private async void OnSelectLogTaskClicked(object? sender, EventArgs e)
    {
        if (!_connected || _tasks.Count == 0) return;
        var names = _tasks.Select(t => t.Name).ToArray();
        var selected = await DisplayActionSheetAsync("选择日志任务", "取消", null, names);
        var item = _tasks.FirstOrDefault(t => t.Name == selected);
        if (item is null) return;
        _logTask = item.Id;
        try { await RefreshLogsAsync(Token); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Notice("日志读取失败：" + ex.Message); }
    }
    private async void OnTaskButtonClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: string id } || !_connected || _busy) return;
        var item = _tasks.FirstOrDefault(t => t.Id == id);
        if (item is null || item.Busy) return;
        SetBusy(true); item.SetBusy(true, true);
        try
        {
            var message = await _api.TaskAsync(item.Running, id, Token);
            Notice(message, false);
            await RefreshAsync(force: true);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Notice("任务操作失败：" + ex.Message); }
        finally { item.SetBusy(false, _connected); SetBusy(false); }
    }

    private async Task RefreshDevicesAsync(CancellationToken token)
    {
        if (!await _deviceGate.WaitAsync(0, token)) return;
        try
        {
            var result = await _api.DevicesAsync(token);
            token.ThrowIfCancellationRequested();
            DeviceCount.Text = result.Devices.Count.ToString();
            DeviceSummary.Text = result.Devices.Count == 0 ? "未检测到设备" : "来自电脑端 ADB";
            DeviceLabel.Text = result.Devices.Count == 0
                ? "未检测到 ADB 设备。请连接手机并开启 USB 调试，或检查模拟器端口。"
                : string.Join("\n", result.Devices.Select(FormatDevice));
            if (result.Devices.Count == 0 && !string.IsNullOrWhiteSpace(result.Raw))
                DeviceLabel.Text += "\n" + result.Raw.Trim();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { DeviceCount.Text = "—"; DeviceSummary.Text = "检测失败"; DeviceLabel.Text = ex.Message; }
        finally { _deviceGate.Release(); }
    }
    private static string FormatDevice(JsonElement item) => item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" :
        item.ValueKind == JsonValueKind.Object ? string.Join(" · ", item.EnumerateObject().Select(p => $"{p.Name}: {p.Value}")) : item.ToString();
    private async void OnDevicesClicked(object? sender, EventArgs e)
    {
        if (!_connected) return;
        DevicesBtn.IsEnabled = false;
        DeviceLabel.Text = "正在读取电脑端 ADB 设备…";
        try { await RefreshDevicesAsync(Token); }
        catch (OperationCanceledException) { }
        finally { UpdateAvailability(); }
    }

    private async void OnTabClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: string tab }) return;
        _tab = tab;
        DashboardPanel.IsVisible = tab == "dashboard";
        SdkPanel.IsVisible = tab == "sdk";
        AiPanel.IsVisible = tab == "ai";
        SettingsPanel.IsVisible = tab == "settings";
        MainScroll.IsVisible = tab != "companion";
        CompanionView.IsVisible = tab == "companion";
        foreach (var (button, key) in new[] { (DashboardTab, "dashboard"), (SdkTab, "sdk"), (AiTab, "ai"), (SettingsTab, "settings"), (CompanionTab, "companion") })
        {
            button.BackgroundColor = Color.FromArgb(tab == key ? "#11856B" : "#E8F0ED");
            button.TextColor = Color.FromArgb(tab == key ? "#FFFFFF" : "#132C36");
        }
        if (tab == "ai" && _connected && _sdkRunning && !_aiLoaded)
        {
            try { await LoadAiAsync(Token); }
            catch (OperationCanceledException) { }
        }
        await MainScroll.ScrollToAsync(0, 0, false);
        if (tab == "companion")
        {
            LayoutCompanion();
            if (_cloudWeb.Source is null) Browse(CloudHome);
            await RefreshClientsAsync();
        }
        if (tab == "sdk" && _connected)
        {
            try { await RefreshSdkAsync(Token); }
            catch (OperationCanceledException) { }
        }
    }

    private async Task LoadConfigAsync(CancellationToken token)
    {
        try
        {
            var config = await _api.ConfigAsync(token);
            token.ThrowIfCancellationRequested();
            foreach (var (key, entry) in ConfigEntries()) entry.Text = config.TryGetProperty(key, out var value) ? value.ToString() : "";
            MomentsContactsSelector.SetSelection(config.TryGetProperty("replica_authors", out var authors) && authors.ValueKind == JsonValueKind.Array
                ? authors.EnumerateArray().Where(a => a.ValueKind == JsonValueKind.String).Select(a => a.GetString()!)
                : new[] { Text(config, "replica_author") });
            ConfigStatusLabel.Text = "已从电脑端读取。修改后请保存。";
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { ConfigStatusLabel.Text = "读取失败：" + ex.Message; }
    }
    private (string key, Entry entry)[] ConfigEntries() =>
    [
        ("round_like_count", LikeCountEntry), ("round_rest_minutes", RestEntry),
        ("run_start_hour", StartHourEntry), ("run_end_hour", EndHourEntry),
        ("wechat_count", WechatCountEntry), ("adb_port", AdbPortEntry)
    ];
    private async void OnReloadConfigClicked(object? sender, EventArgs e)
    {
        if (!_connected || _busy) return;
        SetBusy(true);
        try { await LoadConfigAsync(Token); }
        catch (OperationCanceledException) { }
        finally { SetBusy(false); }
    }
    private async void OnSaveConfigClicked(object? sender, EventArgs e)
    {
        if (!_connected || _busy) return;
        try
        {
            var bounds = new[] { (1, 10000), (0, 10080), (0, 23), (0, 23), (1, 20), (1, 65535) };
            var names = new[] { "每轮数量", "休息分钟", "开始时刻", "结束时刻", "多开数量", "ADB 端口" };
            var entries = ConfigEntries();
            var changes = new Dictionary<string, object>();
            for (int index = 0; index < entries.Length; index++)
            {
                if (!int.TryParse(entries[index].entry.Text?.Trim(), out var number) || number < bounds[index].Item1 || number > bounds[index].Item2)
                    throw new ArgumentException($"{names[index]}必须是 {bounds[index].Item1}–{bounds[index].Item2} 的整数。");
                changes[entries[index].key] = entries[index].key == "adb_port" ? number.ToString() : number;
            }
            changes["replica_authors"] = MomentsContactsSelector.SelectedNames;
            changes["replica_author"] = MomentsContactsSelector.SelectedNames.FirstOrDefault() ?? "";
            SetBusy(true);
            ConfigStatusLabel.Text = "正在保存…";
            await _api.SaveConfigAsync(changes, Token);
            ConfigStatusLabel.Text = "已保存到电脑。新参数将在下次启动任务时生效。";
            Notice("参数已保存。", false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ConfigStatusLabel.Text = "保存失败：" + ex.Message; Notice(ex.Message); }
        finally { SetBusy(false); }
    }

    private static string Text(JsonElement root, string key, string fallback = "") =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(key, out var value) ? value.ToString() : fallback;
    private async Task RefreshSdkAsync(CancellationToken token)
    {
        if (!await _sdkGate.WaitAsync(0, token)) return;
        try
        {
            var result = await _api.SdkStatusAsync(token);
            token.ThrowIfCancellationRequested();
            _sdkRunning = result.TryGetProperty("running", out var running) && running.ValueKind == JsonValueKind.True;
            SdkBadge.Text = _sdkRunning ? "运行中" : "已停止";
            SdkBadge.TextColor = Color.FromArgb(_sdkRunning ? "#11856B" : "#5E756F");
            SdkStateLabel.Text = Text(result, "message", _sdkRunning ? "C# 服务已就绪，通过电脑端统一代理访问。" : "点击启动 SDK 服务，读取本机微信与 SDK 能力。");
            SdkVersionLabel.Text = "托管服务 " + Text(result, "version", "未运行");
            if (result.TryGetProperty("session", out var session))
            {
                SdkSessionLabel.Text = Text(session, "message", Text(session, "state", "未知"));
                if (session.TryGetProperty("processes", out var processes) && processes.ValueKind == JsonValueKind.Array)
                {
                    var versions = processes.EnumerateArray().Select(p => $"{Text(p, "name", "微信")}  {Text(p, "version", "版本未知")} · PID {Text(p, "pid")}").ToArray();
                    if (versions.Length > 0) SdkVersionLabel.Text += "\n" + string.Join("\n", versions);
                }
            }
            if (result.TryGetProperty("sdk", out var sdk))
            {
                SdkVersionLabel.Text += $"\n{Text(sdk, "name", "SDK")} {Text(sdk, "packageVersion")} · 适配 {Text(sdk, "supportedClient", "以 SDK 探测为准")}";
                SdkCapabilityLabel.Text = "支持官方登录窗口、进程状态、账号与可见会话、通讯录姓名、本地只读插件。\n" + Text(sdk, "notes");
            }
            else SdkCapabilityLabel.Text = "服务启动后显示 SDK 探测结果。";
            PluginList.Children.Clear();
            if (result.TryGetProperty("plugins", out var plugins) && plugins.ValueKind == JsonValueKind.Array)
            {
                foreach (var plugin in plugins.EnumerateArray()) AddPlugin(plugin);
            }
            if (PluginList.Children.Count == 0) PluginList.Children.Add(new Label { Text = _sdkRunning ? "当前服务未提供可运行插件。" : "启动服务后载入只读诊断插件。", TextColor = Color.FromArgb("#5E756F") });
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { SdkBadge.Text = "不可用"; SdkStateLabel.Text = ex.Message; _sdkRunning = false; }
        finally { _sdkGate.Release(); UpdateAvailability(); }
    }
    private void AddPlugin(JsonElement plugin)
    {
        var id = Text(plugin, "id");
        var button = new Button { Text = "运行诊断", CommandParameter = id, FontSize = 13, HorizontalOptions = LayoutOptions.Start, AutomationId = "Plugin-" + id };
        button.Clicked += OnPluginClicked;
        var content = new VerticalStackLayout { Spacing = 9 };
        content.Children.Add(new Label { Text = Text(plugin, "name", id), FontAttributes = FontAttributes.Bold, FontSize = 16 });
        content.Children.Add(new Label { Text = Text(plugin, "description"), FontSize = 12, TextColor = Color.FromArgb("#5E756F") });
        content.Children.Add(button);
        PluginList.Children.Add(new Border { Style = (Style)Application.Current!.Resources["Panel"], Content = content });
    }
    private static string DescribeJson(JsonElement element) => element.ValueKind == JsonValueKind.Object
        ? string.Join("\n", element.EnumerateObject().Select(p => $"{p.Name}：{(p.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array ? p.Value.GetRawText() : p.Value.ToString())}"))
        : element.ToString();
    private async void OnSdkRefreshClicked(object? sender, EventArgs e)
    {
        if (!_connected || _sdkBusy) return;
        _sdkBusy = true; UpdateAvailability();
        try { await RefreshSdkAsync(Token); }
        catch (OperationCanceledException) { }
        finally { _sdkBusy = false; UpdateAvailability(); }
    }
    private async void OnSdkActionClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: string action } || !_connected || _sdkBusy) return;
        _sdkBusy = true; UpdateAvailability();
        SdkStateLabel.Text = action == "login" ? "正在电脑打开微信官方登录窗口…" : "正在处理服务…";
        try
        {
            var result = await _api.SdkActionAsync(action, new { }, Token);
            Notice(Text(result, "message", "操作已完成。"), false);
            await RefreshSdkAsync(Token);
            if (action == "start" && _sdkRunning) await LoadAiAsync(Token);
            if (action == "stop") { _aiLoaded = false; _aiRunning = false; AiBadge.Text = "SDK 已停止"; }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Notice("SDK 操作失败：" + ex.Message); SdkStateLabel.Text = ex.Message; }
        finally { _sdkBusy = false; UpdateAvailability(); }
    }
    private async void OnPluginClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: string id } || !_connected || _sdkBusy) return;
        _sdkBusy = true; UpdateAvailability();
        PluginResultLabel.Text = "正在读取诊断信息…";
        try
        {
            var result = await _api.SdkActionAsync("plugins/run", new { id }, Token);
            PluginResultLabel.Text = result.TryGetProperty("result", out var data) ? DescribeJson(data) : DescribeJson(result);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { PluginResultLabel.Text = "诊断失败：" + ex.Message; }
        finally { _sdkBusy = false; UpdateAvailability(); }
    }

    private void OnPageSizeChanged(object? sender, EventArgs e)
    {
        LayoutCompanion();
        var wide = Width >= 1000;
        MainContent.Padding = new Thickness(Width < 500 ? 16 : 24, 20, Width < 500 ? 16 : 24, 32);
        WorkspaceGrid.ColumnDefinitions.Clear();
        WorkspaceGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(wide ? 1.15 : 1, GridUnitType.Star)));
        if (wide) WorkspaceGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        Grid.SetColumn(EvidenceColumn, wide ? 1 : 0);
        Grid.SetRow(EvidenceColumn, wide ? 0 : 1);
        AiWorkspaceGrid.ColumnDefinitions.Clear();
        AiWorkspaceGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(wide ? 0.9 : 1, GridUnitType.Star)));
        if (wide) AiWorkspaceGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1.1, GridUnitType.Star)));
        Grid.SetColumn(AiReplyColumn, wide ? 1 : 0);
        Grid.SetRow(AiReplyColumn, wide ? 0 : 1);
    }
    private bool _workspaceLaunchProcessed;
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        ResumePolling();
        if (!_workspaceLaunchProcessed)
        {
            _workspaceLaunchProcessed = true;
#if WINDOWS
            if (Environment.GetCommandLineArgs().Contains("--workspace-local"))
            {
                IpEntry.Text = "127.0.0.1";
                PortEntry.Text = "8765";
                await ConnectAsync(false);
                OnTabClicked(CompanionTab, EventArgs.Empty);
            }
            else if (Environment.GetCommandLineArgs().Contains("--wechat-direct"))
                await OpenWechatSimplyAsync();
#else
            await Task.CompletedTask;
#endif
        }
    }
    public void ResumePolling()
    {
        if (_session.IsCancellationRequested) NewSession();
        if (_connected) { _timer.Start(); _ = RefreshAsync(); }
    }
    protected override void OnDisappearing()
    {
        SuspendPolling();
        base.OnDisappearing();
    }
    public void SuspendPolling()
    {
        _timer.Stop();
        _session.Cancel();
    }
}



