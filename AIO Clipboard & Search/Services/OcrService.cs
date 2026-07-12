using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace AIO_Hybrid_Clipboard.Services
{
    internal static class OcrService
    {
        // The native engine reports "no text found" / errors as bracketed marker
        // strings ("[OCR: ...]", "[OCR Error: ...]") instead of real OCR output.
        private const string PlaceholderPrefix = "[OCR";

        [DllImport("AIO_SearchEngine.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern void ProcessImageOCR(byte[] pixelData, int width, int height, int stride, StringBuilder outText, int maxLen);

        /// <summary>True when the result is an engine status message, not recognized text.</summary>
        public static bool IsPlaceholder(string text) =>
            text.StartsWith(PlaceholderPrefix, StringComparison.Ordinal);

        public static Task<string> RecognizeAsync(byte[] pixels, int width, int height, int stride)
        {
            return Task.Run(() =>
            {
                var sb = new StringBuilder(8192);
                ProcessImageOCR(pixels, width, height, stride, sb, sb.Capacity);
                return sb.ToString().Trim();
            });
        }
    }
}
