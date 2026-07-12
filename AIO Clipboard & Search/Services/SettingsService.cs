using Microsoft.Win32;
using System;
using System.Globalization;
using System.Resources;

namespace AIO_Hybrid_Clipboard.Services
{
    internal sealed class SettingsService
    {
        private const string RegistryKey = @"SOFTWARE\AIO_ClipboardSearch";

        public enum AppLanguage { English, Turkish }

        public AppLanguage CurrentLanguage { get; set; } = AppLanguage.English;

        private static readonly ResourceManager Strings =
            new("AIO_Hybrid_Clipboard.Resources.Strings", typeof(SettingsService).Assembly);

        private CultureInfo Culture => CurrentLanguage == AppLanguage.Turkish
            ? CultureInfo.GetCultureInfo("tr")
            : CultureInfo.InvariantCulture;

        /// <summary>Returns the localized string for the key, or the key itself when missing.</summary>
        public string T(string key)
        {
            try { return Strings.GetString(key, Culture) ?? key; }
            catch (MissingManifestResourceException) { return key; }
        }

        public void Load(out uint modifier, out uint key)
        {
            modifier = 0x0001;
            key      = 0x20;
            try
            {
                using var rk = Registry.CurrentUser.OpenSubKey(RegistryKey);
                if (rk == null) return;
                if (rk.GetValue("Language") is string lang && Enum.TryParse<AppLanguage>(lang, out var parsed))
                    CurrentLanguage = parsed;
                if (rk.GetValue("HotkeyModifier") is int mod) modifier = (uint)mod;
                if (rk.GetValue("HotkeyKey")      is int k)   key      = (uint)k;
            }
            catch (Exception ex) { Log.Error("Settings load failed", ex); }
        }

        public void Save(uint modifier, uint key)
        {
            try
            {
                using var rk = Registry.CurrentUser.CreateSubKey(RegistryKey);
                rk?.SetValue("Language",       CurrentLanguage.ToString());
                rk?.SetValue("HotkeyModifier", (int)modifier, RegistryValueKind.DWord);
                rk?.SetValue("HotkeyKey",      (int)key,      RegistryValueKind.DWord);
            }
            catch (Exception ex) { Log.Error("Settings save failed", ex); }
        }
    }
}
