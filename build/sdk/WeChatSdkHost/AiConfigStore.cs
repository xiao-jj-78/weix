using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WeChatSdkHost;

public sealed class AiProfile
{
    public string Id { get; set; } = "default";
    public string Name { get; set; } = "自定义兼容接口";
    public string Protocol { get; set; } = "openai-compatible";
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "";
    public string ApiKeyEnv { get; set; } = "WLC_AI_API_KEY";
    public string? EncryptedApiKey { get; set; }
}

public sealed class AiConfiguration
{
    public string ActiveProfileId { get; set; } = "default";
    public List<AiProfile> Profiles { get; set; } = [new()];
    public string SystemPrompt { get; set; } = "请用自然、简洁、礼貌的中文回复。只回复当前消息，不编造身份或事实。";
    public string[] AllowedContacts { get; set; } = [];
    public string Account { get; set; } = "";
    public bool AllowGroups { get; set; }
    public int MinReplySeconds { get; set; } = 15;
    public int MaxRepliesPerHour { get; set; } = 20;
    public int SessionMinutes { get; set; } = 60;
}

public sealed class AiConfigStore
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly object _gate = new();
    private readonly string _path;
    public AiConfigStore() : this(Environment.GetEnvironmentVariable("WLC_SDK_AI_CONFIG_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WeChatLikeConsole", "SdkAi")) { }
    internal AiConfigStore(string directory) { Directory.CreateDirectory(directory); _path = Path.Combine(directory, "ai-config.json"); }

    public AiConfiguration Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path)) return new();
            try { return JsonSerializer.Deserialize<AiConfiguration>(File.ReadAllText(_path), Json) ?? new(); }
            catch { throw new InvalidOperationException("本地 AI 配置无法读取；请备份并修复 ai-config.json。"); }
        }
    }

    public object Public(AiConfiguration config) => new
    {
        config.ActiveProfileId,
        profiles = config.Profiles.Select(p => new { p.Id, p.Name, p.Protocol, p.BaseUrl, p.Model, p.ApiKeyEnv, hasApiKey = !string.IsNullOrEmpty(p.EncryptedApiKey) || (!string.IsNullOrEmpty(p.ApiKeyEnv) && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(p.ApiKeyEnv))) }),
        config.SystemPrompt, config.AllowedContacts, config.Account, config.AllowGroups, config.MinReplySeconds, config.MaxRepliesPerHour, config.SessionMinutes
    };

    public object Save(JsonElement patch)
    {
        lock (_gate)
        {
            var config = Load();
            if (patch.ValueKind != JsonValueKind.Object) throw new ArgumentException("AI 配置必须是 JSON 对象。");
            if (patch.TryGetProperty("config", out var nested)) patch = nested;
            if (patch.TryGetProperty("profiles", out var profiles))
            {
                if (profiles.ValueKind != JsonValueKind.Array || profiles.GetArrayLength() is < 1 or > 30) throw new ArgumentException("供应商数量应为 1 到 30 个。");
                var list = new List<AiProfile>();
                foreach (var item in profiles.EnumerateArray())
                {
                    var profile = JsonSerializer.Deserialize<AiProfile>(item.GetRawText(), Json) ?? throw new ArgumentException("供应商格式错误。");
                    if (!Regex.IsMatch(profile.Id, @"^[A-Za-z0-9_-]{1,64}$")) throw new ArgumentException("供应商 ID 只允许字母、数字、下划线和短横线，最多 64 字符。");
                    if (list.Any(p => p.Id == profile.Id)) throw new ArgumentException("供应商 ID 不能重复。");
                    if (profile.Protocol is not ("openai-compatible" or "ollama")) throw new ArgumentException("只支持 openai-compatible 或 ollama 协议；Claude 专用接口不能直接导入为兼容接口。");
                    profile.Name = Bounded(profile.Name, 80, "供应商名称");
                    profile.Model = Bounded(profile.Model.Trim(), 200, "模型名称");
                    profile.BaseUrl = profile.BaseUrl.Trim();
                    if (profile.BaseUrl.Length > 0) _ = AiTransport.Endpoint(profile);
                    if (profile.ApiKeyEnv.Length > 0 && !Regex.IsMatch(profile.ApiKeyEnv, @"^[A-Za-z_][A-Za-z0-9_]{0,99}$")) throw new ArgumentException("密钥环境变量名称无效。");
                    // GET/export has no encrypted field. Only explicit write-only apiKey may change it.
                    profile.EncryptedApiKey = config.Profiles.FirstOrDefault(p => p.Id == profile.Id)?.EncryptedApiKey;
                    if (item.TryGetProperty("clearApiKey", out var clear) && clear.ValueKind == JsonValueKind.True) profile.EncryptedApiKey = null;
                    if (item.TryGetProperty("apiKey", out var key) && !string.IsNullOrEmpty(key.GetString()))
                    {
                        var raw = key.GetString()!;
                        if (raw.Length > 4096 || raw.Any(char.IsControl)) throw new ArgumentException("API 密钥格式无效。");
                        profile.EncryptedApiKey = WindowsSecret.Protect(raw);
                    }
                    list.Add(profile);
                }
                config.Profiles = list;
            }
            if (patch.TryGetProperty("activeProfileId", out var active)) config.ActiveProfileId = active.GetString() ?? "";
            if (!config.Profiles.Any(p => p.Id == config.ActiveProfileId)) throw new ArgumentException("请选择存在的供应商方案。");
            if (patch.TryGetProperty("systemPrompt", out var prompt)) config.SystemPrompt = Bounded(prompt.GetString() ?? "", 20000, "提示词");
            if (patch.TryGetProperty("allowedContacts", out var contacts)) config.AllowedContacts = contacts.EnumerateArray().Select(c => Bounded((c.GetString() ?? "").Trim(), 100, "联系人")).Where(c => c.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
            if (config.AllowedContacts.Length > 10) throw new ArgumentException("当前轮询模式最多选择 10 个联系人。");
            if (patch.TryGetProperty("account", out var account)) config.Account = Bounded(account.GetString() ?? "", 200, "微信账号");
            if (patch.TryGetProperty("allowGroups", out var groups)) config.AllowGroups = groups.GetBoolean();
            if (config.AllowGroups) throw new ArgumentException("当前版本仅支持私聊，群聊自动回复尚未启用。");
            if (patch.TryGetProperty("minReplySeconds", out var min)) config.MinReplySeconds = min.GetInt32();
            if (patch.TryGetProperty("maxRepliesPerHour", out var max)) config.MaxRepliesPerHour = max.GetInt32();
            if (patch.TryGetProperty("sessionMinutes", out var duration)) config.SessionMinutes = duration.GetInt32();
            if (config.MinReplySeconds is < 10 or > 3600 || config.MaxRepliesPerHour is < 1 or > 120 || config.SessionMinutes is < 1 or > 480) throw new ArgumentException("回复间隔 10–3600 秒、每小时 1–120 条、托管时间 1–480 分钟。");
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(config, Json), new UTF8Encoding(false));
            File.Move(temporary, _path, overwrite: true);
            return Public(config);
        }
    }

    public object Import(string text)
    {
        if (text.Length > 200000) throw new ArgumentException("导入 JSON 不能超过 200 KB。");
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        if (root.TryGetProperty("config", out var nested)) root = nested;
        if (root.TryGetProperty("profiles", out _)) return Save(root);
        // Deliberately narrow interoperability: CC Switch has several unrelated
        // agent formats. Accept explicit OpenAI env fields only, never guess that
        // ANTHROPIC_AUTH_TOKEN speaks Chat Completions.
        if (root.TryGetProperty("settingsConfig", out var settings) && settings.TryGetProperty("env", out var env))
        {
            if (env.TryGetProperty("ANTHROPIC_BASE_URL", out _) || env.TryGetProperty("ANTHROPIC_AUTH_TOKEN", out _)) throw new ArgumentException("这是 Claude/Anthropic 专用配置。请创建 OpenAI 兼容方案并填写兼容地址、模型和密钥。");
            var baseUrl = env.TryGetProperty("OPENAI_BASE_URL", out var url) ? url.GetString() : "";
            var model = env.TryGetProperty("OPENAI_MODEL", out var modelValue) ? modelValue.GetString() : "";
            var key = env.TryGetProperty("OPENAI_API_KEY", out var keyValue) ? keyValue.GetString() : "";
            var config = Load();
            var id = "import-" + Guid.NewGuid().ToString("N")[..10];
            var profiles = config.Profiles.Select(p => JsonSerializer.SerializeToElement(new { p.Id, p.Name, p.Protocol, p.BaseUrl, p.Model, p.ApiKeyEnv }, Json)).ToList();
            profiles.Add(JsonSerializer.SerializeToElement(new { id, name = root.TryGetProperty("name", out var name) ? name.GetString() : "导入方案", protocol = "openai-compatible", baseUrl, model, apiKey = key, apiKeyEnv = "" }, Json));
            return Save(JsonSerializer.SerializeToElement(new { profiles, activeProfileId = id }, Json));
        }
        throw new ArgumentException("支持本项目导出的 profiles 配置或含 OPENAI_BASE_URL/OPENAI_MODEL 的供应商 JSON；不支持 CC Switch 数据库或整库备份。");
    }

    public (AiConfiguration Config, AiProfile Profile, string? Key) Resolve()
    {
        var config = Load();
        var profile = config.Profiles.FirstOrDefault(p => p.Id == config.ActiveProfileId) ?? throw new InvalidOperationException("请先选择 AI 供应商。");
        if (string.IsNullOrWhiteSpace(profile.BaseUrl) || string.IsNullOrWhiteSpace(profile.Model)) throw new InvalidOperationException("请先填写 AI 接口地址和模型。");
        _ = AiTransport.Endpoint(profile);
        string? key = null;
        if (!string.IsNullOrEmpty(profile.EncryptedApiKey)) key = WindowsSecret.Unprotect(profile.EncryptedApiKey);
        else if (!string.IsNullOrEmpty(profile.ApiKeyEnv)) key = Environment.GetEnvironmentVariable(profile.ApiKeyEnv);
        if (profile.Protocol == "openai-compatible" && string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("当前方案尚未设置 API 密钥。填写密钥或配置指定的环境变量后重启宿主。");
        return (config, profile, key);
    }

    private static string Bounded(string text, int limit, string label) => text.Length <= limit ? text : throw new ArgumentException($"{label}最多 {limit} 字符。");
}

internal static class WindowsSecret
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    public static string Protect(string raw) => System.Convert.ToBase64String(Convert(Encoding.UTF8.GetBytes(raw), true));
    public static string Unprotect(string encrypted)
    {
        try { return Encoding.UTF8.GetString(Convert(System.Convert.FromBase64String(encrypted), false)); }
        catch { throw new InvalidOperationException("当前 Windows 用户无法解密此方案的密钥，请重新填写。"); }
    }
    private static byte[] Convert(byte[] data, bool protect)
    {
        var input = new Blob { Length = data.Length, Data = Marshal.AllocHGlobal(data.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(data, 0, input.Data, data.Length);
            var ok = protect ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output) : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new CryptographicException("Windows 本地密钥保护失败。");
            var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, output.Length); return result;
        }
        finally
        {
            for (var i = 0; i < input.Length; i++) Marshal.WriteByte(input.Data, i, 0);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
            CryptographicOperations.ZeroMemory(data);
        }
    }
}
