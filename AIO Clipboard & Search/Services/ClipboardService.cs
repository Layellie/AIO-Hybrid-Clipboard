using AIO_Hybrid_Clipboard.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using static AIO_Hybrid_Clipboard.Services.Win32Api;

namespace AIO_Hybrid_Clipboard.Services
{
    internal sealed class ClipboardService : IDisposable
    {
        private readonly IntPtr _hwnd;
        private DateTime _lastScreenshotTime = DateTime.MinValue;

        public event Action<string>? TextCaptured;
        public event Action<ScreenshotModel, byte[], int, int, int>? ImageCaptured;

        public ClipboardService(IntPtr hwnd) => _hwnd = hwnd;

        public void Start() => AddClipboardFormatListener(_hwnd);
        public void Stop()  => RemoveClipboardFormatListener(_hwnd);

        public void SetClipboardSilent(string text)
        {
            Stop();
            Clipboard.SetText(text);
            Start();
        }

        public void HandleClipboardUpdate()
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    string text = Clipboard.GetText().Trim();
                    if (!string.IsNullOrEmpty(text) && !text.StartsWith("[OCR:"))
                        TextCaptured?.Invoke(text);
                }
                else if (Clipboard.ContainsImage())
                {
                    if ((DateTime.Now - _lastScreenshotTime).TotalMilliseconds < 800) return;
                    _lastScreenshotTime = DateTime.Now;

                    var img = Clipboard.GetImage();
                    if (img == null) return;

                    string cacheFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AIO_Cache");
                    if (!Directory.Exists(cacheFolder)) Directory.CreateDirectory(cacheFolder);

                    string fileName = $"snap_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                    string fullPath = Path.Combine(cacheFolder, fileName);

                    using (var fs = new FileStream(fullPath, FileMode.Create))
                    {
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(img));
                        encoder.Save(fs);
                    }

                    int width  = img.PixelWidth;
                    int height = img.PixelHeight;
                    int stride = width * ((img.Format.BitsPerPixel + 7) / 8);
                    byte[] pixels = new byte[height * stride];
                    img.CopyPixels(pixels, stride, 0);

                    var model = new ScreenshotModel
                    {
                        Name    = $"Capture - {DateTime.Now:HH:mm:ss}",
                        Path    = fullPath,
                        Image   = img,
                        OcrText = string.Empty
                    };

                    ImageCaptured?.Invoke(model, pixels, width, height, stride);
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[AIO] ClipboardUpdate error: {ex.Message}"); }
        }

        public void Dispose() => Stop();
    }
}
