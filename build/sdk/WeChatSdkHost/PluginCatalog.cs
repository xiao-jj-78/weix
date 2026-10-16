using System.Reflection;
using WeChatSdk.PluginContracts;

namespace WeChatSdkHost;

public sealed class PluginCatalog
{
    private readonly List<IReadOnlyPlugin> _plugins = [new SdkHealthPlugin(), new SdkReadPlugin("sdk-session", "读取 SDK 会话", "使用 C# SDK 获取账号和当前可见会话；会聚焦/调整微信窗口。", "session"), new SdkReadPlugin("sdk-contacts", "读取通讯录姓名", "通过 C# SDK 读取已登录账号的通讯录，可能切换微信页面并写入本地 SDK 缓存。", "contacts")];
    public List<string> Errors { get; } = [];
    public PluginCatalog()
    {
        // Only local, owner-installed assemblies under the plugins/ folder are
        // loaded. HTTP cannot upload a DLL or name a path. Local plugins are
        // trusted .NET code, not a sandbox.
        var dir = Path.Combine(AppContext.BaseDirectory, "plugins");
        if (!Directory.Exists(dir)) return;
        foreach (var dll in Directory.GetFiles(dir, "*.dll"))
        {
            try
            {
                var assembly = Assembly.LoadFrom(dll);
                foreach (var type in assembly.GetTypes().Where(type => typeof(IReadOnlyPlugin).IsAssignableFrom(type) && type is { IsClass: true, IsAbstract: false }))
                    if (Activator.CreateInstance(type) is IReadOnlyPlugin plugin && _plugins.All(existing => existing.Id != plugin.Id)) _plugins.Add(plugin);
            }
            catch (Exception exception) { Errors.Add("插件加载失败 " + Path.GetFileName(dll) + "：" + exception.GetBaseException().Message); }
        }
    }
    public IReadOnlyPlugin? Find(string id) => _plugins.FirstOrDefault(plugin => plugin.Id == id);
    public object[] Describe() => _plugins.Select(plugin => (object)new { id = plugin.Id, name = plugin.Name, description = plugin.Description, readOnly = true, source = plugin.Id == "session-snapshot" ? "local-assembly" : "built-in", desktopInteraction = plugin.Id is "sdk-session" or "sdk-contacts" }).ToArray();
}

internal sealed class SdkHealthPlugin : IReadOnlyPlugin
{
    public string Id => "sdk-health";
    public string Name => "SDK 兼容性检查";
    public string Description => "读取真实 SDK 程序集、适配器能力与桌面依赖。";
    public Task<object> RunAsync(IReadOnlyPluginContext context, CancellationToken cancellationToken) => Task.FromResult(context.GetSdkHealth());
}

internal sealed class SdkReadPlugin(string id, string name, string description, string operation) : IReadOnlyPlugin
{
    public string Id => id;
    public string Name => name;
    public string Description => description;
    public Task<object> RunAsync(IReadOnlyPluginContext context, CancellationToken cancellationToken) => context.RunSdkReadOnlyAsync(operation, cancellationToken);
}
