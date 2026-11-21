using System;
using System.IO;
using System.Runtime.InteropServices;
using static AIO_Hybrid_Clipboard.Services.Win32Api;

namespace AIO_Hybrid_Clipboard.Services
{
    /// <summary>
    /// Manages the Windows system tray icon with deterministic HICON lifetime tracking
    /// (never calls DestroyIcon on shared system fallback icons) and Explorer.exe restart recovery.
    /// </summary>
    internal sealed class TrayIconService : IDisposable
    {
        private NOTIFYICONDATA _nid;
        private readonly IntPtr _hwnd;
        private bool   _initialized;
        private bool   _ownsIcon;
        private string _currentTip = "AIO Hybrid Clipboard";

        public event Action? OpenRequested;
        public event Action? ContextMenuRequested;

        public TrayIconService(IntPtr hwnd) => _hwnd = hwnd;

        public void Initialize()
        {
            if (_initialized || _hwnd == IntPtr.Zero) return;

            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_icon.ico");
            IntPtr hIcon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
            if (hIcon != IntPtr.Zero)
            {
                _ownsIcon = true;
            }
            else
            {
                // System shared icon (IDI_APPLICATION): must NOT be destroyed via DestroyIcon.
                hIcon = LoadIcon(IntPtr.Zero, new IntPtr(32512));
                _ownsIcon = false;
            }

            _nid = new NOTIFYICONDATA
            {
                cbSize           = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd             = _hwnd,
                uID              = 1,
                uFlags           = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_TRAYICON,
                hIcon            = hIcon,
                szTip            = _currentTip
            };

            Shell_NotifyIcon(NIM_ADD, ref _nid);
            _initialized = true;
        }

        /// <summary>
        /// Re-registers the tray icon after Explorer.exe restarts (TaskbarCreated) or system resume,
        /// reusing the existing HICON handle without leaking GDI resources.
        /// </summary>
        public void ReRegister()
        {
            if (!_initialized || _hwnd == IntPtr.Zero) return;

            _nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
            _nid.szTip  = _currentTip;
            Shell_NotifyIcon(NIM_ADD, ref _nid);
        }

        public void UpdateTooltip(string tip)
        {
            _currentTip = tip.Length > 127 ? tip[..127] : tip;
            if (!_initialized) return;

            _nid.uFlags = NIF_TIP;
            _nid.szTip  = _currentTip;
            Shell_NotifyIcon(NIM_MODIFY, ref _nid);
        }

        public void HandleTrayMessage(int clickType)
        {
            if (clickType == WM_RBUTTONUP)      ContextMenuRequested?.Invoke();
            else if (clickType == WM_LBUTTONUP) OpenRequested?.Invoke();
        }

        public void Dispose()
        {
            if (_initialized)
            {
                Shell_NotifyIcon(NIM_DELETE, ref _nid);
                _initialized = false;
            }

            if (_ownsIcon && _nid.hIcon != IntPtr.Zero)
                DestroyIcon(_nid.hIcon);

            _nid.hIcon = IntPtr.Zero;
            _ownsIcon  = false;
        }
    }
}
