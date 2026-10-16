using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Storage;
using WeChatLikeMaui.Services;

namespace WeChatLikeMaui;

// Dedicated, self-contained page for the owner's own read-only WeChat data:
// login (open official client), contacts (user table), and chat history.
// All data is the local owner's own logged-in account, read through the Windows
// host's SDK bridge; nothing is injected into WeChat and no credentials are automated.
public partial class WeChatDataPage : ContentPage
{
    private readonly ConsoleApi _api = new();
    private readonly ObservableCollection<string> _contacts = new();
    private readonly ObservableCollection<ChatRow> _messages = new();
    private bool _ready;

    public WeChatDataPage()
    {
        InitializeComponent();
        ContactsView.ItemsSource = _contacts;
        ChatView.ItemsSource = _messages;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        TryConfigure();
    }

    private void TryConfigure()
    {
        var ip = Preferences.Get("ip", "");
        var port = Preferences.Get("port", "18766");
        if (string.IsNullOrWhiteSpace(ip))
        {
            ConnLabel.Text = "未连接。请先在‘工作台’连接电脑端（默认 127.0.0.1:18766）。";
            _ready = false;
            return;
        }
        try
        {
            _api.Configure(ip, port);
            _ready = true;
            ConnLabel.Text = $"已连接 {_api.BaseUrl}（本人数据只读，仅本机）。";
        }
        catch (Exception ex)
        {
            _ready = false;
            ConnLabel.Text = "连接配置无效：" + ex.Message;
        }
    }

    private CancellationToken Token => CancellationToken.None;
    private void Fail(string msg) { ErrorLabel.Text = msg; ErrorLabel.IsVisible = true; StatusLabel.Text = ""; }
    private void Ok(string msg) { StatusLabel.Text = msg; ErrorLabel.IsVisible = false; }

    private async void OnLoginClicked(object? sender, EventArgs e)
    {
        if (!_ready) { Fail("请先在‘工作台’连接电脑端。"); return; }
        try
        {
            var r = await _api.SdkActionAsync("login", new { }, Token);
            Ok(Text(r, "message", "已请求官方微信登录窗口。"));
        }
        catch (Exception ex) { Fail(ex.Message); }
    }

    private async void OnContactsClicked(object? sender, EventArgs e)
    {
        if (!_ready) { Fail("请先连接电脑端。"); return; }
        try
        {
            var r = await _api.SdkReadAsync("contacts", Token);
            _contacts.Clear();
            if (r.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                foreach (var acc in data.EnumerateArray())
                    if (acc.TryGetProperty("names", out var names) && names.ValueKind == JsonValueKind.Array)
                        foreach (var n in names.EnumerateArray()) _contacts.Add(n.GetString() ?? "");
            Ok($"通讯录已读取，共 {_contacts.Count} 条。仅本人本机数据。");
        }
        catch (Exception ex) { Fail(ex.Message); }
    }

    private async void OnChatClicked(object? sender, EventArgs e)
    {
        if (!_ready) { Fail("请先连接电脑端。"); return; }
        var who = WhoEntry.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(who)) { Fail("请填写会话标识 who。"); return; }
        try
        {
            var r = await _api.SdkChatAsync(who, Token);
            _messages.Clear();
            if (r.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                foreach (var acc in data.EnumerateArray())
                    if (acc.TryGetProperty("messages", out var msgs) && msgs.ValueKind == JsonValueKind.Array)
                        foreach (var m in msgs.EnumerateArray())
                            _messages.Add(new ChatRow(Text(m, "who"), Text(m, "message"), Text(m, "time")));
            Ok($"聊天记录已读取，共 {_messages.Count} 条（仅本人会话）。");
        }
        catch (Exception ex) { Fail(ex.Message); }
    }

    private async void OnExportClicked(object? sender, EventArgs e)
    {
        if (_contacts.Count == 0 && _messages.Count == 0) { Fail("没有可导出的数据，请先读取。"); return; }
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("type,who,message,time");
            foreach (var c in _contacts) sb.AppendLine("contact,," + Escape(c) + ",");
            foreach (var m in _messages) sb.AppendLine("chat," + Escape(m.Who) + "," + Escape(m.Message) + "," + Escape(m.Time));
            var path = Path.Combine(FileSystem.AppDataDirectory, "wechat_data_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
            await File.WriteAllTextAsync(path, sb.ToString());
            Ok("已导出到本机文件（透明、仅本地）：" + path);
        }
        catch (Exception ex) { Fail(ex.Message); }
    }

    private static string Escape(string? v) => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"";
    private static string Text(JsonElement r, string k, string f = "")
        => r.ValueKind == JsonValueKind.Object && r.TryGetProperty(k, out var v)
            ? (v.ValueKind == JsonValueKind.String ? v.GetString()! : v.ToString()) : f;
}

public sealed class ChatRow(string Who, string Message, string Time)
{
    public string Who { get; } = Who;
    public string Message { get; } = Message;
    public string Time { get; } = Time;
}
