using AIO_Hybrid_Clipboard.Common;
using AIO_Hybrid_Clipboard.Models;
using AIO_Hybrid_Clipboard.Services;
using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace AIO_Hybrid_Clipboard.ViewModels
{
    /// <summary>
    /// Application state and behavior behind <c>MainWindow</c>: history collections
    /// (exposed as filtered views), 80ms debounced multi-word fuzzy search, category filtering,
    /// pinning, non-blocking copy-back/auto-paste, cancellable background OCR, incognito pause,
    /// toast notifications, settings persistence, async cold startup, and deterministic shutdown.
    /// </summary>
    public sealed class MainViewModel : INotifyPropertyChanged
    {
        internal HistoryService  History  { get; }
        internal SettingsService Settings { get; }

        private readonly UpdateService  _updater;
        private readonly IDialogService _dialogs;
        private readonly CancellationTokenSource _shutdownCts = new();
        private readonly Dictionary<ScreenshotModel, CancellationTokenSource> _activeOcr = new();

        private ClipboardService?        _clipboard;
        private CancellationTokenSource? _toastCts;
        private CancellationTokenSource? _searchDebounceCts;
        private string                   _effectiveSearchQuery = string.Empty;
        private uint                     _hotkeyModifier;
        private uint                     _hotkeyKey;
        private bool                     _sessionInitialized;

        /// <summary>Production constructor: defers disk I/O to <see cref="InitializeAsync"/> for instant startup.</summary>
        public MainViewModel() : this(null, null, null, loadSessionSync: false) { }

        /// <summary>Test seam: inject fakes and optionally load session synchronously.</summary>
        internal MainViewModel(
            UpdateService? updater,
            IDialogService? dialogs,
            SettingsService? settings = null,
            bool loadSessionSync = true)
        {
            _updater = updater  ?? new UpdateService();
            _dialogs = dialogs  ?? new MessageBoxDialogService();
            Settings = settings ?? new SettingsService();

            Settings.Load(out _hotkeyModifier, out _hotkeyKey);
            History = new HistoryService(Settings.HistoryLimit);
            Loc     = new LocalizationProxy(Settings);

            _hideOnCopy        = Settings.HideOnCopy;
            _autoPaste         = Settings.AutoPaste;
            _startMinimized    = Settings.StartMinimized;
            _autoOcr           = Settings.AutoOcr;
            _quickPasteEnabled = Settings.QuickPasteEnabled;
            _historyLimit      = Settings.HistoryLimit;
            _ocrLanguage       = Settings.OcrLanguage;
            _excludedApps      = Settings.ExcludedApps;
            _encryptSession    = Settings.EncryptSession;
            _retentionDays     = Settings.RetentionDays;

            OcrLanguages = new System.Collections.ObjectModel.ObservableCollection<string> { OcrService.AutoLanguageTag };
            if (!string.Equals(_ocrLanguage, OcrService.AutoLanguageTag, StringComparison.OrdinalIgnoreCase))
                OcrLanguages.Add(_ocrLanguage);
            RebuildRetentionOptions();

            TextsView = CollectionViewSource.GetDefaultView(History.Texts);
            TextsView.Filter = o => o is ClipItem c &&
                HistoryService.MatchesText(c, _effectiveSearchQuery, _selectedCategory);

            ScreenshotsView = CollectionViewSource.GetDefaultView(History.Screenshots);
            ScreenshotsView.Filter = o => o is ScreenshotModel s &&
                HistoryService.MatchesScreenshot(s, _effectiveSearchQuery, _selectedCategory);

            History.Texts.CollectionChanged       += OnHistoryCollectionChanged;
            History.Screenshots.CollectionChanged += OnHistoryCollectionChanged;

            _startWithWindows = StartupService.IsEnabled();

            CopyTextCommand               = new RelayCommand(p => { if (p is ClipItem item) CopyText(item.Text); });
            CopyAndPasteCommand           = new RelayCommand(p => { if (p is ClipItem item) CopyText(item.Text, forceAutoPaste: true); });
            TogglePinTextCommand          = new RelayCommand(p => { if (p is ClipItem item) TogglePin(item); });
            TogglePinScreenshotCommand    = new RelayCommand(p => { if (p is ScreenshotModel shot) TogglePin(shot); });
            DeleteSingleTextCommand       = new RelayCommand(p => { if (p is ClipItem item) DeleteText(item); });
            DeleteSingleScreenshotCommand = new RelayCommand(p => { if (p is ScreenshotModel shot) DeleteScreenshot(shot); });
            CopyScreenshotCommand         = new RelayCommand(p => { if (p is ScreenshotModel shot) CopyScreenshot(shot); });
            CopyOcrTextCommand            = new RelayCommand(p => { if (p is ScreenshotModel shot) CopyOcrText(shot); });
            OpenUrlCommand                = new RelayCommand(p => OpenUrl(p is ClipItem item ? item.Text : p as string));
            ClearUnpinnedCommand          = new RelayCommand(_ => ClearUnpinned());
            OpenCacheFolderCommand        = new RelayCommand(_ => OpenCacheFolder());
            ClearSearchCommand            = new RelayCommand(_ => SearchQuery = string.Empty);
            TogglePauseCommand            = new RelayCommand(_ => IsPaused = !IsPaused);
            SetAliasCommand               = new RelayCommand(p => { if (p is ClipItem item) EditAlias(item); });
            SaveScreenshotAsCommand       = new RelayCommand(p => { if (p is ScreenshotModel shot) SaveScreenshotAs(shot); });
            EditOcrTextCommand            = new RelayCommand(p => { if (p is ScreenshotModel shot) EditOcrText(shot); });
            SetCategoryCommand            = new RelayCommand(p =>
            {
                if (p is ClipFilterCategory cat) SelectedCategory = cat;
                else if (p is string s && Enum.TryParse<ClipFilterCategory>(s, ignoreCase: true, out var parsed))
                    SelectedCategory = parsed;
            });

            RunOcrCommand = new AsyncRelayCommand(
                async p => { if (p is ScreenshotModel shot) await RunOcrAsync(shot); },
                allowConcurrent: true);

            CheckUpdatesCommand = new AsyncRelayCommand(
                async _ => await CheckForUpdatesAsync(),
                _ => !_updateCheckRunning);

            if (loadSessionSync)
                LoadSessionSynchronously();
            else
                RefreshViewCounts();
        }

        // --- ASYNC COLD STARTUP ---

        private void LoadSessionSynchronously()
        {
            _sessionInitialized = true;
            var (texts, shots) = SessionStore.Load();
            ApplyLoadedSession(texts, shots);
            SessionStore.CleanOrphanCache(History.Screenshots.Select(s => s.Path));
        }

        /// <summary>
        /// Loads persisted history thumbnails, cleans orphan cache files, and schedules a quiet
        /// update check completely off the UI thread so cold startup is instantaneous.
        /// </summary>
        public async Task InitializeAsync()
        {
            if (_sessionInitialized || _shutdownCts.IsCancellationRequested) return;
            _sessionInitialized = true;

            try
            {
                var ct = _shutdownCts.Token;
                var sessionTask   = Task.Run(SessionStore.Load, ct);
                var ocrLangsTask  = Task.Run(OcrService.GetAvailableLanguages, ct);

                var (texts, shots) = await sessionTask;
                if (ct.IsCancellationRequested) return;

                ApplyLoadedSession(texts, shots);

                // Apply TTL expiration once on startup (0% idle CPU).
                if (_retentionDays > 0)
                {
                    var expiredShots = History.PurgeExpired(_retentionDays);
                    foreach (var shot in expiredShots) TryDeleteFile(shot.Path);
                    if (expiredShots.Count > 0) ScheduleAutoSave();
                }

                var langs = await ocrLangsTask;
                if (!ct.IsCancellationRequested)
                {
                    foreach (string tag in langs)
                    {
                        if (!OcrLanguages.Contains(tag, StringComparer.OrdinalIgnoreCase))
                            OcrLanguages.Add(tag);
                    }
                    OnPropertyChanged(nameof(SelectedOcrLanguage));
                }

                var keepPaths = History.Screenshots.Select(s => s.Path).ToList();
                _ = Task.Run(() => SessionStore.CleanOrphanCache(keepPaths), ct);

                // Defer quiet network update check so startup stays 0-CPU/0-network.
                await Task.Delay(2000, ct);
                await CheckForUpdatesSilentAsync(ct);
            }
            catch (OperationCanceledException)
            {
                // App shut down before background init finished.
            }
            catch (Exception ex)
            {
                Log.Error("Async startup initialization failed", ex);
            }
        }

        private void ApplyLoadedSession(List<ClipItem> texts, List<ScreenshotModel> shots)
        {
            // Append restored items below anything the user may have already copied during startup.
            var existingTexts = new HashSet<string>(History.Texts.Select(t => t.Text), StringComparer.Ordinal);
            foreach (var t in texts)
            {
                if (existingTexts.Add(t.Text))
                    History.Texts.Add(t);
            }

            var existingPaths = new HashSet<string>(History.Screenshots.Select(s => s.Path), StringComparer.OrdinalIgnoreCase);
            foreach (var s in shots)
            {
                if (existingPaths.Add(s.Path))
                    History.Screenshots.Add(s);
            }

            if (History.Texts.Count == 0)
                History.Texts.Add(new ClipItem(Settings.T("InitialMsg")));

            RefreshViews();
        }

        // --- BINDABLE STATE ---

        public ICollectionView   TextsView       { get; }
        public ICollectionView   ScreenshotsView { get; }
        public LocalizationProxy Loc             { get; }

        public string VersionText => $"v{UpdateService.CurrentVersion}";

        /// <summary>Debounce window (ms) for search filtering while typing. 80ms in production.</summary>
        internal int SearchDebounceMs { get; set; } = 80;

        /// <summary>Diagnostic counter incremented on every ICollectionView filter pass (for tests).</summary>
        internal int FilterRefreshCount { get; private set; }

        private string _searchQuery = string.Empty;
        /// <summary>
        /// Raw search text preserved as-typed (never trimmed in the setter) so users can
        /// freely enter multi-word queries with spaces. Filters are debounced by 80ms.
        /// </summary>
        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                string next = value ?? string.Empty;
                if (string.Equals(_searchQuery, next, StringComparison.Ordinal)) return;
                _searchQuery = next;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSearchQuery));

                ScheduleSearchRefresh(next);
            }
        }

        private void ScheduleSearchRefresh(string query)
        {
            var prev = _searchDebounceCts;
            _searchDebounceCts = null;
            prev?.Cancel();

            // Clearing search or zero-debounce tests apply immediately.
            if (string.IsNullOrEmpty(query) || SearchDebounceMs <= 0)
            {
                _effectiveSearchQuery = query;
                RefreshViews();
                return;
            }

            var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token);
            _searchDebounceCts = cts;
            _ = DebounceSearchAsync(query, cts);
        }

        private async Task DebounceSearchAsync(string query, CancellationTokenSource cts)
        {
            try
            {
                await Task.Delay(SearchDebounceMs, cts.Token);
                if (cts.IsCancellationRequested) return;

                _effectiveSearchQuery = query;
                RefreshViews();
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer keystroke or shutdown.
            }
            finally
            {
                if (ReferenceEquals(_searchDebounceCts, cts))
                    _searchDebounceCts = null;
                cts.Dispose();
            }
        }

        public bool HasSearchQuery => !string.IsNullOrEmpty(_searchQuery);

        private ClipFilterCategory _selectedCategory = ClipFilterCategory.All;
        public ClipFilterCategory SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (_selectedCategory == value) return;
                _selectedCategory = value;
                OnPropertyChanged();
                RefreshViews();
            }
        }

        private int _visibleTextsCount;
        public int VisibleTextsCount
        {
            get => _visibleTextsCount;
            private set
            {
                if (_visibleTextsCount == value) return;
                _visibleTextsCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsTextsEmpty));
            }
        }

        private int _visibleScreenshotsCount;
        public int VisibleScreenshotsCount
        {
            get => _visibleScreenshotsCount;
            private set
            {
                if (_visibleScreenshotsCount == value) return;
                _visibleScreenshotsCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsScreenshotsEmpty));
            }
        }

        public bool IsTextsEmpty       => _visibleTextsCount == 0;
        public bool IsScreenshotsEmpty => _visibleScreenshotsCount == 0;

        private bool _isEditMode;
        public bool IsEditMode
        {
            get => _isEditMode;
            set { if (_isEditMode != value) { _isEditMode = value; OnPropertyChanged(); } }
        }

        private bool _isPaused;
        public bool IsPaused
        {
            get => _isPaused;
            set
            {
                if (_isPaused == value) return;
                _isPaused = value;
                if (_clipboard != null) _clipboard.IsPaused = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PauseMenuLabel));
                OnPropertyChanged(nameof(PauseTooltip));
                PauseStateChanged?.Invoke(value);
                ShowToast(Settings.T(value ? "ToastPaused" : "ToastResumed"), isWarning: value);
            }
        }

        public string PauseMenuLabel => Settings.T(_isPaused ? "TrayResume" : "TrayPause");
        public string PauseTooltip   => Settings.T(_isPaused ? "ResumeMonitoring" : "PauseMonitoring");

        // --- PERSISTENT SETTINGS ---

        private bool _hideOnCopy;
        public bool HideOnCopy
        {
            get => _hideOnCopy;
            set
            {
                if (_hideOnCopy == value) return;
                _hideOnCopy = value;
                Settings.HideOnCopy = value;
                PersistSettings();
                OnPropertyChanged();
            }
        }

        private bool _autoPaste;
        public bool AutoPaste
        {
            get => _autoPaste;
            set
            {
                if (_autoPaste == value) return;
                _autoPaste = value;
                Settings.AutoPaste = value;
                PersistSettings();
                OnPropertyChanged();
            }
        }

        private bool _startMinimized;
        public bool StartMinimized
        {
            get => _startMinimized;
            set
            {
                if (_startMinimized == value) return;
                _startMinimized = value;
                Settings.StartMinimized = value;
                PersistSettings();
                OnPropertyChanged();
            }
        }

        private bool _autoOcr;
        public bool AutoOcr
        {
            get => _autoOcr;
            set
            {
                if (_autoOcr == value) return;
                _autoOcr = value;
                Settings.AutoOcr = value;
                PersistSettings();
                OnPropertyChanged();
            }
        }

        private bool _quickPasteEnabled;
        public bool QuickPasteEnabled
        {
            get => _quickPasteEnabled;
            set
            {
                if (_quickPasteEnabled == value) return;
                _quickPasteEnabled = value;
                Settings.QuickPasteEnabled = value;
                PersistSettings();
                OnPropertyChanged();
                QuickPasteSettingChanged?.Invoke(value);
            }
        }

        private int _historyLimit;
        public int HistoryLimit
        {
            get => _historyLimit;
            set
            {
                int clamped = HistoryService.ClampLimit(value);
                if (_historyLimit == clamped) return;
                _historyLimit = clamped;
                Settings.HistoryLimit = clamped;
                History.MaxItems = clamped;
                var evictedShots = History.TrimToLimit();
                foreach (var shot in evictedShots)
                {
                    CancelOcrFor(shot);
                    shot.ReleasePreview();
                    TryDeleteFile(shot.Path);
                }
                PersistSettings();
                ScheduleAutoSave();
                OnPropertyChanged();
            }
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
                PersistSettings();
                OnPropertyChanged();
                Loc.Refresh();
                RebuildRetentionOptions();
                OnPropertyChanged(nameof(PauseMenuLabel));
                OnPropertyChanged(nameof(PauseTooltip));
                LanguageChanged?.Invoke();
            }
        }

        // --- V2.1 POWER FEATURES: OCR LANGUAGE, APP EXCLUSION, DPAPI ENCRYPTION, TTL ---

        public System.Collections.ObjectModel.ObservableCollection<string> OcrLanguages { get; }

        private string _ocrLanguage;
        public string SelectedOcrLanguage
        {
            get => _ocrLanguage;
            set
            {
                string next = string.IsNullOrWhiteSpace(value) ? OcrService.AutoLanguageTag : value.Trim();
                if (string.Equals(_ocrLanguage, next, StringComparison.OrdinalIgnoreCase)) return;
                _ocrLanguage = next;
                Settings.OcrLanguage = next;
                PersistSettings();
                OnPropertyChanged();
            }
        }

        private string _excludedApps;
        public string ExcludedApps
        {
            get => _excludedApps;
            set
            {
                string next = value ?? string.Empty;
                if (string.Equals(_excludedApps, next, StringComparison.Ordinal)) return;
                _excludedApps = next;
                Settings.ExcludedApps = next;
                _clipboard?.SetExcludedApps(next);
                PersistSettings();
                OnPropertyChanged();
            }
        }

        private bool _encryptSession;
        public bool EncryptSession
        {
            get => _encryptSession;
            set
            {
                if (_encryptSession == value) return;
                _encryptSession = value;
                Settings.EncryptSession = value;
                PersistSettings();
                OnPropertyChanged();
                if (AutoSaveEnabled)
                    SessionStore.Save(History.Texts, History.Screenshots, encrypt: value);
                ShowToast(Settings.T(value ? "ToastEncryptionOn" : "ToastEncryptionOff"));
            }
        }

        public System.Collections.ObjectModel.ObservableCollection<string> RetentionOptions { get; } = new();

        private int _retentionDays;
        public int RetentionIndex
        {
            get
            {
                int idx = Array.IndexOf(SettingsService.AllowedRetentionDays, _retentionDays);
                return idx >= 0 ? idx : 0;
            }
            set
            {
                int clampedIdx = Math.Clamp(value, 0, SettingsService.AllowedRetentionDays.Length - 1);
                int days = SettingsService.AllowedRetentionDays[clampedIdx];
                if (_retentionDays == days) return;
                _retentionDays = days;
                Settings.RetentionDays = days;
                PersistSettings();
                OnPropertyChanged();

                if (days > 0)
                {
                    var removed = History.PurgeExpired(days);
                    foreach (var shot in removed)
                    {
                        CancelOcrFor(shot);
                        shot.ReleasePreview();
                        TryDeleteFile(shot.Path);
                    }
                    if (removed.Count > 0) ScheduleAutoSave();
                }
            }
        }

        private void RebuildRetentionOptions()
        {
            int currentIdx = RetentionIndex;
            RetentionOptions.Clear();
            foreach (int days in SettingsService.AllowedRetentionDays)
            {
                RetentionOptions.Add(days == 0
                    ? Settings.T("RetentionNever")
                    : string.Format(Settings.T("RetentionDaysFormat"), days));
            }
            OnPropertyChanged(nameof(RetentionIndex));
            _ = currentIdx;
        }

        private string _updateStatus = string.Empty;
        public string UpdateStatus
        {
            get => _updateStatus;
            private set { if (_updateStatus != value) { _updateStatus = value; OnPropertyChanged(); } }
        }

        private string _hotkeyConflictWarning = string.Empty;
        public string HotkeyConflictWarning
        {
            get => _hotkeyConflictWarning;
            internal set { if (_hotkeyConflictWarning != value) { _hotkeyConflictWarning = value; OnPropertyChanged(); } }
        }

        // --- TOAST ---

        private string _toastMessage = string.Empty;
        public string ToastMessage
        {
            get => _toastMessage;
            private set { if (_toastMessage != value) { _toastMessage = value; OnPropertyChanged(); } }
        }

        private bool _isToastVisible;
        public bool IsToastVisible
        {
            get => _isToastVisible;
            private set { if (_isToastVisible != value) { _isToastVisible = value; OnPropertyChanged(); } }
        }

        private bool _toastIsWarning;
        public bool ToastIsWarning
        {
            get => _toastIsWarning;
            private set { if (_toastIsWarning != value) { _toastIsWarning = value; OnPropertyChanged(); } }
        }

        public void ShowToast(string message, bool isWarning = false, int durationMs = 2400)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            var prev = _toastCts;
            _toastCts = null;
            prev?.Cancel();

            var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token);
            _toastCts = cts;

            ToastMessage   = message;
            ToastIsWarning = isWarning;
            IsToastVisible = true;

            _ = AutoHideToastAsync(cts, durationMs);
        }

        public void DismissToast()
        {
            var prev = _toastCts;
            _toastCts = null;
            prev?.Cancel();
            IsToastVisible = false;
        }

        private async Task AutoHideToastAsync(CancellationTokenSource cts, int durationMs)
        {
            try
            {
                await Task.Delay(durationMs, cts.Token);
                if (!cts.IsCancellationRequested)
                    IsToastVisible = false;
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer toast, hide, or shutdown.
            }
            finally
            {
                if (ReferenceEquals(_toastCts, cts))
                    _toastCts = null;
                cts.Dispose();
            }
        }

        /// <summary>
        /// Releases all transient hover preview bitmaps and stops any toast delay timer
        /// when the launcher hides, keeping idle RAM and CPU at baseline.
        /// </summary>
        internal void ReleaseTransientResources()
        {
            DismissToast();
            foreach (var shot in History.Screenshots)
                shot.ReleasePreview();
        }

        // --- COMMANDS ---

        public ICommand CopyTextCommand               { get; }
        public ICommand CopyAndPasteCommand           { get; }
        public ICommand TogglePinTextCommand          { get; }
        public ICommand TogglePinScreenshotCommand    { get; }
        public ICommand DeleteSingleTextCommand       { get; }
        public ICommand DeleteSingleScreenshotCommand { get; }
        public ICommand RunOcrCommand                 { get; }
        public ICommand CopyScreenshotCommand         { get; }
        public ICommand CopyOcrTextCommand            { get; }
        public ICommand OpenUrlCommand                { get; }
        public ICommand ClearUnpinnedCommand          { get; }
        public ICommand OpenCacheFolderCommand        { get; }
        public ICommand ClearSearchCommand            { get; }
        public ICommand SetCategoryCommand            { get; }
        public ICommand TogglePauseCommand            { get; }
        public ICommand SetAliasCommand               { get; }
        public ICommand SaveScreenshotAsCommand       { get; }
        public ICommand EditOcrTextCommand            { get; }
        public ICommand CheckUpdatesCommand           { get; }

        // --- VIEW EVENTS ---

        /// <summary>Raised when the window should hide (copy-back with HideOnCopy or AutoPaste).</summary>
        public event Action? HideRequested;

        /// <summary>Raised when the view should restore the previous foreground window and paste.</summary>
        public event Action? PasteToPreviousWindowRequested;

        /// <summary>Raised when QuickPasteEnabled changes so the view can re-register hotkeys.</summary>
        public event Action<bool>? QuickPasteSettingChanged;

        /// <summary>Raised when Incognito/Pause state changes so the view can update tray tooltip.</summary>
        public event Action<bool>? PauseStateChanged;

        /// <summary>Raised when UI language changes so dynamic formatted labels in the view can refresh.</summary>
        public event Action? LanguageChanged;

        // --- HOTKEY PERSISTENCE ---

        public uint HotkeyModifier => _hotkeyModifier;
        public uint HotkeyKey      => _hotkeyKey;

        internal void SaveHotkey(uint modifier, uint key)
        {
            _hotkeyModifier = modifier;
            _hotkeyKey      = key;
            PersistSettings();
        }

        internal void ReportHotkeyRegistration(HotkeyRegistrationResult result, bool showToastOnSuccess)
        {
            if (!result.ToggleRegistered)
            {
                HotkeyConflictWarning = Settings.T("HotkeyConflict");
                ShowToast(Settings.T("ToastHotkeyConflict"), isWarning: true, durationMs: 3600);
            }
            else if (!result.QuickPasteRegistered && QuickPasteEnabled)
            {
                HotkeyConflictWarning = Settings.T("HotkeyConflict");
                ShowToast(Settings.T("ToastHotkeyConflict"), isWarning: true, durationMs: 3200);
            }
            else
            {
                HotkeyConflictWarning = string.Empty;
                if (showToastOnSuccess)
                    ShowToast(Settings.T("ToastHotkeySaved"));
            }
        }

        // --- CLIPBOARD ---

        /// <summary>Wires the HWND-bound clipboard service created by the view.</summary>
        internal void AttachClipboard(ClipboardService clipboard)
        {
            _clipboard = clipboard;
            _clipboard.IsPaused = _isPaused;
            _clipboard.SetExcludedApps(_excludedApps);
            clipboard.TextCaptured  += OnTextCaptured;
            clipboard.ImageCaptured += OnImageCaptured;
        }

        private void OnTextCaptured(string text, string sourceApp)
        {
            History.InsertText(text, sourceApp);
            ScheduleAutoSave();
        }

        private async void OnImageCaptured(ScreenshotModel model, PooledPixelBuffer pixelBuffer)
        {
            using (pixelBuffer)
            {
                var evicted = History.AddScreenshot(model, out bool wasDuplicate);
                if (wasDuplicate)
                {
                    TryDeleteFile(model.Path);
                    ScheduleAutoSave();
                    return;
                }

                if (evicted != null)
                {
                    CancelOcrFor(evicted);
                    evicted.ReleasePreview();
                    TryDeleteFile(evicted.Path);
                }

                ScheduleAutoSave();

                if (!AutoOcr || _shutdownCts.IsCancellationRequested)
                    return;

                using var ocrCts = RegisterOcrCancellation(model);
                model.IsOcrRunning = true;
                try
                {
                    string ocrResult = await OcrService.RecognizeAsync(
                        pixelBuffer.Buffer,
                        pixelBuffer.Width,
                        pixelBuffer.Height,
                        pixelBuffer.Stride,
                        ocrCts.Token,
                        _ocrLanguage);

                    if (!ocrCts.IsCancellationRequested && !string.IsNullOrEmpty(ocrResult))
                    {
                        model.OcrText = ocrResult;
                        ScheduleAutoSave();
                    }
                }
                catch (OperationCanceledException)
                {
                    // Item deleted or application exiting — stop silently.
                }
                catch (Exception ex) { Log.Error("Background OCR failed", ex); }
                finally
                {
                    model.IsOcrRunning = false;
                    UnregisterOcrCancellation(model, ocrCts);
                }
            }
        }

        internal void CopyText(string? text, bool forceAutoPaste = false)
        {
            if (IsEditMode) return;
            if (string.IsNullOrEmpty(text) || OcrService.IsPlaceholder(text)) return;

            if (_clipboard == null)
            {
                CompleteCopyAction(forceAutoPaste, Settings.T("ToastCopied"));
                return;
            }

            _ = CopyTextWithClipboardAsync(text, forceAutoPaste);
        }

        private async Task CopyTextWithClipboardAsync(string text, bool forceAutoPaste)
        {
            if (_clipboard == null) return;
            bool ok = await _clipboard.SetClipboardSilentAsync(text, _shutdownCts.Token);
            if (ok)
                CompleteCopyAction(forceAutoPaste, Settings.T("ToastCopied"));
        }

        internal void CopyScreenshot(ScreenshotModel shot, bool forceAutoPaste = false)
        {
            if (IsEditMode || string.IsNullOrWhiteSpace(shot.Path)) return;

            if (_clipboard == null)
            {
                CompleteCopyAction(forceAutoPaste, Settings.T("ToastImageCopied"));
                return;
            }

            _ = CopyScreenshotWithClipboardAsync(shot.Path, forceAutoPaste);
        }

        private async Task CopyScreenshotWithClipboardAsync(string path, bool forceAutoPaste)
        {
            if (_clipboard == null) return;
            bool ok = await _clipboard.SetImageSilentAsync(path, _shutdownCts.Token);
            if (ok)
                CompleteCopyAction(forceAutoPaste, Settings.T("ToastImageCopied"));
        }

        private void CompleteCopyAction(bool forceAutoPaste, string toastMessage)
        {
            bool shouldPaste = forceAutoPaste || AutoPaste;
            if (shouldPaste)
            {
                HideRequested?.Invoke();
                PasteToPreviousWindowRequested?.Invoke();
            }
            else if (HideOnCopy)
            {
                HideRequested?.Invoke();
            }
            else
            {
                ShowToast(toastMessage);
            }
        }

        internal void CopyOcrText(ScreenshotModel shot)
        {
            if (IsEditMode || string.IsNullOrWhiteSpace(shot.OcrText) || OcrService.IsPlaceholder(shot.OcrText))
                return;

            CopyText(shot.OcrText);
        }

        internal async void QuickPaste(int index)
        {
            if (!QuickPasteEnabled || index < 0 || History.Texts.Count <= index) return;
            string text = History.Texts[index].Text;
            if (string.IsNullOrEmpty(text)) return;

            try
            {
                if (_clipboard != null)
                {
                    bool ok = await _clipboard.SetClipboardSilentAsync(text, _shutdownCts.Token);
                    if (!ok) return;
                }
                HotkeyManager.SimulatePaste();
            }
            catch (Exception ex) { Log.Warn($"QuickPaste failed: {ex.Message}"); }
        }

        internal void OpenUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            string trimmed = url.Trim();

            // Strictly allow http/https to prevent launching arbitrary local executables or protocols.
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex) { Log.Warn($"OpenUrl failed: {ex.Message}"); }
        }

        internal void OpenCacheFolder()
        {
            try
            {
                AppPaths.EnsureCacheFolder();
                Process.Start(new ProcessStartInfo(AppPaths.CacheFolder) { UseShellExecute = true });
            }
            catch (Exception ex) { Log.Warn($"OpenCacheFolder failed: {ex.Message}"); }
        }

        internal void TransformClip(ClipItem item, TextTransformKind kind)
        {
            string? transformed = TextTransformer.Apply(item.Text, kind);
            if (transformed == null)
            {
                if (kind is TextTransformKind.FormatJson or TextTransformKind.MinifyJson)
                    ShowToast(Settings.T("ToastInvalidJson"), isWarning: true);
                return;
            }

            History.InsertText(transformed, item.SourceApp);
            ScheduleAutoSave();
            CopyText(transformed);
            if (!HideOnCopy && !AutoPaste)
                ShowToast(Settings.T("ToastTransformed"));
        }

        internal void EditAlias(ClipItem item)
        {
            string? result = _dialogs.PromptText(
                Settings.T("AliasDialogTitle"),
                Settings.T("AliasDialogPrompt"),
                item.Alias,
                multiline: false,
                okLabel: Settings.T("BtnSave"),
                cancelLabel: Settings.T("BtnCancel"));

            if (result == null) return;

            item.Alias = result.Trim();
            RefreshViews();
            ScheduleAutoSave();
            ShowToast(Settings.T("ToastAliasSaved"));
        }

        internal void SaveScreenshotAs(ScreenshotModel shot)
        {
            if (string.IsNullOrWhiteSpace(shot.Path) || !File.Exists(shot.Path)) return;

            string suggested = $"capture_{shot.CapturedAt:yyyyMMdd_HHmmss}.png";
            string? dest = _dialogs.PickSavePngPath(suggested);
            if (string.IsNullOrWhiteSpace(dest)) return;

            try
            {
                File.Copy(shot.Path, dest, overwrite: true);
                ShowToast(Settings.T("ToastPngSaved"));
            }
            catch (Exception ex)
            {
                Log.Error("Save screenshot as PNG failed", ex);
                _dialogs.ShowError(ex.Message, Settings.T("MenuSavePng"));
            }
        }

        internal void EditOcrText(ScreenshotModel shot)
        {
            string? edited = _dialogs.PromptText(
                Settings.T("EditOcrDialogTitle"),
                Settings.T("EditOcrDialogPrompt"),
                shot.OcrText,
                multiline: true,
                okLabel: Settings.T("BtnSave"),
                cancelLabel: Settings.T("BtnCancel"));

            if (edited == null) return;

            string trimmed = edited.Trim();
            shot.OcrText = trimmed;
            if (!string.IsNullOrEmpty(trimmed))
            {
                History.InsertText(trimmed, shot.SourceApp);
                CopyText(trimmed);
            }
            ScheduleAutoSave();
        }

        // --- PINNING ---

        internal void TogglePin(ClipItem item)
        {
            History.TogglePin(item);
            RefreshViews();
            ScheduleAutoSave();
        }

        internal void TogglePin(ScreenshotModel shot)
        {
            History.TogglePin(shot);
            RefreshViews();
            ScheduleAutoSave();
        }

        // --- OCR ---

        internal async Task RunOcrAsync(ScreenshotModel model)
        {
            if (IsEditMode || model.IsOcrRunning || _shutdownCts.IsCancellationRequested) return;

            using var ocrCts = RegisterOcrCancellation(model);
            model.IsOcrRunning = true;
            try
            {
                OcrService.OcrStatus status;
                string ocrResult;

                // Prefer full-resolution cached PNG on disk (gallery thumbnails are 320px).
                if (!string.IsNullOrWhiteSpace(model.Path) && File.Exists(model.Path))
                {
                    (status, ocrResult) = await OcrService.RecognizeImageFileAsync(model.Path, ocrCts.Token, _ocrLanguage);
                }
                else if (model.Image != null)
                {
                    int width  = model.Image.PixelWidth;
                    int height = model.Image.PixelHeight;
                    int stride = width * 4;
                    int bytes  = height * stride;
                    byte[] rented = ArrayPool<byte>.Shared.Rent(bytes);
                    try
                    {
                        model.Image.CopyPixels(rented, stride, 0);
                        (status, ocrResult) = await OcrService.RecognizeWithStatusAsync(
                            rented, width, height, stride, ocrCts.Token, _ocrLanguage);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(rented);
                    }
                }
                else
                {
                    return;
                }

                if (ocrCts.IsCancellationRequested) return;

                if (status != OcrService.OcrStatus.Success || string.IsNullOrEmpty(ocrResult))
                {
                    ShowToast(Settings.T("ToastOcrNoText"), isWarning: true);
                    return;
                }

                model.OcrText = ocrResult;
                History.InsertText(ocrResult);
                ScheduleAutoSave();

                if (_clipboard != null)
                    await _clipboard.SetClipboardSilentAsync(ocrResult, _shutdownCts.Token);

                ShowToast(Settings.T("ToastOcrCopied"));
            }
            catch (OperationCanceledException)
            {
                // Screenshot deleted or app shutting down.
            }
            catch (Exception ex)
            {
                Log.Error("On-demand OCR failed", ex);
                _dialogs.ShowError(Settings.T("OcrException") + ex.Message, Settings.T("OcrError"));
            }
            finally
            {
                model.IsOcrRunning = false;
                UnregisterOcrCancellation(model, ocrCts);
            }
        }

        private CancellationTokenSource RegisterOcrCancellation(ScreenshotModel model)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token);
            lock (_activeOcr)
            {
                if (_activeOcr.TryGetValue(model, out var prev))
                    prev.Cancel();
                _activeOcr[model] = cts;
            }
            return cts;
        }

        private void UnregisterOcrCancellation(ScreenshotModel model, CancellationTokenSource cts)
        {
            lock (_activeOcr)
            {
                if (_activeOcr.TryGetValue(model, out var current) && ReferenceEquals(current, cts))
                    _activeOcr.Remove(model);
            }
        }

        internal void CancelOcrFor(ScreenshotModel model)
        {
            CancellationTokenSource? cts;
            lock (_activeOcr)
            {
                if (_activeOcr.Remove(model, out cts))
                {
                    try { cts.Cancel(); }
                    catch (ObjectDisposedException) { }
                }
            }
        }

        // --- DELETE / CLEAR ---

        internal void DeleteText(ClipItem item)
        {
            if (History.Texts.Remove(item))
                ScheduleAutoSave();
        }

        internal void DeleteScreenshot(ScreenshotModel shot)
        {
            CancelOcrFor(shot);
            shot.ReleasePreview();
            if (History.Screenshots.Remove(shot))
            {
                TryDeleteFile(shot.Path);
                ScheduleAutoSave();
            }
        }

        internal void DeleteItems(IList selectedTexts, IList selectedShots)
        {
            bool changed = false;

            foreach (var item in selectedTexts.Cast<ClipItem>().ToList())
                changed |= History.Texts.Remove(item);

            foreach (var shot in selectedShots.Cast<ScreenshotModel>().ToList())
            {
                CancelOcrFor(shot);
                shot.ReleasePreview();
                TryDeleteFile(shot.Path);
                changed |= History.Screenshots.Remove(shot);
            }

            if (changed) ScheduleAutoSave();
        }

        internal void ClearUnpinned()
        {
            bool hasUnpinned = History.Texts.Any(t => !t.IsPinned) || History.Screenshots.Any(s => !s.IsPinned);
            if (!hasUnpinned)
            {
                ShowToast(Settings.T("ToastNothingToClear"));
                return;
            }

            if (!_dialogs.Confirm(Settings.T("ClearUnpinnedConfirm"), Settings.T("ClearUnpinnedTitle")))
                return;

            var removedShots = History.ClearUnpinned();
            foreach (var shot in removedShots)
            {
                CancelOcrFor(shot);
                shot.ReleasePreview();
                TryDeleteFile(shot.Path);
            }

            ScheduleAutoSave();
            ShowToast(Settings.T("ToastCleared"));
        }

        // --- UPDATES ---

        private bool _updateCheckRunning;

        /// <summary>Quiet startup check: surfaces a notice, never shows dialogs.</summary>
        public async Task CheckForUpdatesSilentAsync(CancellationToken ct = default)
        {
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token, ct);
                var info = await _updater.CheckLatestAsync(linked.Token);
                if (info != null && info.LatestVersion > UpdateService.CurrentVersion)
                    UpdateStatus = string.Format(Settings.T("UpdateAvailable"), info.LatestVersion);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log.Warn($"Silent update check failed: {ex.Message}"); }
        }

        internal async Task CheckForUpdatesAsync()
        {
            _updateCheckRunning = true;
            UpdateStatus = Settings.T("UpdateChecking");
            try
            {
                var info = await _updater.CheckLatestAsync(_shutdownCts.Token);
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
                        _updater.OpenReleasesPage();
                        return;
                    }

                    UpdateStatus = Settings.T("UpdateDownloading");
                    var progress = new Progress<double>(pct =>
                        UpdateStatus = string.Format(Settings.T("UpdateDownloadingProgress"), pct));

                    string installerPath = await _updater.DownloadInstallerAsync(
                        info.InstallerUrl, progress, _shutdownCts.Token, info.ChecksumsUrl);
                    _updater.RunInstaller(installerPath);
                    Application.Current?.Shutdown();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Error("Update check failed", ex);
                UpdateStatus = Settings.T("UpdateFailed");
            }
            finally { _updateCheckRunning = false; }
        }

        // --- SHUTDOWN / PERSISTENCE ---

        internal bool AutoSaveEnabled { get; set; } = true;

        private void ScheduleAutoSave()
        {
            if (AutoSaveEnabled && !_shutdownCts.IsCancellationRequested)
                SessionStore.ScheduleSave(History.Texts, History.Screenshots, encrypt: _encryptSession);
        }

        /// <summary>Immediately cancels all in-flight OCR, debounce, toast, and update tasks.</summary>
        internal void CancelBackgroundWork()
        {
            try { _shutdownCts.Cancel(); }
            catch (ObjectDisposedException) { }

            var searchCts = _searchDebounceCts;
            _searchDebounceCts = null;
            searchCts?.Cancel();

            DismissToast();

            lock (_activeOcr)
            {
                foreach (var cts in _activeOcr.Values)
                {
                    try { cts.Cancel(); }
                    catch (ObjectDisposedException) { }
                }
                _activeOcr.Clear();
            }
        }

        internal void SaveSession() => SessionStore.ShutdownAndSave(History.Texts, History.Screenshots, encrypt: _encryptSession);

        private void PersistSettings() => Settings.Save(_hotkeyModifier, _hotkeyKey);

        // --- HELPERS ---

        private void OnHistoryCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
            => RefreshViewCounts();

        private void RefreshViews()
        {
            FilterRefreshCount++;
            TextsView.Refresh();
            ScreenshotsView.Refresh();
            RefreshViewCounts();
        }

        private void RefreshViewCounts()
        {
            VisibleTextsCount       = TextsView.Cast<object>().Count();
            VisibleScreenshotsCount = ScreenshotsView.Cast<object>().Count();
        }

        private static void TryDeleteFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { Log.Warn($"Delete cached screenshot failed: {ex.Message}"); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
