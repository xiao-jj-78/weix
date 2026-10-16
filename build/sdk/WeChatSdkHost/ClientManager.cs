using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace WeChatSdkHost;

// Process discovery is passive. Window activation and SDK reads are explicit.
public sealed class ClientManager(HostState host)
{
    private readonly object _gate = new();
    private readonly string _aliasesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WeChatLikeConsole", "Clients", "aliases.json");
    private readonly Dictionary<string, JsonElement> _verified = [];
    private DateTimeOffset? _checked;
    public static string Identity(Process process) => process.Id.ToString(CultureInfo.InvariantCulture) + ":" + process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture);
    public static bool IsIdentity(string? id) => id is not null && System.Text.RegularExpressions.Regex.IsMatch(id, @"^[1-9][0-9]{0,9}:[1-9][0-9]{10,18}$");
    private static bool IsClient(Process process) => process.SessionId == Process.GetCurrentProcess().SessionId &&
        (process.ProcessName.Equals("Weixin", StringComparison.OrdinalIgnoreCase) || process.ProcessName.Equals("WeChat", StringComparison.OrdinalIgnoreCase));
    private static List<Process> Processes()
    {
        var result = new List<Process>();
        foreach (var name in new[] { "Weixin", "WeChat" })
        foreach (var process in Process.GetProcessesByName(name))
        {
            try { if (IsClient(process) && process.MainWindowHandle != 0) { result.Add(process); continue; } } catch { }
            process.Dispose();
        }
        return result.OrderBy(process => process.Id).ToList();
    }
    private static Process Resolve(string id)
    {
        if (!IsIdentity(id) || !int.TryParse(id.Split(':')[0], out var pid)) throw new ArgumentException("请选择有效的微信窗口。");
        var process = Process.GetProcessById(pid);
        try
        {
            if (!IsClient(process) || Identity(process) != id || process.MainWindowHandle == 0) throw new InvalidOperationException("该微信窗口已关闭或进程已变化，请刷新列表。");
            return process;
        }
        catch { process.Dispose(); throw; }
    }
    private Dictionary<string, string> LoadAliases()
    {
        if (!File.Exists(_aliasesPath)) return [];
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_aliasesPath)) ?? []; }
        catch (JsonException) { return []; }
    }
    public object List()
    {
        lock (_gate)
        {
            var aliases = LoadAliases();
            var list = new List<object>();
            foreach (var process in Processes())
            using (process)
            {
                try
                {
                    var id = Identity(process);
                    var account = _verified.TryGetValue(id, out var value) ? (object)value : null;
                    list.Add(new { instanceId = id, pid = process.Id, name = process.ProcessName,
                        alias = aliases.GetValueOrDefault(id, ""), version = process.MainModule?.FileVersionInfo.FileVersion,
                        windowPresent = true, foreground = GetForegroundWindow() == process.MainWindowHandle,
                        state = account is null ? "login-unverified" : "verified-at-last-read", account,
                        verifiedAt = account is null ? (DateTimeOffset?)null : _checked });
                }
                catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
            }
            return new { ok = true, clients = list, count = list.Count, checkedAt = DateTimeOffset.UtcNow,
                message = "仅列出同一 Windows 桌面会话中有主窗口的微信进程。进程存在不代表已登录；同一进程内的多个账号以 SDK 读取结果为准。" };
        }
    }
    public object Focus(string id)
    {
        using var process = Resolve(id);
        if (!ShowWindowAsync(process.MainWindowHandle, IsIconic(process.MainWindowHandle) ? 9 : 5) && !IsWindowVisible(process.MainWindowHandle))
            throw new InvalidOperationException("Windows 未允许显示该窗口，请在对应桌面手动打开微信。");
        var foreground = SetForegroundWindow(process.MainWindowHandle);
        return new { ok = true, foreground, message = foreground ? "已切换到所选官方微信窗口，请按客户端提示继续登录。" : "已显示所选窗口；Windows 未允许自动置前，请点击任务栏中的微信。" };
    }
    public object Alias(string id, string value)
    {
        using var process = Resolve(id);
        value = value.Trim();
        if (value.Length > 60 || value.Any(char.IsControl)) throw new ArgumentException("备注最多 60 字，不能包含换行或控制字符。");
        lock (_gate)
        {
            var aliases = LoadAliases();
            // Aliases identify this process lifetime, never an unverified account.
            var active = Processes();
            try
            {
                var ids = active.Select(Identity).ToHashSet();
                aliases = aliases.Where(pair => ids.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
            }
            finally { foreach (var item in active) item.Dispose(); }
            if (value.Length == 0) aliases.Remove(id); else aliases[id] = value;
            Directory.CreateDirectory(Path.GetDirectoryName(_aliasesPath)!);
            var temporary = _aliasesPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(aliases));
            File.Move(temporary, _aliasesPath, true);
        }
        return new { ok = true, message = "窗口备注已保存；重启后的新进程需要重新标记。" };
    }
    public object Open()
    {
        var path = HostState.FindWeChatPath() ?? throw new InvalidOperationException("未找到官方微信，请先安装或设置 WLC_WECHAT_EXE。");
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path)! })?.Dispose();
        return new { ok = true, message = "已请求官方微信打开。已有登录状态由客户端复用；能否新增独立账号窗口以官方客户端为准，本工具不会强制多开。" };
    }
    public async Task<object> Verify(CancellationToken token)
    {
        var current = Processes();
        Dictionary<int, string> identities;
        try { identities = current.ToDictionary(process => process.Id, Identity); }
        finally { foreach (var process in current) process.Dispose(); }
        var result = JsonSerializer.SerializeToElement(await host.RunSdkReadOnlyAsync("session", token));
        if (!result.TryGetProperty("ok", out var ok) || !ok.GetBoolean()) return new { ok = false, message = result.TryGetProperty("message", out var message) ? message.GetString() : "SDK 未返回账号。" };
        lock (_gate)
        {
            _verified.Clear(); _checked = DateTimeOffset.UtcNow;
            foreach (var account in result.GetProperty("accounts").EnumerateArray())
            {
                if (account.TryGetProperty("pid", out var pid) && pid.TryGetInt32(out var value) && identities.TryGetValue(value, out var id))
                    _verified[id] = account.Clone();
            }
        }
        return List();
    }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(nint hwnd, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
}

