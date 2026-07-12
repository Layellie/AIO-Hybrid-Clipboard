using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

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

        // Legacy marker prefix: older engine versions reported status as bracketed
        // strings ("[OCR: ...]"); sessions saved by them may still contain these.
        private const string PlaceholderPrefix = "[OCR";

        [DllImport("AIO_SearchEngine.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int ProcessImageOCR(byte[] pixelData, int width, int height, int stride, StringBuilder outText, int maxLen);

        /// <summary>True when the text is a legacy engine status message, not recognized text.</summary>
        public static bool IsPlaceholder(string text) =>
            text.StartsWith(PlaceholderPrefix, StringComparison.Ordinal);

        /// <summary>
        /// Runs OCR on raw BGRA8 pixels on a background thread.
        /// Returns the recognized text, or an empty string when there is none.
        /// </summary>
        public static async Task<string> RecognizeAsync(byte[] pixels, int width, int height, int stride)
        {
            var (status, text) = await RecognizeWithStatusAsync(pixels, width, height, stride);
            if (status != OcrStatus.Success && status != OcrStatus.NoText)
                Log.Warn($"Native OCR failed with status {status}");
            return text;
        }

        /// <summary>Same as <see cref="RecognizeAsync"/>, but surfaces the engine status.</summary>
        internal static Task<(OcrStatus Status, string Text)> RecognizeWithStatusAsync(byte[] pixels, int width, int height, int stride)
        {
            return Task.Run(() =>
            {
                var sb = new StringBuilder(8192);
                var status = (OcrStatus)ProcessImageOCR(pixels, width, height, stride, sb, sb.Capacity);
                return (status, status == OcrStatus.Success ? sb.ToString().Trim() : string.Empty);
            });
        }
    }
}
