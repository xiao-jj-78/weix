using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace WeChatAuto.Utils
{
    /// <summary>
    /// Windows剪贴板工具类
    /// </summary>
    public static class ClipboardHelper
    {
        /// <summary>
        /// 将文字放入剪切板.
        /// </summary>
        /// <param name="text"></param>
        public static bool SetText(string text)
        {
            Exception ex = null;

            var thread = new Thread(() =>
            {
                try
                {
                    // throw new Exception("测试");
                    System.Windows.Clipboard.SetText(text);
                }
                catch (Exception e)
                {
                    ex = e;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);

            thread.Start();
            thread.Join();

            if (ex != null)
                return false;
            return true;
        }
    }
}