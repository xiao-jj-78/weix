using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace WeChatSdkHost;

public sealed class AiCoordinator(AiConfigStore store, AiTransport transport, HostState host, IHostApplicationLifetime lifetime) : IDisposable
{
    private readonly object _gate = new();
    private Process? _worker;
    private string? _statusPath;
    private JsonElement? _lastStatus;
    private bool _disposed;
    private CancellationTokenRegistration _stopping;
    private DateTimeOffset? _startedAt;
    private string _state = "stopped";
    private string? _lastError;
    public bool Running { get { lock (_gate) return _worker is not null; } }
    public void RegisterShutdown() => _stopping = lifetime.ApplicationStopping.Register(() => Stop());
    public object Config() { lock (_gate) return new { ok = true, config = store.Public(store.Load()), running = _worker is not null }; }
    public object Save(JsonElement body) { lock (_gate) { RequireStopped(); return new { ok = true, config = store.Save(body), running = false }; } }
    public object Import(string json) { lock (_gate) { RequireStopped(); return new { ok = true, config = store.Import(json), running = false }; } }
    private void RequireStopped() { if (_worker is not null) throw new InvalidOperationException("请先停止智能回复再修改或切换方案。"); }
    public async Task<object> PreviewAsync(string message, CancellationToken token)
    {
        var (config, profile, key) = store.Resolve();
        var reply = await transport.GenerateAsync(profile, key, config.SystemPrompt, message, token);
        return new { ok = true, reply, profileId = profile.Id, profile.Model, sentToWechat = false };
    }
    public object Start()
    {
        lock (_gate)
        {
            if (_disposed) throw new InvalidOperationException("宿主正在停止。");
            RequireStopped();
            var (config, _, _) = store.Resolve();
            if (config.AllowedContacts.Length is < 1 or > 10) throw new ArgumentException("请先选择 1 到 10 个私聊联系人。");
            if (config.AllowGroups) throw new ArgumentException("当前版本只支持私聊。");
            if (!host.TryBeginSdkSession()) throw new InvalidOperationException("另一个 SDK 操作正在进行，请稍后重试。");
            try
            {
                _lastStatus = null; _lastError = null;
                _statusPath = Path.Combine(Path.GetTempPath(), "WeChatAi-" + Guid.NewGuid().ToString("N") + ".json");
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AppContext.BaseDirectory, RedirectStandardOutput = true, RedirectStandardError = true };
                if (Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
                foreach (var value in new[] { "--ai-worker", "--status", _statusPath }) start.ArgumentList.Add(value);
                _worker = Process.Start(start) ?? throw new InvalidOperationException("无法启动智能回复进程。");
                // Upstream can print UI diagnostics; discard them, never include
                // chat text or provider credentials in host/network logs.
                _worker.OutputDataReceived += (_, _) => { }; _worker.ErrorDataReceived += (_, _) => { };
                _worker.BeginOutputReadLine(); _worker.BeginErrorReadLine();
                _startedAt = DateTimeOffset.UtcNow; _state = "starting";
                _ = WatchAsync(_worker, _statusPath, config.SessionMinutes);
                return new { ok = true, running = true, state = _state, message = "正在验证微信并建立历史消息基线；只处理启动后新收到的私聊。" };
            }
            catch
            {
                if (_worker is not null) { try { _worker.Kill(entireProcessTree: true); } catch { } _worker.Dispose(); _worker = null; }
                host.EndSdkSession();
                throw;
            }
        }
    }
    private async Task WatchAsync(Process process, string statusPath, int sessionMinutes)
    {
        var deadline = DateTimeOffset.UtcNow.AddMinutes(sessionMinutes).AddSeconds(30);
        var lastHeartbeat = DateTimeOffset.UtcNow;
        string? forcedError = null;
        try
        {
            while (true)
            {
                lock (_gate)
                {
                    if (_worker != process) return;
                    if (File.Exists(statusPath))
                    {
                        try
                        {
                            using var document = JsonDocument.Parse(File.ReadAllText(statusPath));
                            _lastStatus = document.RootElement.Clone();
                            if (_lastStatus.Value.TryGetProperty("state", out var state)) _state = state.GetString() ?? _state;
                            if (_lastStatus.Value.TryGetProperty("lastError", out var error)) _lastError = error.ValueKind == JsonValueKind.String ? error.GetString() : null;
                            if (_lastStatus.Value.TryGetProperty("heartbeatAt", out var time) && time.TryGetDateTimeOffset(out var heartbeat)) lastHeartbeat = heartbeat;
                        }
                        catch { }
                    }
                    if (process.HasExited)
                    {
                        if (_lastStatus is null && process.ExitCode != 0) { _state = "error"; _lastError = "智能回复进程初始化失败。"; }
                        break;
                    }
                    if (DateTimeOffset.UtcNow > deadline || DateTimeOffset.UtcNow - lastHeartbeat > TimeSpan.FromSeconds(120))
                    {
                        forcedError = DateTimeOffset.UtcNow > deadline ? "托管时长已到，已停止。" : "SDK 操作超过 120 秒未响应，已终止隔离进程。";
                        _lastError = forcedError;
                        _state = "stopped";
                        try { process.Kill(entireProcessTree: true); } catch { }
                    }
                }
                await Task.Delay(500);
            }
        }
        catch { lock (_gate) { _lastError = "智能回复进程已结束。"; } }
        finally
        {
            lock (_gate)
            {
                if (_worker == process)
                {
                    _worker = null;
                    if (forcedError is not null) { _lastError = forcedError; _state = "error"; }
                    if (_state is not ("error" or "completed")) _state = "stopped";
                    host.EndSdkSession();
                }
            }
            process.Dispose();
            try { File.Delete(statusPath); } catch { }
        }
    }
    public object Status()
    {
        lock (_gate) return new { ok = true, running = _worker is not null, state = _state, startedAt = _startedAt, lastError = _lastError, replyCount = _lastStatus?.TryGetProperty("replyCount", out var count) == true ? count.GetInt32() : 0, lastActivityAt = _lastStatus?.TryGetProperty("lastActivityAt", out var time) == true ? time : (JsonElement?)null, mode = "sdk-polling-private-contacts", requiresInteractiveDesktop = true, autoResume = false };
    }
    public object Stop()
    {
        lock (_gate)
        {
            if (_worker is not null)
            {
                try { _worker.Kill(entireProcessTree: true); _worker.WaitForExit(5000); }
                catch { if (!_worker.HasExited) throw new InvalidOperationException("智能回复进程未能停止，请重试或退出 SDK 宿主。"); }
                if (!_worker.HasExited) throw new InvalidOperationException("智能回复进程未在 5 秒内停止，请重试。");
            }
            _state = "stopped";
            return new { ok = true, running = false, state = "stopped", message = "已请求停止智能回复。" };
        }
    }
    public void Dispose() { lock (_gate) _disposed = true; Stop(); _stopping.Dispose(); }
}
