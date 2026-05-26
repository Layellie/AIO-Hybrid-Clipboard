using Microsoft.Win32;
using System;
using System.Collections.Generic;

namespace AIO_Hybrid_Clipboard.Services
{
    internal sealed class SettingsService
    {
        private const string RegistryKey = @"SOFTWARE\AIO_ClipboardSearch";

        public enum AppLanguage { English, Turkish }

        public AppLanguage CurrentLanguage { get; set; } = AppLanguage.English;

        private static readonly Dictionary<AppLanguage, Dictionary<string, string>> Strings = new()
        {
            [AppLanguage.English] = new()
            {
                ["HideOnCopy"]       = "Automatically hide menu on new selection",
                ["StartWithWindows"] = "Start with Windows",
                ["ShortcutKey"]      = "Shortcut Key: ",
                ["Language"]         = "Language: ",
                ["TrayOpen"]         = "Open",
                ["TrayExit"]         = "Exit",
                ["InitialMsg"]       = "AIO OCR Clipboard Active! Capture images to process... 🚀",
                ["OcrError"]         = "OCR Error",
                ["OcrException"]     = "OCR Processing Exception: ",
            },
            [AppLanguage.Turkish] = new()
            {
                ["HideOnCopy"]       = "Seçimde menüyü otomatik gizle",
                ["StartWithWindows"] = "Windows ile başlat",
                ["ShortcutKey"]      = "Kısayol Tuşu: ",
                ["Language"]         = "Dil: ",
                ["TrayOpen"]         = "Aç",
                ["TrayExit"]         = "Çıkış",
                ["InitialMsg"]       = "AIO OCR Pano Aktif! İşlemek için görüntü yakalayın... 🚀",
                ["OcrError"]         = "OCR Hatası",
                ["OcrException"]     = "OCR İşleme Hatası: ",
            }
        };

        public string T(string key) =>
            Strings[CurrentLanguage].TryGetValue(key, out var val) ? val : key;

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
            catch { }
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
            catch { }
        }
    }
}
