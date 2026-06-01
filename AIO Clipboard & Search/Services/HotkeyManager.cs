using System;
using System.Runtime.InteropServices;
using static AIO_Hybrid_Clipboard.Services.Win32Api;

namespace AIO_Hybrid_Clipboard.Services
{
    internal sealed class HotkeyManager : IDisposable
    {
        private const int HOTKEY_ID       = 9000;
        private const int QUICKPASTE_ID_1 = 9001;
        private const int QUICKPASTE_ID_2 = 9002;
        private const int QUICKPASTE_ID_3 = 9003;

        private IntPtr _hwnd;

        public uint CurrentModifier { get; set; } = 0x0001;
        public uint CurrentKey      { get; set; } = 0x20;

        public event Action?      ToggleRequested;
        public event Action<int>? QuickPasteRequested;

        internal void Attach(IntPtr hwnd) => _hwnd = hwnd;

        // MOD_NOREPEAT (0x4000): suppress auto-repeat WM_HOTKEY messages while the
        // combo is held down, so a single press triggers a single action.
        private const uint MOD_NOREPEAT = 0x4000;

        public void Register()
        {
            uint mod = CurrentModifier | MOD_NOREPEAT;

            UnregisterHotKey(_hwnd, HOTKEY_ID);
            RegisterHotKey(_hwnd, HOTKEY_ID, mod, CurrentKey);

            UnregisterHotKey(_hwnd, QUICKPASTE_ID_1);
            UnregisterHotKey(_hwnd, QUICKPASTE_ID_2);
            UnregisterHotKey(_hwnd, QUICKPASTE_ID_3);
            RegisterHotKey(_hwnd, QUICKPASTE_ID_1, mod, 0x31);
            RegisterHotKey(_hwnd, QUICKPASTE_ID_2, mod, 0x32);
            RegisterHotKey(_hwnd, QUICKPASTE_ID_3, mod, 0x33);
        }

        public void Unregister()
        {
            UnregisterHotKey(_hwnd, HOTKEY_ID);
            UnregisterHotKey(_hwnd, QUICKPASTE_ID_1);
            UnregisterHotKey(_hwnd, QUICKPASTE_ID_2);
            UnregisterHotKey(_hwnd, QUICKPASTE_ID_3);
        }

        public bool HandleMessage(int hotkeyId)
        {
            if (hotkeyId == HOTKEY_ID)       { ToggleRequested?.Invoke();       return true; }
            if (hotkeyId == QUICKPASTE_ID_1) { QuickPasteRequested?.Invoke(0); return true; }
            if (hotkeyId == QUICKPASTE_ID_2) { QuickPasteRequested?.Invoke(1); return true; }
            if (hotkeyId == QUICKPASTE_ID_3) { QuickPasteRequested?.Invoke(2); return true; }
            return false;
        }

        private const uint   KEYEVENTF_KEYUP = 0x0002;
        private const ushort VK_SHIFT        = 0x10;
        private const ushort VK_CONTROL      = 0x11;
        private const ushort VK_MENU         = 0x12; // ALT
        private const ushort VK_LWIN         = 0x5B;
        private const ushort VK_RWIN         = 0x5C;
        private const ushort VK_V            = 0x56;

        public static void SimulatePaste()
        {
            // The quick-paste hotkey (e.g. ALT+1) fires while the user is still
            // physically holding the modifier keys. If we send CTRL+V directly the
            // target window receives ALT+CTRL+V and nothing pastes. Release any held
            // modifiers first so the target gets a clean CTRL+V.
            var release = new[]
            {
                KeyInput(VK_MENU,    KEYEVENTF_KEYUP),
                KeyInput(VK_CONTROL, KEYEVENTF_KEYUP),
                KeyInput(VK_SHIFT,   KEYEVENTF_KEYUP),
                KeyInput(VK_LWIN,    KEYEVENTF_KEYUP),
                KeyInput(VK_RWIN,    KEYEVENTF_KEYUP),
            };
            SendInput((uint)release.Length, release, Marshal.SizeOf<INPUT>());

            var paste = new[]
            {
                KeyInput(VK_CONTROL, 0),
                KeyInput(VK_V,       0),
                KeyInput(VK_V,       KEYEVENTF_KEYUP),
                KeyInput(VK_CONTROL, KEYEVENTF_KEYUP),
            };
            SendInput((uint)paste.Length, paste, Marshal.SizeOf<INPUT>());
        }

        private static INPUT KeyInput(ushort vk, uint flags) => new()
        {
            type = 1, // INPUT_KEYBOARD
            U    = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = flags } }
        };

        public void Dispose() => Unregister();
    }
}
