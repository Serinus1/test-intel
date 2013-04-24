using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace TestIntelReporter {
    internal static class NativeMethods {
        internal static readonly IntPtr HWND_BROADCAST = (IntPtr)0xFFFFU;

        [DllImport("user32", SetLastError = true)]
        internal extern static int PostMessage([In] IntPtr hWnd, [In] uint uMsg,
            [In] IntPtr wParam, [In] IntPtr lParam);

        [DllImport("user32", SetLastError = true)]
        internal extern static uint RegisterWindowMessage([In] string lpString);
    }
}
