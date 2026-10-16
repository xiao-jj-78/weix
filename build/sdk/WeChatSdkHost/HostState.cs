using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Win32;
using WeChatAuto.Components;
using WeChatAuto.Utils;
using WeChatSdk.PluginContracts;

namespace WeChatSdkHost;

public sealed class HostState : IReadOnlyPluginContext
{
    private readonly CancellationToken _hostStopping;
    public HostState(IHostApplicationLifetime lifetime) => _hostStopping = lifetime.ApplicationStopping;
    private readonly DateTimeOffset _started = DateTimeOffset.UtcNow;
    private readonly SemaphoreSlim _sdkGate = new(1, 1);
    private ProcessSnapshot[] _processes = [];
    private object[] _accounts = [];
    private DateTimeOffset _lastProbe;
    private DateTimeOffset? _lastSdkRead;
    public double Uptime => (DateTimeOffset.UtcNow - _started).TotalSeconds;
    internal bool TryBeginSdkSession() => _sdkGate.Wait(0);
    internal void EndSdkSession() => _sdkGate.Release();

    public void Probe()
    {
        var list = new List<ProcessSnapshot>();
        foreach (var name in new[] { "WeChat", "Weixin" })
        foreach (var process in Process.GetProcessesByName(name))
        {
            using (process)
            {
                string version = "unknown";
                try { version = process.MainModule?.FileVersionInfo.FileVersion ?? version; } catch { }
                nint handle = 0;
                try { handle = process.MainWindowHandle; } catch { }
                // Real SDK public API, read-only: identify the owner of the main window.
                var windowPid = handle == 0 ? 0 : Win32WindowHelper.GetProcessId(handle);
                list.Add(new ProcessSnapshot(process.Id, name, version, windowPid == process.Id));
            }
        }
        _processes = list.ToArray();
        if (list.Count == 0) { _accounts = []; _lastSdkRead = null; }
        _lastProbe = DateTimeOffset.UtcNow;
    }

    public object GetSessionSnapshot() => new
    {
        state = _processes.Length == 0 ? "client-not-running" : "login-unverified",
        message = _processes.Length == 0 ? "未检测到官方微信客户端。点击打开微信并扫码登录。" : "已检测到客户端；请在官方窗口扫码，运行 SDK 会话读取以验证登录。",
        processes = _processes, accounts = _accounts, checkedAt = _lastProbe, lastSdkReadAt = _lastSdkRead,
        requiresInteractiveDesktop = true, authenticationVerifiedContinuously = false
    };

    public object GetSdkHealth() => new
    {
        loaded = true, name = "WeChatAuto4x.SDK", packageVersion = "2.1.4", assembly = typeof(WeChatClientFactory).Assembly.GetName().Name,
        supportedClient = "4.x (按原 SDK 的窗口兼容性)", target = ".NET 10 / Windows x64",
        originalSource = "scottfly189/WeChatAuto.SDK", adapter = "public-sdk-ui-automation",
        capabilities = new[] { "official-login-launch", "process-status", "sdk-account-session", "sdk-visible-conversations", "sdk-contact-names", "sdk-chat-history", "local-readonly-plugins" },
        forceOpenUiTree = false, registryInitialization = false, ocrEnabled = false, injection = false,
        notes = "读取账号/通讯录会调用 SDK 的公开 UIA 接口，可能切换、聚焦或调整微信窗口。UI Tree 未开放时会返回失败。"
    };

