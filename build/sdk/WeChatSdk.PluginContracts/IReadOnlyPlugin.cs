namespace WeChatSdk.PluginContracts;

// Carries an operation selector and an optional conversation id ("who") so a single
// plugin can expose several read-only operations through the same entry point.
public sealed record PluginRunArgs(string Operation, string? Who = null);

// This interface intentionally exposes snapshots and a thin set of read-only SDK
// operations only. Plugins run in the local host process, never in WeChat, and must
// be installed by a trusted local owner. No write, no injection, no credential input.
public interface IReadOnlyPlugin
{
    string Id { get; }
    string Name { get; }
    string Description { get; }
    Task<object> RunAsync(IReadOnlyPluginContext context, CancellationToken cancellationToken);
    // New overload lets a plugin receive an operation selector + optional "who".
    // The default forwards to the legacy single-signature so existing plugins keep working.
    Task<object> RunAsync(IReadOnlyPluginContext context, PluginRunArgs args, CancellationToken cancellationToken)
        => RunAsync(context, cancellationToken);
}

public interface IReadOnlyPluginContext
{
    object GetSessionSnapshot();
    object GetSdkHealth();
    Task<object> RunSdkReadOnlyAsync(string operation, CancellationToken cancellationToken);
    // Owner's own chat history for a single conversation. who is supplied by the owner.
    Task<object> RunSdkChatAsync(string who, CancellationToken cancellationToken);
    // Request the official client to open so the owner can scan. Never automates credentials.
    Task<object> RequestOfficialLoginAsync(CancellationToken cancellationToken);
}
