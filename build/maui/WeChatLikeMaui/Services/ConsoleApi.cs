using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WeChatLikeMaui.Services;

/// <summary>All platforms use the LAN API; the SDK host stays behind the desktop proxy.</summary>
public sealed class ConsoleApi : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(115) };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    public string BaseUrl { get; private set; } = "";
    public string Host { get; private set; } = "";
    public string Port { get; private set; } = "8765";

    public void Configure(string host, string port)
    {
        if (!int.TryParse(port.Trim(), out var number) || number is < 1 or > 65535)
            throw new ArgumentException("端口必须是 1–65535 的整数。");
        var value = host.Trim();
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("请输入电脑的 IP 地址或主机名。");
        if (!value.Contains("://")) value = "http://" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https") ||
            string.IsNullOrWhiteSpace(uri.Host) || uri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("请输入有效的 IP 或主机名，不要包含路径、账号或查询参数。");
        var endpoint = new UriBuilder(uri.Scheme, uri.Host, number).Uri;
        Host = uri.Host;
        Port = number.ToString();
        BaseUrl = endpoint.GetLeftPart(UriPartial.Authority);
    }

    private async Task<JsonElement> RequestAsync(string path, object? body, CancellationToken token)
    {
        if (BaseUrl.Length == 0) throw new InvalidOperationException("请先连接电脑端服务。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        var seconds = path == "/api/sdk/contacts" || path == "/api/sdk/plugins/run" || path == "/api/sdk/clients/verify" ? 110 :
            path == "/api/sdk/ai/preview" ? 100 : path == "/api/sdk/start" ? 60 : 15;
        timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
        using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, BaseUrl + path);
        // The frozen Python API reads Content-Length; buffer JSON instead of chunked JsonContent.
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        try
        {
            using var response = await _http.SendAsync(request, timeout.Token);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var root = document.RootElement;
            var message = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("message", out var m) ? m.ToString() :
                root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error) ? error.ToString() : "服务未接受此操作。";
            if (!response.IsSuccessStatusCode) throw new ApiException($"HTTP {(int)response.StatusCode}：{message}");
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True)
                throw new ApiException(message);
            return root.Clone();
        }
        catch (JsonException) { throw new ApiException("服务返回了无效的数据，请确认 IP 和端口对应本项目控制台。"); }
        catch (HttpRequestException ex) { throw new ApiException("无法访问电脑端服务，请检查服务、IP 和同一 Wi-Fi。" + ex.Message); }
        catch (TaskCanceledException) when (!token.IsCancellationRequested) { throw new ApiException("电脑端响应超时，请重试。"); }
    }

    public async Task PingAsync(CancellationToken token) => await RequestAsync("/api/ping", null, token);
    public async Task<StatusDto> StatusAsync(CancellationToken token) =>
        (await RequestAsync("/api/status", null, token)).Deserialize<StatusDto>(JsonOptions) ?? throw new ApiException("状态数据为空。");
    public async Task<List<string>> LogsAsync(string id, CancellationToken token) =>
        (await RequestAsync($"/api/logs?task={Uri.EscapeDataString(id)}&n=60", null, token)).Deserialize<LogsDto>(JsonOptions)?.Lines ?? [];
    public async Task<DevicesDto> DevicesAsync(CancellationToken token) =>
        (await RequestAsync("/api/devices", null, token)).Deserialize<DevicesDto>(JsonOptions) ?? new();
    public async Task<JsonElement> ConfigAsync(CancellationToken token) =>
        (await RequestAsync("/api/config", null, token)).GetProperty("config").Clone();
    public async Task SaveConfigAsync(Dictionary<string, object> changes, CancellationToken token) => await RequestAsync("/api/config", changes, token);
    public async Task<string> TaskAsync(bool stop, string id, CancellationToken token)
    {
        var result = await RequestAsync(stop ? "/api/task/stop" : "/api/task/start", new { task = id }, token);
        return result.TryGetProperty("message", out var m) ? m.ToString() : "操作已完成。";
    }
    public Task<JsonElement> SdkStatusAsync(CancellationToken token) => RequestAsync("/api/sdk/status", null, token);
    public Task<JsonElement> SdkPluginsAsync(CancellationToken token) => RequestAsync("/api/sdk/plugins", null, token);
    public Task<JsonElement> SdkReadAsync(string path, CancellationToken token) => RequestAsync("/api/sdk/" + path, null, token);
    public Task<JsonElement> SdkActionAsync(string action, object body, CancellationToken token) => RequestAsync("/api/sdk/" + action, body, token);
    public Task<JsonElement> SdkChatAsync(string who, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(who)) throw new ApiException("请先填写会话标识 who。");
        var encoded = Uri.EscapeDataString(who);
        return RequestAsync("/api/sdk/chat?who=" + encoded, null, token);
    }
    public void Dispose() => _http.Dispose();
}

public sealed class ApiException(string message) : Exception(message);
public sealed class StatusDto
{
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("uptime")] public int Uptime { get; set; }
    [JsonPropertyName("tasks")] public List<TaskDto> Tasks { get; set; } = [];
    [JsonPropertyName("remote")] public RemoteDto? Remote { get; set; }
}
public sealed class RemoteDto
{
    [JsonPropertyName("port")] public int Port { get; set; }
    [JsonPropertyName("lan_ips")] public List<string> LanIps { get; set; } = [];
}
public sealed class TaskDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("desc")] public string Desc { get; set; } = "";
    [JsonPropertyName("running")] public bool Running { get; set; }
    [JsonPropertyName("elapsed")] public int Elapsed { get; set; }
    [JsonPropertyName("pid")] public int? Pid { get; set; }
    [JsonPropertyName("exit_code")] public int? ExitCode { get; set; }
    [JsonPropertyName("last_line")] public string? LastLine { get; set; }
}
public sealed class LogsDto
{
    [JsonPropertyName("lines")] public List<string> Lines { get; set; } = [];
}
public sealed class DevicesDto
{
    [JsonPropertyName("devices")] public List<JsonElement> Devices { get; set; } = [];
    [JsonPropertyName("raw")] public string Raw { get; set; } = "";
}

