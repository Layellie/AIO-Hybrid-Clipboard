using AIO_Hybrid_Clipboard.Models;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using static AIO_Hybrid_Clipboard.Services.Win32Api;

namespace AIO_Hybrid_Clipboard.Services
{
    internal sealed class ClipboardService : IDisposable
    {
        // Guard window against the same image clipboard event firing multiple times.
        private const int ImageDebounceMs = 800;

        private readonly IntPtr _hwnd;
        private DateTime _lastScreenshotTime = DateTime.MinValue;

        public event Action<string>? TextCaptured;
        public event Action<ScreenshotModel, byte[], int, int, int>? ImageCaptured;

        public ClipboardService(IntPtr hwnd) => _hwnd = hwnd;

        public void Start() => AddClipboardFormatListener(_hwnd);
        public void Stop()  => RemoveClipboardFormatListener(_hwnd);

        /// <summary>Puts text on the clipboard without recording it as a new history entry.</summary>
        public void SetClipboardSilent(string text)
        {
            Stop();
            try { Clipboard.SetText(text); }
            finally { Start(); } // never leave the listener detached, even if SetText throws
        }

        public async void HandleClipboardUpdate()
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    string text = Clipboard.GetText().Trim();
                    if (!string.IsNullOrEmpty(text) && !OcrService.IsPlaceholder(text))
                        TextCaptured?.Invoke(text);
                }
                else if (Clipboard.ContainsImage())
                {
                    if ((DateTime.Now - _lastScreenshotTime).TotalMilliseconds < ImageDebounceMs) return;
                    _lastScreenshotTime = DateTime.Now;

                    var img = Clipboard.GetImage();
                    if (img == null) return;

                    // Grab the raw pixels on the UI thread (a cheap copy), then push the
                    // expensive PNG encode + disk write to the thread pool so the UI
                    // never stalls on large captures.
                    int width  = img.PixelWidth;
                    int height = img.PixelHeight;
                    int stride = width * ((img.Format.BitsPerPixel + 7) / 8);
                    byte[] pixels = new byte[height * stride];
                    img.CopyPixels(pixels, stride, 0);

                    BitmapSource source = img;
                    if (source.CanFreeze)
                    {
                        source.Freeze();
                    }
                    else
                    {
                        // Rebuild from the pixels we just copied so the bitmap can be
                        // frozen and safely touched from the encoder thread.
                        source = BitmapSource.Create(width, height, img.DpiX, img.DpiY,
                                                     img.Format, img.Palette, pixels, stride);
                        source.Freeze();
                    }

                    var capturedAt  = DateTime.Now;
                    string fullPath = Path.Combine(AppPaths.CacheFolder, $"snap_{capturedAt:yyyyMMdd_HHmmss}.png");

                    await Task.Run(() =>
                    {
                        AppPaths.EnsureCacheFolder();
                        using var fs = new FileStream(fullPath, FileMode.Create);
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(source));
                        encoder.Save(fs);
                    });

                    var model = new ScreenshotModel
                    {
                        Name    = $"Capture - {capturedAt:HH:mm:ss}",
                        Path    = fullPath,
                        Image   = source,
                        OcrText = string.Empty
                    };

                    ImageCaptured?.Invoke(model, pixels, width, height, stride);
                }
            }
            catch (Exception ex) { Log.Error("Clipboard update handling failed", ex); }
        }

        public void Dispose() => Stop();
    }
}
