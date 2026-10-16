using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace WeChatSdkHost;

public sealed class AiTransport : IDisposable
{
    private readonly HttpClient _client;
    public AiTransport() : this(new HttpClientHandler { AllowAutoRedirect = false }) { }
    internal AiTransport(HttpMessageHandler handler) => _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(45) };
    public static Uri Endpoint(AiProfile profile)
    {
        if (!Uri.TryCreate(profile.BaseUrl, UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback))) throw new ArgumentException("AI 地址必须使用 HTTPS；本机服务可使用 http://127.0.0.1。不允许地址内嵌密钥、参数或用户信息。");
        var path = uri.AbsolutePath.TrimEnd('/');
        if (profile.Protocol == "ollama")
        {
            if (path.EndsWith("/api/chat", StringComparison.OrdinalIgnoreCase)) return new Uri(uri.GetLeftPart(UriPartial.Authority) + path);
            return new Uri(uri.GetLeftPart(UriPartial.Authority) + path + (path.EndsWith("/api", StringComparison.OrdinalIgnoreCase) ? "/chat" : "/api/chat"));
        }
        if (profile.Protocol != "openai-compatible") throw new ArgumentException("不支持该 AI 协议。");
        if (path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) return new Uri(uri.GetLeftPart(UriPartial.Authority) + path);
        return new Uri(uri.GetLeftPart(UriPartial.Authority) + (path.Length == 0 ? "/v1" : path) + "/chat/completions");
    }

    public async Task<string> GenerateAsync(AiProfile profile, string? key, string prompt, string message, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        token = timeout.Token;
        if (string.IsNullOrWhiteSpace(message) || message.Length > 8000) throw new ArgumentException("测试消息应为 1 到 8000 字符。");
        if (prompt.Length > 20000) throw new ArgumentException("提示词最多 20000 字符。");
        var endpoint = Endpoint(profile);
        var messages = new[] { new { role = "system", content = prompt }, new { role = "user", content = message } };
        object body = profile.Protocol == "ollama" ? new { model = profile.Model, messages, stream = false, options = new { num_predict = 512 } } : new { model = profile.Model, messages, stream = false, max_tokens = 512 };
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new StringContent(JsonSerializer.Serialize(body, AiConfigStore.Json), Encoding.UTF8, "application/json") };
        if (!string.IsNullOrEmpty(key)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        HttpResponseMessage response;
        try { response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token); }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException) { throw new InvalidOperationException("AI 服务连接失败，请检查接口地址及网络。"); }
        using (response)
        {
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"AI 接口返回 HTTP {(int)response.StatusCode}。请检查协议、模型和密钥；服务端错误正文未记录。");
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192]; int read;
            while ((read = await stream.ReadAsync(chunk, token)) > 0) { if (buffer.Length + read > 1024 * 1024) throw new InvalidOperationException("AI 响应超过 1 MB，已停止读取。"); await buffer.WriteAsync(chunk.AsMemory(0, read), token); }
            try
            {
                using var document = JsonDocument.Parse(buffer.ToArray());
                var root = document.RootElement;
                var reply = profile.Protocol == "ollama" ? root.GetProperty("message").GetProperty("content").GetString() : root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                if (string.IsNullOrWhiteSpace(reply)) throw new InvalidOperationException("AI 未返回可用文本。");
                if (reply.Length > 4000) throw new InvalidOperationException("AI 回复超过 4000 字符，未发送。请缩短提示词或模型回复。");
                return reply.Trim();
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
            { throw new InvalidOperationException("AI 返回格式不符合所选协议，或回复为空/过长；请检查供应商设置。"); }
        }
    }
    public void Dispose() => _client.Dispose();
}
