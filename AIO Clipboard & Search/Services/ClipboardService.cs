using AIO_Hybrid_Clipboard.Models;
using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static AIO_Hybrid_Clipboard.Services.Win32Api;

namespace AIO_Hybrid_Clipboard.Services
{
    /// <summary>
    /// Ownership wrapper around an <see cref="ArrayPool{T}"/> rented pixel buffer so large
    /// screenshot captures never accumulate on the Large Object Heap (LOH).
    /// </summary>
    internal sealed class PooledPixelBuffer : IDisposable
    {
        private byte[]? _buffer;

        public PooledPixelBuffer(byte[] buffer, int length, int width, int height, int stride)
        {
            _buffer = buffer;
            Length  = length;
            Width   = width;
            Height  = height;
            Stride  = stride;
        }

        public byte[] Buffer => _buffer ?? throw new ObjectDisposedException(nameof(PooledPixelBuffer));
        public int    Length { get; }
        public int    Width  { get; }
        public int    Height { get; }
        public int    Stride { get; }

        public void Dispose()
        {
            byte[]? buf = Interlocked.Exchange(ref _buffer, null);
            if (buf != null)
                ArrayPool<byte>.Shared.Return(buf);
        }
    }

    internal sealed class ClipboardService : IDisposable
    {
        // Guard window against the same image clipboard event firing multiple times.
        private const int ImageDebounceMs      = 800;
        internal const int ThumbnailDecodeWidth = 320;
        internal const int PreviewDecodeWidth   = 640;

        private static readonly int[] RetryDelaysMs = { 25, 50, 90, 140, 200 };

        private readonly IntPtr _hwnd;
        private DateTime _lastScreenshotTime = DateTime.MinValue;
        private uint     _lastSequenceNumber;
        private bool     _isListening;
        private System.Collections.Generic.HashSet<string> _excludedApps = new(StringComparer.OrdinalIgnoreCase);

        public bool IsPaused { get; set; }

        public event Action<string, string>? TextCaptured;
        public event Action<ScreenshotModel, PooledPixelBuffer>? ImageCaptured;

        public ClipboardService(IntPtr hwnd) => _hwnd = hwnd;

        public void SetExcludedApps(string? rawList) =>
            _excludedApps = ClipboardPrivacyGuard.ParseExcludedApps(rawList);

        public void Start()
        {
            if (_isListening || _hwnd == IntPtr.Zero) return;
            _lastSequenceNumber = GetClipboardSequenceNumber();
            _isListening = AddClipboardFormatListener(_hwnd);
        }

        public void Stop()
        {
            if (!_isListening || _hwnd == IntPtr.Zero) return;
            RemoveClipboardFormatListener(_hwnd);
            _isListening = false;
        }

        /// <summary>
        /// Re-establishes the Win32 clipboard format listener after system sleep/resume
        /// so 24/7 background monitoring never silently detaches.
        /// </summary>
        public void EnsureListening()
        {
            if (_hwnd == IntPtr.Zero) return;
            if (_isListening) RemoveClipboardFormatListener(_hwnd);
            _lastSequenceNumber = GetClipboardSequenceNumber();
            _isListening = AddClipboardFormatListener(_hwnd);
        }

        /// <summary>
        /// Puts text on the clipboard without recording it as a new history entry and without
        /// blocking the UI thread when another process temporarily holds the clipboard lock.
        /// </summary>
        public async Task<bool> SetClipboardSilentAsync(string text, CancellationToken ct = default)
        {
            bool wasListening = _isListening;
            Stop();
            try
            {
                await RetryClipboardAsync(() => { Clipboard.SetText(text); return true; }, ct);
                _lastSequenceNumber = GetClipboardSequenceNumber();
                return true;
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex)
            {
                Log.Warn($"SetClipboard failed: {ex.Message}");
                return false;
            }
            finally
            {
                if (wasListening) Start(); // never leave the listener detached
            }
        }

        /// <summary>
        /// Loads a full-resolution cached PNG from disk on demand, puts it on the clipboard
        /// asynchronously without blocking the UI thread, and immediately lets the full-res
        /// bitmap become eligible for GC.
        /// </summary>
        public async Task<bool> SetImageSilentAsync(string imagePath, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                return false;

            BitmapImage fullImage = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource   = new Uri(imagePath, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }, ct);

            bool wasListening = _isListening;
            Stop();
            try
            {
                await RetryClipboardAsync(() => { Clipboard.SetImage(fullImage); return true; }, ct);
                _lastSequenceNumber = GetClipboardSequenceNumber();
                return true;
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex)
            {
                Log.Warn($"SetImage failed: {ex.Message}");
                return false;
            }
            finally
            {
                if (wasListening) Start();
            }
        }

        /// <summary>Loads a low-memory frozen thumbnail (max 320px wide) from disk.</summary>
        internal static BitmapImage LoadThumbnail(string filePath, int originalWidth = 0)
            => LoadDecodedBitmap(filePath, originalWidth, ThumbnailDecodeWidth);

