using System.Text.Json;
using Microsoft.Maui.Storage;

namespace WeChatLikeMaui.Services;

public static class PhoneWorkspace
{
    private static readonly object Gate = new();
    private static readonly List<PhoneMessage> Inbox = new();
    private static readonly List<WatchAuditEntry> WatchLog = new();
    public static event Action? Changed;
    public static bool ListenerConnected { get; set; }
    public static string Status { get; private set; } = "消息只保留在本次运行的手机内存中。";
    public static PhoneAiProfile Profile => new(Preferences.Get("phone.ai.url", "https://apihub.agnes-ai.com/v1"), Preferences.Get("phone.ai.model", "agnes-3.0-flash"), Preferences.Get("phone.ai.prompt", "用简洁、友好的中文回复。"));
    public static string[] Targets => Preferences.Get("phone.targets", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    public static IReadOnlyList<PhoneMessage> Messages { get { lock (Gate) return Inbox.ToArray(); } }
    public static void SetStatus(string message) { Status = message; Changed?.Invoke(); }
    public static void Put(PhoneMessage message)
    {
        lock (Gate) {
            if (Inbox.Any(m => m.Id == message.Id)) return;
            Inbox.RemoveAll(m => m.NotificationKey == message.NotificationKey);
            Inbox.Insert(0, message);
            if (Inbox.Count > 30) Inbox.RemoveRange(30, Inbox.Count - 30);
        }
        Changed?.Invoke();
    }
    public static void Remove(string key) { lock (Gate) Inbox.RemoveAll(m => m.NotificationKey == key); Changed?.Invoke(); }
    public static void Clear() { lock (Gate) Inbox.Clear(); SetStatus("已清空手机内存中的消息。"); }
    /// <summary>最近 50 条巡检决策记录，供本人回看与止损；进程内保留，同时落本地文件。</summary>
    public static IReadOnlyList<WatchAuditEntry> WatchAudit { get { lock (Gate) return WatchLog.ToArray(); } }
    /// <summary>
    /// 自动巡检每次决策的留痕入口。只做可观察性记录：不改变匹配逻辑、不扩大操作范围。
    /// 记录同时写入本机 AppDataDirectory/watch-audit.log（仅本机、不含消息正文之外的内容）。
    /// </summary>
    public static void RecordWatchAudit(WatchAuditEntry entry)
    {
        lock (Gate) { WatchLog.Insert(0, entry); if (WatchLog.Count > 50) WatchLog.RemoveRange(50, WatchLog.Count - 50); }
        try
        {
            var path = Path.Combine(FileSystem.AppDataDirectory, "watch-audit.log");
            if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024)
                File.WriteAllText(path, "");
            File.AppendAllText(path, $"{entry.At:yyyy-MM-dd HH:mm:ss}\t{entry.Decision}\t{entry.Author}\t{entry.Detail}{Environment.NewLine}");
        }
        catch { /* 审计落盘失败不影响巡检本身 */ }
        // 仅把需要本人立刻知道的少数事件提示到界面，避免每 15 秒刷屏。
        if (entry.Decision is "Liked" or "CapReached")
            SetStatus(entry.Decision == "Liked"
                ? $"已点赞：{entry.Author}（本次自动巡检累计 {WatchLog.Count(e => e.Decision == "Liked")} 次）"
                : "本次自动巡检已达上限，已停止点赞；可在巡检记录中核对。");
        else Changed?.Invoke();
    }
    public static void ValidateProfile(PhoneAiProfile profile) {
        PhoneRules.CompletionUri(profile.BaseUrl);
        if (string.IsNullOrWhiteSpace(profile.Model) || profile.Model.Length > 160 || profile.Prompt.Length > 4000) throw new InvalidOperationException("请核对模型名称和提示词长度。");
    }
    public static void SaveProfile(PhoneAiProfile profile) {
        ValidateProfile(profile);
        Preferences.Set("phone.ai.url", profile.BaseUrl.Trim().TrimEnd('/'));
        Preferences.Set("phone.ai.model", profile.Model.Trim());
        Preferences.Set("phone.ai.prompt", profile.Prompt);
    }
    public static void SaveTargets(string text) {
        var names = text.Split('\n',StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).ToArray();
        if (names.Length > 20 || names.Any(n => n.Length > 100 || n.Any(char.IsControl))) throw new InvalidOperationException("最多 20 个会话标题，每行一个，最长 100 字。");
        Preferences.Set("phone.targets",string.Join('\n',names));
        // Changing targets always requires enabling automatic replies again.
        Preferences.Set("phone.auto",false);
    }
    public static async Task<string> GenerateAsync(string input, CancellationToken token) =>
        await PhoneRules.GenerateAsync(Profile, await SecureStorage.GetAsync("phone.ai.key") ?? "", input, token);
    public static void DisableAuto() { Preferences.Set("phone.auto",false); SetStatus("自动回复已关闭。"); }
}
