using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
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
        // --- C++ IMPORTS (P/INVOKE) ---
        [DllImport("AIO_SearchEngine.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern void ProcessImageOCR(byte[] pixelData, int width, int height, int stride, StringBuilder outText, int maxLen);

        // --- WIN32 SYSTEM TRAY API ---
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct NOTIFYICONDATA
        {
            public int cbSize; public IntPtr hWnd; public int uID; public int uFlags;
            public int uCallbackMessage; public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Auto)] private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        private const uint IMAGE_ICON = 1;
        private const uint LR_LOADFROMFILE = 0x00000010;
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr hIcon);

        // --- SEND INPUT (QUICK PASTE) ---
        [DllImport("user32.dll")]
        private static extern uint SendInput(uint nInputs, [MarshalAs(UnmanagedType.LPArray), In] INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT { public uint type; public InputUnion U; }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

        // --- GLOBAL HOTKEY & CLIPBOARD API ---
        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool AddClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        private const int NIM_ADD = 0x00000000; private const int NIM_DELETE = 0x00000002;
        private const int NIF_MESSAGE = 0x00000001; private const int NIF_ICON = 0x00000002; private const int NIF_TIP = 0x00000004;
        private const int WM_TRAYICON = 0x0400 + 100; private const int WM_LBUTTONUP = 0x0202; private const int WM_RBUTTONUP = 0x0205;
        private static readonly IntPtr IDI_APPLICATION = new IntPtr(32512);
        private const int WM_HOTKEY = 0x0312; private const int WM_CLIPBOARDUPDATE = 0x031D;
        private const int HOTKEY_ID = 9000;
        private const int QUICKPASTE_ID_1 = 9001;
        private const int QUICKPASTE_ID_2 = 9002;
        private const int QUICKPASTE_ID_3 = 9003;

        // --- LOCALIZATION ---
        private enum AppLanguage { English, Turkish }
        private AppLanguage _currentLanguage = AppLanguage.English;

        private static readonly Dictionary<AppLanguage, Dictionary<string, string>> _strings = new()
        {
            [AppLanguage.English] = new()
            {
                ["HideOnCopy"]        = "Automatically hide menu on new selection",
                ["StartWithWindows"]  = "Start with Windows",
                ["ShortcutKey"]       = "Shortcut Key: ",
                ["Language"]          = "Language: ",
                ["TrayOpen"]          = "Open",
                ["TrayExit"]          = "Exit",
                ["InitialMsg"]        = "AIO OCR Clipboard Active! Capture images to process... 🚀",
                ["OcrError"]          = "OCR Error",
                ["OcrException"]      = "OCR Processing Exception: ",
            },
            [AppLanguage.Turkish] = new()
            {
                ["HideOnCopy"]        = "Seçimde menüyü otomatik gizle",
                ["StartWithWindows"]  = "Windows ile başlat",
                ["ShortcutKey"]       = "Kısayol Tuşu: ",
                ["Language"]          = "Dil: ",
                ["TrayOpen"]          = "Aç",
                ["TrayExit"]          = "Çıkış",
                ["InitialMsg"]        = "AIO OCR Pano Aktif! İşlemek için görüntü yakalayın... 🚀",
                ["OcrError"]          = "OCR Hatası",
                ["OcrException"]      = "OCR İşleme Hatası: ",
            }
        };

        private string T(string key) =>
            _strings[_currentLanguage].TryGetValue(key, out var val) ? val : key;

        private void LoadSettings()
        {
            try
            {
                using var rk = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\AIO_ClipboardSearch");
                if (rk == null) return;
                if (rk.GetValue("Language") is string lang && Enum.TryParse<AppLanguage>(lang, out var parsed))
                    _currentLanguage = parsed;
                if (rk.GetValue("HotkeyModifier") is int mod) CurrentModifier = (uint)mod;
                if (rk.GetValue("HotkeyKey") is int key) CurrentKey = (uint)key;
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                using var rk = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\AIO_ClipboardSearch");
                rk?.SetValue("Language", _currentLanguage.ToString());
                rk?.SetValue("HotkeyModifier", (int)CurrentModifier, RegistryValueKind.DWord);
                rk?.SetValue("HotkeyKey", (int)CurrentKey, RegistryValueKind.DWord);
            }
            catch { }
        }

        private void ApplyLanguage()
        {
            ChkHideOnCopy.Content       = T("HideOnCopy");
            ChkStartWithWindows.Content  = T("StartWithWindows");
            TxtShortcutLabel.Text        = T("ShortcutKey");
            TxtLanguageLabel.Text        = T("Language");

            if (this.Resources["DiscordTrayMenu"] is ContextMenu trayMenu)
            {
                if (trayMenu.Items[0] is MenuItem open) open.Header = T("TrayOpen");
                if (trayMenu.Items[1] is MenuItem exit) exit.Header = T("TrayExit");
            }
        }

        private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _currentLanguage = CmbLanguage.SelectedIndex == 1 ? AppLanguage.Turkish : AppLanguage.English;
            ApplyLanguage();
            SaveSettings();
        }

        // --- DATA COLLECTIONS ---
        private ObservableCollection<string> ClipboardHistory = new();
        private ObservableCollection<ScreenshotModel> ScreenshotHistory = new ObservableCollection<ScreenshotModel>();

        private uint CurrentModifier = 0x0001; private uint CurrentKey = 0x20; int ClipboardLimit = 15;
        private IntPtr _windowHandle;
        private HwndSource? _hwndSource;
        private DateTime _lastScreenshotTime = DateTime.MinValue;
        private NOTIFYICONDATA _nid;
        private Point _dragStartPoint;

        public MainWindow()
        {
            InitializeComponent();
            this.PreviewKeyDown += MainWindow_PreviewKeyDown;
            LoadSettings();
            PopulateSettingsUI();
            CheckRegistryStartup();

            LoadSession();
            if (ClipboardHistory.Count == 0)
                ClipboardHistory.Add(T("InitialMsg"));

            TxtSearch.TextChanged += TxtSearch_TextChanged;

            // Wire after SelectedIndex is set to avoid firing during init
            CmbLanguage.SelectionChanged += CmbLanguage_SelectionChanged;
            ApplyLanguage();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _windowHandle = new WindowInteropHelper(this).Handle;

            this.Left = (SystemParameters.PrimaryScreenWidth - this.Width) / 2;
            this.Top = 15;

            BindGlobalHotkey();
            AddClipboardFormatListener(_windowHandle);
            InitializeTrayIcon();

            _hwndSource = HwndSource.FromHwnd(_windowHandle);
            _hwndSource.AddHook(HwndHook);

            LstResults.ItemsSource = ClipboardHistory;
            LstScreenshots.ItemsSource = ScreenshotHistory;
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtSearch == null || LstResults == null || LstScreenshots == null) return;
            string query = TxtSearch.Text.Trim();

            if (string.IsNullOrEmpty(query))
            {
                LstResults.ItemsSource = ClipboardHistory;
                LstScreenshots.ItemsSource = ScreenshotHistory;
                return;
            }

            LstResults.ItemsSource = ClipboardHistory.Where(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            LstScreenshots.ItemsSource = ScreenshotHistory.Where(s =>
                s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.OcrText.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        // --- MOUSE GESTURE HANDLING (DRAG VS CLICK) ---
        private void Screenshot_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        private void Screenshot_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                Point currentPos = e.GetPosition(null);
                if (Math.Abs(currentPos.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(currentPos.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    var border = sender as Border;
                    if (border != null && border.DataContext is ScreenshotModel model)
                    {
                        DragDrop.DoDragDrop(border, new DataObject(DataFormats.FileDrop, new string[] { model.Path }), DragDropEffects.Copy);
                    }
                }
            }
        }

        private async void Screenshot_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (ChkEdit?.IsChecked == true) return; // Edit mode: ListBox handles multi-selection

            Point currentPos = e.GetPosition(null);
            if (Math.Abs(currentPos.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(currentPos.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                var border = sender as Border;
                if (border != null && border.DataContext is ScreenshotModel model)
                {
                    await ExecuteOcrOnScreenshot(model);
                }
            }
        }

        // --- ASYNCHRONOUS OCR EXECUTION LAYER ---
        private async Task ExecuteOcrOnScreenshot(ScreenshotModel model)
        {
            if (model.Image == null) return;

            int width = model.Image.PixelWidth;
            int height = model.Image.PixelHeight;
            int stride = width * ((model.Image.Format.BitsPerPixel + 7) / 8);
            byte[] pixels = new byte[height * stride];
            model.Image.CopyPixels(pixels, stride, 0);

            await Task.Run(() =>
            {
                try
                {
                    StringBuilder sbText = new StringBuilder(8192);
                    ProcessImageOCR(pixels, width, height, stride, sbText, sbText.Capacity);
                    string ocrResult = sbText.ToString().Trim();

                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (!string.IsNullOrEmpty(ocrResult))
                        {
                            model.OcrText = ocrResult;

                            if (ClipboardHistory.Contains(ocrResult)) ClipboardHistory.Remove(ocrResult);
                            ClipboardHistory.Insert(0, ocrResult);

                            while (ClipboardHistory.Count > ClipboardLimit) ClipboardHistory.RemoveAt(ClipboardHistory.Count - 1);

                            LstResults.ItemsSource = ClipboardHistory;

                            Clipboard.SetText(ocrResult);
                        }
                    }));
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() => {
                        MessageBox.Show(T("OcrException") + ex.Message, T("OcrError"));
                    }));
                }
            });
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_TRAYICON)
            {
                int clickType = lParam.ToInt32();
                if (clickType == WM_RBUTTONUP) { ShowTrayContextMenu(); handled = true; }
                else if (clickType == WM_LBUTTONUP) { ShowLauncher(); handled = true; }
            }
            else if (msg == WM_HOTKEY)
            {
                int hotkeyId = wParam.ToInt32();
                if (hotkeyId == HOTKEY_ID) { ToggleLauncher(); handled = true; }
                else if (hotkeyId == QUICKPASTE_ID_1) { QuickPaste(0); handled = true; }
                else if (hotkeyId == QUICKPASTE_ID_2) { QuickPaste(1); handled = true; }
                else if (hotkeyId == QUICKPASTE_ID_3) { QuickPaste(2); handled = true; }
            }
            else if (msg == WM_CLIPBOARDUPDATE)
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        string newText = Clipboard.GetText().Trim();
                        if (!string.IsNullOrEmpty(newText) && !newText.StartsWith("[OCR:"))
                        {
                            if (ClipboardHistory.Contains(newText)) ClipboardHistory.Remove(newText);
                            ClipboardHistory.Insert(0, newText);
                            while (ClipboardHistory.Count > ClipboardLimit) ClipboardHistory.RemoveAt(ClipboardHistory.Count - 1);
                        }
                    }
                    else if (Clipboard.ContainsImage())
                    {
                        if ((DateTime.Now - _lastScreenshotTime).TotalMilliseconds < 800)
                        {
                            handled = true;
                            return IntPtr.Zero;
                        }
                        _lastScreenshotTime = DateTime.Now;

                        var clipboardImage = Clipboard.GetImage();
                        if (clipboardImage != null)
                        {
                            string cacheFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AIO_Cache");
                            if (!Directory.Exists(cacheFolder)) Directory.CreateDirectory(cacheFolder);

                            string fileName = $"snap_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                            string fullPath = Path.Combine(cacheFolder, fileName);

                            using (var fileStream = new FileStream(fullPath, FileMode.Create))
                            {
                                PngBitmapEncoder encoder = new PngBitmapEncoder();
                                encoder.Frames.Add(BitmapFrame.Create(clipboardImage));
                                encoder.Save(fileStream);
                            }

                            if (ScreenshotHistory.Count >= ClipboardLimit)
                            {
                                try { File.Delete(ScreenshotHistory.Last().Path); } catch { }
                                ScreenshotHistory.RemoveAt(ScreenshotHistory.Count - 1);
                            }

                            int width = clipboardImage.PixelWidth;
                            int height = clipboardImage.PixelHeight;
                            int stride = width * ((clipboardImage.Format.BitsPerPixel + 7) / 8);
                            byte[] pixels = new byte[height * stride];
                            clipboardImage.CopyPixels(pixels, stride, 0);

                            var newModel = new ScreenshotModel
                            {
                                Name = $"Capture - {DateTime.Now:HH:mm:ss}",
                                Path = fullPath,
                                Image = clipboardImage,
                                OcrText = string.Empty
                            };

                            ScreenshotHistory.Insert(0, newModel);

                            Task.Run(() =>
                            {
                                try
                                {
                                    StringBuilder sbText = new StringBuilder(8192);
                                    ProcessImageOCR(pixels, width, height, stride, sbText, sbText.Capacity);
                                    string ocrResult = sbText.ToString().Trim();
                                    Application.Current.Dispatcher.BeginInvoke(
                                        new Action(() => newModel.OcrText = ocrResult));
                                }
                                catch { }
                            });
                        }
                    }
                }
                catch (Exception) { }
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void InitializeTrayIcon()
        {
            // Resolves the absolute path of the icon in the deployment folder
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_icon.ico");
            IntPtr hIconHandle = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);

            // Fallback to default application icon if the file is missing
            if (hIconHandle == IntPtr.Zero)
            {
                hIconHandle = LoadIcon(IntPtr.Zero, new IntPtr(32512));
            }

            _nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA)),
                hWnd = _windowHandle,
                uID = 1,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_TRAYICON,
                hIcon = hIconHandle,
                szTip = "AIO Hybrid Clipboard"
            };
            Shell_NotifyIcon(NIM_ADD, ref _nid);
        }

        private void BindGlobalHotkey()
        {
            UnregisterHotKey(_windowHandle, HOTKEY_ID);
            RegisterHotKey(_windowHandle, HOTKEY_ID, CurrentModifier, CurrentKey);

            UnregisterHotKey(_windowHandle, QUICKPASTE_ID_1);
            UnregisterHotKey(_windowHandle, QUICKPASTE_ID_2);
            UnregisterHotKey(_windowHandle, QUICKPASTE_ID_3);
            RegisterHotKey(_windowHandle, QUICKPASTE_ID_1, CurrentModifier, 0x31); // +1
            RegisterHotKey(_windowHandle, QUICKPASTE_ID_2, CurrentModifier, 0x32); // +2
            RegisterHotKey(_windowHandle, QUICKPASTE_ID_3, CurrentModifier, 0x33); // +3
        }

        private void PopulateSettingsUI()
        {
            CmbLanguage.SelectedIndex = _currentLanguage == AppLanguage.Turkish ? 1 : 0;
            UpdateHotkeyDisplay();
        }

        // --- HOTKEY CAPTURE ---
        private bool _isCapturingHotkey = false;

        private void UpdateHotkeyDisplay()
        {
            if (TxtHotkeyCapture == null) return;
            var parts = new List<string>();
            if ((CurrentModifier & 0x0002) != 0) parts.Add("CTRL");
            if ((CurrentModifier & 0x0001) != 0) parts.Add("ALT");
            if ((CurrentModifier & 0x0004) != 0) parts.Add("SHIFT");
            Key k = KeyInterop.KeyFromVirtualKey((int)CurrentKey);
            parts.Add(k.ToString().ToUpperInvariant());
            TxtHotkeyCapture.Text = string.Join(" + ", parts);

            if (TxtQuickPasteHint == null) return;
            string mod = string.Join("+", parts.Take(parts.Count - 1));
            TxtQuickPasteHint.Text = $"Quick paste: {mod}+1 / {mod}+2 / {mod}+3";
        }

        private void TxtHotkeyCapture_GotFocus(object sender, RoutedEventArgs e)
        {
            _isCapturingHotkey = true;
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

            // Ignore lone modifier keys
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

            if (modifier == 0) { e.Handled = true; return; } // Require at least one modifier

            CurrentModifier = modifier;
            CurrentKey = (uint)KeyInterop.VirtualKeyFromKey(key);

            if (_windowHandle != IntPtr.Zero) BindGlobalHotkey();
            SaveSettings();

            _isCapturingHotkey = false;
            UpdateHotkeyDisplay();
            Keyboard.ClearFocus();
            e.Handled = true;
        }

        private void ShowTrayContextMenu()
        {
            SetForegroundWindow(_windowHandle);
            ContextMenu? menu = this.FindResource("DiscordTrayMenu") as ContextMenu;
            if (menu != null)
            {
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                menu.IsOpen = true;
            }
        }

        private void TrayMenu_Open_Click(object sender, RoutedEventArgs e) => ShowLauncher();
        private void TrayMenu_Close_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
        private void ToggleLauncher() { if (this.Visibility == Visibility.Visible) this.Hide(); else ShowLauncher(); }

        private void ShowLauncher()
        {
            this.Show();
            this.Activate();
            TxtSearch.Focus();
        }

        private void CheckRegistryStartup()
        {
            try
            {
                RegistryKey rk = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
                if (rk != null) ChkStartWithWindows.IsChecked = rk.GetValue("AIO_ClipboardSearch") != null;
            }
            catch { }
        }

        private void ChkStartWithWindows_Checked(object sender, RoutedEventArgs e) => ManageStartup(true);
        private void ChkStartWithWindows_Unchecked(object sender, RoutedEventArgs e) => ManageStartup(false);

        private void ManageStartup(bool enable)
        {
            try
            {
                RegistryKey rk = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                if (rk != null)
                {
                    if (enable) rk.SetValue("AIO_ClipboardSearch", Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location);
                    else rk.DeleteValue("AIO_ClipboardSearch", false);
                }
            }
            catch { }
        }

        private void ListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ListBox listBox)
            {
                var scrollViewer = FindVisualChild<ScrollViewer>(listBox);
                if (scrollViewer != null)
                {
                    if (e.Delta > 0) scrollViewer.LineLeft(); else scrollViewer.LineRight();
                    e.Handled = true;
                }
            }
        }

        private T? FindVisualChild<T>(DependencyObject obj) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(obj, i);
                if (child is T t) return t;
                T? childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null) return childOfChild;
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
            if (ChkEdit?.IsChecked == true) return; // Edit mode: ListBox handles multi-selection

            var border = sender as Border;
            if (border != null && border.DataContext != null)
            {
                CopyBackAndHide(border.DataContext.ToString());
                e.Handled = true;
            }
        }

        private void CopyBackAndHide(string? text)
        {
            if (string.IsNullOrEmpty(text) || text.StartsWith("[OCR:")) return;
            try
            {
                RemoveClipboardFormatListener(_windowHandle);
                Clipboard.SetText(text);
                AddClipboardFormatListener(_windowHandle);
            }
            catch { }

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
                RemoveClipboardFormatListener(_windowHandle);
                Clipboard.SetText(text);
                AddClipboardFormatListener(_windowHandle);
                SimulatePaste();
            }
            catch { }
        }

        private static void SimulatePaste()
        {
            const uint INPUT_KEYBOARD = 1;
            const uint KEYEVENTF_KEYUP = 0x0002;
            const ushort VK_CONTROL = 0x11;
            const ushort VK_V = 0x56;
            var inputs = new INPUT[]
            {
                new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = VK_CONTROL } } },
                new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = VK_V } } },
                new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = VK_V,       dwFlags = KEYEVENTF_KEYUP } } },
                new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = VK_CONTROL, dwFlags = KEYEVENTF_KEYUP } } },
            };
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        private void ChkEdit_Checked(object sender, RoutedEventArgs e)
        {
            LstResults.SelectionMode = SelectionMode.Multiple;
            LstScreenshots.SelectionMode = SelectionMode.Multiple;
        }

        private void ChkEdit_Unchecked(object sender, RoutedEventArgs e)
        {
            LstResults.SelectedItems.Clear();
            LstScreenshots.SelectedItems.Clear();
            LstResults.SelectionMode = SelectionMode.Single;
            LstScreenshots.SelectionMode = SelectionMode.Single;
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            var selectedTexts = LstResults.SelectedItems.Cast<string>().ToList();
            foreach (var text in selectedTexts)
                ClipboardHistory.Remove(text);

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

        private void BtnSettings_Click(object sender, RoutedEventArgs e) => PnlSettingsDrawer.Visibility = PnlSettingsDrawer.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

        // --- SESSION PERSISTENCE ---
        private static string SessionFilePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AIO_Cache", "session.json");

        private void SaveSession()
        {
            try
            {
                string cacheFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AIO_Cache");
                if (!Directory.Exists(cacheFolder)) Directory.CreateDirectory(cacheFolder);

                var data = new
                {
                    ClipboardEntries = ClipboardHistory.ToList(),
                    Screenshots = ScreenshotHistory.Select(s => new { s.Name, s.Path, s.OcrText }).ToList()
                };
                File.WriteAllText(SessionFilePath, JsonSerializer.Serialize(data));
            }
            catch { }
        }

        private void LoadSession()
        {
            try
            {
                if (!File.Exists(SessionFilePath)) return;
                using var doc = JsonDocument.Parse(File.ReadAllText(SessionFilePath));
                var root = doc.RootElement;

                if (root.TryGetProperty("ClipboardEntries", out var entries))
                {
                    foreach (var entry in entries.EnumerateArray())
                    {
                        string? text = entry.GetString();
                        if (!string.IsNullOrEmpty(text))
                            ClipboardHistory.Add(text);
                    }
                }

                if (root.TryGetProperty("Screenshots", out var screenshots))
                {
                    foreach (var s in screenshots.EnumerateArray())
                    {
                        string name    = s.TryGetProperty("Name",    out var n) ? n.GetString() ?? "" : "";
                        string path    = s.TryGetProperty("Path",    out var p) ? p.GetString() ?? "" : "";
                        string ocrText = s.TryGetProperty("OcrText", out var o) ? o.GetString() ?? "" : "";

                        if (!File.Exists(path)) continue;

                        BitmapImage img;
                        try
                        {
                            img = new BitmapImage();
                            img.BeginInit();
                            img.CacheOption = BitmapCacheOption.OnLoad;
                            img.UriSource = new Uri(path, UriKind.Absolute);
                            img.EndInit();
                            img.Freeze();
                        }
                        catch { continue; }

                        ScreenshotHistory.Add(new ScreenshotModel
                        {
                            Name = name,
                            Path = path,
                            Image = img,
                            OcrText = ocrText
                        });
                    }
                }
            }
            catch { }
        }

        protected override void OnClosed(EventArgs e)
        {
            SaveSession();
            Shell_NotifyIcon(NIM_DELETE, ref _nid);
            UnregisterHotKey(_windowHandle, HOTKEY_ID);
            UnregisterHotKey(_windowHandle, QUICKPASTE_ID_1);
            UnregisterHotKey(_windowHandle, QUICKPASTE_ID_2);
            UnregisterHotKey(_windowHandle, QUICKPASTE_ID_3);
            RemoveClipboardFormatListener(_windowHandle);
            _hwndSource?.RemoveHook(HwndHook);
            if (_nid.hIcon != IntPtr.Zero) DestroyIcon(_nid.hIcon);
            base.OnClosed(e);
        }

        public class ScreenshotModel
        {
            public string Name { get; set; } = string.Empty;
            public string Path { get; set; } = string.Empty;
            public BitmapSource? Image { get; set; }
            public string OcrText { get; set; } = string.Empty;
        }
    }
}