using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AIO_Hybrid_Clipboard.Services
{
    internal static class OcrService
    {
        /// <summary>Status codes returned by the native engine. Keep in sync with dllmain.cpp.</summary>
        internal enum OcrStatus
        {
            Success           = 0,
            NoText            = 1,
            EngineUnavailable = -1,
            ProcessingError   = -2,
            InvalidArgument   = -3,
        }

        internal const string AutoLanguageTag = "auto";

        // Legacy marker prefix: older engine versions reported status as bracketed
        // strings ("[OCR: ...]"); sessions saved by them may still contain these.
        private const string PlaceholderPrefix = "[OCR";

        // 64K UTF-16 chars (~128 KB rented from ArrayPool) prevents truncation on dense screenshots.
        private const int OutputBufferChars = 65536;

        // Guard against runaway allocations on malformed image files.
        private const int MaxOcrDimension = 16384;

        // Bound concurrent WinRT OCR executions to keep memory and CPU usage predictable.
        private static readonly SemaphoreSlim ConcurrencyGate = new(2, 2);

        [DllImport("AIO_SearchEngine.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int ProcessImageOCRForLanguage(
            byte[] pixelData,
            int width,
            int height,
            int stride,
            string? languageTag,
            [Out] char[] outText,
            int maxLen);

        [DllImport("AIO_SearchEngine.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int GetAvailableOcrLanguages(
            [Out] char[] outText,
            int maxLen);

        [DllImport("AIO_SearchEngine.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int IsOcrEngineAvailable();

        /// <summary>True when the text is a legacy engine status message, not recognized text.</summary>
        public static bool IsPlaceholder(string text) =>
            !string.IsNullOrEmpty(text) && text.StartsWith(PlaceholderPrefix, StringComparison.Ordinal);

        /// <summary>Probes whether the native DLL is loadable and a Windows OCR engine is installed.</summary>
        public static bool IsEngineAvailable()
        {
            try
            {
                return IsOcrEngineAvailable() == 1;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                Log.Warn($"Native OCR engine probe failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Returns available OCR language tags starting with <c>"auto"</c> followed by every
        /// Windows OCR language pack installed on this machine (e.g. <c>"en-US"</c>, <c>"tr-TR"</c>).
        /// </summary>
        public static IReadOnlyList<string> GetAvailableLanguages()
        {
            var result = new List<string> { AutoLanguageTag };
            try
            {
                char[] buf = new char[2048];
                int count = GetAvailableOcrLanguages(buf, buf.Length);
                if (count > 0)
                {
                    int len = Array.IndexOf(buf, '\0');
                    string raw = len >= 0 ? new string(buf, 0, len) : new string(buf);
                    foreach (string tag in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
                    {
                        string clean = tag.Trim();
                        if (clean.Length > 0 && !result.Contains(clean, StringComparer.OrdinalIgnoreCase))
                            result.Add(clean);
                    }
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                Log.Warn($"GetAvailableOcrLanguages failed: {ex.Message}");
            }
            return result;
        }

        /// <summary>
        /// Runs OCR on raw premultiplied BGRA8 pixels on a low-priority background thread.
        /// Returns the recognized text, or an empty string when there is none.
        /// </summary>
        public static async Task<string> RecognizeAsync(
            byte[] pixels,
            int width,
            int height,
            int stride,
            CancellationToken ct = default,
            string? languageTag = null)
        {
            var (status, text) = await RecognizeWithStatusAsync(pixels, width, height, stride, ct, languageTag);
            if (status != OcrStatus.Success && status != OcrStatus.NoText)
                Log.Warn($"Native OCR failed with status {status}");
            return text;
        }

        /// <summary>
        /// Decodes a full-resolution cached image from disk as Pbgra32 into a pooled byte buffer
        /// (avoiding LOH churn) and runs OCR on it, returning the buffer immediately when done.
        /// </summary>
        public static async Task<(OcrStatus Status, string Text)> RecognizeImageFileAsync(
            string imagePath,
            CancellationToken ct = default,
            string? languageTag = null)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                return (OcrStatus.InvalidArgument, string.Empty);

            byte[]? rentedPixels = null;
            int width = 0, height = 0, stride = 0;

            try
            {
                await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();
                    using var fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    if (decoder.Frames.Count == 0)
                        return;

                    BitmapSource frame = decoder.Frames[0];
                    if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0 ||
                        frame.PixelWidth > MaxOcrDimension || frame.PixelHeight > MaxOcrDimension)
                        return;

                    if (frame.Format != PixelFormats.Pbgra32)
                        frame = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);

                    width  = frame.PixelWidth;
                    height = frame.PixelHeight;
                    stride = width * 4;
                    long totalBytes = (long)height * stride;
                    if (totalBytes <= 0 || totalBytes > int.MaxValue)
                        return;

                    rentedPixels = ArrayPool<byte>.Shared.Rent((int)totalBytes);
                    frame.CopyPixels(rentedPixels, stride, 0);
                }, ct);

                if (rentedPixels == null || width <= 0 || height <= 0)
                    return (OcrStatus.InvalidArgument, string.Empty);

                return await RecognizeWithStatusAsync(rentedPixels, width, height, stride, ct, languageTag);
            }
            finally
            {
                if (rentedPixels != null)
                    ArrayPool<byte>.Shared.Return(rentedPixels);
            }
        }

        /// <summary>Same as <see cref="RecognizeAsync"/>, but surfaces the native status code.</summary>
        internal static async Task<(OcrStatus Status, string Text)> RecognizeWithStatusAsync(
            byte[] pixels,
            int width,
            int height,
            int stride,
            CancellationToken ct = default,
            string? languageTag = null)
        {
            if (pixels == null || width <= 0 || height <= 0 ||
                width > MaxOcrDimension || height > MaxOcrDimension ||
                stride < width * 4 || (long)height * stride > pixels.Length)
            {
                return (OcrStatus.InvalidArgument, string.Empty);
            }

            string? normalizedTag = string.IsNullOrWhiteSpace(languageTag) ||
                                    string.Equals(languageTag, AutoLanguageTag, StringComparison.OrdinalIgnoreCase)
                ? null
                : languageTag.Trim();

            await ConcurrencyGate.WaitAsync(ct);
            try
            {
                return await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();

                    Thread current = Thread.CurrentThread;
                    ThreadPriority prevPriority = current.Priority;
                    char[] buffer = ArrayPool<char>.Shared.Rent(OutputBufferChars);
                    try
                    {
                        try { current.Priority = ThreadPriority.BelowNormal; }
                        catch { /* best-effort priority hint */ }

                        buffer[0] = '\0';
                        var status = (OcrStatus)ProcessImageOCRForLanguage(
                            pixels, width, height, stride, normalizedTag, buffer, OutputBufferChars);
                        if (status != OcrStatus.Success)
                            return (status, string.Empty);

                        int len = Array.IndexOf(buffer, '\0', 0, OutputBufferChars);
                        if (len < 0) len = OutputBufferChars;

                        return (status, new string(buffer, 0, len).Trim());
                    }
                    catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
                    {
                        Log.Error("Native OCR DLL could not be invoked", ex);
                        return (OcrStatus.EngineUnavailable, string.Empty);
                    }
                    finally
                    {
                        ArrayPool<char>.Shared.Return(buffer);
                        try { current.Priority = prevPriority; }
                        catch { /* best-effort restore */ }
                    }
                }, ct);
            }
            finally
            {
                ConcurrencyGate.Release();
            }
        }
    }
}
