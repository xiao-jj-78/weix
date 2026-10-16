using System.Text.Json;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WeChatAuto.Components;
using WeChatAuto.Extentions;
using WeChatAuto.Services;
using WeChatAuto.Utils;

namespace WeChatSdkHost;

// SDK calls run in a short-lived child process. Several upstream UI traversals
// have retry loops without cancellation; the parent enforces a hard time limit
// and kills this process on cancellation instead of leaving UI work running.
internal static class SdkWorker
{
    public static async Task<int> RunAsync(string[] args)
    {
        var outputIndex = Array.IndexOf(args, "--output");
        var whoIndex = Array.IndexOf(args, "--who");
        var who = whoIndex >= 0 && whoIndex + 1 < args.Length ? args[whoIndex + 1] : null;
        if (outputIndex < 0 || outputIndex + 1 >= args.Length || args[1] is not ("session" or "contacts" or "chat")) return 2;
        var output = args[outputIndex + 1];
        object result;
        try
        {
            if (args[1] == "chat" && string.IsNullOrWhiteSpace(who))
                throw new InvalidOperationException("聊天读取需要 --who 会话标识。");
            using var provider = CreateProvider();
            var factory = provider.GetRequiredService<WeChatClientFactory>();
            var clients = factory.GetWeChatClientList();
            if (clients.Count == 0) throw new InvalidOperationException("SDK 未获取到已登录的微信账号。");
            var accounts = clients.Select(pair => new { name = pair.Key, nickname = pair.Value.NickName, wxid = pair.Value.WxId, pid = pair.Value.ClientProcessId }).ToArray();
            var data = new List<object>();
            foreach (var pair in clients.Take(8))
            {
                if (args[1] == "contacts")
                {
                    var names = await pair.Value.GetAllFriendNames();
                    data.Add(new { account = pair.Key, count = names.Count, names = names.Take(500).ToArray(), truncated = names.Count > 500 });
                }
                else if (args[1] == "chat")
                {
                    var history = await pair.Value.GetChatHistory(who!);
                    data.Add(new { account = pair.Key, who, count = history.Count, messages = history.Take(200).Select(m => new { who = m.Who, message = m.Message, time = m.SendDateTime, at = m.DateTime }).ToArray(), truncated = history.Count > 200 });
                }
                else
                {
                    var titles = await pair.Value.Conversations.GetVisibleConversationTitles();
                    data.Add(new { account = pair.Key, count = titles.Count, visibleConversations = titles.Take(100).ToArray() });
                }
            }
            result = new { ok = true, operation = args[1], source = "WeChatAuto4x.SDK public UIA APIs", accounts, data, readAt = DateTimeOffset.UtcNow, message = "SDK 读取完成；仅代表本次读取时的登录状态。" };
        }
        catch (Exception exception)
        {
            result = new { ok = false, operation = args[1], error = "sdk_read_failed", message = exception.GetBaseException().Message, accounts = Array.Empty<object>() };
        }
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(result));
        return 0;
    }

    internal static ServiceProvider CreateProvider()
    {
            var config = WeAutomation.Config;
            config.IsForceOpenUITree = false;
            config.EnableRecordVideo = false;
            config.EnableOCR = false;
            config.EnableMouseKeyboardSimulator = false;
            config.InitAdressBook = false;
            config.DebugMode = false;
            // Use the SDK's documented process discovery path. That path may
            // activate and arrange official windows, so it only runs on demand.
            config.IsOptimizeMainWindowLayout = true;

            // Preflight only reads the exposed accessibility tree. Do not call
            // ForceOpenUITree or the global Initialize (which writes registry).
            using (var automation = new UIA3Automation())
            {
                var window = automation.GetDesktop().FindFirstChild(condition =>
                    condition.ByClassName("mmui::MainWindow").And(condition.ByControlType(ControlType.Window)));
                if (window is null || window.FindFirstDescendant(condition => condition.ByAutomationId("MainView.main_tabbar")) is null)
                    throw new InvalidOperationException("未找到已登录且公开 UI Tree 的微信 4.x 主窗口。请在官方窗口完成扫码并保持桌面解锁；当前客户端可能未开放辅助功能。");
            }

            // Register upstream public services explicitly. No global SDK
            // initialization, registry edits, remote desktop edits or memory
            // accessibility patching are performed by this adapter.
            var services = new ServiceCollection();
            services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
            services.AddAutoLogger();
            services.AddSingleton(_ => new WeChatRecordVideo(config.TargetVideoPath));
            services.AddSingleton(_ => new WeChatCaptureImage(config.CaptureUIPath, false));
            services.AddSingleton<OCRService>();
            services.AddSingleton<WeChatClientFactory>();
            return services.BuildServiceProvider();
    }
}
