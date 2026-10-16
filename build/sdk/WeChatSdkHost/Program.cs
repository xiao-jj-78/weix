using System.Net;
using System.Text.Json;
using WeChatSdk.PluginContracts;
using WeChatSdkHost;

if (args.Contains("--ai-selftest"))
{
    Environment.ExitCode = await AiSelfTest.RunAsync();
    return;
}
if (args.Length >= 2 && args[0] == "--sdk-worker")
{
    Environment.ExitCode = await SdkWorker.RunAsync(args);
    return;
}
if (args.Length >= 1 && args[0] == "--ai-worker")
{
    Environment.ExitCode = await AiWorker.RunAsync(args);
    return;
}

var port = 18766;
for (var i = 0; i < args.Length; i++)
    if (args[i] == "--port" && i + 1 < args.Length && (!int.TryParse(args[++i], out port) || port < 1024 || port > 65535))
        throw new ArgumentException("--port 必须在 1024 到 65535 之间。");

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], ContentRootPath = AppContext.BaseDirectory });
// A local companion is deliberately bound to loopback. The existing authenticated
// console gateway owns LAN access; a browser cannot upload or execute plugins.
builder.WebHost.ConfigureKestrel(options => { options.Listen(IPAddress.Loopback, port); options.Limits.MaxRequestBodySize = 256 * 1024; });
builder.Services.AddSingleton<HostState>();
builder.Services.AddSingleton<ClientManager>();
builder.Services.AddSingleton<PluginCatalog>();
builder.Services.AddSingleton<AiConfigStore>();
builder.Services.AddSingleton<AiTransport>();
builder.Services.AddSingleton<AiCoordinator>();
builder.Services.AddHostedService<SessionSupervisor>();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
var app = builder.Build();
app.Services.GetRequiredService<AiCoordinator>().RegisterShutdown();
app.Use(async (context, next) =>
{
    if (context.Request.Method != "GET")
    {
        if (!context.Request.HasJsonContentType())
        {
            context.Response.StatusCode = 415;
            await context.Response.WriteAsJsonAsync(new { ok = false, error = "json_required", message = "请求必须使用 application/json。" });
            return;
        }
        var origin = context.Request.Headers.Origin.ToString();
        if (origin.Length > 0 && (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || !uri.IsLoopback))
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new { ok = false, error = "origin_denied" });
            return;
        }
    }
    await next(context);
});

app.MapGet("/api/sdk/status", (HostState state, PluginCatalog plugins) => new
{
    ok = true, running = true, app = "WeChatSdkHost", version = "2.0.0", mode = "interactive-desktop",
    uptime = state.Uptime, session = state.GetSessionSnapshot(), sdk = state.GetSdkHealth(), plugins = plugins.Describe(),
    message = "后台宿主已启动。微信需在同一 Windows 用户桌面中运行，扫码登录在官方窗口完成。"
});
app.MapGet("/api/sdk/plugins", (PluginCatalog plugins) => new { ok = true, plugins = plugins.Describe(), errors = plugins.Errors });
app.MapPost("/api/sdk/host/stop", (HttpContext context, IHostApplicationLifetime lifetime) =>
{
    if (context.Connection.RemoteIpAddress is not { } address || !IPAddress.IsLoopback(address)) return Results.Json(new { ok = false, error = "loopback_required" }, statusCode: 403);
    context.Response.OnCompleted(() => { lifetime.StopApplication(); return Task.CompletedTask; });
    return Results.Json(new { ok = true, message = "SDK 宿主正在停止。" });
});
app.MapAiEndpoints();
app.MapClientEndpoints();
app.MapPost("/api/sdk/login", (HostState state) => Results.Json(state.OpenOfficialLogin()));
app.MapGet("/api/sdk/chat", async (HostState state, HttpContext context) =>
{
    var who = context.Request.Query["who"].ToString();
    try { return Results.Json(await state.RunSdkChatAsync(who, context.RequestAborted)); }
    catch (OperationCanceledException) { return Results.Json(new { ok = false, error = "cancelled", message = "请求已取消或超过 90 秒。" }, statusCode: 408); }
    catch (InvalidOperationException exception) { return Results.Json(new { ok = false, error = "invalid_request", message = exception.Message }, statusCode: 400); }
});
app.MapPost("/api/sdk/plugins/run", async (PluginRequest request, PluginCatalog catalog, HostState state, HttpContext context) =>
{
    var plugin = catalog.Find(request.Id);
    if (plugin is null)
        return Results.Json(new { ok = false, error = "plugin_not_found", message = "未找到本地已安装的插件。" }, statusCode: 404);
    try
    {
        var result = await plugin.RunAsync(state, new PluginRunArgs(request.Operation ?? "contacts", request.Who), context.RequestAborted);
        if (result is JsonElement value && value.TryGetProperty("ok", out var childOk) && !childOk.GetBoolean())
            return Results.Json(new { ok = false, id = plugin.Id, result, message = value.TryGetProperty("message", out var msg) ? msg.GetString() : "SDK 读取失败。" });
        return Results.Json(new { ok = true, id = plugin.Id, result });
    }
    catch (OperationCanceledException)
    {
        return Results.Json(new { ok = false, error = "cancelled", message = "插件已取消或超时。" }, statusCode: 408);
    }
    catch (InvalidOperationException exception)
    {
        return Results.Json(new { ok = false, error = "sdk_unavailable", message = exception.Message }, statusCode: 409);
    }
});
await app.RunAsync();

internal sealed record PluginRequest(string Id, string? Operation = null, string? Who = null);
