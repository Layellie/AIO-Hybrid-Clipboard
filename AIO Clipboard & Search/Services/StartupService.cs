using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Reflection;

namespace AIO_Hybrid_Clipboard.Services
{
    internal static class StartupService
    {
        private const string RunKey    = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "AIO_ClipboardSearch";

        public static bool IsEnabled()
        {
            try
            {
                using var rk = Registry.CurrentUser.OpenSubKey(RunKey, false);
                return rk?.GetValue(ValueName) != null;
            }
            catch (Exception ex) { Debug.WriteLine($"[AIO] Startup read failed: {ex.Message}"); return false; }
        }

        public static void Set(bool enable)
        {
            try
            {
                using var rk = Registry.CurrentUser.OpenSubKey(RunKey, true);
                if (rk == null) return;
                if (enable) rk.SetValue(ValueName, Environment.ProcessPath ?? Assembly.GetExecutingAssembly().Location);
                else rk.DeleteValue(ValueName, false);
            }
            catch (Exception ex) { Debug.WriteLine($"[AIO] Startup write failed: {ex.Message}"); }
        }
    }
}
