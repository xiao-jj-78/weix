using WeChatSdk.PluginContracts;

namespace WeChatSdk.SamplePlugin;

public sealed class SessionSnapshotPlugin : IReadOnlyPlugin
{
    public string Id => "session-snapshot";
    public string Name => "桌面会话快照";
    public string Description => "读取宿主已经采集的客户端状态，不读取私聊内容。";
    public Task<object> RunAsync(IReadOnlyPluginContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(context.GetSessionSnapshot());
    }
}