public static class ClientEndpoints
{
    public static void MapClientEndpoints(this WebApplication app)
    {
        app.MapGet("/api/sdk/clients", (ClientManager manager) => manager.List());
        app.MapPost("/api/sdk/clients/open", (ClientManager manager, JsonElement body) => Run(() => { Empty(body); return manager.Open(); }));
        app.MapPost("/api/sdk/clients/focus", (ClientManager manager, JsonElement body) => Run(() => manager.Focus(Read(body, false).id)));
        app.MapPost("/api/sdk/clients/alias", (ClientManager manager, JsonElement body) => Run(() => { var (id, alias) = Read(body, true); return manager.Alias(id, alias!); }));
        app.MapPost("/api/sdk/clients/verify", async (ClientManager manager, JsonElement body, HttpContext context) =>
        {
            try { Empty(body); return Results.Json(await manager.Verify(context.RequestAborted)); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OperationCanceledException) { return Results.Json(new { ok = false, message = ex.Message }, statusCode: 409); }
        });
    }
    private static IResult Run(Func<object> work)
    {
        try { return Results.Json(work()); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return Results.Json(new { ok = false, message = ex.Message }, statusCode: 409); }
    }
    private static void Empty(JsonElement body)
    { if (body.ValueKind != JsonValueKind.Object || body.EnumerateObject().Any()) throw new ArgumentException("此操作不接受额外参数。"); }
    private static (string id, string? alias) Read(JsonElement body, bool alias)
    {
        if (body.ValueKind != JsonValueKind.Object || body.EnumerateObject().Any(p => p.Name != "instanceId" && !(alias && p.Name == "alias")) ||
            !body.TryGetProperty("instanceId", out var id) || id.ValueKind != JsonValueKind.String || !ClientManager.IsIdentity(id.GetString())) throw new ArgumentException("窗口标识无效。");
        string? value = null;
        if (alias && (!body.TryGetProperty("alias", out var name) || name.ValueKind != JsonValueKind.String)) throw new ArgumentException("请填写窗口备注。");
        if (alias) value = body.GetProperty("alias").GetString();
        return (id.GetString()!, value);
    }
}
