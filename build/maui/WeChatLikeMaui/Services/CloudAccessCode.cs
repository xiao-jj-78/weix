using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WeChatLikeMaui.Services;

public static class CloudAccessCode
{
    public static async Task CopyAsync()
    {
#if WINDOWS
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WeChatLikeConsole", "CloudBridge", "config.json");
        if (!File.Exists(path)) throw new InvalidOperationException("本机尚未配对云连接，请先运行交付包的“连接腾讯云”。");
        if (new FileInfo(path).Length > 65536) throw new InvalidOperationException("云连接配置无效。");
        using var config = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var encrypted = Convert.FromBase64String(config.RootElement.GetProperty("browserTokenProtected").GetString()!);
        var source = new Blob { Length = encrypted.Length, Data = Marshal.AllocHGlobal(encrypted.Length) };
        var plain = new Blob(); string token;
        try
        {
            Marshal.Copy(encrypted, 0, source.Data, encrypted.Length);
            if (!CryptUnprotectData(ref source, 0, 0, 0, 0, 1, out plain)) throw new InvalidOperationException("当前 Windows 用户无法读取云访问码，请重新配对。");
            var bytes = new byte[plain.Length];
            try { Marshal.Copy(plain.Data, bytes, 0, bytes.Length); token = Encoding.UTF8.GetString(bytes); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        finally { Marshal.FreeHGlobal(source.Data); if (plain.Data != 0) LocalFree(plain.Data); }
        if (token.Length is < 32 or > 512 || token.Any(char.IsWhiteSpace)) throw new InvalidOperationException("云访问码格式无效，请重新配对。");
        await Clipboard.Default.SetTextAsync(token);
        _clipboardSequence = GetClipboardSequenceNumber();
        _ = ClearLater(_clipboardSequence);
#else
        throw new InvalidOperationException("请先在 Windows 电脑复制云访问码，再在此设备粘贴。");
#endif
    }
#if WINDOWS
    private static uint _clipboardSequence;
    public static void ClearIfOwned(uint expected = 0)
    {
        var sequence = _clipboardSequence;
        if (sequence == 0 || (expected != 0 && expected != sequence) || GetClipboardSequenceNumber() != sequence) return;
        if (!OpenClipboard(0)) return;
        try { if (GetClipboardSequenceNumber() == sequence) { EmptyClipboard(); _clipboardSequence = 0; } }
        finally { CloseClipboard(); }
    }
    private static async Task ClearLater(uint sequence)
    {
        await Task.Delay(TimeSpan.FromSeconds(60));
        try { await MainThread.InvokeOnMainThreadAsync(() => ClearIfOwned(sequence)); } catch { }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public nint Data; }
    [DllImport("crypt32.dll", SetLastError = true)] private static extern bool CryptUnprotectData(ref Blob source, nint description, nint entropy, nint reserved, nint prompt, int flags, out Blob result);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint value);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] private static extern bool OpenClipboard(nint owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
#else
    public static void ClearIfOwned(uint expected = 0) { }
#endif
}
