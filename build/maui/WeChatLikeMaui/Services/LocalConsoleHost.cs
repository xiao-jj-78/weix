using System.Diagnostics;
using System.Text;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace WeChatLikeMaui.Services;

/// <summary>Owns only helpers created by this app; an existing user's server is never stopped.</summary>
public static class LocalConsoleHost
{
    private static Process? _ownedProcess;
    private static int _ownedPort = 0;

    public static async Task EnsureStartedAsync(int port, CancellationToken token)
    {
#if WINDOWS
        using var probe = new ConsoleApi();
        probe.Configure("127.0.0.1", port.ToString());
        if (await IsReadyAsync(probe, token)) return;
        if (_ownedProcess is { HasExited: false })
            throw new InvalidOperationException("本程序已启动一个服务，正在等待它就绪。请稍后重试。");
        var executable = FindExecutable() ?? throw new FileNotFoundException("未找到微信自动化控制台.exe。请把它放在本程序同目录，或保留交付目录 deliverables。");
        var info = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        info.ArgumentList.Add("--serve");
        info.ArgumentList.Add(port.ToString());
        _ownedProcess = Process.Start(info) ?? throw new InvalidOperationException("无法启动电脑端服务。");
        _ownedPort = port;
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline)
            {
                token.ThrowIfCancellationRequested();
                if (_ownedProcess.HasExited) throw new InvalidOperationException($"电脑端服务提前退出，退出码 {_ownedProcess.ExitCode}。");
                if (await IsReadyAsync(probe, token)) return;
                await Task.Delay(600, token);
            }
            throw new TimeoutException("服务启动超过 60 秒。请检查电脑端程序是否完整。");
        }
        catch { await StopOwnedAsync(); throw; }
#else
        await Task.CompletedTask;
        throw new PlatformNotSupportedException("请在 Windows 电脑启动服务。");
#endif
    }

    private static async Task<bool> IsReadyAsync(ConsoleApi api, CancellationToken token)
    {
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(token);
        attempt.CancelAfter(TimeSpan.FromMilliseconds(1200));
        try { await api.PingAsync(attempt.Token); return true; }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return false; }
        catch (ApiException) { return false; }
    }

    private static string? FindExecutable()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            foreach (var candidate in new[]
            {
                Path.Combine(directory.FullName, "微信自动化控制台.exe"),
                Path.Combine(directory.FullName, "deliverables", "微信自动化控制台.exe"),
                Path.Combine(directory.FullName, "build", "console", "dist", "微信自动化控制台.exe")
            })
                if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    // Destroying is synchronous. ConfigureAwait(false) below keeps cleanup independent of the UI thread.
    public static void StopOwned() => StopOwnedAsync().GetAwaiter().GetResult();

    public static async Task StopOwnedAsync()
    {
        var process = _ownedProcess;
        _ownedProcess = null;
        if (process is null) return;
        try
        {
            if (process.HasExited) return;
            bool requested = false;
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                using var content = new StringContent("{}", Encoding.UTF8, "application/json");
                using var response = await client.PostAsync($"http://127.0.0.1:{_ownedPort}/api/service/stop", content).ConfigureAwait(false);
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                requested = response.IsSuccessStatusCode && body.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            catch (JsonException) { }
            if (requested)
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                try { await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false); return; }
                catch (OperationCanceledException) { }
            }
#if WINDOWS
            StopOnlyHelperDescendants(process);
#else
            if (!process.HasExited) process.Kill(entireProcessTree: false);
#endif
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        finally { process.Dispose(); }
    }

#if WINDOWS
    private static void StopOnlyHelperDescendants(Process owned)
    {
        // A newly opened official WeChat may be a descendant. Only stop our known helper names.
        var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == new IntPtr(-1)) { owned.Kill(entireProcessTree: false); return; }
        var entries = new List<ProcessEntry>();
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            if (Process32First(snapshot, ref entry))
                do { entries.Add(entry); entry.Size = (uint)Marshal.SizeOf<ProcessEntry>(); }
                while (Process32Next(snapshot, ref entry));
        }
        finally { CloseHandle(snapshot); }
        var family = new HashSet<uint> { (uint)owned.Id };
        var descendants = new List<ProcessEntry>();
        bool added;
        do
        {
            added = false;
            foreach (var entry in entries)
                if (family.Contains(entry.ParentId) && family.Add(entry.Id))
                { descendants.Add(entry); added = true; }
        } while (added);
        var helperName = owned.ProcessName + ".exe";
        foreach (var entry in descendants.AsEnumerable().Reverse().Where(entry =>
            string.Equals(entry.Name, helperName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entry.Name, "WeChatSdkHost.exe", StringComparison.OrdinalIgnoreCase)))
        {
            try { using var child = Process.GetProcessById((int)entry.Id); child.Kill(entireProcessTree: false); }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
        if (!owned.HasExited) owned.Kill(entireProcessTree: false);
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, Id;
        public UIntPtr DefaultHeap;
        public uint ModuleId, Threads, ParentId;
        public int Priority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
#endif
}

