using System.Text.Json;

namespace WeChatSdkHost;

internal static class AiEndpoints
{
    internal static void MapAiEndpoints(this WebApplication app)
    {
        app.MapGet("/api/sdk/ai/config", (AiCoordinator ai) => Safe(() => ai.Config()));
        app.MapPost("/api/sdk/ai/config", (JsonElement body, AiCoordinator ai) => Safe(() => ai.Save(body)));
        app.MapGet("/api/sdk/ai/profiles/export", (AiCoordinator ai) => Safe(() => ai.Config()));
        app.MapPost("/api/sdk/ai/profiles/import", (AiImportRequest body, AiCoordinator ai) => Safe(() => ai.Import(body.Json)));
        app.MapGet("/api/sdk/ai/status", (AiCoordinator ai) => ai.Status());
        app.MapPost("/api/sdk/ai/start", (AiCoordinator ai) => Safe(() => ai.Start()));
        app.MapPost("/api/sdk/ai/stop", (AiCoordinator ai) => Safe(() => ai.Stop()));
        app.MapPost("/api/sdk/ai/preview", async (AiPreviewRequest body, AiCoordinator ai, HttpContext context) => await SafeAsync(() => ai.PreviewAsync(body.Message, context.RequestAborted)));
        app.MapGet("/api/sdk/contacts", async (HostState state, HttpContext context) => await SafeAsync(() => state.RunSdkReadOnlyAsync("contacts", context.RequestAborted)));
    }
    private static IResult Safe(Func<object> action)
    {
        try { return Results.Json(action()); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException or NullReferenceException or FormatException)
        { return Failure(exception); }
    }
    private static async Task<IResult> SafeAsync(Func<Task<object>> action)
    {
        try { return Results.Json(await action()); }
        catch (OperationCanceledException) { return Results.Json(new { ok = false, error = "cancelled", message = "请求已取消或超过 45 秒。" }, statusCode: 408); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException or NullReferenceException or FormatException)
        { return Failure(exception); }
    }
    private static IResult Failure(Exception exception) => Results.Json(new { ok = false, error = "ai_configuration_or_operation_failed", message = exception is JsonException or NullReferenceException or FormatException ? "AI JSON 格式或字段类型无效。" : exception.Message }, statusCode: exception is ArgumentException or JsonException or NullReferenceException or FormatException ? 400 : 409);
}
internal sealed record AiImportRequest(string Json);
internal sealed record AiPreviewRequest(string Message);
