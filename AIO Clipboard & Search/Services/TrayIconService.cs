using System;
using System.IO;
using System.Runtime.InteropServices;
using static AIO_Hybrid_Clipboard.Services.Win32Api;

namespace AIO_Hybrid_Clipboard.Services
{
    internal sealed class TrayIconService : IDisposable
    {
        private NOTIFYICONDATA _nid;
        private readonly IntPtr _hwnd;

        public event Action? OpenRequested;
        public event Action? ContextMenuRequested;

        public TrayIconService(IntPtr hwnd) => _hwnd = hwnd;

        public void Initialize()
        {
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_icon.ico");
            IntPtr hIcon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
            if (hIcon == IntPtr.Zero) hIcon = LoadIcon(IntPtr.Zero, new IntPtr(32512));

            _nid = new NOTIFYICONDATA
            {
                cbSize           = Marshal.SizeOf(typeof(NOTIFYICONDATA)),
                hWnd             = _hwnd,
                uID              = 1,
                uFlags           = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_TRAYICON,
                hIcon            = hIcon,
                szTip            = "AIO Hybrid Clipboard"
            };
            Shell_NotifyIcon(NIM_ADD, ref _nid);
        }

        public void HandleTrayMessage(int clickType)
        {
            if (clickType == WM_RBUTTONUP)      ContextMenuRequested?.Invoke();
            else if (clickType == WM_LBUTTONUP) OpenRequested?.Invoke();
        }

        public void Dispose()
        {
            Shell_NotifyIcon(NIM_DELETE, ref _nid);
            if (_nid.hIcon != IntPtr.Zero) DestroyIcon(_nid.hIcon);
        }
    }
}
