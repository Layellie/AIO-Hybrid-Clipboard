using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using static AIO_Hybrid_Clipboard.Services.Win32Api;

namespace AIO_Hybrid_Clipboard.Services
{
    internal readonly record struct HotkeyRegistrationResult(bool ToggleRegistered, bool QuickPasteRegistered)
    {
        public bool AllSucceeded => ToggleRegistered && QuickPasteRegistered;
    }

    internal sealed class HotkeyManager : IDisposable
    {
        private const int HOTKEY_ID       = 9000;
        private const int QUICKPASTE_ID_1 = 9001;
        private const int QUICKPASTE_ID_2 = 9002;
        private const int QUICKPASTE_ID_3 = 9003;

        private IntPtr _hwnd;

        public uint CurrentModifier   { get; set; } = 0x0001; // ALT
        public uint CurrentKey        { get; set; } = 0x20;   // SPACE
        public bool QuickPasteEnabled { get; set; } = true;

        public event Action?      ToggleRequested;
        public event Action<int>? QuickPasteRequested;

        internal void Attach(IntPtr hwnd) => _hwnd = hwnd;

        // MOD_NOREPEAT (0x4000): suppress auto-repeat WM_HOTKEY messages while the
        // combo is held down, so a single press triggers a single action.
        private const uint MOD_NOREPEAT = 0x4000;

        public HotkeyRegistrationResult Register()
        {
            if (_hwnd == IntPtr.Zero)
                return new HotkeyRegistrationResult(false, false);

            uint mod = CurrentModifier | MOD_NOREPEAT;

            UnregisterHotKey(_hwnd, HOTKEY_ID);
            bool toggleOk = RegisterHotKey(_hwnd, HOTKEY_ID, mod, CurrentKey);
            if (!toggleOk)
                Log.Warn("RegisterHotKey failed for toggle hotkey — combo may be in use by another app.");

            UnregisterQuickPaste();

            bool quickOk = true;
            if (QuickPasteEnabled)
            {
                bool q1 = RegisterHotKey(_hwnd, QUICKPASTE_ID_1, mod, 0x31);
                bool q2 = RegisterHotKey(_hwnd, QUICKPASTE_ID_2, mod, 0x32);
                bool q3 = RegisterHotKey(_hwnd, QUICKPASTE_ID_3, mod, 0x33);
                quickOk = q1 && q2 && q3;
                if (!quickOk)
                    Log.Warn("RegisterHotKey failed for one or more quick-paste hotkeys.");
            }

            return new HotkeyRegistrationResult(toggleOk, quickOk);
        }

        public void Unregister()
        {
            if (_hwnd == IntPtr.Zero) return;
            UnregisterHotKey(_hwnd, HOTKEY_ID);
            UnregisterQuickPaste();
        }

        private void UnregisterQuickPaste()
        {
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
        private const ushort VK_LSHIFT       = 0xA0;
        private const ushort VK_RSHIFT       = 0xA1;
        private const ushort VK_LCONTROL     = 0xA2;
        private const ushort VK_RCONTROL     = 0xA3;
        private const ushort VK_LMENU        = 0xA4;
        private const ushort VK_RMENU        = 0xA5;
        private const ushort VK_LWIN         = 0x5B;
        private const ushort VK_RWIN         = 0x5C;
        private const ushort VK_V            = 0x56;

        /// <summary>
        /// Releases any held modifier keys (generic + left/right) and dispatches a clean Ctrl+V.
        /// </summary>
        public static void SimulatePaste()
        {
            int inputSize = Marshal.SizeOf<INPUT>();

            var release = new[]
            {
                KeyInput(VK_MENU,     KEYEVENTF_KEYUP),
                KeyInput(VK_LMENU,    KEYEVENTF_KEYUP),
                KeyInput(VK_RMENU,    KEYEVENTF_KEYUP),
                KeyInput(VK_CONTROL,  KEYEVENTF_KEYUP),
                KeyInput(VK_LCONTROL, KEYEVENTF_KEYUP),
                KeyInput(VK_RCONTROL, KEYEVENTF_KEYUP),
                KeyInput(VK_SHIFT,    KEYEVENTF_KEYUP),
                KeyInput(VK_LSHIFT,   KEYEVENTF_KEYUP),
                KeyInput(VK_RSHIFT,   KEYEVENTF_KEYUP),
                KeyInput(VK_LWIN,     KEYEVENTF_KEYUP),
                KeyInput(VK_RWIN,     KEYEVENTF_KEYUP),
            };
            SendInput((uint)release.Length, release, inputSize);

            var paste = new[]
            {
                KeyInput(VK_CONTROL, 0),
                KeyInput(VK_V,       0),
                KeyInput(VK_V,       KEYEVENTF_KEYUP),
                KeyInput(VK_CONTROL, KEYEVENTF_KEYUP),
            };
            SendInput((uint)paste.Length, paste, inputSize);
        }

        /// <summary>
        /// Waits briefly for window focus transfer and then injects Ctrl+V into the foreground app.
        /// </summary>
        public static async Task SimulatePasteAsync(int delayMs = 65)
        {
            if (delayMs > 0)
                await Task.Delay(delayMs);
            SimulatePaste();
        }

        private static INPUT KeyInput(ushort vk, uint flags) => new()
        {
            type = 1, // INPUT_KEYBOARD
            U    = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = flags } }
        };

        public void Dispose() => Unregister();
    }
}
