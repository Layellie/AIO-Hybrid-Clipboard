using AIO_Hybrid_Clipboard.Models;
using AIO_Hybrid_Clipboard.Services;
using AIO_Hybrid_Clipboard.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace AIO_Hybrid_Clipboard
{
    /// <summary>
    /// View layer only: window chrome, HWND message hooks, global hotkeys, tray,
    /// drag &amp; drop and focus handling. All application behavior lives in
    /// <see cref="MainViewModel"/>; gestures here just invoke its commands.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm = new();
        private readonly HotkeyManager _hotkeys = new();

        private IntPtr           _windowHandle;
        private HwndSource?      _hwndSource;
        private ClipboardService? _clipboard;
        private TrayIconService?  _tray;

        private bool  _isCapturingHotkey;
        private Point _dragStartPoint;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = _vm;

            _vm.HideRequested += Hide;
            this.PreviewKeyDown += MainWindow_PreviewKeyDown;

            _hotkeys.CurrentModifier = _vm.HotkeyModifier;
            _hotkeys.CurrentKey      = _vm.HotkeyKey;
            UpdateHotkeyDisplay();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _windowHandle = new WindowInteropHelper(this).Handle;

            this.Left = (SystemParameters.PrimaryScreenWidth - this.Width) / 2;
            this.Top  = 15;

            _hotkeys.Attach(_windowHandle);
            _hotkeys.ToggleRequested     += ToggleLauncher;
            _hotkeys.QuickPasteRequested += _vm.QuickPaste;
            _hotkeys.Register();

            _clipboard = new ClipboardService(_windowHandle);
            _vm.AttachClipboard(_clipboard);
            _clipboard.Start();

            _tray = new TrayIconService(_windowHandle);
            _tray.OpenRequested        += ShowLauncher;
            _tray.ContextMenuRequested += ShowTrayContextMenu;
            _tray.Initialize();

            _hwndSource = HwndSource.FromHwnd(_windowHandle);
            _hwndSource.AddHook(HwndHook);

            _ = _vm.CheckForUpdatesSilentAsync();
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

        private void Screenshot_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            Point pos = e.GetPosition(null);
            if (Math.Abs(pos.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                if (sender is Border { DataContext: ScreenshotModel model })
                    _vm.RunOcrCommand.Execute(model);
            }
        }

        // --- ITEM GESTURES ---
        private void ResultItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_vm.IsEditMode) return;
            if (sender is Border { DataContext: ClipItem item })
            {
                _vm.CopyTextCommand.Execute(item);
                e.Handled = true;
            }
        }

        private void ResultItem_TogglePin(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border { DataContext: ClipItem item })
            {
                _vm.TogglePinTextCommand.Execute(item);
                e.Handled = true;
            }
        }

        private void Screenshot_TogglePin(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border { DataContext: ScreenshotModel shot })
            {
                _vm.TogglePinScreenshotCommand.Execute(shot);
                e.Handled = true;
            }
        }

        // --- TRAY ---
        private void ShowTrayContextMenu()
        {
            Win32Api.SetForegroundWindow(_windowHandle);
            if (this.FindResource("DiscordTrayMenu") is ContextMenu menu)
            {
                menu.DataContext = _vm; // resource tree does not inherit the window's DataContext
                menu.Placement   = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                menu.IsOpen      = true;
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

        // --- HOTKEY CAPTURE (view-only: raw key handling + Win32 registration) ---
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
            _vm.SaveHotkey(_hotkeys.CurrentModifier, _hotkeys.CurrentKey);

            _isCapturingHotkey = false;
            UpdateHotkeyDisplay();
            Keyboard.ClearFocus();
            e.Handled = true;
        }

        // --- KEYBOARD & SCROLL ---
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
                _vm.CopyTextCommand.Execute(item);
                e.Handled = true;
            }
        }

        // --- EDIT MODE (SelectionMode is a view concern) ---
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

        private void BtnSettings_Click(object sender, RoutedEventArgs e) =>
            PnlSettingsDrawer.Visibility = PnlSettingsDrawer.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;

        // --- SHUTDOWN ---
        protected override void OnClosed(EventArgs e)
        {
            _vm.SaveSession();
            _hwndSource?.RemoveHook(HwndHook);
            _clipboard?.Dispose();
            _hotkeys.Dispose();
            _tray?.Dispose();
            base.OnClosed(e);
        }
    }
}
