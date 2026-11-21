using AIO_Hybrid_Clipboard.Models;
using AIO_Hybrid_Clipboard.Services;
using AIO_Hybrid_Clipboard.ViewModels;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace AIO_Hybrid_Clipboard
{
    /// <summary>
    /// Thin WPF view layer: borderless window chrome, multi-monitor DPI positioning,
    /// HWND message hooks (including TaskbarCreated Explorer recovery), SystemEvents resilience
    /// (sleep/resume &amp; display changes), global hotkeys, system tray, drag &amp; drop, and keyboard navigation.
    /// All application logic resides in <see cref="MainViewModel"/>.
    /// </summary>
    public partial class MainWindow : Window
    {
        // Sent by Explorer.exe whenever the taskbar is recreated (e.g., after an explorer.exe restart).
        private static readonly uint WM_TASKBARCREATED = Win32Api.RegisterWindowMessage("TaskbarCreated");

        private readonly MainViewModel _vm      = new();
        private readonly HotkeyManager _hotkeys = new();

        private IntPtr            _windowHandle;
        private IntPtr            _previousForegroundWindow;
        private HwndSource?       _hwndSource;
        private ClipboardService? _clipboard;
        private TrayIconService?  _tray;

        private bool             _isCapturingHotkey;
        private bool             _isDragging;
        private bool             _isCleanedUp;
        private bool             _systemEventsHooked;
        private bool             _suppressDeactivate;
        private Point            _dragStartPoint;
        private ClipItem?        _contextClipItem;
        private ScreenshotModel? _contextScreenshot;

        public bool ShouldStartMinimized => _vm.StartMinimized;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = _vm;

            _vm.HideRequested                  += HideLauncher;
            _vm.PasteToPreviousWindowRequested += PasteIntoPreviousWindow;
            _vm.QuickPasteSettingChanged       += OnQuickPasteSettingChanged;
            _vm.PauseStateChanged              += OnPauseStateChanged;
            _vm.LanguageChanged                += UpdateHotkeyDisplay;

            PreviewKeyDown   += MainWindow_PreviewKeyDown;
            Deactivated      += MainWindow_Deactivated;
            IsVisibleChanged += MainWindow_IsVisibleChanged;

            _hotkeys.CurrentModifier   = _vm.HotkeyModifier;
            _hotkeys.CurrentKey        = _vm.HotkeyKey;
            _hotkeys.QuickPasteEnabled = _vm.QuickPasteEnabled;
            UpdateHotkeyDisplay();
        }

        /// <summary>
        /// Creates the underlying Win32 HWND so hotkeys, clipboard monitoring, and the tray icon
        /// start immediately without rendering or flashing the launcher window on screen.
        /// </summary>
        public void InitializeHidden()
        {
            new WindowInteropHelper(this).EnsureHandle();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            if (_windowHandle != IntPtr.Zero) return;

            _windowHandle = new WindowInteropHelper(this).Handle;
            ApplyDwmWindowHints(_windowHandle);

            _hotkeys.Attach(_windowHandle);
            _hotkeys.ToggleRequested     += ToggleLauncher;
            _hotkeys.QuickPasteRequested += _vm.QuickPaste;
            var regResult = _hotkeys.Register();
            _vm.ReportHotkeyRegistration(regResult, showToastOnSuccess: false);

            _clipboard = new ClipboardService(_windowHandle);
            _vm.AttachClipboard(_clipboard);
            _clipboard.Start();

            _tray = new TrayIconService(_windowHandle);
            _tray.OpenRequested        += ShowLauncher;
            _tray.ContextMenuRequested += ShowTrayContextMenu;
            _tray.Initialize();
            OnPauseStateChanged(_vm.IsPaused);

            _hwndSource = HwndSource.FromHwnd(_windowHandle);
            _hwndSource?.AddHook(HwndHook);

            HookSystemEvents();

            // Defer heavy disk I/O (session thumbnails, orphan cleanup, quiet update check)
            // until after the tray icon and listeners are live.
            _ = _vm.InitializeAsync();
        }

        // --- 24/7 SYSTEM EVENTS (SLEEP/RESUME & DISPLAY CHANGES) ---
        private void HookSystemEvents()
        {
            if (_systemEventsHooked) return;
            SystemEvents.PowerModeChanged       += OnPowerModeChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            _systemEventsHooked = true;
        }

        private void UnhookSystemEvents()
        {
            if (!_systemEventsHooked) return;
            SystemEvents.PowerModeChanged       -= OnPowerModeChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _systemEventsHooked = false;
        }

        private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.Resume) return;

            Dispatcher.BeginInvoke(() =>
            {
                if (_isCleanedUp) return;
                Log.Info("System resumed from sleep — re-verifying listeners, hotkeys, and tray icon.");
                _clipboard?.EnsureListening();
                _hotkeys.Register();
                _tray?.ReRegister();
            });
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_isCleanedUp) return;
                if (Visibility == Visibility.Visible)
                    PositionOnActiveMonitor();
            });
        }

        // --- WND PROC (100% EVENT-DRIVEN, ZERO POLLING) ---
        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (WM_TASKBARCREATED != 0 && (uint)msg == WM_TASKBARCREATED)
            {
                Log.Info("TaskbarCreated received (Explorer restarted) — re-registering tray icon.");
                _tray?.ReRegister();
                handled = true;
                return IntPtr.Zero;
            }

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

        // --- MULTI-MONITOR + DPI + TASKBAR-AWARE POSITIONING ---
        private void PositionOnActiveMonitor()
        {
            double workLeft   = SystemParameters.WorkArea.Left;
            double workTop    = SystemParameters.WorkArea.Top;
            double workWidth  = SystemParameters.WorkArea.Width;
            double workHeight = SystemParameters.WorkArea.Height;

            if (Win32Api.GetCursorPos(out var cursor))
            {
                IntPtr hMonitor = Win32Api.MonitorFromPoint(cursor, Win32Api.MONITOR_DEFAULTTONEAREST);
                var info = new Win32Api.MONITORINFO { cbSize = Marshal.SizeOf<Win32Api.MONITORINFO>() };
                if (hMonitor != IntPtr.Zero && Win32Api.GetMonitorInfo(hMonitor, ref info))
                {
                    var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
                                    ?? Matrix.Identity;

                    Point topLeft     = transform.Transform(new Point(info.rcWork.Left, info.rcWork.Top));
                    Point bottomRight = transform.Transform(new Point(info.rcWork.Right, info.rcWork.Bottom));

                    workLeft   = topLeft.X;
                    workTop    = topLeft.Y;
                    workWidth  = Math.Max(300, bottomRight.X - topLeft.X);
                    workHeight = Math.Max(300, bottomRight.Y - topLeft.Y);
                }
            }

            double winWidth  = ActualWidth  > 0 ? ActualWidth  : Width;
            double winHeight = ActualHeight > 0 ? ActualHeight : Height;

            double targetLeft = workLeft + (workWidth - winWidth) / 2.0;
            double targetTop  = workTop + Math.Min(36.0, Math.Max(8.0, (workHeight - winHeight) / 6.0));

            Left = Math.Clamp(targetLeft, workLeft, Math.Max(workLeft, workLeft + workWidth - winWidth));
            Top  = Math.Clamp(targetTop,  workTop,  Math.Max(workTop,  workTop + workHeight - winHeight));
        }

        // --- LAUNCHER SHOW / HIDE / AUTO-PASTE ---
        private void ToggleLauncher()
        {
            if (Visibility == Visibility.Visible) HideLauncher();
            else ShowLauncher();
        }

        /// <summary>Shows, positions on the active monitor, and focuses the search bar.</summary>
        public void ShowLauncher()
        {
            IntPtr fg = Win32Api.GetForegroundWindow();
            if (fg != IntPtr.Zero && fg != _windowHandle)
                _previousForegroundWindow = fg;

            PositionOnActiveMonitor();
            Show();
            PositionOnActiveMonitor();

            if (_windowHandle != IntPtr.Zero)
                Win32Api.SetForegroundWindow(_windowHandle);

            Activate();
            TxtSearch.Focus();
            TxtSearch.SelectAll();
        }

        private void HideLauncher()
        {
            if (Visibility == Visibility.Visible)
                Hide();
            _vm.ReleaseTransientResources();
        }

        private void MainWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is false)
                _vm.ReleaseTransientResources();
        }

        private async void PasteIntoPreviousWindow()
        {
            IntPtr target = _previousForegroundWindow;
            if (target == IntPtr.Zero || target == _windowHandle)
                return;

            try
            {
                Win32Api.SetForegroundWindow(target);
                await HotkeyManager.SimulatePasteAsync(70);
            }
            catch (Exception ex)
            {
                Log.Warn($"Auto-paste into previous window failed: {ex.Message}");
            }
        }

        private static void ApplyDwmWindowHints(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                int dark = 1;
                Win32Api.DwmSetWindowAttribute(hwnd, Win32Api.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

                // Windows 11 (build 22000+) rounded corner hint; harmless no-op on Windows 10.
                int round = Win32Api.DWMWCP_ROUND;
                Win32Api.DwmSetWindowAttribute(hwnd, Win32Api.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            }
            catch
            {
                // Older Windows versions without DWM attributes simply use the WPF rounded chrome.
            }
        }

        private void MainWindow_Deactivated(object? sender, EventArgs e)
        {
            if (_isDragging || _isCapturingHotkey || _suppressDeactivate) return;
            HideLauncher();
        }

        private void RunWithDeactivateSuppressed(Action action)
        {
            _suppressDeactivate = true;
            try { action(); }
            finally { _suppressDeactivate = false; }
        }

        private void HeaderBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed &&
                !IsClickInsideInteractiveControl(e.OriginalSource as DependencyObject))
            {
                DragMove();
            }
        }

        // --- ON-DEMAND SCREENSHOT HOVER PREVIEW (RELEASED ON CLOSE/HIDE) ---
        private void Screenshot_ToolTipOpening(object sender, ToolTipEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: ScreenshotModel model } &&
                model.PreviewImage == null &&
                !string.IsNullOrEmpty(model.Path))
            {
                model.PreviewImage = ClipboardService.LoadHoverPreview(model.Path, model.PixelWidth);
            }
        }

        private void Screenshot_ToolTipClosing(object sender, ToolTipEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: ScreenshotModel model })
                model.ReleasePreview();
        }

        // --- DRAG & DROP (SCREENSHOTS) ---
        private void Screenshot_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsClickInsideButton(e.OriginalSource as DependencyObject)) return;
            _dragStartPoint = e.GetPosition(null);
        }

        private void Screenshot_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            if (IsClickInsideButton(e.OriginalSource as DependencyObject)) return;

            Point pos = e.GetPosition(null);
            if (Math.Abs(pos.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(pos.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                if (sender is FrameworkElement { DataContext: ScreenshotModel model } element &&
                    !string.IsNullOrEmpty(model.Path))
                {
                    _isDragging = true;
                    try
                    {
                        DragDrop.DoDragDrop(
                            element,
                            new DataObject(DataFormats.FileDrop, new[] { model.Path }),
                            DragDropEffects.Copy);
                    }
                    finally
                    {
                        _isDragging = false;
                    }
                }
            }
        }

        private void Screenshot_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_vm.IsEditMode || IsClickInsideButton(e.OriginalSource as DependencyObject)) return;

            Point pos = e.GetPosition(null);
            if (Math.Abs(pos.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                if (sender is FrameworkElement { DataContext: ScreenshotModel model })
                    _vm.RunOcrCommand.Execute(model);
            }
        }

        // --- ITEM GESTURES ---
        private void ResultItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_vm.IsEditMode || IsClickInsideButton(e.OriginalSource as DependencyObject)) return;

            if (sender is FrameworkElement { DataContext: ClipItem item })
            {
                bool shiftHeld = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                if (shiftHeld) _vm.CopyAndPasteCommand.Execute(item);
                else           _vm.CopyTextCommand.Execute(item);
                e.Handled = true;
            }
        }

        private void ResultItem_TogglePin(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: ClipItem item })
            {
                _contextClipItem = item;
                if (FindResource("TextItemContextMenu") is ContextMenu menu)
                {
                    menu.DataContext = _vm;
                    menu.Placement   = PlacementMode.MousePoint;
                    menu.IsOpen      = true;
                }
                e.Handled = true;
            }
        }

        private void Screenshot_TogglePin(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: ScreenshotModel shot })
            {
                _contextScreenshot = shot;
                if (FindResource("ShotItemContextMenu") is ContextMenu menu)
                {
                    menu.DataContext = _vm;
                    menu.Placement   = PlacementMode.MousePoint;
                    menu.IsOpen      = true;
                }
                e.Handled = true;
            }
        }

        // --- ITEM CONTEXT MENU ACTIONS ---
        private void TextMenu_Pin_Click(object sender, RoutedEventArgs e)
        {
            if (_contextClipItem != null) _vm.TogglePinTextCommand.Execute(_contextClipItem);
        }

        private void TextMenu_Alias_Click(object sender, RoutedEventArgs e)
        {
            if (_contextClipItem != null)
                RunWithDeactivateSuppressed(() => _vm.EditAlias(_contextClipItem));
        }

        private void TextMenu_Transform_Click(object sender, RoutedEventArgs e)
        {
            if (_contextClipItem != null &&
                sender is MenuItem { Tag: string tag } &&
                Enum.TryParse<TextTransformKind>(tag, out var kind))
            {
                _vm.TransformClip(_contextClipItem, kind);
            }
        }

        private void TextMenu_Delete_Click(object sender, RoutedEventArgs e)
        {
            if (_contextClipItem != null) _vm.DeleteSingleTextCommand.Execute(_contextClipItem);
        }

        private void ShotMenu_Pin_Click(object sender, RoutedEventArgs e)
        {
            if (_contextScreenshot != null) _vm.TogglePinScreenshotCommand.Execute(_contextScreenshot);
        }

        private void ShotMenu_RunOcr_Click(object sender, RoutedEventArgs e)
        {
            if (_contextScreenshot != null) _vm.RunOcrCommand.Execute(_contextScreenshot);
        }

        private void ShotMenu_EditOcr_Click(object sender, RoutedEventArgs e)
        {
            if (_contextScreenshot != null)
                RunWithDeactivateSuppressed(() => _vm.EditOcrText(_contextScreenshot));
        }

        private void ShotMenu_CopyImage_Click(object sender, RoutedEventArgs e)
        {
            if (_contextScreenshot != null) _vm.CopyScreenshotCommand.Execute(_contextScreenshot);
        }

        private void ShotMenu_SavePng_Click(object sender, RoutedEventArgs e)
        {
            if (_contextScreenshot != null)
                RunWithDeactivateSuppressed(() => _vm.SaveScreenshotAs(_contextScreenshot));
        }

        private void ShotMenu_Delete_Click(object sender, RoutedEventArgs e)
        {
            if (_contextScreenshot != null) _vm.DeleteSingleScreenshotCommand.Execute(_contextScreenshot);
        }

        private static bool IsClickInsideButton(DependencyObject? source)
        {
            while (source != null)
            {
                if (source is ButtonBase) return true;
                source = VisualTreeHelper.GetParent(source);
            }
            return false;
        }

        private static bool IsClickInsideInteractiveControl(DependencyObject? source)
        {
            while (source != null)
            {
                if (source is ButtonBase or TextBoxBase) return true;
                source = VisualTreeHelper.GetParent(source);
            }
            return false;
        }

        // --- TRAY ---
        private void ShowTrayContextMenu()
        {
            Win32Api.SetForegroundWindow(_windowHandle);
            if (FindResource("TrayContextMenu") is ContextMenu menu)
            {
                menu.DataContext = _vm;
                menu.Placement   = PlacementMode.MousePoint;
                menu.IsOpen      = true;
            }
        }

        private void TrayMenu_Open_Click(object sender, RoutedEventArgs e) => ShowLauncher();

        private void TrayMenu_Settings_Click(object sender, RoutedEventArgs e)
        {
            PnlSettingsDrawer.Visibility = Visibility.Visible;
            AdjustHeightForDrawer();
            ShowLauncher();
        }

        private void TrayMenu_Close_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        private void OnPauseStateChanged(bool isPaused)
        {
            _tray?.UpdateTooltip(isPaused
                ? "AIO Hybrid Clipboard (Paused)"
                : "AIO Hybrid Clipboard");
        }

        private void OnQuickPasteSettingChanged(bool enabled)
        {
            _hotkeys.QuickPasteEnabled = enabled;
            if (_windowHandle != IntPtr.Zero)
            {
                var result = _hotkeys.Register();
                _vm.ReportHotkeyRegistration(result, showToastOnSuccess: false);
            }
            UpdateHotkeyDisplay();
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
            if (!_vm.QuickPasteEnabled)
            {
                TxtQuickPasteHint.Text = _vm.Settings.T("QuickPasteDisabledHint");
            }
            else
            {
                string mod = string.Join("+", parts.Take(Math.Max(1, parts.Count - 1)));
                TxtQuickPasteHint.Text = string.Format(_vm.Settings.T("QuickPasteHint"), mod);
            }
        }

        private void TxtHotkeyCapture_GotFocus(object sender, RoutedEventArgs e)
        {
            _isCapturingHotkey    = true;
            TxtHotkeyCapture.Text = _vm.Settings.T("HotkeyPressKeys");
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

            if (_windowHandle != IntPtr.Zero)
            {
                var regResult = _hotkeys.Register();
                _vm.ReportHotkeyRegistration(regResult, showToastOnSuccess: true);
            }
            _vm.SaveHotkey(_hotkeys.CurrentModifier, _hotkeys.CurrentKey);

            _isCapturingHotkey = false;
            UpdateHotkeyDisplay();
            Keyboard.ClearFocus();
            e.Handled = true;
        }

        // --- FULL KEYBOARD NAVIGATION ---
        private void TxtSearch_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down)
            {
                if (LstResults.Items.Count > 0)
                {
                    FocusListItem(LstResults, 0);
                    e.Handled = true;
                }
                else if (LstScreenshots.Items.Count > 0)
                {
                    FocusListItem(LstScreenshots, 0);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Enter && LstResults.Items.Count > 0 && LstResults.Items[0] is ClipItem first)
            {
                bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                if (shift) _vm.CopyAndPasteCommand.Execute(first);
                else       _vm.CopyTextCommand.Execute(first);
                e.Handled = true;
            }
        }

        private void ListBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not ListBox list) return;

            if (e.Key == Key.Up && list.SelectedIndex == 0)
            {
                TxtSearch.Focus();
                e.Handled = true;
            }
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_isCapturingHotkey) return;

            bool ctrl  = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift)   != 0;

            if (ctrl && e.Key == Key.F)
            {
                TxtSearch.Focus();
                TxtSearch.SelectAll();
                e.Handled = true;
                return;
            }

            if (ctrl && e.Key == Key.P)
            {
                if (LstScreenshots.IsKeyboardFocusWithin && LstScreenshots.SelectedItem is ScreenshotModel shot)
                    _vm.TogglePinScreenshotCommand.Execute(shot);
                else if (LstResults.SelectedItem is ClipItem item)
                    _vm.TogglePinTextCommand.Execute(item);
                else if (LstScreenshots.SelectedItem is ScreenshotModel fallbackShot)
                    _vm.TogglePinScreenshotCommand.Execute(fallbackShot);
                e.Handled = true;
                return;
            }

            // F2: set alias on selected text clip, or edit OCR text on selected screenshot
            if (e.Key == Key.F2)
            {
                if (LstScreenshots.IsKeyboardFocusWithin && LstScreenshots.SelectedItem is ScreenshotModel shot)
                    RunWithDeactivateSuppressed(() => _vm.EditOcrText(shot));
                else if (LstResults.SelectedItem is ClipItem item)
                    RunWithDeactivateSuppressed(() => _vm.EditAlias(item));
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Tab && PnlSettingsDrawer.Visibility != Visibility.Visible)
            {
                if (TxtSearch.IsKeyboardFocusWithin)
                {
                    if (shift) FocusListOrFallback(LstScreenshots, LstResults);
                    else       FocusListOrFallback(LstResults, LstScreenshots);
                    e.Handled = true;
                    return;
                }
                if (LstResults.IsKeyboardFocusWithin)
                {
                    if (shift) TxtSearch.Focus();
                    else if (LstScreenshots.Items.Count > 0) FocusListItem(LstScreenshots, Math.Max(0, LstScreenshots.SelectedIndex));
                    else TxtSearch.Focus();
                    e.Handled = true;
                    return;
                }
                if (LstScreenshots.IsKeyboardFocusWithin)
                {
                    if (shift && LstResults.Items.Count > 0) FocusListItem(LstResults, Math.Max(0, LstResults.SelectedIndex));
                    else TxtSearch.Focus();
                    e.Handled = true;
                    return;
                }
            }

            if (e.Key == Key.Escape)
            {
                if (PnlSettingsDrawer.Visibility == Visibility.Visible)
                {
                    PnlSettingsDrawer.Visibility = Visibility.Collapsed;
                    AdjustHeightForDrawer();
                }
                else if (_vm.HasSearchQuery)
                {
                    _vm.SearchQuery = string.Empty;
                    TxtSearch.Focus();
                }
                else
                {
                    HideLauncher();
                }
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter && !TxtSearch.IsKeyboardFocusWithin)
            {
                if (LstScreenshots.IsKeyboardFocusWithin && LstScreenshots.SelectedItem is ScreenshotModel shot)
                {
                    if (shift) _vm.CopyScreenshotCommand.Execute(shot);
                    else       _vm.RunOcrCommand.Execute(shot);
                    e.Handled = true;
                    return;
                }

                if (LstResults.SelectedItem is ClipItem item)
                {
                    if (shift) _vm.CopyAndPasteCommand.Execute(item);
                    else       _vm.CopyTextCommand.Execute(item);
                    e.Handled = true;
                    return;
                }
            }

            if (e.Key == Key.Delete && !TxtSearch.IsKeyboardFocusWithin)
            {
                if (_vm.IsEditMode)
                {
                    _vm.DeleteItems(LstResults.SelectedItems, LstScreenshots.SelectedItems);
                }
                else if (LstScreenshots.IsKeyboardFocusWithin && LstScreenshots.SelectedItem is ScreenshotModel shot)
                {
                    _vm.DeleteSingleScreenshotCommand.Execute(shot);
                }
                else if (LstResults.SelectedItem is ClipItem item)
                {
                    _vm.DeleteSingleTextCommand.Execute(item);
                }
                e.Handled = true;
            }
        }

        private void FocusListOrFallback(ListBox primary, ListBox secondary)
        {
            if (primary.Items.Count > 0)
                FocusListItem(primary, Math.Max(0, primary.SelectedIndex));
            else if (secondary.Items.Count > 0)
                FocusListItem(secondary, Math.Max(0, secondary.SelectedIndex));
        }

        private static void FocusListItem(ListBox list, int index)
        {
            if (list.Items.Count == 0) return;
            index = Math.Clamp(index, 0, list.Items.Count - 1);
            list.SelectedIndex = index;
            list.ScrollIntoView(list.Items[index]);
            list.UpdateLayout();

            if (list.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem container)
                container.Focus();
            else
                list.Focus();
        }

        // --- EDIT MODE & SETTINGS DRAWER ---
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

        private void BtnDelete_Click(object sender, RoutedEventArgs e) =>
            _vm.DeleteItems(LstResults.SelectedItems, LstScreenshots.SelectedItems);

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            PnlSettingsDrawer.Visibility = PnlSettingsDrawer.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
            AdjustHeightForDrawer();
        }

        private void AdjustHeightForDrawer()
        {
            Height = PnlSettingsDrawer.Visibility == Visibility.Visible ? 720 : 560;
            if (Visibility == Visibility.Visible)
                PositionOnActiveMonitor();
        }

        // --- DETERMINISTIC SHUTDOWN ---
        internal void ShutdownCleanup()
        {
            if (_isCleanedUp) return;
            _isCleanedUp = true;

            _vm.CancelBackgroundWork();
            _vm.ReleaseTransientResources();
            UnhookSystemEvents();

            _vm.SaveSession();

            _hwndSource?.RemoveHook(HwndHook);
            _hwndSource = null;

            _clipboard?.Dispose();
            _clipboard = null;

            _hotkeys.Dispose();

            _tray?.Dispose();
            _tray = null;
        }

        protected override void OnClosed(EventArgs e)
        {
            ShutdownCleanup();
            base.OnClosed(e);
        }
    }
}
