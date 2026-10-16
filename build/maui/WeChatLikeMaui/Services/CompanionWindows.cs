using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WeChatLikeMaui.Services;

public sealed class CompanionWindows : IDisposable
{
#if WINDOWS
    private nint _owner, _wechat;
    private string? _instance;
    private Placement _ownerPlacement, _wechatPlacement;
    public void Arrange(Window window, string instance)
    {
        Dispose();
        using var process = Resolve(instance);
        var owner = WinRT.Interop.WindowNative.GetWindowHandle(window.Handler!.PlatformView);
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(owner, 2), ref monitor)) throw new InvalidOperationException("无法读取当前屏幕工作区。");
        int width = monitor.Work.Right - monitor.Work.Left, height = monitor.Work.Bottom - monitor.Work.Top;
        if (width < 1100 || height < 650) throw new InvalidOperationException("当前屏幕较小，请使用“显示窗口”或切换到更大的屏幕。");
        var a = new Placement { Length = Marshal.SizeOf<Placement>() };
        var b = new Placement { Length = Marshal.SizeOf<Placement>() };
        if (!GetWindowPlacement(owner, ref a) || !GetWindowPlacement(process.MainWindowHandle, ref b)) throw new InvalidOperationException("无法保存窗口原始位置。");
        _owner = owner; _wechat = process.MainWindowHandle; _instance = instance; _ownerPlacement = a; _wechatPlacement = b;
        ShowWindow(owner, 9); ShowWindow(_wechat, 9);
        int leftWidth = Math.Clamp((int)(width * .60), 650, width - 440);
        if (!MoveWindow(owner, monitor.Work.Left, monitor.Work.Top, leftWidth, height, true) ||
            !MoveWindow(_wechat, monitor.Work.Left + leftWidth, monitor.Work.Top, width - leftWidth, height, true))
        { Dispose(); throw new InvalidOperationException("Windows 未允许排列窗口，原位置已恢复。"); }
    }
    public void Dispose()
    {
        if (_owner != 0 && IsWindow(_owner)) SetWindowPlacement(_owner, ref _ownerPlacement);
        if (_wechat != 0 && _instance is not null)
        {
            try { using var process = Resolve(_instance); if (process.MainWindowHandle == _wechat) SetWindowPlacement(_wechat, ref _wechatPlacement); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
        _owner = _wechat = 0; _instance = null;
    }
    private static Process Resolve(string instance)
    {
        var parts = instance.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var pid) || !long.TryParse(parts[1], out var ticks)) throw new ArgumentException("微信窗口标识无效。");
        var process = Process.GetProcessById(pid);
        try
        {
            if (process.SessionId != Process.GetCurrentProcess().SessionId || process.StartTime.ToUniversalTime().Ticks != ticks ||
                !(process.ProcessName.Equals("Weixin", StringComparison.OrdinalIgnoreCase) || process.ProcessName.Equals("WeChat", StringComparison.OrdinalIgnoreCase)) || process.MainWindowHandle == 0)
                throw new InvalidOperationException("微信窗口已变化，请刷新后重新选择。");
            return process;
        }
        catch { process.Dispose(); throw; }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Placement { public int Length, Flags, ShowCmd; public Point Min, Max; public Rect Normal; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(nint window, ref Placement placement);
    [DllImport("user32.dll")] private static extern bool SetWindowPlacement(nint window, ref Placement placement);
    [DllImport("user32.dll")] private static extern bool MoveWindow(nint window, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
#else
    public void Arrange(Window window, string instance) => throw new InvalidOperationException("窗口伴随仅适用于本机 Windows 客户端。");
    public void Dispose() { }
#endif
}
