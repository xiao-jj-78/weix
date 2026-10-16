using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WeChatAuto.Utils
{

    public static class WindowsInputHelper
    {
        // 英语（美国）键盘布局
        private const string EnglishKeyboardLayout = "00000409";

        // WM_INPUTLANGCHANGEREQUEST
        private const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;

        // KLF_ACTIVATE
        private const uint KLF_ACTIVATE = 0x00000001;

        // ------------------------------------------------------------
        // Win32
        // ------------------------------------------------------------

        [StructLayout(LayoutKind.Sequential)]
        private struct GUITHREADINFO
        {
            public uint cbSize;
            public uint flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
        }

        [DllImport("user32.dll")]
        private static extern bool GetGUIThreadInfo(
            uint idThread,
            ref GUITHREADINFO lpguithreadinfo);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(
            IntPtr hWnd,
            out uint processId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadKeyboardLayout(
            string pwszKLID,
            uint Flags);

        [DllImport("user32.dll")]
        private static extern IntPtr ActivateKeyboardLayout(
            IntPtr hkl,
            uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetKeyboardLayout(
            uint idThread);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(
            IntPtr hWnd,
            uint msg,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(
            EnumWindowsProc lpEnumFunc,
            IntPtr lParam);

        private delegate bool EnumWindowsProc(
            IntPtr hWnd,
            IntPtr lParam);

        // ------------------------------------------------------------
        // 查找指定进程的顶层窗口
        // ------------------------------------------------------------

        private static IntPtr FindProcessWindow(uint processId)
        {
            IntPtr result = IntPtr.Zero;

            EnumWindows((hWnd, lParam) =>
            {
                GetWindowThreadProcessId(hWnd, out uint pid);

                if (pid == processId)
                {
                    result = hWnd;
                    return false;
                }

                return true;
            }, IntPtr.Zero);

            return result;
        }

        // ------------------------------------------------------------
        // 获取目标线程当前真正的键盘焦点窗口
        // ------------------------------------------------------------

        private static IntPtr GetFocusedWindow(uint threadId)
        {
            var info = new GUITHREADINFO
            {
                cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>()
            };

            if (!GetGUIThreadInfo(threadId, ref info))
                return IntPtr.Zero;

            return info.hwndFocus;
        }

        // ------------------------------------------------------------
        // 解析目标进程的 UI 线程与当前键盘焦点窗口
        // ------------------------------------------------------------

        private static bool TryResolveFocusWindow(
            uint processId,
            out uint threadId,
            out IntPtr focusWindow)
        {
            threadId = 0;
            focusWindow = IntPtr.Zero;

            // 1. 找到目标进程窗口
            IntPtr processWindow = FindProcessWindow(processId);
            if (processWindow == IntPtr.Zero)
                return false;

            // 2. 获取目标窗口所属 UI 线程
            threadId = GetWindowThreadProcessId(
                processWindow,
                out uint actualProcessId);

            if (threadId == 0 || actualProcessId != processId)
                return false;

            // 3. 获取这个线程当前真正的键盘焦点窗口
            // 某些程序可能暂时没有 hwndFocus，这种情况下退回到顶层窗口
            focusWindow = GetFocusedWindow(threadId);
            if (focusWindow == IntPtr.Zero)
                focusWindow = processWindow;

            return IsWindow(focusWindow);
        }

        // ------------------------------------------------------------
        // 强制目标进程切换到英文键盘布局
        // ------------------------------------------------------------

        public static bool ForceEnglishInput(uint processId)
        {
            if (!TryResolveFocusWindow(processId, out _, out IntPtr focusWindow))
                return false;

            // 4. 加载英文键盘布局
            IntPtr hkl = LoadKeyboardLayout(
                EnglishKeyboardLayout,
                KLF_ACTIVATE);

            if (hkl == IntPtr.Zero)
                return false;

            // 5. 向目标窗口发送“切换输入语言”请求
            //
            // lParam = HKL
            //
            return PostMessage(
                focusWindow,
                WM_INPUTLANGCHANGEREQUEST,
                IntPtr.Zero,
                hkl);
        }

        // ------------------------------------------------------------
        // 保存当前布局 → 切换英文 → 结束后恢复原布局
        // ------------------------------------------------------------

        /// <summary>
        /// 先记录目标线程当前的键盘布局，再切换到英文，并返回一个作用域对象。
        /// 用 using 包裹自动化动作，作用域结束（含异常退出）时自动恢复原布局。
        /// 切换失败时返回 <see cref="KeyboardLayoutScope.Empty"/>（无操作）。
        /// </summary>
        public static KeyboardLayoutScope SwitchToEnglish(uint processId)
        {
            if (!TryResolveFocusWindow(processId, out uint threadId, out IntPtr focusWindow))
                return KeyboardLayoutScope.Empty;

            // 保存目标线程当前的键盘布局
            // 注意：跨进程调用 GetKeyboardLayout 通常可用，若返回 0 则恢复时按无操作处理
            IntPtr originalHkl = GetKeyboardLayout(threadId);

            IntPtr englishHkl = LoadKeyboardLayout(
                EnglishKeyboardLayout,
                KLF_ACTIVATE);

            if (englishHkl == IntPtr.Zero)
                return KeyboardLayoutScope.Empty;

            PostMessage(
                focusWindow,
                WM_INPUTLANGCHANGEREQUEST,
                IntPtr.Zero,
                englishHkl);

            return new KeyboardLayoutScope(focusWindow, originalHkl);
        }

        public static KeyboardLayoutScope SwitchToEnglish(Process process)
        {
            if (process == null)
                throw new ArgumentNullException(nameof(process));

            return SwitchToEnglish((uint)process.Id);
        }

        /// <summary>
        /// 键盘布局作用域：Dispose 时把之前保存的布局发回目标焦点窗口，实现“用后恢复”。
        /// </summary>
        public sealed class KeyboardLayoutScope : IDisposable
        {
            /// <summary>切换失败时的无操作作用域，用于保持 using 结构统一。</summary>
            public static KeyboardLayoutScope Empty { get; } = new KeyboardLayoutScope(IntPtr.Zero, IntPtr.Zero);

            private readonly IntPtr _focusWindow;
            private readonly IntPtr _originalHkl;
            private bool _disposed;

            internal KeyboardLayoutScope(IntPtr focusWindow, IntPtr originalHkl)
            {
                _focusWindow = focusWindow;
                _originalHkl = originalHkl;
            }

            /// <summary>立即恢复原来的键盘布局（幂等，可重复调用）。</summary>
            public void Restore()
            {
                if (_disposed)
                    return;
                _disposed = true;

                if (_focusWindow == IntPtr.Zero || _originalHkl == IntPtr.Zero)
                    return;

                if (IsWindow(_focusWindow))
                {
                    PostMessage(
                        _focusWindow,
                        WM_INPUTLANGCHANGEREQUEST,
                        IntPtr.Zero,
                        _originalHkl);
                }
            }

            public void Dispose()
            {
                Restore();
            }
        }


        public static bool ForceEnglishInput(Process process)
        {
            if (process == null)
                throw new ArgumentNullException(nameof(process));

            return ForceEnglishInput((uint)process.Id);
        }
    }
}