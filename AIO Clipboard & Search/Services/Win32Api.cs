using System;
using System.Runtime.InteropServices;

namespace AIO_Hybrid_Clipboard.Services
{
    internal static class Win32Api
    {
        // --- Tray icon ---
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        internal struct NOTIFYICONDATA
        {
            public int cbSize; public IntPtr hWnd; public int uID; public int uFlags;
            public int uCallbackMessage; public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        internal static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        internal static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        [DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern bool DestroyIcon(IntPtr hIcon);

        // --- SendInput ---
        [DllImport("user32.dll")]
        internal static extern uint SendInput(uint nInputs, [MarshalAs(UnmanagedType.LPArray), In] INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        internal struct INPUT { public uint type; public InputUnion U; }

        [StructLayout(LayoutKind.Explicit)]
        internal struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

        // --- Hotkey & Clipboard ---
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        // --- Constants ---
        internal const int  NIM_ADD          = 0x00000000;
        internal const int  NIM_DELETE       = 0x00000002;
        internal const int  NIF_MESSAGE      = 0x00000001;
        internal const int  NIF_ICON         = 0x00000002;
        internal const int  NIF_TIP          = 0x00000004;
        internal const uint IMAGE_ICON       = 1;
        internal const uint LR_LOADFROMFILE  = 0x00000010;
        internal const int  WM_TRAYICON      = 0x0400 + 100;
        internal const int  WM_LBUTTONUP     = 0x0202;
        internal const int  WM_RBUTTONUP     = 0x0205;
        internal const int  WM_HOTKEY        = 0x0312;
        internal const int  WM_CLIPBOARDUPDATE = 0x031D;
    }
}
