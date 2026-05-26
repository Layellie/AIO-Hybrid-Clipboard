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

        public void Register()
        {
            UnregisterHotKey(_hwnd, HOTKEY_ID);
            RegisterHotKey(_hwnd, HOTKEY_ID, CurrentModifier, CurrentKey);

            UnregisterHotKey(_hwnd, QUICKPASTE_ID_1);
            UnregisterHotKey(_hwnd, QUICKPASTE_ID_2);
            UnregisterHotKey(_hwnd, QUICKPASTE_ID_3);
            RegisterHotKey(_hwnd, QUICKPASTE_ID_1, CurrentModifier, 0x31);
            RegisterHotKey(_hwnd, QUICKPASTE_ID_2, CurrentModifier, 0x32);
            RegisterHotKey(_hwnd, QUICKPASTE_ID_3, CurrentModifier, 0x33);
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

        public static void SimulatePaste()
        {
            const uint   INPUT_KEYBOARD  = 1;
            const uint   KEYEVENTF_KEYUP = 0x0002;
            const ushort VK_CONTROL      = 0x11;
            const ushort VK_V            = 0x56;
            var inputs = new INPUT[]
            {
                new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = VK_CONTROL } } },
                new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = VK_V } } },
                new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = VK_V,       dwFlags = KEYEVENTF_KEYUP } } },
                new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = VK_CONTROL, dwFlags = KEYEVENTF_KEYUP } } },
            };
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        public void Dispose() => Unregister();
    }
}
