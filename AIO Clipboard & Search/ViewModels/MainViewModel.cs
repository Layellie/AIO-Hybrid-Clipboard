using AIO_Hybrid_Clipboard.Common;
using AIO_Hybrid_Clipboard.Models;
using AIO_Hybrid_Clipboard.Services;
using System;
using System.Collections;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace AIO_Hybrid_Clipboard.ViewModels
{
    /// <summary>
    /// Application state and behavior behind <c>MainWindow</c>: history collections
    /// (exposed as filtered views), search, pinning, copy-back, OCR, deletion,
    /// settings and updates. The window keeps only Win32 plumbing (HWND hooks,
    /// hotkeys, tray, drag &amp; drop) and forwards user gestures to the commands here.
    /// </summary>
    public sealed class MainViewModel : INotifyPropertyChanged
    {
        internal HistoryService  History  { get; } = new();
        internal SettingsService Settings { get; } = new();

        private readonly UpdateService _updater;
        private readonly IDialogService _dialogs;
        private ClipboardService? _clipboard;
        private uint _hotkeyModifier;
        private uint _hotkeyKey;

        public MainViewModel() : this(null, null) { }

        /// <summary>Test seam: inject a fake updater (canned HTTP) and dialog service.</summary>
        internal MainViewModel(UpdateService? updater, IDialogService? dialogs)
        {
            _updater = updater ?? new UpdateService();
            _dialogs = dialogs ?? new MessageBoxDialogService();

            Settings.Load(out _hotkeyModifier, out _hotkeyKey);
            Loc = new LocalizationProxy(Settings);

            var (texts, shots) = SessionStore.Load();
            foreach (var t in texts) History.Texts.Add(t);
            foreach (var s in shots) History.Screenshots.Add(s);
            if (History.Texts.Count == 0) History.Texts.Add(new ClipItem(Settings.T("InitialMsg")));

            // Drop cached PNGs that no restored screenshot points at anymore.
            SessionStore.CleanOrphanCache(History.Screenshots.Select(s => s.Path));

            TextsView = CollectionViewSource.GetDefaultView(History.Texts);
            TextsView.Filter = o => string.IsNullOrEmpty(_searchQuery) ||
                (o is ClipItem c && c.Text.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase));

            ScreenshotsView = CollectionViewSource.GetDefaultView(History.Screenshots);
            ScreenshotsView.Filter = o => string.IsNullOrEmpty(_searchQuery) ||
                (o is ScreenshotModel s &&
                 (s.Name.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase) ||
                  s.OcrText.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase)));

            _startWithWindows = StartupService.IsEnabled();

            CopyTextCommand            = new RelayCommand(p => { if (p is ClipItem item) CopyText(item.Text); });
            TogglePinTextCommand       = new RelayCommand(p => { if (p is ClipItem item) History.TogglePin(item); });
            TogglePinScreenshotCommand = new RelayCommand(p => { if (p is ScreenshotModel shot) History.TogglePin(shot); });
            RunOcrCommand              = new RelayCommand(async p => { if (p is ScreenshotModel shot) await RunOcrAsync(shot); });
            CheckUpdatesCommand        = new RelayCommand(async _ => await CheckForUpdatesAsync(), _ => !_updateCheckRunning);
        }

        // --- BINDABLE STATE ---

        public ICollectionView TextsView       { get; }
        public ICollectionView ScreenshotsView { get; }
        public LocalizationProxy Loc           { get; }

        public string VersionText => $"v{UpdateService.CurrentVersion}";

        private string _searchQuery = string.Empty;
        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (_searchQuery == value.Trim()) return;
                _searchQuery = value.Trim();
                OnPropertyChanged();
                TextsView.Refresh();
                ScreenshotsView.Refresh();
            }
        }

        private bool _isEditMode;
        public bool IsEditMode
        {
            get => _isEditMode;
            set { if (_isEditMode != value) { _isEditMode = value; OnPropertyChanged(); } }
        }

        private bool _hideOnCopy;
        public bool HideOnCopy
        {
            get => _hideOnCopy;
            set { if (_hideOnCopy != value) { _hideOnCopy = value; OnPropertyChanged(); } }
        }

        private bool _startWithWindows;
        public bool StartWithWindows
        {
            get => _startWithWindows;
            set
            {
                if (_startWithWindows == value) return;
                _startWithWindows = value;
                StartupService.Set(value);
                OnPropertyChanged();
            }
        }

        public int LanguageIndex
        {
            get => Settings.CurrentLanguage == SettingsService.AppLanguage.Turkish ? 1 : 0;
            set
            {
                var language = value == 1 ? SettingsService.AppLanguage.Turkish : SettingsService.AppLanguage.English;
                if (Settings.CurrentLanguage == language) return;
                Settings.CurrentLanguage = language;
                Settings.Save(_hotkeyModifier, _hotkeyKey);
                OnPropertyChanged();
                Loc.Refresh();
            }
        }

        private string _updateStatus = string.Empty;
        public string UpdateStatus
        {
            get => _updateStatus;
            private set { if (_updateStatus != value) { _updateStatus = value; OnPropertyChanged(); } }
        }

        // --- COMMANDS ---

        public ICommand CopyTextCommand            { get; }
        public ICommand TogglePinTextCommand       { get; }
        public ICommand TogglePinScreenshotCommand { get; }
        public ICommand RunOcrCommand              { get; }
        public ICommand CheckUpdatesCommand        { get; }

        /// <summary>Raised when the window should hide (copy-back with HideOnCopy on).</summary>
        public event Action? HideRequested;

        // --- HOTKEY PERSISTENCE (the view owns the Win32 registration) ---

        public uint HotkeyModifier => _hotkeyModifier;
        public uint HotkeyKey      => _hotkeyKey;

        internal void SaveHotkey(uint modifier, uint key)
        {
            _hotkeyModifier = modifier;
            _hotkeyKey      = key;
            Settings.Save(modifier, key);
        }

        // --- CLIPBOARD ---

        /// <summary>Wires the HWND-bound clipboard service created by the view.</summary>
        internal void AttachClipboard(ClipboardService clipboard)
        {
            _clipboard = clipboard;
            clipboard.TextCaptured  += text => History.InsertText(text);
            clipboard.ImageCaptured += OnImageCaptured;
        }

        private async void OnImageCaptured(ScreenshotModel model, byte[] pixels, int width, int height, int stride)
        {
            var evicted = History.AddScreenshot(model);
            if (evicted != null) TryDeleteFile(evicted.Path);

            try
            {
                string ocrResult = await OcrService.RecognizeAsync(pixels, width, height, stride);
                if (!string.IsNullOrEmpty(ocrResult))
                    model.OcrText = ocrResult;
            }
            catch (Exception ex) { Log.Error("Background OCR failed", ex); }
        }

        internal void CopyText(string? text)
        {
            if (IsEditMode) return;
            if (string.IsNullOrEmpty(text) || OcrService.IsPlaceholder(text)) return;

            try { _clipboard?.SetClipboardSilent(text); }
            catch (Exception ex) { Log.Warn($"SetClipboard failed: {ex.Message}"); }

            if (HideOnCopy) HideRequested?.Invoke();
        }

        internal void QuickPaste(int index)
        {
            if (History.Texts.Count <= index) return;
            string text = History.Texts[index].Text;
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                _clipboard?.SetClipboardSilent(text);
                HotkeyManager.SimulatePaste();
            }
            catch (Exception ex) { Log.Warn($"QuickPaste failed: {ex.Message}"); }
        }

        // --- OCR ---

        internal async Task RunOcrAsync(ScreenshotModel model)
        {
            if (IsEditMode || model.Image == null) return;

            int width  = model.Image.PixelWidth;
            int height = model.Image.PixelHeight;
            int stride = width * ((model.Image.Format.BitsPerPixel + 7) / 8);
            byte[] pixels = new byte[height * stride];
            model.Image.CopyPixels(pixels, stride, 0);

            try
            {
                string ocrResult = await OcrService.RecognizeAsync(pixels, width, height, stride);
                if (string.IsNullOrEmpty(ocrResult))
                {
                    model.OcrText = Settings.T("OcrNoText");
                    return;
                }

                model.OcrText = ocrResult;
                History.InsertText(ocrResult);
                try { _clipboard?.SetClipboardSilent(ocrResult); }
                catch (Exception ex) { Log.Warn($"SetClipboard failed: {ex.Message}"); }
            }
            catch (Exception ex)
            {
                Log.Error("On-demand OCR failed", ex);
                _dialogs.ShowError(Settings.T("OcrException") + ex.Message, Settings.T("OcrError"));
            }
        }

        // --- DELETE ---

        internal void DeleteItems(IList selectedTexts, IList selectedShots)
        {
            foreach (var item in selectedTexts.Cast<ClipItem>().ToList())
                History.Texts.Remove(item);

            foreach (var shot in selectedShots.Cast<ScreenshotModel>().ToList())
            {
                TryDeleteFile(shot.Path);
                History.Screenshots.Remove(shot);
            }
        }

        // --- UPDATES ---

        private bool _updateCheckRunning;

        /// <summary>Quiet startup check: surfaces a notice, never shows dialogs.</summary>
        public async Task CheckForUpdatesSilentAsync()
        {
            try
            {
                var info = await _updater.CheckLatestAsync();
                if (info != null && info.LatestVersion > UpdateService.CurrentVersion)
                    UpdateStatus = string.Format(Settings.T("UpdateAvailable"), info.LatestVersion);
            }
            catch (Exception ex) { Log.Warn($"Silent update check failed: {ex.Message}"); }
        }

        internal async Task CheckForUpdatesAsync()
        {
            _updateCheckRunning = true;
            UpdateStatus = Settings.T("UpdateChecking");
            try
            {
                var info = await _updater.CheckLatestAsync();
                if (info == null)
                {
                    UpdateStatus = Settings.T("UpdateFailed");
                }
                else if (info.LatestVersion <= UpdateService.CurrentVersion)
                {
                    UpdateStatus = Settings.T("UpdateUpToDate");
                }
                else
                {
                    UpdateStatus = string.Format(Settings.T("UpdateAvailable"), info.LatestVersion);

                    if (!_dialogs.Confirm(
                            string.Format(Settings.T("UpdatePrompt"), info.LatestVersion),
                            Settings.T("UpdateTitle")))
                        return;

                    if (info.InstallerUrl == null)
                    {
                        // Release has no installer asset — fall back to the releases page.
                        _updater.OpenReleasesPage();
                        return;
                    }

                    UpdateStatus = Settings.T("UpdateDownloading");
                    string installerPath = await _updater.DownloadInstallerAsync(info.InstallerUrl);
                    _updater.RunInstaller(installerPath);
                    Application.Current.Shutdown(); // let the installer replace our files
                }
            }
            catch (Exception ex)
            {
                Log.Error("Update check failed", ex);
                UpdateStatus = Settings.T("UpdateFailed");
            }
            finally { _updateCheckRunning = false; }
        }

        // --- SHUTDOWN ---

        internal void SaveSession() => SessionStore.Save(History.Texts, History.Screenshots);

        // --- HELPERS ---

        private static void TryDeleteFile(string path)
        {
            try { File.Delete(path); }
            catch (Exception ex) { Log.Warn($"Delete cached screenshot failed: {ex.Message}"); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
