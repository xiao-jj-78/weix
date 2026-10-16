using System.Text.Json;
using WeAutoCommon.Enums;
using WeChatAuto.Components;
using WeChatAuto.Models;

namespace WeChatSdkHost;

internal static class AiWorker
{
    public static async Task<int> RunAsync(string[] args)
    {
        var index = Array.IndexOf(args, "--status");
        if (index < 0 || index + 1 >= args.Length) return 2;
        var output = args[index + 1];
        var replies = 0;
        DateTimeOffset? activity = null;
        void Status(string state, string? error = null) => WriteStatus(output, new { state, lastError = error, replyCount = replies, lastActivityAt = activity, heartbeatAt = DateTimeOffset.UtcNow });
        try
        {
            Status("starting");
            var (config, profile, key) = new AiConfigStore().Resolve();
            if (config.AllowGroups || config.AllowedContacts.Length is < 1 or > 10) throw new AiOperationException("请先选择私聊联系人。");
            using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(config.SessionMinutes));
            var token = lifetime.Token;
            using var provider = SdkWorker.CreateProvider();
            var clients = provider.GetRequiredService<WeChatClientFactory>().GetWeChatClientList();
            var matches = clients.Where(p => config.Account.Length == 0 || p.Key == config.Account || p.Value.WxId == config.Account).ToArray();
            if (matches.Length != 1) throw new AiOperationException("未找到唯一的已登录账号；多账号时请先在 AI 设置中指定 SDK 账号名或 wxid。");
            var client = matches[0].Value;
            if (config.AllowedContacts.Any(c => c == client.NickName)) throw new AiOperationException("联系人与当前账号同名，无法可靠区分自己。请使用唯一备注名。");
            using var transport = new AiTransport();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var sent = new Queue<DateTimeOffset>();
            var lastReply = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
            var started = DateTime.Now;
            // Establish every contact before generating any response. Old chat
            // history, including history visible in the first scan, is skipped.
            Status("baselining");
            foreach (var contact in config.AllowedContacts)
            {
                token.ThrowIfCancellationRequested();
                var history = await client.GetChatHistory(contact, DateTime.Today);
                await RequirePrivateTitle(client, contact);
                foreach (var message in history) seen.Add(AiReplyPolicy.Identity(contact, message));
                activity = DateTimeOffset.UtcNow; Status("baselining");
            }
            Status("running");
            while (!token.IsCancellationRequested)
            {
                foreach (var contact in config.AllowedContacts)
                {
                    token.ThrowIfCancellationRequested();
                    // SDK returns minute-resolution timestamps. Strict > start
                    // deliberately ignores the startup minute to avoid treating
                    // an old message loaded later as newly received.
                    var history = await client.GetChatHistory(contact, started.AddMinutes(-1), DateTime.Now);
                    await RequirePrivateTitle(client, contact);
                    var candidates = history.Where(m => AiReplyPolicy.Accept(contact, client.NickName, m, started, seen)).OrderBy(m => m.DateTime).ToArray();
                    foreach (var message in history) seen.Add(AiReplyPolicy.Identity(contact, message));
                    if (seen.Count > 100000) throw new AiOperationException("本次消息去重容量已到，请停止后重新启动。");
                    activity = DateTimeOffset.UtcNow; Status("running");
                    if (candidates.Length == 0) continue;
                    var now = DateTimeOffset.UtcNow;
                    while (sent.Count > 0 && now - sent.Peek() >= TimeSpan.FromHours(1)) sent.Dequeue();
                    if (sent.Count >= config.MaxRepliesPerHour || (lastReply.TryGetValue(contact, out var last) && now - last < TimeSpan.FromSeconds(config.MinReplySeconds))) continue;
                    // One reply for the latest new text avoids a burst of
                    // responses when several messages arrive in one poll.
                    string reply;
                    try { reply = await transport.GenerateAsync(profile, key, config.SystemPrompt, candidates[^1].Message, token); }
                    catch (InvalidOperationException exception) { throw new AiOperationException(exception.Message); }
                    token.ThrowIfCancellationRequested();
                    await client.SearchFriend(contact);
                    await RequirePrivateTitle(client, contact);
                    token.ThrowIfCancellationRequested();
                    await client.SendMessage(reply);
                    sent.Enqueue(DateTimeOffset.UtcNow); lastReply[contact] = DateTimeOffset.UtcNow;
                    replies++; activity = DateTimeOffset.UtcNow; Status("running");
                }
                await Task.Delay(TimeSpan.FromSeconds(config.MinReplySeconds), token);
            }
            Status("completed");
        }
        catch (OperationCanceledException) { Status("completed"); }
        catch (Exception exception)
        {
            // Upstream exceptions may include conversation text/UI details.
            // Report only controlled adapter errors to the cross-device UI.
            var message = exception is AiOperationException ? exception.Message : "SDK/AI 执行失败；请检查官方微信登录、公开 UI Tree、账号和接口设置。";
            Status("error", message);
            return 1;
        }
        return 0;
    }

    private static async Task RequirePrivateTitle(WeChatClient client, string expected)
    {
        var title = await client.GetTitle();
        if (title.HeaderType != ChatType.好友 || !string.Equals(title.Title, expected, StringComparison.Ordinal)) throw new AiOperationException("当前会话与所选私聊联系人不一致，已停止。请为联系人设置唯一备注并检查群聊/重名情况。");
    }
    private static void WriteStatus(string path, object value)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, AiConfigStore.Json));
        File.Move(temporary, path, overwrite: true);
    }
}

internal sealed class AiOperationException(string message) : InvalidOperationException(message);

internal static class AiReplyPolicy
{
    internal static string Identity(string contact, ChatSimpleMessage message) => contact + "\u001f" + message.DateTime.Ticks + "\u001f" + message.Who + "\u001f" + message.UniqueString + "\u001f" + message.Message;
    internal static bool Accept(string contact, string self, ChatSimpleMessage message, DateTime started, HashSet<string> seen) =>
        message.Who == contact && message.Who != self && message.DateTime > started && !string.IsNullOrWhiteSpace(message.Message) && message.Message.Length <= 8000 && !message.Message.StartsWith('[') && !seen.Contains(Identity(contact, message));
}
