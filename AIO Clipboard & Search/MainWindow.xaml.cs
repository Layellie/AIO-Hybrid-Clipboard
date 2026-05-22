using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
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
        public static extern void ProcessImageOCR(byte[] pixelData, int width, int height, int stride, StringBuilder outText, int maxLen);

        // --- WIN32 SYSTEM TRAY API ---
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct NOTIFYICONDATA
        {
            public int cbSize; public IntPtr hWnd; public int uID; public int uFlags;
            public int uCallbackMessage; public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Auto)] public static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        private const uint IMAGE_ICON = 1;
        private const uint LR_LOADFROMFILE = 0x00000010;
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);

        // --- GLOBAL HOTKEY & CLIPBOARD API ---
        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool AddClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        private const int NIM_ADD = 0x00000000; private const int NIM_DELETE = 0x00000002;
        private const int NIF_MESSAGE = 0x00000001; private const int NIF_ICON = 0x00000002; private const int NIF_TIP = 0x00000004;
        private const int WM_TRAYICON = 0x0400 + 100; private const int WM_LBUTTONUP = 0x0202; private const int WM_RBUTTONUP = 0x0205;
        private static readonly IntPtr IDI_APPLICATION = new IntPtr(32512);
        private const int WM_HOTKEY = 0x0312; private const int WM_CLIPBOARDUPDATE = 0x031D; private const int HOTKEY_ID = 9000;

        // --- DATA COLLECTIONS ---
        private List<string> ClipboardHistory = new List<string>();
        private ObservableCollection<ScreenshotModel> ScreenshotHistory = new ObservableCollection<ScreenshotModel>();

        private uint CurrentModifier = 0x0001; private uint CurrentKey = 0x20; int ClipboardLimit = 15;
        private IntPtr _windowHandle;
        private DateTime _lastScreenshotTime = DateTime.MinValue;
        private NOTIFYICONDATA _nid;
        private Point _dragStartPoint;

        public MainWindow()
        {
            InitializeComponent();
            this.PreviewKeyDown += MainWindow_PreviewKeyDown;
            PopulateSettingsUI();
            CheckRegistryStartup();

            ClipboardHistory.Add("AIO OCR Clipboard Active! Capture images to process... 🚀");
            TxtSearch.TextChanged += TxtSearch_TextChanged;
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

            HwndSource source = HwndSource.FromHwnd(_windowHandle);
            source.AddHook(HwndHook);

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

                            LstResults.ItemsSource = null;
                            LstResults.ItemsSource = ClipboardHistory;

                            Clipboard.SetText(ocrResult);
                        }
                    }));
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() => {
                        MessageBox.Show("OCR Processing Exception: " + ex.Message, "OCR Error");
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
            else if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                ToggleLauncher();
                handled = true;
            }
            else if (msg == WM_CLIPBOARDUPDATE)
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        string yeniMetin = Clipboard.GetText().Trim();
                        if (!string.IsNullOrEmpty(yeniMetin) && !yeniMetin.StartsWith("[OCR:"))
                        {
                            if (ClipboardHistory.Contains(yeniMetin)) ClipboardHistory.Remove(yeniMetin);
                            ClipboardHistory.Insert(0, yeniMetin);
                            while (ClipboardHistory.Count > ClipboardLimit) ClipboardHistory.RemoveAt(ClipboardHistory.Count - 1);

                            if (string.IsNullOrEmpty(TxtSearch.Text))
                            {
                                LstResults.ItemsSource = null;
                                LstResults.ItemsSource = ClipboardHistory;
                            }
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

                        var resim = Clipboard.GetImage();
                        if (resim != null)
                        {
                            string cacheFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AIO_Cache");
                            if (!Directory.Exists(cacheFolder)) Directory.CreateDirectory(cacheFolder);

                            string dosyaAdi = $"snap_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                            string tamYol = Path.Combine(cacheFolder, dosyaAdi);

                            using (var fileStream = new FileStream(tamYol, FileMode.Create))
                            {
                                PngBitmapEncoder encoder = new PngBitmapEncoder();
                                encoder.Frames.Add(BitmapFrame.Create(resim));
                                encoder.Save(fileStream);
                            }

                            if (ScreenshotHistory.Count >= ClipboardLimit)
                            {
                                try { File.Delete(ScreenshotHistory.Last().Path); } catch { }
                                ScreenshotHistory.RemoveAt(ScreenshotHistory.Count - 1);
                            }

                            int width = resim.PixelWidth;
                            int height = resim.PixelHeight;
                            int stride = width * ((resim.Format.BitsPerPixel + 7) / 8);
                            byte[] pixels = new byte[height * stride];
                            resim.CopyPixels(pixels, stride, 0);

                            var newModel = new ScreenshotModel
                            {
                                Name = $"Capture - {DateTime.Now:HH:mm:ss}",
                                Path = tamYol,
                                Image = resim,
                                OcrText = string.Empty
                            };

                            ScreenshotHistory.Insert(0, newModel);

                            Task.Run(() =>
                            {
                                try
                                {
                                    StringBuilder sbText = new StringBuilder(8192);
                                    ProcessImageOCR(pixels, width, height, stride, sbText, sbText.Capacity);
                                    newModel.OcrText = sbText.ToString().Trim();
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
        }

        private void PopulateSettingsUI()
        {
            CmbModifier.Items.Add(new KeyValueProxy("ALT", 0x0001));
            CmbModifier.Items.Add(new KeyValueProxy("CTRL", 0x0002));
            CmbModifier.Items.Add(new KeyValueProxy("SHIFT", 0x0004));
            CmbModifier.SelectedIndex = 0;

            CmbKey.Items.Add(new KeyValueProxy("SPACE", 0x20));
            CmbKey.Items.Add(new KeyValueProxy("ENTER", 0x0D));
            CmbKey.Items.Add(new KeyValueProxy("F4", 0x73));
            CmbKey.Items.Add(new KeyValueProxy("TAB", 0x09));
            CmbKey.SelectedIndex = 0;
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

        private void ManageStartup(bool ekle)
        {
            try
            {
                RegistryKey rk = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                if (rk != null)
                {
                    if (ekle) rk.SetValue("AIO_ClipboardSearch", Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location);
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
                GeriKopyalaVeGizle(LstResults.SelectedItem?.ToString());
                e.Handled = true;
            }
        }

        private void ResultItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var border = sender as Border;
            if (border != null && border.DataContext != null)
            {
                GeriKopyalaVeGizle(border.DataContext.ToString());
                e.Handled = true;
            }
        }

        private void GeriKopyalaVeGizle(string? metin)
        {
            if (string.IsNullOrEmpty(metin) || metin.StartsWith("[OCR:")) return;
            try
            {
                RemoveClipboardFormatListener(_windowHandle);
                Clipboard.SetText(metin);
                AddClipboardFormatListener(_windowHandle);
            }
            catch { }

            if (ChkHideOnCopy.IsChecked == true) this.Hide();
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e) => PnlSettingsDrawer.Visibility = PnlSettingsDrawer.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

        private void HotkeyChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbModifier?.SelectedItem is KeyValueProxy mod && CmbKey?.SelectedItem is KeyValueProxy key)
            {
                CurrentModifier = mod.Value; CurrentKey = key.Value;
                if (_windowHandle != IntPtr.Zero) BindGlobalHotkey();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            Shell_NotifyIcon(NIM_DELETE, ref _nid);
            UnregisterHotKey(_windowHandle, HOTKEY_ID);
            RemoveClipboardFormatListener(_windowHandle);
            base.OnClosed(e);
        }

        public class KeyValueProxy
        {
            public string Name { get; set; }
            public uint Value { get; set; }
            public KeyValueProxy(string name, uint value) { Name = name; Value = value; }
            public override string ToString() => Name;
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