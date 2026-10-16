using System.Net;
using System.Text;
using System.Text.Json;
using WeChatAuto.Models;

namespace WeChatSdkHost;

internal static class AiSelfTest
{
    public static async Task<int> RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "WeChatAiTest-" + Guid.NewGuid().ToString("N"));
        var checks = 0;
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
        try
        {
            foreach (var input in new[] { "https://unit.invalid", "https://unit.invalid/v1", "https://unit.invalid/v1/", "https://unit.invalid/v1/chat/completions" }) Check(AiTransport.Endpoint(new() { BaseUrl = input }).AbsoluteUri == "https://unit.invalid/v1/chat/completions", "endpoint normalization");
            Check(AiTransport.Endpoint(new() { Protocol = "ollama", BaseUrl = "http://127.0.0.1:11434" }).AbsolutePath == "/api/chat", "ollama endpoint");
            foreach (var input in new[] { "http://remote.invalid/v1", "https://user:secret@unit.invalid/v1", "https://unit.invalid/v1?key=secret", "file:///etc/passwd" })
            {
                try { _ = AiTransport.Endpoint(new() { BaseUrl = input }); throw new Exception("accepted invalid URL"); } catch (ArgumentException) { checks++; }
            }
            var store = new AiConfigStore(directory);
            using var patch = JsonDocument.Parse("""{"profiles":[{"id":"one","name":"测试供应商","baseUrl":"https://unit.invalid/v1","protocol":"openai-compatible","model":"mock-model","apiKeyEnv":"","apiKey":"mock-test-secret"}],"activeProfileId":"one","allowedContacts":["测试好友"],"systemPrompt":"测试规则"}""");
            store.Save(patch.RootElement);
            Check(store.Resolve().Key == "mock-test-secret", "dpapi round trip");
            Check(!File.ReadAllText(Path.Combine(directory, "ai-config.json")).Contains("mock-test-secret"), "disk secret protection");
            var sanitized = JsonSerializer.Serialize(store.Public(store.Load()), AiConfigStore.Json);
            Check(!sanitized.Contains("mock-test-secret") && !sanitized.Contains("encryptedApiKey") && sanitized.Contains("hasApiKey"), "sanitized config");
            using (var roundtrip = JsonDocument.Parse(sanitized)) store.Save(roundtrip.RootElement);
            Check(store.Resolve().Key == "mock-test-secret", "sanitized update preserves stored key");
            try { store.Import("""{"settingsConfig":{"env":{"ANTHROPIC_BASE_URL":"https://unit.invalid","ANTHROPIC_AUTH_TOKEN":"mock"}}}"""); throw new Exception("accepted incompatible Claude protocol"); } catch (ArgumentException) { checks++; }
            store.Import("""{"name":"导入供应商","settingsConfig":{"env":{"OPENAI_BASE_URL":"https://unit.invalid/v1","OPENAI_MODEL":"import-model","OPENAI_API_KEY":"mock-import-secret"}}}""");
            Check(store.Load().Profiles.Count == 2 && store.Resolve().Profile.Model == "import-model" && store.Resolve().Key == "mock-import-secret", "provider import switches active");
            var transportHandler = new MockHandler(async request =>
            {
                Check(request.RequestUri?.AbsoluteUri == "https://unit.invalid/v1/chat/completions", "request endpoint");
                Check(request.Headers.Authorization?.Parameter == "mock-test-secret", "authorization header");
                var body = await request.Content!.ReadAsStringAsync();
                using var parsed = JsonDocument.Parse(body);
                Check(parsed.RootElement.GetProperty("messages")[0].GetProperty("content").GetString() == "system" && parsed.RootElement.GetProperty("messages")[1].GetProperty("content").GetString() == "hello", "prompt and input mapping");
                Check(!body.Contains("mock-test-secret"), "secret absent request body");
                return Json("""{"choices":[{"message":{"content":"mock reply"}}]}""");
            });
            using (var transport = new AiTransport(transportHandler)) Check(await transport.GenerateAsync(new() { BaseUrl = "https://unit.invalid/v1", Model = "mock" }, "mock-test-secret", "system", "hello", default) == "mock reply", "compatible response parse");
            using (var transport = new AiTransport(new MockHandler(_ => Task.FromResult(Json("""{"message":{"content":"ollama reply"}}"""))))) Check(await transport.GenerateAsync(new() { Protocol = "ollama", BaseUrl = "http://127.0.0.1:11434", Model = "mock" }, null, "", "hello", default) == "ollama reply", "ollama response parse");
            using (var transport = new AiTransport(new MockHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("mock-secret-server-body") }))))
            {
                try { await transport.GenerateAsync(new() { BaseUrl = "https://unit.invalid/v1", Model = "mock" }, "fixture", "", "hello", default); throw new Exception("accepted HTTP failure"); }
                catch (InvalidOperationException error) { Check(error.Message.Contains("401") && !error.Message.Contains("mock-secret-server-body"), "HTTP failure redaction"); }
            }
            var started = DateTime.Now;
            var inbound = new ChatSimpleMessage { Who = "friend", Message = "new text", UniqueString = "one", DateTime = started.AddMinutes(1) };
            var seen = new HashSet<string>();
            Check(AiReplyPolicy.Accept("friend", "self", inbound, started, seen), "new whitelisted inbound");
            seen.Add(AiReplyPolicy.Identity("friend", inbound));
            Check(!AiReplyPolicy.Accept("friend", "self", inbound, started, seen), "dedupe");
            seen.Clear(); inbound.Who = "self";
            Check(!AiReplyPolicy.Accept("friend", "self", inbound, started, seen), "self ignored");
            inbound.Who = "stranger";
            Check(!AiReplyPolicy.Accept("friend", "self", inbound, started, seen), "unknown sender ignored");
            inbound.Who = "friend"; inbound.DateTime = started.AddMinutes(-1);
            Check(!AiReplyPolicy.Accept("friend", "self", inbound, started, seen), "history ignored");
            inbound.DateTime = started.AddMinutes(1); inbound.Message = "[图片]";
            Check(!AiReplyPolicy.Accept("friend", "self", inbound, started, seen), "nontext ignored");
            Console.WriteLine($"AI_SELFTEST_PASS checks={checks} network=mock-only wechat=untouched");
            return 0;
        }
        catch (Exception exception) { Console.WriteLine("AI_SELFTEST_FAIL " + exception.Message); return 1; }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private sealed class MockHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handle(request);
    }
}
