using Microsoft.Win32;
using System;
using System.Globalization;
using System.Resources;

namespace AIO_Hybrid_Clipboard.Services
{
    internal sealed class SettingsService
    {
        private const string DefaultRegistryKey = @"SOFTWARE\AIO_ClipboardSearch";

        public enum AppLanguage { English, Turkish }

        internal static readonly int[] AllowedRetentionDays = { 0, 7, 14, 30, 90 };

        private readonly string _registryKey;

        public SettingsService(string registryKey = DefaultRegistryKey)
        {
            _registryKey = registryKey;
        }

        public AppLanguage CurrentLanguage   { get; set; } = AppLanguage.English;
        public bool        HideOnCopy        { get; set; } = true;
        public bool        AutoPaste         { get; set; } = false;
        public bool        StartMinimized    { get; set; } = false;
        public bool        AutoOcr           { get; set; } = true;
        public bool        QuickPasteEnabled { get; set; } = true;
        public int         HistoryLimit      { get; set; } = HistoryService.Limit;
        public string      OcrLanguage       { get; set; } = OcrService.AutoLanguageTag;
        public string      ExcludedApps      { get; set; } = string.Empty;
        public bool        EncryptSession    { get; set; } = false;
        public int         RetentionDays     { get; set; } = 0;

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
            modifier = 0x0001; // ALT
            key      = 0x20;   // SPACE
            try
            {
                using var rk = Registry.CurrentUser.OpenSubKey(_registryKey);
                if (rk == null) return;

                if (rk.GetValue("Language") is string lang && Enum.TryParse<AppLanguage>(lang, out var parsed))
                    CurrentLanguage = parsed;
                if (rk.GetValue("HotkeyModifier") is int mod) modifier = (uint)mod;
                if (rk.GetValue("HotkeyKey")      is int k)   key      = (uint)k;

                HideOnCopy        = ReadBool(rk, "HideOnCopy",        defaultValue: true);
                AutoPaste         = ReadBool(rk, "AutoPaste",         defaultValue: false);
                StartMinimized    = ReadBool(rk, "StartMinimized",    defaultValue: false);
                AutoOcr           = ReadBool(rk, "AutoOcr",           defaultValue: true);
                QuickPasteEnabled = ReadBool(rk, "QuickPasteEnabled", defaultValue: true);
                EncryptSession    = ReadBool(rk, "EncryptSession",    defaultValue: false);

                if (rk.GetValue("HistoryLimit") is int limit)
                    HistoryLimit = HistoryService.ClampLimit(limit);

                if (rk.GetValue("OcrLanguage") is string ocrLang && !string.IsNullOrWhiteSpace(ocrLang))
                    OcrLanguage = ocrLang.Trim();

                if (rk.GetValue("ExcludedApps") is string excluded)
                    ExcludedApps = excluded;

                if (rk.GetValue("RetentionDays") is int days && days >= 0)
                    RetentionDays = days;
            }
            catch (Exception ex) { Log.Error("Settings load failed", ex); }
        }

        public void Save(uint modifier, uint key)
        {
            try
            {
                using var rk = Registry.CurrentUser.CreateSubKey(_registryKey);
                if (rk == null) return;

                rk.SetValue("Language",          CurrentLanguage.ToString());
                rk.SetValue("HotkeyModifier",    (int)modifier,                           RegistryValueKind.DWord);
                rk.SetValue("HotkeyKey",         (int)key,                                RegistryValueKind.DWord);
                rk.SetValue("HideOnCopy",        HideOnCopy        ? 1 : 0,               RegistryValueKind.DWord);
                rk.SetValue("AutoPaste",         AutoPaste         ? 1 : 0,               RegistryValueKind.DWord);
                rk.SetValue("StartMinimized",    StartMinimized    ? 1 : 0,               RegistryValueKind.DWord);
                rk.SetValue("AutoOcr",           AutoOcr           ? 1 : 0,               RegistryValueKind.DWord);
                rk.SetValue("QuickPasteEnabled", QuickPasteEnabled ? 1 : 0,               RegistryValueKind.DWord);
                rk.SetValue("EncryptSession",    EncryptSession    ? 1 : 0,               RegistryValueKind.DWord);
                rk.SetValue("HistoryLimit",      HistoryService.ClampLimit(HistoryLimit), RegistryValueKind.DWord);
                rk.SetValue("OcrLanguage",       OcrLanguage ?? OcrService.AutoLanguageTag);
                rk.SetValue("ExcludedApps",      ExcludedApps ?? string.Empty);
                rk.SetValue("RetentionDays",     Math.Max(0, RetentionDays),              RegistryValueKind.DWord);
            }
            catch (Exception ex) { Log.Error("Settings save failed", ex); }
        }

        private static bool ReadBool(RegistryKey rk, string name, bool defaultValue) =>
            rk.GetValue(name) is int v ? v != 0 : defaultValue;
    }
}