        /// <summary>Loads a crisp transient frozen preview (max 640px wide) for hover tooltips.</summary>
        internal static BitmapImage? LoadHoverPreview(string filePath, int originalWidth = 0)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return null;
            try
            {
                return LoadDecodedBitmap(filePath, originalWidth, PreviewDecodeWidth);
            }
            catch (Exception ex)
            {
                Log.Warn($"Hover preview load failed: {ex.Message}");
                return null;
            }
        }

        private static BitmapImage LoadDecodedBitmap(string filePath, int originalWidth, int maxDecodeWidth)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource   = new Uri(filePath, UriKind.Absolute);
            if (originalWidth <= 0 || originalWidth > maxDecodeWidth)
                bmp.DecodePixelWidth = maxDecodeWidth;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        public async void HandleClipboardUpdate()
        {
            if (IsPaused) return;

            uint seq = GetClipboardSequenceNumber();
            if (seq != 0 && seq == _lastSequenceNumber) return;
            _lastSequenceNumber = seq;

            PooledPixelBuffer? pooledPixels = null;
            bool ownershipTransferred = false;

            try
            {
                if (ClipboardPrivacyGuard.ShouldIgnoreCurrentClipboard(_hwnd))
                {
                    Log.Info("Ignored sensitive clipboard payload (privacy guard matched).");
                    return;
                }

                string sourceApp = ClipboardPrivacyGuard.GetClipboardSourceAppName();
                if (ClipboardPrivacyGuard.IsProcessExcluded(sourceApp, _excludedApps))
                {
                    Log.Info($"Ignored clipboard payload from excluded application: {sourceApp}");
                    return;
                }

                // Non-blocking async retry on CLIPBRD_E_CANT_OPEN (COMException).
                bool hasText = await RetryClipboardAsync(Clipboard.ContainsText);
                if (hasText)
                {
                    string text = await RetryClipboardAsync(Clipboard.GetText);
                    if (string.IsNullOrWhiteSpace(text) || OcrService.IsPlaceholder(text))
                        return;

                    // Avoid duplicating huge (>1 MB) strings when there is no edge whitespace.
                    string clean = (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1]))
                        ? text.Trim()
                        : text;

                    if (clean.Length > 0)
                        TextCaptured?.Invoke(clean, sourceApp);
                    return;
                }

                bool hasImage = await RetryClipboardAsync(Clipboard.ContainsImage);
                if (!hasImage) return;

                if ((DateTime.Now - _lastScreenshotTime).TotalMilliseconds < ImageDebounceMs)
                    return;
                _lastScreenshotTime = DateTime.Now;

                BitmapSource? img = await RetryClipboardAsync(Clipboard.GetImage);
                if (img == null || img.PixelWidth <= 0 || img.PixelHeight <= 0)
                    return;

                // Normalize to premultiplied BGRA8 (Pbgra32) for native WinRT OCR compatibility.
                BitmapSource normalized = img.Format == PixelFormats.Pbgra32
                    ? img
                    : new FormatConvertedBitmap(img, PixelFormats.Pbgra32, null, 0);

                int width  = normalized.PixelWidth;
                int height = normalized.PixelHeight;
                int stride = width * 4;
                long totalBytes = (long)height * stride;
                if (totalBytes <= 0 || totalBytes > int.MaxValue)
                    return;

                int byteCount = (int)totalBytes;
                byte[] rented = ArrayPool<byte>.Shared.Rent(byteCount);
                pooledPixels = new PooledPixelBuffer(rented, byteCount, width, height, stride);
                normalized.CopyPixels(rented, stride, 0);

                // Rebuild a frozen bitmap from copied bytes so encoder thread never touches COM objects.
                BitmapSource frozenSource = BitmapSource.Create(
                    width, height,
                    img.DpiX > 0 ? img.DpiX : 96.0,
                    img.DpiY > 0 ? img.DpiY : 96.0,
                    PixelFormats.Pbgra32, null, rented, stride);
                frozenSource.Freeze();

                var capturedAt = DateTime.Now;
                string suffix   = Guid.NewGuid().ToString("N")[..6];
                string fullPath = Path.Combine(
                    AppPaths.CacheFolder,
                    $"snap_{capturedAt:yyyyMMdd_HHmmss_fff}_{suffix}.png");

                var (contentHash, thumbnail) = await Task.Run(() =>
                {
                    AppPaths.EnsureCacheFolder();

                    string hash = ComputeImageHash(rented.AsSpan(0, byteCount), width, height);

                    using (var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(frozenSource));
                        encoder.Save(fs);
                    }

                    BitmapImage thumb = LoadThumbnail(fullPath, width);
                    return (hash, thumb);
                });

                // frozenSource is now out of scope and eligible for immediate collection;
                // only the 320px frozen thumbnail stays referenced by ScreenshotModel.
                var model = new ScreenshotModel
                {
                    Name        = $"Capture - {capturedAt:HH:mm:ss}",
                    Path        = fullPath,
                    ContentHash = contentHash,
                    SourceApp   = sourceApp,
                    PixelWidth  = width,
                    PixelHeight = height,
                    CapturedAt  = capturedAt,
                    Image       = thumbnail,
                    OcrText     = string.Empty
                };

                if (ImageCaptured != null)
                {
                    ownershipTransferred = true;
                    ImageCaptured.Invoke(model, pooledPixels);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Clipboard update handling failed", ex);
            }
            finally
            {
                if (!ownershipTransferred)
                    pooledPixels?.Dispose();
            }
        }

        internal static string ComputeImageHash(ReadOnlySpan<byte> pixels, int width, int height)
        {
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Span<byte> dims = stackalloc byte[8];
            BitConverter.TryWriteBytes(dims[..4], width);
            BitConverter.TryWriteBytes(dims[4..], height);
            sha.AppendData(dims);
            sha.AppendData(pixels);
            return Convert.ToHexString(sha.GetHashAndReset());
        }

        private static async Task<T> RetryClipboardAsync<T>(Func<T> action, CancellationToken ct = default)
        {
            for (int attempt = 0; ; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    return action();
                }
                catch (COMException) when (attempt < RetryDelaysMs.Length)
                {
                    await Task.Delay(RetryDelaysMs[attempt], ct);
                }
            }
        }

        public void Dispose() => Stop();
    }
}