    public object OpenOfficialLogin()
    {
        Probe();
        if (_processes.Length > 0)
        {
            foreach (var item in _processes)
            {
                try
                {
                    using var process = Process.GetProcessById(item.Pid);
                    if (process.MainWindowHandle != 0) Win32WindowHelper.ShowWindow(process.MainWindowHandle);
                }
                catch { }
            }
            return new { ok = true, launched = false, message = "微信已在运行，请在官方微信窗口中完成扫码/手机确认，然后读取 SDK 会话。" };
        }
        var path = FindWeChatPath();
        if (path is null)
            return new { ok = false, launched = false, error = "wechat_not_installed", message = "未找到官方微信。请先安装电脑版微信；自定义安装位置可通过启动前的 WLC_WECHAT_EXE 配置。" };
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path)! });
            return new { ok = true, launched = true, message = "已打开官方微信，请亲自扫码并在手机确认。" };
        }
        catch (Exception exception) { return new { ok = false, launched = false, error = "launch_failed", message = exception.Message }; }
    }

    internal static string? FindWeChatPath()
    {
        var candidates = new List<string?> { Environment.GetEnvironmentVariable("WLC_WECHAT_EXE") };
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                foreach (var name in new[] { "WeChat.exe", "Weixin.exe" })
                {
                    using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + name, false);
                    candidates.Add(key?.GetValue(null) as string);
                }
                foreach (var product in new[] { "WeChat", "Weixin" })
                {
                    using var key = root.OpenSubKey(@"SOFTWARE\Tencent\" + product, false);
                    if (key?.GetValue("InstallPath") is string directory)
                    {
                        candidates.Add(Path.Combine(directory, "WeChat.exe"));
                        candidates.Add(Path.Combine(directory, "Weixin.exe"));
                    }
                }
            }
            catch { }
        }
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs") })
        foreach (var relative in new[] { "Tencent/WeChat/WeChat.exe", "Tencent/Weixin/Weixin.exe", "Tencent/Weixin/WeChat.exe" })
            candidates.Add(Path.Combine(root, relative));
        return candidates.Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => path!.Trim('"')).FirstOrDefault(path =>
            new[] { "WeChat.exe", "Weixin.exe" }.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase) && File.Exists(path));
    }

    public async Task<object> RunSdkReadOnlyAsync(string operation, CancellationToken cancellationToken)
    {
        if (operation is not ("session" or "contacts")) throw new InvalidOperationException("不支持的 SDK 操作。");
        return await RunSdkChildAsync(operation, null, cancellationToken);
    }

    public async Task<object> RunSdkChatAsync(string who, CancellationToken cancellationToken)
    {
        ValidateWho(who);
        return await RunSdkChildAsync("chat", who, cancellationToken);
    }

    public Task<object> RequestOfficialLoginAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<object>(OpenOfficialLogin());
    }

    private static void ValidateWho(string who)
    {
        if (string.IsNullOrWhiteSpace(who)) throw new InvalidOperationException("会话标识 who 不能为空。");
        if (who.Length > 96) throw new InvalidOperationException("会话标识过长（上限 96 字符）。");
        if (who.IndexOfAny(['\0', '/', '\\', '\n', '\r', '\t']) >= 0 || who.Contains(".."))
            throw new InvalidOperationException("会话标识包含非法字符。");
    }

    private async Task<object> RunSdkChildAsync(string operation, string? who, CancellationToken cancellationToken)
    {
        if (!await _sdkGate.WaitAsync(0, cancellationToken)) throw new InvalidOperationException("另一个 SDK 读取正在进行，请稍后重试。");
        var output = Path.Combine(Path.GetTempPath(), "WeChatSdkHost-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Probe();
            if (!_processes.Any(process => process.Name.Equals("Weixin", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("未检测到微信 4.x 的 Weixin 进程。请先打开官方微信并完成登录。3.9 客户端需使用原工程的兼容控制台。");
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AppContext.BaseDirectory };
            if (Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            foreach (var arg in new[] { "--sdk-worker", operation, "--output", output }) start.ArgumentList.Add(arg);
            if (who is not null) { start.ArgumentList.Add("--who"); start.ArgumentList.Add(who); }
            using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动隔离 SDK 读取进程。");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _hostStopping);
            timeout.CancelAfter(operation == "chat" ? TimeSpan.FromSeconds(90) : operation == "contacts" ? TimeSpan.FromSeconds(90) : TimeSpan.FromSeconds(45));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw;
            }
            if (!File.Exists(output)) throw new InvalidOperationException("SDK 进程未返回结果，检查官方微信的版本和 UI Tree 支持。");
            var result = JsonDocument.Parse(await File.ReadAllTextAsync(output, cancellationToken)).RootElement.Clone();
            if (result.TryGetProperty("ok", out var ok) && ok.GetBoolean())
            {
                _lastSdkRead = DateTimeOffset.UtcNow;
                if (result.TryGetProperty("accounts", out var accounts)) _accounts = accounts.EnumerateArray().Select(account => (object)account.Clone()).ToArray();
            }
            return result;
        }
        finally
        {
            try { File.Delete(output); } catch { }
            _sdkGate.Release();
        }
    }
}

public sealed record ProcessSnapshot(int Pid, string Name, string Version, bool MainWindowPresent);

public sealed class SessionSupervisor(HostState state, ILogger<SessionSupervisor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            try { state.Probe(); } catch (Exception exception) { logger.LogWarning("Client probe failed: {Error}", exception.Message); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
