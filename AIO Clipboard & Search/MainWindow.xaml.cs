using AIO_Hybrid_Clipboard.Models;
using AIO_Hybrid_Clipboard.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AIO_Hybrid_Clipboard
{
    public partial class MainWindow : Window
    {
        private const int ClipboardLimit = 15;

        private readonly ObservableCollection<string>        ClipboardHistory  = new();
        private readonly ObservableCollection<ScreenshotModel> ScreenshotHistory = new();

        private IntPtr       _windowHandle;
        private HwndSource?  _hwndSource;

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
            foreach (var t in texts) ClipboardHistory.Add(t);
            foreach (var s in shots) ScreenshotHistory.Add(s);
            if (ClipboardHistory.Count == 0) ClipboardHistory.Add(_settings.T("InitialMsg"));

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

            LstResults.ItemsSource     = ClipboardHistory;
            LstScreenshots.ItemsSource = ScreenshotHistory;
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
        private void OnTextCaptured(string text)
        {
            if (ClipboardHistory.Contains(text)) ClipboardHistory.Remove(text);
            ClipboardHistory.Insert(0, text);
            while (ClipboardHistory.Count > ClipboardLimit) ClipboardHistory.RemoveAt(ClipboardHistory.Count - 1);
        }

        private async void OnImageCaptured(ScreenshotModel model, byte[] pixels, int width, int height, int stride)
        {
            if (ScreenshotHistory.Count >= ClipboardLimit)
            {
                try { File.Delete(ScreenshotHistory.Last().Path); } catch { }
                ScreenshotHistory.RemoveAt(ScreenshotHistory.Count - 1);
            }
            ScreenshotHistory.Insert(0, model);

            try
            {
                string ocrResult = await OcrService.RecognizeAsync(pixels, width, height, stride);
                if (!string.IsNullOrEmpty(ocrResult))
                    model.OcrText = ocrResult;
            }
            catch { }
        }

        // --- SEARCH ---
        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtSearch == null || LstResults == null || LstScreenshots == null) return;
            string query = TxtSearch.Text.Trim();

            if (string.IsNullOrEmpty(query))
            {
                LstResults.ItemsSource     = ClipboardHistory;
                LstScreenshots.ItemsSource = ScreenshotHistory;
                return;
            }

            LstResults.ItemsSource = ClipboardHistory
                .Where(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            LstScreenshots.ItemsSource = ScreenshotHistory
                .Where(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            s.OcrText.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
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
                if (!string.IsNullOrEmpty(ocrResult))
                {
                    model.OcrText = ocrResult;
                    if (ClipboardHistory.Contains(ocrResult)) ClipboardHistory.Remove(ocrResult);
                    ClipboardHistory.Insert(0, ocrResult);
                    while (ClipboardHistory.Count > ClipboardLimit) ClipboardHistory.RemoveAt(ClipboardHistory.Count - 1);
                    LstResults.ItemsSource = ClipboardHistory;
                    Clipboard.SetText(ocrResult);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(_settings.T("OcrException") + ex.Message, _settings.T("OcrError"));
            }
        }

        // --- COPY & HIDE ---
        private void CopyBackAndHide(string? text)
        {
            if (string.IsNullOrEmpty(text) || text.StartsWith("[OCR:")) return;
            try { _clipboard?.SetClipboardSilent(text); } catch { }
            if (ChkHideOnCopy.IsChecked == true) this.Hide();
        }

        // --- QUICK PASTE ---
        private void QuickPaste(int index)
        {
            if (ClipboardHistory.Count <= index) return;
            string text = ClipboardHistory[index];
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                _clipboard?.SetClipboardSilent(text);
                HotkeyManager.SimulatePaste();
            }
            catch { }
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

        private void ShowLauncher()
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

            if (this.Resources["DiscordTrayMenu"] is ContextMenu trayMenu)
            {
                if (trayMenu.Items[0] is MenuItem open) open.Header = _settings.T("TrayOpen");
                if (trayMenu.Items[1] is MenuItem exit) exit.Header = _settings.T("TrayExit");
            }
        }

        private void PopulateSettingsUI()
        {
            CmbLanguage.SelectedIndex = _settings.CurrentLanguage == SettingsService.AppLanguage.Turkish ? 1 : 0;
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

        private T? FindVisualChild<T>(DependencyObject obj) where T : DependencyObject
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
            if (e.Key == Key.Escape) this.Hide();
            if (e.Key == Key.Enter && LstResults.SelectedItem != null)
            {
                CopyBackAndHide(LstResults.SelectedItem?.ToString());
                e.Handled = true;
            }
        }

        private void ResultItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ChkEdit?.IsChecked == true) return;
            if (sender is Border { DataContext: { } ctx })
            {
                CopyBackAndHide(ctx.ToString());
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
            var selectedTexts = LstResults.SelectedItems.Cast<string>().ToList();
            foreach (var text in selectedTexts) ClipboardHistory.Remove(text);

            var selectedShots = LstScreenshots.SelectedItems.Cast<ScreenshotModel>().ToList();
            foreach (var shot in selectedShots)
            {
                try { File.Delete(shot.Path); } catch { }
                ScreenshotHistory.Remove(shot);
            }

            if (selectedTexts.Count == 0 && selectedShots.Count == 0) return;

            string query = TxtSearch.Text.Trim();
            if (!string.IsNullOrEmpty(query))
            {
                LstResults.ItemsSource = ClipboardHistory
                    .Where(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
                LstScreenshots.ItemsSource = ScreenshotHistory
                    .Where(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                s.OcrText.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            }
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e) =>
            PnlSettingsDrawer.Visibility = PnlSettingsDrawer.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;

        // --- SHUTDOWN ---
        protected override void OnClosed(EventArgs e)
        {
            SessionStore.Save(ClipboardHistory, ScreenshotHistory);
            _hwndSource?.RemoveHook(HwndHook);
            _clipboard?.Dispose();
            _hotkeys.Dispose();
            _tray?.Dispose();
            base.OnClosed(e);
        }
    }
}
