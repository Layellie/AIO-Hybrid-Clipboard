using AIO_Hybrid_Clipboard.Models;
using AIO_Hybrid_Clipboard.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace AIO_Hybrid_Clipboard
{
    public partial class MainWindow : Window
    {
        private IntPtr       _windowHandle;
        private HwndSource?  _hwndSource;

        private readonly HistoryService  _history  = new();
        private readonly SettingsService _settings = new();
        private readonly HotkeyManager   _hotkeys  = new();
        private ClipboardService?        _clipboard;
        private TrayIconService?         _tray;

        private bool  _isCapturingHotkey;
        private Point _dragStartPoint;

        public MainWindow()
        {
            InitializeComponent();
            this.PreviewKeyDown += MainWindow_PreviewKeyDown;

            _settings.Load(out uint modifier, out uint key);
            _hotkeys.CurrentModifier = modifier;
            _hotkeys.CurrentKey      = key;

            PopulateSettingsUI();
            ChkStartWithWindows.IsChecked = StartupService.IsEnabled();

            var (texts, shots) = SessionStore.Load();
            foreach (var t in texts) _history.Texts.Add(t);
            foreach (var s in shots) _history.Screenshots.Add(s);
            if (_history.Texts.Count == 0) _history.Texts.Add(new ClipItem(_settings.T("InitialMsg")));

            // Drop cached PNGs that no restored screenshot points at anymore.
            SessionStore.CleanOrphanCache(_history.Screenshots.Select(s => s.Path));

            TxtSearch.TextChanged        += TxtSearch_TextChanged;
            CmbLanguage.SelectionChanged += CmbLanguage_SelectionChanged;
            ApplyLanguage();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _windowHandle = new WindowInteropHelper(this).Handle;

            this.Left = (SystemParameters.PrimaryScreenWidth - this.Width) / 2;
            this.Top  = 15;

            _hotkeys.Attach(_windowHandle);
            _hotkeys.ToggleRequested     += ToggleLauncher;
            _hotkeys.QuickPasteRequested += QuickPaste;
            _hotkeys.Register();

            _clipboard = new ClipboardService(_windowHandle);
            _clipboard.TextCaptured  += OnTextCaptured;
            _clipboard.ImageCaptured += OnImageCaptured;
            _clipboard.Start();

            _tray = new TrayIconService(_windowHandle);
            _tray.OpenRequested        += ShowLauncher;
            _tray.ContextMenuRequested += ShowTrayContextMenu;
            _tray.Initialize();

            _hwndSource = HwndSource.FromHwnd(_windowHandle);
            _hwndSource.AddHook(HwndHook);

            LstResults.ItemsSource     = _history.Texts;
            LstScreenshots.ItemsSource = _history.Screenshots;

            _ = CheckForUpdatesSilentAsync();
        }

        // --- WND PROC ---
        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Win32Api.WM_TRAYICON)
            {
                _tray?.HandleTrayMessage(lParam.ToInt32());
                handled = true;
            }
            else if (msg == Win32Api.WM_HOTKEY)
            {
                if (_hotkeys.HandleMessage(wParam.ToInt32())) handled = true;
            }
            else if (msg == Win32Api.WM_CLIPBOARDUPDATE)
            {
                _clipboard?.HandleClipboardUpdate();
                handled = true;
            }
            return IntPtr.Zero;
        }

        // --- CLIPBOARD EVENTS ---
        private void OnTextCaptured(string text) => _history.InsertText(text);

        private async void OnImageCaptured(ScreenshotModel model, byte[] pixels, int width, int height, int stride)
        {
            var evicted = _history.AddScreenshot(model);
            if (evicted != null)
            {
                try { File.Delete(evicted.Path); }
                catch (Exception ex) { Log.Warn($"Delete evicted screenshot failed: {ex.Message}"); }
            }

            try
            {
                string ocrResult = await OcrService.RecognizeAsync(pixels, width, height, stride);
                if (!string.IsNullOrEmpty(ocrResult))
                    model.OcrText = ocrResult;
            }
            catch (Exception ex) { Log.Error("Background OCR failed", ex); }
        }

        // --- SEARCH ---
        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

        private void ApplyFilter()
        {
            if (TxtSearch == null || LstResults == null || LstScreenshots == null) return;
            string query = TxtSearch.Text.Trim();

            if (string.IsNullOrEmpty(query))
            {
                LstResults.ItemsSource     = _history.Texts;
                LstScreenshots.ItemsSource = _history.Screenshots;
                return;
            }

            LstResults.ItemsSource     = _history.FilterTexts(query);
            LstScreenshots.ItemsSource = _history.FilterScreenshots(query);
        }

        // --- DRAG & DROP ---
        private void Screenshot_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => _dragStartPoint = e.GetPosition(null);

        private void Screenshot_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                Point pos = e.GetPosition(null);
                if (Math.Abs(pos.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(pos.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    if (sender is Border { DataContext: ScreenshotModel model })
                        DragDrop.DoDragDrop((Border)sender, new DataObject(DataFormats.FileDrop, new[] { model.Path }), DragDropEffects.Copy);
                }
            }
        }

        private async void Screenshot_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (ChkEdit?.IsChecked == true) return;
            Point pos = e.GetPosition(null);
            if (Math.Abs(pos.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                if (sender is Border { DataContext: ScreenshotModel model })
                    await ExecuteOcrOnScreenshot(model);
            }
        }

        // --- ON-DEMAND OCR ---
        private async Task ExecuteOcrOnScreenshot(ScreenshotModel model)
        {
            if (model.Image == null) return;

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
                    model.OcrText = _settings.T("OcrNoText");
                    return;
                }

                model.OcrText = ocrResult;
                _history.InsertText(ocrResult);
                ApplyFilter();
                _clipboard?.SetClipboardSilent(ocrResult);
            }
            catch (Exception ex)
            {
                Log.Error("On-demand OCR failed", ex);
                MessageBox.Show(_settings.T("OcrException") + ex.Message, _settings.T("OcrError"));
            }
        }

        // --- COPY & HIDE ---
        private void CopyBackAndHide(string? text)
        {
            if (string.IsNullOrEmpty(text) || OcrService.IsPlaceholder(text)) return;
            try { _clipboard?.SetClipboardSilent(text); } catch (Exception ex) { Log.Warn($"SetClipboard failed: {ex.Message}"); }
            if (ChkHideOnCopy.IsChecked == true) this.Hide();
        }

        // --- QUICK PASTE ---
        private void QuickPaste(int index)
        {
            if (_history.Texts.Count <= index) return;
            string text = _history.Texts[index].Text;
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                _clipboard?.SetClipboardSilent(text);
                HotkeyManager.SimulatePaste();
            }
            catch (Exception ex) { Log.Warn($"QuickPaste failed: {ex.Message}"); }
        }

        // --- TRAY ---
        private void ShowTrayContextMenu()
        {
            Win32Api.SetForegroundWindow(_windowHandle);
            if (this.FindResource("DiscordTrayMenu") is ContextMenu menu)
            {
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                menu.IsOpen    = true;
            }
        }

        private void TrayMenu_Open_Click(object sender, RoutedEventArgs e) => ShowLauncher();
        private void TrayMenu_Close_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        // --- LAUNCHER ---
        private void ToggleLauncher() { if (this.Visibility == Visibility.Visible) this.Hide(); else ShowLauncher(); }

        /// <summary>Shows and focuses the launcher. Also invoked when a second app instance starts.</summary>
        public void ShowLauncher()
        {
            this.Show();
            this.Activate();
            TxtSearch.Focus();
        }

        // --- LANGUAGE & SETTINGS ---
        private void ApplyLanguage()
        {
            ChkHideOnCopy.Content       = _settings.T("HideOnCopy");
            ChkStartWithWindows.Content = _settings.T("StartWithWindows");
            TxtShortcutLabel.Text       = _settings.T("ShortcutKey");
            TxtLanguageLabel.Text       = _settings.T("Language");
            TxtVersionLabel.Text        = _settings.T("Version");
            BtnCheckUpdate.Content      = _settings.T("CheckUpdates");

            if (this.Resources["DiscordTrayMenu"] is ContextMenu trayMenu)
            {
                if (trayMenu.Items[0] is MenuItem open) open.Header = _settings.T("TrayOpen");
                if (trayMenu.Items[1] is MenuItem exit) exit.Header = _settings.T("TrayExit");
            }
        }

        private void PopulateSettingsUI()
        {
            CmbLanguage.SelectedIndex = _settings.CurrentLanguage == SettingsService.AppLanguage.Turkish ? 1 : 0;
            TxtVersionValue.Text = $"v{UpdateService.CurrentVersion}";
            UpdateHotkeyDisplay();
        }

        private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _settings.CurrentLanguage = CmbLanguage.SelectedIndex == 1
                ? SettingsService.AppLanguage.Turkish
                : SettingsService.AppLanguage.English;
            ApplyLanguage();
            _settings.Save(_hotkeys.CurrentModifier, _hotkeys.CurrentKey);
        }

        // --- STARTUP ---
        private void ChkStartWithWindows_Checked(object sender, RoutedEventArgs e)   => StartupService.Set(true);
        private void ChkStartWithWindows_Unchecked(object sender, RoutedEventArgs e) => StartupService.Set(false);

        // --- UPDATES ---
        // Quiet startup check: only surfaces a notice in the settings drawer,
        // never interrupts the user with dialogs.
        private async Task CheckForUpdatesSilentAsync()
        {
            try
            {
                var info = await UpdateService.CheckLatestAsync();
                if (info != null && info.LatestVersion > UpdateService.CurrentVersion)
                    TxtUpdateStatus.Text = string.Format(_settings.T("UpdateAvailable"), info.LatestVersion);
            }
            catch (Exception ex) { Log.Warn($"Silent update check failed: {ex.Message}"); }
        }

        private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            BtnCheckUpdate.IsEnabled = false;
            TxtUpdateStatus.Text = _settings.T("UpdateChecking");
            try
            {
                var info = await UpdateService.CheckLatestAsync();
                if (info == null)
                {
                    TxtUpdateStatus.Text = _settings.T("UpdateFailed");
                }
                else if (info.LatestVersion <= UpdateService.CurrentVersion)
                {
                    TxtUpdateStatus.Text = _settings.T("UpdateUpToDate");
                }
                else
                {
                    TxtUpdateStatus.Text = string.Format(_settings.T("UpdateAvailable"), info.LatestVersion);

                    var answer = MessageBox.Show(this,
                        string.Format(_settings.T("UpdatePrompt"), info.LatestVersion),
                        _settings.T("UpdateTitle"),
                        MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (answer != MessageBoxResult.Yes) return;

                    if (info.InstallerUrl == null)
                    {
                        // Release has no installer asset — fall back to the releases page.
                        UpdateService.OpenReleasesPage();
                        return;
                    }

                    TxtUpdateStatus.Text = _settings.T("UpdateDownloading");
                    string installerPath = await UpdateService.DownloadInstallerAsync(info.InstallerUrl);
                    UpdateService.RunInstaller(installerPath);
                    Application.Current.Shutdown(); // let the installer replace our files
                }
            }
            catch (Exception ex)
            {
                Log.Error("Update check failed", ex);
                TxtUpdateStatus.Text = _settings.T("UpdateFailed");
            }
            finally { BtnCheckUpdate.IsEnabled = true; }
        }

        // --- HOTKEY CAPTURE ---
        private void UpdateHotkeyDisplay()
        {
            if (TxtHotkeyCapture == null) return;
            var parts = new List<string>();
            if ((_hotkeys.CurrentModifier & 0x0002) != 0) parts.Add("CTRL");
            if ((_hotkeys.CurrentModifier & 0x0001) != 0) parts.Add("ALT");
            if ((_hotkeys.CurrentModifier & 0x0004) != 0) parts.Add("SHIFT");
            Key k = KeyInterop.KeyFromVirtualKey((int)_hotkeys.CurrentKey);
            parts.Add(k.ToString().ToUpperInvariant());
            TxtHotkeyCapture.Text = string.Join(" + ", parts);

            if (TxtQuickPasteHint == null) return;
            string mod = string.Join("+", parts.Take(parts.Count - 1));
            TxtQuickPasteHint.Text = $"Quick paste: {mod}+1 / {mod}+2 / {mod}+3";
        }

        private void TxtHotkeyCapture_GotFocus(object sender, RoutedEventArgs e)
        {
            _isCapturingHotkey    = true;
            TxtHotkeyCapture.Text = "Press keys...";
        }

        private void TxtHotkeyCapture_LostFocus(object sender, RoutedEventArgs e)
        {
            _isCapturingHotkey = false;
            UpdateHotkeyDisplay();
        }

        private void TxtHotkeyCapture_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_isCapturingHotkey) return;

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;

            if (key == Key.Escape)
            {
                _isCapturingHotkey = false;
                UpdateHotkeyDisplay();
                Keyboard.ClearFocus();
                e.Handled = true;
                return;
            }

            if (key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl
                     or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            {
                e.Handled = true;
                return;
            }

            uint modifier = 0;
            if (Keyboard.IsKeyDown(Key.LeftAlt)   || Keyboard.IsKeyDown(Key.RightAlt))   modifier |= 0x0001;
            if (Keyboard.IsKeyDown(Key.LeftCtrl)  || Keyboard.IsKeyDown(Key.RightCtrl))  modifier |= 0x0002;
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) modifier |= 0x0004;

            if (modifier == 0) { e.Handled = true; return; }

            _hotkeys.CurrentModifier = modifier;
            _hotkeys.CurrentKey      = (uint)KeyInterop.VirtualKeyFromKey(key);

            if (_windowHandle != IntPtr.Zero) _hotkeys.Register();
            _settings.Save(_hotkeys.CurrentModifier, _hotkeys.CurrentKey);

            _isCapturingHotkey = false;
            UpdateHotkeyDisplay();
            Keyboard.ClearFocus();
            e.Handled = true;
        }

        // --- UI HELPERS ---
        private void ListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ListBox listBox)
            {
                var sv = FindVisualChild<ScrollViewer>(listBox);
                if (sv != null) { if (e.Delta > 0) sv.LineLeft(); else sv.LineRight(); e.Handled = true; }
            }
        }

        private static T? FindVisualChild<T>(DependencyObject obj) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                var child = VisualTreeHelper.GetChild(obj, i);
                if (child is T t) return t;
                var found = FindVisualChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }

        private void TxtSearch_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down && LstResults.Items.Count > 0)
            {
                LstResults.Focus();
                LstResults.SelectedIndex = 0;
                e.Handled = true;
            }
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // While capturing a new hotkey in settings, let the capture handler
            // own the keys (Escape cancels capture instead of hiding the window).
            if (_isCapturingHotkey) return;

            if (e.Key == Key.Escape) { this.Hide(); return; }
            if (e.Key == Key.Enter && LstResults.SelectedItem is ClipItem item)
            {
                CopyBackAndHide(item.Text);
                e.Handled = true;
            }
        }

        private void ResultItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ChkEdit?.IsChecked == true) return;
            if (sender is Border { DataContext: ClipItem item })
            {
                CopyBackAndHide(item.Text);
                e.Handled = true;
            }
        }

        // Right-click toggles pin on a text entry.
        private void ResultItem_TogglePin(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border { DataContext: ClipItem item })
            {
                _history.TogglePin(item);
                ApplyFilter();
                e.Handled = true;
            }
        }

        // Right-click toggles pin on a screenshot.
        private void Screenshot_TogglePin(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border { DataContext: ScreenshotModel shot })
            {
                _history.TogglePin(shot);
                ApplyFilter();
                e.Handled = true;
            }
        }

        // --- EDIT MODE ---
        private void ChkEdit_Checked(object sender, RoutedEventArgs e)
        {
            LstResults.SelectionMode     = SelectionMode.Multiple;
            LstScreenshots.SelectionMode = SelectionMode.Multiple;
        }

        private void ChkEdit_Unchecked(object sender, RoutedEventArgs e)
        {
            LstResults.SelectedItems.Clear();
            LstScreenshots.SelectedItems.Clear();
            LstResults.SelectionMode     = SelectionMode.Single;
            LstScreenshots.SelectionMode = SelectionMode.Single;
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            var selectedTexts = LstResults.SelectedItems.Cast<ClipItem>().ToList();
            foreach (var item in selectedTexts) _history.Texts.Remove(item);

            var selectedShots = LstScreenshots.SelectedItems.Cast<ScreenshotModel>().ToList();
            foreach (var shot in selectedShots)
            {
                try { File.Delete(shot.Path); }
                catch (Exception ex) { Log.Warn($"Delete screenshot file failed: {ex.Message}"); }
                _history.Screenshots.Remove(shot);
            }

            if (selectedTexts.Count == 0 && selectedShots.Count == 0) return;

            ApplyFilter();
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e) =>
            PnlSettingsDrawer.Visibility = PnlSettingsDrawer.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;

        // --- SHUTDOWN ---
        protected override void OnClosed(EventArgs e)
        {
            SessionStore.Save(_history.Texts, _history.Screenshots);
            _hwndSource?.RemoveHook(HwndHook);
            _clipboard?.Dispose();
            _hotkeys.Dispose();
            _tray?.Dispose();
            base.OnClosed(e);
        }
    }
}
