using WeChatSdk.PluginContracts;

namespace WeChatSdk.OwnDataPlugin;

// Demonstrates the independent-host-plugin model. This assembly is installed locally
// by the owner under plugins/. It never runs inside WeChat. It only calls the host's
// read-only SDK bridge for the owner's own logged-in account.
public sealed class OwnDataPlugin : IReadOnlyPlugin
{
    public string Id => "own-data";
    public string Name => "本人数据只读插件";
    public string Description => "通过宿主已加载的 SDK，读取本人已登录账号的登录态、通讯录与聊天记录；不注入微信、不读取他人数据。";

    public Task<object> RunAsync(IReadOnlyPluginContext context, CancellationToken cancellationToken)
        => RunAsync(context, new PluginRunArgs("contacts"), cancellationToken);

    public Task<object> RunAsync(IReadOnlyPluginContext context, PluginRunArgs args, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return args.Operation switch
        {
            "login" => context.RequestOfficialLoginAsync(cancellationToken),
            "contacts" => context.RunSdkReadOnlyAsync("contacts", cancellationToken),
            "chat" => context.RunSdkChatAsync(args.Who ?? throw new InvalidOperationException("聊天读取需要提供 who（会话标识）。"), cancellationToken),
            _ => Task.FromResult<object>(new { ok = false, error = "unknown_operation", message = "支持 login / contacts / chat。", id = Id })
        };
    }
}
