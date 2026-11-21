using Microsoft.Win32;
using System;
using System.Reflection;

namespace AIO_Hybrid_Clipboard.Services
{
    internal static class StartupService
    {
        private const string RunKey    = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "AIO_ClipboardSearch";

        internal const string MinimizedArg = "--minimized";

        /// <summary>Formats a properly quoted Run registry command line with the minimized flag.</summary>
        internal static string FormatStartupCommand(string exePath)
        {
            string clean = (exePath ?? string.Empty).Trim().Trim('"');
            return $"\"{clean}\" {MinimizedArg}";
        }

        public static bool IsEnabled()
        {
            try
            {
                using var rk = Registry.CurrentUser.OpenSubKey(RunKey, false);
                return rk?.GetValue(ValueName) != null;
            }
            catch (Exception ex) { Log.Warn($"Startup read failed: {ex.Message}"); return false; }
        }

        public static void Set(bool enable)
        {
            try
            {
                using var rk = Registry.CurrentUser.OpenSubKey(RunKey, true);
                if (rk == null) return;

                if (enable)
                {
                    string exePath = Environment.ProcessPath ?? Assembly.GetExecutingAssembly().Location;
                    rk.SetValue(ValueName, FormatStartupCommand(exePath));
                }
                else
                {
                    rk.DeleteValue(ValueName, false);
                }
            }
            catch (Exception ex) { Log.Warn($"Startup write failed: {ex.Message}"); }
        }
    }
}
