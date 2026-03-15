using System;
using System.Runtime.InteropServices;

namespace GenieClient
{

    /// <summary>
/// Necessary native methods to import
/// </summary>
    internal static class NativeMethods
    {
        static NativeMethods()
        {
        }

#if WINDOWS
        [DllImport("user32.dll", CallingConvention = CallingConvention.Winapi)]
        public static extern bool FlashWindow(IntPtr hwnd, bool bInvert);
#else
        public static bool FlashWindow(IntPtr hwnd, bool bInvert) => false;
#endif
    }
}
